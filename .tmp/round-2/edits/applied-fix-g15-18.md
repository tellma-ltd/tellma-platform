# Applied fixes — specs 0015, 0016, 0017, 0018 (consistency and final-review pass)

One line per change: finding — §target — what changed, in plain words — why (needed by spec 00NN).

## Spec 0015 (`docs/specs/0015-crud-service-pipeline.md`)

- A3 / K-reports-7 / F-2 — §5.3 step 3 — the step now names two forms of extra: a statement (`AddExtra`, as before) and a computed extra (`AddComputedExtra`), C# that runs after the batch over the loaded entities and its own `AddRead` results, adding no round trip — needed by specs 0033 (§5.2 `permissions`), 0035 (§6.1 `view`), 0032 (§5.5), 0018 (§4.3 `problems`), 0020 (§7.8 `permissions`)
- A3 / K-reports-7 / F-2 — §5.3 `DetailsPlan` sketch — adds `AddRead(QuerySpec, QueryArguments?)` and `AddRead(FormattableString, SqlOptions?)` returning `BatchResult`s that are never sent, and `AddComputedExtra<TValue>(name, Func<IReadOnlyList<TEntity>, TValue>)` — needed by specs 0033, 0035
- A3 / K-reports-7 / F-2 — §5.3 member table — `AddRead` and `AddComputedExtra` rows; the `AddExtra` row's duplicate-name rule covers both forms — needed by specs 0033, 0035, 0018
- A3 — §3.1 member table, `Extras`/`ContributeDetails` row — "the statements" becomes "the statements or computed extras" — consistency with §5.3
- A3 — §18.2 pins — adds the `IDetailsContributor<Widget>`'s computed extra built from one `AddRead` result inside the details round trip — pins the new contract
- A3 — Decisions record — new item 34: an extra SQL cannot express is computed in C# after the details batch — keeps the record in line with §5.3
- K14 / K-shell-entity-10 / F-26 — §11.2 "Metadata only" bullet — says the client metadata carries the entity per argument field as `TmActionMetadata.fields[].value.target`, which the generated dialog reads; the claim that the JSON Schema marks the property is gone — needed by specs 0029 (§2.2), 0031 (§10.4)

## Spec 0016 (`docs/specs/0016-crud-web-api.md`)

- K13 / K-shell-entity-3 — §11.5 `tellma_describe` row — with `entity` the tool renders the server's metadata (spec 0012 §3.1's `EntityMetadata` with spec 0015's `StackDescriptor`), not the client entity model; the reference to spec 0029 §2.1 is gone (the row already lists store types and the default select) — needed by spec 0029
- K-reports-14 — §4.6 — the startup audit logs `ApiChildSurfacesProjected` once at Information, listing every projected child query surface with its owner — needed by spec 0033 (§10)
- K-reports-14 — §10.1 log events — adds `ApiChildSurfacesProjected(surfaces)` at Information at startup — the event §4.6 names
- Addendum 8 — §4.1 projection paragraph — the child surface's `query` calls `IChildQueryService.QueryAsync(resource, request)` (spec 0015 §5.2) — needed by spec 0033 (§10)
- Addendum 8 — §11.5 opener — `tellma_query` over a child entity goes through `IChildQueryService.QueryAsync`, pointing at §4.1 instead of spec 0033 §10 — needed by spec 0033
- Addendum 7 — §6.2 opener — the early refusals that carry `Tellma-Trace-Id` and `Tellma-Build` now include tenant 404, matching spec 0011 §5.1 — needed by spec 0024 (§10.6)

## Spec 0017 (`docs/specs/0017-blob-storage.md`)

- A2 / K-foundation-8 — §5.2 — `support-bundle` blobs are read by the request's submitter and by holders of `core.SupportRequest × Read` — needed by spec 0026 (§12.3, §12.6)

## Spec 0018 (`docs/specs/0018-core-and-gl-reference-stacks.md`)

