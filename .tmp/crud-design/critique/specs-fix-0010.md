# Spec 0010 — fix log (2026-09-04)

Source: `specs-review-A.md` §"Spec 0010" (7 findings) and the cross-spec entry naming 0010; `specs-review-B.md` has no 0010 section and no cross-spec entry addressed to 0010 (its 0017-3 and 0019-3 mentions require no 0010 change).

## Applied

- A-0010-1 (blocking) — `ITenantScopeFactory.CreateScopeAsync(snapshot, allowNonActive: bool = false)`: the flag admits `Provisioning` and `ReadOnly` (never `Suspended`/`Retired`), requires `Kind = System` (else `InvalidOperationException`), and is passed only by the migrator's step runner. §3.3 verdict rows for `Provisioning` and `ReadOnly` name the exception; §4.4 defines the mechanism; §6.2 cites it; §10 integration suite pins it. Named `allowNonActive` rather than the reviewer's `allowProvisioning` because `migrate` also runs behind-version steps on `ReadOnly` tenants (§6.1), which the narrower name would not cover. seams.md §9/§18 need the same member.
- A-0010-2 (minor) — `Subject` in §3.8 (`TenantMembershipRecord`), §7.3 (`TenantMemberships` and the `TenantMembershipList` type) and §7.4 (`Sessions`) is now `varchar(255) COLLATE Latin1_General_100_BIN2`, matching ledger §1.16 and 0013 §3.1. seams.md §9 should follow.
- A-0010-3 (minor) — `provision` adopts `--tenant <id>` (requested id with `--name`/`--category`; resume of an existing `Provisioning` row alone); `--id` removed from §6.1 and §6.3. seams.md §18 keeps `--tenant`; its command is a subset of the spec's.
- A-0010-4 (minor) — §1.2 `reservedSlugs` no longer names the architecture document.
- A-0010-5 (minor) — §5.2 anonymous list drops `logout` (cookie-authenticated per §5.5).
- A-0010-6 (minor) — Review flag 19 reads "added to the reserved list" (ledger S39; `reference` is not on the platform list, and a platform-taken slug joins it, as `acme` does).
- A-cross (0012 `core.settings`) — §6.2 platform steps now list `15 core.settings` between the bootstrap and blob-container steps.

## Rejected / no change

- A-0010-7 (minor) — `RenameAsync` row in §3.7 kept as is: the spec is the seam owner and the row states the caller; the finding itself defers the fix to 0012-2 (add the post-commit call in 0012 §6.3). Removing the caller here would leave the mirror with no writer.
