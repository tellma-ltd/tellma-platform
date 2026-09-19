# Users, roles, and permissions — correctness, security, and long-term maintainability

Theme key `users-roles-permissions`, future spec 0013. Written 2026-09-01 against the brain dump,
the ten-spec breakdown, ARCHITECTURE.md, specs 0001/0003/0007/0008, the compiled Queryex and
Abstractions code, and the research file for this theme. Every design choice below is made so that
access control fails closed, no write path can leave a cache stale without being caught, the
security invariants hold under concurrency, and the schema can evolve by expand/contract with an
N−1 application still running.

---

## 1. Critique

### 1.1 The general design is right; the enforcement points are missing

The brain dump's model — roles as bags of `(Resource, Action, Filter)` grants, memberships on
users, public grants unioned in, filters disjoined, write implying read, inactive roles ignored,
bespoke service criteria, "no permission reads like no record" — is the grant-only, disjunctive
shape that Odoo record rules and Salesforce sharing rules converge on, and it composes cleanly with
`FilterTree.Or` (empty disjunction is `false`, so an empty grant set denies by construction). It is
the correct model. What the draft does not yet say is *where the invariants are enforced*, and
that is where a security design lives or dies:

- **Every guard is described as a C# check before the write.** "A user cannot strip their own admin
  permissions", "public role has no members", "at least one admin remains" are all write-skew
  races under READ COMMITTED SNAPSHOT (on by default on Azure SQL, and the research recommends the
  migrator turn it on everywhere): two admins removing each other concurrently each see the other
  remaining and both commit. A check in C# gives the nice error message; only a check *inside the
  persist transaction*, serialized, gives correctness. The draft has no in-transaction guard anywhere.
- **The cache-freshness story is "read the versions before executing any API call"** but the goal
  is one round trip per read. Reading tags in one round trip and running the query in a second
  reopens the window the tags were meant to close. The batch itself has to refuse to run against a
  stale permission set.
- **"Make it difficult to forget to secure an endpoint"** is asked but nothing structural is
  proposed. ASP.NET Core 10 gives three levers (fallback policy, endpoint metadata honoured by
  `IAuthorizationRequirementData`, and an `EndpointDataSource` walk at startup); none of them proves
  that the *service* evaluated the permission, which is the check that carries the row filter. The
  design needs a runtime witness, not just static metadata.
- **Privilege escalation through the role editor is unaddressed.** Anyone holding `Save` on
  `core.Role` can add `*/*` to a role they belong to. That is an ordinary consequence of the model,
  but it must be stated and bounded, or a distro author will hand `core.Role` save to a "HR admin"
  role without understanding it is full admin.
- **Nothing says who the actor of seeded rows is.** Every top-level row carries `CreatedById`; the
  migrator's runtime seeds and the tenant bootstrap need a user to be that actor before any human
  exists. The draft has no system user; the orchestrator's hint for T10 needs one anyway.

### 1.2 Detailed choices, column by column

**`core.User` (singular).** ARCHITECTURE.md and spec 0001 write `[gl].Invoices`, `[gl].[InvoicesList]`;
the legacy schema is plural. Singular in the brain dump is the odd one out and should change.

**Temporal `User` with `LastActive`, `UserSettingsVersion`, `PermissionsVersion`, `InboxTracking` on
the row.** The draft already knows this is wrong (every `UPDATE` on a temporal table writes a history
row even when nothing changed). The split is necessary, and the draft should go one step further:
the *only* writer of the temporal table is the save pipeline plus one documented one-time transition
(`Invited → Active`); every other write goes to the sibling.

**`SavedAt` + `SavedById` + `ValidFrom`/`ValidTo`.** Three problems. `SavedAt` duplicates `ValidFrom`
exactly (the orchestrator's hint is right). Creation (`CreatedAt`/`CreatedById`) is lost — it can
only be reconstructed by scanning history, which is expensive and which a retention policy on the
history table will eventually delete. And `Center` uses four audit columns while `User` uses two
plus a period, so the wire shape and the concurrency stamp differ per entity for no reason. One
vocabulary — four audit columns on every top-level entity, system-versioning as an additive
capability — fixes all three.

**`State` "?? we need to track the invitation state" and the six-state list.** The tenant can only
*know* three things: nobody has called invite (New), invite returned a `sub` (Invited), the subject
has authenticated to *this tenant* (Active). Everything else (queued, sent, bounced, accepted) is
the identity server's knowledge, scoped to the inviting client, and is by construction a live
drill-down, not tenant state — the research confirms the delivery-status API reports only the
latest invitation and says `NotFound` for one raised by another client. Storing it would create a
second source of truth that goes stale within minutes. The draft's "tenant view immediately, fine
grain on demand" is the right instinct; the state list should be cut to what the tenant owns.

**"Write-once columns like Subject and Email."** `Subject` is not write-once: a re-invite after the
identity user was purged and recreated returns a *different* `sub` for the same email, and the
tenant row must follow it. `Email` is the invitation key at the identity server (creates-or-gets by
email), so editing it after an invite silently desynchronizes the two systems. The correct rule is
"Email is editable only while `State = New`; Subject is server-owned and written only by the Invite
action", and the pipeline enforces it by overwriting server-owned values from the database, not by
splitting the UDTT.

**`RoleMembership` "Weak temporal entity, edited and saved together with the User" with no
uniqueness.** `(UserId, RoleId)` must be unique at the database, and the fan-in index on `RoleId`
is what makes "who has this role" and the public-role invariant cheap.

**`Permission.Resource` "distro specific e.g. invoices. Or 'all'".** Free-form strings drift.
Resource keys must be derived from something already canonical across forks — the entity's
schema-qualified logical name, which ARCHITECTURE.md guarantees survives forks — and the wildcard
must be a reserved token (`*`), not the English word "all" that a real resource could collide with.
A filter on a wildcard permission has no root entity to bind against and must be rejected.

**`Filter` + `FilterLanguageVersion`** are right (spec 0008 §17 requires the stamp). The CHECK that
they are null together is missing.

**`Notes` on `RoleMembership` and `Permission`, `SavedById` on weak entities.** Keep `Notes`
(`nvarchar(1024)`, not the legacy `Memo`). Drop per-child audit columns: every save of a parent
stamps the parent's `ModifiedAt`/`ModifiedById` even when only children changed (required for
aggregate-level optimistic concurrency anyway), and SQL Server stamps every row touched in one
transaction with the same `ValidFrom`, so a child history row's actor is recoverable by joining
the parent's history at equal `ValidFrom`. Direct actor columns on children would cost temporal
churn on every synchronization and buy nothing the join does not.

**Public permissions.** `IsPublic` on `Role` is the right answer; the legacy `dbo.Roles` table
already has it (verified), Odoo's group-less access rights are the same shape, and it reuses the
role editor, the securables validation, `IsActive`, and temporal history. A seeded well-known role
needs the same "no memberships" validation anyway, and a separate role-less table duplicates
validation and UI. What the draft misses: a public role must never hold a wildcard resource and
the seeded Administrator role must never be public, or one checkbox makes everyone an admin.

**`UserSettings` KV bag.** Fine for opaque UI personalization. Wrong for the three preferences the
*server* reads on every background send (language, calendar, time zone): those need typed,
validated columns. And the name collides with the tenant `Settings` table of T3; the user's bag
should be `UserPreferences`.

**`PushSettings` "?? what shape" / "one JSON field?"** A JSON blob for per-type × per-channel
opt-ins is the classic schema-drift trap: it cannot be constrained, cannot be queried by the
dispatcher without `OPENJSON`, and every new notification type is an implicit migration. A narrow
normalized table is queryable, constrainable, and evolves by inserting rows.

**"Cache versions of the user stuff live in the User table."** For permissions, a *user-level* tag
means every role edit must fan out to exactly the right users (members for an ordinary role,
everyone for a public role, plus the diff when `IsPublic` flips) — a fan-out that has to be right in
every code path forever. A single tenant-level `PermissionsTag` bumped by any write to the three
security tables is one declared mapping, cannot miss a case, and costs one recompute per active
user per instance per role edit — a rare admin action against a small set. Correctness wins.

**"Version / ETag / fingerprint / metaversion."** HTTP reserves ETag for representation validators
(the blob endpoint of T7). Use **tag** for the opaque cache validators (`PermissionsTag`,
`PreferencesTag`), **stamp** for timestamps (`LastActiveAt`, the `ModifiedAt` concurrency stamp),
**fingerprint** for the deployment-derived hash of the securables registry, and **format version**
(a `const int` per cached shape) for what the draft calls metaversion.

### 1.3 Gaps

- No **system user** and no seed for the **Administrator role**; no statement of which seeded rows
  are immutable.
- No **lockout invariant under concurrency**, no **app lock**, no in-transaction guards.
- No answer to **"the batch runs against a stale permission set"**.
- No **escalation rule** for role editing; no mention of **step-up** although spec 0003 §9.3 says
  the sensitive-operation set is fixed in distribution source and role/membership edits are the
  archetype.
- No **maximum age** on the permissions cache, so an out-of-band database edit (support tooling)
  that forgets the tag bump leaves an instance stale until restart.
- No **drift diagnostics surface**: where an admin sees that a permission grants nothing.
- No **`Kind`** on `User` even though "service accounts get a separate table later" and the system
  user needs to be distinguishable from humans by a constraint, not a magic email.
- No **deletion rules**: audit FKs make a user that ever acted undeletable; the rule should be
  explicit rather than surfacing as a foreign-key error.

### 1.4 Internally inconsistent

- "The Save operation updates the audit columns" (plural, creation and modification) versus the
  temporal entities that carry only `SavedById`.
- "Reading these early on guarantees that all subsequent logic relies on a fresh server cache"
  versus the 1-1 round-trip goal and versus the question "can we collapse DB calls #1 and #2".
- "Permissions under inactive roles are not included" but `Role.IsActive` toggling is not listed
  as something that must invalidate anyone's cache.
- The securable tuple is `(Resource × Action × SupportsFilter × FilterRootEntity)` while the
  permission stores a free-text `Resource`; nothing links the two at save time except "validated
  against the registry", which is asserted, not designed.

---

## 2. Decisions

Confidence is high unless stated. A **review flag** marks a judgment call with an equally plausible
alternative that Ahmad should see.

### D1 — Vocabulary (seam 17)

- Tables are **plural**, schema-qualified: `core.Users`, `core.UserStamps`, `core.UserPreferences`,
  `core.NotificationPreferences`, `core.Roles`, `core.RoleMemberships`, `core.Permissions`. Queryex
  logical entity names are singular (`User`, `Role`, `RoleMembership`, `Permission`, `UserPreference`).
- Surrogate keys are `int` from `sq_<TableName>`; ids 1–999 are the reserved seed band (sequences
  `StartsAt = 1000`). This is T2's band to fix; this theme needs ids 1 and 2 in it.
- Top-level entities carry **four audit columns** `CreatedAt`, `CreatedById`, `ModifiedAt`,
  `ModifiedById`; weak entities carry none. `ModifiedAt` is the optimistic-concurrency stamp.
- All platform timestamps are `datetime2(7)` in UTC (the period columns are `datetime2` UTC by
  engine rule; a second timestamp type would make `ModifiedAt` and `ValidFrom` incomparable).
- Free text on weak security rows is `Notes` (`nvarchar(1024)`).
- The user's bag is `UserPreferences`; "Settings" is reserved for the tenant.
- **tag** = opaque cache validator, `uniqueidentifier`, app-generated `Guid.NewGuid()` (restore-safe,
  no coordination, no ordering leak); **stamp** = timestamp; **fingerprint** = hash of a
  deployment-time registry; **format version** = `const int` per cached shape.
- Actions are PascalCase identifiers (`Read`, `Save`, `Delete`, `Activate`); resources are
  `<schema>.<EntityName>` (`core.User`, `gl.Center`); `*` is the only wildcard.

Rationale: one vocabulary across T2–T10 removes a whole class of "which stamp is this" bugs.
Alternatives rejected: singular tables (contradicts ARCHITECTURE.md and spec 0001), `datetimeoffset`
(the legacy choice; carries an offset nobody sets to anything but zero and cannot be compared with
period columns), `rowversion` as a tag (bumps on bookkeeping writes; research §4.5).

### D2 — Temporal is an additive capability; the security tables opt in

`core.Users`, `core.Roles`, `core.RoleMemberships`, `core.Permissions` are system-versioned
(history tables `<Table>History` in `core`, period columns `ValidFrom`/`ValidTo` as EF 10 shadow
properties). `core.UserStamps`, `core.UserPreferences`, `core.NotificationPreferences` are not.

- The **only writers** of a temporal security table are the save pipeline (stamping `ModifiedAt`)
  and the one-time `Invited → Active` transition (D7), which does not stamp `ModifiedAt` and is the
  single documented exception.