- K44 / addendum 5 / K-shell-entity-2 / F-25 — §1.4 `AcmeFeature` sketch — `Contribute` also registers `NotificationType(AcmeNotifications.InvoiceAssigned)` — needed by spec 0028 (§3.6)
- K44 / F-25 — §1.4 table, `SalesInvoice` row — adds `[Assignable]`, the server-owned `IsPosted` column, and "assignments (spec 0028)" under Exercises — needed by spec 0028 (§3.5, §13)
- K44 / F-25 — §1.4 — new bullet "`assign` and `post`": both `[EntityAction]`s are written as spec 0028 §3.5 shows; `assign` takes `AssignInvoiceArguments` with `[EntityRef("core.User")]`; `post` takes no arguments, sets `IsPosted` and completes the open assignment — needed by specs 0028, 0031
- K44 / F-25 — §1.4 — new bullet "`acme.invoice.assigned`": a channel-only type (category `sales`) that `assign` raises for the assignee — needed by spec 0028 (§3.6)
- K44 — §1.4 "Development data" bullet — says the step also writes demo invoices and assignments (§6.3) — needed by spec 0028
- K44 — §6.3 `acme.sample-data` bullet — the step's writes become a list and gain 20 sales invoices with lines over the sample centers; it then assigns three invoices to the administrator through `IAssignments.AssignAsync` — needed by spec 0028
- K44 — §1.3 seams table — adds `[EntityRef]` to spec 0015's row, a spec 0028 §3.1 row (`[Assignable]`, `ActionContext.Assignments`, `IAssignments.AssignAsync`), and `NotificationTypeDescriptor` to spec 0021's row — keeps the table in line with §1.4 and §6.3
- K44 — §9 Acme integration row — pins the invoices, the administrator's three open assignments, the `Sales` grant of `acme.sales-by-center`, and `assign`/`post` — pins the new development data
- F-2 (part 2) — §4.3 `members` and `problems` bullets — `members` is one `AddExtra` query; `problems` is a computed extra (`DetailsPlan.AddComputedExtra`, spec 0015 §5.3) — needed by spec 0032 (§5.5)
- F-2 — §1.3 seams table — the `DetailsPlan` members used add `AddComputedExtra` — consistency with §4.3
- K49 / addendum 4 / K-reports-6 — §6.3 step list — `core.report-seeds` moves after `gl.sample-centers` and `acme.sample-data`; the "Reports" bullet says it runs last and writes the seed's grant to `Sales` — needed by spec 0033 (§13)
- K49 — §5.6 — "after the platform steps" becomes "after the platform steps below 100", since `core.report-seeds` (900) is a platform step that runs later — consistency with spec 0011 §6.2
- K-reports-32 — §1.3 seams table — `ReportSeed`, `ShareAccess`, `IReportResults` cite spec 0033 §2.1, §11, §13 (was §5.1) — `ShareAccess` is declared in spec 0033 §2.1

## Deviations and skips

- K-shell-entity-2's development-data text ("two open assignments and one return for the demo users") is not used: spec 0018 seeds no demo users (spec 0032 §14 has the e2e fixtures create them), so the step assigns three invoices to the administrator and writes the invoices those assignments need; a return needs a second user and is left to the e2e fixtures. K44 (binding) asks only for "a few demo assignments in the sample-data step".
- F-2's shape `AddComputedExtra<TValue>(name, Func<DetailsResult<TEntity>, TValue>)` reading `AddExtra` result sets is not used: spec 0035 §6.1's summaries and spec 0033 §5.2's per-decision id sets are inputs that must not be sent as extras. K-reports-7's `AddRead` keeps them private; F-2's typed value (`TValue`) is kept because `DashboardView` and `SharePermissions` are records, not row sets, and `EntitiesResult.Extras` holds `JsonElement`s.
- K-reports-7's member-table sentence listing spec 0033 §5.2 and spec 0018 §4.3 is cut to one example (spec 0018 §4.3), since consumers cite the owner, not the reverse.
- K-shell-entity-3's parenthetical "store types and the default select included" is not added to §11.5: the same row already lists both.
- K-crud-9's alternative (seed users and a drifting role in spec 0018 §6.3) is not applied: the primary fix (e2e fixtures in spec 0032 §14) needs nothing in 0018.
- K5 (metadata chunk caching): already resolved in 0016 §8.5, which cites spec 0024 §6; no change.
- Addendum 10 (`activity` extra on the users stack): already in 0018 Goals, §1.2 and §3.1; no change.
