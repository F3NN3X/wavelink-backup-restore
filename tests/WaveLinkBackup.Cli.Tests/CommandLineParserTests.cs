using WaveLinkBackup.Cli.CommandLine;

namespace WaveLinkBackup.Cli.Tests;

/// <summary>
/// The parser is pure, so these need no console and no filesystem — the same property that
/// justified hand-rolling it rather than taking a pre-release dependency (ADR-009).
/// </summary>
public sealed class CommandLineParserTests
{
    [Theory]
    [InlineData("backup", Verb.Backup)]
    [InlineData("list", Verb.List)]
    [InlineData("ls", Verb.List)]
    [InlineData("restore", Verb.Restore)]
    [InlineData("rename", Verb.Rename)]
    [InlineData("delete", Verb.Delete)]
    [InlineData("rm", Verb.Delete)]
    [InlineData("verify", Verb.Verify)]
    [InlineData("prune", Verb.Prune)]
    [InlineData("watch", Verb.Watch)]
    [InlineData("help", Verb.Help)]
    [InlineData("version", Verb.Version)]
    public void Every_verb_parses(string input, Verb expected)
    {
        Assert.Equal(expected, CommandLineParser.Parse([input]).Verb);
    }

    [Theory]
    [InlineData("BACKUP")]
    [InlineData("Backup")]
    public void Verbs_are_case_insensitive(string input)
    {
        Assert.Equal(Verb.Backup, CommandLineParser.Parse([input]).Verb);
    }

    [Fact]
    public void No_arguments_shows_help_rather_than_failing()
    {
        var command = CommandLineParser.Parse([]);

        Assert.Equal(Verb.Help, command.Verb);
        Assert.True(command.IsValid);
    }

    [Fact]
    public void An_unknown_verb_is_a_usage_error_naming_what_was_typed()
    {
        var command = CommandLineParser.Parse(["destroy"]);

        Assert.False(command.IsValid);
        Assert.Contains("destroy", command.Error!, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unknown_option_is_a_usage_error()
    {
        var command = CommandLineParser.Parse(["backup", "--turbo"]);

        Assert.False(command.IsValid);
        Assert.Contains("--turbo", command.Error!, StringComparison.Ordinal);
    }

    [Fact]
    public void Options_with_values_are_read()
    {
        var command = CommandLineParser.Parse([
            "backup", "--name", "Before 3.3 beta", "--store", @"D:\backups",
            "--settings-path", @"D:\rescued\Settings.json", "--keep", "10", "--interval", "30"]);

        Assert.Equal("Before 3.3 beta", command.Name);
        Assert.Equal(@"D:\backups", command.StorePath);
        Assert.Equal(@"D:\rescued\Settings.json", command.SettingsPath);
        Assert.Equal(10, command.KeepCount);
        Assert.Equal(30, command.IntervalSeconds);
    }

    [Fact]
    public void Flags_are_read()
    {
        var command = CommandLineParser.Parse(["restore", "abc", "--yes", "--json"]);

        Assert.True(command.AssumeYes);
        Assert.True(command.Json);
        Assert.Equal(["abc"], command.Arguments);
    }

    [Fact]
    public void Restoring_the_plugin_files_is_opt_in()
    {
        // The only thing in this program that writes outside the user's own folders, so it is
        // never the default ([[ADR-006]]).
        Assert.False(CommandLineParser.Parse(["restore", "abc"]).WithPlugins);
        Assert.True(CommandLineParser.Parse(["restore", "abc", "--with-plugins"]).WithPlugins);
    }

    [Fact]
    public void An_option_missing_its_value_is_a_usage_error()
    {
        Assert.False(CommandLineParser.Parse(["backup", "--name"]).IsValid);
    }

    [Fact]
    public void An_option_followed_by_another_option_is_a_missing_value()
    {
        // `--name --json` is a mistake, not a backup called "--json".
        var command = CommandLineParser.Parse(["backup", "--name", "--json"]);

        Assert.False(command.IsValid);
        Assert.Contains("--name", command.Error!, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--keep", "many")]
    [InlineData("--keep", "-1")]
    [InlineData("--interval", "0")]
    [InlineData("--interval", "-5")]
    public void Numeric_options_reject_nonsense(string option, string value)
    {
        Assert.False(CommandLineParser.Parse(["watch", option, value]).IsValid);
    }

    [Fact]
    public void A_keep_count_of_zero_is_allowed_because_it_means_something()
    {
        // "keep no automatic backups" is a real choice; manual ones survive regardless.
        Assert.Equal(0, CommandLineParser.Parse(["prune", "--keep", "0"]).KeepCount);
    }

    [Fact]
    public void Positional_arguments_keep_their_order_around_options()
    {
        var command = CommandLineParser.Parse(["rename", "the-id", "--json", "The New Name"]);

        Assert.Equal(["the-id", "The New Name"], command.Arguments);
    }

    [Fact]
    public void A_name_that_looks_like_a_path_is_still_just_a_name()
    {
        var command = CommandLineParser.Parse(["backup", "--name", @"Mic chain 3/4"" <hot>"]);

        Assert.Equal(@"Mic chain 3/4"" <hot>", command.Name);
    }

    [Fact]
    public void Nothing_is_assumed_when_options_are_absent()
    {
        var command = CommandLineParser.Parse(["backup"]);

        Assert.Null(command.Name);
        Assert.Null(command.StorePath);
        Assert.Null(command.SettingsPath);
        Assert.Null(command.KeepCount);
        Assert.Null(command.IntervalSeconds);
        Assert.False(command.AssumeYes);
        Assert.False(command.Json);
    }
}
