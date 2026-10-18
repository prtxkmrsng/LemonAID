using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;

namespace LemonAID.Services;

public sealed class WedgeVisualNode
{
    public required Path WedgePath { get; init; }
    public required StackPanel LabelStack { get; init; }
    public required FontIcon Icon { get; init; }
    public required TextBlock Label { get; init; }
    public required CompositeTransform WedgeTransform { get; init; }
    public required CompositeTransform LabelTransform { get; init; }
    public required double MidAngleRadians { get; init; }
}

public static class WheelAnimationHelper
{
    public static void AnimateWheelSummon(UIElement wheelRoot, CompositeTransform rootTransform, CompositeTransform? centerTransform)
    {
        if (!BackgroundSettingsService.Current.AnimationsEnabled)
        {
            wheelRoot.Opacity = 1.0;
            rootTransform.ScaleX = 1.0;
            rootTransform.ScaleY = 1.0;
            rootTransform.Rotation = 0.0;
            return;
        }

        var sb = new Storyboard();
        var duration = new Duration(TimeSpan.FromMilliseconds(220));
        var springEase = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.45 };

        AddDoubleAnimation(sb, rootTransform, "ScaleX", 0.58, 1.0, duration, springEase);
        AddDoubleAnimation(sb, rootTransform, "ScaleY", 0.58, 1.0, duration, springEase);
        AddDoubleAnimation(sb, rootTransform, "Rotation", -14.0, 0.0, duration, springEase);
        AddDoubleAnimation(sb, wheelRoot, "Opacity", 0.0, 1.0, new Duration(TimeSpan.FromMilliseconds(140)), null);

        if (centerTransform != null)
        {
            AddDoubleAnimation(sb, centerTransform, "ScaleX", 0.72, 1.0, duration, springEase);
            AddDoubleAnimation(sb, centerTransform, "ScaleY", 0.72, 1.0, duration, springEase);
        }

        sb.Begin();
    }

    public static void AnimateModeSwitch(CompositeTransform wheelTransform, CompositeTransform? centerTransform)
    {
        if (!BackgroundSettingsService.Current.AnimationsEnabled)
        {
            wheelTransform.ScaleX = 1.0;
            wheelTransform.ScaleY = 1.0;
            wheelTransform.Rotation = 0.0;
            return;
        }

        var sb = new Storyboard();
        var duration = new Duration(TimeSpan.FromMilliseconds(175));
        var backEase = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.5 };

        AddDoubleAnimation(sb, wheelTransform, "ScaleX", 0.90, 1.0, duration, backEase);
        AddDoubleAnimation(sb, wheelTransform, "ScaleY", 0.90, 1.0, duration, backEase);
        AddDoubleAnimation(sb, wheelTransform, "Rotation", -9.0, 0.0, duration, backEase);

        if (centerTransform != null)
        {
            AddDoubleAnimation(sb, centerTransform, "ScaleX", 0.86, 1.0, duration, backEase);
            AddDoubleAnimation(sb, centerTransform, "ScaleY", 0.86, 1.0, duration, backEase);
        }

        sb.Begin();
    }

    public static void UpdateWedgeSelection(
        WedgeVisualNode node,
        bool isSelected,
        DynamicPalette palette)
    {
        node.WedgePath.Fill = new SolidColorBrush(isSelected ? palette.Accent : palette.WedgeFill);
        node.WedgePath.Stroke = new SolidColorBrush(isSelected ? palette.AccentLight2 : palette.WedgeStroke);
        node.WedgePath.StrokeThickness = isSelected ? 2.2 : 1.5;

        node.Icon.Foreground = new SolidColorBrush(isSelected ? palette.TextOnAccent : palette.Accent);
        node.Label.Foreground = new SolidColorBrush(isSelected ? palette.TextOnAccent : palette.TextPrimary);

        double radialDistance = isSelected ? 8.5 : 0.0;
        double targetTx = Math.Cos(node.MidAngleRadians) * radialDistance;
        double targetTy = Math.Sin(node.MidAngleRadians) * radialDistance;
        double targetScale = isSelected ? 1.045 : 1.0;

        if (!BackgroundSettingsService.Current.AnimationsEnabled)
        {
            node.WedgeTransform.TranslateX = targetTx;
            node.WedgeTransform.TranslateY = targetTy;
            node.WedgeTransform.ScaleX = targetScale;
            node.WedgeTransform.ScaleY = targetScale;
            node.LabelTransform.TranslateX = targetTx;
            node.LabelTransform.TranslateY = targetTy;
            node.LabelTransform.ScaleX = targetScale;
            node.LabelTransform.ScaleY = targetScale;
            return;
        }

        var sb = new Storyboard();
        var duration = new Duration(TimeSpan.FromMilliseconds(isSelected ? 150 : 130));
        EasingFunctionBase ease = isSelected
            ? new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.38 }
            : new CubicEase { EasingMode = EasingMode.EaseOut };

        AddDoubleAnimation(sb, node.WedgeTransform, "TranslateX", null, targetTx, duration, ease);
        AddDoubleAnimation(sb, node.WedgeTransform, "TranslateY", null, targetTy, duration, ease);
        AddDoubleAnimation(sb, node.WedgeTransform, "ScaleX", null, targetScale, duration, ease);
        AddDoubleAnimation(sb, node.WedgeTransform, "ScaleY", null, targetScale, duration, ease);

        AddDoubleAnimation(sb, node.LabelTransform, "TranslateX", null, targetTx, duration, ease);
        AddDoubleAnimation(sb, node.LabelTransform, "TranslateY", null, targetTy, duration, ease);
        AddDoubleAnimation(sb, node.LabelTransform, "ScaleX", null, targetScale, duration, ease);
        AddDoubleAnimation(sb, node.LabelTransform, "ScaleY", null, targetScale, duration, ease);

        sb.Begin();
    }

    public static void AnimateWedgeExecutionPulse(WedgeVisualNode node)
    {
        if (!BackgroundSettingsService.Current.AnimationsEnabled) return;

        var sb = new Storyboard();
        var duration = new Duration(TimeSpan.FromMilliseconds(130));
        var ease = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.6 };

        AddDoubleAnimation(sb, node.WedgeTransform, "ScaleX", 0.94, 1.06, duration, ease);
        AddDoubleAnimation(sb, node.WedgeTransform, "ScaleY", 0.94, 1.06, duration, ease);
        sb.Begin();
    }

    private static void AddDoubleAnimation(
        Storyboard sb,
        DependencyObject target,
        string propertyPath,
        double? from,
        double to,
        Duration duration,
        EasingFunctionBase? easing)
    {
        var anim = new DoubleAnimation
        {
            From = from,
            To = to,
            Duration = duration,
            EasingFunction = easing
        };
        Storyboard.SetTarget(anim, target);
        Storyboard.SetTargetProperty(anim, propertyPath);
        sb.Children.Add(anim);
    }
}
