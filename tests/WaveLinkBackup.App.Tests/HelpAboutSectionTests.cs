using WaveLinkBackup.App.Updates;
using WaveLinkBackup.App.ViewModels;

namespace WaveLinkBackup.App.Tests;

/// <summary>
/// The About section at the top of Help.
///
/// <para>
/// Composed from <see cref="AboutDialogModel"/> rather than restated, and that is the whole point
/// of these tests: two copies of a version number are two copies that can disagree. The About
/// dialog already reads the version from the assembly so it cannot drift from the release tag, and
/// Help now inherits that rather than opening a second way to be wrong.
/// </para>
/// </summary>
public sealed class HelpAboutSectionTests
{
    private static HelpSection About() => HelpDialogModel.Build().Sections[0];

    [Fact]
    public void It_is_the_first_section()
    {
        // Someone opening Help to find out what the app even is should not have to close it and
        // open About instead.
        Assert.StartsWith("About ", About().Heading, StringComparison.Ordinal);
    }

    [Fact]
    public void It_carries_the_standard_four_facts()
    {
        var about = AboutDialogModel.Build();
        var section = About();

        Assert.Contains(about.AppName, section.Heading, StringComparison.Ordinal);
        Assert.Contains(about.Version, section.Body, StringComparison.Ordinal);
        Assert.Contains(about.LicenceLine, section.Body, StringComparison.Ordinal);
        Assert.Contains(about.Description, section.Body, StringComparison.Ordinal);
        Assert.Contains(about.AffiliationLine, section.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void The_version_is_the_running_builds_own()
    {
        // Not a literal. This is what makes the number in Help, the number in About, the number the
        // updater compares against and the release tag one number.
        Assert.Contains(
            ReleaseVersion.Display(ReleaseVersion.Current), About().Body, StringComparison.Ordinal);
    }

    [Fact]
    public void A_different_about_model_moves_the_section_with_it()
    {
        // The seam that keeps them from drifting: Help does not know these strings, it is handed
        // them. If someone reverts this to hard-coded copy, this is the test that stops passing.
        var invented = AboutDialogModel.Build() with
        {
            AppName = "Something Else",
            Version = "9.9.9",
            LicenceLine = "A licence nobody uses",
        };

        var section = HelpDialogModel.Build(invented).Sections[0];

        Assert.Equal("About Something Else", section.Heading);
        Assert.Contains("9.9.9", section.Body, StringComparison.Ordinal);
        Assert.Contains("A licence nobody uses", section.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void The_rest_of_help_is_untouched_and_still_in_order()
    {
        // Adding a section must not reorder or drop the four that were there.
        var headings = HelpDialogModel.Build().Sections.Select(s => s.Heading).ToList();

        Assert.Equal(5, headings.Count);
        Assert.Equal(
        [
            "What gets backed up",
            "How snapshots are kept",
            "How restoring works",
            "The tray icon",
        ], headings.Skip(1));
    }

    [Fact]
    public void Every_section_says_something()
    {
        Assert.All(HelpDialogModel.Build().Sections, section =>
        {
            Assert.NotEmpty(section.Heading);
            Assert.NotEmpty(section.Body);
        });
    }
}
