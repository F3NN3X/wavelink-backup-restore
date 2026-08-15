# Changelog

All notable changes to Wave Link Backup are recorded here.

Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/); versioning follows
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

> **This is the engineering changelog** — what shipped, per version, broad enough to become
> release notes. The documentation ecosystem has its own delta log in
> [`_docs/documentation-stats.md`](_docs/documentation-stats.md) → *Recent additions*. Same
> commit is fine; don't write the same entry in both.

---

## [Unreleased]

Pre-alpha. **No application code exists yet.** Phase 0 of
[the roadmap](_docs/dev-phases/README.md) is in progress.

### Added

- Documentation system under `_docs/` — 8 ADRs, 8 gotchas, 1 recipe, 1 upstream audit, an
  8-phase roadmap with phases 0 and 1 detailed, a glossary, and a technical-debt register.
  Seeded from `SPEC.md` and the design handoff.
- Root `README.md` and this file.
- `.gitignore`, including project-specific rules that refuse to commit real Wave Link settings
  files, VST3 binaries or a backup store — those files embed hardware serial numbers and the
  Windows username.

### Changed

- `design_handoff_wave_link_backup/` moved to `_docs/operations/design/`; its `README.md`
  renamed `design-handoff.md`.
- `_docs/README-temp.md` consumed into `_docs/README.md` and archived.

### Not yet done in phase 0

- `LICENSE` — MIT, preserving upstream's copyright notice.
- Fork intake from
  [voltybat/WaveLinkSettingsUtility](https://github.com/voltybat/WaveLinkSettingsUtility) at
  `main` (pushed 2026-07-19). The exact upstream commit will be recorded here on merge, so
  [the audit](_docs/audits/2026-08-15-voltybat-wavelinksettingsutility.md) can be re-run
  against a known base.
- The four-project solution layout, and CI enforcing that `WaveLinkBackup.Core` cannot
  reference `PresentationFramework` or `System.Console`.

---

## Release checklist

Not a version. A gate, recorded here because the first release is the moment it stops being
optional.

Before any public release:

- [ ] **Redacting "copy diagnostics" action.** Settings files contain hardware serial numbers
      and the Windows username, and users attach backups to bug reports without thinking about
      it. This gates going public rather than following it — see
      [`technical-debt.md`](_docs/technical-debt.md) §6.
- [ ] **Packaging decision made deliberately** — self-contained, framework-dependent, or
      NativeAOT for the CLI. Upstream ships `PublishSingleFile` with `SelfContained=false`, so
      a user who downloads one `.exe` and double-clicks it currently gets an error.
- [ ] **MIT attribution** preserved for upstream, in `LICENSE` and `README.md`.
- [ ] **Windows-only stated above the fold** in `README.md`.
- [ ] **The VST3 bundle path covered by a fixture test.** It cannot be exercised by the
      author's machine and will silently capture nothing if wrong.

<!-- Add the [Unreleased] compare link here once the repository has a remote. -->

