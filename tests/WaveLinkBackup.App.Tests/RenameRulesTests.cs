using WaveLinkBackup.App.ViewModels;

namespace WaveLinkBackup.App.Tests;

/// <summary>
/// Rename is free text with no validation beyond non-empty and filesystem-safe. These tests pin
/// that rule down before any view touches it: empty/whitespace rejected, every illegal Windows
/// filename character rejected (naming the offender), and ordinary names - including spaces and
/// dots - accepted.
/// </summary>
public sealed class RenameRulesTests
{
    [Fact]
    public void An_empty_name_is_invalid()
    {
        var result = RenameRules.Validate(string.Empty);

        Assert.False(result.IsValid);
        Assert.NotNull(result.Reason);
    }

    [Theory]
    [InlineData("   ")]
    [InlineData("\t")]
    [InlineData("  \t ")]
    public void A_whitespace_only_name_is_invalid(string name)
    {
        var result = RenameRules.Validate(name);

        Assert.False(result.IsValid);
        Assert.NotNull(result.Reason);
    }

    [Theory]
    [InlineData(@"a\b")]
    [InlineData("a/b")]
    [InlineData("a:b")]
    [InlineData("a*b")]
    [InlineData("a?b")]
    [InlineData("a\"b")]
    [InlineData("a<b")]
    [InlineData("a>b")]
    [InlineData("a|b")]
    public void Each_illegal_character_is_invalid_and_named(string name)
    {
        var result = RenameRules.Validate(name);

        Assert.False(result.IsValid);
        // The cue names the offending character so the user knows what to remove.
        Assert.Contains(name[1].ToString(), result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void A_plain_name_is_valid()
    {
        var result = RenameRules.Validate("Before 3.3 beta");

        Assert.True(result.IsValid);
        Assert.Null(result.Reason);
    }

    [Fact]
    public void A_name_with_spaces_and_dots_is_valid()
    {
        var result = RenameRules.Validate("My setup, v2.1 (final)");

        Assert.True(result.IsValid);
        Assert.Null(result.Reason);
    }

    [Fact]
    public void A_single_illegal_character_among_legal_ones_is_invalid()
    {
        // "good/bad" - the slash is the only problem; the rest of the name is fine.
        var result = RenameRules.Validate("good/bad");

        Assert.False(result.IsValid);
        Assert.Contains("/", result.Reason, StringComparison.Ordinal);
    }
}
