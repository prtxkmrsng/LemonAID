using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using LemonAID.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.ApplicationModel.DataTransfer;
using Windows.ApplicationModel.DataTransfer.DragDrop;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;
using WinRT.Interop;

namespace LemonAID;

public sealed partial class MainPage : Page
{
    public static IReadOnlyList<string> CurrentLoadedFiles => _sharedLoadedFiles;
    public static string? CurrentCustomOutputFolder { get; private set; }
    private static readonly List<string> _sharedLoadedFiles = [];

    private WheelMode _currentMode = WheelMode.Converter;
    private readonly List<string> _loadedFiles = [];
    private IReadOnlyList<SectorItem> _currentSectors = [];
    private int _selectedSectorIndex = -1;
    private string? _customOutputFolder;
    private int _onboardingStep = 0;

    public MainPage()
    {
        InitializeComponent();

        if (MainWindow.CurrentInstance != null)
        {
            MainWindow.CurrentInstance.GlobalOverlayStatusReceived += msg =>
            {
                StatusText.Text = msg;
            };
        }

        ThemeService.PaletteChanged += _ => ApplyDynamicPalette();

        Loaded += (_, _) =>
        {
            ApplyDynamicPalette();
            UpdateOnboardingUI();
            SwitchMode(WheelMode.Converter);
            Focus(FocusState.Programmatic);
        };
    }

    private void ApplyDynamicPalette()
    {
        var palette = ThemeService.GetCurrentPalette();

        var accentBrush = new SolidColorBrush(palette.Accent);
        var textPrimaryBrush = new SolidColorBrush(palette.TextPrimary);
        var textSecondaryBrush = new SolidColorBrush(palette.TextSecondary);
        var textOnAccentBrush = new SolidColorBrush(palette.TextOnAccent);
        var cardFillBrush = new SolidColorBrush(palette.SurfaceCardFill);
        var cardStrokeBrush = new SolidColorBrush(palette.SurfaceStroke);

        // Top Bar
        BrandAccentDot.Fill = accentBrush;
        BrandTitleText.Foreground = textPrimaryBrush;
        BrandPillBorder.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(40, palette.Accent.R, palette.Accent.G, palette.Accent.B));
        BrandPillBorder.BorderBrush = cardStrokeBrush;
        BrandPillText.Foreground = accentBrush;
        BrandPillText.Text = ThemeService.CurrentBackdrop == BackdropPreference.MicaAlt
            ? "Mica Alt · Dynamic Accent"
            : "Acrylic · Dynamic Accent";

        // Wheel Frame
        MainWheelOuterEllipse.Fill = new SolidColorBrush(palette.WheelRingFill);
        MainWheelOuterEllipse.Stroke = cardStrokeBrush;
        MainWheelCenterBorder.Background = new SolidColorBrush(palette.WheelCenterFill);
        MainWheelCenterBorder.BorderBrush = accentBrush;
        CenterSizeText.Foreground = textPrimaryBrush;
        CenterCountText.Foreground = textSecondaryBrush;

        // Right Workspace Text & Translucent Cards
        ModeTitleText.Foreground = textPrimaryBrush;
        ModeSubtitleText.Foreground = textSecondaryBrush;

        InstructionCardBorder.Background = cardFillBrush;
        InstructionCardBorder.BorderBrush = cardStrokeBrush;
        Step1LabelText.Foreground = textPrimaryBrush;
        Step2LabelText.Foreground = textPrimaryBrush;

        KeycapBorder.Background = new SolidColorBrush(palette.WheelCenterFill);
        KeycapBorder.BorderBrush = accentBrush;
        KeycapSymbolText.Foreground = accentBrush;
        KeycapLabelText.Foreground = textPrimaryBrush;
        KeycapHintText.Foreground = textSecondaryBrush;

        SummaryCardBorder.Background = cardFillBrush;
        SummaryCardBorder.BorderBrush = cardStrokeBrush;
        SelectedFilesSummaryText.Foreground = textPrimaryBrush;
        HoveredSectorDescriptionText.Foreground = accentBrush;

