using System.IO;
using System.Text.RegularExpressions;
using WaveLinkBackup.App.Views;

namespace WaveLinkBackup.App.Tests;

/// <summary>
/// README's motion rules: 140ms hover, 220ms state change, easing cubic-bezier(.2,0,0,1), no
/// bounce, no slide.
///
/// Two halves, and the first is the one that can be quietly wrong. A bezier's PARAMETER is not its
/// x coordinate; reading y at s = x instead of solving for s produces a curve that looks plausible
/// and is not the specified one, and no eye would ever catch the difference on a 140ms fade. So the
/// solver is pinned numerically. The second half is a source scan, because the failure mode for the
/// timings is not a wrong curve - it is the next animation someone adds with a hand-typed 300ms and
/// no easing at all.
/// </summary>
public sealed class MotionTests
{
    // The design's curve.
    private const double X1 = 0.2, Y1 = 0.0, X2 = 0.0, Y2 = 1.0;

    private static double Ease(double x) => CubicBezierEase.Solve(x, X1, Y1, X2, Y2);

    [Fact]
    public void The_curve_starts_at_zero_and_ends_at_one()
    {
        Assert.Equal(0, Ease(0));
        Assert.Equal(1, Ease(1));

        // Out-of-range input is clamped, not extrapolated: an animation that overshoots would be
        // the "bounce" the design forbids.
        Assert.Equal(0, Ease(-0.5));
        Assert.Equal(1, Ease(1.5));
    }

    [Fact]
    public void The_curve_never_goes_backwards()
    {
        var previous = -1d;

        for (var i = 0; i <= 100; i++)
        {
            var value = Ease(i / 100d);

            Assert.True(value >= previous, $"Progress fell from {previous} to {value} at t={i / 100d}.");
            Assert.InRange(value, 0, 1);
            previous = value;
        }
    }

    /// <summary>
    /// The point of this curve: it is a hard ease-OUT. Most of the distance is covered in the first
    /// fifth of the time, which is what makes a hover feel like it responds instantly and still
    /// settles rather than snapping. These two figures come from solving the bezier by hand -
    /// x(s) = 0.6s(1-s)² + s³, y(s) = 3s² - 2s³ - so they pin the curve, not the implementation.
    /// </summary>
    [Fact]
    public void One_fifth_of_the_time_covers_half_the_distance()
    {
        // s = 0.5 puts x at exactly 0.2 and y at exactly 0.5.
        Assert.InRange(Ease(0.2), 0.49, 0.51);

        // By the halfway point it is nearly there: s ≈ 0.782, y ≈ 0.878.
        Assert.InRange(Ease(0.5), 0.86, 0.89);
    }

    /// <summary>
    /// The solver against a curve whose answer is known independently: cubic-bezier(1/3, 1/3, 2/3,
    /// 2/3) is the identity, so any x must come back as itself. A solver that returned y(x) rather
    /// than y(s(x)) also passes this one - which is why the ease-out figures above exist too.
    /// </summary>
    [Theory]
    [InlineData(0.1)]
    [InlineData(0.25)]
    [InlineData(0.5)]
    [InlineData(0.75)]
    [InlineData(0.9)]
    public void The_linear_bezier_is_the_identity(double x)
    {
        Assert.InRange(CubicBezierEase.Solve(x, 1 / 3d, 1 / 3d, 2 / 3d, 2 / 3d), x - 0.001, x + 0.001);
    }

    // ------------------------------------------------------------------ the source scan

    private static readonly string[] AnimatedFiles = ["ControlStyles.xaml", "RowStyles.xaml"];

    private static IEnumerable<(string File, string Animation)> Animations()
    {
        foreach (var name in AnimatedFiles)
        {
            var text = File.ReadAllText(Path.Combine(AppResources.SourceRoot, "Views", name));

            foreach (Match match in Regex.Matches(text, "<DoubleAnimation\\b.*?/>", RegexOptions.Singleline))
            {
                yield return (name, match.Value);
            }
        }
    }

    [Fact]
    public void There_is_motion_to_check_at_all()
    {
        // A source scan over an empty set passes vacuously, which is the one way this whole file
        // could go green while the app animated nothing.
        Assert.True(Animations().Count() >= 12,
            "Far fewer animations than the six templates plus the row expansion should carry.");
    }

    [Fact]
    public void Every_animation_uses_one_of_the_two_specified_durations()
    {
        var offenders = Animations()
            .Where(a => !a.Animation.Contains("Duration=\"0:0:0.14\"", StringComparison.Ordinal)
                     && !a.Animation.Contains("Duration=\"0:0:0.22\"", StringComparison.Ordinal)
                     // The expansion's reset. Instant on purpose - see RowStyles' own comment.
                     && !a.Animation.Contains("Duration=\"0:0:0\"", StringComparison.Ordinal))
            .Select(a => $"  {a.File}: {Compact(a.Animation)}")
            .ToArray();

        Assert.True(offenders.Length == 0,
            $"README gives two durations - 140ms for hover, 220ms for a state change. Found:" +
            $"{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    [Fact]
    public void Every_animation_that_takes_time_uses_the_shared_easing()
    {
        var offenders = Animations()
            // A zero-duration reset has no curve to follow.
            .Where(a => !a.Animation.Contains("Duration=\"0:0:0\"", StringComparison.Ordinal))
            .Where(a => !a.Animation.Contains("EasingFunction=\"{StaticResource WlStandardEase}\"", StringComparison.Ordinal))
            .Select(a => $"  {a.File}: {Compact(a.Animation)}")
            .ToArray();

        Assert.True(offenders.Length == 0,
            $"The design names one curve, cubic-bezier(.2,0,0,1), declared once as WlStandardEase " +
            $"in Motion.xaml. A linear animation is a different design. Found:" +
            $"{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    /// <summary>
    /// "No bounce" is a rule about which easing functions are allowed to exist, not only about
    /// which are used - WPF ships four that overshoot and any of them would violate it.
    /// </summary>
    [Fact]
    public void Nothing_in_the_app_uses_an_overshooting_easing()
    {
        var banned = new[] { "BackEase", "ElasticEase", "BounceEase" };

        var offenders = (
            from file in Directory.EnumerateFiles(AppResources.SourceRoot, "*.xaml", SearchOption.AllDirectories)
            where !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
               && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
            let text = File.ReadAllText(file)
            from name in banned
            where text.Contains(name, StringComparison.Ordinal)
            select $"  {Path.GetFileName(file)}: {name}").ToArray();

        Assert.True(offenders.Length == 0,
            $"README: \"No bounce, no slide.\" Found:{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    private static string Compact(string animation) =>
        Regex.Replace(animation, "\\s+", " ").Trim();
}
