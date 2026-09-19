# Spec 0014 — findings applied and rejected

## Applied

- A-1 (major) — `DetailsPlan<TEntity, TKey>` contract block + Members table added to §5.3; `IdsSource` kept as `@tb{b}_keys` (spec 0011's reserved, executor-captured name; `@tb{b}_main` exists in no reserved list). seams §3.3, 0017 (line ~440) and 0018 §4 must say `@tb{b}_keys` — the identifier is opaque to contributors anyway.
- A-2 (major) — fixture model restated verbatim from 0011 §13.2 (plus 0012's `fixture.Lookups`), located in `TellmaFixtureDbContext`; this spec adds only `fixture.Gadgets` (`[Stack(Operations = Read)]`, `[Cacheable]`, `[Searchable]`), the stack registrations, `WidgetService`, the three components and one companion. §1.1, §18.2, §19 updated; `test/shared/Tellma.Testing.Entities` removed.
- A-3 (minor) — §6.7 step 4: the pipeline (not the executor) calls `IAccessGuards.Contribute` once participants have declared writes, for save/action/delete persists, except `Kind = System`; spec 0013 places the lock ahead of the first write and the invariants after the last. §10.1 names the guards. Contract kept as one member (0013 has not split it).
- A-4 (minor) — §11.1 `@tb{b}_vis` block dropped; the fast path requests `UpdateSpec { …, AllOrNothing = true }` and relies on 0011 §9.1's assertion and missing-ids result set. Delta for 0011 §9.1 / seams §1: add `AllOrNothing: bool = false` to `UpdateSpec` with the missing-ids result set before the `THROW 50404`.
- A-5 (minor) — §7.2 illustration cut to three lines (loader call + error add); the loop and payload-parent lookup moved to prose.
- A-6 (minor) — `ConcurrencyException("concurrency-conflict", conflicts)` in §8 and §14.2.
- A-8 (minor) — `crud.tag` closed set is `permissions` only (§16.1), with the reason cross-referenced to §5.1.
- B cross (a) — §13.1 states `ContributeAsync` runs for every `Persist` batch on the stack's table (saves, all three deletes, activate/deactivate, actions) with `Entities` empty and `SavedIds` the deleted key table on deletes; §11.1 fast path lists the participants; decision 23 added.
- B cross (b) — §11.2 "Partial failure" rule: a method throwing `PartialFailureException(Code, Results, Failed)` after composing has RT2 executed with what it composed, post-commit run, and the exception rethrown; any other exception abandons the batch. 0017 §3.3 must adopt the `(Code, Results, Failed)` shape.
- B cross (c) — stack companion mechanism: `StackCompanionContributionItem(EntityType, CompanionType)` via `contribution.EntityCompanion<TEntity, TCompanion>()`; `ActionDescriptor.HandlerType: Type` added; §2.2, §2.3, §2.4, §2.6 (startup checks), §2.7, §11.3, §12 updated; decision 22 added. Deltas: seams §… add the item, `HandlerType` and the `EntityCompanion` sugar; 0015 §4.4 must invoke `HandlerType` rather than "the service"; 0018 §1.2 should say "attached as a stack companion".

## Rejected

- A-7 (minor) — no change in the spec; the ledger's two entries are the ones to correct (`ValidationException(Tree.TooDeep)`), as the finding itself says.
- B cross (d) — 0014 never restates `IdsRequest` (the service takes `ids` and `arguments` separately); nothing to add here. `Arguments` belongs in seams §13.1 and 0015 only.
