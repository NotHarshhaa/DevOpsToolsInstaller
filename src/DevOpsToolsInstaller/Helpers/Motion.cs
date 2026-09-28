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
        if (d is not FrameworkElement el)
        {
            return;
        }

        if ((bool)e.NewValue)
        {
            el.PointerEntered += OnHoverLiftEntered;
            el.PointerExited += OnHoverLiftLeft;
            el.PointerCanceled += OnHoverLiftLeft;
            el.SizeChanged += OnHoverLiftSizeChanged;
            el.Unloaded += OnHoverLiftUnloaded;
        }
        else
        {
            el.PointerEntered -= OnHoverLiftEntered;
            el.PointerExited -= OnHoverLiftLeft;
            el.PointerCanceled -= OnHoverLiftLeft;
            el.SizeChanged -= OnHoverLiftSizeChanged;
            el.Unloaded -= OnHoverLiftUnloaded;
        }
    }

    private static void OnHoverLiftEntered(object sender, PointerRoutedEventArgs e)
        => AnimateScale((UIElement)sender, 1.02f);

    private static void OnHoverLiftLeft(object sender, PointerRoutedEventArgs e)
        => AnimateScale((UIElement)sender, 1f);

    private static void OnHoverLiftSizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Scale around the element's center so the lift feels balanced.
        var visual = ElementCompositionPreview.GetElementVisual((UIElement)sender);
        visual.CenterPoint = new Vector3((float)e.NewSize.Width / 2f, (float)e.NewSize.Height / 2f, 0f);
    }

    private static void OnHoverLiftUnloaded(object sender, RoutedEventArgs e)
        => AnimateScale((UIElement)sender, 1f);

    private static void AnimateScale(UIElement el, float target)
    {
        var visual = ElementCompositionPreview.GetElementVisual(el);
        var compositor = visual.Compositor;

        // Back-out bezier (y overshoots 1.0) gives the springy settle that
        // natural-motion springs provide, without requiring spring factories.
        var scale = compositor.CreateVector3KeyFrameAnimation();
        scale.InsertKeyFrame(1f, new Vector3(target, target, 1f),
            compositor.CreateCubicBezierEasingFunction(
                new Vector2(0.175f, 0.885f), new Vector2(0.32f, 1.5f)));
        scale.Duration = TimeSpan.FromMilliseconds(200);

        visual.StartAnimation("Scale", scale);
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
