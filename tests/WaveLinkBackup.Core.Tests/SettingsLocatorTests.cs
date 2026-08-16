using WaveLinkBackup.Core.Discovery;
using WaveLinkBackup.Core.Results;
using WaveLinkBackup.Core.Tests.Fakes;

namespace WaveLinkBackup.Core.Tests;

/// <summary>
/// Discovery is where the project's most expensive bug lives: a tool that finds the stale
/// vendor folder reports success and protects nothing.
/// See _docs/knowledge-base/gotchas/backup-succeeds-but-protects-nothing.md
/// </summary>
public sealed class SettingsLocatorTests
{
    private const string LocalAppData = @"C:\Users\test\AppData\Local";
    private const string RoamingAppData = @"C:\Users\test\AppData\Roaming";
    private const string Package = @"C:\Users\test\AppData\Local\Packages\Elgato.WaveLink_g54w8ztgkx496";
    private const string RealSettings = Package + @"\LocalState\Settings.json";

    /// <summary>The decoy: populated, plausible, and nine months stale.</summary>
    private const string DecoySettings = RoamingAppData + @"\Elgato\WaveLink\Settings.json";

    private static SettingsLocator Locator(FakeFileSystem fs) => new(fs, LocalAppData);

    [Fact]
    public void Finds_the_package_under_LocalState()
    {
        var fs = new FakeFileSystem().AddFile(RealSettings, "{}");

        var result = Locator(fs).Locate();

        Assert.True(result.IsSuccess);
        Assert.Equal(RealSettings, result.Value.SettingsPath);
        Assert.Equal("Elgato.WaveLink_g54w8ztgkx496", result.Value.PackageFamilyName);
        Assert.Equal(Package + @"\LocalState", result.Value.LocalStatePath);
        Assert.Equal(Package + @"\LocalState\Logs", result.Value.LogsPath);
    }

    [Fact]
    public void Ignores_the_decoy_vendor_folder_even_when_it_holds_a_settings_file()
    {
        // THE regression guard. If someone later adds a "fallback location", this fails.
        var fs = new FakeFileSystem()
            .AddFile(DecoySettings, """{"MixerConfiguration":{}}""")
            .AddFile(RealSettings, "{}");

        var result = Locator(fs).Locate();

        Assert.True(result.IsSuccess);
        Assert.Equal(RealSettings, result.Value.SettingsPath);
        Assert.DoesNotContain("Roaming", result.Value.SettingsPath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_decoy_alone_is_not_an_installation()
    {
        var fs = new FakeFileSystem().AddFile(DecoySettings, "{}");

        Assert.IsType<WaveLinkNotInstalled>(Locator(fs).Locate().Error);
    }

    [Fact]
    public void A_package_directory_without_a_settings_file_does_not_count()
    {
        var fs = new FakeFileSystem().AddDirectory(Package + @"\LocalState");

        Assert.IsType<WaveLinkNotInstalled>(Locator(fs).Locate().Error);
    }

    [Fact]
    public void No_packages_at_all_reports_not_installed()
    {
        Assert.IsType<WaveLinkNotInstalled>(Locator(new FakeFileSystem()).Locate().Error);
    }

    [Fact]
    public void Refuses_to_guess_between_multiple_packages()
    {
        var other = LocalAppData + @"\Packages\Elgato.WaveLink_otherid00000\LocalState\Settings.json";
        var fs = new FakeFileSystem().AddFile(RealSettings, "{}").AddFile(other, "{}");

        var error = Assert.IsType<MultiplePackagesFound>(Locator(fs).Locate().Error);
        Assert.Equal(2, error.Candidates.Count);
    }

    [Fact]
    public void The_family_name_is_globbed_not_hard_coded()
    {
        var renamed = LocalAppData + @"\Packages\Elgato.WaveLink_futureid1234\LocalState\Settings.json";
        var fs = new FakeFileSystem().AddFile(renamed, "{}");

        Assert.True(Locator(fs).Locate().IsSuccess);
    }

    [Fact]
    public void Unrelated_packages_are_not_candidates()
    {
        var fs = new FakeFileSystem()
            .AddFile(LocalAppData + @"\Packages\Elgato.StreamDeck_abc\LocalState\Settings.json", "{}");

        Assert.IsType<WaveLinkNotInstalled>(Locator(fs).Locate().Error);
    }

    [Fact]
    public void An_explicit_path_bypasses_discovery_entirely()
    {
        // Diverges from upstream, which requires the override to match a discovered
        // candidate. Bypassing is the only thing that helps a user whose install we cannot
        // find - the possible non-MSIX case. technical-debt.md 2.2.
        var elsewhere = @"D:\rescued\Settings.json";
        var fs = new FakeFileSystem().AddFile(elsewhere, "{}");

        var result = Locator(fs).Locate(elsewhere);

        Assert.True(result.IsSuccess);
        Assert.Equal(elsewhere, result.Value.SettingsPath);
    }

    [Fact]
    public void An_explicit_path_that_does_not_exist_is_an_error()
    {
        var result = Locator(new FakeFileSystem()).Locate(@"D:\nope\Settings.json");

        Assert.IsType<SettingsUnreadable>(result.Error);
    }

    [Fact]
    public void An_explicit_path_wins_over_an_installed_package()
    {
        var elsewhere = @"D:\rescued\Settings.json";
        var fs = new FakeFileSystem().AddFile(RealSettings, "{}").AddFile(elsewhere, "{}");

        Assert.Equal(elsewhere, Locator(fs).Locate(elsewhere).Value.SettingsPath);
    }
}