- `ModifiedAt` is kept next to `ValidFrom` on temporal rows. It is redundant in value but not in
  role: it is app-chosen, uniform across temporal and non-temporal entities, and is what the wire
  carries as the concurrency stamp; `ValidFrom` is engine-generated and would need an `OUTPUT`
  read-back to echo. Eight bytes per row is the price of one vocabulary.
- **Required from T2**: the emitter's `UPDATE` on a temporal table must skip rows whose writable
  columns are unchanged (`WHERE NOT EXISTS (SELECT <cols> FROM inserted-image INTERSECT SELECT <cols>
  FROM target)` shape), otherwise re-saving a user with ten memberships writes ten history rows every
  time. Also required: children are deleted before parents and inserted after, in a fixed schema
  order, so concurrent saves cannot deadlock on table order.

Confidence high. **Review flag**: dropping `ModifiedAt` on temporal entities and using `ValidFrom`
as the stamp saves a column but forks the wire shape and the emitter; the uniform choice is
preferred.

### D3 — `core.Users`

Columns (types in §4): `Id`, `Kind` (`Human | System`; `Service` is reserved for a later spec and
is not in the CHECK today), `Subject` (nullable, unique when present), `Email` (required, unique
across all kinds), `State` (`New | Invited | Active`), `InvitedAt`, `InviteStatus`
(`Invited | Reinvited | Active` — the identity API's last successful status, verbatim), `LastInviteError`
(free text from the identity API, stored for troubleshooting, never localized or branched on),
`ActivatedAt`, `Name`/`Name2`/`Name3`, `ImageId` (column reserved; shape and lifecycle belong to
T7), `PreferredLanguage` (BCP 47), `PreferredCalendar`, `PreferredTimeZone` (IANA), `ContactEmail`,
`ContactMobile` (E.164), `PreferredChannel` (`Email | Sms | Push`), `IsActive`, four audit columns,
period.

- **Server-owned** (never taken from a payload; the pipeline overwrites from the database on
  update and sets defaults on insert): `Kind`, `Subject`, `State`, `InvitedAt`, `InviteStatus`,
  `LastInviteError`, `ActivatedAt`, `IsActive`, audit columns.
- **Editable**: names, `Email` (only while `State = New`; a change afterwards is validation error
  `Users.EmailLockedAfterInvite`), `ImageId` (through T7's staging token), the three preferences,
  contact fields, channel.
- The `System` row can never acquire a subject: CHECK `CK_Users_SystemHasNoSubject`. A `Human`
  row's state and subject agree: CHECK `CK_Users_StateSubject` (`State = 'New'` ⇔ `Subject IS NULL`).
- Uniqueness is the database's job: filtered unique index on `Subject`, unique index on `Email`.
  The C# check exists for the message and for intra-batch duplicates; errors 2601/2627 map to the
  same field error (`Users.EmailTaken`, `Users.SubjectTaken`) — a repeat of the research's write-skew
  finding, applied here.

Alternatives rejected: an `IsService` bit plus `ClientId` on the same table (legacy) — the brain
dump defers service accounts to their own table, and a `Kind` column lets that table arrive without
touching this one's constraints; a magic email for the system user with no `Kind` — a constraint
cannot be written against a magic value.

### D4 — `core.UserStamps`, the non-temporal sibling

One row per user, `UserId` as PK and FK (`ON DELETE CASCADE`), created in the same batch as the
user by the user recipe and re-created idempotently by the cold connect path if absent. Columns:
`LastActiveAt`, `PreferencesTag`, and two columns reserved for T10 — `InboxSeenAt` and
`InboxReadWatermark bigint` (a watermark needs ordering, which is the one place a monotonic value
is right; T10 defines its semantics).

Rationale: everything the platform writes without the user editing anything lives here, so the
temporal table's history is a faithful record of *edits*. Name: "stamps" because the row holds
timestamps and tags and nothing the user authored. Alternatives: `UserActivity` (tags are not
activity), `UserTracking` (implies telemetry), keeping the columns on `Users` (temporal churn).

### D5 — `core.UserPreferences` and typed preference columns

`core.UserPreferences (Id, UserId, Key varchar(128), Value nvarchar(max))`, unique `(UserId, Key)`.
Keys are ASCII dotted segments (`grid.users.columns`, `tour.dismissed`); values are opaque strings
(JSON by convention) the server never interprets. Pinned quick-access screens are a key
(`nav.pinned`); an admin-managed default for less technical users is a tenant setting (T3) the
client merges, not a second table — no second table until a requirement exists.

The three preferences the server needs (`PreferredLanguage`, `PreferredCalendar`,
`PreferredTimeZone`) are typed columns on `Users` (D3), validated against the tenant's configured
languages/calendars (T3) and the IANA list, because background sends address the user in their
language without a request in flight. Both the typed columns and the bag are one cached
"preferences" DTO under `UserStamps.PreferencesTag`.

### D6 — Notification preferences are a normalized table (shape here, semantics in T10)

`core.NotificationPreferences (Id, UserId, NotificationType varchar(64), Channel varchar(8),
IsEnabled bit)`, unique `(UserId, NotificationType, Channel)`; a missing row means "the type's
default" (T10 defines defaults and the non-mutable types). Push subscriptions (endpoint, p256dh,
auth, per device) are T10's table. `Users` keeps only `ContactEmail`, `ContactMobile`,
`PreferredChannel`. The rows are a weak collection under `User` (edited by admins with the user)
and also written by the self-service preferences endpoint (T8).

Confidence medium. **Review flag**: a single `NotificationSettings` JSON column on `Users` is the
lower-ceremony alternative; it is rejected here because the dispatcher (T10) must select recipients
by type and channel in SQL and because JSON shapes drift silently.

### D7 — The user state model

`State` is a stored, queryable column with three values the tenant owns:

| State | Meaning | Written by |
|---|---|---|
| `New` | Never successfully invited; `Subject IS NULL`. | Create (default) |
| `Invited` | The invite API returned a `sub` with status `Invited`, `Reinvited`, or `Active`. `Subject`, `InvitedAt`, `InviteStatus` set; `LastInviteError` cleared. | `UserService.Invite` (T8), through the pipeline |
| `Active` | The subject has authenticated to this tenant at least once. `ActivatedAt` set. | The cold connect path (D18), one-time `UPDATE … WHERE State = 'Invited'` |

`IsActive` is orthogonal (an admin switch; false refuses access at connect regardless of `State`).
A failed invite leaves `State` unchanged and writes `LastInviteError` (through the pipeline, so it
is a normal audited mutation). The identity-side fine grain (`Pending | Sent | Delivered | Bounced |
Complained | Rejected | Abandoned | Accepted | NotFound`, plus `expectsDeliveryEvents` deciding whether
`Sent` is terminal) is a live drill-down endpoint on `UserService` and is never stored.

"Are the states exhaustive?" — yes for what the tenant can know. The identity server's lifecycle
(`active/orphaned/disabled/purged`) is invisible to the tenant except as an invite error, and
"signed in somewhere" is not a fact the API exposes.

### D8 — `core.Roles` with `IsPublic`

Columns: `Id`, `Name` (unique), `Name2`/`Name3` (unique when present), `Code` (unique when present),
`IsPublic`, `IsActive`, four audit columns, period.

- A public role has no memberships (validation `Roles.PublicRoleHasMembers` plus the in-transaction
  guard of D20), grants apply to every active human user, and its `IsActive` is honoured.
- A public role may not hold a permission with `Resource = '*'` (validation
  `Permissions.WildcardResourceOnPublicRole`).
- The seeded Administrator role (`Id = 1`, D24) cannot be made public, deactivated, deleted, or have
  its single `*/*` permission removed or filtered (validation `Roles.AdministratorImmutable`);
  `Name2`, `Name3`, and `Notes` on it remain editable.

Rationale in §1.2. Alternatives: seeded well-known "Everyone" role (needs the same validation, adds a
magic id, cannot be split into several public sets managed by different admins); separate role-less
table (duplicates validation and UI).

### D9 — `core.RoleMemberships`

Columns: `Id`, `UserId`, `RoleId`, `Notes`, period. Unique `(UserId, RoleId)`; index `(RoleId)`.
A weak collection of `User` (synchronized on user save); read-only from the role side. No audit
columns (D2, §1.2). Deleting a role with memberships is a validation error
(`Roles.HasMembers`), with the FK as backstop.

### D10 — `core.Permissions`

Columns: `Id`, `RoleId`, `Resource varchar(128)`, `Action varchar(32)`, `Filter nvarchar(2048)`,
`FilterLanguageVersion int`, `Notes`, period. CHECK `(Filter IS NULL) = (FilterLanguageVersion IS NULL)`.
Index `(RoleId)`.

Validation at role save (T8 `RoleService`, rules supplied by Core — D20):

1. `(Resource, Action)` must exist in the securables registry, or be `*` in either position.
2. A filter is allowed only when the securable's `FilterRoot` is non-null and neither position is
   `*` (`Permissions.FilterNotSupported`).
3. A filter is validated with `QueryexEngine.Validate` in `Filter` mode against the tenant's
   current schema and the securable's `FilterRoot`, under `LanguageVersion = QueryexLanguage.Version`,
   and the version passed is what is stored — the stamp is minted here and nowhere else.
4. Exact duplicates `(Resource, Action, Filter)` within a role are collapsed silently.
5. `Filter` length ≤ 2048 and the engine's default limits (spec 0008 §15) apply.

`Filter` is bounded at 2048 characters rather than the engine's 8192 because a permission criterion
is a short predicate and a bounded column is a bounded attack surface.

### D11 — Resource and action naming

- **Resource key** = `<schema>.<LogicalEntityName>` for entity securables (`core.User`,
  `core.Role`, `gl.Center`); the same grammar for non-entity securables a service registers
  (`core.Settings`, `core.Settings.Notifications`): two or more segments of
  `[A-Za-z][A-Za-z0-9]*` joined by `.`, ≤ 128 characters, compared ordinal-ignore-case, stored as
  registered. The schema-qualified logical name is canonical across forks (ARCHITECTURE.md's
  schema-on-fork rule), so a distro extending `User` keeps `core.User`.
- **Actions** are a small core set plus capability-declared additions:

| Action | Meaning | Declared by |
|---|---|---|
| `Read` | Query, details, export in any format | every readable stack |
| `Save` | Create and update, including import | editable stacks |
| `Delete` | Delete by ids/query, delete with descendants | deletable stacks |
| `Activate` | Change the activation state in either direction | the `IsActive` capability |
| `*` | Every action, including ones added later | admin grants only |

  Capabilities and services add actions (`Invite` on `core.User`, later `Post`, `Assign`) through the
  registry. Export is not a separate action (it is `Read` in another format); import is `Save`.
- **Implicit read**: any grant on `(R, A ≠ Read, F)` also grants `(R, Read, F)` — the same filter,
  never a wider one.
- A securable key rename is a breaking change shipped with a data-migration recipe that rewrites
  `Permissions.Resource`/`Action`; until migrated, old rows drift and grant nothing (D22).

Confidence high. **Review flag**: one `Activate` action versus two (`Activate`, `Deactivate`);
one is preferred because the filter semantics ("may change activation of rows where F") is the
same and two doubles every role's row count for no expressiveness.

### D12 — The securables registry

"Securable" is the term (SQL Server uses it for exactly this: an object permissions apply to).
A securable is `(Resource, Action, FilterRoot?, IsSensitive, Feature)`:

- `FilterRoot` is the **logical entity name** whose Queryex descriptor is the root for filters
  (`null` = filters unsupported). The registry holds names, not `EntityDescriptor` instances,
  because the tenant's schema is rebuilt per configuration (Name2/Name3 gating) and descriptors are
  compared by identity (spec 0008 §16).
- `IsSensitive` marks securables in the step-up set (D25).
- `Feature` names the composition feature that registered it, for diagnostics.
- Registration happens in the realize phase of `AddTellma`: capability recipes (T5) register the
  standard actions for every stack; a feature or service registers custom securables through
  `ISecurableRegistryBuilder`. After realization the registry is frozen (`ISecurableRegistry`,
  singleton, immutable), duplicate keys are a startup error aggregated with the composition
  diagnostics, and `Fingerprint` (SHA-256 of the sorted keys, hex) is computed once.
- The registry is deployment-scoped, not tenant-scoped; the role editor caches its securable list
  by `Fingerprint`.

### D13 — Endpoints are hard to leave unsecured, and the service check is witnessed

Four layers, each catching what the previous cannot:

1. **Fallback policy** on the host: `RequireAuthenticatedUser()`. Any endpoint without metadata is
   at least authenticated.
2. **Tenant group** `/{tenantId}/api/web` (T1/T6): `RequireAuthorization()` plus the tenant filter
   (membership via connect, suspension, sandbox). `AllowAnonymous` anywhere inside the group is a
   startup error.
3. **Endpoint metadata**: every endpoint in the group carries exactly one of
   `SecurableEndpointMetadata(Resource, Action)` (projected automatically for stack endpoints;
   `[Securable("core.User", Actions.Invite)]` — an attribute implementing
   `IAuthorizationRequirementData` — on custom endpoints) or `SelfServiceEndpointMetadata`
   (authenticated active member; `me/…` endpoints). A **startup audit** walks `EndpointDataSource`
   and fails startup, in the same aggregated diagnostic as composition, for any endpoint in the
   group with neither, and for any securable metadata whose key is not in the registry. The
   securable metadata resolves to a coarse check in an endpoint filter: the caller's cached
   effective set must contain at least one candidate grant for `(Resource, Action)`, else 403 —
   cheap, and it denies zero-grant users even if a custom handler forgot everything else.
4. **Authorization witness**: `IPermissionEvaluator` records every evaluation on the request's
   `ICallerContext`. An endpoint filter installed by the group asserts, before a 2xx result is
   written, that the endpoint's declared securable was evaluated during the request; otherwise it
   replaces the result with 500, logs `SecurityEvents.MissingAuthorizationWitness` at Error, and
   increments `tellma.security.witness.missing`. Data never leaves through a handler that did not
   ask. Self-service endpoints witness `SelfService` the same way (the connect step records it).

The record-level check — the filter — lives only in the service (research §2.4: attribute
evaluation happens before any resource is loaded), which is why layer 4 exists.

Confidence high on 1–3, medium on 4. **Review flag**: the witness turns a forgotten check into a
500 in production; the alternative is Error-level logging only (fail open with an alert). Failing
closed is chosen because the missing check is exactly the bug the alert would be reporting.

### D14 — Permission evaluation semantics

`IPermissionEvaluator.EvaluateAsync(SecurableKey)` for the ambient caller:

1. Caller with no `UserId` (unauthenticated background scope) → `Denied` for everything.
2. Effective set = grants of active roles the user is a member of ∪ grants of active public roles,
   computed by the recompute query (D17) and validated against the registry (D22 drift filtering
   removes a grant before it is ever a candidate).
3. Candidates for `(R, A)` = grants where `Resource ∈ {R, '*'}` and (`Action ∈ {A, '*'}` or
   `A = Read`). Bespoke grants from every `IBespokeGrantProvider` registered for `R` are added as
   candidates (they may only add disjuncts, never remove any).
4. Any candidate without a filter → `Unrestricted`. Otherwise candidates non-empty → `Restricted`
   with `FilterTree.Or(leaves)`. Otherwise `Denied`.
5. The decision carries `Grants` (the candidates, each with role id, role name, `IsPublic`,
   permission id, filter text, and `Source ∈ {Role, PublicRole, Bespoke}`), which is the "why".

Outcomes map to HTTP in T6 as: `Denied` → 403 for the operation; rows outside a restricted filter
→ absent from queries, 404 from details, 403 from save/delete pre-check (the brain dump's
"same as non-existent" for reads; for writes the caller already knows the id, so 403 is honest and
prevents a probing loop of save attempts — **review flag**).

Rules from the brain dump made precise:
- **Inactive roles** contribute nothing; **inactive users** never reach evaluation (connect refuses).
- **Filters merge as a disjunction** across roles, public roles, and bespoke providers; there is no
  intersecting global rule (Odoo's documented "impotent admin" hazard is avoided by construction).
- **Related entities in details** are not separately authorized: `Read` on the root securable
  covers the related-entity dictionary and extras (the draft's rule), which is why services must
  keep related-entity loading to the columns the details page shows.
- **Weak entities as query roots** (later): their securable is the parent's, with `FilterRoot` =
  the weak entity and a `RootPath` to the parent (D16).

### D15 — Row-level security as `FilterTree`, composed once per request

`QuerySpec.Filter = And([userFilter?, permissionFilter])` where `permissionFilter` is the
`Restricted` decision's tree. Rules:

- Leaves are the stored permission texts verbatim; identical texts across grants are collapsed to
  one leaf, and leaves are sorted ordinally before `Or` — two users with the same grant set produce
  byte-identical SQL, and the engine's L2/L3 caches (keyed on text and tree shape) are hit rather
  than fragmented.
- `me()`/`today()`/`now()` inside a filter are parameter slots bound by the host, so per-user values
  never enter the SQL text; `HasUser = true` for every evaluation that reaches SQL (a caller
  without a user is `Denied` before compilation).
- The composed tree per `(user, resource, action)` is memoized inside the cached effective set;
  composition never concatenates text.
- Native SQL Server row-level security is **not used**: the filters are user-authored Queryex with
  navigations and hierarchy predicates that no schema-bound predicate function can hold; policies
  would be logic in the database, would need a second policy per history table, would block
  `ALTER COLUMN` under schema binding, and would filter the migrator and support tooling unless a
  bypass is coded into the predicate — a fail-open hazard (research §1.6). `SESSION_CONTEXT` is
  reserved for correlation ids, never for authorization.

### D16 — Weak-entity path rewriting is an engine amendment, not text surgery

When a weak entity becomes a query root for reports, its securable maps to the parent's key with a
`RootPath` (`["Invoice"]` for `InvoiceLine`). Rewriting `PostingDate > @x` into
`Invoice.PostingDate > @x` textually is unsafe (identifiers inside string literals, function names,
declared parameters). The correct mechanism is a leaf that binds its paths relative to a navigation
path: `FilterTree.Leaf(text, rootPath: IReadOnlyList<string>)`, resolved by the binder from
`root.<rootPath>` and emitted through the ordinary join planner. This is an amendment to spec 0008
documented by spec 0011 (T2 owns engine amendments). Until it ships, **no weak entity is a query
root**, and the registry refuses a securable whose `FilterRoot` is a weak entity (startup error).

Confidence high on the mechanism, medium on timing (it can wait; nothing in this release exposes a
weak entity as a root).

### D17 — The permissions cache and its tag

- **Tag**: one tenant-level `PermissionsTag uniqueidentifier` in T3's tenant tag row, bumped by any
  write to `core.Roles`, `core.Permissions`, or `core.RoleMemberships` — declared once on the entity
  classes with `[BumpsTenantTag(TenantTags.Permissions)]` so the emitter bumps automatically (D19).
  There is **no** user-level permissions tag.
- **Entry**: `EffectivePermissionSet` (immutable) per `(tenantId, userId)`, holding the tag it was
  computed under, the validated grants, the drift list, memoized composed trees, and
  `FormatVersion` (`const int` bumped by hand when the shape changes; guards the SPA's copy, which
  outlives deploys).
- **Store**: a private bounded `MemoryCache` per tenant with entry-count size limit (T3's cache
  infrastructure; research §1.8 argues against `HybridCache` for tag-validated entries), single-flight
  recompute per key, and a **maximum age** (`PermissionsCacheMaxAge`, default 15 minutes) after which
  the set is recomputed even when the tag matches — the bound on damage from an out-of-band edit
  that forgot the bump.
- **Recompute batch** (one round trip): first the tag read, *then* the grant query (§4, "recompute").
  Reading the tag first means a commit landing between the two statements produces a set stamped
  with an older tag than the data it holds — the safe direction (one spurious recompute on the next
  request); the other order caches new-tag/old-data as fresh.
- **Validation at recompute**: each grant is checked against the registry and its filter validated
  under its stored language version; failures are recorded as drift (D22) and excluded.

Alternatives rejected: per-user tag with fan-out on role save (must be right in every path; the
public/`IsPublic`-flip cases are easy to miss); `rowversion` (bumps on bookkeeping); SQL Server
change tracking (database-wide, per-table enablement, restore hazard).

### D18 — The connect step and the collapse into the first business round trip (seam 16)

Per request inside the tenant group, the connect step must (a) resolve subject → user, (b) refuse
inactive or unknown users, (c) stamp activity, (d) read the tenant and user tags, and (e) flip
`Invited → Active` once. Design:

**Warm path (one round trip).** The instance holds a bounded `(tenantId, subject) → (userId, kind)`
cache (a subject is never re-pointed to a different user, so entries never invalidate; G1 re-checks
them anyway) and the caller's `EffectivePermissionSet` (D17). The pipeline builds the business
batch from the cached identity and permissions and asks `IConnectStep.Contribute` to prepend:

```sql
-- G1: identity guard. Ends the batch before any business statement runs.
IF NOT EXISTS (SELECT 1 FROM [core].[Users]
               WHERE [Id] = @tc_userId AND [Subject] = @tc_subject AND [IsActive] = 1 AND [Kind] = 'Human')
    THROW 50403, 'TELLMA_USER_INACTIVE', 1;
-- G2: permissions-tag guard. The batch was built from a set computed under @tc_permissionsTag.
IF NOT EXISTS (SELECT 1 FROM [core].[TenantStamps] WHERE [PermissionsTag] = @tc_permissionsTag)
    THROW 50409, 'TELLMA_PERMISSIONS_STALE', 1;
-- T: tag reads for every other cache (settings, cacheable entity types, user preferences).
SELECT [SettingsTag], [PermissionsTag], /* one column per cacheable-entity tag (T3) */ FROM [core].[TenantStamps];
SELECT [PreferencesTag] FROM [core].[UserStamps] WHERE [UserId] = @tc_userId;
-- A1: throttled activity stamp; idempotent; MayRetry = true; only on interactive endpoints.
UPDATE [core].[UserStamps] SET [LastActiveAt] = @tc_now
WHERE [UserId] = @tc_userId AND ([LastActiveAt] IS NULL OR [LastActiveAt] < DATEADD(second, -60, @tc_now));
```

Then the business statements follow (the query and its count; or the validation-context loads; or
the persist statements inside `SET XACT_ABORT ON; BEGIN TRAN … COMMIT`). `THROW` with `XACT_ABORT ON`
rolls the transaction back and terminates the batch, so no business statement executes after a
failed guard. The executor maps error 50403 to `TenantAccessDeniedException` (→ 403) and 50409 to
`PermissionsStaleException`, which the pipeline handles by recomputing the set (D17) and re-running
the whole batch once; a second 50409 re-runs once more; a third fails the request with 503
(`SecurityEvents.PermissionsTagThrash` at Warning) — three role saves inside one request's window is
not a state worth optimizing for.

**Cold path (one extra round trip, once per user per instance).** On a subject-cache miss the step
runs a dedicated batch: resolve the user by subject, insert the `UserStamps` row if absent, flip
`Invited → Active`, read all tags, and compute the effective set (the recompute batch of D17 is
appended to it, so the cold path is still one round trip). Unknown subject → 403 `TenantAccessDenied`.
The `Invited → Active` statement is `UPDATE [core].[Users] SET [State] = 'Active', [ActivatedAt] = @tc_now
WHERE [Id] = @tc_userId AND [State] = 'Invited'` — the one write on a temporal table outside the
pipeline (D2); it does not bump `ModifiedAt` because no client edits `State`.

**Common-case round trips**: read = 1; save = 2 (context loads with guards; persist with guards,
pre-check, writes, post-check, echo). Guards are prepended to *every* batch of a request, not only
the first, so a request's second batch cannot outlive a revocation either.

**Failure modes, stated**:
- *Deactivated user*: G1 fails on the very next batch, from any instance; no cached state is
  consulted. Deactivation also closes the user's SignalR connections and kills the session cookie
  (T1/T10, spec 0003 §7.4), but authorization never depends on that.
- *Stale permissions*: G2 fails; results of that batch are never read; recompute; re-run. A commit
  landing *after* G2 passed and *before* the business statements ran is equivalent to the request
  having arrived before the commit (RCSI statement-level snapshots make G2 the linearization point);
  no lock is taken on the tag row — an S lock would only serialize revocations against in-flight
  requests without changing any observable outcome and would create S→X deadlocks between two
  role saves.
- *RLS pre-check ordering for saves*: the authoritative pre-check (`the rows being updated satisfy
  the caller's Save filter`) and post-check (`the rows as written still satisfy it`) both execute
  inside the persist transaction, after G1/G2, in that order around the writes; the pre-check in
  the context-loading batch is an early-exit optimization T5 may add, never the enforcement.
- *Interactive versus background*: A1 runs only for endpoints not marked `NoActivityStampMetadata`
  (the inbox poll, SignalR negotiate) and never in background scopes.
- *`System` user*: never authenticates (no subject by constraint); background scopes running as
  `SystemUserId` skip G1 and A1 and keep G2.

**Review flag**: guards as `THROW`-ing statements versus returning a first result set the pipeline
inspects. `THROW` is chosen because it structurally prevents the business statements from running;
the result-set variant relies on the pipeline reading the row before using the data.

### D19 — Every write path declares what it writes; tags bump by declaration

- Entities declare the tags their table bumps: `[BumpsTenantTag(TenantTags.Permissions)]` on `Role`,
  `RoleMembership`, `Permission`; `[BumpsUserTag(UserTags.Preferences, userIdProperty: nameof(UserId))]`
  on `UserPreference` and `NotificationPreference`; `[BumpsUserTag(UserTags.Preferences,
  userIdProperty: nameof(Id))]` on `User`. The emitter (T2) appends the bump statements for every
  table its save statements touch — one `UPDATE [core].[TenantStamps] SET [PermissionsTag] = NEWID()`
  per tenant tag, and one row-scoped `UPDATE [core].[UserStamps] SET [PreferencesTag] = NEWID()
  WHERE [UserId] IN (SELECT [Id] FROM @<ids>)` per user tag. The attribute is inherited by distro
  leaves.
- Raw SQL added to a batch must state its write set (`WriteSet.None` for reads; a list of entity
  types otherwise) as a required argument; the builder derives bumps from the declared set. A raw
  statement cannot be added without the argument, so omission is visible at the call site.
- Test tier: the batch executor in the integration suites snapshots
  `sys.dm_db_index_operational_stats` leaf insert/update/delete counts per table around each batch
  and fails the test when a table outside the declared write set changed — the declared write set
  is verified against what the engine actually modified, not against what the author remembered.
- Out-of-band writes (support tooling, hotfix scripts) must end with the tag bump; the cache
  max-age (D17) bounds the damage when they do not.

### D20 — Lockout guards and security invariants inside the transaction

C# validators (Core-owned, applied to the base `User`/`Role` types so a distro leaf cannot drop
them) produce field-level messages; in-transaction guards produce correctness. Both are supplied by
Core and plugged into the pipeline by T8's services.

**Validators (before persist)**:
- `Users.CannotDeactivateSelf`, `Users.CannotDeleteSelf`.
- `Users.CannotRemoveOwnAdministratorMembership` (the acting user cannot delete their own
  membership in role 1; GitHub's "you can't change your own role").
- `Roles.PublicRoleHasMembers`, `Permissions.WildcardResourceOnPublicRole`,
  `Roles.AdministratorImmutable`, `Users.SystemUserImmutable` (row 1 accepts no edits at all),
  `Roles.HasMembers` on delete.
- `Permissions.EscalationBeyondSelf`: a permission `(R, A, F)` may be saved only by a caller whose
  own effective set holds `(R, A)` **unrestricted** (or a wildcard covering it); filters on the
  caller's side do not qualify because filter implication is undecidable. A caller holding `*/*` is
  exempt. This bounds delegation: a role editor can hand out at most what they hold.
- `Users.MembershipEscalation`: adding a membership to a role requires that every permission of that
  role passes the same test against the caller's set.

**Guards (in the persist batch, after the writes)** — every batch that writes any of `core.Users`,
`core.Roles`, `core.RoleMemberships`, `core.Permissions` first serializes on a per-tenant-database
application lock, which makes the guards race-free by construction (no key-range locking, no
deadlock analysis):

```sql
DECLARE @tc_lock int;
EXEC @tc_lock = sp_getapplock @Resource = N'tellma:core.security', @LockMode = N'Exclusive',
                              @LockOwner = N'Transaction', @LockTimeout = 5000;
IF @tc_lock < 0 THROW 50503, 'TELLMA_SECURITY_LOCK_TIMEOUT', 1;
-- … writes …
-- L1: at least one active human administrator remains.
IF NOT EXISTS (SELECT 1 FROM [core].[Users] u JOIN [core].[RoleMemberships] rm ON rm.[UserId] = u.[Id]
               WHERE rm.[RoleId] = 1 AND u.[IsActive] = 1 AND u.[Kind] = 'Human')
    THROW 50423, 'TELLMA_ADMIN_LOCKOUT', 1;
-- L2: public roles have no members.
IF EXISTS (SELECT 1 FROM [core].[RoleMemberships] rm JOIN [core].[Roles] r ON r.[Id] = rm.[RoleId] WHERE r.[IsPublic] = 1)
    THROW 50423, 'TELLMA_PUBLIC_ROLE_HAS_MEMBERS', 1;
-- L3: the Administrator role still holds its unfiltered */* grant, is active, and is not public.
IF NOT EXISTS (SELECT 1 FROM [core].[Roles] r JOIN [core].[Permissions] p ON p.[RoleId] = r.[Id]
               WHERE r.[Id] = 1 AND r.[IsActive] = 1 AND r.[IsPublic] = 0
                 AND p.[Resource] = '*' AND p.[Action] = '*' AND p.[Filter] IS NULL)
    THROW 50423, 'TELLMA_ADMINISTRATOR_ROLE_DAMAGED', 1;
```

`sp_getapplock` returns a code rather than throwing, hence the explicit check; the lock is
transaction-owned and released at commit or rollback. The invariant is defined over the **seeded
Administrator role only** (not "any role with `*/*`"), which keeps L1 a single `EXISTS` and makes
the recovery story deterministic: the seeded role is always the way back in.

Error 50423 maps to a 422 with the message key from the `THROW` text; the batch is rolled back by
`XACT_ABORT`. The `Activate` action on users (deactivate) runs through the same guard set.

The identity server's own break-glass administrator is *not* a tenant recovery path (it holds no
tenant permissions); the documented recovery is the migrator's `--bootstrap-admin <email>` command
(D24), which requires database-level access by design.

Confidence high on L1–L3 and the app lock; medium on the escalation rules. **Review flag**:
`Permissions.EscalationBeyondSelf` forbids an HR admin (holding `Save` on `core.Role` but not on
`gl.Invoice`) from granting invoice access — the alternative is to document `core.Role`/`core.User`
`Save` as "administrative, equivalent to full admin" and drop the rule. The rule is preferred
because it is the difference between "role editing is admin" and "role editing is delegable".

### D21 — Deletion rules

- A `User` is deletable only while `State = New` and `Kind = Human` (validation `Users.OnlyNewUsersDeletable`);
  every user that ever acted is referenced by audit FKs and must be deactivated instead. The delete
  recipe removes `UserStamps`, `UserPreferences`, `NotificationPreferences`, and `RoleMemberships`
  rows in the same batch (children first).
- A `Role` is deletable when it has no memberships and is not the Administrator role; its permissions
  go with it.
- `DeleteByQuery` on `core.User`/`core.Role` is registered but the securable is marked `IsSensitive`.

### D22 — Drift policy

A grant is **drifted** when its `(Resource, Action)` is not in the registry (and not wildcards),
when it carries a filter but the securable has no `FilterRoot` or a position is a wildcard, when
`FilterLanguageVersion < QueryexLanguage.Minimum` or `> Version`, or when `Validate` reports any
diagnostic against the tenant's current schema (a dropped `Name2` language, a renamed column, a
removed navigation). A drifted grant:

- **grants nothing** (fail closed) and never affects the user's other grants;
- is listed in `EffectivePermissionSet.Drift` with `(PermissionId, RoleId, Reason, Diagnostics)`;
- is counted once per recompute on `tellma.permissions.drift.grants{reason}` and logged once per
  `(permission id, reason)` per tag cycle (`SecurityEvents.PermissionDrift`, Warning);
- is surfaced by `RoleService` details as a per-permission diagnostic so the role editor shows the
  broken row with the engine's diagnostic code and location (spec 0008 §14), and by the "can I, and
  why" endpoint under `Drift`;
- is never auto-migrated, never shimmed, never blocks the user.

A securable removed by a new deployment while an N−1 instance still runs is accepted by the old
instance and drifted by the new one; the union during the rollout window is bounded by the old
version's semantics, which is the N−1 guarantee the fleet already relies on.

### D23 — "Can I, and why"

`IPermissionEvaluator.ExplainAsync(userId, key)` returns the same `PermissionDecision` the pipeline
uses, computed for another user from that user's (possibly cold) effective set, plus the drift list.
Exposed by `UserService` (T8) as `POST users/{id}/permissions/explain { resource, action }` (requires
`Read` on `core.User`) and as `POST me/permissions/explain` (self-service). `GET me/permissions`
returns the compact effective set `[{ resource, action, hasFilter }]`, the tag, and `FormatVersion`
for the SPA's show/hide decisions; filter text is never sent to the client for grants of other
users.

### D24 — Tenant bootstrap: the system user, the Administrator role, the first admin

Seeded with `HasData` in the reserved band (deterministic ids code references):

| Row | Id | Values |
|---|---|---|
| `core.Users` | 1 | `Kind = System`, `Email = 'system@tellma.invalid'` (RFC 2606 `.invalid`; validation refuses `.invalid` addresses on human users), `State = Active`, `Name = 'System'`, `IsActive = 1`, `CreatedById = ModifiedById = 1` (self-reference in one insert is valid) |
| `core.UserStamps` | 1 | `PreferencesTag = <fixed Guid>` |
| `core.Roles` | 1 | `Code = 'Administrator'`, `Name = 'Administrator'`, `IsPublic = 0`, `IsActive = 1` |
| `core.Permissions` | 1 | `RoleId = 1`, `Resource = '*'`, `Action = '*'`, `Filter = NULL` |
| `core.RoleMemberships` | 1 | `UserId = 1`, `RoleId = 1` (the system user is an administrator by membership, so the evaluator has no special case) |

`WellKnownIds` in Abstractions carries these constants. Seed rows are immutable through the API
(D8, D20); `Name2`/`Name3`/`Notes` on the role excepted.

**First human admin** is *not* `HasData` (its email is environment-specific): the migrator's
tenant bootstrap (`--bootstrap-admin <email> [--name <name>]`, T1's provisioning seam calling this
theme's `ITenantBootstrapper`) runs the user recipe through the pipeline as `SystemUserId`: creates
the user (`State = New`) with a membership in role 1, in one transaction that passes L1. In deployed
instances the provisioning flow then calls `UserService.Invite` (T8). In **Development in-proc
mode** the bootstrapper accepts `--bootstrap-admin-subject`; the reference distribution's local
setup passes `admin@localhost` with `00000000-0000-0000-0000-000000000001`, which creates the user
directly in `State = Invited` with that subject (the first request flips it to `Active`). The
bootstrapper refuses a fixed subject outside the Development environment: a deployed tenant must
obtain subjects only from the invite API.

### D25 — Sensitive securables and step-up

The registry marks `IsSensitive = true` on: `core.Role` `Save`/`Delete`, `core.User` `Save`/`Delete`/
`Activate`/`Invite`, and any `DeleteByQuery` endpoint. T1/T6's step-up middleware reads the flag
from the endpoint's securable metadata and issues the `401 insufficient_user_authentication`
challenge of spec 0003 §9.3 when the session's `acr`/`auth_time` are below the distribution's
configured bar. The set is Core's default; a distribution may add securables to it and may not remove
Core's (the composition validates). Self-service endpoints are never sensitive.

Confidence medium. **Review flag**: whether role/membership edits should require step-up by default
or only when the distribution opts in; default-on is chosen because these are the operations whose
misuse is unbounded.

### D26 — Telemetry (seam 14)

Meter `Tellma.Core`; names as `const`s in `Tellma.Core.Abstractions.Security.SecurityTelemetryNames`;
no per-tenant tags.

| Instrument | Kind | Tags |
|---|---|---|
| `tellma.permissions.evaluations` | counter | `outcome` ∈ `unrestricted\|restricted\|denied` |
| `tellma.permissions.cache.hits`, `.misses` | counters | `reason` on misses ∈ `absent\|tag\|age\|format` |
| `tellma.permissions.recompute.duration` | histogram (s) | — |
| `tellma.permissions.drift.grants` | counter | `reason` ∈ `unknown_resource\|unknown_action\|filter_unsupported\|filter_invalid\|version_unsupported` |
| `tellma.connect.cold_paths` | counter | — |
| `tellma.connect.stale_retries` | counter | `attempt` ∈ `1\|2\|3` |
| `tellma.security.witness.missing` | counter | — |
| `tellma.security.lockout.rejections` | counter | `guard` ∈ `admin_lockout\|public_role_members\|administrator_damaged\|lock_timeout` |

Log events (`SecurityEvents`): `MissingAuthorizationWitness` (Error), `PermissionDrift` (Warning),
`PermissionsTagThrash` (Warning), `EscalationRejected` (Information), `AdminLockoutRejected`
(Warning). Every event carries `tenantId`, `userId`, `resource`, `action` as structured properties.

### D27 — Testing

- `test/core/Tellma.Core.Tests` (unit): evaluator semantics over an in-memory effective set
  (candidate selection, implicit read, wildcard, bespoke union, drift exclusion, `Or` ordering and
  deduplication, empty set → `Denied`); validators; securable key grammar; registry freezing and
  duplicate detection; `FormatVersion` mismatch → miss.
- `test/core/Tellma.Core.IntegrationTests` (`Category=Integration`, LocalDB/Testcontainers):
  the schema of §4 applied by migrations; G1/G2/A1 behaviour under concurrent role saves (a role
  save committed between two batches of one request forces exactly one retry); L1–L3 under two
  concurrent admin removals (exactly one succeeds); temporal history rows per save (unchanged
  children write none); tag bumps for every declared write path; the write-set audit against
  `sys.dm_db_index_operational_stats`; the startup audit failing on an unmarked endpoint and on
  `AllowAnonymous` inside the group; the witness filter replacing a 2xx with 500.
- The identity-server invite/delivery-status client belongs to T8; this theme's tests stub it.

### D28 — Native RLS, `SESSION_CONTEXT`, and other rejected mechanisms (record)

Rejected with reasons recorded so the next "why not native RLS?" review does not reopen it:
SQL Server security policies (D15), `SESSION_CONTEXT`-keyed predicates (same, plus the open
parallel-plan defect), `rowversion` tags (D1), per-user permission tags (D17), JSON notification
settings (D6), textual filter rewriting (D16), two UDTTs for write-once columns (D3), a seeded
"Everyone" role and a role-less permission table (D8).

---

## 3. Contracts

Placement: `Tellma.Core.Abstractions.Users` (user-side entities and enums),
`Tellma.Core.Abstractions.Security` (roles, permissions, securables, evaluation, telemetry names,
well-known ids). Both namespaces are EF-free and framework-free (BCL DataAnnotations only). Attributes
marked *(T2)* are requested from the entity-contract seam and are shown here in the shape this
theme needs; base classes `AuditedEntity` and `ChildEntity` are T2's, assumed as shown. Code blocks
are normative for shape, not formatting.

### 3.1 Enums and well-known ids

```csharp
namespace Tellma.Core.Abstractions.Users;

/// <summary>What kind of principal a user row represents.</summary>
public enum UserKind
{
    /// <summary>A person who authenticates through the identity authority.</summary>
    Human,

    /// <summary>The seeded actor for platform-initiated work; can never authenticate.</summary>
    System,
}

/// <summary>The tenant's own knowledge of a human user's onboarding.</summary>
public enum UserState
{
    /// <summary>Created; no invitation has succeeded; no subject is bound.</summary>
    New,

    /// <summary>An invitation succeeded and a subject is bound; the user has not yet authenticated here.</summary>
    Invited,

    /// <summary>The subject has authenticated to this tenant at least once.</summary>
    Active,
}

/// <summary>The identity authority's status for the last successful invitation.</summary>
public enum InviteStatus
{
    /// <summary>A new identity user was created and a link was queued.</summary>
    Invited,

    /// <summary>An existing credential-less or orphaned identity user was re-invited.</summary>
    Reinvited,

    /// <summary>The identity user already holds a credential; no email was sent.</summary>
    Active,
}

/// <summary>A notification delivery channel.</summary>
public enum NotificationChannel
{
    /// <summary>Email to the contact address.</summary>
    Email,

    /// <summary>SMS to the contact mobile number.</summary>
    Sms,

    /// <summary>Web push to the user's subscribed devices.</summary>
    Push,
}
```

```csharp
namespace Tellma.Core.Abstractions.Security;

/// <summary>Ids of the seeded rows every tenant database contains.</summary>
public static class WellKnownIds
{
    /// <summary>The system user: the actor of seeds, bootstrap, and built-in schedules.</summary>
    public const int SystemUserId = 1;

    /// <summary>The Administrator role: the one role whose unfiltered wildcard grant is guaranteed.</summary>
    public const int AdministratorRoleId = 1;

    /// <summary>The Administrator role's wildcard permission.</summary>
    public const int AdministratorPermissionId = 1;

    /// <summary>The system user's membership in the Administrator role.</summary>
    public const int SystemAdministratorMembershipId = 1;

    /// <summary>The inclusive upper bound of the reserved seed band; sequences start above it.</summary>
    public const int ReservedIdBandEnd = 999;
}
```

### 3.2 Entities

```csharp
namespace Tellma.Core.Abstractions.Users;

/// <summary>A tenant user. The pack default; distributions extend it by inheritance.</summary>
[TableType]                                                    // spec 0001
[Temporal]                                                     // (T2) system-versioning capability
[BumpsUserTag(UserTags.Preferences, userIdProperty: nameof(Id))]
public class User : AuditedEntity, IActivatable, IMultilingualName
{
    /// <summary>What kind of principal this row is. Server-owned.</summary>
    [ServerOwned]
    public UserKind Kind { get; set; } = UserKind.Human;

    /// <summary>The identity authority's stable subject identifier; null until an invitation succeeds. Server-owned.</summary>
    [ServerOwned, MaxLength(128)]
    public string? Subject { get; set; }

    /// <summary>The invitation address; unique per tenant; editable only while <see cref="State"/> is <see cref="UserState.New"/>.</summary>
    [Required, EmailAddress, MaxLength(255)]
    public string Email { get; set; } = null!;

    /// <summary>The tenant's onboarding state. Server-owned.</summary>
    [ServerOwned]
    public UserState State { get; set; } = UserState.New;

    /// <summary>When the last successful invitation was raised. Server-owned.</summary>
    [ServerOwned]
    public DateTime? InvitedAt { get; set; }

    /// <summary>The identity authority's status for the last successful invitation. Server-owned.</summary>
    [ServerOwned]
    public InviteStatus? InviteStatus { get; set; }

    /// <summary>The identity authority's error text for the last failed invitation, verbatim. Server-owned.</summary>
    [ServerOwned, MaxLength(512)]
    public string? LastInviteError { get; set; }

    /// <summary>When the subject first authenticated to this tenant. Server-owned.</summary>
    [ServerOwned]
    public DateTime? ActivatedAt { get; set; }

    /// <summary>The name in the tenant's primary language.</summary>
    [Required, MaxLength(255)]
    public string Name { get; set; } = null!;

    /// <summary>The name in the tenant's secondary language.</summary>
    [MaxLength(255)]
    public string? Name2 { get; set; }

    /// <summary>The name in the tenant's ternary language.</summary>
    [MaxLength(255)]
    public string? Name3 { get; set; }

    /// <summary>The profile image reference; lifecycle owned by the blob pattern.</summary>
    [MaxLength(64)]
    public string? ImageId { get; set; }

    /// <summary>Preferred UI and message language (BCP 47); must be one the tenant configures.</summary>
    [MaxLength(16)]
    public string? PreferredLanguage { get; set; }

    /// <summary>Preferred calendar; must be one the tenant configures.</summary>
    [MaxLength(16)]
    public string? PreferredCalendar { get; set; }

    /// <summary>Preferred time zone (IANA id).</summary>
    [MaxLength(64)]
    public string? PreferredTimeZone { get; set; }

    /// <summary>The address notifications are sent to; defaults to <see cref="Email"/> when null.</summary>
    [EmailAddress, MaxLength(255)]
    public string? ContactEmail { get; set; }

    /// <summary>The mobile number notifications are sent to, in E.164 form.</summary>
    [MaxLength(32)]
    public string? ContactMobile { get; set; }

    /// <summary>The channel used when a notification type does not pin one.</summary>
    public NotificationChannel PreferredChannel { get; set; } = NotificationChannel.Email;

    /// <summary>Whether the user may access this tenant. Changed only through the Activate action.</summary>
    [ServerOwned]
    public bool IsActive { get; set; } = true;

    /// <summary>The user's role memberships; travels with the user on details and save.</summary>
    [NotMapped]
    public List<RoleMembership> RoleMemberships { get; set; } = [];

    /// <summary>Per-type, per-channel notification opt-ins; travels with the user.</summary>
    [NotMapped]
    public List<NotificationPreference> NotificationPreferences { get; set; } = [];
}

/// <summary>The non-temporal bookkeeping row paired one-to-one with a user.</summary>
public class UserStamp
{
    /// <summary>The user; primary key and foreign key.</summary>
    [Key]
    public int UserId { get; set; }

    /// <summary>The last interactive request, stamped at most once per minute.</summary>
    public DateTime? LastActiveAt { get; set; }

    /// <summary>The cache validator for the user's preferences; regenerated on every preference write.</summary>
    public Guid PreferencesTag { get; set; }

    /// <summary>Reserved for the inbox: when the user last opened the inbox.</summary>
    public DateTime? InboxSeenAt { get; set; }

    /// <summary>Reserved for the inbox: the read watermark.</summary>
    public long? InboxReadWatermark { get; set; }
}

/// <summary>One opaque preference of a user, keyed by a dotted ASCII key.</summary>
[TableType]
[Parent(nameof(UserId))]                                       // (T2) weak-entity declaration
[BumpsUserTag(UserTags.Preferences, userIdProperty: nameof(UserId))]
public class UserPreference : ChildEntity
{
    /// <summary>The owning user.</summary>
    public int UserId { get; set; }

    /// <summary>The key: dotted ASCII segments, unique per user.</summary>
    [Required, MaxLength(128), RegularExpression("^[A-Za-z][A-Za-z0-9]*(\\.[A-Za-z][A-Za-z0-9]*)*$")]
    public string Key { get; set; } = null!;

    /// <summary>The value; opaque to the server.</summary>
    [Required]
    public string Value { get; set; } = null!;
}

/// <summary>A user's opt-in or opt-out for one notification type on one channel.</summary>
[TableType]
[Parent(nameof(UserId))]
[BumpsUserTag(UserTags.Preferences, userIdProperty: nameof(UserId))]
public class NotificationPreference : ChildEntity
{
    /// <summary>The owning user.</summary>
    public int UserId { get; set; }

    /// <summary>The notification type key, as registered by the inbox.</summary>
    [Required, MaxLength(64)]
    public string NotificationType { get; set; } = null!;

    /// <summary>The channel this row applies to.</summary>
    public NotificationChannel Channel { get; set; }

    /// <summary>Whether the type is delivered on the channel.</summary>
    public bool IsEnabled { get; set; }
}
```

```csharp
namespace Tellma.Core.Abstractions.Security;

/// <summary>A role: a named, activatable bag of grants.</summary>
[TableType]
[Temporal]
[BumpsTenantTag(TenantTags.Permissions)]
public class Role : AuditedEntity, IActivatable, IMultilingualName, IHasCode
{
    /// <summary>The unique name in the primary language.</summary>
    [Required, MaxLength(255)]
    public string Name { get; set; } = null!;

    /// <summary>The name in the secondary language; unique when present.</summary>
    [MaxLength(255)]
    public string? Name2 { get; set; }

    /// <summary>The name in the ternary language; unique when present.</summary>
    [MaxLength(255)]
    public string? Name3 { get; set; }

    /// <summary>A stable code for import and cross-tenant reference; unique when present.</summary>
    [MaxLength(50)]
    public string? Code { get; set; }

    /// <summary>True when the role's grants apply to every active user; such a role has no members.</summary>
    public bool IsPublic { get; set; }

    /// <summary>Whether the role's grants count. Changed only through the Activate action.</summary>
    [ServerOwned]
    public bool IsActive { get; set; } = true;

    /// <summary>The role's grants; travel with the role on details and save.</summary>
    [NotMapped]
    public List<Permission> Permissions { get; set; } = [];
}

/// <summary>A user's membership in a role.</summary>
[TableType]
[Temporal]
[Parent(nameof(UserId))]
[BumpsTenantTag(TenantTags.Permissions)]
public class RoleMembership : ChildEntity
{
    /// <summary>The member.</summary>
    public int UserId { get; set; }

    /// <summary>The role.</summary>
    public int RoleId { get; set; }

    /// <summary>Free text.</summary>
    [MaxLength(1024)]
    public string? Notes { get; set; }
}

/// <summary>One grant of a role: an action on a resource, optionally restricted by a filter.</summary>
[TableType]
[Temporal]
[Parent(nameof(RoleId))]
[BumpsTenantTag(TenantTags.Permissions)]
public class Permission : ChildEntity
{
    /// <summary>The granting role.</summary>
    public int RoleId { get; set; }

    /// <summary>The securable resource key, or <c>*</c>.</summary>
    [Required, MaxLength(128)]
    public string Resource { get; set; } = null!;

    /// <summary>The action, or <c>*</c>.</summary>
    [Required, MaxLength(32)]
    public string Action { get; set; } = null!;

    /// <summary>A row-level criterion in the query language, rooted at the securable's filter root; null grants every row.</summary>
    [MaxLength(2048)]
    public string? Filter { get; set; }

    /// <summary>The query-language version <see cref="Filter"/> was validated under; null exactly when the filter is null.</summary>
    public int? FilterLanguageVersion { get; set; }

    /// <summary>Free text.</summary>
    [MaxLength(1024)]
    public string? Notes { get; set; }
}
```

Attributes this theme needs from T2's entity contract (shapes proposed):

```csharp
namespace Tellma.Core.Abstractions.Entities;

/// <summary>Marks a property the client may send but the pipeline never accepts: it is overwritten from the database on update and defaulted on insert.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class ServerOwnedAttribute : Attribute;

/// <summary>Declares that any write to the entity's table regenerates a tenant-level tag.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = true)]
public sealed class BumpsTenantTagAttribute(string tag) : Attribute
{
    /// <summary>The tag name, one of <see cref="TenantTags"/>.</summary>
    public string Tag { get; } = tag;
}

/// <summary>Declares that any write to the entity's table regenerates a user-level tag for the users the written rows belong to.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = true)]
public sealed class BumpsUserTagAttribute(string tag, string userIdProperty) : Attribute
{
    /// <summary>The tag name, one of <see cref="UserTags"/>.</summary>
    public string Tag { get; } = tag;

    /// <summary>The property holding the user id the written row belongs to.</summary>
    public string UserIdProperty { get; } = userIdProperty;
}

/// <summary>Tenant-level tag names this theme declares; the tag row itself belongs to the settings and cache contract.</summary>
public static class TenantTags
{
    /// <summary>Regenerated by every write to roles, memberships, or permissions.</summary>
    public const string Permissions = "Permissions";
}

/// <summary>User-level tag names, stored on the user's stamp row.</summary>
public static class UserTags
{
    /// <summary>Regenerated by every write to the user's row, preferences, or notification preferences.</summary>
    public const string Preferences = "Preferences";
}
```

### 3.3 Securables

```csharp
namespace Tellma.Core.Abstractions.Security;

/// <summary>The core actions every stack may declare. Capabilities and services add their own.</summary>
public static class Actions
{
    /// <summary>Query, details, and export in any format.</summary>
    public const string Read = "Read";

    /// <summary>Create and update, including import.</summary>
    public const string Save = "Save";

    /// <summary>Delete by ids or by query.</summary>
    public const string Delete = "Delete";

    /// <summary>Change the activation state in either direction.</summary>
    public const string Activate = "Activate";

    /// <summary>Raise identity invitations.</summary>
    public const string Invite = "Invite";

    /// <summary>The wildcard: every action, including ones added later.</summary>
    public const string All = "*";
}

/// <summary>Reserved resource tokens.</summary>
public static class Resources
{
    /// <summary>The wildcard: every resource, including ones added later.</summary>
    public const string All = "*";

    /// <summary>Whether a text is a well-formed resource key: two or more identifier segments joined by dots, at most 128 characters.</summary>
    public static bool IsValidKey(string resource);
}

/// <summary>A resource and an action, compared ordinal-ignore-case.</summary>
public readonly record struct SecurableKey(string Resource, string Action)
{
    /// <summary>True when either position is the wildcard.</summary>
    public bool IsWildcard => Resource == Resources.All || Action == Actions.All;
}

/// <summary>A registered securable.</summary>
/// <param name="Key">The resource and action.</param>
/// <param name="FilterRoot">The logical entity filters bind against, or null when filters are unsupported.</param>
/// <param name="IsSensitive">True when the operation belongs to the step-up set.</param>
/// <param name="Feature">The composition feature that registered it, for diagnostics.</param>
public sealed record SecurableDescriptor(
    SecurableKey Key,
    string? FilterRoot,
    bool IsSensitive,
    string Feature);

/// <summary>Collects securables during composition; frozen into <see cref="ISecurableRegistry"/> before traffic.</summary>
public interface ISecurableRegistryBuilder
{
    /// <summary>Registers one securable. A duplicate key is a composition error reported with every other violation.</summary>
    void Add(SecurableDescriptor securable);
}

/// <summary>The frozen, deployment-scoped set of securables.</summary>
public interface ISecurableRegistry
{
    /// <summary>Every securable, ordered by key.</summary>
    IReadOnlyList<SecurableDescriptor> All { get; }

    /// <summary>Looks a securable up by key.</summary>
    bool TryGet(SecurableKey key, out SecurableDescriptor securable);

    /// <summary>A hex SHA-256 over the ordered keys and filter roots; the client caches the securable list by it.</summary>
    string Fingerprint { get; }
}
```

Endpoint-side (lives in `Tellma.Core`, web-facing; shown for the seam with T6):

```csharp
namespace Tellma.Core.Security;

/// <summary>Endpoint metadata naming the securable an endpoint enforces; projected for stack endpoints and attached by <see cref="SecurableAttribute"/> on custom ones.</summary>
public sealed record SecurableEndpointMetadata(SecurableKey Key);

/// <summary>Endpoint metadata for endpoints any active member may call about themselves.</summary>
public sealed record SelfServiceEndpointMetadata;

/// <summary>Endpoint metadata suppressing the activity stamp (polling endpoints).</summary>
public sealed record NoActivityStampMetadata;

/// <summary>Declares the securable a custom endpoint enforces; an authorization requirement the framework honours on routed endpoints.</summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Delegate)]
public sealed class SecurableAttribute(string resource, string action)
    : Attribute, IAuthorizationRequirementData, IAuthorizationRequirement
{
    /// <summary>The securable key.</summary>
    public SecurableKey Key { get; } = new(resource, action);

    /// <inheritdoc />
    public IEnumerable<IAuthorizationRequirement> GetRequirements() { yield return this; }
}
```

### 3.4 Evaluation

```csharp
namespace Tellma.Core.Abstractions.Security;

/// <summary>How a decision came out.</summary>
public enum PermissionOutcome
{
    /// <summary>No grant applies; the operation is refused.</summary>
    Denied,

    /// <summary>At least one unfiltered grant applies; every row is in scope.</summary>
    Unrestricted,

    /// <summary>Only filtered grants apply; <see cref="PermissionDecision.Filter"/> selects the rows in scope.</summary>
    Restricted,
}

/// <summary>Where a grant came from.</summary>
public enum GrantSource
{
    /// <summary>A role the user is a member of.</summary>
    Role,

    /// <summary>A public role.</summary>
    PublicRole,

    /// <summary>A service-supplied criterion.</summary>
    Bespoke,
}

/// <summary>One grant that contributed to a decision: the "why".</summary>
/// <param name="Source">Where it came from.</param>
/// <param name="RoleId">The role, for role-sourced grants.</param>
/// <param name="RoleName">The role's name, for display.</param>
/// <param name="PermissionId">The permission row, for role-sourced grants.</param>
/// <param name="Key">The grant's resource and action as stored (wildcards preserved).</param>
/// <param name="Filter">The grant's criterion text, or null.</param>
public sealed record PermissionGrant(
    GrantSource Source,
    int? RoleId,
    string? RoleName,
    int? PermissionId,
    SecurableKey Key,
    string? Filter);

/// <summary>Why a stored grant is excluded from evaluation.</summary>
public enum DriftReason
{
    /// <summary>The resource is not registered.</summary>
    UnknownResource,

    /// <summary>The action is not registered for the resource.</summary>
    UnknownAction,

    /// <summary>A filter is present but the securable supports none, or a position is a wildcard.</summary>
    FilterUnsupported,

    /// <summary>The filter does not validate against the tenant's current schema.</summary>
    FilterInvalid,

    /// <summary>The filter's language version is outside what this engine compiles.</summary>
    VersionUnsupported,
}

/// <summary>A grant excluded from evaluation, with the reason.</summary>
/// <param name="PermissionId">The permission row.</param>
/// <param name="RoleId">Its role.</param>
/// <param name="Reason">Why it is excluded.</param>
/// <param name="Diagnostics">The engine's diagnostics when the reason is a filter problem.</param>
public sealed record PermissionDrift(
    int PermissionId,
    int RoleId,
    DriftReason Reason,
    IReadOnlyList<QueryexDiagnostic> Diagnostics);

/// <summary>The answer to "may the caller perform this action on this resource, on which rows, and why".</summary>
/// <param name="Key">The securable asked about.</param>
/// <param name="Outcome">The outcome.</param>
/// <param name="Filter">The composed row criterion; non-null exactly when <paramref name="Outcome"/> is <see cref="PermissionOutcome.Restricted"/>.</param>
/// <param name="Grants">The grants that applied; empty when denied.</param>
public sealed record PermissionDecision(
    SecurableKey Key,
    PermissionOutcome Outcome,
    FilterTree? Filter,
    IReadOnlyList<PermissionGrant> Grants)
{
    /// <summary>True unless denied.</summary>
    public bool IsAllowed => Outcome != PermissionOutcome.Denied;
}

/// <summary>The caller's validated grants under one permissions tag.</summary>
public sealed class EffectivePermissionSet
{
    /// <summary>Bumped by hand when the cached shape changes; a mismatch is a cache miss.</summary>
    public const int FormatVersion = 1;

    /// <summary>The tenant permissions tag the set was computed under.</summary>
    public Guid Tag { get; }

    /// <summary>When the set was computed; the maximum-age rule reads it.</summary>
    public DateTime ComputedAt { get; }

    /// <summary>The grants that survived validation.</summary>
    public IReadOnlyList<PermissionGrant> Grants { get; }

    /// <summary>The grants excluded, with reasons.</summary>
    public IReadOnlyList<PermissionDrift> Drift { get; }

    /// <summary>Decides one securable from the role-sourced grants alone; bespoke grants are added by the evaluator.</summary>
    public PermissionDecision Decide(SecurableKey key);
}

/// <summary>Supplies criteria a service grants outside the permission tables (an assignee may see their own documents).</summary>
public interface IBespokeGrantProvider
{
    /// <summary>The resource the provider contributes to.</summary>
    string Resource { get; }

    /// <summary>Additional filtered grants for the ambient caller and the given action; each adds a disjunct and none removes one.</summary>
    ValueTask<IReadOnlyList<PermissionGrant>> GetGrantsAsync(string action, CancellationToken cancellationToken);
}

/// <summary>Resolves the ambient caller's permissions. The only path by which any code obtains a row filter.</summary>
public interface IPermissionEvaluator
{
    /// <summary>Decides one securable for the ambient caller and records a witness on the caller context.</summary>
    ValueTask<PermissionDecision> EvaluateAsync(SecurableKey key, CancellationToken cancellationToken);

    /// <summary>Decides several securables at once, sharing one effective set.</summary>
    ValueTask<IReadOnlyList<PermissionDecision>> EvaluateAsync(IReadOnlyList<SecurableKey> keys, CancellationToken cancellationToken);

    /// <summary>Explains a decision for another user; records no witness.</summary>
    ValueTask<PermissionExplanation> ExplainAsync(int userId, SecurableKey key, CancellationToken cancellationToken);
}

/// <summary>A decision for a named user together with that user's drift list.</summary>
/// <param name="Decision">The decision.</param>
/// <param name="Drift">Grants of the user that are excluded.</param>
/// <param name="Tag">The permissions tag the answer was computed under.</param>
public sealed record PermissionExplanation(PermissionDecision Decision, IReadOnlyList<PermissionDrift> Drift, Guid Tag);
```

### 3.5 Connect step and caller context

```csharp
namespace Tellma.Core.Abstractions.Hosting;

/// <summary>The ambient caller, populated once per request scope by the tenant filter and once per job scope by the job runner.</summary>
public interface ICallerContext
{
    /// <summary>The tenant the work belongs to.</summary>
    int TenantId { get; }

    /// <summary>The authenticated subject; null in background scopes.</summary>
    string? Subject { get; }

    /// <summary>The resolved tenant user; null until connect completes, and for anonymous background work.</summary>
    int? UserId { get; }

    /// <summary>The kind of the resolved user.</summary>
    UserKind? UserKind { get; }

    /// <summary>True for user-initiated requests; false for polling endpoints and background scopes.</summary>
    bool IsInteractive { get; }

    /// <summary>The securables evaluated so far in this scope; the endpoint witness reads it.</summary>
    IReadOnlySet<SecurableKey> Witnessed { get; }
}
```

```csharp
namespace Tellma.Core.Security;

/// <summary>The identity and tag expectations a batch is built against.</summary>
/// <param name="UserId">The resolved user.</param>
/// <param name="Subject">The subject, re-verified by the identity guard.</param>
/// <param name="PermissionsTag">The tag the caller's effective set was computed under.</param>
/// <param name="StampActivity">Whether the activity stamp statement is included.</param>
public sealed record ConnectExpectation(int UserId, string Subject, Guid PermissionsTag, bool StampActivity);

/// <summary>Tags read by the connect statements, handed to the caches.</summary>
public sealed record ConnectTags(IReadOnlyDictionary<string, Guid> TenantTags, Guid PreferencesTag);

/// <summary>Contributes identity, permission, activity, and tag statements to a request's batches.</summary>
public interface IConnectStep
{
    /// <summary>Resolves the caller from cache, or runs the cold round trip; throws when the subject is not an active member.</summary>
    ValueTask<ConnectExpectation> PrepareAsync(CancellationToken cancellationToken);

    /// <summary>Prepends the guard, tag-read, and activity statements to a batch.</summary>
    void Contribute(IBatchBuilder batch, ConnectExpectation expectation);

    /// <summary>Reads the tag result sets out of a completed batch and refreshes the caches.</summary>
    ConnectTags Complete(IBatchResult result);

    /// <summary>Recomputes the caller's effective set after a stale-tag rejection.</summary>
    ValueTask<ConnectExpectation> RefreshPermissionsAsync(CancellationToken cancellationToken);
}

/// <summary>Raised by the executor for guard error 50403: the subject is not an active member.</summary>
public sealed class TenantAccessDeniedException : TellmaException;

/// <summary>Raised by the executor for guard error 50409: the batch was built against a stale permission set.</summary>
public sealed class PermissionsStaleException : TellmaException;

/// <summary>Raised by the executor for guard error 50423: a security invariant would be broken; surfaces as a validation error.</summary>
public sealed class SecurityInvariantException(string messageKey) : TellmaValidationException(messageKey);
```

### 3.6 Bootstrap and validators

```csharp
namespace Tellma.Core.Security;

/// <summary>What the migrator needs to create the first human administrator.</summary>
/// <param name="Email">The administrator's email.</param>
/// <param name="Name">The display name.</param>
/// <param name="Subject">A fixed subject, accepted only in the Development environment.</param>
public sealed record TenantBootstrapRequest(string Email, string Name, string? Subject = null);

/// <summary>Creates the first administrator through the save pipeline as the system user.</summary>
public interface ITenantBootstrapper
{
    /// <summary>Creates or reconciles the first administrator; idempotent on email.</summary>
    Task<int> BootstrapAdministratorAsync(TenantBootstrapRequest request, CancellationToken cancellationToken);
}

/// <summary>The Core-owned validation rules for users, applied to every leaf deriving from <see cref="User"/>.</summary>
public sealed class UserSecurityRules<TUser> : IEntityValidator<TUser> where TUser : User;

/// <summary>The Core-owned validation rules for roles and permissions, applied to every leaf deriving from <see cref="Role"/>.</summary>
public sealed class RoleSecurityRules<TRole> : IEntityValidator<TRole> where TRole : Role;

/// <summary>Contributes the application lock and the in-transaction invariant guards to a persist batch that writes security tables.</summary>
public interface ISecurityGuards
{
    /// <summary>Adds the lock acquisition as the first statement and the invariant guards after the writes.</summary>
    void Contribute(IBatchBuilder batch);
}
```

### 3.7 Telemetry names

```csharp
namespace Tellma.Core.Abstractions.Security;

/// <summary>Meter, instrument, and tag names emitted by the security machinery.</summary>
public static class SecurityTelemetryNames
{
    /// <summary>The meter name.</summary>
    public const string MeterName = "Tellma.Core";

    /// <summary>Counts decisions, tagged by outcome.</summary>
    public const string Evaluations = "tellma.permissions.evaluations";

    /// <summary>Counts effective-set cache hits.</summary>
    public const string CacheHits = "tellma.permissions.cache.hits";

    /// <summary>Counts effective-set cache misses, tagged by reason.</summary>
    public const string CacheMisses = "tellma.permissions.cache.misses";

    /// <summary>Recompute duration in seconds.</summary>
    public const string RecomputeDuration = "tellma.permissions.recompute.duration";

    /// <summary>Counts drifted grants found at recompute, tagged by reason.</summary>
    public const string DriftGrants = "tellma.permissions.drift.grants";

    /// <summary>Counts cold connect paths.</summary>
    public const string ColdPaths = "tellma.connect.cold_paths";

    /// <summary>Counts batches re-run after a stale-tag rejection, tagged by attempt.</summary>
    public const string StaleRetries = "tellma.connect.stale_retries";

    /// <summary>Counts successful results refused because no authorization witness existed.</summary>
    public const string WitnessMissing = "tellma.security.witness.missing";

    /// <summary>Counts persist batches rejected by an invariant guard, tagged by guard.</summary>
    public const string LockoutRejections = "tellma.security.lockout.rejections";

    /// <summary>Tag keys and the closed sets of values.</summary>
    public static class Tags
    {
        public const string Outcome = "outcome";
        public const string Reason = "reason";
        public const string Attempt = "attempt";
        public const string Guard = "guard";
    }
}
```

---

## 4. Schema

All tables in schema `core`; all timestamps `datetime2(7)` UTC; every FK named explicitly; every
temporal table has history table `core.<Table>History` with the default clustered index on the
period. Sequences `sq_Users`, `sq_UserPreferences`, `sq_NotificationPreferences`, `sq_Roles`,
`sq_RoleMemberships`, `sq_Permissions`, each `START WITH 1000`.

```
core.Users  (system-versioned; history core.UsersHistory; UDTT core.UsersList — excludes ValidFrom/ValidTo)
  Id                  int             NOT NULL  PK_Users (clustered)
  Kind                varchar(16)     NOT NULL  CK_Users_Kind IN ('Human','System')
  Subject             nvarchar(128)   NULL      UX_Users_Subject UNIQUE WHERE Subject IS NOT NULL
  Email               nvarchar(255)   NOT NULL  UX_Users_Email UNIQUE
  State               varchar(16)     NOT NULL  CK_Users_State IN ('New','Invited','Active')
  InvitedAt           datetime2(7)    NULL
  InviteStatus        varchar(16)     NULL      CK_Users_InviteStatus IN ('Invited','Reinvited','Active')
  LastInviteError     nvarchar(512)   NULL
  ActivatedAt         datetime2(7)    NULL
  Name                nvarchar(255)   NOT NULL
  Name2               nvarchar(255)   NULL
  Name3               nvarchar(255)   NULL
  ImageId             nvarchar(64)    NULL
  PreferredLanguage   varchar(16)     NULL
  PreferredCalendar   varchar(16)     NULL
  PreferredTimeZone   varchar(64)     NULL
  ContactEmail        nvarchar(255)   NULL
  ContactMobile       varchar(32)     NULL
  PreferredChannel    varchar(8)      NOT NULL  DF 'Email'; CK_Users_PreferredChannel IN ('Email','Sms','Push')
  IsActive            bit             NOT NULL  DF 1
  CreatedAt           datetime2(7)    NOT NULL
  CreatedById         int             NOT NULL  FK_Users_CreatedById -> core.Users(Id)
  ModifiedAt          datetime2(7)    NOT NULL
  ModifiedById        int             NOT NULL  FK_Users_ModifiedById -> core.Users(Id)
  ValidFrom           datetime2(7)    NOT NULL  GENERATED ALWAYS AS ROW START (shadow)
  ValidTo             datetime2(7)    NOT NULL  GENERATED ALWAYS AS ROW END   (shadow)
  CK_Users_SystemHasNoSubject : Kind <> 'System' OR Subject IS NULL
  CK_Users_StateSubject       : Kind <> 'Human' OR ((State = 'New') = (Subject IS NULL))
  CK_Users_ActivatedAt        : State = 'Active' OR ActivatedAt IS NULL
  IX_Users_IsActive_Name (IsActive, Name)      -- the default list view
```

```
core.UserStamps  (not versioned; no UDTT)
  UserId              int             NOT NULL  PK_UserStamps; FK_UserStamps_UserId -> core.Users(Id) ON DELETE CASCADE
  LastActiveAt        datetime2(7)    NULL
  PreferencesTag      uniqueidentifier NOT NULL
  InboxSeenAt         datetime2(7)    NULL      -- reserved for the inbox
  InboxReadWatermark  bigint          NULL      -- reserved for the inbox
```

```
core.UserPreferences  (not versioned; UDTT core.UserPreferencesList)
  Id                  int             NOT NULL  PK_UserPreferences
  UserId              int             NOT NULL  FK_UserPreferences_UserId -> core.Users(Id) ON DELETE CASCADE
  Key                 varchar(128)    NOT NULL
  Value               nvarchar(max)   NOT NULL
  UX_UserPreferences_UserId_Key UNIQUE (UserId, Key)
```

```
core.NotificationPreferences  (not versioned; UDTT core.NotificationPreferencesList)
  Id                  int             NOT NULL  PK_NotificationPreferences
  UserId              int             NOT NULL  FK_NotificationPreferences_UserId -> core.Users(Id) ON DELETE CASCADE
  NotificationType    varchar(64)     NOT NULL
  Channel             varchar(8)      NOT NULL  CK_NotificationPreferences_Channel IN ('Email','Sms','Push')
  IsEnabled           bit             NOT NULL
  UX_NotificationPreferences_User_Type_Channel UNIQUE (UserId, NotificationType, Channel)
```

```
core.Roles  (system-versioned; history core.RolesHistory; UDTT core.RolesList)
  Id                  int             NOT NULL  PK_Roles
  Name                nvarchar(255)   NOT NULL  UX_Roles_Name UNIQUE
  Name2               nvarchar(255)   NULL      UX_Roles_Name2 UNIQUE WHERE Name2 IS NOT NULL
  Name3               nvarchar(255)   NULL      UX_Roles_Name3 UNIQUE WHERE Name3 IS NOT NULL
  Code                nvarchar(50)    NULL      UX_Roles_Code UNIQUE WHERE Code IS NOT NULL
  IsPublic            bit             NOT NULL  DF 0
  IsActive            bit             NOT NULL  DF 1
  CreatedAt           datetime2(7)    NOT NULL
  CreatedById         int             NOT NULL  FK_Roles_CreatedById -> core.Users(Id)
  ModifiedAt          datetime2(7)    NOT NULL
  ModifiedById        int             NOT NULL  FK_Roles_ModifiedById -> core.Users(Id)
  ValidFrom / ValidTo period (shadow)
```

```
core.RoleMemberships  (system-versioned; history core.RoleMembershipsHistory; UDTT core.RoleMembershipsList)
  Id                  int             NOT NULL  PK_RoleMemberships
  UserId              int             NOT NULL  FK_RoleMemberships_UserId -> core.Users(Id)
  RoleId              int             NOT NULL  FK_RoleMemberships_RoleId -> core.Roles(Id)
  Notes               nvarchar(1024)  NULL
  ValidFrom / ValidTo period (shadow)
  UX_RoleMemberships_UserId_RoleId UNIQUE (UserId, RoleId)
  IX_RoleMemberships_RoleId (RoleId)
```

```
core.Permissions  (system-versioned; history core.PermissionsHistory; UDTT core.PermissionsList)
  Id                    int             NOT NULL  PK_Permissions
  RoleId                int             NOT NULL  FK_Permissions_RoleId -> core.Roles(Id)
  Resource              varchar(128)    NOT NULL
  Action                varchar(32)     NOT NULL
  Filter                nvarchar(2048)  NULL
  FilterLanguageVersion int             NULL
  Notes                 nvarchar(1024)  NULL
  ValidFrom / ValidTo period (shadow)
  CK_Permissions_FilterVersion : (Filter IS NULL) = (FilterLanguageVersion IS NULL)
  IX_Permissions_RoleId (RoleId)
```

Column reserved on T3's tenant tag row: `core.TenantStamps.PermissionsTag uniqueidentifier NOT NULL`
(name of the row's table is T3's to fix; this theme needs the column and its bump).

**Recompute batch (D17)** — the tag read precedes the grant query:

```sql
SELECT [PermissionsTag] FROM [core].[TenantStamps];
SELECT p.[Id], p.[RoleId], r.[Name], r.[IsPublic], p.[Resource], p.[Action], p.[Filter], p.[FilterLanguageVersion]
FROM [core].[Permissions] p
JOIN [core].[Roles] r ON r.[Id] = p.[RoleId]
WHERE r.[IsActive] = 1
  AND (r.[IsPublic] = 1
       OR EXISTS (SELECT 1 FROM [core].[RoleMemberships] rm WHERE rm.[RoleId] = r.[Id] AND rm.[UserId] = @tc_userId));
```

**Cold connect batch (D18)**:

```sql
SELECT [Id], [Kind], [IsActive], [State] FROM [core].[Users] WHERE [Subject] = @tc_subject;
IF NOT EXISTS (SELECT 1 FROM [core].[UserStamps] s JOIN [core].[Users] u ON u.[Id] = s.[UserId] WHERE u.[Subject] = @tc_subject)
    INSERT INTO [core].[UserStamps] ([UserId], [PreferencesTag])
    SELECT [Id], NEWID() FROM [core].[Users] WHERE [Subject] = @tc_subject;
UPDATE [core].[Users] SET [State] = 'Active', [ActivatedAt] = @tc_now
WHERE [Subject] = @tc_subject AND [State] = 'Invited' AND [IsActive] = 1;
-- followed by the tag reads, the activity stamp, and the recompute batch above
```

**Seed rows (`HasData`)** are the five rows of D24; the migrator's runtime seeds and the bootstrap
run through the pipeline as `SystemUserId`.

**Engine-facing names**: the Queryex schema adapter (T2) exposes `User` (`[core].[Users]`), `Role`,
`RoleMembership`, `Permission`, `UserPreference`, `NotificationPreference`, with navigations
`CreatedBy`/`ModifiedBy` → `User`, `RoleMembership.User`/`.Role`, `Permission.Role`;
`UserStamp` is exposed read-only as `UserStamp` with navigation `User` so reports can show
`LastActiveAt`; `Users.Subject` and `LastInviteError` are declared but the projection for non-admin
readers is T5's column-gating concern. `Kind`, `State`, `InviteStatus`, `PreferredChannel`,
`Channel` are string-typed enum properties.

---

## 5. Answers

| Brain-dump question (abridged) | Answer |
|---|---|
| "Is `State` needed / how to track invitation state?" | Stored `New\|Invited\|Active`, tenant-owned facts only; identity-side fine grain is a live drill-down (D7). |
| "Better approach for the record + blobs pattern?" | T7's; the user carries `ImageId` only (D3). |
| "Is JSON the right shape for user preferences? Pinned screens in a separate table?" | KV bag `core.UserPreferences` for opaque UI state; typed columns for what the server reads; no pinned-screens table (D5). |
| "What to store for notification settings? One JSON field?" | Contact fields and channel on `Users`; a normalized `core.NotificationPreferences`; push subscriptions in T10 (D6). |
| "Separate SettingsVersion and PermissionsVersion or one field?" | Separate, and at different levels: user-level `PreferencesTag`; tenant-level `PermissionsTag` (D17, D4). |
| "Version, ETag, or fingerprint?" | tag / stamp / fingerprint / format version (D1). |
| "Shape of inbox tracking?" | Two reserved columns on `core.UserStamps`; semantics in T10 (D4). |
| "Are the listed user states exhaustive?" | Yes for the tenant's knowledge; the rest is identity-server state, read live (D7). |
| "Write-once columns: two UDTTs or service-layer rule?" | Service layer plus `[ServerOwned]`; `Subject` is server-owned not write-once; `Email` editable only while `New` (D3). |
| "Global permissions: system role, `IsPublic`, or separate table?" | `IsPublic` on `Role`, with the no-members and no-wildcard-resource invariants guarded in-transaction (D8, D20). |
| "Do we need `SavedById` on weak entities?" | No; the parent's stamp plus equal `ValidFrom` recovers the actor (D2). |
| "Best convention to encode the Resource a permission secures?" | `<schema>.<LogicalEntityName>`, `*` wildcard (D11). |
| "How to make registering securables and enforcing access control hard to forget?" | Fallback policy, group policy, securable endpoint metadata with a startup audit, and a runtime authorization witness (D13). |
| "Is 'Securable' the right word?" | Yes (D12). |
| "Permissions that stop being valid after a schema change: shim or block?" | Neither: the grant drifts (grants nothing), is diagnosed and shown, never blocks the user's other grants (D22). |
| "Can DB calls #1 and #2 collapse if permissions are cached, re-running when stale?" | Yes: guards G1/G2 prepend every batch; a stale tag throws before business statements run; recompute and re-run once (D18). |
| "How do we guarantee the cache tag is invalidated when the data changes?" | Declared on the entity class, bumped by the emitter; raw SQL must declare its write set; a test-tier write-set audit (D19). |
| "Is 'version' / 'metaversion' the right name?" | tag; format version (D1). |
| "Services should rely on interfaces so distros can replace entities" | Core rules are generic over `TUser : User` / `TRole : Role`; the connect step reads the registered leaf through the model (D20, §3.6). |
| "How to design the API so validators participate in the batch context load?" (touching this theme) | The security validators declare their context (the acting user's memberships and the roles being saved) through T5's validator API and contribute in-transaction guards through `ISecurityGuards` (D20). |
| "A user cannot delete or deactivate their own user, nor strip their own admin permissions" | Validators plus the in-transaction L1 guard under an application lock (D20). |
| "Endpoint answering does X have permission Y on Z, and why" | `ExplainAsync` and the two endpoints (D23). |
| "Should background tasks run under a system user with full permissions?" | A seeded `System` user (id 1) that is an Administrator by membership and can never authenticate (D24); scheduling policy is T10's. |

---

## 6. Seams

**1. Batch abstraction (T2 owns).** Needed: `IBatchBuilder.AddStatement(string sql, IReadOnlyList<BatchParameter> parameters, WriteSet writes, bool mayRetry)`
where `WriteSet` is a required argument (`WriteSet.None` for reads); `AddTableValued(name, rows,
tableTypeOf: Type)` for the ids TVPs; automatic tag bumps derived from `WriteSet` and from the
emitter's own targets via `[BumpsTenantTag]`/`[BumpsUserTag]`; a mapping from `THROW` error numbers
50403/50409/50423/50503 to `TenantAccessDeniedException`/`PermissionsStaleException`/
`SecurityInvariantException`/`SecurityLockTimeoutException`; whole-batch retry on deadlock (1205)
even for non-idempotent batches (rollback is guaranteed); an `IBatchResult` exposing result sets by
ordinal so `IConnectStep.Complete` can read the tag sets. Guards are the first statements of every
batch and the persist batch runs under `SET XACT_ABORT ON; BEGIN TRAN … COMMIT` in the text.

**2. Entity class vs wire shape (T2 owns).** `[NotMapped]` child collections on `User`
(`RoleMemberships`, `NotificationPreferences`) and `Role` (`Permissions`); `[ServerOwned]` as the
editable/server-owned split; `IsActive` is server-owned on save for every `IActivatable`.

**3. One capability, declared once (T5 owns).** `IActivatable` registers action `Activate` and
marks `IsActive` server-owned; every stack registers `Read`, and `Save`/`Delete` when editable;
capability securables carry `FilterRoot` = the stack's logical entity name.

**4. Queryex schema per tenant (T2 owns; T3/T4 consume).** The evaluator validates filters against
the tenant's current `QueryexSchema` instance at recompute; a rebuilt schema (Name2/Name3 gating
change) is followed by a tenant `PermissionsTag` bump issued by the settings save (T3 declares
`[BumpsTenantTag(TenantTags.Permissions)]` on the languages-bearing settings row) so every set is
revalidated. Engine amendment requested: `FilterTree.Leaf(text, rootPath)` (D16).

**5. Version tags (T3 owns).** Tenant tag row holds `PermissionsTag`; user tag lives on
`core.UserStamps.PreferencesTag`; tags are `uniqueidentifier`, regenerated with `NEWID()` in SQL
by the emitter's bump statement; the connect statements read all tenant tags and the user tag in
one result set each; `FormatVersion` is a per-shape `const int`.

**6. Feature composition (T1 owns).** Securable registration and the endpoint audit run in the
realize phase and report through the same aggregated startup diagnostic.

**8. Background-task columns (T10 owns).** Background scopes populate `ICallerContext` with
`UserId = SystemUserId` or the scheduling user; the evaluator treats a null `UserId` as `Denied`.

**9. Request context (T1 owns).** `ICallerContext` as in §3.5, held by a scoped holder (never an
`AsyncLocal` source of truth); the connect step writes `UserId`/`UserKind` through an internal writer
interface; `ISandboxContext` reads the same holder.

**11. Permission evaluation API (T4 owns).** §3.4 verbatim; T5 calls `EvaluateAsync` before building
any query or persist batch; T6 uses the coarse metadata check and the witness.

**14. Telemetry (T2 owns the DB-call budget).** §3.7; connect statements count as part of the batch
they ride, never as a separate round trip.

**15. Notification enqueue riding the save batch (T10 owns).** Recipients are selected against
`core.NotificationPreferences` in SQL inside the same batch.

**16. Connect-call collapse (T4/T5).** D18 is this theme's position: warm path collapses into the
first batch via prepended guards; cold path is one extra round trip per user per instance.

**17. Vocabulary.** D1.

---

## 7. Departures

- **None from ARCHITECTURE.md's Data Layer, Identity, or Observability sections.** Plural table
  names, FK-enforced references, app-assigned ids, seeds in the reserved band, no logic in the
  database (the guards and the app lock are dynamic SQL composed in C#, never persisted modules),
  telemetry names as `const`s without per-tenant tags, and the BFF/`sub` identity model are all
  followed.
- **Departure from the brain dump, not the architecture**: singular table names, `SavedById`+period
  audit vocabulary, user-level permissions version, JSON notification settings, "all" as wildcard,
  six-state invitation model — each replaced above with reasons.
- **One clarification ARCHITECTURE.md should record**: "Capability interfaces — no paired interface
  per entity" stands; this theme's Core rules are generic over the base classes, not over
  interfaces, and no `IUserForConnect`-style interface is introduced.
- **One addition ARCHITECTURE.md should record** under Identity: the distribution's sensitive-operation
  set has a Core-supplied default (D25) that distributions extend but do not shrink.

---

## 8. Verification

Relied on from the research file (`research/users-roles-permissions.md`, verified 2026-09-01):
native RLS mechanics, `SESSION_CONTEXT` semantics and the open parallel-plan defect, the temporal
history table not inheriting predicates; ASP.NET Core 10 `AuthorizationMiddleware`/`CombineAsync`
order, fallback versus default policy, `AllowAnonymous` being absolute, `IAuthorizationRequirementData`
honoured on Minimal API endpoints, cookie auth returning 401/403 on `IApiEndpointMetadata` endpoints;
Odoo/Salesforce/Dataverse permission shapes and the lockout precedents (Entra last-admin rule,
GitHub "you can't change your own role", Odoo self-deactivation guard); `rowversion` semantics,
`uniqueidentifier` sort order and Guid v7 non-sequentiality, `MIN_ACTIVE_ROWVERSION` lesson; the
identity server's invite and delivery-status APIs exactly as implemented (statuses, error strings,
prefix results, `NotFound` scoping, `expectsDeliveryEvents`, the dev admin subject
`00000000-0000-0000-0000-000000000001` seeded only in Development).

Relied on from sibling research files: RCSI defaults and write-skew analysis, `READPAST`/`UPDLOCK`
under RCSI, error 2601/2627 mapping, optimized locking on Azure SQL (`service-pipeline.md` §6);
temporal `UPDATE` writing history rows even when unchanged, period columns as EF 10 shadow
properties, TPT roots cannot be temporal (`data-access.md` §2); `HybridCache` unsuitability and the
private `MemoryCache` recommendation, Rails/Django precedents for tag and format version
(`settings-cache-l10n.md` §1.8, §6); `AsyncLocal` guidance and route-group filter ordering
(`host-tenancy.md` §6–7); the identity client's `<slug>-svc` registration and token recipe
(`core-gl-stacks.md` §3).

Verified by me on 2026-09-01:
- `THROW` requires `error_number` ≥ 50000, needs no `sys.messages` entry, always severity 16,
  terminates the batch absent `TRY…CATCH`, and rolls the transaction back when `XACT_ABORT` is ON —
  https://learn.microsoft.com/en-us/sql/t-sql/language-elements/throw-transact-sql (ms.date 2024-07-26).
- `sp_getapplock` returns 0/1 on success, −1 timeout, −2 cancelled, −3 deadlock victim, −999
  validation error; a `Transaction`-owned lock is released at commit or rollback; the resource name
  is binary-compared and truncated at 255 characters; a deadlock on an application lock does not
  roll the transaction back by itself, hence the explicit check —
  https://learn.microsoft.com/en-us/sql/relational-databases/system-stored-procedures/sp-getapplock-transact-sql (ms.date 2026-06-19).
- Legacy `tellma-ltd/tellma` `dbo.Roles` carries `IsPublic BIT NOT NULL DEFAULT 0`; `dbo.Permissions`
  is `(RoleId, View, Action, Criteria nvarchar(1024), Mask, Memo)` with no CHECK on `Action`;
  `dbo.Users` carried `PermissionsVersion`/`UserSettingsVersion` as `uniqueidentifier DEFAULT NEWID()`,
  `LastAccess`, `LastInboxCheck`, `LastNotificationsCheck`, explicit per-channel notification bits,
  and push-subscription columns directly on the row; `dbo.RoleMemberships` is temporal with `Memo`
  and `SavedById` — raw files under
  https://raw.githubusercontent.com/tellma-ltd/tellma/master/Tellma.Database.Application/dbo/Tables/.
- `FilterTree`, `QuerySpec`, `QueryCompilationOptions`, `ValidationOptions`, `QueryexParameterSlot`,
  `QueryexLanguage`, `QueryexSchema`/`EntityDescriptor`/`PropertyDescriptor`/`NavigationDescriptor`
  as compiled in `src/core/Tellma.Core.Queryex` — including that `Or([])` is `false`, `Leaf("")`
  throws, schema identity is the cache key, and `LanguageVersion` is required on both validation
  and compilation.

Still unverified (does not change a decision, recorded for the spec author):
- Whether `sys.dm_db_index_operational_stats` counters are stable enough on LocalDB for the
  test-tier write-set audit (D19); fall back to Change Tracking on the test database if not.
- Whether `RouteGroupBuilder` conventions reliably stamp `SecurableEndpointMetadata`-adjacent
  metadata onto endpoints added to the group after the convention (research flagged the group
  inheritance as API-shape inference); the startup audit catches any gap regardless.
- The exact clock the emitter uses for `ModifiedAt` (app clock versus `SYSUTCDATETIME()`); either
  works for equality, and T2 should choose the database clock so the stamp is monotonic per row.
