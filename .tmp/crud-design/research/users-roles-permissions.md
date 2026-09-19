# Research: Users, roles, and permissions (theme T4 → spec 0013)

Verified on 2026-09-01 unless a finding says otherwise. Each finding is tagged **[verified]** (read in a
primary source or in this repo), **[community]** (consistent non-Microsoft sources, no primary doc found),
or **[inference]** (my reasoning from verified facts). Source lists put primary sources first.

## 0. Version baseline

- **.NET 10 / ASP.NET Core 10**: GA 2025-11-11 (10.0.0); latest patch **10.0.11, released 2026-08-11**; LTS,
  supported to 2028-11-14. [verified] — https://github.com/dotnet/core/blob/main/release-notes/10.0/README.md
- **SQL Server 2025 (17.x)**: GA 2025-11-18, GA build 17.0.1000.7. [community-verified: Microsoft's release-notes
  page (updated 2026-08-19) points to the build-versions page, which I did not fetch; the date and build come from
  the search summary of that page and sqlserverbuilds.] — https://learn.microsoft.com/en-us/sql/sql-server/sql-server-2025-release-notes,
  https://learn.microsoft.com/en-us/troubleshoot/sql/releases/sqlserver-2025/build-versions
- Row-level security, `SESSION_CONTEXT`, sequences, temporal tables and change tracking are all available on
  Azure SQL Database (elastic pools included), SQL Server 2016+, and LocalDB — every Tellma target. [verified via
  the "Applies to" banners on each page cited below]
- EF Core 10.0.9 and Microsoft.Data.SqlClient 6.1.1 are fixed by the briefing; not re-verified here.

---

## 1. SQL Server native row-level security versus application-composed filters

### 1.1 What native RLS is (mechanics) [verified]

- RLS = `CREATE SECURITY POLICY` binding an **inline table-valued function** (must be created `WITH SCHEMABINDING`
  when the policy has `SCHEMABINDING = ON`, the default) to a table as a **filter predicate** (silently filters
  `SELECT`/`UPDATE`/`DELETE`) and/or a **block predicate** (`AFTER INSERT`, `AFTER UPDATE`, `BEFORE UPDATE`,
  `BEFORE DELETE`; raises an error). At most one enabled policy per table per DML operation; only one enabled
  policy may target a table at a time.
- Filter predicates are "functionally equivalent to appending a `WHERE` clause"; the application is unaware rows
  were filtered (all-filtered returns an empty set). Rows can be inserted that the inserter cannot then read.
- "Security policies apply to all users, including dbo users"; sysadmin/db_owner see filtered rows unless the
  predicate lets them through — the predicate must code the bypass.
- Schema-bound policy blocks `ALTER` of columns the predicate references; adding a predicate to a table that
  already has one for that operation errors; altering the predicate function errors while bound.
- `AFTER UPDATE` block predicates are skipped when the predicate's columns were not touched. Block predicates
  are evaluated after the DML runs, so `READ UNCOMMITTED` readers may see transient values.
- Sources: https://learn.microsoft.com/en-us/sql/relational-databases/security/row-level-security (ms.date
  2025-09-11, updated 2026-07-20); https://learn.microsoft.com/en-us/sql/t-sql/statements/create-security-policy-transact-sql
  (ms.date 2026-04-08).

### 1.2 `SESSION_CONTEXT` for middle-tier apps [verified]

- Microsoft's documented pattern for apps where all users share one SQL login: after opening the connection the
  app runs `EXEC sp_set_session_context @key=N'UserId', @value=<id>, @read_only=1`; the predicate reads
  `CAST(SESSION_CONTEXT(N'UserId') AS int)` and additionally checks `DATABASE_PRINCIPAL_ID() = DATABASE_PRINCIPAL_ID('AppUser')`.
- `@read_only = 1` pins the key "until the connection is closed (returned to the connection pool)". Key ≤ 128
  bytes, value is `sql_variant` ≤ 8,000 bytes, whole context ≤ 1 MB. Any user can set/read their own session
  context. Under MARS, values set in one batch are not visible to concurrently active batches and cannot be set
  read-only.
- Known issue: an access violation / wrong results with `SESSION_CONTEXT` inside parallel plans when a session is
  reset for reuse (SQL Server 2019 CU14+; trace flag 11042 workaround, forces serial evaluation). Status "has
  workaround", not resolved, as of the page's 2026-08-24 update.
- Pooled connections are reset with `sp_reset_connection` on reuse (SqlClient, `Connection Reset` keyword,
  default true), which is what makes `@read_only` safe with pooling. [verified that pooling resets connection
  state; that the reset clears session context specifically is stated by Microsoft's Data API Builder RLS page
  and community sources, not by the SESSION_CONTEXT page itself — treat as **[community]** for that detail.]
- Sources: https://learn.microsoft.com/en-us/sql/relational-databases/system-stored-procedures/sp-set-session-context-transact-sql
  (ms.date 2025-06-23); https://learn.microsoft.com/en-us/sql/t-sql/functions/session-context-transact-sql
  (ms.date 2025-07-07); https://learn.microsoft.com/en-us/sql/connect/ado-net/sql-server-connection-pooling;
  https://learn.microsoft.com/en-us/azure/data-api-builder/concept/security/row-level-security.

