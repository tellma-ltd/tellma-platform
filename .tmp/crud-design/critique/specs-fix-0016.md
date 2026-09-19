# Fix record — spec 0016 (`docs/specs/0016-blob-storage.md`), 2026-09-04

Findings from `specs-review-B.md` §"Spec 0016" and its Cross-spec section; `specs-review-A.md` has no 0016 entry.

## Applied

- B-0016-1 (blocking) — §4.4 now cites spec 0014 §13.1 (already amended per `specs-fix-0014.md` "B cross (a)") as the owner of the rule that `ContributeAsync` runs for every `Persist` batch on the stack's table (save, the three deletes, `activate`/`deactivate`, every action) with `Entities` empty and `SavedIds` the deleted key table on deletes; flag 20 names that 0014 obligation and what depends on it (owner deletes, `core.file-retention`).
- B-0016-4 (minor) — §4.4 Placement: "spec 0011's persist assembly" → "spec 0014's persist assembly (first among the `ContributeAsync` participants)"; the access guards added to the ordering list, matching 0014 §6.7.
- B-0016-5 (minor) — flags 3, 15, 18: ledger ids `S16`, `S25`, `S36` deleted.
- B-0016-6 (minor) — flag 2 reworded without the author's name or a to-do.
- B-0019-1 (major, cross-spec) — §7.4 `core.blob-reconcile` cron aligned to 0019 §7.9's built-in table as it stands (`0 1 * * 0`, Sunday 01:00) and 0019 §7.9 named as the table of record; if 0019 later swaps its two Sunday slots, §7.4 follows it.

## Rejected / no spec change

- B-0016-2 (minor) — container name `{ContainerPrefix}t{tenantId}`: the reviewer prefers the spec's form; the correction belongs to seams.md §12.3, so the spec is unchanged.
- B-0016-3 (minor) — `BlobOptions` without `SweepInterval`, `BlobDownload.State`, `IBlobStore.EnsureTenantAsync`, `deleted.[State]` in the claim's `OUTPUT`, `StagingTtl` kept: all owner deltas to absorb into seams.md §12; the spec already carries them, so no change.
- B cross 0014(a) — same as B-0016-1; owned and already applied in 0014.
- B-0018-2 (cross-spec, `inspect-import` round-trip count) — 0018's fix; 0016 §3.1/§5.1 are the reference and stay as they are.
