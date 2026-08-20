---
title: "Releasing a version, and how the app updates itself"
status: published
created: 2026-08-20
updated: 2026-08-20
related_adrs: [ADR-012, ADR-011, ADR-004]
tags: [runbook, release, updates, ci]
---

# Releasing a version, and how the app updates itself

**One loop, described end to end**: you push a tag, CI builds and publishes a release, and every
running copy of the app finds it. The two halves are only useful together — a release in the wrong
shape is invisible to the updater, and an updater pointed at nothing is a button that cannot work.
So they are one document.

> **Provenance.** The pipeline and the updater are **built and unit-tested; the loop has never run
> end to end**, because this repository has no remote and has published no release. Everything
> below marked *measured* was verified locally against fixtures; everything marked *unverified* is
> the part that needs a first real release to confirm. Say which when you edit this.

**Why this folder exists now.** [`_docs/README.md`](../../README.md) set `operations/runbooks/`'s
trigger as *"there is a running system to operate — realistically, the first release."* Preparing
that release is the trigger firing.

---

## 1 · The shape a release must have

This is the contract. The updater reads it and nothing else.

| | What | Why the updater needs it |
|---|---|---|
| **Tag** | `v1.4.0` on the commit to release | `ReleaseVersion.Parse` reads the tag as the version. `v` optional, `1.4` fine, `1.4.0-beta.2` reads as `1.4.0` — see §6 on why a pre-release suffix is dropped rather than ordered |
| **Asset 1** | `WaveLinkBackup-1.4.0-win-x64.zip` | Matched on the **suffix** `win-x64.zip`, so the version in the name is free-form |
| **Asset 2** | `WaveLinkBackup-1.4.0-win-x64.zip.sha256` | Matched on the suffix `.sha256`. **Not optional** — `UpdateDownloader` refuses an update with no published checksum rather than installing whatever arrived |

**The archive's root is the install directory's contents**, not a folder containing them.
`UpdateInstaller` expands it and expects `WaveLinkBackup.exe` at the top level; anything else is
refused *before* the swap, with `THE DOWNLOAD DIDN'T CONTAIN THE APP · NOTHING CHANGED`.

`.github/workflows/release.yml` produces exactly this, which is the point of it existing — the
shape is CI's responsibility rather than something a person remembers on release day.

---

## 2 · Cutting a release

```
git tag v1.4.0
git push origin v1.4.0
```

That is the whole procedure. The workflow triggers on `push: tags: v*` and:

1. **Takes the version from the tag**, not from `Directory.Build.props`. `-p:Version=1.4.0`
   overrides the file, so nobody has to remember to edit it first — and a build whose assembly
   version disagreed with its tag would make the app compare itself against the wrong number.
2. **Runs the full suite** before publishing anything. A release that fails its own tests should
   not exist to be found.
3. **Publishes the app self-contained** (`--self-contained true`), matching the csproj rather than
   overriding it. [technical-debt.md](../../technical-debt.md) §1.5 is about upstream's pipeline
   disagreeing with its project file; ours agrees on purpose, so a local `dotnet publish` produces
   the same artifact CI does.
4. **Publishes the CLI beside it**, into the same directory, so one archive is one install.
5. **Packages and hashes**, writing `<hash>  <name>` in `sha256sum` format.
6. **Creates the GitHub release** with both assets.

`workflow_dispatch` is also wired, for re-running a release whose upload failed.

### Before you tag

- [ ] `CHANGELOG.md` has a section for this version.
- [ ] `documentation-stats.md`'s **Recent additions** has a block for it.
- [ ] The tag is on the commit you mean. A tag is cheap to delete and expensive to have wrong,
      because the updater trusts the newest release absolutely.

---

## 3 · Pointing the app at the feed

**Where to look is configuration, not a constant** — read from the environment at startup:

```
WLBACKUP_UPDATE_OWNER = <github owner>
WLBACKUP_UPDATE_REPO  = <repository name>
```

