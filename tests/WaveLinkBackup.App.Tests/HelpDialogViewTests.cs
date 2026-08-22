using System.Windows;
using WaveLinkBackup.App.Theming;
using WaveLinkBackup.App.ViewModels;
using WaveLinkBackup.App.Views;

namespace WaveLinkBackup.App.Tests;

/// <summary>
/// The help dialog's view, forced through a real layout pass. The companion to
/// <see cref="SettingsDialogViewTests"/> and <see cref="DeleteDialogViewTests"/>: the model is static
/// copy with nothing to unit-test, so the Window itself - that it parses, resolves its resources
/// under App.xaml's real merge order, and lays out - is what this file guards.
///
/// The documentation link's RequestNavigate handler calls Process.Start; under the test Application
/// nothing clicks it, so constructing the window is safe. Close is IsCancel, so dismissing needs no
/// handler to test either.
/// </summary>
public sealed class HelpDialogViewTests
{
    private static void ShowAndAssert(AppTheme theme, Action<FrameworkElement> assert) => Wpf.Run(() =>
    {
        AppResources.Load(theme);

        // An explicit size: DialogOverlay only sizes this window when it has an owner, and a layout
        // assertion needs a deterministic one. Wide enough for the 560px card plus its margins, tall
        // enough that the sections scroll inside the card rather than the card being clipped.
        var dialog = new HelpDialog(HelpDialogModel.Build())
        {
            Width = 900,
            Height = 800,
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
    /// All four sections render their headings and bodies - a section that exists only in the model
    /// passes every model check and paints nothing, so this walks the real visual tree.
    /// </summary>
    [Fact]
    public void Every_section_renders_its_heading_and_body()
    {
        var text = new List<string>();

        ShowAndAssert(AppTheme.Dark, e =>
        {
            if (e is System.Windows.Controls.TextBlock block && !string.IsNullOrWhiteSpace(block.Text))
            {
                text.Add(block.Text);
            }
        });

        Assert.Contains(text, t => t == "What gets backed up");
        Assert.Contains(text, t => t.StartsWith("Wave Link keeps its entire setup", StringComparison.Ordinal));
        Assert.Contains(text, t => t == "How snapshots are kept");
        Assert.Contains(text, t => t.StartsWith("One snapshot per distinct configuration", StringComparison.Ordinal));
        Assert.Contains(text, t => t == "How restoring works");
        Assert.Contains(text, t => t.StartsWith("Restoring closes Wave Link", StringComparison.Ordinal));
        Assert.Contains(text, t => t == "The tray icon");
        Assert.Contains(text, t => t.StartsWith("Right-click the tray icon", StringComparison.Ordinal));
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
    /// The footer offers exactly one action - Close - and it is the button Escape reaches. No Save,
    /// no other verbs: this dialog changes nothing, so there is nothing to save.
    /// </summary>
    [Fact]
    public void The_footer_says_close_and_offers_nothing_else()
    {
        var buttonLabels = new List<string>();

        ShowAndAssert(AppTheme.Dark, e =>
        {
            if (e is System.Windows.Controls.Button { Content: string label }) buttonLabels.Add(label);
        });

        Assert.Equal(["Close"], buttonLabels);
    }

    /// <summary>
    /// With no repository URL configured the documentation link renders nothing at all - a link that
    /// points nowhere is worse than no link (the same rule as App.ReleaseSource's IsConfigured).
    /// </summary>
    [Fact]
    public void The_documentation_link_is_absent_when_no_url_is_configured()
    {
        var found = false;

        ShowAndAssert(AppTheme.Dark, e =>
        {
            if (e.Name == "DocumentationLink" && e is System.Windows.Controls.TextBlock link)
            {
                found = true;
                Assert.Equal(Visibility.Collapsed, link.Visibility);
            }
        });

        Assert.True(found, "DocumentationLink is gone or renamed.");
    }

    /// <summary>
    /// With a URL configured the link renders its label and stays visible - the other half of the
    /// collapse rule above.
    /// </summary>
    [Fact]
    public void The_documentation_link_renders_when_a_url_is_configured()
    {
        var model = new HelpDialogModel(
            "How this app works",
            HelpDialogModel.Build().Sections,
            "Documentation",
            "https://example.com/docs");

        Wpf.Run(() =>
        {
            AppResources.Load(AppTheme.Dark);

            var dialog = new HelpDialog(model)
            {
                Width = 900,
                Height = 800,
                Left = -3000,
                Top = -3000,
                ShowInTaskbar = false,
            };

            dialog.Show();
            dialog.UpdateLayout();

            try
            {
                var link = AppResources.Descendants(dialog)
                    .OfType<System.Windows.Controls.TextBlock>()
                    .Single(e => e.Name == "DocumentationLink");

                Assert.Equal(Visibility.Visible, link.Visibility);
                Assert.Contains("Documentation", link.Text);
            }
            finally
            {
                dialog.Close();
            }

            return true;
        });
    }
}
