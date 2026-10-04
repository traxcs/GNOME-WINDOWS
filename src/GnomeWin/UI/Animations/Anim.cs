using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using GnomeWin.Services.Settings;

namespace GnomeWin.UI.Animations;

public static class Anim
{
    private static double _factor = 1.0;

    public static void Configure(bool enabled, AnimationSpeed speed)
    {
        _factor = !enabled ? 0 : speed switch
        {
            AnimationSpeed.Slow => 1.6,
            AnimationSpeed.Fast => 0.6,
            _ => 1.0,
        };
    }

    public static bool Enabled => _factor > 0;

    public static TimeSpan Duration(int ms) => TimeSpan.FromMilliseconds(ms * _factor);
    public static int Ms(int ms) => (int)(ms * _factor);

    public static readonly IEasingFunction EaseOut = new CubicEase { EasingMode = EasingMode.EaseOut };
    public static readonly IEasingFunction EaseInOut = new CubicEase { EasingMode = EasingMode.EaseInOut };

    public static double EaseOutCubic(double t) => 1 - Math.Pow(1 - t, 3);
    public static double EaseInOutCubic(double t) => t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;

    public static void Fade(UIElement e, double to, int ms, Action? completed = null)
    {
        if (!Enabled || ms <= 0)
        {
            e.BeginAnimation(UIElement.OpacityProperty, null);
            e.Opacity = to;
            completed?.Invoke();
            return;
        }
        var a = new DoubleAnimation(to, Duration(ms)) { EasingFunction = EaseOut };
        if (completed != null) a.Completed += (_, _) => completed();
        e.BeginAnimation(UIElement.OpacityProperty, a);
    }

    public static void Animate(Animatable target, DependencyProperty dp, double to, int ms, IEasingFunction? easing = null, Action? completed = null)
    {
        if (!Enabled || ms <= 0)
        {
            target.BeginAnimation(dp, null);
            target.SetValue(dp, to);
            completed?.Invoke();
            return;
        }
        var a = new DoubleAnimation(to, Duration(ms)) { EasingFunction = easing ?? EaseOut };
        if (completed != null) a.Completed += (_, _) => completed();
        target.BeginAnimation(dp, a);
    }

    public static void Animate(UIElement target, DependencyProperty dp, double to, int ms, IEasingFunction? easing = null, Action? completed = null)
    {
        if (!Enabled || ms <= 0)
        {
            target.BeginAnimation(dp, null);
            target.SetValue(dp, to);
            completed?.Invoke();
            return;
        }
        var a = new DoubleAnimation(to, Duration(ms)) { EasingFunction = easing ?? EaseOut };
        if (completed != null) a.Completed += (_, _) => completed();
        target.BeginAnimation(dp, a);
    }
}

public sealed class FrameAnimation
{
    private readonly Stopwatch _clock = new();
    private readonly Action<double> _onFrame;
    private Action? _onCompleted;
    private double _from, _to, _durationMs;
    private bool _running;

    public FrameAnimation(Action<double> onFrame) => _onFrame = onFrame;

    public static int Running { get; private set; }

    public double Value { get; private set; }
    public bool IsRunning => _running;

    public void Start(double from, double to, int durationMs, Action? completed = null)
    {
        _from = from;
        _to = to;
        _durationMs = Anim.Ms(durationMs);
        _onCompleted = completed;
        _clock.Restart();
        if (_durationMs <= 0 || Math.Abs(to - from) < 0.0001)
        {
            Stop();
            Value = to;
            _onFrame(to);
            completed?.Invoke();
            return;
        }
        Value = from;
        if (!_running)
        {
            _running = true;
            Running++;
            CompositionTarget.Rendering += OnRendering;
        }
        _onFrame(from);
    }

    public void Stop()
    {
        if (!_running) return;
        _running = false;
        Running--;
        CompositionTarget.Rendering -= OnRendering;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        double t = Math.Clamp(_clock.Elapsed.TotalMilliseconds / _durationMs, 0, 1);
        Value = _from + (_to - _from) * t;
        _onFrame(Value);
        if (t >= 1)
        {
            Stop();
            var c = _onCompleted;
            _onCompleted = null;
            c?.Invoke();
        }
    }
}
