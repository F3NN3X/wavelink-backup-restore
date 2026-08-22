using System.Windows;
using WaveLinkBackup.App.Theming;
using WaveLinkBackup.App.ViewModels;
using WaveLinkBackup.App.Views;

namespace WaveLinkBackup.App.Tests;

/// <summary>
/// The about dialog's view, forced through a real layout pass. The companion to
/// <see cref="HelpDialogViewTests"/>: the model is built once from the running assembly, so this
/// file guards the Window itself - that it parses, resolves its resources under App.xaml's real
/// merge order, and lays out - plus the two facts only a rendered tree can prove: that the version
/// line carries the real build's number, and that the not-affiliated line is actually on screen.
/// </summary>
public sealed class AboutDialogViewTests
{
    private static void ShowAndAssert(AppTheme theme, Action<FrameworkElement> assert) => Wpf.Run(() =>
    {
        AppResources.Load(theme);

        // An explicit size: DialogOverlay only sizes this window when it has an owner, and a layout
        // assertion needs a deterministic one. Wide enough for the 420px card plus its margins.
        var dialog = new AboutDialog(AboutDialogModel.Build())
        {
            Width = 800,
            Height = 700,
            Left = -3000,
            Top = -3000,
            ShowInTaskbar = false,
        };

        dialog.Show();
        dialog.UpdateLayout();

        try
        {
            foreach (var element in AppResources.Descendants(dialog)) assert(element);
        }
        finally
        {
            dialog.Close();
        }

        return true;
    });

    [Theory]
    [InlineData(AppTheme.Dark)]
    [InlineData(AppTheme.Light)]
    [InlineData(AppTheme.HighContrast)]
    public void The_dialog_renders_in_every_theme(AppTheme theme)
    {
        // A style-target mismatch, an unresolvable StaticResource or a bad template all throw during
        // the layout pass, so "did not throw" is the assertion this file exists for.
        ShowAndAssert(theme, _ => { });
    }

    /// <summary>
    /// The version line carries the running build's number - read from the rendered tree rather than
    /// the model, so a binding that silently fails to evaluate (the property-element-syntax bug
    /// SettingsDialogViewTests pins) would show as a missing or empty line here.
    /// </summary>
    [Fact]
    public void The_version_line_shows_the_running_builds_version()
    {
        var expected = WaveLinkBackup.App.Updates.ReleaseVersion.Display(
            WaveLinkBackup.App.Updates.ReleaseVersion.Current);

        var versions = new List<string>();

        ShowAndAssert(AppTheme.Dark, e =>
        {
            if (e is System.Windows.Controls.TextBlock block && !string.IsNullOrWhiteSpace(block.Text))
            {
                versions.Add(block.Text);
            }
        });

        Assert.Contains(versions, v => v == expected);
    }

    /// <summary>
    /// The not-affiliated line is legal copy, not decoration - the README's own sentence, pinned so a
    /// copy edit that drops it fails here instead of shipping.
    /// </summary>
    [Fact]
    public void The_not_affiliated_line_is_on_screen()
    {
        var text = new List<string>();

        ShowAndAssert(AppTheme.Dark, e =>
        {
            if (e is System.Windows.Controls.TextBlock block && !string.IsNullOrWhiteSpace(block.Text))
            {
                text.Add(block.Text);
            }
        });

        Assert.Contains(text, t => t.StartsWith("Not affiliated with", StringComparison.Ordinal));
    }

    /// <summary>
    /// The same XAML rule SettingsDialogViewTests pins: a binding written in property-element syntax
    /// never evaluates and prints itself. This reads the rendered text rather than the source.
    /// </summary>
    [Fact]
    public void No_rendered_text_is_an_unevaluated_binding_expression()
    {
        var literals = new List<string>();

        ShowAndAssert(AppTheme.Dark, e =>
        {
            if (e is not System.Windows.Controls.TextBlock block) return;

            foreach (var inline in block.Inlines)
            {
                if (inline is System.Windows.Documents.Run { Text: { } runText } &&
                    runText.Contains("{Binding", StringComparison.Ordinal))
                {
                    literals.Add(runText);
                }
            }

            if (block.Text.Contains("{Binding", StringComparison.Ordinal)) literals.Add(block.Text);
        });

        Assert.True(literals.Count == 0,
            "A binding expression is being shown to the user as text. Use Text=\"{Binding ...}\" " +
            "in attribute syntax, never <Run.Text>{Binding ...}</Run.Text>. Found: " +
            string.Join(" | ", literals.Distinct()));
    }

    /// <summary>
    /// The footer offers exactly one action - OK - and it is the button Escape reaches. No other
    /// verbs: this dialog changes nothing, so there is nothing to save or confirm.
    /// </summary>
    [Fact]
    public void The_footer_says_ok_and_offers_nothing_else()
    {
        var buttonLabels = new List<string>();

        ShowAndAssert(AppTheme.Dark, e =>
        {
            if (e is System.Windows.Controls.Button { Content: string label }) buttonLabels.Add(label);
        });

        Assert.Equal(["OK"], buttonLabels);
    }

    /// <summary>
    /// With no URLs configured both links render nothing at all - a link that points nowhere is
    /// worse than no link (the same rule as App.ReleaseSource's IsConfigured).
    /// </summary>
    [Fact]
    public void Both_links_are_absent_when_no_urls_are_configured()
    {
        var found = new List<string>();

        ShowAndAssert(AppTheme.Dark, e =>
        {
            if (e.Name is "ReleasesLink" or "RepositoryLink" && e is System.Windows.Controls.TextBlock link)
            {
                found.Add(e.Name);
                Assert.Equal(Visibility.Collapsed, link.Visibility);
            }
        });

        Assert.Equal(["ReleasesLink", "RepositoryLink"], found);
    }

    /// <summary>
    /// With URLs configured both links render their labels and stay visible - the other half of the
    /// collapse rule above.
    /// </summary>
    [Fact]
    public void Both_links_render_when_urls_are_configured()
    {
        var model = new AboutDialogModel(
            Title: "About",
            AppName: "Wave Link Backup",
            Version: "0.7.1",
            Description: "A free, open-source Windows utility.",
            LicenceLine: "MIT licence",
            AffiliationLine: "Not affiliated with Elgato.",
            ReleasesLabel: "Releases",
            ReleasesUrl: "https://example.com/releases",
            RepositoryLabel: "Source code",
            RepositoryUrl: "https://example.com/repo");

        Wpf.Run(() =>
        {
            AppResources.Load(AppTheme.Dark);

            var dialog = new AboutDialog(model)
            {
                Width = 800,
                Height = 700,
                Left = -3000,
                Top = -3000,
                ShowInTaskbar = false,
            };

            dialog.Show();
            dialog.UpdateLayout();

            try
            {
                var links = AppResources.Descendants(dialog)
                    .OfType<System.Windows.Controls.TextBlock>()
                    .Where(e => e.Name is "ReleasesLink" or "RepositoryLink")
                    .ToList();

                Assert.Equal(2, links.Count);
                Assert.All(links, link => Assert.Equal(Visibility.Visible, link.Visibility));
            }
            finally
            {
                dialog.Close();
            }

            return true;
        });
    }
}
