namespace WaveLinkBackup.Cli.Output;

/// <summary>
/// Hand-maintained, because the parser is hand-rolled (ADR-009) and nothing generates this.
/// That means it CAN drift from the parser, so a test asserts every verb and option appears
/// here — which is the price of the decision, paid rather than ignored.
/// </summary>
public static class HelpText
{
    public static IReadOnlyList<string> Lines { get; } =
    [
        "wlbackup — back up and restore Elgato Wave Link settings",
        "",
        "USAGE",
        "  wlbackup <command> [options]",
        "",
        "COMMANDS",
        "  backup            Take a backup now. Never skipped, even if nothing changed.",
        "  list              List backups, newest first.",
        "  restore <id>      Replace your settings with a backup. Asks first.",
        "  rename <id> <name>  Rename a backup. Moves no files.",
        "  delete <id>       Move a backup to the trash. Asks first.",
        "  empty-trash       Remove trashed backups for good.",
        "  verify [id]       Check a backup still matches its recorded hashes.",
        "  prune             Delete old automatic backups. Yours are never touched.",
        "  watch             Back up automatically when settings change. Ctrl+C to stop.",
        "  diagnostics       Print what this app knows about itself, with serial numbers and",
        "                    your user name removed. Nothing is ever uploaded.",
        "  version           Print the version.",
        "  help              Print this.",
        "",
        "OPTIONS",
        "  --name <text>         Name for a new backup.",
        "  --settings-path <p>   Use this settings file instead of finding Wave Link.",
        "  --store <path>        Where backups are kept.",
        "  --keep <n>            How many automatic backups to keep (default 30).",
        "  --interval <seconds>  How often watch checks (default 15).",
        "  --with-plugins        Also put the plug-in files back (restore). Needs admin rights.",
        "  --yes                 Do not ask for confirmation.",
        "  --json                Machine-readable output.",
        "",
        "NOTES",
        "  Backups you take yourself are never deleted automatically. Nor are the ones",
        "  taken just before a restore. Nor are damaged ones — a corrupt backup never",
        "  pushes a good one out.",
        "",
        "  Deleting moves a backup to a .trash folder inside your backup folder, so it",
        "  is easy to get back. empty-trash sends it to the Recycle Bin — or removes it",
        "  permanently if your backup folder is somewhere without one, like a NAS.",
        "",
        "  A restore always puts your effect presets back. The plug-in files themselves are",
        "  only restored with --with-plugins, because writing into Program Files needs",
        "  administrator rights - and everything that matters restores without it.",
        "",
        "  A backup describes THIS computer: it names the audio devices plugged into it,",
        "  so restoring it elsewhere will not line up with that machine's inputs.",
    ];
}
