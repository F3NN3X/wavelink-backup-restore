// System.IO is NOT in the implicit-usings set for a UseWPF project - see ThemeTests.cs's own
// comment on this.
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows.Input;
using WaveLinkBackup.App.ViewModels;

namespace WaveLinkBackup.App.Tests;

/// <summary>
/// technical-debt.md §7.4: implement to Windows conventions generally, not only the four keys the
/// design names.
///
/// The list it gives is: full keyboard reachability with a visible focus ring; Alt-accelerators on
/// dialog buttons; Space activating the focused control; arrow keys moving list selection with
/// Home/End; Shift+F10 and the Menu key opening the row's overflow; Ctrl+F reaching the search
/// field; Delete on a selected row opening the delete dialog; and screen-reader labels that read as
/// sentences.
///
/// Several of those are WPF's own once the structure is right — Space, ↑/↓, Home/End and Shift+F10
/// all come free, and the point of the tests below is to pin the STRUCTURE that makes them free,
/// because the previous shape took each of them away.
/// </summary>
public sealed class KeyboardConventionTests
{
    private static readonly string SourceRoot = Assembly.GetExecutingAssembly()
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .Single(a => a.Key == "AppSourceRoot").Value!;

    private static string Read(params string[] parts) =>
        File.ReadAllText(Path.Combine([SourceRoot, .. parts]));

    // ------------------------------------------------------------------ the key map

    [Theory]
    [InlineData(nameof(ShellCommands.Refresh), Key.F5, ModifierKeys.None)]
    [InlineData(nameof(ShellCommands.Search), Key.F, ModifierKeys.Control)]
    [InlineData(nameof(ShellCommands.ClearSearch), Key.Escape, ModifierKeys.None)]
    [InlineData(nameof(ShellCommands.BackUpNow), Key.B, ModifierKeys.Control)]
    [InlineData(nameof(ShellCommands.Rename), Key.F2, ModifierKeys.None)]
    [InlineData(nameof(ShellCommands.Delete), Key.Delete, ModifierKeys.None)]
    [InlineData(nameof(ShellCommands.Restore), Key.Enter, ModifierKeys.None)]
    public void Every_named_convention_has_the_gesture_the_design_gives_it(
        string name, Key key, ModifierKeys modifiers)
    {
        var command = ShellCommands.All.Single(c => c.Name == name);
        var gesture = command.InputGestures.OfType<KeyGesture>().Single();

        Assert.Equal(key, gesture.Key);
        Assert.Equal(modifiers, gesture.Modifiers);
    }

    /// <summary>
    /// RoutedUICommand's Text is what a screen reader announces and what the row's overflow menu
    /// shows. An empty one would leave both blank.
    /// </summary>
    [Fact]
    public void Every_command_carries_text_a_reader_can_announce()
    {
        Assert.All(ShellCommands.All, c => Assert.False(string.IsNullOrWhiteSpace(c.Text)));
    }

    // ------------------------------------------------------------------ the row's overflow

    /// <summary>
    /// Shift+F10 and the Menu key open a control's ContextMenu with no code at all — but only if
    /// the menu is on the CONTAINER. Hung off the ··· glyph it would answer a right-click and
    /// neither key, which is what the row had: a decorative ··· and no menu anywhere.
    /// </summary>
    [Fact]
    public void The_rows_overflow_menu_is_on_the_container_so_the_keyboard_can_open_it()
    {
        var rowStyles = Read("Views", "RowStyles.xaml");

        var style = Regex.Match(
            rowStyles,
            "<Style x:Key=\"WlRowTemplate\" TargetType=\"ListBoxItem\">.*?</Style>",
            RegexOptions.Singleline).Value;

        Assert.Contains("<Setter Property=\"ContextMenu\">", style, StringComparison.Ordinal);
    }

    /// <summary>
    /// The menu uses the same commands as the bottom bar and the key map, so there is one
    /// definition of what each does and one place its CanExecute is decided — the menu greys
    /// itself on a damaged row without knowing what damaged means.
    /// </summary>
    [Theory]
    [InlineData("ShellCommands.Restore")]
    [InlineData("ShellCommands.Rename")]
    [InlineData("ShellCommands.Delete")]
    public void The_overflow_menu_reuses_the_commands_rather_than_redefining_them(string command)
    {
        Assert.Contains(
            $"Command=\"{{x:Static vm:{command}}}\"",
            Read("Views", "RowStyles.xaml"),
            StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ Alt-accelerators

    /// <summary>
    /// The defect this would otherwise have: a bare ContentPresenter has RecognizesAccessKey FALSE,
    /// so an underscore in a Content string renders as a literal underscore and no accelerator
    /// exists. The buttons would have LOOKED wired — §4.20's lesson in a different costume.
    /// </summary>
    [Fact]
    public void Every_button_template_recognises_an_access_key()
    {
        var styles = Read("Views", "ControlStyles.xaml");

        var presenters = Regex.Matches(styles, "<ContentPresenter ", RegexOptions.Singleline).Count;
        var recognising = Regex.Matches(styles, "RecognizesAccessKey=\"True\"").Count;

        Assert.True(
            recognising >= presenters,
            $"{presenters} ContentPresenters, {recognising} recognising an access key. " +
            "A button template that does not renders the underscore as text.");
    }

    [Theory]
    [InlineData("DeleteDialog.xaml", "_Cancel")]
    [InlineData("DeleteDialog.xaml", "_Delete")]
    [InlineData("RestoreDialog.xaml", "_Cancel")]
    [InlineData("RestoreDialog.xaml", "_Restore this backup")]
    [InlineData("EmptyTrashDialog.xaml", "_Cancel")]
    public void The_confirmation_dialogs_carry_their_accelerators(string file, string label)
    {
        Assert.Contains(label, Read("Views", file), StringComparison.Ordinal);
    }

    /// <summary>
    /// An accelerator on the destructive button must NOT weaken the focus rule
    /// (10-decisions.md §6): focus still starts on Cancel, and Enter still fires the destructive
    /// button only once the user has deliberately moved there.
    /// </summary>
    [Theory]
    [InlineData("DeleteDialog.xaml")]
    [InlineData("RestoreDialog.xaml")]
    [InlineData("EmptyTrashDialog.xaml")]
    public void No_confirmation_dialog_hands_Enter_to_its_destructive_button(string file)
    {
        var xaml = Regex.Replace(Read("Views", file), "<!--.*?-->", string.Empty, RegexOptions.Singleline);

        Assert.DoesNotContain("IsDefault=\"True\"", xaml, StringComparison.Ordinal);
    }
}