**Unset hides the entire UPDATES section.** A *Check now* that cannot reach anything is worse than
no button, and this repository has no remote yet — a compiled-in owner/repo would be
[technical-debt.md](../../technical-debt.md) §5's exact mistake, a constant that becomes wrong the
moment a real one exists. `App.ReleaseSource` is the one place it is read.

**When the repository goes public**, set both — in the published build's environment, or by
promoting them to `Directory.Build.props` as constants once they genuinely are stable. Promoting
them is a deliberate choice to make, not a default: it trades the flexibility for one fewer moving
part.

---

## 4 · What the app does with it

### Checking

- **Weekly**, if *Check for updates on its own* is on (it is, by default), and **only when the
  Settings dialog is opened** — there is no background poller. The design's rule is that an
  available update is *never* a notification, a badge or a banner, so nothing about a check needs
  to happen while nobody is looking.
- **On demand**, from *Check now*.
- **From error 8**, whose *Get the update* opens Settings scrolled to UPDATES with a check already
  running — a backup made by a newer version cannot be restored until you update, so that is the
  one path where the app raises the subject itself.

One request to `releases/latest`. The result is one of four states, and **a check that cannot be
understood reports that it failed, never that you are up to date** — reporting up-to-date wrongly
means the user never hears about a fix and nothing in the app would ever say otherwise.

The last-checked time is recorded **even when the check failed**, or a machine that is offline for
a fortnight re-checks on every tick.

### Installing

Only ever from a press. *"It never installs anything without you"* is structural rather than a
promise: `UpdateViewModel` takes no success as an input and cannot produce a congratulatory
anything, and the only unprompted act in the whole feature is the weekly look.

1. **Download**, streamed, hashing as it goes.
2. **Verify** against the published `.sha256`. A mismatch deletes the file and reports
   `THE DOWNLOAD DIDN'T MATCH ITS CHECKSUM · NOTHING WAS INSTALLED` — deleted rather than kept,
   because a file that failed its checksum must not sit there for a later run, or a person, to find
   and trust.
3. **Expand** to `<install>.staged`, and check `WaveLinkBackup.exe` is in it.
4. **Hand over**: the *staged* copy is started with `--apply-update <pid> <install>`, and this
   process shuts down completely — watcher, tray, store, and the same last backup a Quit takes.
5. **Swap**, from the staged copy, which is outside the directory being replaced. A process cannot
   overwrite its own executable while running; that is why there are two.
6. **Relaunch**, either way — the new install on success, the old one on failure.

**The ordering is chosen so every interruption leaves something that runs.** The previous install
is *moved* to `<install>.previous`, not deleted, and only removed once the new one is in place. A
failed rename puts it straight back. There is no instant at which you have no app.

---

## 5 · Why the updater does not elevate

**It could, and deliberately does not.** [[ADR-011]] already built an elevation path — the shell
relaunches itself elevated to restore tier 4's `.vst3` files into
`C:\Program Files\Common Files\VST3`, which is not user-writable. Reusing it here would let the
app install itself into `Program Files`.

The reason not to is the difference between the two operations:

| | Tier 4 restore ([[ADR-011]]) | Updating |
|---|---|---|
| What is written | Files the user asked to put back, from a backup they made | **This program's own binaries** |
| Where they came from | The user's own disk | The network |
| Who decided | The user, on an opt-in row, on a restore they were already making | The user pressed a button; the *bytes* were chosen by whoever controls the release |
| If it goes wrong | Some plug-ins are missing; reinstall them | The program is replaced by an unknown one, with administrator rights |

So an install under `Program Files` reports
`COULDN'T WRITE TO … · ACCESS DENIED` and offers *Try again* / *Download it yourself* — which is
the failed-update block `screens/12` already draws, and is the honest answer. A program that
silently escalates to administrator to overwrite its own binaries is the shape of a thing users
are right to distrust, and the shape a supply-chain attack wants it to have.

**This is also the argument for signing** (§7). Elevation would be defensible if the app could
prove the bytes are its own; today it can only prove they are the bytes the release named.

---

## 6 · Decisions worth knowing before you change it

