using System;
using System.Numerics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Composition;

namespace DevOpsToolsInstaller.Helpers;

/// <summary>
/// Lightweight Fluent motion helpers exposed as attached properties, so pages
/// can opt into consistent micro-animations without code-behind:
///
///   helpers:Motion.HoverLift="True"   — springy scale-up on pointer hover
///   helpers:Motion.Pulse="True"       — gentle looping opacity pulse (badges,
///                                       loading captions)
///
/// Both effects run on the composition thread and respect the element's
/// existing visual state.
/// </summary>
public static class Motion
{
    // ── HoverLift: spring scale on pointer over ─────────────────────────

    public static readonly DependencyProperty HoverLiftProperty =
        DependencyProperty.RegisterAttached(
            "HoverLift", typeof(bool), typeof(Motion),
            new PropertyMetadata(false, OnHoverLiftChanged));

    public static bool GetHoverLift(UIElement obj) => (bool)obj.GetValue(HoverLiftProperty);
    public static void SetHoverLift(UIElement obj, bool value) => obj.SetValue(HoverLiftProperty, value);

    private static void OnHoverLiftChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        // Scaling composition visuals (e.g. 1.02f) breaks DirectWrite subpixel pixel-grid snapping
        // and triggers bilinear texture filtering in DirectComposition, causing text to look blurry/fuzzy.
        // Disabled so all text, badges, and controls remain 100% sharp and crisp on both hover and focus.
    }

    // ── Pulse: gentle looping opacity pulse ─────────────────────────────

    public static readonly DependencyProperty PulseProperty =
        DependencyProperty.RegisterAttached(
            "Pulse", typeof(bool), typeof(Motion),
            new PropertyMetadata(false, OnPulseChanged));

    public static bool GetPulse(UIElement obj) => (bool)obj.GetValue(PulseProperty);
    public static void SetPulse(UIElement obj, bool value) => obj.SetValue(PulseProperty, value);

    private static void OnPulseChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement el || !(bool)e.NewValue)
        {
            return;
        }

        RoutedEventHandler? onLoaded = null;
        onLoaded = (s, _) =>
        {
            el.Loaded -= onLoaded;
            StartPulse(el);
        };
        el.Loaded += onLoaded;
    }

    private static void StartPulse(UIElement el)
    {
        // Respect the Windows "reduce animations" accessibility setting.
        if (!AnimationSettingsHelper.AnimationsEnabled) return;

        var visual = ElementCompositionPreview.GetElementVisual(el);
        var compositor = visual.Compositor;

        var pulse = compositor.CreateScalarKeyFrameAnimation();
        pulse.InsertKeyFrame(0.0f, 1f);
        pulse.InsertKeyFrame(0.5f, 0.6f,
            compositor.CreateCubicBezierEasingFunction(new Vector2(0.4f, 0f), new Vector2(0.2f, 1f)));
        pulse.InsertKeyFrame(1.0f, 1f);
        pulse.Duration = TimeSpan.FromMilliseconds(1600);
        pulse.IterationBehavior = AnimationIterationBehavior.Forever;

        visual.StartAnimation("Opacity", pulse);
    }
}
