// Phase 4 builds this out. It exists now because ADR-004 puts the CLI in its own project
// from the first commit: it is the only AOT-eligible artifact, and separating it later
// would mean untangling it from WPF.
//
// Phase 1 ships Core only. This proves the reference graph and nothing else.

using WaveLinkBackup.Core.Discovery;

Console.WriteLine("wlbackup — Wave Link Backup CLI");
Console.WriteLine($"Core loaded: {typeof(SettingsLocation).Assembly.GetName().Name}");
Console.WriteLine("Not implemented yet. See _docs/dev-phases/README.md — phase 4.");
return 0;
