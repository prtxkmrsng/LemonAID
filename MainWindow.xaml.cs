using System;
using System.Collections.Generic;
using System.IO;
using LemonAID.Services;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;

namespace LemonAID;

public sealed partial class MainWindow : Window
{
    public static MainWindow? CurrentInstance { get; private set; }

    public GlobalInputHookService HookService { get; } = new();
    private RadialOverlayWindow? _overlayWindow;
    private readonly DispatcherQueue _dispatcherQueue;

    public RadialOverlayWindow? OverlayWindowInstance => _overlayWindow;

    public event Action<string>? GlobalOverlayStatusReceived;

    public MainWindow()
    {
        InitializeComponent();
        CurrentInstance = this;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();

        ThemeService.Initialize(_dispatcherQueue);
        ThemeService.ApplyBackdropToWindow(this, isTransientOverlay: false);
        ThemeService.PaletteChanged += _ => ThemeService.StyleTitleBarButtons(this);

        ExtendsContentIntoTitleBar = true;
        AppWindow.Resize(new SizeInt32(980, 650));

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsMaximizable = true;
            presenter.IsMinimizable = true;
        }

        SetupGlobalHookEvents();
        HookService.Start();

        Closed += (_, _) =>
        {
            HookService.Dispose();
            _overlayWindow?.Close();
        };

        RootFrame.Navigate(typeof(MainPage));
    }

    private void SetupGlobalHookEvents()
    {
        HookService.OverlaySummoned += (screenX, screenY, mode, sourceHwnd) =>
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                if (_overlayWindow == null)
                {
                    _overlayWindow = new RadialOverlayWindow();
                    _overlayWindow.OperationCompleted += msg =>
                    {
                        HookService.IsOverlayVisible = false;
                        GlobalOverlayStatusReceived?.Invoke(msg);
                    };
                }

                List<string> paths = GlobalInputHookService.TryGetSelectedPathsFromExplorer(sourceHwnd);
                if (paths.Count == 0 && MainPage.CurrentLoadedFiles.Count > 0)
                {
                    paths.AddRange(MainPage.CurrentLoadedFiles);
                }

                HookService.IsOverlayVisible = true;
                _overlayWindow.SummonAtCursor(screenX, screenY, mode, paths);
            });
        };

        HookService.ModeChangeRequested += mode =>
        {
            _dispatcherQueue.TryEnqueue(() => _overlayWindow?.SwitchMode(mode));
        };

        HookService.CursorMovedWhileActive += (screenX, screenY) =>
        {
            _dispatcherQueue.TryEnqueue(() => _overlayWindow?.UpdateCursorScreenPosition(screenX, screenY));
        };

        HookService.LeftButtonReleasedWhileActive += (screenX, screenY) =>
        {
            _dispatcherQueue.TryEnqueue(async () =>
            {
                if (_overlayWindow != null)
                {
                    HookService.IsOverlayVisible = false;
                    await _overlayWindow.CompleteOnMouseReleaseAsync(screenX, screenY, MainPage.CurrentCustomOutputFolder);
                }
            });
        };

        HookService.DismissRequested += () =>
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                HookService.IsOverlayVisible = false;
                _overlayWindow?.DismissOverlay();
            });
        };
    }

    public void SetAlwaysOnTop(bool pin)
    {
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = pin;
        }
    }

    public static void SpawnFloatingFileWindow(string filePath)
    {
        var palette = ThemeService.GetCurrentPalette();

        var floatWin = new Window
        {
            Title = $"Float — {Path.GetFileName(filePath)}",
            ExtendsContentIntoTitleBar = true,
            SystemBackdrop = new DesktopAcrylicBackdrop()
        };

        floatWin.AppWindow.Resize(new SizeInt32(360, 320));
        if (floatWin.AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = true;
            presenter.IsMaximizable = false;
        }

        var root = new Grid
        {
            Background = new SolidColorBrush(Colors.Transparent),
            Padding = new Thickness(16),
            RequestedTheme = ThemeService.CurrentThemeOverride,
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = new GridLength(1, GridUnitType.Star) },
                new RowDefinition { Height = GridLength.Auto }
            }
        };

        var header = new TextBlock
        {
            Text = $"📌 {Path.GetFileName(filePath.TrimEnd(Path.DirectorySeparatorChar))}",
            Foreground = new SolidColorBrush(palette.Accent),
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Margin = new Thickness(0, 8, 0, 12)
        };
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        string ext = Path.GetExtension(filePath).ToLowerInvariant();
        if (File.Exists(filePath) && ext is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".webp")
        {
            var img = new Image
            {
                Source = new BitmapImage(new Uri(filePath)),
                Stretch = Stretch.Uniform
            };
            Grid.SetRow(img, 1);
            root.Children.Add(img);
        }
        else
        {
            var infoText = new TextBlock
            {
                Text = $"Path:\n{filePath}",
                Foreground = new SolidColorBrush(palette.TextPrimary),
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetRow(infoText, 1);
            root.Children.Add(infoText);
        }

        var closeBtn = new Button
        {
            Content = "Dismiss Float",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = new SolidColorBrush(palette.Accent),
            Foreground = new SolidColorBrush(palette.TextOnAccent),
            CornerRadius = new CornerRadius(10),
            Margin = new Thickness(0, 12, 0, 0)
        };
        closeBtn.Click += (_, _) => floatWin.Close();
        Grid.SetRow(closeBtn, 2);
        root.Children.Add(closeBtn);

        floatWin.Content = root;
        ThemeService.StyleTitleBarButtons(floatWin);
        floatWin.Activate();
    }
}
