using System.Windows.Media.Animation;

namespace AIUsageMeter.Windows.Design;

/// <summary>
/// SwiftUI's spring, solved rather than approximated.
/// </summary>
/// <remarks>
/// <para>
/// SwiftUI parameterises a spring by <c>response</c> — the undamped period — and
/// <c>dampingFraction</c>. WPF ships no spring at all, and a cubic ease cannot produce the overshoot
/// that makes the rail feel the way it does on macOS, so this solves the damped harmonic oscillator
/// directly:
/// </para>
/// <code>
/// w0   = 2*pi / response
/// wd   = w0 * sqrt(1 - zeta^2)
/// x(t) = 1 - e^(-zeta*w0*t) * (cos(wd*t) + (zeta*w0/wd) * sin(wd*t))
/// </code>
/// <para>
/// Implemented as <see cref="IEasingFunction"/> rather than <c>EasingFunctionBase</c>: the latter is
/// a <c>Freezable</c> whose cloning contract expects dependency properties, which buys nothing here.
/// </para>
/// </remarks>
internal sealed class SpringEase : IEasingFunction
{
    private readonly double _decay;    // zeta * w0
    private readonly double _damped;   // wd
    private readonly double _settling; // seconds

    /// <summary>Creates a spring. Only underdamped springs are supported; the app uses no others.</summary>
    public SpringEase(double response, double dampingFraction)
    {
        if (response <= 0 || double.IsNaN(response))
            throw new ArgumentOutOfRangeException(nameof(response), response, "Response must be positive.");
        if (!(dampingFraction > 0 && dampingFraction < 1))
            throw new ArgumentOutOfRangeException(nameof(dampingFraction), dampingFraction,
                "Only underdamped springs are solved; every spring in the app has damping between 0 and 1.");

        Response = response;
        DampingFraction = dampingFraction;

        var natural = 2 * Math.PI / response;
        _decay = dampingFraction * natural;
        _damped = natural * Math.Sqrt(1 - dampingFraction * dampingFraction);
        _settling = Math.Log(1000) / _decay;
        Settling = TimeSpan.FromSeconds(_settling);
    }

    public double Response { get; }
    public double DampingFraction { get; }

    /// <summary>How long the spring takes to settle within a thousandth of its target.</summary>
    public TimeSpan Settling { get; }

    /// <summary>Progress towards the target at <paramref name="seconds"/> after release.</summary>
    public double Progress(double seconds)
    {
        if (seconds <= 0) return 0;
        var envelope = Math.Exp(-_decay * seconds);
        return 1 - envelope * (Math.Cos(_damped * seconds) + _decay / _damped * Math.Sin(_damped * seconds));
    }

    public double Ease(double normalizedTime)
    {
        if (normalizedTime <= 0) return 0;
        // WPF reads the easing at exactly 1 to set an animation's final value, and the curve is still
        // a thousandth off its target there. Land on it, or the property never quite arrives.
        if (normalizedTime >= 1) return 1;
        return Progress(normalizedTime * _settling);
    }
}

/// <summary>Named animations, mirroring <c>Motion</c> in <c>Sources/AIUsageMeter/Design.swift</c>.</summary>
internal static class Motion
{
    public static readonly TimeSpan OpenDelay = TimeSpan.FromMilliseconds(30);
    public static readonly TimeSpan DismissDelay = TimeSpan.FromMilliseconds(240);
    public static readonly TimeSpan IdleDelay = TimeSpan.FromSeconds(8);

    /// <summary>One turn of the refresh sweep.</summary>
    public static readonly TimeSpan Sweep = TimeSpan.FromSeconds(0.9);

    /// <summary>How long the sweep takes to fade out once a refresh finishes.</summary>
    public static readonly TimeSpan SweepStop = TimeSpan.FromSeconds(0.18);

    private static readonly SpringEase RevealSpring = new(0.30, 0.82);
    private static readonly SpringEase GeometrySpring = new(0.24, 0.85);
    private static readonly SpringEase ValueSpring = new(0.45, 0.90);
    private static readonly IEasingFunction Gentle = FrozenEaseOut();

    public static IEasingFunction Reveal(bool reduced) => reduced ? Gentle : RevealSpring;
    public static IEasingFunction Geometry(bool reduced) => reduced ? Gentle : GeometrySpring;
    public static IEasingFunction Value(bool reduced) => reduced ? Gentle : ValueSpring;

    /// <summary>How long the matching <see cref="Reveal"/> animation should run for.</summary>
    public static TimeSpan RevealDuration(bool reduced) => reduced ? TimeSpan.FromSeconds(0.14) : RevealSpring.Settling;
    public static TimeSpan GeometryDuration(bool reduced) => reduced ? TimeSpan.FromSeconds(0.11) : GeometrySpring.Settling;
    public static TimeSpan ValueDuration(bool reduced) => reduced ? TimeSpan.FromSeconds(0.16) : ValueSpring.Settling;

    private static CubicEase FrozenEaseOut()
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        ease.Freeze();
        return ease;
    }
}
