# Fix pass — specs 0011, 0012, 0013, 0014

One line per change, in the form "§target — what changed, in plain words — why (needed by spec 00NN)".
Finding ids follow each line.

## Spec 0011 (`docs/specs/0011-distribution-host-and-multitenancy.md`)

- §5.1 `UseTellma()` bullet — the pipeline now starts with the header stamp, which puts `Tellma-Trace-Id` and `Tellma-Build` on every response so early refusals (CSRF 403, 412, 429, tenant 404) carry them; the bullet was reflowed — needed by specs 0016 §6.2, 0024 §5.6, §10.6 (A7 / addendum 7, K-foundation-7).
- §5.1 `MapTellma()` relay sub-bullet — the relay's policy, tenantless reason, rate policy, body limit and tracing exclusion are no longer restated; the bullet says it is mapped as spec 0026 §6.1 defines it — needed by spec 0026, which owns the relay (K-foundation-37).
- §6.2 `ITenantProvisioningStep.Order` comment — bands are now "platform 0–99 and 900+, packs 100–199, distributions 200–899" — needed by spec 0033 §13 (K49 / addendum 4, K-reports-6, F-7).
- §6.2 platform step list — `core.report-seeds` moves from order 30 to 900, and one sentence makes 900 and above the band for platform steps that must run after every pack and distribution step, so a seed's roles exist — needed by spec 0033 §13 (K49 / addendum 4, K-reports-6, F-7).
- §8 jsonc and key table — added `"Copyright": null` and a `Copyright` row: the About dialog's copyright line, copied by `emit-client-assets` into the client metadata's `distribution.copyright`; null omits the line — needed by specs 0024 §3.1, 0027 §11.1 (K7, K-foundation-13, K-shell-entity-13, F-26).
- K5 — verified resolved (§5.1 SPA bullet and §5.2 cite spec 0024 §6 for the metadata chunks behind the cookie); no change.

## Spec 0012 (`docs/specs/0012-data-access-layer.md`)

- §5.2 `IDataBatch` sketch — added `BatchResult<QueryRowSet> EvaluateConstant(IReadOnlyList<ConstantItem> items, int languageVersion)`, which runs spec 0010 §F.11's constant statement and returns its one row — needed by spec 0033 §4.3, §8.3 (K50, K-reports-2, F-1).
- §5.2 member table — added the `EvaluateConstant` row, pointing at §11.3 for compile and bind — needed by spec 0033 (K50, K-reports-2, F-1).
- §11.2 — the list of Part F amendments this spec relies on gains constant statements (§F.11) — needed by spec 0010 §F.11, which names `IDataBatch.EvaluateConstant` as the host member (K50).
- §11.3 — heading is now "Rows, Count and constant statements"; a new paragraph says `EvaluateConstant` compiles under the given language version with `BatchOrdinal = b`, binds `Today`, `Now`, `UserId` and `TimeZone` from the same `QueryexContextValues` as `Rows` (one `today()` for a default and its query), and returns one row with one column per item — needed by specs 0010 §F.11, 0033 §4.3 (K50, K-reports-2, F-1).
- §13.3 Queries bullet — added the pin that `EvaluateConstant` beside a `Rows` in one batch returns one row whose `today()` equals the query's — needed by spec 0033 §4.3 (K50).
- Conflict: F-1 names the member `IDataBatch.Constant(items, ConstantCompilationOptions?)` returning `RawResult`; K-reports-2 names it `EvaluateConstant(items, int languageVersion)` returning `QueryRowSet`. Followed K-reports-2: K50 says "name it to match §F.11", and spec 0010 §F.11 and spec 0033 §4.3, §8.3 already cite `IDataBatch.EvaluateConstant`.

## Spec 0013 (`docs/specs/0013-settings-localization-and-cache.md`)

- §7.6 "Key-resolving twins" — one sentence says spec 0029's emit adds two chains of its own, for the create label and an action's argument fields, citing spec 0029 §2.4 and §10.1 for the keys — needed by spec 0029 §2.4 (K11, K-shell-entity-8, F-26).
- §10.1 key conventions — new bullet "Client entity labels": `<Schema>_<Entity>_New` with Core's `Entity_New`, `<Schema>_<Entity>_Action_<Name>_<Field>`, and Core's `Entity_Level` ("Level {j}") — needed by spec 0029 §2.4, §4.3 (K11, K-shell-entity-8, F-26).
- §10.1 key conventions — new bullet "The boot page": `Boot_*`, in Core — needed by spec 0024 §4.2 (K-foundation-31).
- §10.1 "Reports and dashboards" bullet — added `Core_ReportDefinition_Synonyms` and `Core_Job_ExportReport` (spec 0019 §12.2) — needed by specs 0033 §12.1, 0034 §2.3 and addendum 9 (K-reports-30).
- §12 Resource audit bullet — keys derived by convention now include create and action-field labels, so a pack's `<Schema>_<Entity>_New` is not an orphan; the bullet was reflowed — needed by spec 0029 §2.4 (follows K11).
- Conflict: K-shell-entity-8 adds the two chains as new `ILabelProvider` rows (`NewLabel(T)`, action field) in the §7.6 table; F-26 asks only for the keys, citing spec 0029 §2.4 as the resolver. Followed F-26: spec 0029 §2.4 owns the chains and resolves them in its emit, so new table rows would restate them and imply `ILabelProvider` members spec 0029 never calls.

## Spec 0014 (`docs/specs/0014-users-roles-and-permissions.md`)

- §5.3 self-scope bullet — support requests join users, jobs, exports, imports, notifications and assignments, with spec 0026 §12.3 (`SupportRequestAccessCriteria`) beside the other providers; the bullet was reflowed — needed by spec 0026 §12.3, §12.6 (A2 / addendum 2, K-foundation-8, F-20).
- Choice: F-20 cites spec 0026 §12.6 (Access); K-foundation-8 cites §12.3. Cited §12.3, where the `IAccessCriteriaProvider` is declared, matching how the bullet cites the other owners' providers.
