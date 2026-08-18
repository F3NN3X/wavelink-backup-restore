namespace WaveLinkBackup.App.ViewModels;

/// <summary>The outcome of validating a rename: valid, or invalid with the reason to show inline.</summary>
/// <param name="IsValid">True when the name may be committed to the store.</param>
/// <param name="Reason">Human-readable why, for the inline cue. Null when valid.</param>
public sealed record RenameValidation(bool IsValid, string? Reason)
{
    public static RenameValidation Valid => new(true, null);

    public static RenameValidation Invalid(string reason) => new(false, reason);
}

/// <summary>
/// Rename is free text with no validation beyond non-empty and filesystem-safe (05 / README
/// Interactions: "in place on the row's name; commit on Enter or blur, cancel on Escape"). Kept
/// pure so it is testable without a store or a window - the view-model asks it whether a draft may
/// be committed, nothing more.
/// </summary>
public static class RenameRules
{
    /// <summary>
    /// Characters Windows will not put in a file or folder name. A rename that contains any of
    /// these would make the store's own move fail at the filesystem, so it is rejected here first.
    /// </summary>
    public static readonly char[] IllegalCharacters = ['\\', '/', ':', '*', '?', '"', '<', '>', '|'];

    /// <summary>
    /// Validate a draft name. Empty or whitespace-only is invalid (no trimming - "  " is not the
    /// same as "", and neither may be committed). Any illegal character is invalid, naming the
    /// first one found so the cue points at the problem. Otherwise valid.
    /// </summary>
    public static RenameValidation Validate(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return RenameValidation.Invalid("A name can't be empty.");
        }

        foreach (var c in IllegalCharacters)
        {
            if (name.IndexOf(c) >= 0)
            {
                return RenameValidation.Invalid($"A name can't contain '{c}'.");
            }
        }

        return RenameValidation.Valid;
    }
}