        // Bottom Status Bar
        BottomStatusBarBorder.Background = cardFillBrush;
        StatusText.Foreground = textSecondaryBrush;
        OutputModeBadge.Foreground = accentBrush;

        // Onboarding Dynamic Accent Gradient Card
        var onboardingGradient = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1)
        };
        onboardingGradient.GradientStops.Add(new GradientStop { Color = palette.AccentDark1, Offset = 0.0 });
        onboardingGradient.GradientStops.Add(new GradientStop { Color = palette.Accent, Offset = 0.55 });
        onboardingGradient.GradientStops.Add(new GradientStop { Color = palette.AccentLight2, Offset = 1.0 });

        OnboardingCardBorder.Background = onboardingGradient;
        OnboardingCardBorder.BorderBrush = new SolidColorBrush(palette.AccentLight2);
        OnboardingStepTag.Foreground = textOnAccentBrush;
        OnboardingStepTitle.Foreground = textOnAccentBrush;
        OnboardingStepBody.Foreground = textOnAccentBrush;

        OnboardingNextBtn.Background = new SolidColorBrush(palette.WheelCenterFill);
        OnboardingNextBtn.Foreground = textPrimaryBrush;

        // Settings Card
        SettingsCardBorder.Background = new SolidColorBrush(palette.WheelCenterFill);
        SettingsCardBorder.BorderBrush = cardStrokeBrush;
        SettingsHeaderTitle.Foreground = textPrimaryBrush;
        SettingsAppearanceLabel.Foreground = accentBrush;
        SettingsFolderLabel.Foreground = accentBrush;
        SettingsFolderStatusText.Foreground = textSecondaryBrush;
        SettingsShortcutsLabel.Foreground = accentBrush;
        SettingsShortcutsBody.Foreground = textSecondaryBrush;
        SettingsDoneBtn.Background = accentBrush;
        SettingsDoneBtn.Foreground = textOnAccentBrush;

        UpdateTabButtonStyles(palette);
        UpdateOnboardingDots(palette);
        RenderRadialWheel();
    }

    private void UpdateTabButtonStyles(DynamicPalette palette)
    {
        var activeBg = new SolidColorBrush(palette.Accent);
        var activeFg = new SolidColorBrush(palette.TextOnAccent);
        var inactiveBg = new SolidColorBrush(palette.SurfaceCardFill);
        var inactiveFg = new SolidColorBrush(palette.TextPrimary);

        ConverterTabBtn.Background = _currentMode == WheelMode.Converter ? activeBg : inactiveBg;
        ConverterTabBtn.Foreground = _currentMode == WheelMode.Converter ? activeFg : inactiveFg;

        ToolsTabBtn.Background = _currentMode == WheelMode.Tools ? activeBg : inactiveBg;
        ToolsTabBtn.Foreground = _currentMode == WheelMode.Tools ? activeFg : inactiveFg;

        ActionsTabBtn.Background = _currentMode == WheelMode.Actions ? activeBg : inactiveBg;
        ActionsTabBtn.Foreground = _currentMode == WheelMode.Actions ? activeFg : inactiveFg;
    }

    private void SwitchMode(WheelMode mode)
    {
        _currentMode = mode;
        _currentSectors = FileProcessorService.GetSectorsForMode(mode, _loadedFiles);
        _selectedSectorIndex = _currentSectors.Count > 0 ? 0 : -1;

        switch (mode)
        {
            case WheelMode.Converter:
                ModeTitleText.Text = "File Converter";
                ModeSubtitleText.Text = "Select an output format wedge on the wheel. Results are saved next to the originals.";
                KeycapSymbolText.Text = "⇧";
                KeycapLabelText.Text = "shift";
                break;
            case WheelMode.Tools:
                ModeTitleText.Text = "File Tools";
                ModeSubtitleText.Text = "Run non-destructive editing, compression, metadata cleanup, cropping, or redaction.";
                KeycapSymbolText.Text = "⎇";
                KeycapLabelText.Text = "alt";
                break;
            case WheelMode.Actions:
                ModeTitleText.Text = "File Actions";
                ModeSubtitleText.Text = "Route files to Nearby Share, Float preview, Clipboard, Move, Copy Path, or Terminal.";
                KeycapSymbolText.Text = "⌃";
                KeycapLabelText.Text = "ctrl";
                break;
        }

        UpdateTabButtonStyles(ThemeService.GetCurrentPalette());
        RenderRadialWheel();
    }

    private void RenderRadialWheel()
    {
        var palette = ThemeService.GetCurrentPalette();

        WheelCanvas.Children.Clear();
        CenterSizeText.Text = FileProcessorService.FormatTotalSize(_loadedFiles);
        CenterCountText.Text = _loadedFiles.Count == 0
            ? "Drop files here"
            : $"{_loadedFiles.Count} item{(_loadedFiles.Count == 1 ? "" : "s")} ready";

        int count = _currentSectors.Count;
        if (count == 0) return;

        const double cx = 190.0;
        const double cy = 190.0;
        const double innerRadius = 66.0;
        const double outerRadius = 176.0;
        const double gapDegrees = 4.0;

        double sweepPerSector = 360.0 / count;
        double startOffset = -90.0 - (sweepPerSector / 2.0);

        for (int i = 0; i < count; i++)
        {
            int index = i;
            var sector = _currentSectors[i];
            bool isSelected = i == _selectedSectorIndex;

            double a0 = startOffset + i * sweepPerSector + (gapDegrees / 2.0);
            double a1 = startOffset + (i + 1) * sweepPerSector - (gapDegrees / 2.0);

            var wedgePath = CreateWedgePath(cx, cy, innerRadius, outerRadius, a0, a1, isSelected, palette);
            wedgePath.PointerEntered += (_, _) =>
            {
                _selectedSectorIndex = index;
                HoveredSectorDescriptionText.Text = $"{sector.Label}: {sector.Description}";
                RenderRadialWheel();
            };
            wedgePath.PointerPressed += async (_, _) => await TriggerSectorAsync(sector);

            WheelCanvas.Children.Add(wedgePath);

            double midAngleRad = ((a0 + a1) / 2.0) * Math.PI / 180.0;
            double midRadius = (innerRadius + outerRadius) / 2.0;
            double labelX = cx + midRadius * Math.Cos(midAngleRad);
            double labelY = cy + midRadius * Math.Sin(midAngleRad);

            var labelStack = new StackPanel
            {
                Spacing = 3,
                Width = 84,
                IsHitTestVisible = false
            };

            labelStack.Children.Add(new FontIcon
            {
                Glyph = sector.Glyph,
                FontSize = 16,
                Foreground = new SolidColorBrush(isSelected ? palette.TextOnAccent : palette.Accent),
                HorizontalAlignment = HorizontalAlignment.Center
            });

            labelStack.Children.Add(new TextBlock
            {
                Text = sector.Label,
                FontSize = 11,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(isSelected ? palette.TextOnAccent : palette.TextPrimary),
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center
            });

            Canvas.SetLeft(labelStack, labelX - 42);
            Canvas.SetTop(labelStack, labelY - 20);
            WheelCanvas.Children.Add(labelStack);
        }

        if (_selectedSectorIndex >= 0 && _selectedSectorIndex < _currentSectors.Count)
        {
            var active = _currentSectors[_selectedSectorIndex];
            HoveredSectorDescriptionText.Text = $"{active.Label}: {active.Description}";
        }
    }

    private static Microsoft.UI.Xaml.Shapes.Path CreateWedgePath(
        double cx, double cy, double rIn, double rOut, double startDeg, double endDeg, bool isHighlighted, DynamicPalette palette)
    {
        double a0 = startDeg * Math.PI / 180.0;
        double a1 = endDeg * Math.PI / 180.0;

        Point p1 = new(cx + rIn * Math.Cos(a0), cy + rIn * Math.Sin(a0));
        Point p2 = new(cx + rOut * Math.Cos(a0), cy + rOut * Math.Sin(a0));
        Point p3 = new(cx + rOut * Math.Cos(a1), cy + rOut * Math.Sin(a1));
        Point p4 = new(cx + rIn * Math.Cos(a1), cy + rIn * Math.Sin(a1));

        bool isLargeArc = (endDeg - startDeg) > 180.0;

        var figure = new PathFigure { StartPoint = p1, IsClosed = true, IsFilled = true };
        figure.Segments.Add(new LineSegment { Point = p2 });
        figure.Segments.Add(new ArcSegment
        {
            Point = p3,
            Size = new Size(rOut, rOut),
            SweepDirection = SweepDirection.Clockwise,
            IsLargeArc = isLargeArc
        });
        figure.Segments.Add(new LineSegment { Point = p4 });
        figure.Segments.Add(new ArcSegment
        {
            Point = p1,
            Size = new Size(rIn, rIn),
            SweepDirection = SweepDirection.Counterclockwise,
            IsLargeArc = isLargeArc
        });

        var geo = new PathGeometry();
        geo.Figures.Add(figure);

        return new Microsoft.UI.Xaml.Shapes.Path
        {
            Data = geo,
            Fill = new SolidColorBrush(isHighlighted ? palette.Accent : palette.WedgeFill),
            Stroke = new SolidColorBrush(isHighlighted ? palette.AccentLight2 : palette.WedgeStroke),
            StrokeThickness = 1.5
        };
    }

    private async Task TriggerSectorAsync(SectorItem sector)
    {
        StatusText.Text = $"Running {sector.Label}...";
        string result = await FileProcessorService.ExecuteSectorAsync(
            _currentMode,
            sector.Id,
            _loadedFiles,
            _customOutputFolder,
            filePath => MainWindow.SpawnFloatingFileWindow(filePath));

        StatusText.Text = $"✅ {result}";
    }

    private void Page_DragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = DataPackageOperation.Copy;

        if (e.Modifiers.HasFlag(DragDropModifiers.Shift))
        {
            if (_currentMode != WheelMode.Converter) SwitchMode(WheelMode.Converter);
        }
        else if (e.Modifiers.HasFlag(DragDropModifiers.Alt))
        {
            if (_currentMode != WheelMode.Tools) SwitchMode(WheelMode.Tools);
        }
        else if (e.Modifiers.HasFlag(DragDropModifiers.Control))
        {
            if (_currentMode != WheelMode.Actions) SwitchMode(WheelMode.Actions);
        }

        Point pt = e.GetPosition(WheelCanvas);
        UpdateHoveredSectorFromPoint(pt);
    }

    private void UpdateHoveredSectorFromPoint(Point pt)
    {
        double dx = pt.X - 190.0;
        double dy = pt.Y - 190.0;
        double dist = Math.Sqrt(dx * dx + dy * dy);
        if (dist < 60 || dist > 185 || _currentSectors.Count == 0) return;

        double deg = Math.Atan2(dy, dx) * 180.0 / Math.PI;
        double sweep = 360.0 / _currentSectors.Count;
        double normalized = (deg - (-90.0 - sweep / 2.0) + 360.0) % 360.0;
        int idx = (int)(normalized / sweep);

        if (idx >= 0 && idx < _currentSectors.Count && idx != _selectedSectorIndex)
        {
            _selectedSectorIndex = idx;
            RenderRadialWheel();
        }
    }

    private async void Page_Drop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;

        var items = await e.DataView.GetStorageItemsAsync();
        _loadedFiles.Clear();
        foreach (var item in items)
        {
            if (!string.IsNullOrEmpty(item.Path))
            {
                _loadedFiles.Add(item.Path);
            }
        }

        UpdateLoadedFilesSummary();
        SwitchMode(_currentMode);

        Point pt = e.GetPosition(WheelCanvas);
        double dx = pt.X - 190.0;
        double dy = pt.Y - 190.0;
        double dist = Math.Sqrt(dx * dx + dy * dy);
        if (dist >= 60 && dist <= 185 && _selectedSectorIndex >= 0 && _selectedSectorIndex < _currentSectors.Count)
        {
            await TriggerSectorAsync(_currentSectors[_selectedSectorIndex]);
        }
    }

    private void UpdateLoadedFilesSummary()
    {
        _sharedLoadedFiles.Clear();
        _sharedLoadedFiles.AddRange(_loadedFiles);

        if (_loadedFiles.Count == 0)
        {
            SelectedFilesSummaryText.Text = "No files loaded yet — drop any file onto the window or click 'Load Sample Image'.";
            return;
        }

        string names = string.Join(", ", _loadedFiles.Select(p => System.IO.Path.GetFileName(p.TrimEnd(System.IO.Path.DirectorySeparatorChar))));
        SelectedFilesSummaryText.Text = $"Loaded ({_loadedFiles.Count}): {names}";
    }

    private async void Page_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Shift:
                SwitchMode(WheelMode.Converter);
                break;
            case VirtualKey.Menu:
                SwitchMode(WheelMode.Tools);
                break;
            case VirtualKey.Control:
                SwitchMode(WheelMode.Actions);
                break;
            case VirtualKey.Right:
            case VirtualKey.Down:
                if (_currentSectors.Count > 0)
                {
                    _selectedSectorIndex = (_selectedSectorIndex + 1) % _currentSectors.Count;
                    RenderRadialWheel();
                }
                break;
            case VirtualKey.Left:
            case VirtualKey.Up:
                if (_currentSectors.Count > 0)
                {
                    _selectedSectorIndex = (_selectedSectorIndex - 1 + _currentSectors.Count) % _currentSectors.Count;
                    RenderRadialWheel();
                }
                break;
            case VirtualKey.Enter:
                if (_selectedSectorIndex >= 0 && _selectedSectorIndex < _currentSectors.Count)
                {
                    await TriggerSectorAsync(_currentSectors[_selectedSectorIndex]);
                }
                break;
            case VirtualKey.Escape:
                _loadedFiles.Clear();
                UpdateLoadedFilesSummary();
                RenderRadialWheel();
                StatusText.Text = "Cleared active file selection.";
                break;
        }
    }

    private void ConverterTab_Click(object sender, RoutedEventArgs e) => SwitchMode(WheelMode.Converter);
    private void ToolsTab_Click(object sender, RoutedEventArgs e) => SwitchMode(WheelMode.Tools);
    private void ActionsTab_Click(object sender, RoutedEventArgs e) => SwitchMode(WheelMode.Actions);

    private void PinToggleBtn_Changed(object sender, RoutedEventArgs e)
    {
        MainWindow.CurrentInstance?.SetAlwaysOnTop(PinToggleBtn.IsChecked == true);
    }

    private async void BrowseFiles_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add("*");
        if (MainWindow.CurrentInstance != null)
        {
            IntPtr hwnd = WindowNative.GetWindowHandle(MainWindow.CurrentInstance);
            InitializeWithWindow.Initialize(picker, hwnd);
        }

        var files = await picker.PickMultipleFilesAsync();
        if (files != null && files.Count > 0)
        {
            _loadedFiles.Clear();
            foreach (var f in files) _loadedFiles.Add(f.Path);
            UpdateLoadedFilesSummary();
            SwitchMode(_currentMode);
            StatusText.Text = $"Loaded {_loadedFiles.Count} file(s). Click a radial wedge or press Enter.";
        }
    }

    private async void CreateSampleFile_Click(object sender, RoutedEventArgs e)
    {
        var palette = ThemeService.GetCurrentPalette();
        string folder = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            folder = System.IO.Path.GetTempPath();

        string samplePath = System.IO.Path.Combine(folder, "LemonAID_Sample.png");
        uint w = 400, h = 400;
        byte[] pixels = new byte[w * h * 4];
        for (uint y = 0; y < h; y++)
        {
            for (uint x = 0; x < w; x++)
            {
                uint idx = (y * w + x) * 4;
                double dx = (int)x - 200;
                double dy = (int)y - 200;
                bool inCircle = (dx * dx + dy * dy) < 140 * 140;
                pixels[idx + 0] = inCircle ? palette.Accent.B : (byte)40;
                pixels[idx + 1] = inCircle ? palette.Accent.G : (byte)42;
                pixels[idx + 2] = inCircle ? palette.Accent.R : (byte)48;
                pixels[idx + 3] = 255;
            }
        }

        using (var fs = new FileStream(samplePath, FileMode.Create, FileAccess.ReadWrite))
        using (var ras = fs.AsRandomAccessStream())
        {
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, ras);
            encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, w, h, 96, 96, pixels);
            await encoder.FlushAsync();
        }

        _loadedFiles.Clear();
        _loadedFiles.Add(samplePath);
        UpdateLoadedFilesSummary();
        SwitchMode(_currentMode);
        StatusText.Text = $"Created & loaded dynamic-accent sample image at {samplePath}";
    }

    private void UpdateOnboardingUI()
    {
        OnboardingBackBtn.Visibility = _onboardingStep > 0 ? Visibility.Visible : Visibility.Collapsed;
        OnboardingExtraPanel.Visibility = Visibility.Collapsed;
        OnboardingFolderBtn.Visibility = Visibility.Collapsed;
        LaunchAtLoginToggle.Visibility = Visibility.Collapsed;
        OnboardingNextBtn.Content = _onboardingStep == 5 ? "Done" : "Continue";

        switch (_onboardingStep)
        {
            case 0:
                OnboardingStepTag.Text = "Welcome to";
                OnboardingStepTitle.Text = "LemonAID";
                OnboardingStepBody.Text = "Contextual radial wheels with Windows Mica Alt & Dynamic Accent theming.";
                break;
            case 1:
                OnboardingStepTag.Text = "Step 2 of 6 · Shift Key Cue";
                OnboardingStepTitle.Text = "File Converter";
                OnboardingStepBody.Text = "1. Drag or Hold Left-Click on a file   2. Tap [Shift] — Convert to PDF, PNG, WEBP, HEIC, TIFF, AVIF or document formats.";
                break;
            case 2:
                OnboardingStepTag.Text = "Step 3 of 6 · Alt Key Cue";
                OnboardingStepTitle.Text = "File Tools";
                OnboardingStepBody.Text = "1. Drag or Hold Left-Click   2. Tap [Alt] — Access Compress, Metadata, Edit, Annotate, Add BG, Crop, and Redact.";
                break;
            case 3:
                OnboardingStepTag.Text = "Step 4 of 6 · Ctrl Key Cue";
                OnboardingStepTitle.Text = "File Actions";
                OnboardingStepBody.Text = "1. Drag or Hold Left-Click   2. Tap [Ctrl] — Trigger Nearby Share, Float, Copy, Move, Copy Path, or Windows Terminal.";
                break;
            case 4:
                OnboardingStepTag.Text = "Step 5 of 6 · Output Behavior";
                OnboardingStepTitle.Text = "Folder access";
                OnboardingStepBody.Text = "Results are saved next to the originals by default. You can also optionally choose a dedicated output folder.";
                OnboardingExtraPanel.Visibility = Visibility.Visible;
                OnboardingFolderBtn.Visibility = Visibility.Visible;
                break;
            case 5:
                OnboardingStepTag.Text = "Step 6 of 6 · Startup Preference";
                OnboardingStepTitle.Text = "Finish setup";
                OnboardingStepBody.Text = "Choose whether LemonAID launches at login. You can customize Mica Alt / Acrylic and shortcuts anytime in Settings.";
                OnboardingExtraPanel.Visibility = Visibility.Visible;
                LaunchAtLoginToggle.Visibility = Visibility.Visible;
                break;
        }

        UpdateOnboardingDots(ThemeService.GetCurrentPalette());
    }

    private void UpdateOnboardingDots(DynamicPalette palette)
    {
        if (ProgressDotsPanel == null) return;
        ProgressDotsPanel.Children.Clear();
        for (int i = 0; i < 6; i++)
        {
            ProgressDotsPanel.Children.Add(new Ellipse
            {
                Width = 8,
                Height = 8,
                Fill = new SolidColorBrush(i == _onboardingStep
                    ? palette.TextOnAccent
                    : Windows.UI.Color.FromArgb(95, palette.TextOnAccent.R, palette.TextOnAccent.G, palette.TextOnAccent.B))
            });
        }
    }

    private void OnboardingBack_Click(object sender, RoutedEventArgs e)
    {
        if (_onboardingStep > 0)
        {
            _onboardingStep--;
            UpdateOnboardingUI();
        }
    }

    private void OnboardingNext_Click(object sender, RoutedEventArgs e)
    {
        if (_onboardingStep < 5)
        {
            _onboardingStep++;
            UpdateOnboardingUI();
        }
        else
        {
            OnboardingOverlay.Visibility = Visibility.Collapsed;
            Focus(FocusState.Programmatic);
        }
    }

    private void OpenOnboarding_Click(object sender, RoutedEventArgs e)
    {
        _onboardingStep = 0;
        UpdateOnboardingUI();
        OnboardingOverlay.Visibility = Visibility.Visible;
    }

    private void OpenSettings_Click(object sender, RoutedEventArgs e) => SettingsOverlay.Visibility = Visibility.Visible;
    private void CloseSettings_Click(object sender, RoutedEventArgs e) => SettingsOverlay.Visibility = Visibility.Collapsed;

    private void BackdropComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (BackdropComboBox == null) return;
        var pref = BackdropComboBox.SelectedIndex == 1
            ? BackdropPreference.Acrylic
            : BackdropPreference.MicaAlt;

        ThemeService.SetBackdropPreference(
            pref,
            MainWindow.CurrentInstance,
            MainWindow.CurrentInstance?.OverlayWindowInstance);
    }

    private void ThemeModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ThemeModeComboBox == null) return;
        ElementTheme theme = ThemeModeComboBox.SelectedIndex switch
        {
            1 => ElementTheme.Dark,
            2 => ElementTheme.Light,
            _ => ElementTheme.Default
        };

        ThemeService.SetThemeOverride(
            theme,
            MainWindow.CurrentInstance,
            MainWindow.CurrentInstance?.OverlayWindowInstance);
    }

    private async void SelectCustomFolder_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker();
        picker.FileTypeFilter.Add("*");
        if (MainWindow.CurrentInstance != null)
        {
            IntPtr hwnd = WindowNative.GetWindowHandle(MainWindow.CurrentInstance);
            InitializeWithWindow.Initialize(picker, hwnd);
        }

        StorageFolder? folder = await picker.PickSingleFolderAsync();
        if (folder != null)
        {
            _customOutputFolder = folder.Path;
            CurrentCustomOutputFolder = _customOutputFolder;
            OutputModeBadge.Text = $"Output: {folder.Name}";
            SettingsFolderStatusText.Text = $"Saving results to: {_customOutputFolder}";
            StatusText.Text = $"Updated output folder to {_customOutputFolder}";
        }
    }

    private void ResetOutputFolder_Click(object sender, RoutedEventArgs e)
    {
        _customOutputFolder = null;
        CurrentCustomOutputFolder = null;
        OutputModeBadge.Text = "Output: Beside Originals";
        SettingsFolderStatusText.Text = "Default: Results are saved next to the originals.";
        StatusText.Text = "Output location reset to save beside original files.";
    }
}
