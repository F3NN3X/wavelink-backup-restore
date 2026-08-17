using System.Windows.Input;
using WaveLinkBackup.App.ViewModels;

namespace WaveLinkBackup.App.Tests;

/// <summary>
/// 10-decisions section 6 pins four of these and 7.4 adds the rest. A key map is exactly the
/// kind of thing that drifts silently, so it is asserted rather than trusted.
/// </summary>
public sealed class ShellCommandTests
{
    private static KeyGesture Gesture(RoutedUICommand command) =>
        (KeyGesture)command.InputGestures[0]!;

    [Fact]
    public void F5_re_reads_the_backup_folder()
    {
        Assert.Equal(Key.F5, Gesture(ShellCommands.Refresh).Key);
        Assert.Equal(ModifierKeys.None, Gesture(ShellCommands.Refresh).Modifiers);
    }

    [Fact]
    public void Escape_clears_the_search()
    {
        Assert.Equal(Key.Escape, Gesture(ShellCommands.ClearSearch).Key);
    }

    [Fact]
    public void Ctrl_f_reaches_the_search_field()
    {
        Assert.Equal(Key.F, Gesture(ShellCommands.Search).Key);
        Assert.Equal(ModifierKeys.Control, Gesture(ShellCommands.Search).Modifiers);
    }

    [Fact]
    public void Delete_deletes_and_f2_renames()
    {
        Assert.Equal(Key.Delete, Gesture(ShellCommands.Delete).Key);
        Assert.Equal(Key.F2, Gesture(ShellCommands.Rename).Key);
    }

    // Enter fires the primary, and on screen 1 the primary is Restore. In the RESTORE DIALOG it
    // must not - focus starts on Cancel there and the destructive button is reached
    // deliberately - but that dialog is a later session, and this is the list.
    [Fact]
    public void Enter_restores_from_the_list()
    {
        Assert.Equal(Key.Enter, Gesture(ShellCommands.Restore).Key);
    }

    [Fact]
    public void Every_command_has_a_name_a_screen_reader_can_announce()
    {
        foreach (var command in ShellCommands.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(command.Text), command.Name);
        }
    }
}