### 1.3 Performance characteristics [verified unless marked]

- Microsoft best practices: avoid type conversions, recursion and "excessive table joins in predicate functions";
  predicates must not depend on session `SET` options (they can leak rows). Full-text functions take an extra
  join under RLS; columnstore may lose batch mode "because row-level security applies a function"; **indexed
  views cannot be created on RLS-bound tables**; `DBCC SHOW_STATISTICS` is restricted; CDC/Change Tracking can
  leak keys; **temporal tables: the predicate is not replicated to the history table** and must be added
  separately (confirmed on the temporal limitations page: "Row-level security predicates" are in the list of
  objects not copied to the history table).
- Red Gate (Ben Johnston, 2023-09-10) measured lookup methods over 1M executions: `SESSION_CONTEXT` was the
  fastest by far (differences under 300 ms per million); "every join added to a predicate degrades performance
  at scale"; keep predicates "simple, flat, and well-indexed"; index both sides of predicate joins; denormalize
  the filter column into the fact table where possible; `UNION` beats `OR` inside predicates; disable the policy
  to isolate its cost when troubleshooting; ETL/service accounts must be granted full visibility or anti-join
  deletes go wrong. **[community]**
- Side channels: Microsoft's own page documents exfiltration via error-based queries
  (`SELECT 1/(SALARY-100000) ...`) and via a malicious policy manager; RLS does not close these. [verified]
- Sources: RLS page above; https://www.red-gate.com/simple-talk/databases/sql-server/sql-server-rls-performance-and-troubleshooting/;
  https://learn.microsoft.com/en-us/sql/relational-databases/tables/temporal/considerations-limitations
  (ms.date 2026-08-18).

### 1.4 Where Microsoft positions RLS [verified]

