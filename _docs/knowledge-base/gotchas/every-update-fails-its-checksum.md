---
title: "Every update fails its checksum"
status: published
created: 2026-08-25
updated: 2026-08-25
related_adrs: [ADR-012]
tags: [gotcha, updates, releases]
---

# Every update fails its checksum

**Provenance:** **Observed**, 2026-08-25, on the first real update attempt this project has ever
made. Present since 0.7.2 and invisible for three releases, because nobody had run an update.

## Symptom

The in-app update finds a new version, downloads it, and refuses to install it with a checksum
error. Every time. Retrying does not help, and neither does a different network.

The release itself is fine. Download the archive by hand, hash it, and it matches the published
`.sha256` exactly.

## Cause

The release feed picked the checksum by shape rather than by name:

```csharp
if (name.EndsWith(source.AssetSuffix, ...))       { downloadUrl = url; }
else if (name.EndsWith(".sha256", ...))           { sha256 = url; }   // last one wins
```

That is correct for a release carrying one archive. **It became wrong at 0.7.2**, when the CLI was
split into its own artifact so the download would stop carrying the .NET runtime twice. A release
has carried four assets ever since, and GitHub returns them in this order:

```
WaveLinkBackup-0.7.5-app-win-x64.zip
WaveLinkBackup-0.7.5-app-win-x64.zip.sha256
WaveLinkBackup-CLI-0.7.5-win-x64.zip
WaveLinkBackup-CLI-0.7.5-win-x64.zip.sha256      <- the loop kept this one
```

So the app downloaded its own archive and verified it against **the CLI's digest**. A guaranteed
mismatch, on every update, forever.

## The plausible explanation, and why it is wrong

**"The download is corrupt."** That is what a checksum error means everywhere else, and it is the
only thing the message says, so the search starts at the network, the CDN, or the proxy. It is
none of those, and every check you can run points the wrong way: the archive downloads fine, its
hash matches its own published file, and the release workflow is producing correct checksums for
both artifacts.

The failure is not in either file. It is in which file was compared with which, and nothing in the
error surfaces that, because the code that chose the pairing believed it had the right one.

**"CI would have caught a bad release."** CI publishes both archives and both digests correctly.
The bug is entirely in the client's pairing, on a code path CI never runs.

## Fix

Collect the assets first, then pair the download with **its own** checksum by name:

```csharp
var download = found.FirstOrDefault(a => a.Name.EndsWith(source.AssetSuffix, ...));

var sha256 = found
    .FirstOrDefault(a => a.Name.Equals(download.Name + ".sha256", ...))
    .Url;
```

Order no longer decides the answer, and the size fed to the progress bar is the archive's own
rather than whichever asset happened to be last.

**A checksum belonging to a different file is treated as NO checksum**, not used anyway. It would
fail every time, and `UpdateDownloader` already refuses to install what it cannot verify. That is
the honest failure instead of a misleading one.

## How to avoid it

`UpdateFeedChecksumPairingTests` uses the exact asset list, in the exact order, that a real release
publishes. Two of its four cases fail against the old pairing, verified before the fix went in.

**The reason this survived is worth more than the fix.** Every payload in `UpdateFeedTests` carried
one archive and one `.sha256`, and with one of each, *"take any asset ending .sha256"* and *"take
the right one"* are indistinguishable. The fixture was simpler than production in exactly the
dimension the bug lived in.

**When a release gains a second artifact, the test fixtures have to gain it too.** Nothing about
the 0.7.2 packaging change touched this file, so nothing prompted anyone to look.

## References

- [[ADR-012]]: check-only updates with a staged swap
- [[the-update-installs-nothing-and-says-nothing]]: the failure immediately after this one
- `src/WaveLinkBackup.App/Updates/IUpdateFeed.cs`, the pairing
