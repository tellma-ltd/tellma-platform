# Fix record — spec 0018 (Excel codec)

Review A has no `## Spec 0018` section; its cross-spec entry reports no drift for 0018. All findings below are review B's.

## Applied

- B1 (major, §1.3 `Stamp` wire string) — trailing `Z` dropped; the stamp is spec 0015's `DateTime` encoding `yyyy-MM-ddTHH:mm:ss.fffffff` (UTC by convention, no offset, no designator).
- B2 (major, §1.4 `inspect-import` round trips) — row set to 1: the staged blob's `IBlobService.ResolveAsync` read (spec 0016's `Read` batch) with the prologue riding it; the "prologue rides the file-open check only when cold" clause deleted.
- B3 (minor, §12.1 retention) — aligned to 0019 §14.4: ids selected in pages of 500 and deleted through `DeleteByIdsAsync` on both stacks as the system user.
- B4 (minor, §12.3/§12.6 notification arguments) — `core.export.ready` `{ fileName, rowCount }`; `core.import.completed` `{ fileName, rowCount, errorCount }`; `core.import.failed` `{ fileName, errorCode }` (first error's code), recipients and target stated; both sections say the names are spec 0020's catalogue entries.
- B5 (minor, §2.1 undeclared `TKey`) — `service ExcelOperations<TEntity> where TEntity: Entity`; the name `ExcelOperations<TEntity>` (seams.md §19) is unchanged.
- B6 (minor, §12.5) — `ImportCheckpoint.Progress.Append(context.Batch, …)`.
- B7 (minor, §3.3 plural sheet names) — sheet names come from spec 0012's `EntityLabel(entityType, plural: true)` (the `<Entity>_Plural` key with the singular fallback), matching the 0012 fix (specs-fix-0012, B-cross-0012 (b)).
- B8 (minor, §1.2 companion mechanism) — `ExcelOperations<TEntity>` is attached as a stack companion through `contribution.EntityCompanion<TEntity, ExcelOperations<TEntity>>()` (0014's `StackCompanionContributionItem`), projected on stacks with `Query` (export actions) and `Save` (import actions); §1.1, §2.2 and decision 2 now say "stack-companion contribution"/"the companion's four methods".
- specs-fix-0014 A-1 cross-note (`@tb{b}_keys` in 0018 §4) — no edit needed: 0018 names only `@tb{b}_saved` (§13), never `@tb{b}_main`.
- specs-fix-0019 B2 — no edit needed: 0018 §12.3 is the reference (`MaxAttempts = 3`, own persist round trip) and stays as written.

## Rejected

- None.