- The Azure SQL SaaS tenancy guidance (ms.date 2025-08-21, updated 2026-08-27) recommends RLS for the
  **multitenant-database** pattern ("During development, ensure that queries never expose data from more than
  one tenant. SQL Database supports row-level security, which can enforce that data returned from a query be
  scoped to a single tenant"). For **database-per-tenant** — Tellma's fixed model — it lists strong isolation
  as inherent and does not suggest RLS. https://learn.microsoft.com/en-us/azure/azure-sql/database/saas-tenancy-app-design-patterns

### 1.5 Arguments for native RLS (collected)

1. Defense in depth across every access path: SSMS, BI, tier-2 `SqlBuilder<T>` SQL, raw `Sql(...)` escape
   hatches and future integrations are all filtered "from any tier" (RLS page). [verified]
2. Block predicates guard writes that the app might forget (insert into a partition you cannot read). [verified]
3. `SESSION_CONTEXT` lookup is cheap; a flat equality predicate on an indexed column is a normal seek. [verified/community]
4. The policy is declarative and auditable in one place (`sys.security_policies`). [verified]

### 1.6 Arguments against native RLS for Tellma (collected)

1. **It is logic in the database.** ARCHITECTURE.md: "no logic lives in the database except in exceptional,
   documented circumstances: C# deployments are reliable and cleanly reversible (slot-swap) in ways SQL
   deployments are not." A predicate function is exactly such logic, per table, per tenant DB. [verified in repo]
2. **The filters Tellma needs are not expressible as a static predicate.** Permission filters are user-authored
   Queryex expressions with navigations (`Customer.Region.Code = 'X'`), `descendantOf(...)`, `me()`/`today()`,
   OR-merged across roles, plus bespoke service criteria ("assigned to me"). A schema-bound iTVF can neither hold
   arbitrary expressions nor be redeployed on every role edit without DDL under user action; a generic
   "join the Permissions table" predicate is the join-heavy shape Microsoft and Red Gate warn against, and still
   cannot evaluate Queryex text. [inference from verified facts]
3. **Schema evolution friction.** Schema binding blocks `ALTER COLUMN` on referenced columns and blocks altering
   the function; expand/contract migrations and N−1 compatibility would have to drop/recreate policies in the
   migrator, and UDTT-style versioning would be needed for predicates. [verified mechanics; inference on impact]
4. **Temporal history is unprotected by default**; every system-versioned entity would need a second policy on
   its history table, and `FOR SYSTEM_TIME` reads route through both. [verified]
5. **Indexed views are impossible** on RLS tables (may matter for tier-2 reporting later); columnstore batch mode
   may be lost. [verified]
6. **dbo/db_owner are filtered too**: the migrator (runtime seeding through the bulk pipeline), support tooling,
   background jobs and the seeded system user all need an explicit bypass coded in the predicate — a fail-open
   hazard if keyed on "no session context set". [verified mechanics; inference on hazard]
7. **The "can I, and why" query needs the permission model in C# regardless**; RLS would be a second source of
   truth for the same rule. [inference]
8. **Testability**: `FilterTree` composition is unit-testable without a database; predicates need per-table
   integration tests. [inference]
9. **Tenant isolation, the headline RLS use case, is already physical** (one DB per tenant). [verified per §1.4]
10. Open engine bug with `SESSION_CONTEXT` in parallel plans on reset sessions (§1.2). [verified]

### 1.7 What application-composed filters look like against verified Queryex facts

- Queryex already compiles a `FilterTree` with `And/Or/Not/Leaf`, parameterizes literals and binds `me()`,
  `today()`, `now()` as parameter slots (`@qx{b}_p{n}`), so `And(userFilter, Or(permissionFilter_1..n))` yields
  one parameterized statement per (entity, permission-set shape) — plan-cache friendly and per-user values never
  enter the SQL text. [verified in briefing §2 and spec 0008]
- Failure modes to design around (the pro-RLS list, answered in C#): raw SQL/tier-2 paths must obtain their
  filter from the same evaluator (make the evaluator the only way to get a "readable set" for an entity); writes
  need an RLS pre-check (does the row the user is editing satisfy the filter before the change) and post-check
  (after), which the brain dump already lists in the save pipeline; empty `Or` compiles to false (fail closed)
  when a user has no permission. [inference on verified engine facts]

**Implication for the design:** keep row-level security as `FilterTree` composition inside the C# permission
evaluator and Queryex; do not use SQL Server security policies. Record the reasons (§1.6) in the spec so the
decision survives the next "why not native RLS?" review, and reserve `SESSION_CONTEXT` for observability
(correlation id) rather than authorization.

---

## 2. ASP.NET Core 10 authorization

### 2.1 How the authorization middleware decides [verified from source, main branch]

- `AuthorizationMiddleware.Invoke`: reads `endpoint.Metadata`; if a cache is enabled it looks the combined policy
  up per endpoint (`AuthorizationMiddlewareCache`), else calls `AuthorizationPolicy.CombineAsync(policyProvider,
  metadata)`. If the combined policy is `null` → `_next` (no authorization). Then `policyEvaluator.AuthenticateAsync`
  → **then** the `IAllowAnonymous` check short-circuits to `_next` (authentication still runs, so `User` is
  populated on anonymous endpoints) → `policyEvaluator.AuthorizeAsync` → `IAuthorizationMiddlewareResultHandler.HandleAsync`
  (challenge/forbid/next).
- `AuthorizationPolicy.CombineAsync`: every `IAuthorizeData` contributes policy names (looked up through
  `IAuthorizationPolicyProvider.GetPolicyAsync`), comma-split roles and authentication schemes; an `[Authorize]`
  with no policy and no roles pulls in **`GetDefaultPolicyAsync`**; `AuthorizationPolicy` metadata instances and
  `IAuthorizationRequirementData` requirements are added; **only when no `IAuthorizeData`, no policies and no
  requirement data exist** does it fall back to **`GetFallbackPolicyAsync`**, which returns `null` when none is
  configured. All contributions are combined with AND (requirements and schemes are unioned into one policy).
- Sources: https://raw.githubusercontent.com/dotnet/aspnetcore/main/src/Security/Authorization/Policy/src/AuthorizationMiddleware.cs;
  https://raw.githubusercontent.com/dotnet/aspnetcore/main/src/Security/Authorization/Core/src/AuthorizationPolicy.cs.

### 2.2 FallbackPolicy vs DefaultPolicy [verified]

- Reference code: `builder.Services.AddAuthorization(options => { options.FallbackPolicy = new
  AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build(); });` (or `AddAuthorizationBuilder()
  .SetFallbackPolicy(...)`). Docs: the fallback policy "requires all users to be authenticated" except endpoints
  with their own authorization metadata, endpoints served by middleware that runs before authorization (static
  files), and `[AllowAnonymous]`. `DefaultPolicy` is what `[Authorize]`/`RequireAuthorization()` without a name
  means; `FallbackPolicy` is what applies when there is **no** authorization metadata at all. Microsoft's own
  wording: setting the fallback "is more secure than relying on new controllers and Razor Pages to include the
  `[Authorize]` attribute".
- `AllowAnonymous()` "will bypass all authorization checks for the endpoint including the default authorization
  policy and fallback authorization policy" (API doc). Once present anywhere in an endpoint's metadata it wins.
- Sources: https://learn.microsoft.com/en-us/aspnet/core/security/authorization/secure-data?view=aspnetcore-10.0
  (updated 2026-07-22); https://learn.microsoft.com/en-us/aspnet/core/security/authorization/policies?view=aspnetcore-10.0
  (ms.date 2026-07-21); https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.builder.authorizationendpointconventionbuilderextensions?view=aspnetcore-10.0.

### 2.3 Endpoint metadata APIs for Minimal APIs [verified]

- `RequireAuthorization()` (default policy), `RequireAuthorization(params string[] policyNames)`,
  `RequireAuthorization(params IAuthorizeData[])`, `RequireAuthorization(AuthorizationPolicy)`,
  `RequireAuthorization(Action<AuthorizationPolicyBuilder>)` (inline policy, no registration needed), and
  `AllowAnonymous()` — all extension methods on `IEndpointConventionBuilder`, so they apply to a single
  `MapPost` and to a `MapGroup(...)` builder, which stamps the metadata on every endpoint in the group
  (`RouteGroupBuilder : IEndpointConventionBuilder`). [the group behaviour is a high-confidence inference from
  the API shape; the API doc lists the overloads]
- Policies are AND-ed when stacked; requirements inside a policy are AND-ed; **OR** is expressed by registering
  several handlers for one requirement (any `Succeed` satisfies) — `context.Fail()` forces failure regardless;
  `InvokeHandlersAfterFailure` defaults to true; handlers run "even if authentication fails" and in no defined
  order. `RequireAssertion(Func<AuthorizationHandlerContext,bool>)` for inline checks.
- **`IAuthorizationRequirementData`** (ASP.NET Core 8+): an attribute that is both `AuthorizeAttribute` and
  `IAuthorizationRequirement` and yields its requirements from metadata — `RequireAuthorization(new
  MinimumAgeAuthorizeAttribute(21))` needs no named policy. In .NET 8–10 it is honoured **only on routed/Minimal
  API endpoints** (not MVC controllers, SignalR hubs, Blazor); .NET 11 extends it.
  https://learn.microsoft.com/en-us/aspnet/core/security/authorization/iard?view=aspnetcore-10.0 (ms.date 2026-07-29)
- Custom `IAuthorizationPolicyProvider` generates policies dynamically (e.g. `"Securable:core.User:Save"` parsed
  on demand) instead of registering thousands at startup.
  https://learn.microsoft.com/en-us/aspnet/core/security/authorization/custom-authorization-policy-providers

### 2.4 Resource-based (imperative) authorization [verified]

- `IAuthorizationService.AuthorizeAsync(user, resource, policyName | requirements)`; handlers derive from
  `AuthorizationHandler<TRequirement, TResource>`; `OperationAuthorizationRequirement { Name }` with a static
  `Operations` class (Create/Read/Update/Delete) lets one handler serve all operations. Registered as
  `IAuthorizationHandler` (any lifetime; singleton in docs). Attribute evaluation "occurs before data binding
  and before execution of any method that loads a resource", so record-level checks are always imperative.
- Source: https://learn.microsoft.com/en-us/aspnet/core/security/authorization/resourcebased?view=aspnetcore-10.0
  (ms.date 2026-07-21).

### 2.5 .NET 10 changes that touch this theme [verified]

- **Cookie auth returns 401/403 for "known API endpoints"** instead of redirecting to login/access-denied. Known =
  carries `IApiEndpointMetadata`, added automatically to `[ApiController]` endpoints, Minimal API endpoints that
  read JSON bodies or write JSON, endpoints returning `TypedResults`, and SignalR endpoints. This is exactly the
  BFF-cookie behaviour the distribution needs; a Minimal API endpoint that neither reads nor writes JSON (e.g. a
  blob GET) may still redirect unless the metadata is added explicitly. [inference on the last clause]
- New **authentication/authorization metrics** (challenge count, forbid count, "count of requests requiring
  authorization") and ASP.NET Core Identity metrics.
- **Minimal API validation** (`AddValidation()`, DataAnnotations on body/query/header, 400 via
  `IProblemDetailsService`, `DisableValidation()` per endpoint) and record-type validation.
- Source: https://learn.microsoft.com/en-us/aspnet/core/release-notes/aspnetcore-10.0?view=aspnetcore-10.0.

### 2.6 Making it hard to leave an endpoint unsecured (techniques, with status)

1. **Fallback policy = `RequireAuthenticatedUser()`** at the host — every endpoint without metadata is at least
   authenticated. [verified] Limitation: it says nothing about *which* securable; an endpoint can still be
   "authenticated but unauthorized-by-omission".
2. **Group-level `RequireAuthorization`** on the `/api/{tenantId}` group so projected endpoints inherit the
   tenant-membership policy. [verified mechanism]
3. **Metadata-carried securable**: a Tellma `IAuthorizationRequirementData` attribute (`[Securable("core.User",
   "Read")]`) or an `AuthorizationPolicy` produced by the projection code makes the requirement part of the
   endpoint's metadata; a dynamic `IAuthorizationPolicyProvider` resolves it. [verified mechanism, .NET 10 honours
   it for Minimal API endpoints]
4. **Startup audit over `EndpointDataSource`**: iterate `RouteEndpoint`s and fail startup (or a test) for any
   endpoint under `/api` lacking the Tellma securable metadata and lacking an explicit "public" marker. This is a
   documented community practice (json.codes, joaograssi.com), not a Microsoft feature; it fits the briefing's
   "one aggregated startup validation" in `AddTellma`. **[community + inference]**
5. **`AllowAnonymous` is absolute** — treat its use inside the tenant API group as a build/startup violation
   (analyzer or the same audit). [verified semantics]
6. Resource-level checks cannot be declarative; the service pipeline must be the only path to data (record-level
   `AuthorizeAsync`-style checks live in the service, not the endpoint). [verified §2.4]

**Implication for the design:** declare securables as endpoint metadata that a custom policy provider can
resolve, set a fallback policy, and add a startup audit that refuses any tenant-API endpoint without securable
or explicit-public metadata; keep row filters and record checks imperative inside the service pipeline.

---

## 3. Permission models in ERP/SaaS products

### 3.1 Odoo (18.0/19.0 developer docs; 18.0 source) [verified]

- Two layers: **access rights** (`ir.model.access`: model × group × `perm_read/write/create/unlink`) and **record
  rules** (`ir.rule`: model, `groups`, computed `global` = no groups, `domain_force`, per-operation booleans).
- Access rights are **additive** ("the union of the accesses they get through all their groups"); an access right
  with **no group applies to all users** — Odoo's "public permission" is simply a group-less right.
- Record rules are **default-allow** when none applies; **global rules intersect** (all must hold, each one only
  restricts) and **group rules union** (any may hold, expanding access but never beyond the global set); the two
  sets intersect. Documented warning: several global rules "is risky as it's possible to create non-overlapping
  rulesets, which will remove all access".
- Domain variables available in `domain_force`: `user`, `company_id`, `company_ids`, `time`.
- Self-lockout guards in `res_users.py` (18.0): "You cannot deactivate the user you're currently logged in as.";
  "You cannot delete the admin user because it is utilized in various places … Instead, archive it."; the
  superuser cannot be deleted or activated. The user docs still warn that removing your own rights can produce
  an "impotent admin" and recommend contacting support — i.e., Odoo guards self-deactivation and the seeded admin
  row, **not** stripping your own Settings/Access-Rights group.
- Sources: https://www.odoo.com/documentation/18.0/developer/reference/backend/security.html;
  https://raw.githubusercontent.com/odoo/odoo/18.0/odoo/addons/base/models/res_users.py;
  https://www.odoo.com/documentation/18.0/applications/general/users/access_rights.html.

### 3.2 Salesforce [verified via Trailhead; help.salesforce.com and architect.salesforce.com refused fetch]

- Layers: org-wide defaults (baseline per object: Private / Public Read Only / Public Read/Write), role
  hierarchy, **sharing rules** (owner-based: records owned by users in role/group X shared with Y; criteria-based:
  records whose field values match), manual sharing, teams. Sharing rules "can never be stricter than your
  org-wide default settings" — **grant only, never restrict**; each rule = records to share + target users
  (roles, roles and subordinates, territories, public groups) + access level (Read Only / Read/Write).
- Salesforce advises rules "defined for a particular group of users that you can determine or predict in
  advance" — sharing is materialized into share tables and recalculated, so churn is expensive. Per-object limits
  on criteria-based rules and supported field types: **not verified** (pages blocked).
- Source: https://trailhead.salesforce.com/content/learn/modules/data_security/data_security_sharing_rules.

### 3.3 Microsoft Dynamics 365 / Dataverse [verified]

- Security roles carry table privileges (Create, Read, Write, Delete, Append, Append To, Assign, Share) each at an
  **access level**: None / User (own, shared-with-me, team-shared) / Business Unit / Parent: Child Business Units /
  Organization. Roles are **cumulative** ("Users are granted the privileges that are available in each role
  assigned to them"); higher levels imply lower. Organization-owned tables have only None/Organization.
- The filter vocabulary is fixed (ownership and business-unit hierarchy), not an expression language; per-record
  exceptions come from sharing and access teams. Predefined roles ("Basic User", "App Opener") are the
  "everyone" baseline that admins assign to all users.
- No documented last-admin guard: removing System Administrator from yourself locks the environment and needs
  another Power Platform/Global admin to recover (Microsoft Q&A, 2026). **[community]**
- "Check Access" on a record explains why a user has access (role, share, team) — a product analog of the "can I,
  and why" query. **[community]**
- Sources: https://learn.microsoft.com/en-us/power-platform/admin/security-roles-privileges (ms.date 2025-12-09);
  https://learn.microsoft.com/en-us/answers/questions/5872712/dataverse-admin-lockout-environment-not-visible-af.

### 3.4 Treatment of "public" permissions across products [verified per 3.1–3.3]

- Odoo: group-less access right (a flag-like shape, not a role). Salesforce: org-wide default per object (a
  setting, not a role). Dataverse: a role everybody is assigned. All three keep public grants inside the same
  validation/UI as other grants; none uses a separate table of role-less permissions.

### 3.5 Admin self-lockout protections [verified unless marked]

- **Microsoft Entra ID**: "Microsoft Entra ID prevents the last Global Administrator account from being deleted,
  but it doesn't prevent the account from being deleted or disabled on-premises"; Microsoft additionally
  recommends two or more break-glass accounts excluded from Conditional Access, monitored, validated every 90
  days. https://learn.microsoft.com/en-us/entra/identity/role-based-access-control/security-emergency-access (ms.date 2026-06-04)
- **GitHub organizations**: "you can't change your own role"; the sole owner must first make another member an
  owner before removing themselves; at least two owners recommended.
  https://docs.github.com/en/organizations/managing-organization-settings/transferring-organization-ownership;
  https://docs.github.com/en/organizations/managing-peoples-access-to-your-organization-with-roles/maintaining-ownership-continuity-for-your-organization
- **Odoo**: cannot deactivate yourself; cannot delete the seeded admin/superuser (§3.1).
- **Dataverse**: no guard; documented lockouts. [community]
- Common denominators: (a) the acting user may not deactivate/delete themselves; (b) the last holder of the
  admin capability cannot be removed by anyone; (c) an out-of-band recovery path exists (break-glass account or
  seeded admin). None of the three products prevents an admin from narrowing their *own* permissions below admin
  while another admin remains.

**Implication for the design:** model permissions as grant-only, disjunctive, resource × action × optional
filter (the Odoo group-rule / Salesforce sharing-rule shape, not Odoo's intersecting global rules); make the
public grant a role-shaped thing reusing role validation; implement lockout guards as (1) no self-deactivation
or self-deletion, (2) "at least one active user with the admin securable must remain" validated inside the same
save transaction, and (3) a seeded, non-signin system user is *not* a recovery path — keep a documented
break-glass procedure instead.

---

## 4. Opaque version tags: `uniqueidentifier` vs `rowversion` vs monotonic `bigint`

### 4.1 `rowversion` [verified]

- 8 bytes; database-wide counter incremented on **every insert/update of any row in any table that has a
  rowversion column**; one column per table; changes "with any update statement, even if no row values are
  changed"; not a clock; `@@DBTS` returns the current counter; nullable rowversion ≡ `varbinary(8)`,
  non-nullable ≡ `binary(8)`; `timestamp` synonym deprecated; poor key/index-key candidate because every update
  moves the row in the index. `SELECT INTO` can duplicate values.
- `MIN_ACTIVE_ROWVERSION()` exists because values are assigned before commit: an in-flight transaction holds a
  rowversion lower than `@@DBTS`, so "if an application uses @@DBTS rather than MIN_ACTIVE_ROWVERSION, it is
  possible to miss changes" — the general lesson for any monotonic counter read as "what changed since".
- Sources: https://learn.microsoft.com/en-us/sql/t-sql/data-types/rowversion-transact-sql (updated 2026-08-24);
  https://learn.microsoft.com/en-us/sql/t-sql/functions/min-active-rowversion-transact-sql.

### 4.2 `uniqueidentifier` [verified + community]

- 16 bytes; only comparison and NULL checks; "ordering is not implemented by comparing the bit patterns";
  cannot be IDENTITY; `NEWID()` random, `NEWSEQUENTIALID()` sequential per machine (column default only).
- Ordering (community, consistent since 2007 — SQLBI/Alberto Ferrari; Born SQL; Microsoft Tech Community
  "Lesson Learned #497"): the last 6 bytes are most significant, then bytes 8–9, then 6–7, 4–5, 0–3 (the first
  three groups compared left-to-right within the group, the last two right-to-left). Consequence: **.NET
  `Guid.CreateVersion7()` (RFC 9562, .NET 9+) is time-ordered in .NET byte order but not in SQL Server's sort
  order**, so a v7 GUID does not give a sequential clustered/nonclustered key in SQL Server without byte
  shuffling. Irrelevant for an unindexed tag column, relevant if a tag is ever indexed or range-compared.
- Sources: https://learn.microsoft.com/en-us/sql/t-sql/data-types/uniqueidentifier-transact-sql (updated 2026-08-24);
  https://learn.microsoft.com/en-us/dotnet/api/system.guid.createversion7;
  https://www.sqlbi.com/blog/alberto/2007/08/31/how-are-guids-sorted-by-sql-server/.

### 4.3 Monotonic `bigint` [verified]

- From a sequence: `NEXT VALUE FOR` is allowed in `SELECT` lists, `INSERT … VALUES`, `UPDATE … SET`, variable
  assignment and default constraints, but **not** in `MERGE` (except via a default constraint), subqueries/CTEs,
  `WHERE`, `OUTPUT`, `TOP`/`OFFSET`, `DISTINCT`/`UNION`, `CASE`/`COALESCE`, or UDT defaults. Values are
  assigned per row in `UPDATE` and may have gaps under concurrency; `sp_sequence_get_range` reserves ranges
  (already the id-allocator mechanism). https://learn.microsoft.com/en-us/sql/t-sql/functions/next-value-for-transact-sql
- From the row itself: `UPDATE T SET Version = Version + 1 WHERE …` is atomic per row under the update lock;
  concurrent bumpers serialize on the row. [inference from standard locking; no doc needed]
- Built-in alternative — **Change Tracking**: per-database `CHANGE_TRACKING_CURRENT_VERSION()` (bigint) and
  per-table/per-row versions, enabled per database then per table, no schema change, in-memory rowstore flushed
  at checkpoint, retention-based cleanup (SQL Server 2025 adds adaptive shallow cleanup by default), tracks all
  DML "even if the value of a column doesn't change", supported "only on the current table" of temporal tables.
  It gives a "did table X change since version V" primitive without app code but is a server feature to enable
  and maintain per tenant DB. https://learn.microsoft.com/en-us/sql/relational-databases/track-changes/about-change-tracking-sql-server (ms.date 2025-05-19)
- Restore/rewind hazard for any monotonic counter: after restoring an older backup the counter is lower than a
  value some instance/browser cached; equality comparison then *detects* the change (values differ) but a later
  bump can re-reach the cached value and read as "unchanged" once. A random 128-bit tag cannot collide this way.
  [inference]

### 4.4 EF Core concurrency tokens [verified]

- `[Timestamp]`/`IsRowVersion()` maps a `byte[]` to `rowversion` (database-generated, whole-row); EF adds
  `AND [Version] = @p` to `UPDATE`/`DELETE` and throws `DbUpdateConcurrencyException` on 0 rows. "Application-
  managed concurrency tokens" (`[ConcurrencyCheck] Guid Version` regenerated in code, optionally via a
  `SaveChanges` interceptor) are documented specifically because they "provide fine-grained control on exactly
  which column changes cause the token to be regenerated" — e.g. cached or unimportant columns. The bulk-save
  emitter can apply the same predicate shape in its own UPDATE.
  https://learn.microsoft.com/en-us/ef/core/saving/concurrency (updated 2025-10-30)

### 4.5 Comparison for Tellma's tags (settings, per-entity-type cache tags, securables, user-level tags)

| Property | `rowversion` | `uniqueidentifier` | monotonic `bigint` |
|---|---|---|---|
| Size / wire | 8 B, binary (base64 in JSON) | 16 B, string 36 chars | 8 B, number (JS-safe only ≤ 2^53) |
| Who generates | engine, on every update of the row | app (`Guid.NewGuid()` / v7) or `NEWID()` | app or sequence/`+1` |
| Bumps only on user-visible change | **no** (any UPDATE, incl. bookkeeping) | yes, app decides | yes, app decides |
| Coordination across instances | none needed | none needed | needs the row/sequence round trip |
| Survives DB restore without false "fresh" | n/a (rewinds; equality still differs) | yes (random) | equality ok, monotonic reasoning breaks |
| Supports "newer than" comparison | yes (DB-wide) | no | yes |
| Indexable cheaply | poor as key | 16 B, non-sequential in SQL order | good |
| Temporal churn | every stamp writes the row → history row | same when stored on a temporal row | same |
| Fits an opaque cache ETag | yes | yes | yes (but leaks ordering) |

- Indexing implications: none of these tags should live in an index key; tags are read by PK (single-row
  settings/user rows) and compared for equality. If tags live in a **narrow non-temporal sibling table** keyed
  by the owner PK, the column type barely matters for storage; what matters is who bumps and when (§4.5 row 3).
- `rowversion` cannot serve as a *selective* tag (bookkeeping updates bump it) and must be excluded from temporal
  entities' hot rows to avoid history churn; the UDTT extension already carries rowversion as nullable
  `binary(8)` if ever needed (spec 0001). [verified in briefing]

**Implication for the design:** use an app-generated `uniqueidentifier` (Guid) for opaque tags that only need
equality (settings, permissions, user settings, cacheable entity types) — no coordination, restore-safe, bumped
only by the statements that declare a write; use a monotonic `bigint` only where ordering is needed (e.g. an
inbox "seen up to" watermark), and never `rowversion` for user-visible concurrency because it moves on
bookkeeping writes.

---

## 5. Tellma identity server: bulk invite and delivery-status APIs as implemented (repo, 2026-09-01) [verified]

Files: `src/apps/Tellma.Identity/Controllers/InvitationsController.cs`, `Controllers/Api/InvitationDtos.cs`,
`Services/Invitations/InvitationService.cs`, `Services/Invitations/InvitationDeliveryStatusService.cs`,
`Data/UserLifecycleState.cs`, `Data/Entities/SingleUseCode.cs`, `Infrastructure/ApiPolicies.cs`,
`TellmaIdentityConstants.cs`; tests `test/apps/Tellma.Identity.IntegrationTests/Api/InvitationApiTests.cs`,
`InvitationDeliveryStatusApiTests.cs`.

### 5.1 Endpoints and authentication

- `POST /api/identity/invitations` and `POST /api/identity/invitations/delivery-status`; MVC `[ApiController]`
  controllers (not Minimal API). Authorization: OpenIddict validation scheme + policy `TellmaIdentityScope`
  (`RequireAuthenticatedUser` + `scope` claim contains `tellma_identity`). Token obtained by the distribution
  with `client_credentials` (`scope=tellma_identity`); tests also send `resource=http://localhost` on the token
  request. Constants: `tellma_api`, `tellma_identity`, `tellma_control_plane`, audience `urn:tellma:control-plane`.
- Caller identity for scoping = `client_id` claim, falling back to `sub`. Delivery-status **returns 403 Forbid**
  when neither is present; invite proceeds with a null client id (invitations then have `CreatedByClientId =
  null` and are unreadable through delivery-status).

### 5.2 Invite request/response (JSON, camelCase)

```json
// request
{ "users": [ { "email": "a@x.com", "displayName": "…", "locale": "ar", "gender": "female", "returnUrl": "https://slug.app.tellma.com/…" } ] }
// response
{ "results": [ { "email": "a@x.com", "sub": "6f9…", "status": "Invited", "error": null } ] }
```

- `users`: `[Required][MinLength(1)][MaxLength(1000)]`; `email` `[Required][EmailAddress]`; `displayName`,
  `locale` (BCP 47; server defaults blank to `"en"`), `gender` (`female`|`male`; unrecognised → unstated, never
  an error), `returnUrl` all optional.
- Per result exactly one of `status` or `error` is non-null; `sub` is non-null only with a status.
- **Statuses**: `Invited` (new user created + link queued), `Reinvited` (existing credential-less or orphaned
  user; orphaned is restored to Active first), `Active` (already holds a passkey/password/external login — **no
  email sent**, membership is the caller's to record and announce).
- **Error strings** (the only shape: free text, no code): "The return url is not a destination this client is
  registered to receive users at."; "The user cannot be invited; an operator must re-enable the account first."
  (Disabled or Purged — deliberately not distinguishing); "The user could not be restored from the orphaned
  state."; "The user could not be created: <Identity error descriptions joined by '; '>"; "The user could not be
  invited." (any exception, or the item during which the caller aborted).
- Ordering and completeness: results are in request order, one per item; on caller cancellation the batch stops
  and results cover a **prefix** (the item in flight is reported as error; later items absent). A user whose row
  was committed before a later step failed keeps its row (credential-less, Active) and is reported as error; a
  re-invite of that email resolves as `Reinvited`. Duplicated emails within one batch are processed twice.
- Link lifetime `InvitationLifetime = 7 days`. Email hand-off is one batched enqueue to a background dispatcher
  (queued, drained on graceful shutdown); a Quartz sweep resends anything never put on the wire (secret rotation,
  last link wins).

### 5.3 Delivery-status request/response

```json
// request
{ "subs": ["6f9…", "…"] }
// response
{ "results": [ { "sub": "6f9…", "state": "Delivered", "expectsDeliveryEvents": true, "sentUtc": "…", "updatedUtc": "…", "reason": null } ] }
```

- `subs`: `[Required][MinLength(1)][MaxLength(1000)]`; results positional, duplicates answered twice.
- **States**: `NotFound` (no invitation *this client* raised — identical for unknown subjects), `Pending`,
  `Sent`, `Delivered`, `Bounced`, `Complained`, `Rejected`, `Abandoned`, `Accepted`.
- Derivation: `ConsumedUtc != null` → `Accepted` (outranks everything); else from `EmailDispatchState`
  {`Pending`→Pending, `Rejected`→Rejected, `Abandoned`→Abandoned, `Sent`|`Sandboxed`→ provider report:
  `Delivered`→Delivered, `SpamReported`→Complained, `Bounced`|`Dropped`|`Failed`→Bounced,
  `Deferred`|`Other`|null→Sent}. **A sandboxed send reports `Sent`** (nothing went out). Only the **latest**
  invitation per user is described. `reason` is populated only for Bounced/Rejected/Abandoned.
  `expectsDeliveryEvents=false` means `Sent` is terminal (SMTP relay / on-prem).

### 5.4 HTTP status contract

- Tested: 200 with per-user results on success; **401** without a token (both endpoints).
- Inferred from code/framework (not tested in repo): **403** with a token lacking the scope (policy assertion
  fails) and 403 on delivery-status when the token has no `client_id`/`sub`; **400 `ValidationProblemDetails`**
  (`application/problem+json`, RFC 7807 shape with `errors` keyed by property path) for empty/over-1000 lists or
  invalid emails, produced by `[ApiController]`'s automatic model-state filter
  (https://learn.microsoft.com/en-us/aspnet/core/web-api/?view=aspnetcore-10.0, "Automatic HTTP 400 responses").

### 5.5 Identity-side lifecycle vs tenant-side state

- Identity `UserLifecycleState`: `Active`(0), `Orphaned`(1), `Disabled`(2), `Purged`(3); only Active users can
  obtain tokens; orphaned users are restored by an invite; Disabled/Purged are refused. The invite API never
  reports the lifecycle state, and the delivery-status API never reports whether the user has since signed in
  anywhere — it reports `Accepted` only when *this* invitation link was opened.
- `Active` from the invite API means "has a credential globally", **not** "has ever signed into this tenant"; the
  tenant can only learn the latter from its own connect step (subject resolved on a request).

### 5.6 Gaps the design must fill

- **No distribution-side client exists** (`grep` for `api/identity/invitations` finds only the server and its
  tests): T4/T8 must define the typed client (request/response records mirroring §5.2–5.3, scope acquisition,
  batching by 1000, timeout policy that avoids the abort-prefix path).
- Errors are free-text English strings — the tenant should store them for troubleshooting but not localize or
  branch on them; the only machine-readable signals are `status`/`state`.
- Delivery status is scoped to the inviting `client_id`: a distribution that rotates its client registration
  loses read access to older invitations (they become `NotFound`).

**Implication for the design:** the tenant `User` state machine should be `New → Invited → Active` on the
tenant's own facts (invite call succeeded with `Invited|Reinvited|Active`; first authenticated request flips
Active), store `Subject`, the invite `status`, timestamp and last error, and treat the delivery-status API as a
live, on-demand drill-down (`Pending|Sent|Delivered|Bounced|Complained|Rejected|Abandoned|Accepted|NotFound`,
with `expectsDeliveryEvents` deciding whether `Sent` is final) rather than persisted state.

---

## 6. Unverified or partially verified items

- SQL Server 2025 GA date/build (search summary of Microsoft's build-versions page; not fetched directly).
- That `sp_reset_connection` clears `SESSION_CONTEXT` (Microsoft states it only indirectly via Data API Builder
  guidance; community sources agree).
- Salesforce per-object limits on criteria-based sharing rules (help pages refused automated fetch).
- Dataverse lacking a last-admin guard (Microsoft Q&A, not product docs).
- `RouteGroupBuilder` inheriting `RequireAuthorization` metadata onto child endpoints (API shape, not re-read).
- Whether `rowversion` columns in system-versioned tables are stored as `binary(8)` in the history table (no
  source found; irrelevant if rowversion is not adopted).
