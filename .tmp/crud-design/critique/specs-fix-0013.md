# Fixes applied to spec 0013 (2026-09-04)

Source: `specs-review-A.md` §"Spec 0013" (7 findings) and its cross-spec entry; `specs-review-B.md` names 0013 only in a 0010 finding (`TenantBootstrapRequest.Email`), which is 0010's to fix.

## Applied

- A-0013-1 (blocking) — `IAccessGuards.Contribute` split into `ContributeLock(batch)` and `ContributeInvariants(batch)`; §8.3 states the pipeline (spec 0014) calls the first after the §7.4 re-checks and before the emitter's first statement, the second after the emitter's statements and every `ContributeAsync` contribution, before the post-check and the bumps; SQL block relabelled; §7.3 body comment updated. Cross-spec: seams §11/§1.6, 0014 §6.7 step 4 and 0011 §5.5 must restate the two positions and the pipeline as caller.
- A-0013-2 (major) — `MaxCachedUsers` and `CacheSlidingExpiration` dropped from `AccessOptions`; the `permissions` kind is sized by `TellmaCacheOptions.PermissionsEntries` with no `MaxAge` (§6.2, §12.1); the connect cache (not a `VersionedCache` kind, per 0012) is bounded by the same entry limit (§6.3); review flag 33 restated for the choice as it now stands. Cross-spec: seams §11.2 `AccessOptions` block must drop the two members.
- A-0013-3 (minor) — `THROW 50401, N'CallerInvalid'` in §7.4 and Appendix A.
- A-0013-4 (minor) — §5.3 post-check: message `RowSecurity`, mechanism left to spec 0014 ("a `50403` count assertion over `@tb{b}_saved`"); Appendix A row renamed and attributed to 0014's post-check.
- A-0013-6 (minor) — §3.3: the "re-created by the cold connect path" sentence replaced; the companion insert and `HasData` are the only writers, and the inner join treats a missing row as an unknown caller.
- A-0013-7 (minor) — §1.2 states the two `Entity` lines are the one registration of the `User`/`Role` stacks and spec 0017 adds no `Entity` line for them.

## Rejected / not applicable in this spec

- A-0013-5 (minor) — kept "spec 0010's migrator invokes `IPermissionDriftScanner.ScanAsync` after `migrate`": the spec text is the intended design; the drift is in 0010 §6.1 (`migrate` row) and seams §11, which must add the invocation and the contract. Moving the scan to `status` would weaken the post-deploy guarantee this spec relies on.
- B cross-spec (0010 §6.1 `TenantBootstrapRequest(AdminEmail, …)`) — 0013's shape `(Email, Name, PreferredLanguage, Subject)` matches seams; the fix belongs to 0010.
- A cross-spec (0020 `NotificationPreferences` cascade) — consistent with §3.9; no change.
