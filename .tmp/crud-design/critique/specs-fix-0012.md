# Spec 0012 — findings applied and rejected

## Applied

- A-0012-1 (major) — `core.settings` step kept; §5.7 states it sits among spec 0010's platform steps between `core.bootstrap-administrator` (10) and `core.blob-container` (20); the placeholder shape stays stated once (§5.2 `HasData`, §5.7). Cross-spec: 0010 §6.2 already lists `15 core.settings` (specs-fix-0010); seams.md §18 still needs the row.
- A-0012-2 (minor) — §6.3 gains step 6: when `Name` changed, spec 0010's `ITenantCatalog.RenameAsync(tenantId, name)` runs post-commit through the batch's `OnCommitted` hook, failure logged never surfaced; §12 Settings bullet pins it.
- A-0012-3 (minor) — §2.7 now cites spec 0013's `UserAccess.FormatVersion`, carried by spec 0015's `AccessSummary.FormatVersion`; `UserProfileView.FormatVersion` dropped.
- A-0012-4 (minor) — §2.9 names `Dependencies: list<VersionTagDependency>` as the member carrying the mismatched dependencies.
- A-0012-5 (minor) — §5.2 `Id` row: no opt-out invented; the sequence `core.sq_Settings` that 0011's convention emits exists and is never consumed (`HasData` row, `CK_Settings_Id`). Both specs now describe the same migration with no new 0011 declaration.
- A-0012-6 (minor) — §5.3 illustration reduced to the two `SettingKey<T>` field declarations; the enclosing class is named in prose.
- B-cross-0012 (a) (`cache.changed` has no publisher) — §2.5 gains the rule: the epilogue contributor calls `IClientEventPublisher.Publish(batch, ClientEvent("cache.changed", [], { tag: <name> }))` for every bumped `settings` or `entity:<Name>` name (post-commit, every connected user); `permissions`/user-level bumps publish nothing; §6.4 `refresh-caches` publishes for `settings` and every composed `entity:*`; §12 and §13 updated. 0020 §6.3's publisher row now has a source.
- B-cross-0012 (b) (`<Entity>_Plural`) — §7.1 `EntityLabel(entityType, plural: bool = false)`; §7.6 lookup row `<Entity>_Plural` → base assemblies → the singular label; §10.1 key list names `<Entity>` and `<Entity>_Plural`. Cross-spec: seams.md §22 `ILabelProvider.EntityLabel` needs the optional `plural` parameter.

## Rejected / no change

- A-0012-7 (minor) — no change: 0013 dropped `AccessOptions.MaxCachedUsers`/`CacheSlidingExpiration` and reads `TellmaCacheOptions.PermissionsEntries` (specs-fix-0013), so §3.2's `permissions` row already states the surviving reading.
- A-0014-8 (minor, names 0012 §2.6) — no change here: §2.6 gives `settings` the `Refresh` policy as the finding assumes; the fix belongs to 0014 §16.1's tag set.
- B-cross-0012 (c) (`core.settings` absent from seams.md §18 and ledger §2.23) — not a spec change; seams.md needs the row `15 core.settings (Version = 1)`.
