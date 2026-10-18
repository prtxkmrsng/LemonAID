using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using LemonAID.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Graphics;
using WinRT.Interop;

namespace LemonAID;

public sealed partial class RadialOverlayWindow : Window
{
    private const int LogicalDiameter = 380;

    private readonly IntPtr _hwnd;
    private WheelMode _currentMode = WheelMode.Converter;
    private readonly List<string> _activePaths = [];
    private IReadOnlyList<SectorItem> _sectors = [];
    private readonly List<WedgeVisualNode> _wedgeNodes = [];
    private int _selectedSectorIndex = -1;
    private int _centerScreenX;
    private int _centerScreenY;
    private double _dpiScale = 1.0;
    private bool _isExecutingOrHidden = true;

    public event Action<string>? OperationCompleted;

    public RadialOverlayWindow()
    {
        InitializeComponent();
        _hwnd = WindowNative.GetWindowHandle(this);

        ThemeService.ApplyBackdropToWindow(this, isTransientOverlay: true);
        ThemeService.PaletteChanged += _ => RebuildWedgeVisuals();

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsAlwaysOnTop = true;
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
        }

        AppWindow.IsShownInSwitchers = false;
    }

    public void SummonAtCursor(int screenX, int screenY, WheelMode mode, IReadOnlyList<string> detectedPaths)
    {
        _isExecutingOrHidden = false;
        _centerScreenX = screenX;
        _centerScreenY = screenY;

        _activePaths.Clear();
        foreach (var p in detectedPaths)
        {
            _activePaths.Add(p);
        }

        uint dpi = GetDpiForWindow(_hwnd);
        _dpiScale = dpi > 0 ? dpi / 96.0 : 1.0;
        int physDiameter = (int)Math.Round(LogicalDiameter * _dpiScale);

        int left = screenX - (physDiameter / 2);
        int top = screenY - (physDiameter / 2);

        AppWindow.MoveAndResize(new RectInt32(left, top, physDiameter, physDiameter));

        IntPtr hRgn = CreateEllipticRgn(0, 0, physDiameter, physDiameter);
        SetWindowRgn(_hwnd, hRgn, true);

        SwitchModeInternal(mode, playModeFeedback: false);
        AppWindow.Show(true);

        FeedbackService.Play(FeedbackCue.WheelSummon);
        WheelAnimationHelper.AnimateWheelSummon(RootGrid, RootWheelTransform, CenterHubTransform);
    }

    public void DismissOverlay(bool playSound = true)
    {
        if (!_isExecutingOrHidden && playSound)
        {
            FeedbackService.Play(FeedbackCue.Dismiss);
        }

        _isExecutingOrHidden = true;
        _selectedSectorIndex = -1;
        AppWindow.Hide();
    }

    public void SwitchMode(WheelMode mode)
    {
        SwitchModeInternal(mode, playModeFeedback: true);
    }

    private void SwitchModeInternal(WheelMode mode, bool playModeFeedback)
    {
        _currentMode = mode;
        _sectors = FileProcessorService.GetSectorsForMode(mode, _activePaths);
        _selectedSectorIndex = -1;

        OverlayModeBadge.Text = mode switch
        {
            WheelMode.Converter => "SHIFT · CONVERTER",
            WheelMode.Tools => "ALT · FILE TOOLS",
            WheelMode.Actions => "CTRL · ACTIONS",
            _ => "LEMONAID"
        };

        OverlaySizeText.Text = FileProcessorService.FormatTotalSize(_activePaths);
        OverlayTargetText.Text = _activePaths.Count > 0
            ? System.IO.Path.GetFileName(_activePaths[0].TrimEnd(System.IO.Path.DirectorySeparatorChar))
            : "Drop / Release";

        RebuildWedgeVisuals();

        if (playModeFeedback)
        {
            FeedbackService.Play(FeedbackCue.ModeSwitch);
            WheelAnimationHelper.AnimateModeSwitch(RootWheelTransform, CenterHubTransform);
        }
    }

    public void UpdateCursorScreenPosition(int screenX, int screenY)
    {
        if (_isExecutingOrHidden || _sectors.Count == 0) return;

        double dx = (screenX - _centerScreenX) / _dpiScale;
        double dy = (screenY - _centerScreenY) / _dpiScale;
        UpdateSectorFromDelta(dx, dy);
    }

    public async Task CompleteOnMouseReleaseAsync(int screenX, int screenY, string? customOutputFolder)
    {
        if (_isExecutingOrHidden) return;

        UpdateCursorScreenPosition(screenX, screenY);

        if (_selectedSectorIndex >= 0 && _selectedSectorIndex < _sectors.Count && _activePaths.Count > 0)
        {
            var chosen = _sectors[_selectedSectorIndex];
            FeedbackService.Play(FeedbackCue.ExecuteSuccess);
            DismissOverlay(playSound: false);

            string status = await FileProcessorService.ExecuteSectorAsync(
                _currentMode,
                chosen.Id,
                _activePaths,
                customOutputFolder,
                path => MainWindow.SpawnFloatingFileWindow(path));

            OperationCompleted?.Invoke($"⚡ Overlay [{chosen.Label}]: {status}");
        }
        else if (_activePaths.Count > 0)
        {
            DismissOverlay(playSound: true);
        }
    }

    private void UpdateSectorFromDelta(double dx, double dy)
    {
        double dist = Math.Sqrt(dx * dx + dy * dy);
        if (dist < 56.0 || dist > 195.0 || _sectors.Count == 0)
        {
            if (_selectedSectorIndex != -1)
            {
                int prev = _selectedSectorIndex;
                _selectedSectorIndex = -1;
                OverlayTargetText.Text = _activePaths.Count > 0
                    ? System.IO.Path.GetFileName(_activePaths[0].TrimEnd(System.IO.Path.DirectorySeparatorChar))
                    : "Release to cancel";

                var palette = ThemeService.GetCurrentPalette();
                if (prev >= 0 && prev < _wedgeNodes.Count)
                {
                    WheelAnimationHelper.UpdateWedgeSelection(_wedgeNodes[prev], false, palette);
                }
            }
            return;
        }

        double deg = Math.Atan2(dy, dx) * 180.0 / Math.PI;
        double sweep = 360.0 / _sectors.Count;
        double normalized = (deg - (-90.0 - sweep / 2.0) + 360.0) % 360.0;
        int idx = (int)(normalized / sweep);

        if (idx >= 0 && idx < _sectors.Count && idx != _selectedSectorIndex)
        {
            int previousIdx = _selectedSectorIndex;
            _selectedSectorIndex = idx;
            OverlayTargetText.Text = _sectors[idx].Label;

            var palette = ThemeService.GetCurrentPalette();
            if (previousIdx >= 0 && previousIdx < _wedgeNodes.Count)
            {
                WheelAnimationHelper.UpdateWedgeSelection(_wedgeNodes[previousIdx], false, palette);
            }
            if (idx < _wedgeNodes.Count)
            {
                WheelAnimationHelper.UpdateWedgeSelection(_wedgeNodes[idx], true, palette);
            }

            FeedbackService.Play(FeedbackCue.WedgeTick);
        }
    }

    private void RebuildWedgeVisuals()
    {
        var palette = ThemeService.GetCurrentPalette();

        OuterRingEllipse.Fill = new SolidColorBrush(palette.WheelRingFill);
        OuterRingEllipse.Stroke = new SolidColorBrush(palette.SurfaceStroke);

        CenterHubBorder.Background = new SolidColorBrush(palette.WheelCenterFill);
        CenterHubBorder.BorderBrush = new SolidColorBrush(palette.SurfaceStroke);

        OverlayModeBadge.Foreground = new SolidColorBrush(palette.Accent);
        OverlaySizeText.Foreground = new SolidColorBrush(palette.TextPrimary);
        OverlayTargetText.Foreground = new SolidColorBrush(palette.TextSecondary);

        OverlayWheelCanvas.Children.Clear();
        _wedgeNodes.Clear();

        int count = _sectors.Count;
        if (count == 0) return;

        const double cx = 190.0;
        const double cy = 190.0;
        const double rIn = 66.0;
        const double rOut = 172.0;
        const double gapDeg = 4.0;

        double sweep = 360.0 / count;
        double startOffset = -90.0 - (sweep / 2.0);

        for (int i = 0; i < count; i++)
        {
            int idx = i;
            var sector = _sectors[i];
            bool isSelected = i == _selectedSectorIndex;

            double a0 = startOffset + i * sweep + (gapDeg / 2.0);
            double a1 = startOffset + (i + 1) * sweep - (gapDeg / 2.0);
            double midRad = ((a0 + a1) / 2.0) * Math.PI / 180.0;

            var wedgeTransform = new CompositeTransform { CenterX = cx, CenterY = cy };
            var path = BuildWedgePath(cx, cy, rIn, rOut, a0, a1, isSelected, palette);
            path.RenderTransform = wedgeTransform;

            path.PointerPressed += async (_, _) =>
            {
                _selectedSectorIndex = idx;
                await CompleteOnMouseReleaseAsync(_centerScreenX, _centerScreenY, MainPage.CurrentCustomOutputFolder);
            };
            OverlayWheelCanvas.Children.Add(path);

            double midR = (rIn + rOut) / 2.0;
            double lx = cx + midR * Math.Cos(midRad);
            double ly = cy + midR * Math.Sin(midRad);

            var labelTransform = new CompositeTransform { CenterX = 40, CenterY = 18 };
            var stack = new StackPanel
            {
                Width = 80,
                Spacing = 2,
                IsHitTestVisible = false,
                RenderTransform = labelTransform
            };

            var icon = new FontIcon
            {
                Glyph = sector.Glyph,
                FontSize = 15,
                Foreground = new SolidColorBrush(isSelected ? palette.TextOnAccent : palette.Accent),
                HorizontalAlignment = HorizontalAlignment.Center
            };

            var label = new TextBlock
            {
                Text = sector.Label,
                FontSize = 11,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(isSelected ? palette.TextOnAccent : palette.TextPrimary),
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center
            };

            stack.Children.Add(icon);
            stack.Children.Add(label);

            Canvas.SetLeft(stack, lx - 40);
            Canvas.SetTop(stack, ly - 18);
            OverlayWheelCanvas.Children.Add(stack);

            var node = new WedgeVisualNode
            {
                WedgePath = path,
                LabelStack = stack,
                Icon = icon,
                Label = label,
                WedgeTransform = wedgeTransform,
                LabelTransform = labelTransform,
                MidAngleRadians = midRad
            };
            _wedgeNodes.Add(node);

            if (isSelected)
            {
                WheelAnimationHelper.UpdateWedgeSelection(node, true, palette);
            }
        }
    }

    private static Microsoft.UI.Xaml.Shapes.Path BuildWedgePath(
        double cx, double cy, double rIn, double rOut, double startDeg, double endDeg, bool isSelected, DynamicPalette palette)
    {
        double a0 = startDeg * Math.PI / 180.0;
        double a1 = endDeg * Math.PI / 180.0;

        Point p1 = new(cx + rIn * Math.Cos(a0), cy + rIn * Math.Sin(a0));
        Point p2 = new(cx + rOut * Math.Cos(a0), cy + rOut * Math.Sin(a0));
        Point p3 = new(cx + rOut * Math.Cos(a1), cy + rOut * Math.Sin(a1));
        Point p4 = new(cx + rIn * Math.Cos(a1), cy + rIn * Math.Sin(a1));

        var fig = new PathFigure { StartPoint = p1, IsClosed = true, IsFilled = true };
        fig.Segments.Add(new LineSegment { Point = p2 });
        fig.Segments.Add(new ArcSegment
        {
            Point = p3,
            Size = new Size(rOut, rOut),
            SweepDirection = SweepDirection.Clockwise,
            IsLargeArc = (endDeg - startDeg) > 180.0
        });
        fig.Segments.Add(new LineSegment { Point = p4 });
        fig.Segments.Add(new ArcSegment
        {
            Point = p1,
            Size = new Size(rIn, rIn),
            SweepDirection = SweepDirection.Counterclockwise,
            IsLargeArc = (endDeg - startDeg) > 180.0
        });

        var geo = new PathGeometry();
        geo.Figures.Add(fig);

        return new Microsoft.UI.Xaml.Shapes.Path
        {
            Data = geo,
            Fill = new SolidColorBrush(isSelected ? palette.Accent : palette.WedgeFill),
            Stroke = new SolidColorBrush(isSelected ? palette.AccentLight2 : palette.WedgeStroke),
            StrokeThickness = 1.5
        };
    }

    private void RootGrid_DragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = DataPackageOperation.Copy;
        Point pt = e.GetPosition(OverlayWheelCanvas);
        UpdateSectorFromDelta(pt.X - 190.0, pt.Y - 190.0);
    }

    private async void RootGrid_Drop(object sender, DragEventArgs e)
    {
        if (_isExecutingOrHidden) return;

        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            var items = await e.DataView.GetStorageItemsAsync();
            _activePaths.Clear();
            foreach (var item in items)
            {
                if (!string.IsNullOrEmpty(item.Path))
                {
                    _activePaths.Add(item.Path);
                }
            }
        }

        Point pt = e.GetPosition(OverlayWheelCanvas);
        UpdateSectorFromDelta(pt.X - 190.0, pt.Y - 190.0);

        if (_selectedSectorIndex >= 0 && _selectedSectorIndex < _sectors.Count && _activePaths.Count > 0)
        {
            var chosen = _sectors[_selectedSectorIndex];
            FeedbackService.Play(FeedbackCue.ExecuteSuccess);
            DismissOverlay(playSound: false);

            string status = await FileProcessorService.ExecuteSectorAsync(
                _currentMode,
                chosen.Id,
                _activePaths,
                MainPage.CurrentCustomOutputFolder,
                path => MainWindow.SpawnFloatingFileWindow(path));

            OperationCompleted?.Invoke($"⚡ Overlay [{chosen.Label}]: {status}");
        }
        else
        {
            DismissOverlay(playSound: true);
        }
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateEllipticRgn(int nLeftRect, int nTopRect, int nRightRect, int nBottomRect);

    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool bRedraw);
}
