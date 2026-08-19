using System.Windows;
using System.Windows.Media.Animation;

namespace WaveLinkBackup.App.Views;

/// <summary>
/// CSS <c>cubic-bezier(x1, y1, x2, y2)</c> as a WPF easing function.
///
/// README's motion table names exactly one curve - <c>cubic-bezier(.2,0,0,1)</c> - and WPF ships
/// no equivalent. Its <see cref="EasingFunctionBase"/> family is a fixed set of named shapes
/// (Cubic, Quadratic, Back, ...) and none of them is this curve; the only built-in way to express
/// an arbitrary bezier is a <c>KeySpline</c> on a key frame, which would mean rewriting every
/// animation in the app as a <c>...UsingKeyFrames</c> and restating the four numbers at each one.
/// One easing function, declared once in Motion.xaml, is the smaller thing.
///
/// <see cref="Solve"/> is public and static because the arithmetic is the part that can be quietly
/// wrong: a bezier's parameter is NOT its x coordinate, and reading y at s = x instead of solving
/// for s gives a curve that is close enough to look plausible and is not the specified one.
/// </summary>
public sealed class CubicBezierEase : EasingFunctionBase
{
    public static readonly DependencyProperty X1Property =
        DependencyProperty.Register(nameof(X1), typeof(double), typeof(CubicBezierEase), new PropertyMetadata(0.2));

    public static readonly DependencyProperty Y1Property =
        DependencyProperty.Register(nameof(Y1), typeof(double), typeof(CubicBezierEase), new PropertyMetadata(0.0));

    public static readonly DependencyProperty X2Property =
        DependencyProperty.Register(nameof(X2), typeof(double), typeof(CubicBezierEase), new PropertyMetadata(0.0));

    public static readonly DependencyProperty Y2Property =
        DependencyProperty.Register(nameof(Y2), typeof(double), typeof(CubicBezierEase), new PropertyMetadata(1.0));

    public CubicBezierEase()
    {
        // A CSS bezier already describes the WHOLE curve, in and out. EasingMode is WPF's way of
        // mirroring a one-sided curve, and any mode but EaseIn would mirror a curve that is already
        // complete - so EaseIn here means "use my curve as written", not "accelerate".
        EasingMode = EasingMode.EaseIn;
    }

    public double X1 { get => (double)GetValue(X1Property); set => SetValue(X1Property, value); }
    public double Y1 { get => (double)GetValue(Y1Property); set => SetValue(Y1Property, value); }
    public double X2 { get => (double)GetValue(X2Property); set => SetValue(X2Property, value); }
    public double Y2 { get => (double)GetValue(Y2Property); set => SetValue(Y2Property, value); }

    /// <summary>
    /// Progress at <paramref name="x"/>, where x is elapsed fraction of the duration.
    ///
    /// Two steps, and the first is the one worth stating: find the curve parameter s for which the
    /// curve's X reaches x, then read the curve's Y at that same s. Newton-Raphson converges in a
    /// handful of iterations for the shallow curves a UI uses; bisection is the fallback for the
    /// pathological ones (a near-vertical segment can send Newton outside [0,1]).
    /// </summary>
    public static double Solve(double x, double x1, double y1, double x2, double y2)
    {
        if (x <= 0) return 0;
        if (x >= 1) return 1;

        var s = x;

        for (var i = 0; i < 8; i++)
        {
            var error = Bezier(s, x1, x2) - x;
            if (Math.Abs(error) < 1e-7) return Bezier(s, y1, y2);

            var slope = Slope(s, x1, x2);
            if (Math.Abs(slope) < 1e-7) break;

            s -= error / slope;
            if (s is < 0 or > 1) break;
        }

        var low = 0d;
        var high = 1d;
        s = x;

        for (var i = 0; i < 32; i++)
        {
            var at = Bezier(s, x1, x2);
            if (Math.Abs(at - x) < 1e-7) break;

            if (at < x) low = s; else high = s;
            s = (low + high) / 2;
        }

        return Bezier(s, y1, y2);
    }

    /// <summary>One axis of a cubic bezier with endpoints pinned at 0 and 1.</summary>
    private static double Bezier(double s, double c1, double c2)
    {
        var inverse = 1 - s;

        return (3 * inverse * inverse * s * c1)
             + (3 * inverse * s * s * c2)
             + (s * s * s);
    }

    private static double Slope(double s, double c1, double c2)
    {
        var inverse = 1 - s;

        return (3 * inverse * inverse * c1)
             + (6 * inverse * s * (c2 - c1))
             + (3 * s * s * (1 - c2));
    }

    protected override double EaseInCore(double normalizedTime) =>
        Solve(normalizedTime, X1, Y1, X2, Y2);

    protected override Freezable CreateInstanceCore() => new CubicBezierEase();
}