**A pre-release tag is read as its release version, not ordered against it.** `1.4.0-beta.2` reads
as `1.4.0`. This app has no pre-release channel, and inventing an ordering would silently decide
whether a beta counts as newer than the release it precedes. **If you ever publish a pre-release,
this needs a real decision first** — as it stands, `v1.4.0-beta.1` would be offered to everyone as
`1.4.0`.

**A three-part tag must equal a four-part assembly version.** `ReleaseVersion` zeroes the revision
on both sides, so `1.4.0` from a tag and `1.4.0.0` from the assembly compare equal. Without it the
app would see every release as older than itself and report up-to-date forever. Pinned by a test.

**The checksum is integrity, not authenticity.** It proves the bytes that arrived are the bytes the
release named — catching a truncated download, a bad mirror, a tampered transport. It does not
prove *who* published the release, because whoever controls the release controls the checksum
beside it. This is written on `UpdateRelease.Sha256` itself so it cannot be misread as a security
guarantee.

---

## 7 · Owed before this is used in anger

| | What | Why |
|---|---|---|
| **1** | **Run the loop once, end to end** | Everything here is built and unit-tested; the download, the swap and the relaunch have only met fixtures and temp directories. Do the first release, install it over a real install, and record the result in this file. **Watch step 4 in particular**: the app and the CLI publish into one directory, and while their runtime assemblies should be identical files and their `.deps.json` are named apart, that has never actually run. |
| **2** | **Code signing** | The gap §6 names. Signing is what turns "these are the bytes the release named" into "these are our bytes", and it is what would make elevating defensible. |
| **3** | **Decide the pre-release rule** | Before a `-beta` tag ever exists, not after. |
| **4** | **Set the feed variables** | §3. Until then the UPDATES section hides itself, which is correct but means nobody has exercised it. |

---

## 8 · When it goes wrong

| Symptom | Likely cause |
|---|---|
| UPDATES section is not in Settings | `WLBACKUP_UPDATE_OWNER` / `_REPO` unset (§3). Correct behaviour, not a bug. |
| `NO RELEASE FEED IS CONFIGURED` | Same, but the section is showing — the variables are set to empty strings rather than absent. |
| `COULDN'T REACH THE RELEASE FEED · HTTP 403` | GitHub rate limit, or a missing `User-Agent`. The client sends one; a proxy may be stripping it. |
| `THE NEWEST RELEASE HAS NO VERSION WE CAN READ` | The tag is not version-shaped — `nightly`, `release-candidate`. §1. |
| `RELEASE 1.4.0 HAS NO WIN-X64.ZIP` | The asset name does not end in the expected suffix. §1. |
| `THE RELEASE PUBLISHED NO CHECKSUM` | The `.sha256` asset is missing. The workflow always writes one, so this means a partial upload — re-run it with `workflow_dispatch`. |
| `THE DOWNLOAD DIDN'T CONTAIN THE APP` | The archive has a wrapping folder. §1 — the root must be the install contents. |
| `COULDN'T WRITE TO … · ACCESS DENIED` | Installed somewhere the user cannot write. Working as designed — §5. |
| App relaunches on the OLD version after an update | The swap failed and rolled back, which is the designed outcome. `<install>.staged` will still be there; its presence is the evidence. |
| `<install>.previous` left behind | The swap succeeded but the cleanup did not. Harmless — it costs disk, not correctness, and the next update deletes it first. |

---

## References

- [[ADR-012]] — why check-only with a staged swap, and what it rules out
- [[ADR-011]] — the elevation path this deliberately does not reuse (§5)
- [`operations/design/screens/12-tray-autostart-update.md`](../design/screens/12-tray-autostart-update.md)
  — the designed section, and the restraint rules
- [`technical-debt.md`](../../technical-debt.md) §4.21 item 5 — the debt this closed, §5 — why the
  feed is configuration
- [`.github/workflows/release.yml`](../../../.github/workflows/release.yml) — the pipeline
- [`THIRD-PARTY-NOTICES.md`](../../../THIRD-PARTY-NOTICES.md)
