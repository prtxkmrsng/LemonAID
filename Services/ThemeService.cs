using System;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace LemonAID.Services;

public enum BackdropPreference
{
    MicaAlt,
    Acrylic
}

public sealed record DynamicPalette(
    bool IsDark,
    Color Accent,
    Color AccentLight1,
    Color AccentLight2,
    Color AccentDark1,
    Color TextPrimary,
    Color TextSecondary,
    Color TextOnAccent,
    Color SurfaceCardFill,
    Color SurfaceStroke,
    Color WedgeFill,
    Color WedgeStroke,
    Color WheelRingFill,
    Color WheelCenterFill);

public static class ThemeService
{
    private static readonly UISettings _uiSettings = new();
    private static DispatcherQueue? _dispatcherQueue;
    private static bool _isInitialized;

    public static BackdropPreference CurrentBackdrop { get; private set; } = BackdropPreference.MicaAlt;
    public static ElementTheme CurrentThemeOverride { get; private set; } = ElementTheme.Default;

    public static event Action<DynamicPalette>? PaletteChanged;

    public static void Initialize(DispatcherQueue dispatcherQueue)
    {
        _dispatcherQueue = dispatcherQueue;
        if (_isInitialized) return;
        _isInitialized = true;

        _uiSettings.ColorValuesChanged += (_, _) =>
        {
            _dispatcherQueue?.TryEnqueue(() =>
            {
                PaletteChanged?.Invoke(GetCurrentPalette());
            });
        };
    }

    public static void SetBackdropPreference(BackdropPreference preference, params Window?[] windows)
    {
        CurrentBackdrop = preference;
        foreach (var win in windows)
        {
            if (win != null) ApplyBackdropToWindow(win, isTransientOverlay: win is RadialOverlayWindow);
        }
        PaletteChanged?.Invoke(GetCurrentPalette());
    }

    public static void SetThemeOverride(ElementTheme theme, params Window?[] windows)
    {
        CurrentThemeOverride = theme;
        foreach (var win in windows)
        {
            if (win?.Content is FrameworkElement fe)
            {
                fe.RequestedTheme = theme;
            }
        }
        PaletteChanged?.Invoke(GetCurrentPalette());
    }

    public static void ApplyBackdropToWindow(Window window, bool isTransientOverlay = false)
    {
        if (isTransientOverlay || CurrentBackdrop == BackdropPreference.Acrylic)
        {
            window.SystemBackdrop = new DesktopAcrylicBackdrop();
        }
        else
        {
            window.SystemBackdrop = new MicaBackdrop { Kind = MicaKind.BaseAlt };
        }

        if (window.Content is FrameworkElement rootElement)
        {
            rootElement.RequestedTheme = CurrentThemeOverride;
        }

        StyleTitleBarButtons(window);
    }

    public static void StyleTitleBarButtons(Window window)
    {
        try
        {
            if (!AppWindowTitleBar.IsCustomizationSupported()) return;

            var titleBar = window.AppWindow.TitleBar;
            titleBar.ExtendsContentIntoTitleBar = true;

            var palette = GetCurrentPalette();
            titleBar.ButtonBackgroundColor = Colors.Transparent;
            titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
            titleBar.ButtonForegroundColor = palette.TextPrimary;
            titleBar.ButtonHoverBackgroundColor = Color.FromArgb(40, palette.Accent.R, palette.Accent.G, palette.Accent.B);
            titleBar.ButtonHoverForegroundColor = palette.TextPrimary;
            titleBar.ButtonPressedBackgroundColor = Color.FromArgb(70, palette.Accent.R, palette.Accent.G, palette.Accent.B);
        }
        catch
        {
            // Ignore on unsupported window presenter configurations
        }
    }

    public static DynamicPalette GetCurrentPalette()
    {
        Color sysBg = _uiSettings.GetColorValue(UIColorType.Background);
        bool systemIsDark = (sysBg.R + sysBg.G + sysBg.B) < 384;

        bool isDark = CurrentThemeOverride switch
        {
            ElementTheme.Dark => true,
            ElementTheme.Light => false,
            _ => systemIsDark
        };

        Color accent = _uiSettings.GetColorValue(UIColorType.Accent);
        Color accentLight1 = _uiSettings.GetColorValue(UIColorType.AccentLight1);
        Color accentLight2 = _uiSettings.GetColorValue(UIColorType.AccentLight2);
        Color accentDark1 = _uiSettings.GetColorValue(UIColorType.AccentDark1);

        Color highlightAccent = isDark ? accentLight1 : accent;
        double luminance = (0.299 * highlightAccent.R + 0.587 * highlightAccent.G + 0.114 * highlightAccent.B) / 255.0;
        Color textOnAccent = luminance > 0.58
            ? Color.FromArgb(255, 12, 12, 14)
            : Color.FromArgb(255, 250, 250, 250);

        if (isDark)
        {
            return new DynamicPalette(
                IsDark: true,
                Accent: highlightAccent,
                AccentLight1: accentLight1,
                AccentLight2: accentLight2,
                AccentDark1: accentDark1,
                TextPrimary: Color.FromArgb(255, 248, 250, 252),
                TextSecondary: Color.FromArgb(210, 148, 163, 184),
                TextOnAccent: textOnAccent,
                SurfaceCardFill: Color.FromArgb(140, 24, 27, 34),
                SurfaceStroke: Color.FromArgb(32, 255, 255, 255),
                WedgeFill: Color.FromArgb(160, 32, 36, 45),
                WedgeStroke: Color.FromArgb(36, 255, 255, 255),
                WheelRingFill: Color.FromArgb(90, 16, 18, 24),
                WheelCenterFill: Color.FromArgb(225, 20, 23, 29));
        }
        else
        {
            return new DynamicPalette(
                IsDark: false,
                Accent: highlightAccent,
                AccentLight1: accentLight1,
                AccentLight2: accentLight2,
                AccentDark1: accentDark1,
                TextPrimary: Color.FromArgb(255, 15, 23, 42),
                TextSecondary: Color.FromArgb(220, 71, 85, 105),
                TextOnAccent: textOnAccent,
                SurfaceCardFill: Color.FromArgb(195, 255, 255, 255),
                SurfaceStroke: Color.FromArgb(24, 0, 0, 0),
                WedgeFill: Color.FromArgb(210, 248, 250, 252),
                WedgeStroke: Color.FromArgb(20, 0, 0, 0),
                WheelRingFill: Color.FromArgb(120, 241, 245, 249),
                WheelCenterFill: Color.FromArgb(235, 255, 255, 255));
        }
    }
}
