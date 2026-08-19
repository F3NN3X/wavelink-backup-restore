using System.Globalization;

namespace WaveLinkBackup.Cli.CommandLine;

/// <summary>
/// Arguments in, a record out. PURE - no console, no filesystem, no environment. Hand-rolled
/// rather than taken from a package (ADR-009): eight verbs and five options do not justify a
/// pre-release dependency on the one artifact whose NativeAOT eligibility the project has
/// spent three phases protecting.
///
/// One syntax per option, deliberately. `--opt value`, not `--opt=value` as well; no bundling
/// of short flags. Supporting both forms of everything is where hand-rolled parsers go wrong.
/// </summary>
public static class CommandLineParser
{
    public static ParsedCommand Parse(string[] args)
    {
        if (args.Length == 0) return new ParsedCommand(Verb.Help, []);

        var verb = ParseVerb(args[0]);
        if (verb == Verb.None) return ParsedCommand.Failed($"Unknown command '{args[0]}'.");

        var command = new ParsedCommand(verb, []);
        var positional = new List<string>();

        for (var i = 1; i < args.Length; i++)
        {
            var arg = args[i];

            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                positional.Add(arg);
                continue;
            }

            switch (arg)
            {
                case "--yes" or "--y":
                    command = command with { AssumeYes = true };
                    break;

                case "--json":
                    command = command with { Json = true };
                    break;

                case "--with-plugins":
                    command = command with { WithPlugins = true };
                    break;

                case "--name":
                    if (!TryValue(args, ref i, arg, out var name)) return Missing(arg);
                    command = command with { Name = name };
                    break;

                case "--settings-path":
                    if (!TryValue(args, ref i, arg, out var settings)) return Missing(arg);
                    command = command with { SettingsPath = settings };
                    break;

                case "--store":
                    if (!TryValue(args, ref i, arg, out var store)) return Missing(arg);
                    command = command with { StorePath = store };
                    break;

                case "--keep":
                    if (!TryValue(args, ref i, arg, out var keep)) return Missing(arg);
                    if (!TryNonNegative(keep, out var keepCount))
                        return ParsedCommand.Failed($"--keep needs a number of 0 or more, not '{keep}'.");
                    command = command with { KeepCount = keepCount };
                    break;

                case "--interval":
                    if (!TryValue(args, ref i, arg, out var interval)) return Missing(arg);
                    if (!TryNonNegative(interval, out var seconds) || seconds == 0)
                        return ParsedCommand.Failed($"--interval needs a number of seconds above 0, not '{interval}'.");
                    command = command with { IntervalSeconds = seconds };
                    break;

                default:
                    return ParsedCommand.Failed($"Unknown option '{arg}'.");
            }
        }

        return command with { Arguments = positional };
    }

    private static Verb ParseVerb(string value) => value.ToLowerInvariant() switch
    {
        "backup" => Verb.Backup,
        "list" or "ls" => Verb.List,
        "restore" => Verb.Restore,
        "rename" => Verb.Rename,
        "delete" or "rm" => Verb.Delete,
        "verify" => Verb.Verify,
        "prune" => Verb.Prune,
        "empty-trash" or "empty" => Verb.EmptyTrash,
        "watch" => Verb.Watch,
        "help" or "--help" or "-h" or "-?" => Verb.Help,
        "version" or "--version" => Verb.Version,
        _ => Verb.None,
    };

    private static bool TryValue(string[] args, ref int index, string option, out string value)
    {
        // An option's value must not look like another option: `--name --json` is a missing
        // value, not a backup called "--json".
        if (index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal))
        {
            value = args[++index];
            return true;
        }

        value = string.Empty;
        _ = option;
        return false;
    }

    private static bool TryNonNegative(string text, out int value) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value >= 0;

    private static ParsedCommand Missing(string option) =>
        ParsedCommand.Failed($"{option} needs a value.");
}
