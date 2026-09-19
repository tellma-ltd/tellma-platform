# Users, roles, and permissions — decisions (theme `users-roles-permissions`, spec 0013)

Judged 2026-09-01 against the brain dump, the ten-spec breakdown, ARCHITECTURE.md, specs 0001/0003/0007/0008,
the compiled Queryex and Abstractions code, and `research/users-roles-permissions.md`. This file is
self-contained: every name, type, statement, and column a spec author needs is here. Contract blocks use
the platform's contract notation (names are normative; shape is described, not transcribed); SQL is the
exact shape to emit.

The design in one paragraph. Roles are bags of grant-only `(Resource, Action, Filter?)` rows; a user's
effective set is the union of the rows of active roles the user belongs to and of active public roles;
row filters are Queryex text composed structurally with `FilterTree.Or`, never concatenated; the set is
cached per instance under one tenant-level `Permissions` tag that the save emitter bumps by declaration;
every batch that runs on a caller's behalf starts with a fixed T-SQL prologue that re-resolves the caller,
compares the tags, and skips the batch body when the premises it was composed under no longer hold — so a
read is one round trip and a save two; security invariants (last administrator, public roles have no
members, the seeded Administrator role is intact) are checked inside the persist transaction under an
application lock; a permission that no longer resolves grants nothing and never blocks the user's other
grants.

---

## 1. Critique

### 1.1 The general design

The brain dump's model is right and needs no inventing: grant-only permissions, disjunctive filters,
public grants unioned in, write implying read, inactive roles ignored, bespoke service criteria, "no
permission reads like no record", tenant-owned membership with the identity server owning only
authentication. Odoo's group rules, Salesforce's sharing rules, and Dataverse's cumulative roles converge
on the same shape. What the dump leaves open is mechanism, and five gaps are structural:

1. **No enforcement point for the invariants.** "At least one admin remains", "a public role has no
   members", "you cannot strip your own admin rights" are all described as checks; under read-committed
   snapshot (on by default on Azure SQL, and the migrator turns it on everywhere) two administrators
   removing each other concurrently each see the other remaining and both commit. Only a check inside
   the persist transaction, serialized, is correct; the dump has none.
2. **The freshness story contradicts the round-trip goal.** "Read the versions before executing any API
   call" is a round trip of its own; the batch itself has to refuse to run against a stale permission set.
3. **No system principal.** Seeds, bootstrap, and built-in schedules need an actor for `CreatedById`
   before any human exists, and a principal for unattended work.
4. **The permissions version is on the wrong row.** A per-user `PermissionsVersion` means every role
   edit must fan out to exactly the right users — members for a normal role, everyone for a public role,
   the difference when `IsPublic` flips, the members of a role being deleted before the memberships go —
   in every write path forever. One tenant-level tag bumped by any write to the three security tables
   cannot miss a case.
5. **The user state list persists what the tenant cannot own.** Queued, sent, bounced, delivered are
   identity-server and email-provider facts that arrive unreliably and change after the fact; the tenant
   owns exactly three states on its own evidence.

### 1.2 Detailed choices

- `core.User` (singular) contradicts ARCHITECTURE.md (`gl.Invoices`) and spec 0001 (`InvoicesList`); plural.
- `SavedAt` duplicates the period start; `SavedById` + period loses creation entirely (only recoverable by
  scanning history, which a retention policy eventually deletes); `Center` uses four columns while `User`
  uses two plus a period. One vocabulary: four audit columns everywhere, system-versioning additive.
- `LastActive`, `UserSettingsVersion`, `PermissionsVersion`, `InboxTracking` on the temporal row: every
  `UPDATE` of a temporal table writes a history row even when nothing changed. The dump already flags
  this; the rule is stronger than a split — the only writers of a temporal security table are the save
  pipeline and one documented one-time transition.
- `Subject` has no type. The OpenID Connect `sub` is at most 255 ASCII characters and case-sensitive; the
  identity server issues 36-character GUID strings but the authority is swappable.
- `Permission.Resource` "e.g. invoices, or all": free-form strings drift and the word "all" can collide
  with a real name. Resources need a canonical grammar and a reserved wildcard token.
- `Filter` "only on resources and actions that support it" is contradicted by a wildcard row carrying a
  filter: a filter binds against one root entity, so a filtered wildcard *resource* is unbindable.
- `UserSettings` collides with the tenant `Settings` table and needs no surrogate id: a key/value row is
  not an entity.
- `PushSettings` "?? what shape" conflates two things — what the user wants (a preference) and the
  browser push subscriptions (device credentials the inbox owns). A JSON blob for per-type × per-channel
  opt-ins cannot be joined by the notification writer that must resolve recipients in SQL inside the
  save batch.
- `RoleMembership` has no uniqueness; `(UserId, RoleId)` must be unique at the database.
- The `Filter` / `FilterLanguageVersion` pair lacks the CHECK that they are null together.
- Anyone holding `Save` on roles or users can make themselves an administrator; the dump does not say so
  and does not bound it.

### 1.3 Internal inconsistencies

- "The Save operation updates the audit columns" (plural) versus temporal entities that carry only `SavedById`.
- Inactive *roles* are excluded but inactive *users* are not mentioned; a deactivated user must be refused
  at connect before any permission is consulted.
- "Permissions under inactive roles are not included" but toggling `Role.IsActive` is not listed among
  the writes that must invalidate caches.
- The securable tuple is `(Resource × Action × SupportsFilter × FilterRoot)` while the permission stores
  free text; "validated against the registry" is asserted, not designed.

### 1.4 What the dump does not ask but the spec must answer

The invite API's `Active` status ("already holds a credential; no email sent"); the securable that
defines "administrator" for the lockout invariant; whether permission filters may use declared parameters
(no) and which limits apply; that the cached set has a format version the SPA respects; how a filter over
a language-gated column (`Name3` on a bilingual tenant) behaves (drift); that the activity stamp must be
throttled; deletion rules for users referenced by audit foreign keys; and which operations are sensitive
enough for the step-up challenge spec 0003 reserves.

---

## 2. Decisions

Confidence is high unless stated. Review flags are collected in §9.

### D1 — Vocabulary and placement

- Schema `core`; **plural** table names: `core.Users`, `core.UserStamps`, `core.UserPreferences`,
  `core.NotificationPreferences`, `core.Roles`, `core.RoleMemberships`, `core.Permissions`; history tables
  `core.<Table>History`; sequences `core.sq_<Table>` starting at 1000; ids `int`; the reserved seed band is
  ids 1–999.
- Queryex logical entity names are singular: `User`, `Role`, `RoleMembership`, `Permission`,
  `NotificationPreference`, `UserStamp` (read-only).
- Free-text remark columns are `Notes nvarchar(1024)`.
- **Tag** = opaque cache validator (`uniqueidentifier`, regenerated with `NEWID()` by the bumping
  statement, compared for equality only, never ordered or indexed). **Stamp** = a timestamp (`LastActiveAt`,
  the `ModifiedAt` concurrency stamp). **Fingerprint** = the deployment-derived hash of the securables
  registry. **Format version** = a `const int` per cached shape (what the dump calls "metaversion").
- The permission target is a **securable** (SQL Server's own term for an object permissions apply to).
- Placement: the entity classes (`User`, `UserStamp`, `UserPreference`, `NotificationPreference`, `Role`,
  `RoleMembership`, `Permission`), enums, well-known ids, the securable and evaluation contracts, and the
  telemetry names live in `Tellma.Core.Abstractions`, namespace `Tellma.Core.Abstractions.Access` (EF-free;
  BCL DataAnnotations only). The runtime — evaluator, cache, prologue, registry builder, guards, validators,
  the Core stack feature that registers the entities — lives in `Tellma.Core`, namespace
  `Tellma.Core.Access` (the package is empty today; this is its first content). `[TableType]`, temporal, and
  index configuration are applied fluently by the Core feature's contribute step, because the attribute is
  defined in `Tellma.Core.EntityFrameworkCore`, which Abstractions cannot reference.
- The entity classes are unsealed defaults; a distribution extends by inheritance (`sealed class User :
  Tellma.Core.Abstractions.Access.User`) and selects the leaf at the feature's selection site. Core rules
  and statements are generic over `TUser : User`, `TRole : Role` and name only base-class columns.
  Replacing an entity outright is not supported.

Rejected: singular tables (contradicts the architecture and spec 0001); `bigint` ids; "etag" (HTTP
reserves it for representation validators, which the blob endpoint uses); "version" for tags (implies
ordering); `Tellma.Core.Security` (broader than the theme; "access" is what the evaluator decides);
entity classes in `Tellma.Core` (modules never reference `Tellma.Core`, yet every module entity's
`CreatedById` references `core.Users` and needs the type in the model).

### D2 — Audit vocabulary, temporal as an additive capability, the concurrency stamp

- Every top-level entity carries `CreatedAt datetime2(7)`, `CreatedById int`, `ModifiedAt datetime2(7)`,
  `ModifiedById int` (UTC, `NOT NULL`, FKs to `core.Users`), stamped server-side by the save emitter with
  `SYSUTCDATETIME()` — never the app clock (instances skew; the database clock is one source per tenant).
  Weak entities carry no audit columns and no `SavedById`.
- `ModifiedAt` is the optimistic-concurrency stamp. Rule: user-visible saves and actions stamp
  `ModifiedAt`/`ModifiedById`; bookkeeping never does (bookkeeping lives on `core.UserStamps` or is the one
  documented exception in D18). The root's stamp moves whenever any child row is inserted, updated, or
  deleted, even when no root column changed.
- System-versioning is a per-entity capability (`[Temporal]` from the data-access contract): `core.Users`,
  `core.Roles`, `core.RoleMemberships`, `core.Permissions` opt in; `core.UserStamps`,
  `core.UserPreferences`, `core.NotificationPreferences` do not. Period columns `ValidFrom`/`ValidTo` are
  EF shadow properties and are not in the UDTT. `ModifiedAt` is kept next to `ValidFrom` on temporal rows:
  redundant in value, not in role — it is app-chosen, uniform across temporal and non-temporal entities,
  and is what the wire carries; `ValidFrom` would need an `OUTPUT` read-back to echo.
- Required from the emitter: an `UPDATE` on a temporal table skips rows whose writable columns are
  unchanged (otherwise re-saving a user with ten memberships writes ten history rows), except that a root
  whose children changed is still stamped; children are deleted before parents and inserted after, in a
  fixed table order, so concurrent saves cannot deadlock on table order.
- A child history row's actor is recoverable: SQL Server stamps every row touched in one transaction with
  the same `ValidFrom`, so join the parent's history at equal `ValidFrom`.

Rejected: `SavedAt`/`SavedById` (loses creation; two vocabularies); `rowversion` (moves on every update
including bookkeeping; research §4); `datetimeoffset` (an offset nobody sets to anything but zero,
incomparable with period columns).

### D3 — `core.Users`

Columns (full schema in §4): `Id`; `Kind` (`Human | System`, enum-as-string; `Service` is reserved for
service accounts and added by an expand migration when their spec arrives); `Subject` (the OIDC `sub`;
`varchar(255) COLLATE Latin1_General_100_BIN2`, null until invited, filtered unique); `Email` (required for
humans, null for the system user, filtered unique, the natural key for import); `State`
(`New | Invited | Active`, stored, with CHECK constraints tying it to `Subject`, `InvitedAt`, and
`ActivatedAt` so it cannot drift); `InvitedAt`; `InviteStatus` (`Invited | Reinvited | Active`, the
identity server's word from the last successful invitation, verbatim); `LastInviteError` (the server's
free-text per-user error from the last failed invitation, stored for troubleshooting, never localised or
branched on); `ActivatedAt`; `Name`/`Name2`/`Name3`; `ImageId` (reserved; type and lifecycle are the blob
theme's; the row carries the key only, no fit metadata); `PreferredLanguage` (BCP 47), `PreferredCalendar`,
`PreferredTimeZone` (IANA) — null means the tenant default; `ContactEmail` (null means `Email`),
`ContactMobile` (E.164); `IsActive`; the four audit columns; period.

- **Server-owned** (never taken from a payload; the pipeline overwrites from the database on update and
  sets defaults on insert): `Kind`, `Subject`, `State`, `InvitedAt`, `InviteStatus`, `LastInviteError`,
  `ActivatedAt`, `IsActive`, `ImageId` (written by the blob endpoint), the audit columns.
- **Editable**: the names, `Email` (only while `State = New`; afterwards a change is validation error
  `Users.EmailLockedAfterInvite`, because `Email` is the creates-or-gets key at the identity server and
  editing it desynchronises the two systems), the three preferences (validated against the tenant's
  configured languages and calendars and the IANA list), the contact fields.
- `Subject` is server-owned, not "write-once": it is written only by the Invite action (Core stack theme),
  which is the only code that learns a subject. One UDTT; no create/update split.
- Uniqueness is the database's job (filtered unique indexes); the C# check exists for the message and for
  intra-batch duplicates; errors 2601/2627 map to field errors `Users.EmailTaken`, `Users.SubjectTaken`.
- The typed preference columns are on the temporal row, not in the bag, because background work (an
  email, an inbox item) addresses the user in their language without a request in flight, and because
  they are filterable and exportable. They change rarely.

Rejected: `Subject uniqueidentifier` (ties the schema to one authority's format) and `nvarchar(450)` (the
legacy width; twice the bytes for a value that is ASCII by specification); a separate service-account
table now (the dump defers it; `Kind` lets it arrive as a value, not a table); a magic email for the
system user (a constraint cannot be written against a magic value); a persisted computed `State`
(equivalent, but a computed column is a new shape for the entity contract; the CHECKs give the same
no-drift guarantee).

### D4 — The user state model

`State` moves on the tenant's own evidence only:

| From | To | Trigger | Written |
|---|---|---|---|
| — | `New` | the row is created (UI, import, bootstrap) | `Subject = NULL`, `InvitedAt = NULL` |
| `New`, `Invited` | `Invited` | the bulk-invite call returns `Invited`, `Reinvited`, or `Active` | `Subject`, `InvitedAt`, `InviteStatus`; `LastInviteError = NULL` |
| `New`, `Invited` | unchanged | the bulk-invite call returns a per-user error | `LastInviteError` (through the pipeline: a normal audited mutation) |
| `Invited` | `Active` | the first request in which the connect prologue resolves this subject | `ActivatedAt`; does not stamp `ModifiedAt` (D18) |

`IsActive` is orthogonal (an administrator's switch; false refuses every request regardless of `State`).
Re-inviting a `New` or `Invited` user is allowed (the server re-sends; `InviteStatus` records its word);
re-inviting an `Active` user is validation error `Users.AlreadyActive`. The invite status `Active` means
"already holds a credential, no email was sent": the tenant state becomes `Invited`, the UI renders "added
— no email sent, the user already has an account", and the Core stack theme's `UserService` sends its own
"you were added" notice. Delivery detail (`Pending | Sent | Delivered | Bounced | Complained | Rejected |
Abandoned | Accepted | NotFound`, with `expectsDeliveryEvents` deciding whether `Sent` is terminal) is a
live drill-down through the delivery-status API from the user details page and is never persisted. The
identity server's own lifecycle (`Active | Orphaned | Disabled | Purged`) is invisible to the tenant except
as an invite error. The three states are exhaustive for what the tenant can know.

### D5 — `core.UserStamps`, the non-temporal sibling

One row per user, `UserId` as clustered PK and FK (`ON DELETE CASCADE`), inserted by the same batch that
inserts the user (a companion `INSERT … SELECT … WHERE NOT EXISTS` the Core feature appends after every
`core.Users` insert) and by `HasData` for the system user; the cold connect path re-creates it if absent.
Columns: `LastActiveAt datetime2(0) NULL` (the throttled activity stamp), `PreferencesTag uniqueidentifier
NOT NULL` (bumped by every write to the user's row, preferences, or notification preferences),
`InboxUnreadCount int NOT NULL DEFAULT 0` and `InboxUnseenCount int NOT NULL DEFAULT 0` (materialised
badge counters, reserved for the background-tasks theme, which owns the statements that maintain and
reconcile them; the connect row returns them so a page load gets its badges with zero extra queries).

There is **no per-user permissions tag**; the permissions tag is tenant-level (D17). The activity stamp is
throttled to 60 seconds by the statement's own predicate, so on the common request the `UPDATE` matches
zero rows — a clustered seek, no write, no log record — and additionally skipped by the batch builder when
its per-instance memo says the user was stamped within the interval. An integration test asserts that
1,000 requests add zero rows to `core.UsersHistory`.

Rejected: making `core.Users` non-temporal (loses the audit that matters: who had which name, email,
memberships); timestamps plus `COUNT` queries for the badges (two extra seeks per page load); names
`UserStates` (collides with `State`), `UserActivity`, `UserTracking` (imply a log or telemetry).

### D6 — `core.UserPreferences` (the bag) and the typed preference columns

`core.UserPreferences (UserId int, Key varchar(128), Value nvarchar(max))`, clustered PK `(UserId, Key)`,
not temporal, **not an entity**: no surrogate id, no sequence, no child synchronisation, no audit. Keys are
ASCII dotted segments (`grid.users.columns`, `nav.pinned`, `tour.dismissed`); values are opaque strings
(JSON by convention) the server never interprets; at most 256 keys per user, 32 KB per value.

Written **only** through the self-service endpoints (Core stack theme), by a dedicated statement fed from
the standalone table type `UserPreferenceList (Key, Value)`: delete absent, upsert present, for the
connected user; the statement bumps `UserStamps.PreferencesTag`. The bag never travels through the user
save pipeline and never touches the temporal `core.Users` row, so resizing a grid column does not write a
history row. Pinned quick-access screens are the key `nav.pinned`; an administrator-managed default for
less technical users is a tenant setting (settings theme) the client merges when the key is absent — no
third table.

The server caches nothing from the bag. The typed columns (`PreferredLanguage`, `PreferredCalendar`,
`PreferredTimeZone`) plus the display profile (names, email, image) form the `UserProfile` the connect
prologue returns when the caller's `PreferencesTag` differs from the cached one; the SPA caches the bag
and the profile under the same tag.

Rejected: a surrogate `Id` and the entity pipeline (the root-stamp rule would write a `core.Users` history
row per keystroke-sized change); one JSON column per user (every change rewrites the document); a
pinned-screens table (a stack for a list an administrator edits once a year); typed preferences as bag
keys (background work would parse JSON to address a user).

### D7 — `core.NotificationPreferences`

`core.NotificationPreferences (Id, UserId, NotificationType varchar(64), Email bit, Sms bit, Push bit)`,
not temporal, unique `(UserId, NotificationType)`, a weak child of `User` (synchronised with the user
save; also written by the self-service endpoint restricted to the caller's own row). An absent row means
the type's declared default; types that cannot be muted (a completed export whose file is only reachable
from the notification) are declared so in the notification-type registry the background-tasks theme
owns and validation refuses disabling every channel of such a type. Push *subscriptions* (endpoint, keys,
device label; one per device, rotated by the browser) are a separate table the background-tasks theme
fixes (`core.UserPushSubscriptions`, cascading from `core.Users`); the user row never holds device
credentials. `core.Users` keeps only `ContactEmail` and `ContactMobile`.

The notification writer resolves channels set-based inside the save batch:
`FROM @recipients R LEFT JOIN [core].[NotificationPreferences] S ON S.[UserId] = R.[Id] AND S.[NotificationType] = @type`.

Confidence medium (the background-tasks theme may reshape channels). Rejected: one JSON column
(unqueryable without `OPENJSON` inside the save batch; drifts silently; every new type is an implicit
migration); one row per `(type, channel)` with `IsEnabled` (evolves by rows, but triples the row count
and complicates the join for the common "which channels" question; adding a channel is a rare expand
migration either way).

### D8 — `core.Roles` and public permissions

Columns: `Id`, `Name` (unique), `Name2`/`Name3` (unique when present), `Code` (unique when present; the
natural key for seeds and import), `IsPublic`, `IsActive`, audit, period. Child collection `Permissions`.

`IsPublic = 1` means the role's permissions apply to every active user of the tenant. A public role has
no memberships (validation `Roles.PublicRoleHasMembers` plus the in-transaction guard of D21; the
membership editor hides public roles); it may not hold a permission with `Resource = '*'` (validation
`Permissions.WildcardResourceOnPublicRole`); several public roles may exist and union like any other;
a deactivated public role grants nothing. The load statement covers members and public roles in one
predicate, no `UNION`, no second cache.

The seeded **Administrator** role (`Id = 1`, `Code = 'Administrator'`, D25) is the one role whose
unfiltered `*/*` grant is guaranteed: it cannot be made public, deactivated, or deleted, and its
wildcard permission cannot be removed or filtered (validation `Roles.AdministratorImmutable`; guard L3
as backstop). `Name`, `Name2`, `Name3`, and `Notes` on it remain editable, and further permissions may be
added.

Rejected: a seeded "Everyone" role as the public mechanism (needs the same no-members validation, adds a
magic id, and prevents several public sets managed by different administrators); a role-less permission
table in settings (a second editor, validator, and drift path); a system role with a hardcoded id as the
public grant (same). Packs that want public lookup grants seed an ordinary public role through the runtime
seed pipeline, found by `Code`.

### D9 — `core.RoleMemberships`

Columns: `Id`, `UserId`, `RoleId`, `Notes`, period. Unique `(UserId, RoleId)`; index `(RoleId, UserId)`
for "members of this role" (the members tab, the invariants). A weak child of `User` — synchronised under
the user save; a membership absent from the saved user's list is deleted — and read-only from the role
side (the role details page shows members as an extra with links). No audit columns (D2). `Notes` stays:
"added per ticket 4711" is what an access review asks for.

Rejected: membership as a child of `Role` (onboarding is "create the user, tick the roles");
editing from both sides (two synchronise scopes over one table with no rule for which is authoritative).

### D10 — `core.Permissions` and filter validation

Columns: `Id`, `RoleId`, `Resource varchar(128)`, `Action varchar(32)`, `Filter nvarchar(2048)`,
`FilterLanguageVersion int`, `Notes`, period. CHECK `(Filter IS NULL) = (FilterLanguageVersion IS NULL)`;
CHECK `Resource <> '*' OR Filter IS NULL`. Index `(RoleId) INCLUDE (Resource, Action, Filter,
FilterLanguageVersion)` so the load by role is index-only. No unique index (`Filter` exceeds the key
limit); exact duplicates `(Resource, Action, Filter)` within a role are collapsed silently at save.

Validation at role save (Core-supplied rules `RoleAccessRules<TRole>`, plugged in by `RoleService`):

1. `(Resource, Action)` exists in the securables registry (aliases considered), or is `*` in either
   position (`Permissions.UnknownSecurable`).
2. A filter is allowed only when the securable's `FilterRoot` is non-null and `Resource` is not `*`
   (`Permissions.FilterNotSupported`). A filter with `Action = '*'` on a concrete resource is valid: it
   applies to every filterable action of that resource and never grants a non-filterable one (D14).
3. The filter is validated with `QueryexEngine.Validate` in `Filter` mode against the tenant's current
   schema and the securable's `FilterRoot`, with no declared parameters (an `@x` is a diagnostic),
   `HasUser = true` (`me()` allowed), the limits profile `AccessLimits` (D27), and
   `LanguageVersion = QueryexLanguage.Version`; the version passed is what is stored — the stamp is minted
   here and nowhere else (`Permissions.FilterInvalid` carries the engine diagnostics).
4. `Filter` length ≤ 2048 (a permission criterion is a short predicate; a bounded column is a bounded
   attack surface).
5. Per-role cap `MaxPermissionsPerRole` (D27).

`FilterLanguageVersion` is server-owned (the client never sends it).

### D11 — Resource and action naming, the wildcard, implicit read

- **Resource key** = `<schema>.<LogicalEntityName>` for entity securables (`core.User`, `core.Role`,
  `gl.Center`): the table's schema plus the Queryex logical entity name, which is unique across the schema
  by construction and survives forks (a distribution extending `User` keeps `core.User`). The same grammar
  for non-entity securables a feature registers (`core.Settings`, `core.Settings.General`): two or more
  segments of `[A-Za-z][A-Za-z0-9]*` joined by `.`, ≤ 128 characters, compared ordinal-ignore-case, stored
  as registered.
- **Actions** are PascalCase identifiers ≤ 32 characters: the platform set `Read` (query, details, export
  in any format), `Save` (create and update, including import), `Delete` (by ids, by query, with
  descendants), `Activate` (both directions), plus capability- and service-declared additions (`Invite` on
  `core.User`; later `Post`, `Assign`).
- **`*`** is the only wildcard, valid in either position; the registry refuses `*` as a real name.
- **Implicit read**: any grant on `(R, A ≠ Read, F)` also grants `(R, Read, F)` — the same filter, never a
  wider one; you cannot act on what you cannot see. No other implication exists.
- Matching is exact (no prefix wildcards): every "why" answer and drift diagnostic must explain a match
  the administrator wrote.

Rejected: the canonical table name (`core.Users`) — plural, and a table rename would touch permissions
while the logical name would not; bare logical names (`User`) — unique today, but a schema prefix keeps
non-entity resources collision-free; route segments (`users`) — couple permissions to routing; `All`
as the wildcard (a word a real resource could be); prefix wildcards (`gl.*`).

### D12 — The securables registry

A securable is `(Resource, Action, FilterRoot?, IsSensitive, Feature, Owner?)`: `FilterRoot` is the
logical entity name filters bind against (null = filters unsupported; a name, not a descriptor, because
the tenant's schema is rebuilt per configuration and descriptors compare by identity); `IsSensitive`
marks the step-up set (D26); `Feature` names the composition feature that registered it, for diagnostics;
`Owner` is reserved for weak entities exposed as query roots (D16).

`ISecurableRegistry` is an immutable, deployment-scoped singleton frozen in the realize phase of
`AddTellma` from every `ISecurableContributor`:

- the CRUD stack feature contributes, per entity stack, one securable per capability it realises — `Read`
  always; `Save` when editable; `Delete` when deletable; `Activate` when the entity carries `IsActive`;
  each with `FilterRoot` = the stack's logical entity (tree entities add nothing: `GetByParentIds` is
  `Read`, `DeleteWithDescendants` is `Delete`; export is `Read`; import is `Save`);
- a service method decorated `[Securable("Invite")]` (resource defaulting to the service's entity) or
  `[Securable("Save", "core.Settings.General")]` contributes that securable, and the endpoint projection
  copies it into the endpoint's metadata;
- a feature with non-endpoint securables adds them in its contribute step;
- `RequireSecurable(resource, action)` on a custom Minimal API endpoint is harvested from
  `EndpointDataSource`.

Startup validation, aggregated with the rest of `AddTellma`'s gate: no duplicate `(Resource, Action)` (a
duplicate with the same filter root is idempotent; with a different one it is an error); every
`FilterRoot` resolves to an entity of the distribution's Queryex schema and is not a weak entity; every
`Owner` names a registered resource and an existing navigation; every securable named by endpoint
metadata exists. `Alias(oldResource, newResource)` lets stored permissions naming a renamed resource
resolve for one release while the migrator's data step rewrites them. `Fingerprint` (hex SHA-256 over
the sorted `Resource|Action|FilterRoot|IsSensitive` lines) changes only on deploy; the role editor caches
the securables list under it. The registry is the role editor's data source (localised display names
resolved by resource and action keys) and the drift oracle (D23).

### D13 — Hard to leave unsecured

Four layers, each catching what the previous cannot:

1. **Fallback policy** on the host: `RequireAuthenticatedUser()`. An endpoint with no authorization
   metadata is at least authenticated. `AllowAnonymous` anywhere inside the tenant group is a startup error.
2. **Tenant group** `/{tenantId}/api/web` carries `RequireAuthorization()` and the tenant endpoint filter
   (tenant resolution, sandbox, suspension, the caller context), so every projected and custom endpoint
   inherits it.
3. **Endpoint metadata**: every endpoint in the group carries exactly one of
   `SecurableEndpointMetadata(Resource, Action)` (projected automatically for stack endpoints; attached by
   `[Securable]` or `RequireSecurable(...)` on custom ones) or `MemberEndpointMetadata` (any connected
   active member; the `me/…` endpoints). A **startup audit** walks `EndpointDataSource` and fails startup,
   in the same aggregated diagnostic as composition, for any endpoint in the group with neither, and for
   any securable metadata not in the registry. The metadata drives a **coarse check** in an endpoint
   filter that runs after the tenant filter: the caller's cached set must contain at least one candidate
   grant for `(Resource, Action)`, else 403 — cheap (an in-memory lookup), and it denies zero-grant users
   even when a custom handler forgot everything else. The securable is deliberately **not** an ASP.NET
   authorization requirement: the authorization middleware runs before the tenant filter has resolved the
   caller, and a cold cache would then need a database round trip inside an authorization handler.
4. **Authorization witness**: `IAccessEvaluator` records every evaluation on the request's caller
   context. An endpoint filter installed by the group asserts, before a 2xx result is written, that the
   endpoint's declared securable (or `MemberEndpoint`, which the connect step records) was evaluated
   during the request; otherwise it replaces the result with 500, logs `AccessEvents.MissingWitness` at
   Error, and increments `tellma.access.witness.missing`. Data never leaves through a handler that did
   not ask.

The record-level check — the filter — lives only in the service: the CRUD pipeline obtains one
`AccessDecision` per operation and applies its `FilterTree` to every query and its pre/post checks to
every write; custom service code obtains the same decision through `IAccessEvaluator.Require`, which is
the only API that yields a filter. There is no way to obtain SQL for a filtered query through the stack
without a decision in hand. MCP tools and the public API reach services without these endpoints, which is
why the service check is authoritative and the endpoint check is defence in depth.

Confidence high on 1–3, medium on 4.

### D14 — Permission evaluation semantics

`UserAccess.Decide(securable, bespoke, queryRoot?)` is a pure function over the cached, resolved set —
no I/O, no clock, no ambient state:

```
if set.IsSystem: return Unrestricted (grant: System)
candidates = resolved rows where (Resource = R or '*')
             and (Action = A or '*' or A = 'Read')
if securable.FilterRoot is null: candidates = candidates where Filter is null
leaves = distinct Filter texts of candidates, sorted ordinally, as Leaf(text) ∪ bespoke leaves
if any candidate has Filter = null: return Unrestricted (grants: those rows)
if leaves is empty: return Denied (grants: none; problems: excluded rows that would have matched)
return Filtered (Filter = Or(leaves) [wrapped in Via(Owner.Navigation) when queryRoot is a weak root], grants: filtered rows and bespoke sources)
```

Rules encoded: public roles' rows are in the set like any other (marked `PublicRole` in the "why");
inactive roles were excluded at load; inactive users never reach evaluation (the prologue refuses); an
unfiltered grant absorbs every filtered one; filters union (`Or`); an empty `Or` is `false` (fail closed —
the decision short-circuits to `Denied` before compiling anything); a filtered row never grants a
non-filterable action; `Read` inherits the filter of the grant that implies it; identical filter texts
across roles contribute one leaf and leaves are sorted, so two users with the same grant set produce
byte-identical SQL and the engine's caches are hit rather than fragmented; **bespoke criteria** from
`IAccessCriteriaProvider` implementations registered for the resource are additional leaves that can allow
access on their own (the shape "documents assigned to me" needs) and never absorb or remove anything; a
caller context with no user is `Denied` for everything.

Consumers: the CRUD pipeline calls `EvaluateAsync(resource, action)` once per operation and conjoins
`decision.Filter` into `QuerySpec.Filter` as `And([userFilter, decision.Filter])`; collection operations on
`Denied` throw the forbidden exception (403, so the SPA can hide the page); a by-id read whose row is
outside the filter returns not-found (404, the dump's "same as non-existent"); writes pre-check the ids
they touch in the first batch — ids outside the caller's `Read` filter are 404, ids inside `Read` but
outside the `Save`/`Delete`/`Activate` filter are 403 (the caller already knows the id; 403 is honest and
stops a probing loop of save attempts) — both as count queries over the list-restriction TVP; the
post-check re-evaluates the persisted rows against the same filter inside the persist transaction; details
pages load related entities and extras under the root's `Read` without further filtering (the accepted
disclosure: a document's customer name is visible to whoever can see the document — stated in the spec
because it is the one place the row filter is knowingly bypassed).

### D15 — Row-level security as `FilterTree`; native RLS rejected

Row filters are composed structurally and compiled by Queryex with `me()`/`today()`/`now()` as parameter
slots, so per-user values never enter the SQL text and one parameterised statement serves every user
with the same grant shape. SQL Server security policies are **not** used, and the spec records why so the
next review does not reopen it: they are logic in the database (the architecture forbids it); a
schema-bound inline function cannot hold user-authored Queryex with navigations, `descendantOf`, and
`me()` and cannot be redeployed on every role edit; schema binding blocks `ALTER COLUMN` and fights
expand/contract; history tables are unprotected by default; indexed views are impossible on RLS tables;
dbo is filtered too, so the migrator, support tooling, and background jobs need a bypass coded into the
predicate — a fail-open hazard; the "can I, and why" query needs the model in C# regardless; and tenant
isolation, the headline RLS use case, is already physical. `SESSION_CONTEXT` is reserved for correlation
ids, never for authorization (and has an open parallel-plan defect).

### D16 — Weak entities as query roots: `FilterTree.Via` (reserved)

When weak entities become queryable roots (reports over `RoleMembership`, later invoice lines), the
securable of a weak entity declares `Owner = (OwnerResource, OwnerNavigation)` — `core.RoleMembership` →
(`core.User`, `User`) — and the evaluator decides the owner's securable and rebases the resulting filter
through the navigation with a new node `FilterTree.Via(navigation, inner)`, under which every path in
`inner` resolves from the navigation's target and context functions bind as usual; diagnostics carry
`Filter.Via[User]` locations. The engine amendment is the data-access theme's to document (spec 0008 is
frozen). Until it ships, no weak entity is a query root and the registry refuses a securable whose
`FilterRoot` is a weak entity.

Rejected: rewriting permission *text* (`PostingDate > X` → `Parent.PostingDate > X`) — needs a parser on
the host side, mangles literals and `me()`, and would re-stamp language versions; separate filters
authored per weak entity — the administrator writes the same rule twice and they drift.

Confidence medium (unbuilt).

### D17 — The permission set, its cache, and the tenant-level tag

- **Tag**: one tenant-level tag named `Permissions` in the settings theme's tenant-tag table, bumped by
  every write to `core.Roles`, `core.Permissions`, or `core.RoleMemberships` — declared once on the three
  entity classes with `[BumpsTenantTag(TenantTags.Permissions)]`, so the emitter bumps automatically
  (D20). `core.Users` writes do **not** bump it: `IsActive`, `Kind`, and `State` are read live by the
  prologue. There is no user-level permissions tag.
- **Entry**: `UserAccess` (immutable) per `(tenantId, userId)`: the tag it was built under, `ComputedAt`,
  the resolved grants (filter texts interned per instance so a role's filter is one object however many
  members it has), the problems (D23), memoized composed trees per `(resource, action)`, and
  `FormatVersion = 1` (a `const int` bumped by hand when the shape changes; guards the SPA's copy, which
  outlives deploys).
- **Store**: a private bounded `MemoryCache` per process (`SizeLimit` in entries, `Size = 1`,
  `TrackStatistics = true`; default 10,000 entries, sliding expiration 4 hours), single-flight per key so a
  burst from one user on a cold instance issues one load; the entry is replaced, never mutated. Not
  `HybridCache` (serialises every write, shares the DI memory cache, process-local tag stamps that cannot
  be validated against a database value).
- **Maximum age**: `PermissionsMaxAge` (default 15 minutes), after which the next batch asks the prologue
  to return the rows (`@tm_ReloadPermissions = 1`) without failing the guard, and the set is rebuilt —
  the bound on damage from an out-of-band edit that forgot the bump.
- **Build**: rows arrive from the prologue (D18) — the tag is read into a variable *before* the rows are
  selected, so a bump racing the load yields an older tag than the data (one spurious rebuild next
  request), never a newer tag over older data. Each row is resolved against the registry and each
  distinct filter text validated once with `Validate` under its stored version (the engine's own caches
  make repeats free); unresolved rows become problems.
- **Staleness window**: one in-flight batch per instance (the prologue's tag read is the linearization
  point; D18). No polling, no cross-instance channel other than the tags.
- **Memory**: a grant is roughly 150 bytes plus its interned filter; 10,000 entries is under 400 MB worst
  case and typically far less; the gauge (D28) makes the real number visible.

Rejected: a per-user tag with fan-out on role save (must be right in every path; the `IsPublic` flip and
the delete-before-membership-loss cases are easy to miss; the recompute cost of the tenant-level tag is
one small query per active user per instance per role edit — a rare administrative action); caching per
role and composing per user (an invalidation edge between two caches; revisit if the gauge says so).

### D18 — The connect prologue and the batch guard (seam 16)

Every batch that runs on behalf of a caller begins with a fixed-text T-SQL prologue that resolves the
caller by subject, refuses unknown or deactivated principals, stamps activity (throttled), flips
`Invited → Active` once, reads the tenant tags and the caller's user tag, returns the caller's permission
rows only when the caller's cached tag is stale (or a reload was requested), returns the caller's profile
only when the preferences tag is stale, and computes a guard bit. The body of the batch is wrapped in
`IF @tm_Guard = 1 BEGIN … END`, so it executes only when the premises it was composed under — the user id,
the permissions tag, the tenant settings tag (the Queryex schema and time zone depend on it) — still hold.
A failed guard costs the prologue's few seeks, never a query run under stale permissions, and the rows
needed to recompose arrive in the same round trip.

Parameters bound by the builder (`@tm_` is reserved for platform prologue names, as `@qx` is for Queryex):

| Parameter | Type | Value |
|---|---|---|
| `@tm_Subject` | `varchar(255)` | the caller's `sub` |
| `@tm_Now` | `datetime2(7)` | the app's UTC clock, for the activity stamp and the activation flip only |
| `@tm_StampActivity` | `bit` | 0 for endpoints carrying `NoActivityStampMetadata` (polling) |
| `@tm_ExpectedUserId` | `int` | the cached user id; `NULL` on the cold path |
| `@tm_ExpectedPermissionsTag` | `uniqueidentifier` | the cached set's tag; `NULL` when not cached |
| `@tm_ExpectedPreferencesTag` | `uniqueidentifier` | the cached profile's tag; `NULL` when not cached |
| `@tm_ExpectedSettingsTag` | `uniqueidentifier` | the tenant `Settings` tag the schema was built from |
| `@tm_ReloadPermissions` | `bit` | 1 when the cached set is older than `PermissionsMaxAge` |

```sql
DECLARE @tm_UserId int, @tm_Kind varchar(16), @tm_IsActive bit, @tm_State varchar(16),
        @tm_PreferencesTag uniqueidentifier, @tm_Unread int, @tm_Unseen int,
        @tm_PermissionsTag uniqueidentifier, @tm_SettingsTag uniqueidentifier,
        @tm_PermissionsStale bit = 0, @tm_ProfileStale bit = 0, @tm_Guard bit = 0;

-- Resolve the caller: one seek on the covering Subject index, one clustered seek on UserStamps.
SELECT @tm_UserId = U.[Id], @tm_Kind = U.[Kind], @tm_IsActive = U.[IsActive], @tm_State = U.[State],
       @tm_PreferencesTag = S.[PreferencesTag], @tm_Unread = S.[InboxUnreadCount], @tm_Unseen = S.[InboxUnseenCount]
FROM [core].[Users] AS U
JOIN [core].[UserStamps] AS S ON S.[UserId] = U.[Id]
WHERE U.[Subject] = @tm_Subject;

-- Tags are read before any permission rows (D17: the safe direction under a racing bump).
SELECT @tm_PermissionsTag = [Tag] FROM [core].[TenantTags] WHERE [Name] = N'Permissions';
SELECT @tm_SettingsTag    = [Tag] FROM [core].[TenantTags] WHERE [Name] = N'Settings';

IF @tm_UserId IS NOT NULL AND @tm_IsActive = 1
BEGIN
    -- Throttled activity stamp: matches zero rows on the common request.
    IF @tm_StampActivity = 1
        UPDATE [core].[UserStamps] SET [LastActiveAt] = @tm_Now
        WHERE [UserId] = @tm_UserId
          AND ([LastActiveAt] IS NULL OR [LastActiveAt] < DATEADD(second, -60, @tm_Now));

    -- First authenticated request on this tenant: Invited -> Active, once per user, ever.
    -- The one write to a temporal table outside the save pipeline; does not touch ModifiedAt.
    IF @tm_State = N'Invited'
    BEGIN
        UPDATE [core].[Users] SET [State] = N'Active', [ActivatedAt] = @tm_Now
        WHERE [Id] = @tm_UserId AND [State] = N'Invited';
        SET @tm_State = N'Active';
    END;

    SET @tm_PermissionsStale = CASE WHEN @tm_ExpectedPermissionsTag IS NULL
                                      OR @tm_ExpectedPermissionsTag <> @tm_PermissionsTag THEN 1 ELSE 0 END;
    SET @tm_ProfileStale     = CASE WHEN @tm_ExpectedPreferencesTag IS NULL
                                      OR @tm_ExpectedPreferencesTag <> @tm_PreferencesTag THEN 1 ELSE 0 END;
    SET @tm_Guard = CASE WHEN @tm_UserId = @tm_ExpectedUserId
                          AND @tm_PermissionsStale = 0
                          AND @tm_SettingsTag = @tm_ExpectedSettingsTag THEN 1 ELSE 0 END;
END;

-- Result set 0: the connect row. Always present, exactly one row.
SELECT @tm_UserId AS [UserId], @tm_Kind AS [Kind], @tm_IsActive AS [IsActive], @tm_State AS [State],
       @tm_PreferencesTag AS [PreferencesTag], @tm_Unread AS [InboxUnreadCount], @tm_Unseen AS [InboxUnseenCount],
       @tm_PermissionsStale AS [PermissionsStale], @tm_ProfileStale AS [ProfileStale], @tm_Guard AS [GuardPassed];

-- Result set 1: every tenant-level tag. Always present; a dozen rows.
SELECT [Name], [Tag] FROM [core].[TenantTags];

-- Result set 2: the caller's effective permission rows. Present iff PermissionsStale = 1 or a reload was asked.
IF (@tm_PermissionsStale = 1 OR @tm_ReloadPermissions = 1)
   AND @tm_UserId IS NOT NULL AND @tm_IsActive = 1 AND @tm_Kind <> N'System'
    SELECT P.[Id], P.[RoleId], R.[Name], R.[IsPublic], P.[Resource], P.[Action], P.[Filter], P.[FilterLanguageVersion]
    FROM [core].[Permissions] AS P
    JOIN [core].[Roles] AS R ON R.[Id] = P.[RoleId]
    WHERE R.[IsActive] = 1
      AND (R.[IsPublic] = 1
           OR EXISTS (SELECT 1 FROM [core].[RoleMemberships] AS M
                      WHERE M.[RoleId] = R.[Id] AND M.[UserId] = @tm_UserId));

-- Result set 3: the caller's profile. Present iff ProfileStale = 1.
IF @tm_ProfileStale = 1 AND @tm_UserId IS NOT NULL AND @tm_IsActive = 1
    SELECT [Name], [Name2], [Name3], [Email], [ImageId], [PreferredLanguage], [PreferredCalendar], [PreferredTimeZone]
    FROM [core].[Users] WHERE [Id] = @tm_UserId;

IF @tm_Guard = 1
BEGIN
    -- The batch body, verbatim: Queryex queries, save statements, raw SQL, tag bumps, guards,
    -- notification inserts. A persist body opens its own transaction here
    -- (SET XACT_ABORT ON; BEGIN TRAN ... COMMIT), so the prologue's stamp and flip never sit inside it.
END;
```

**Reader contract.** Sets 0 and 1 are always present; set 2 iff `PermissionsStale = 1` or a reload was
requested (and the caller is an active non-system user); set 3 iff `ProfileStale = 1`; the body's sets
follow iff `GuardPassed = 1`. The reader consults the connect row and knows how many `NextResult()` calls
to make. Nothing in the prologue is inside a transaction; the stamp and the flip are autocommit,
idempotent, single-row statements, so the prologue is always retry-safe.

**Variants.** `ConnectPrologue.ForUser(userId)` — resolves by `U.[Id] = @tm_UserId`, no activity stamp,
no activation flip; background scopes running as a user. `ConnectPrologue.System` — no user lookup;
reads tenant tags only and guards on `@tm_ExpectedSettingsTag`; the migrator's seeds and built-in
schedules, whose set is `UserAccess.System` (unrestricted, never loaded).

**Plan cache.** The prologue text is byte-identical for every batch of the same variant and its
parameters always bind with the same SQL types; a batch's plan differs from another's only by its body,
exactly as without the prologue.

**Concurrency window.** Under read-committed snapshot each statement reads its own snapshot: a role edit
committing between the prologue's tag read and a body statement is not observed by that body, and the
next request sees the new tag. That window — one in-flight batch — is the accepted worst case; no lock is
taken on the tag row (an S lock would only serialise revocations against in-flight requests without
changing any observable outcome and would create S→X deadlocks between two role saves).

Rejected: a separate connect call (the dump; doubles round trips); running the body optimistically and
discarding results on mismatch (executes queries under stale permissions — a leak for reads and a hazard
for writes — and wastes the discarded work; the `IF` costs nothing); `THROW`-ing guards (structurally
equivalent, but the failed round trip then carries no rows, so a stale path costs a recompute round trip
plus a re-run instead of one re-run); membership cached without a tag (unbounded staleness).

### D19 — Round trips per operation, the cold path, the runner, and the failure modes

`IGuardedBatchRunner` is the only way a service executes a batch on a caller's behalf: it obtains the
`ConnectedUser` (cache, or a **cold** prologue-only round trip with `@tm_Expected* = NULL` and no body,
which returns the user, tags, permission rows, and profile in one trip — once per user per instance, or
after eviction), invokes the caller's composer with it, executes, and on `GuardPassed = 0` applies the
connect result to the cache (fresh rows, fresh profile; a changed settings tag triggers the settings
theme's reload) and re-invokes the composer **once**. A second failure raises `ConnectGuardException`
(409): two role edits racing one request is not worth a third attempt.

| Operation (warm cache) | Round trips | Contents |
|---|---|---|
| Query (capped count, ancestors) | 1 | prologue; page query; count; ancestors |
| Details | 1 | prologue; main row; children; related entities; extras; row echo |
| Save, update | 2 | (1) prologue; validation context; RLS pre-checks — (2) prologue; `BEGIN TRAN`; persist; RLS post-check; tag bumps; guards; notifications; read-back; `COMMIT` |
| Save, create, no context needed | 1 | prologue; transaction; persist; read-back |
| Activate / deactivate / delete by ids | 1 | prologue; transaction; RLS-filtered update or delete with affected-count check; bumps; guards; `COMMIT` |
| Role save | 2 | (1) context (name/code pre-image, members of the roles being saved, the caller's set is in memory) — (2) persist; bump; guards |
| "Can I, and why" for the caller | 0 | in memory |
| "Can I, and why" for another user | 1 | that user's permission rows (`ConnectPrologue.ForUser` with `@tm_ExpectedPermissionsTag = NULL`) |
| SPA connect at app start (`me`) | 1 | prologue only |

A "+1" applies on the cold or stale path: exactly one extra round trip.

Failure modes, stated:
- *Unknown subject* (`UserId IS NULL`): `NotMemberException` → 403 `Access.NotMember`. The session cookie
  is untouched (the user may be a member of another tenant of the distribution).
- *Deactivated user* (`IsActive = 0`): `UserDeactivatedException` → 403 `Access.UserDeactivated`, from any
  instance on the very next batch — no cached state is consulted; the entry is dropped on the refusing
  instance and dies elsewhere on the request that observes it.
- *Stale permissions*: transparent, one recompose within the same runner call.
- *Stale settings tag*: the settings theme reloads (one round trip of its own); the batch recomposes once.
- *Stale profile*: no guard failure (the profile does not shape the batch); set 3 refreshes the cache.
- *RLS pre-check ordering*: the pre-check runs in the validation batch under a guard, the post-check inside
  the persist transaction under a guard; if permissions changed in between, the persist guard fails and the
  runner recomposes the persist batch, whose post-check evaluates the new filter — the post-check is the
  authoritative one for the rows as saved, and the recomposed persist statements are themselves
  RLS-filtered.
- *Concurrent first requests*: the activation flip is a conditional `UPDATE`, idempotent.
- *Background scopes*: `ConnectAsUserAsync(userId)` and `ConnectAsSystemAsync()`; permissions are evaluated
  at run time with the same evaluator; a scope with no user is `Denied` for everything.

### D20 — Every write path declares what it writes; tags bump by declaration

- Entities declare the tags their table bumps: `[BumpsTenantTag(TenantTags.Permissions)]` on `Role`,
  `RoleMembership`, `Permission`; `[BumpsUserTag(UserTags.Preferences, userIdProperty: "Id")]` on `User`
  and `[BumpsUserTag(UserTags.Preferences, userIdProperty: "UserId")]` on `NotificationPreference`. The
  attributes are inherited by distribution leaves. The emitter appends the bump statements for every table
  its save statements touch, **inside the same transaction, before the writes** (so a role being deleted
  still bumps, and pre-images are available to any rule that needs them):
  `UPDATE [core].[TenantTags] SET [Tag] = NEWID() WHERE [Name] = N'Permissions';` and
  `UPDATE S SET [PreferencesTag] = NEWID() FROM [core].[UserStamps] AS S JOIN @tm_UserIds AS X ON X.[Id] = S.[UserId];`.
- The preference-bag statement (D6) bumps the caller's `PreferencesTag` itself.
- Raw SQL added to a batch must state its write set (`WriteSet.None` for reads; a list of entity types
  otherwise) as a **required** argument; the builder derives bumps from it. Omission is visible at the
  call site.
- Test tier: the integration batch executor snapshots `sys.dm_db_index_operational_stats` leaf
  insert/update/delete counts per table around each batch and fails the test when a table outside the
  declared write set changed (falling back to change tracking on the test database if the counters prove
  unstable on LocalDB).
- Out-of-band writes (support tooling, hotfix scripts) must end with the bump; `PermissionsMaxAge` bounds
  the damage when they do not.
- New users get their `core.UserStamps` row from a companion insert emitted right after the `core.Users`
  insert:

```sql
INSERT [core].[UserStamps] ([UserId], [PreferencesTag], [LastActiveAt], [InboxUnreadCount], [InboxUnseenCount])
SELECT X.[Id], NEWID(), NULL, 0, 0
FROM @tm_UserIds AS X
WHERE NOT EXISTS (SELECT 1 FROM [core].[UserStamps] AS S WHERE S.[UserId] = X.[Id]);
```

Rejected: bumping in C# after commit (a crash between commit and bump leaves caches stale forever);
per-user permissions bumps (D17).

### D21 — Lockout guards and security invariants

C# validators produce field-level messages; in-transaction guards produce correctness. Both are
Core-owned (`UserAccessRules<TUser>`, `RoleAccessRules<TRole>`, `IAccessGuards`) and applied to the base
types, so a distribution leaf cannot drop them; the Core stack theme's services plug them into the pipeline.

**Validators (before persist)**:
- `Users.CannotDeactivateSelf`, `Users.CannotDeleteSelf` — the acting user's id in an `Activate` (to
  inactive) or `Delete` request.
- `Users.CannotRemoveOwnAdministratorMembership` — the acting user may not delete their own membership in
  role 1 (GitHub's "you can't change your own role"; a hand-over is "make Bob an administrator, then Bob
  removes you").
- `Roles.PublicRoleHasMembers`, `Permissions.WildcardResourceOnPublicRole`, `Roles.AdministratorImmutable`,
  `Users.SystemUserImmutable` (row 1 accepts no edits, activation, or deletion), `Roles.HasMembers` on delete.
- `Permissions.EscalationBeyondSelf` — a permission `(R, A, F)` may be saved only by a caller whose own
  effective set holds `(R, A)` **unrestricted** (or a wildcard covering it); filtered grants on the
  caller's side do not qualify because filter implication is undecidable. A caller holding `*/*` is exempt.
- `Users.MembershipEscalation` — adding a membership to a role requires that every permission of that role
  passes the same test against the caller's set (the role's permissions are loaded in the validation
  context; the caller's set is in memory).

These two escalation rules bound delegation to "a role editor can hand out at most what they hold". Without
them, `Save` on `core.User` lets anyone add themselves to the Administrator role, and `Save` on `core.Role`
lets anyone add `*/*` to a role they belong to.

**Guards (in the persist batch)** — every batch that writes any of `core.Users`, `core.Roles`,
`core.RoleMemberships`, `core.Permissions` first serialises on a per-tenant-database application lock,
which makes the invariants race-free by construction (two administrators removing each other concurrently
would otherwise both pass an `EXISTS` under read-committed snapshot), then checks the invariants after the
writes, before `COMMIT`:

```sql
DECLARE @tm_Lock int;
EXEC @tm_Lock = sp_getapplock @Resource = N'tellma:core.access', @LockMode = N'Exclusive',
                             @LockOwner = N'Transaction', @LockTimeout = 5000;
IF @tm_Lock < 0 THROW 51004, N'Access.LockTimeout', 1;
-- ... writes and bumps ...
-- L1: at least one active, invited-or-active human administrator remains.
IF NOT EXISTS (SELECT 1 FROM [core].[Users] AS U
               JOIN [core].[RoleMemberships] AS M ON M.[UserId] = U.[Id]
               WHERE M.[RoleId] = 1 AND U.[Kind] = N'Human' AND U.[IsActive] = 1
                 AND U.[State] IN (N'Invited', N'Active'))
    THROW 51001, N'Access.LastAdministrator', 1;
-- L2: public roles have no members.
IF EXISTS (SELECT 1 FROM [core].[RoleMemberships] AS M
           JOIN [core].[Roles] AS R ON R.[Id] = M.[RoleId] WHERE R.[IsPublic] = 1)
    THROW 51002, N'Access.PublicRoleHasMembers', 1;
-- L3: the Administrator role is active, not public, and still holds its unfiltered */* grant.
IF NOT EXISTS (SELECT 1 FROM [core].[Roles] AS R
               JOIN [core].[Permissions] AS P ON P.[RoleId] = R.[Id]
               WHERE R.[Id] = 1 AND R.[IsActive] = 1 AND R.[IsPublic] = 0
                 AND P.[Resource] = N'*' AND P.[Action] = N'*' AND P.[Filter] IS NULL)
    THROW 51003, N'Access.AdministratorRoleDamaged', 1;
```

`sp_getapplock` returns a code rather than throwing, hence the explicit check; the lock is
transaction-owned and released at commit or rollback. Platform guards `THROW` in the range 51000–51999
with the validation code as the message; the batch executor maps that range to `AccessInvariantException`
(a validation exception, 422) and `XACT_ABORT ON` rolls the transaction back. The invariant is defined over
the **seeded Administrator role only**, which keeps L1 a single `EXISTS` and makes recovery deterministic:
the seeded role is always the way back in. The guards are skipped when the acting context is the system
user (tenant bootstrap creates the first administrator from zero). A user narrowing their own permissions
while another administrator remains is allowed.

Recovery: the identity server's break-glass administrator holds no tenant permissions and is not a tenant
recovery path; the documented path is the migrator's `--bootstrap-admin <email>` (D25), which requires
database-level access by design. A permanent hidden administrator row is a standing credential and is
rejected.

Confidence high on L1–L3 and the lock; medium on the escalation rules.

### D22 — Deletion and deactivation semantics

- A `User` is deletable only while `State = New` and `Kind = Human` (`Users.OnlyNewUsersDeletable`): every
  user that ever acted is referenced by audit FKs and must be deactivated instead. The delete recipe
  removes `RoleMemberships` and `NotificationPreferences` explicitly (children first); `UserStamps` and
  `UserPreferences` cascade.
- A `Role` is deletable when it has no memberships and is not the Administrator role; its permissions go
  with it (explicit delete, children first). Child→parent FKs of client-edited children are `NO ACTION`;
  system-owned rows cascade.
- **Deactivation**: `Activate` on `core.User` flips `IsActive` through the pipeline (stamps `ModifiedAt`).
  The next prologue on any instance refuses the user. The action's post-commit side effect closes the
  user's SignalR connections **for this tenant** (self-hosted: tracked abort; Azure SignalR: the
  close-connections data-plane call), per spec 0003's session rules. The distribution session cookie is not
  touched — the user may be a member of other tenants of the same distribution; ending the whole session is
  the host theme's decision when no membership remains.

### D23 — Drift policy

A permission row **drifts** — grants nothing, fail closed — when its `Resource` is not registered
(aliases considered) (`UnknownResource`); its `Action` is not registered for the resource
(`UnknownAction`); it carries a filter but the securable has no `FilterRoot` or the resource is `*`
(`FilterUnsupported`); its `FilterLanguageVersion` is outside `[QueryexLanguage.Minimum,
QueryexLanguage.Version]` or differs from the version the composed query compiles under
(`VersionUnsupported`; a tree cannot mix versions, and the differential migration spec 0008 requires on a
version change re-stamps stored text, so the window is the deploy itself); or `Validate` reports any
diagnostic against the tenant's current schema — a renamed column, a removed navigation, a language-gated
`Name3` on a bilingual tenant (`FilterInvalid`, carrying the diagnostics). A drifted row never affects the
user's other rows (an administrator whose one broken permission removed *all* their access could not fix
it). It is listed in `UserAccess.Problems`, surfaced in the "why" answer, in the role details response as a
per-permission diagnostic (code and location per spec 0008 §14), in the role editor as a banner, in
structured logs once per `(permission id, code)` per tag cycle (`AccessEvents.PermissionDrift`, Warning),
counted on `tellma.access.permissions.unresolved{problem}`, and listed by the migrator's post-deploy
report, which runs `Validate` over every stored filter of every tenant database so a deploy that breaks a
permission is known before a user notices. `*/*` grants never drift. A rename ships an `Alias` for one
release and a data-migration recipe that rewrites the strings; there is no shim and no tenant-wide block.
A securable removed by a new deployment while an N−1 instance still runs is accepted by the old instance
and drifted by the new one; the union during the rollout window is bounded by the old version's semantics.

### D24 — "Can I, and why", and the bootstrap call

- `me` (`MemberEndpointMetadata`; verb per the web-API theme): the connect result for the caller — profile,
  `PreferencesTag`, the compact access set `[{ resource, action, hasFilter }]` with its tag and
  `FormatVersion`, the tenant tags, the securables `Fingerprint`, and the two inbox counters. The SPA's
  first call after sign-in and its refresh call whenever a response carries a changed tag. Every API
  response carries a `Tellma-Tags` header (owned by the web-API theme) with the caller's current
  `PreferencesTag`, the tenant `Permissions` and `Settings` tags as observed by that request's prologue,
  so browsers stay fresh with zero polling.
- `access/check` with `{ userId?: int, securables: [{ resource, action }] }` returns one
  `AccessDecision` per securable — outcome, the composed filter rendered for display, grants (source, role
  id and name, permission id, resource, action, filter, or the bespoke reason key, or `System`), and
  problems. `userId` omitted means the caller (in memory); naming another user requires `(core.User, Read)`
  on that user (filtered) and costs one round trip; filter texts of other users' grants are returned only
  to callers holding `(core.Role, Read)`.
- MCP tools (the web-API theme owns the surface): `whoami` returns the `me` payload compacted to name,
  kind, and resources with their actions and whether each is filtered; `check_access` wraps the check.

### D25 — Bootstrap: the system user, the Administrator role, the first administrator, local development

Seeded with `HasData` in the reserved band:

| Row | Id | Values |
|---|---|---|
| `core.Users` | 1 | `Kind = System`, `Subject = NULL`, `Email = NULL`, `State = Active`, `ActivatedAt = 2000-01-01`, `Name = 'System'`, `IsActive = 1`, `CreatedById = ModifiedById = 1` (a self-referencing row is valid in one insert) |
| `core.UserStamps` | 1 | `PreferencesTag = 00000000-0000-0000-0000-000000000001` |
| `core.Roles` | 1 | `Code = 'Administrator'`, `Name = 'Administrator'`, `IsPublic = 0`, `IsActive = 1`, audit → 1 |
| `core.Permissions` | 1 | `RoleId = 1`, `Resource = '*'`, `Action = '*'`, `Filter = NULL` |
| `core.RoleMemberships` | 1 | `UserId = 1`, `RoleId = 1` (the system user is an administrator by membership; the evaluator's `IsSystem` short-circuit is an optimisation, not a rule) |

Plus the tenant-tag rows `Permissions` and `Settings` with fixed initial GUIDs (the settings theme's table).
`WellKnownIds` carries the constants. Seed rows are immutable through the API (D8, D21). The system user
is the `CreatedById` of every seeded row, the identity of built-in schedules, and the bypass for the
guards; it can never authenticate (no subject, by constraint).

**First human administrator** is not `HasData` (its email is environment-specific): the migrator's tenant
bootstrap (`--bootstrap-admin <email> [--name <name>] [--language <bcp47>]`, the host theme's provisioning
seam calling `ITenantBootstrapper`) runs the user recipe through the pipeline as the system user, creating
the user (`State = New`) with a membership in role 1 in one transaction (guards bypassed), idempotent on
email. In deployed instances the provisioning flow then calls `UserService.Invite` (Core stack theme). In
**Development in-proc mode** the bootstrapper accepts `--bootstrap-admin-subject`; the reference
distribution's local setup passes `admin@localhost` with `00000000-0000-0000-0000-000000000001`, creating
the user directly in `State = Invited` with that subject and `InvitedAt = now`, `InviteStatus = Invited`
(the first sign-in flips it to `Active` through the ordinary prologue). The bootstrapper refuses a fixed
subject outside the Development environment: a deployed tenant obtains subjects only from the invite API.

Rejected: negative ids for seeds (legal, ugly in every list and URL); seeding a human administrator by
`HasData` (needs a subject the tenant does not have).

### D26 — Sensitive securables and step-up

The registry marks `IsSensitive = true` on `core.Role` `Save`/`Delete`/`Activate`, `core.User`
`Save`/`Delete`/`Activate`/`Invite`, and every `DeleteByQuery` endpoint. The host and web-API themes'
step-up middleware reads the flag from the endpoint's securable metadata and issues the
`401 insufficient_user_authentication` challenge of spec 0003 when the session's `acr`/`auth_time` are
below the distribution's configured bar. The set is Core's default; a distribution may add securables and
may not remove Core's (the composition validates). Member endpoints are never sensitive. Confidence medium.

### D27 — Limits

`AccessOptions`: `MaxPermissionsPerRole = 300`, `MaxRolesPerUser = 32` (validation on role and user
saves), `MaxFilterLength = 2048`, `MaxCachedUsers = 10_000`, `CacheSlidingExpiration = 4 h`,
`PermissionsMaxAge = 15 min`, `ActivityStampInterval = 60 s`, `MaxPreferenceKeys = 256`,
`MaxPreferenceValueBytes = 32_768`, `SecurityLockTimeout = 5 s`.

Queryex limits: `AccessLimits` (validating one stored filter) = `MaxInputLength 2048`, `MaxJoins 8`,
`MaxParameters 64`, `MaxTypedNodes 512`, others default. The pipeline's compile profile for
permission-bearing queries must admit the composed disjunction: `MaxParameters ≥ 1024`, `MaxJoins ≥ 64`.
A compile that still exceeds a ceiling raises `AccessTooComplexException` (500, logged with the
diagnostics and the role ids involved): an administrator's configuration problem, surfaced rather than
silently denied.

### D28 — Telemetry

Meter `Tellma.Core`; names as `const`s in `Tellma.Core.Abstractions.Access.AccessTelemetryNames`; no
per-tenant or per-user tags (ids go to structured log events).

| Instrument | Kind | Tags |
|---|---|---|
| `tellma.access.decisions` | counter | `outcome` ∈ `denied \| filtered \| unrestricted` |
| `tellma.access.cache.hits` | counter | — |
| `tellma.access.cache.misses` | counter | `reason` ∈ `absent \| tag \| age \| format` |
| `tellma.access.cache.entries` | observable gauge | — |
| `tellma.access.set.build.duration` | histogram (s) | — |
| `tellma.access.set.grants` | histogram | — (effective grants per built set) |
| `tellma.access.permissions.unresolved` | counter | `problem` ∈ the codes of D23 |
| `tellma.access.guards.triggered` | counter | `guard` ∈ `last_administrator \| public_role_members \| administrator_damaged \| lock_timeout \| self_deactivation \| self_deletion \| escalation` |
| `tellma.access.witness.missing` | counter | — |
| `tellma.access.connect.results` | counter | `result` ∈ `warm \| cold \| stale_permissions \| stale_settings \| stale_profile \| unknown_subject \| deactivated \| guard_thrash` |
| `tellma.access.connect.prologue.duration` | histogram (s) | `variant` ∈ `subject \| user \| system` |

Log events (`AccessEvents`): `MissingWitness` (Error), `PermissionDrift` (Warning), `GuardThrash`
(Warning), `EscalationRejected` (Information), `GuardTriggered` (Warning), `ConnectRejected`
(Information), `AccessSetBuilt` (Debug). Alert queries under `infra/monitoring/` for drift > 0 and a cache
hit ratio below 90 % over 15 minutes.

### D29 — Testing

- `test/core/Tellma.Core.Tests` (unit, every PR): `Decide` over hand-built sets (absorption, union, implied
  read, wildcard/filter interaction, inactive and public roles, system user, bespoke-only access, drift
  exclusion, leaf deduplication and ordering, empty set → `Denied`); validators including both escalation
  rules; resource-key grammar; registry freezing, duplicates, aliases, `Fingerprint` stability; the
  startup audit over an in-memory `EndpointDataSource` (unmarked endpoint, `AllowAnonymous` in the group,
  unknown securable); the prologue reader against scripted result sets (every presence combination of sets
  2–3 and the body); `FormatVersion` mismatch → miss; `Via` composition shape.
- `test/core/Tellma.Core.IntegrationTests` (`Category=Integration`, LocalDB or Testcontainers, every PR):
  the schema of §4 applied by migrations; the prologue end to end (cold, warm, stale permissions, stale
  settings, stale profile, unknown subject, deactivated, activation flip exactly once under two concurrent
  first requests); the throttled stamp writes once per minute under 1,000 requests and `UsersHistory`
  gains zero rows from traffic; tag bumps for every declared write path and not for `core.Users`-only
  writes on the `Permissions` tag; the companion `UserStamps` insert; L1–L3 under two concurrent
  administrator removals (exactly one succeeds; the lock serialises); guard violations roll the whole
  transaction back and the system context bypasses; a role save committed between two batches of one
  request forces exactly one recompose; the write-set audit; the witness filter replacing a 2xx with 500;
  temporal history rows per save (unchanged children write none).
- Nightly: the migrator's drift report against a database seeded with a filter over a column the next
  migration renames.
- No `Live=true` suite: the identity invite and delivery-status client is the Core stack theme's, exercised
  there with a capturing sender; this theme's tests stub it.

---

## 3. Contracts

Contract blocks use the platform's contract notation: names are normative; shape is described, not
transcribed. Everything below lives in `Tellma.Core.Abstractions.Access` unless stated. Base shapes and
annotations marked *(data access)* are requested from the entity-contract seam and shown in the shape this
theme needs.

### 3.1 Enums, well-known ids, tag names

```contract
enum UserKind = Human | System            // stored as varchar(16); Service reserved
enum UserState = New | Invited | Active   // stored as varchar(16)
enum InviteStatus = Invited | Reinvited | Active

record WellKnownIds                       // constants
  SystemUserId: int = 1
  AdministratorRoleId: int = 1
  AdministratorPermissionId: int = 1
  SystemAdministratorMembershipId: int = 1
  ReservedIdBandEnd: int = 999

record TenantTags                         // constants
  Permissions: string = "Permissions"

record UserTags                           // constants
  Preferences: string = "Preferences"

record AccessActions                      // constants
  Read: string = "Read"
  Save: string = "Save"
  Delete: string = "Delete"
  Activate: string = "Activate"
  Invite: string = "Invite"
  Wildcard: string = "*"

record CoreResources                      // constants
  User: string = "core.User"
  Role: string = "core.Role"
  Wildcard: string = "*"
```

### 3.2 Annotations (data access owns the mechanism; this theme declares the use)

```contract
annotation [ServerOwned]                                     on property   // never taken from a payload
annotation [Temporal]                                        on type       // system-versioning capability
annotation [BumpsTenantTag(tag: string)]                     on type, inherited, repeatable
annotation [BumpsUserTag(tag: string, userIdProperty: string)] on type, inherited, repeatable
annotation [NaturalKey]                                      on property
annotation [Searchable]                                      on property
annotation [Securable(action: string, resource: string?)]    on method     // service methods; resource defaults to the service's entity
annotation [MemberEndpoint]                                  on method     // any connected active member
```

### 3.3 Securables

```contract
record SecurableOwner(Resource: string, Navigation: string)

record SecurableDescriptor(
  Resource: string,
  Action: string,
  FilterRoot: string?,          // logical entity name; null = filters unsupported
  IsSensitive: bool,
  Feature: string,
  Owner: SecurableOwner?)       // reserved: weak entities as query roots
  SupportsFilter: bool          // derived: FilterRoot is not null

service SecurableRegistryBuilder
  Add(resource: string, action: string, filterRoot: string?, isSensitive: bool = false, owner: SecurableOwner?) -> SecurableRegistryBuilder   sync
  Alias(oldResource: string, newResource: string) -> SecurableRegistryBuilder   sync
  MarkSensitive(resource: string, action: string) -> SecurableRegistryBuilder   sync

contract ISecurableContributor
  Contribute(builder: SecurableRegistryBuilder)   sync

service ISecurableRegistry
  All: list<SecurableDescriptor>                  // ordered by resource, action
  Resources: list<string>
  Find(resource: string, action: string) -> SecurableDescriptor?   sync   // aliases resolved, case-insensitive
  ForResource(resource: string) -> list<SecurableDescriptor>       sync
  Fingerprint: string                             // hex SHA-256; changes only on deploy

record SecurableEndpointMetadata(Resource: string, Action: string)   // endpoint metadata
record MemberEndpointMetadata                                         // endpoint metadata
record NoActivityStampMetadata                                        // endpoint metadata: polling
```

| Member | Meaning |
|---|---|
| `Add` | Declares a securable. The same tuple with the same filter root is idempotent; a different filter root is a composition error. |
| `Alias` | Stored permissions naming `oldResource` resolve to `newResource` for one release. |
| `MarkSensitive` | Adds to the step-up set; Core's defaults cannot be removed. |

### 3.4 Evaluation

```contract
enum AccessOutcome = Denied | Filtered | Unrestricted
enum AccessGrantSource = Role | PublicRole | Bespoke | System
enum AccessProblemCode = UnknownResource | UnknownAction | FilterUnsupported | FilterInvalid | VersionUnsupported

record AccessGrant(
  Source: AccessGrantSource,
  RoleId: int?, RoleName: string?, PermissionId: int?,
  Resource: string, Action: string,       // as stored; wildcards preserved
  Filter: string?,                        // null = unrestricted
  Reason: string?)                        // bespoke reason key, e.g. Access.AssignedToMe

record AccessProblem(PermissionId: int, RoleId: int, Code: AccessProblemCode, Diagnostics: list<QueryexDiagnostic>)

record AccessDecision(
  Resource: string, Action: string,
  Outcome: AccessOutcome,
  Filter: FilterTree?,                    // non-null exactly when Filtered; conjoin, never render and concatenate
  Grants: list<AccessGrant>,
  Problems: list<AccessProblem>)
  IsAllowed: bool                         // derived: Outcome != Denied

record AccessCriterion(Action: string, Filter: string, Reason: string)   // bespoke; current language version

contract IAccessCriteriaProvider
  Resource: string
  GetCriteria(userId: int) -> list<AccessCriterion>

record UserAccess
  FormatVersion: int = 1                  // constant
  UserId: int
  Tag: Guid                               // the tenant Permissions tag the set was built under
  ComputedAt: DateTime
  IsSystem: bool
  Grants: list<AccessGrant>               // resolved rows only
  Problems: list<AccessProblem>
  Decide(securable: SecurableDescriptor, bespoke: list<AccessCriterion>, queryRoot: string?) -> AccessDecision   sync
  System: UserAccess                      // static: unrestricted, never loaded

service IAccessEvaluator
  Evaluate(resource: string, action: string) -> AccessDecision           // ambient caller; records a witness
  Require(resource: string, action: string) -> AccessDecision            // throws ForbiddenException when Denied
  Evaluate(securables: list<(string, string)>) -> list<AccessDecision>   // several at once, one set
  EvaluateFor(userId: int, resource: string, action: string) -> AccessDecision   // "can they, and why"; no witness
  GetAccess() -> UserAccess
  Invalidate(userId: int)   sync

data AccessOptions
  MaxPermissionsPerRole: int = 300
  MaxRolesPerUser: int = 32
  MaxFilterLength: int = 2048
  MaxCachedUsers: int = 10000
  CacheSlidingExpiration: TimeSpan = 4h
  PermissionsMaxAge: TimeSpan = 15min
  ActivityStampInterval: TimeSpan = 60s
  MaxPreferenceKeys: int = 256
  MaxPreferenceValueBytes: int = 32768
  SecurityLockTimeout: TimeSpan = 5s
```

### 3.5 Connect

```contract
record UserProfile(Name: string, Name2: string?, Name3: string?, Email: string?, ImageId: string?,
                   PreferredLanguage: string?, PreferredCalendar: string?, PreferredTimeZone: string?)

record ConnectedUser(                     // the cached entry; immutable, replaced on refresh
  UserId: int, Kind: UserKind, State: UserState,
  Profile: UserProfile, PreferencesTag: Guid,
  Access: UserAccess,                     // carries the Permissions tag
  TenantTags: map<string, Guid>,
  InboxUnreadCount: int, InboxUnseenCount: int,
  LastActivityStampedAt: DateTime?)       // the per-instance throttle memo

record ConnectPremises(                   // what the prologue binds
  Subject: string?, UserId: int?,         // by-subject, by-user, or neither (system)
  ExpectedUserId: int?, ExpectedPermissionsTag: Guid?, ExpectedPreferencesTag: Guid?,
  ExpectedSettingsTag: Guid, StampActivity: bool, ReloadPermissions: bool)
  Cold(subject: string, expectedSettingsTag: Guid, stampActivity: bool) -> ConnectPremises   sync, static
  For(user: ConnectedUser, subject: string, stampActivity: bool) -> ConnectPremises           sync, static

record PermissionRow(PermissionId: int, RoleId: int, RoleName: string, IsPublic: bool,
                     Resource: string, Action: string, Filter: string?, FilterLanguageVersion: int?)

record ConnectResult(                     // what the prologue returned
  UserId: int?, Kind: UserKind?, IsActive: bool, State: UserState?,
  PreferencesTag: Guid?, TenantTags: map<string, Guid>,
  InboxUnreadCount: int, InboxUnseenCount: int,
  PermissionsStale: bool, ProfileStale: bool, GuardPassed: bool,
  PermissionRows: list<PermissionRow>?, Profile: UserProfile?)

service IUserConnector
  Connect() -> ConnectedUser                              // cache, or a cold prologue-only round trip
  ConnectAsUser(userId: int) -> ConnectedUser             // background scope as a user
  ConnectAsSystem() -> ConnectedUser                      // background or migrator scope
  Contribute(batch: IBatchBuilder, premises: ConnectPremises) -> BatchReader<ConnectResult>   sync
  Apply(result: ConnectResult) -> ConnectedUser           sync   // refreshes cache and caller context

service IGuardedBatchRunner
  Run<TResult>(compose: (ConnectedUser) -> IBatchBuilder, read: (ConnectedUser, BatchResult) -> TResult) -> TResult

contract ICallerContext                   // host theme owns; this theme's needs
  TenantId: int
  Subject: string?
  UserId: int?
  UserKind: UserKind?
  IsInteractive: bool
  Witnessed: set<(string, string)>        // securables evaluated in this scope
```

| Member | Meaning |
|---|---|
| `Connect` | Throws `NotMemberException` or `UserDeactivatedException`. |
| `Contribute` | Prepends the prologue; the reader yields the `ConnectResult` after execution and tells the batch reader which later result sets exist. |
| `Run` | Connects, composes, executes, reads; on `GuardPassed = false` applies the result and recomposes once; a second failure throws `ConnectGuardException`. |

### 3.6 Guards, validators, bootstrap, exceptions

```contract
service IAccessGuards
  Contribute(batch: IBatchBuilder)   sync   // app lock first, L1–L3 after the writes, inside the transaction

service UserAccessRules<TUser> where TUser: User       // IEntityValidator<TUser> from the pipeline seam
service RoleAccessRules<TRole> where TRole: Role

record TenantBootstrapRequest(Email: string, Name: string, PreferredLanguage: string?, Subject: string?)

contract ITenantBootstrapper
  BootstrapAdministrator(request: TenantBootstrapRequest) -> int   // idempotent on email; Subject only in Development

// Platform exceptions (types owned by the service-pipeline theme; mapping by the web-API theme)
record NotMemberException          // 403 Access.NotMember
record UserDeactivatedException    // 403 Access.UserDeactivated
record ForbiddenException(Code: string)   // 403 Access.Denied
record ConnectGuardException       // 409 Access.GuardThrash
record AccessInvariantException(Code: string)   // 422; from THROW 51000–51999
record AccessTooComplexException   // 500
```

Validation codes: `Users.EmailLockedAfterInvite`, `Users.EmailTaken`, `Users.SubjectTaken`,
`Users.AlreadyActive`, `Users.CannotDeactivateSelf`, `Users.CannotDeleteSelf`,
`Users.CannotRemoveOwnAdministratorMembership`, `Users.SystemUserImmutable`, `Users.OnlyNewUsersDeletable`,
`Users.MembershipEscalation`, `Users.TooManyRoles`, `Roles.PublicRoleHasMembers`,
`Roles.AdministratorImmutable`, `Roles.HasMembers`, `Roles.TooManyPermissions`,
`Permissions.UnknownSecurable`, `Permissions.FilterNotSupported`, `Permissions.FilterInvalid`,
`Permissions.FilterTooLong`, `Permissions.WildcardResourceOnPublicRole`, `Permissions.EscalationBeyondSelf`.
Guard codes: `Access.LastAdministrator` (51001), `Access.PublicRoleHasMembers` (51002),
`Access.AdministratorRoleDamaged` (51003), `Access.LockTimeout` (51004). The host localises every code.

### 3.7 Telemetry names

```contract
record AccessTelemetryNames             // constants; meter "Tellma.Core"
  Decisions = "tellma.access.decisions"
  CacheHits = "tellma.access.cache.hits"
  CacheMisses = "tellma.access.cache.misses"
  CacheEntries = "tellma.access.cache.entries"
  SetBuildDuration = "tellma.access.set.build.duration"
  SetGrants = "tellma.access.set.grants"
  PermissionsUnresolved = "tellma.access.permissions.unresolved"
  GuardsTriggered = "tellma.access.guards.triggered"
  WitnessMissing = "tellma.access.witness.missing"
  ConnectResults = "tellma.access.connect.results"
  ConnectPrologueDuration = "tellma.access.connect.prologue.duration"
  OutcomeTag = "outcome"; ReasonTag = "reason"; ProblemTag = "problem"; GuardTag = "guard"; ResultTag = "result"; VariantTag = "variant"
```

### 3.8 Wire shapes owned here (the web-API theme projects them)

```jsonc
// me
{
  "user": { "id": 42, "kind": "Human", "state": "Active", "name": "…", "name2": null, "name3": null,
            "email": "…", "imageId": null, "preferredLanguage": "ar", "preferredCalendar": "UmAlQura",
            "preferredTimeZone": "Asia/Riyadh" },
  "preferencesTag": "…",
  "access": { "tag": "…", "formatVersion": 1, "isSystem": false,
              "securables": [ { "resource": "gl.Center", "action": "Save", "hasFilter": true } ],
              "problems": [ { "permissionId": 91, "roleId": 7, "code": "FilterInvalid" } ] },
  "tags": { "Settings": "…", "Permissions": "…" },
  "securablesFingerprint": "…",
  "inbox": { "unread": 3, "unseen": 1 }
}

// access/check
{ "userId": null, "securables": [ { "resource": "gl.Center", "action": "Save" } ] }
// →
[ { "resource": "gl.Center", "action": "Save", "outcome": "Filtered", "filter": "(IsActive = true)",
    "grants": [ { "source": "Role", "roleId": 7, "roleName": "Finance", "permissionId": 91,
                  "resource": "gl.Center", "action": "*", "filter": "IsActive = true", "reason": null } ],
    "problems": [] } ]
```

---

## 4. Schema

All tables in schema `core`; timestamps `datetime2(7)` UTC unless stated; every FK named explicitly;
every temporal table has history table `core.<Table>History` with the default clustered index on the
period and no nonclustered index; sequences `sq_Users`, `sq_NotificationPreferences`, `sq_Roles`,
`sq_RoleMemberships`, `sq_Permissions`, each `START WITH 1000`. Period columns are EF shadow properties,
excluded from every UDTT.

### `core.Users` — carries `AuditedEntity`, `ActivatableEntity`, `MultilingualName`; temporal; UDTT `UsersList`

| Column | Type | Null | Constraints | Notes |
|---|---|---|---|---|
| `Id` | `int` | no | PK clustered, sequence `sq_Users` | 1 = system user |
| `Kind` | `varchar(16)` | no | `CK_Users_Kind IN ('Human','System')`, default `'Human'` | server-owned |
| `Subject` | `varchar(255)` | yes | `COLLATE Latin1_General_100_BIN2`; `UX_Users_Subject` unique filtered `WHERE Subject IS NOT NULL` `INCLUDE (Kind, IsActive, State)` | server-owned; the prologue's covering seek |
| `Email` | `nvarchar(255)` | yes | `UX_Users_Email` unique filtered `WHERE Email IS NOT NULL` | natural key; editable while `New` |
| `State` | `varchar(16)` | no | `CK_Users_State IN ('New','Invited','Active')`, default `'New'` | server-owned |
| `InvitedAt` | `datetime2(7)` | yes | | server-owned |
| `InviteStatus` | `varchar(16)` | yes | `CK_Users_InviteStatus IN ('Invited','Reinvited','Active')` | server-owned |
| `LastInviteError` | `nvarchar(1024)` | yes | | server-owned; verbatim |
| `ActivatedAt` | `datetime2(7)` | yes | | server-owned |
| `Name` | `nvarchar(255)` | no | | searchable |
| `Name2` | `nvarchar(255)` | yes | | gated by tenant languages |
| `Name3` | `nvarchar(255)` | yes | | gated by tenant languages |
| `ImageId` | `varchar(64)` | yes | | server-owned; type per the blob theme |
| `PreferredLanguage` | `varchar(35)` | yes | | BCP 47; null = tenant primary |
| `PreferredCalendar` | `varchar(16)` | yes | | null = tenant primary |
| `PreferredTimeZone` | `varchar(64)` | yes | | IANA; null = tenant zone |
| `ContactEmail` | `nvarchar(255)` | yes | | null = `Email` |
| `ContactMobile` | `varchar(32)` | yes | | E.164 |
| `IsActive` | `bit` | no | default 1 | server-owned; `Activate` action only |
| `CreatedAt` | `datetime2(7)` | no | | |
| `CreatedById` | `int` | no | `FK_Users_CreatedById → core.Users(Id)` | |
| `ModifiedAt` | `datetime2(7)` | no | | concurrency stamp |
| `ModifiedById` | `int` | no | `FK_Users_ModifiedById → core.Users(Id)` | |
| `ValidFrom`, `ValidTo` | `datetime2(7)` | no | period (shadow) | not in the UDTT |

Checks: `CK_Users_HumanHasEmail (Kind <> 'Human' OR Email IS NOT NULL)`;
`CK_Users_SystemHasNoSubject (Kind <> 'System' OR (Subject IS NULL AND Email IS NULL))`;
`CK_Users_StateSubject (Kind <> 'Human' OR ((State = 'New') = (Subject IS NULL)))`;
`CK_Users_StateInvitedAt (Kind <> 'Human' OR ((State = 'New') = (InvitedAt IS NULL)))`;
`CK_Users_StateActivatedAt ((State = 'Active') = (ActivatedAt IS NOT NULL))`.
Index `IX_Users_IsActive_Name (IsActive, Name)` for the default list view.
Carries the child collections `RoleMemberships: list<RoleMembership>` and
`NotificationPreferences: list<NotificationPreference>`, not mapped. Annotations: `[Temporal]`,
`[BumpsUserTag(Preferences, "Id")]`; `Email` `[NaturalKey]`; `Name`, `Name2`, `Name3`, `Email` `[Searchable]`.

### `core.UserStamps` — not versioned; no UDTT

| Column | Type | Null | Constraints | Notes |
|---|---|---|---|---|
| `UserId` | `int` | no | PK clustered; `FK_UserStamps_UserId → core.Users(Id) ON DELETE CASCADE` | |
| `LastActiveAt` | `datetime2(0)` | yes | | throttled stamp |
| `PreferencesTag` | `uniqueidentifier` | no | | user-level tag |
| `InboxUnreadCount` | `int` | no | default 0 | reserved for the background-tasks theme |
| `InboxUnseenCount` | `int` | no | default 0 | reserved for the background-tasks theme |

`HasData`: `(1, NULL, 00000000-0000-0000-0000-000000000001, 0, 0)`.

### `core.UserPreferences` — not versioned; not an entity; standalone table type `UserPreferenceList (Key, Value)`

| Column | Type | Null | Constraints | Notes |
|---|---|---|---|---|
| `UserId` | `int` | no | PK clustered `(UserId, Key)`; `FK_UserPreferences_UserId → core.Users(Id) ON DELETE CASCADE` | |
| `Key` | `varchar(128)` | no | | dotted ASCII segments |
| `Value` | `nvarchar(max)` | no | | opaque; ≤ 32 KB |

Write statement (self-service; `@tm_UserId` is the connected user, `@tm_Items` the `UserPreferenceList` TVP):

```sql
DELETE P FROM [core].[UserPreferences] AS P
WHERE P.[UserId] = @tm_UserId AND NOT EXISTS (SELECT 1 FROM @tm_Items AS I WHERE I.[Key] = P.[Key]);
UPDATE P SET [Value] = I.[Value] FROM [core].[UserPreferences] AS P JOIN @tm_Items AS I ON I.[Key] = P.[Key]
WHERE P.[UserId] = @tm_UserId AND P.[Value] <> I.[Value];
INSERT [core].[UserPreferences] ([UserId], [Key], [Value])
SELECT @tm_UserId, I.[Key], I.[Value] FROM @tm_Items AS I
WHERE NOT EXISTS (SELECT 1 FROM [core].[UserPreferences] AS P WHERE P.[UserId] = @tm_UserId AND P.[Key] = I.[Key]);
UPDATE [core].[UserStamps] SET [PreferencesTag] = NEWID() WHERE [UserId] = @tm_UserId;
```

(A single-key variant takes `@tm_Key`, `@tm_Value` and skips the delete.)

### `core.NotificationPreferences` — weak child of `User`; not versioned; UDTT `NotificationPreferencesList`

| Column | Type | Null | Constraints | Notes |
|---|---|---|---|---|
| `Id` | `int` | no | PK, sequence `sq_NotificationPreferences` | |
| `UserId` | `int` | no | `FK_NotificationPreferences_UserId → core.Users(Id)` | parent key |
| `NotificationType` | `varchar(64)` | no | `UX_NotificationPreferences_UserId_Type` unique `(UserId, NotificationType)` | registry key from the background-tasks theme |
| `Email` | `bit` | no | | |
| `Sms` | `bit` | no | | |
| `Push` | `bit` | no | | |

Annotation: `[BumpsUserTag(Preferences, "UserId")]`.

### `core.Roles` — carries `AuditedEntity`, `ActivatableEntity`, `MultilingualName`; temporal; UDTT `RolesList`

| Column | Type | Null | Constraints | Notes |
|---|---|---|---|---|
| `Id` | `int` | no | PK clustered, sequence `sq_Roles` | 1 = Administrator |
| `Name` | `nvarchar(255)` | no | `UX_Roles_Name` unique | searchable |
| `Name2` | `nvarchar(255)` | yes | `UX_Roles_Name2` unique filtered | |
| `Name3` | `nvarchar(255)` | yes | `UX_Roles_Name3` unique filtered | |
| `Code` | `nvarchar(50)` | yes | `UX_Roles_Code` unique filtered | natural key |
| `IsPublic` | `bit` | no | default 0 | |
| `IsActive` | `bit` | no | default 1 | server-owned; `Activate` action only |
| `CreatedAt`, `CreatedById`, `ModifiedAt`, `ModifiedById` | | no | `FK_Roles_CreatedById`, `FK_Roles_ModifiedById → core.Users(Id)` | |
| `ValidFrom`, `ValidTo` | `datetime2(7)` | no | period (shadow) | |

Index `IX_Roles_IsPublic` filtered `WHERE IsPublic = 1` (the public-roles probe). Carries the child collection
`Permissions: list<Permission>`, not mapped. Annotations: `[Temporal]`, `[BumpsTenantTag(Permissions)]`;
`Code` `[NaturalKey]`.

### `core.RoleMemberships` — weak child of `User`; temporal; UDTT `RoleMembershipsList`

| Column | Type | Null | Constraints | Notes |
|---|---|---|---|---|
| `Id` | `int` | no | PK clustered, sequence `sq_RoleMemberships` | |
| `UserId` | `int` | no | `FK_RoleMemberships_UserId → core.Users(Id)` (NO ACTION) | parent key |
| `RoleId` | `int` | no | `FK_RoleMemberships_RoleId → core.Roles(Id)` (NO ACTION) | |
| `Notes` | `nvarchar(1024)` | yes | | |
| `ValidFrom`, `ValidTo` | `datetime2(7)` | no | period (shadow) | |

Indexes: `UX_RoleMemberships_UserId_RoleId` unique `(UserId, RoleId)`; `IX_RoleMemberships_RoleId_UserId (RoleId, UserId)`.
Annotations: `[Temporal]`, `[BumpsTenantTag(Permissions)]`.

### `core.Permissions` — weak child of `Role`; temporal; UDTT `PermissionsList`

| Column | Type | Null | Constraints | Notes |
|---|---|---|---|---|
| `Id` | `int` | no | PK clustered, sequence `sq_Permissions` | |
| `RoleId` | `int` | no | `FK_Permissions_RoleId → core.Roles(Id)` (NO ACTION) | parent key |
| `Resource` | `varchar(128)` | no | | resource key or `*` |
| `Action` | `varchar(32)` | no | | action or `*` |
| `Filter` | `nvarchar(2048)` | yes | `CK_Permissions_WildcardHasNoFilter (Resource <> '*' OR Filter IS NULL)` | Queryex predicate |
| `FilterLanguageVersion` | `int` | yes | `CK_Permissions_FilterVersion ((Filter IS NULL) = (FilterLanguageVersion IS NULL))` | server-owned |
| `Notes` | `nvarchar(1024)` | yes | | |
| `ValidFrom`, `ValidTo` | `datetime2(7)` | no | period (shadow) | |

Index `IX_Permissions_RoleId (RoleId) INCLUDE (Resource, Action, Filter, FilterLanguageVersion)`.
Annotations: `[Temporal]`, `[BumpsTenantTag(Permissions)]`.

### Read by this theme, owned by the settings theme

`core.TenantTags (Name nvarchar(64) PK, Tag uniqueidentifier NOT NULL)` with rows `Permissions` and
`Settings` seeded by `HasData` with fixed initial GUIDs. The settings theme may choose another table name
or a single wide row; the prologue and the bump statement change accordingly (§10).

### Reserved for the background-tasks theme

`core.UserPushSubscriptions (Id, UserId FK ON DELETE CASCADE, Endpoint, P256dh, Auth, UserAgent, CreatedAt)`
— columns fixed there; this theme reserves the name and the cascade.

### Seeds

`HasData` (reserved band): the five rows of D25 and the two tag rows. Runtime seeds and the bootstrap run
through the pipeline as the system user.

### Engine-facing names

The Queryex schema adapter exposes `User` (`[core].[Users]`), `Role`, `RoleMembership`, `Permission`,
`NotificationPreference`, and `UserStamp` (read-only, navigation `User`, so reports can show
`LastActiveAt`), with navigations `CreatedBy`/`ModifiedBy → User`, `RoleMembership.User`/`.Role`,
`Permission.Role`, `NotificationPreference.User`. `Kind`, `State`, `InviteStatus` are string-typed enum
properties. `UserPreferences` is not exposed.

---

## 5. Answers

| Brain-dump question (abridged) | Answer |
|---|---|
| "`State` — we need to track the invitation state" | Stored `New \| Invited \| Active` on tenant-owned facts, CHECK-constrained against `Subject`/`InvitedAt`/`ActivatedAt`; delivery detail is a live drill-down (D3, D4). |
| "How do we store, retrieve, modify the profile picture? `ImageFitJson` here or central?" | The blob theme's; the row carries `ImageId` only, server-owned (D3). |
| "Is JSON right for user preferences? Pinned screens in a distinct table so an admin can customise?" | A `(UserId, Key, Value)` bag written only by self-service; pinned screens are `nav.pinned`; an admin default is a tenant setting; no third table (D6). |
| "What to store for notification settings? One JSON field?" | `ContactEmail`/`ContactMobile` on the row; `core.NotificationPreferences (UserId, NotificationType, Email, Sms, Push)`; push subscriptions in the inbox's table (D7). |
| "Separate SettingsVersion and PermissionsVersion or one?" | Separate and differently scoped: `PreferencesTag` per user; `Permissions` tenant-level (D5, D17). |
| "Version, ETag, or fingerprint?" | Tag / stamp / fingerprint / format version (D1). |
| "Shape of inbox tracking?" | Two materialised counters on `core.UserStamps`, reserved for the background-tasks theme (D5). |
| "Are the user states exhaustive?" | Yes for what the tenant can know; the rest is identity-server state read live (D4). |
| "Write-once columns: two UDTTs or a service rule?" | One UDTT; `[ServerOwned]` overwrites from the pre-image; `Subject` is server-owned (Invite action only); `Email` editable while `New` (D3). |
| "Global permissions: system role, `IsPublic`, or separate table?" | `IsPublic` on `Role` with the no-members and no-wildcard invariants (D8, D21). |
| "Do we need `SavedById` on weak entities?" | No; the root is stamped when a child changes and equal `ValidFrom` recovers the actor (D2). |
| "Best convention to encode the Resource a permission secures?" | `<schema>.<LogicalEntityName>` (`core.User`), dotted grammar for non-entity resources, `*` wildcard (D11). |
| "How to make registering securables and enforcing access hard to forget?" | Securables derive from capabilities; fallback policy; group policy; endpoint metadata with a startup audit and a coarse check; the runtime witness; filters only through `AccessDecision` (D12, D13). |
| "Is 'Securable' the right word?" | Yes (D12). |
| "Permissions invalidated by a schema change: shim or block?" | Neither: the row drifts (grants nothing), is diagnosed everywhere, never blocks the user; a rename ships an alias and a recipe (D23). |
| "If permissions are cached, can DB calls #1 and #2 collapse?" | Yes, and further: connect folds into every batch as a guarded prologue; read = 1, save = 2 round trips; the stale path recomposes once within the same call (D18, D19). |
| "How do we guarantee the cache version is invalidated when the data changes?" | Declared on the entity class, bumped by the emitter inside the transaction before the writes; raw SQL must declare its write set; a test-tier write-set audit (D20). |
| "Is 'version' / 'metaversion' accurate?" | Tag; format version (D1). |
| "What is a good alternative to RowVersion?" | Server-stamped `ModifiedAt`, checked from a TVP inside the persist transaction; bookkeeping never moves it (D2). |
| "A user cannot delete or deactivate their own user, nor strip their own admin permissions" | Self-deactivate, self-delete, and removing one's own Administrator membership are refused; the last-administrator invariant is checked in SQL under an application lock; narrowing one's own other permissions is allowed (D21). |
| "Endpoint: does X have permission Y on Z, subject to what filter, and why?" | `EvaluateFor` and the `access/check` endpoint; `me` for the caller (D24). |
| "Accessing a record I cannot read returns the same as non-existent" | Reads: 404; writes on rows readable but not writable: 403 (D14). |
| "Weak entities' permissions are the parent's with paths adjusted" | `FilterTree.Via` engine node, reserved (D16). |
| "Services should rely on interfaces so distros can replace entities" | Core rules are generic over `TUser : User` / `TRole : Role` base classes; no paired interface; replacement is not supported (D1). |
| "Should background tasks run under a system user with full permissions?" | The seeded system user (id 1), an administrator by membership, never authenticates; policy is the background-tasks theme's (D25). |
| "How can custom validators participate in the batch context load?" (this theme's part) | The Core rules declare their context (the roles being saved and their permissions, the members of public roles) through the pipeline's validator API and contribute guards through `IAccessGuards` (D21). |

---

## 6. Seams

1. **Batch abstraction (data access owns).** Needed: `IBatchBuilder.Prologue(sql, parameters)` placing
   the connect text first; body statements wrapped as guarded (`IF @tm_Guard = 1 BEGIN … END`);
   `AddRaw(sql, parameters, writes: WriteSet, mayRetry)` with `WriteSet` **required**; automatic tag bumps
   derived from `WriteSet` and from the emitter's own targets via `[BumpsTenantTag]`/`[BumpsUserTag]`,
   emitted inside the transaction before the writes; the affected-root-ids TVP under `@tm_<Root>Ids`;
   `BatchReader<T>` able to consume several result sets and to tell the reader how many follow; the
   reserved prefix `@tm_`; the persist body's `SET XACT_ABORT ON; BEGIN TRAN … COMMIT` inside the guarded
   block, never around the prologue; mapping of `THROW` 51000–51999 to `AccessInvariantException(code)`
   with the message as the code; whole-batch retry on deadlock (1205) even for non-idempotent batches
   (rollback is guaranteed); the prologue is always retry-safe; server-side `ModifiedAt = SYSUTCDATETIME()`;
   the "root stamped when any child changed" rule; skip-unchanged `UPDATE`s on temporal tables.
2. **Entity class vs wire shape (data access owns).** `[NotMapped]` child lists on `User`
   (`RoleMemberships`, `NotificationPreferences`) and `Role` (`Permissions`); `[ServerOwned]` as the
   editable/server-owned split (the emitter never reads those columns from the payload); `ModifiedAt`
   echoed as the concurrency stamp; enum-as-string as `varchar(16)`; period columns excluded from UDTTs.
3. **One capability, declared once (service pipeline owns).** The activatable recipe contributes
   `(Entity, Activate, FilterRoot = Entity)` and marks `IsActive` server-owned on save; every stack
   contributes `Read`, and `Save`/`Delete` when editable/deletable, through one `ISecurableContributor`
   per stack; a custom action is `[Securable("Invite")]` on the service method and nothing else. The
   pipeline calls `Require` once per operation, conjoins the filter into every read, runs the two-stage
   pre-check (Read → 404, then action filter → 403) in the context batch and the post-check in the persist
   transaction, and executes every batch through `IGuardedBatchRunner`.
4. **Queryex schema per tenant configuration (data access owns; settings consumes).** The schema is a
   function of the tenant `Settings` tag; every batch guards on that tag; permission filters are validated
   against the schema at set build; a rebuilt schema (language change) is followed by a `Permissions` bump
   issued by the settings save (the settings theme declares `[BumpsTenantTag(Permissions)]` on the
   languages-bearing row); the list restriction through a TVP for the pre/post count queries;
   `FilterTree.Via` reserved.
5. **Version tags (settings owns).** Tenant tags in one small table with rows `Permissions` and
   `Settings`; user tag `core.UserStamps.PreferencesTag`; `uniqueidentifier` regenerated with `NEWID()` by
   the bump statement; the prologue reads all tenant tags as result set 1; a `const int FormatVersion` per
   cached shape.
6. **Feature composition (host owns).** `ISecurableContributor` and `IAccessCriteriaProvider` are
   contributed at realize; the securable audit and the endpoint audit report through the aggregated
   startup diagnostic.
7. **Natural keys (data access owns).** `User.Email` and `Role.Code` (`Name` fallback) declared by `[NaturalKey]`.
8. **Background-task columns (background tasks owns).** Nothing emitted here; jobs connect through
   `ConnectAsUser`/`ConnectAsSystem`; the inbox counters live on `core.UserStamps`; the notification-type
   registry with "cannot mute" flags; the push-subscription table; the hub close call.
9. **Request context (host owns).** `ICallerContext` (§3.5) in the scoped holder, populated by the tenant
   filter (subject in) and the connect step (user out), copied into job scopes — never `AsyncLocal` as the
   source of truth; `ISandboxContext` reads the same holder. Session revocation on deactivation: hub
   connections for the tenant are closed; the cookie is the host's decision.
10. **Platform exceptions (service pipeline owns the types; web API the mapping).** `NotMemberException`
    → 403, `UserDeactivatedException` → 403, `ForbiddenException` → 403 (404 for filtered by-id reads),
    `ConnectGuardException` → 409, `AccessInvariantException` → 422, `AccessTooComplexException` → 500.
11. **Permission evaluation API (this theme owns).** §3.4 verbatim.
12. **Blob staging tokens (blobs owns).** `User.ImageId` is server-owned and written by the image
    endpoint; nothing else.
13. **Wire shapes (web API owns).** §3.8; the `Tellma-Tags` header; the verb for `me`.
14. **Telemetry (data access owns the DB-call budget).** §3.7; the prologue counts as part of its batch's
    one round trip.
15. **Notification enqueue riding the save batch (background tasks owns).** Recipients are resolved against
    `core.NotificationPreferences` in SQL inside the batch; the counter increments on `core.UserStamps` are
    part of that statement.
16. **Connect-call collapse (this theme with the service pipeline).** D18–D19.
17. **Vocabulary.** D1: plural tables, `core` schema, `int` ids, four audit columns everywhere,
    `datetime2(7)` UTC, `Notes`, reserved band 1–999, `Activate` as one action, `*` wildcard, tag / stamp /
    fingerprint / format version, `UserPreferences` not `UserSettings`, `UserStamps` for the sibling.

---

## 7. Departures

- **Concrete default entity classes in `Tellma.Core.Abstractions`.** The architecture lists "base/abstract
  entity classes" for Abstractions; the unsealed defaults `User`, `Role`, `Permission`, `RoleMembership`,
  `NotificationPreference`, `UserStamp` must also live there because every module entity's `CreatedById`
  references `core.Users` and modules never reference `Tellma.Core`. A clarification of intent; the text
  should say "base classes and Core's own default leaves".
- **`Tellma.Core` gains its first runtime area (`Tellma.Core.Access`).** No rule changes.
- **No SQL Server row-level security policies** — consistent with "no logic in the database"; recorded
  because every reviewer asks (D15). The prologue, bumps, and guards are runtime-emitted SQL, the same
  category as Queryex and the save emitter.
- **Audit vocabulary and the concurrency stamp** (four columns everywhere, `ModifiedAt` as the stamp,
  temporal additive) are stated where the architecture is silent; the data-access theme confirms and the
  architecture records it.
- **The sensitive-operation set has a Core default** (D26) that distributions extend but do not shrink —
  an addition under Identity.
- **HTTP verbs** for `me` and `access/check` follow the web-API theme's decision; nothing here depends on it.
- From the brain dump (not the architecture): no separate `OnConnect` call; `SavedAt`/`SavedById` replaced;
  no `SavedById` on weak entities; `UserSettings` renamed and de-entitied; the six-state invitation model
  cut to three; the per-user permissions version replaced by a tenant tag; "all" replaced by `*`; the
  self-narrowing lockout rule replaced by the invariant plus the own-membership rule.

---

## 8. Verification

Relied on from `research/users-roles-permissions.md` (verified there 2026-09-01): native RLS mechanics and
limitations (schema binding, unprotected history tables, no indexed views, dbo filtered, the
`SESSION_CONTEXT` parallel-plan defect); ASP.NET Core 10 authorization order (`CombineAsync`, fallback
versus default policy, `AllowAnonymous` absolute, `IAuthorizationRequirementData` on Minimal API endpoints,
cookie auth returning 401/403 on API endpoints, resource-based checks being imperative); Odoo, Salesforce,
Dataverse permission shapes and the lockout precedents (Entra last-admin rule, GitHub "you can't change
your own role", Odoo self-deactivation guard, Dataverse lockouts); `rowversion` semantics, `uniqueidentifier`
sort order and Guid v7, `MIN_ACTIVE_ROWVERSION`; the identity server's invite and delivery-status APIs as
implemented (statuses, free-text errors, prefix results, `NotFound` scoping, `expectsDeliveryEvents`,
`client_credentials` with `scope=tellma_identity`, no distribution-side client yet).

Verified against the repo and specs in this pass (2026-09-01):
- Spec 0008: `FilterTree.Or([])` is `false`, `Leaf("")` throws, `MaxDepth = 64`, diagnostics carry tree
  locations; `Validate` mints the stamp and `LanguageVersion` is required on validation and compilation;
  `Minimum`/`Version`/`IsSupported`; a rebuilt schema is the only cache invalidation lever; `me()` binds
  to `HasUser`; the schema has no collections and requires enforced referential integrity.
- Spec 0003: `sub` is the stable identity key; no roles, permissions, or tenant membership in tokens; the
  distribution session cookie is distribution-wide; session-ending events close SignalR connections; the
  dev admin `admin@localhost` and the tenant seeding a matching admin on the same `sub`; management API
  limited to invite, delivery status, service accounts.
- Briefing fixed facts: RCSI on by default on Azure SQL only; `MERGE` is out; concatenated text walked
  with `NextResult()` is the batch mechanism; `SqlBatch` unsuitable; dev admin subject
  `00000000-0000-0000-0000-000000000001`; `HybridCache` unsuitable for tag-validated caches.
- Repo: `[TableType]` and `[ExcludeFromTableType]` live in `src/core/Tellma.Core.EntityFrameworkCore/TableTypes/`
  (Abstractions cannot carry them); `src/core/Tellma.Core/` holds only the csproj; spec 0001 §2 states that
  computed columns are always excluded from UDTTs and says nothing about period columns (the data-access
  theme must exclude `GENERATED ALWAYS` columns explicitly); `ISandboxContext`/`SandboxContext.Never` as
  compiled.

Relied on from the designers' own verification, not re-verified here (standard documented behaviour;
the spec author should cite the primary pages): OpenID Connect Core 1.0 §2 — `sub` at most 255 ASCII
characters, case-sensitive; `THROW` requires numbers ≥ 50000, terminates the batch, and rolls back under
`XACT_ABORT ON`; `sp_getapplock` return codes and transaction-owned release; `MemoryCache.GetCurrentStatistics`
requires `TrackStatistics`; the legacy monolith's `dbo.Roles.IsPublic`, persisted computed `State`, GUID
versions, and `dal.OnConnect` shape (precedent only, not load-bearing).

Unverified or inferred (does not change a decision):
- That a batch whose text differs only in its guarded body adds no plan-cache fragmentation beyond the
  body's own (plans are per batch text; the claim is about the prologue adding none) — measure on LocalDB
  via `sys.dm_exec_query_stats` in the integration suite.
- Whether `sys.dm_db_index_operational_stats` counters are stable enough on LocalDB for the write-set
  audit; fall back to change tracking on the test database.
- That `RouteGroupBuilder` conventions stamp metadata onto endpoints added after the convention; the
  startup audit catches any gap.
- The memory estimate per grant; the gauge exists for that reason.
- The cost of L1–L3 on a tenant with tens of thousands of permission rows (index-supported; once per
  security save under the lock).
- The 60-second throttle and the 15-minute maximum age are judgments, not measurements.

---

## 9. Review flags

1. **Tenant-level `Permissions` tag** (chosen: one tag, any security write bumps it, every user's set
   rebuilds on next request) versus a per-user tag with set-based fan-out bumps (members, everyone for
   public roles, the `IsPublic` flip pre-image). Correctness wins; the cost is one small query per active
   user per instance per role edit.
2. **The T-SQL `IF @tm_Guard = 1` wrapper** (chosen) versus `THROW`-ing guards versus the optimistic
   run-then-discard: the wrapper never executes a body under stale premises and returns the fresh rows in
   the failed round trip.
3. **Stored `State` with CHECK constraints** (chosen) versus a persisted computed column derived from
   `InvitedAt`/`ActivatedAt` (equivalent no-drift guarantee; a new shape for the entity contract).
4. **`Kind = Human | System` now, `Service` reserved** (chosen) versus adding `Service` today so MCP
   service accounts share the table from day one.
5. **Resource key `<schema>.<LogicalEntityName>`** (`core.User`; chosen) versus the canonical table name
   (`core.Users`) versus the bare logical name (`User`).
6. **`*` as the wildcard** (chosen) versus `All`.
7. **One `Activate` action** for both directions (chosen) versus `Activate` + `Deactivate`.
8. **A filter on `(R, *)`** is valid and applies to every filterable action of `R` (chosen) versus
   refusing filters whenever either position is a wildcard.
9. **Escalation rules** `Permissions.EscalationBeyondSelf` and `Users.MembershipEscalation` (chosen:
   role editing is delegable) versus documenting `core.Role`/`core.User` `Save` as "administrative,
   equivalent to full admin" and dropping both rules.
10. **`Users.CannotRemoveOwnAdministratorMembership`** (chosen, GitHub's rule) versus allowing
    "make Bob admin, then step down" in one save (the invariant alone would permit it).
11. **The lockout invariant over the seeded Administrator role only** (chosen) versus "any active user
    holding an unfiltered `*/*` (or `core.Role Save`) grant through any active role".
12. **The application lock** on every security-table write (chosen; race-free by construction) versus
    `UPDLOCK`/key-range locking on the checked rows.
13. **The authorization witness** turning a forgotten service check into a 500 (chosen) versus
    Error-level logging only.
14. **Securable metadata as plain endpoint metadata checked by an endpoint filter** (chosen) versus an
    `IAuthorizationRequirementData` attribute evaluated by the authorization middleware.
15. **`core.NotificationPreferences` with one row per type and a bit per channel** (chosen) versus one
    JSON column on the user versus one row per `(type, channel)`.
16. **`core.UserPreferences` with a composite key and no surrogate id, written only by self-service**
    (chosen) versus a weak child entity administrators can also edit through the user save.
17. **Typed `PreferredLanguage`/`PreferredCalendar`/`PreferredTimeZone` on the temporal row** (chosen)
    versus keys in the bag.
18. **`ModifiedAt` kept beside `ValidFrom`** on temporal rows (chosen, one vocabulary) versus using
    `ValidFrom` as the stamp on temporal entities.
19. **Materialised inbox counters on `core.UserStamps`** (reserved shape) versus timestamps plus counted-on-demand.
20. **Sensitive securables default-on for role and user edits** (chosen) versus opt-in per distribution.
21. **`PermissionsMaxAge` of 15 minutes** as the out-of-band-edit backstop (chosen) versus none.
22. **`Users.OnlyNewUsersDeletable`** (chosen) versus allowing deletes with audit FKs surfacing as errors.
23. **Writes on readable-but-not-writable rows return 403** (chosen) versus 404 uniformly.
24. **`Subject varchar(255)` binary collation** (chosen) versus `uniqueidentifier` (smaller, but ties the
    schema to one authority's format).
25. **Filter column 2048 characters** (chosen) versus 4000.
26. **Namespace `Tellma.Core.Abstractions.Access`** (chosen) versus `.Security`.

---

## 10. Conflicts

Positions other themes must reconcile:

1. **Settings (0012):** the tenant-tag table's name and shape (`core.TenantTags (Name, Tag)` rows is what
   the prologue and bump statements assume; a single wide `core.TenantStamps` row works too and changes two
   statements); the `Permissions` and `Settings` rows must exist from the first migration; the settings save
   must bump `Permissions` when the tenant's languages change (the Queryex schema changes); the connect
   prologue reads all tenant tags as result set 1 and the guard includes the `Settings` tag; the cache
   infrastructure (private `MemoryCache`, single-flight) should be one shared shape.
2. **Data access (0011):** the batch API items in §6.1 (prologue slot, guarded body, required `WriteSet`,
   automatic bumps before writes inside the transaction, `THROW` range mapping, deadlock retry, result-set
   readers); `[ServerOwned]`, `[Temporal]`, `[BumpsTenantTag]`, `[BumpsUserTag]`, `[NaturalKey]`,
   `[Searchable]` as entity-contract annotations; period columns excluded from UDTTs; server-side
   `ModifiedAt` stamping with `SYSUTCDATETIME()` and the root-stamped-on-child-change rule; skip-unchanged
   temporal updates; child-before-parent delete order; the `FilterTree.Via` engine amendment; the list
   restriction through a TVP; the reserved id band 1–999 with sequences starting at 1000; `core.UserPreferences`
   as a standalone table-type shape rather than an entity.
3. **Service pipeline (0014):** `IsActive` is server-owned on save for every activatable entity (changed
   only by `Activate`); the two-stage RLS pre-check (404 then 403) and the in-transaction post-check;
   `IGuardedBatchRunner` as the only executor for caller batches; the platform exception types listed in
   §6.10; the validator API through which `UserAccessRules`/`RoleAccessRules` declare context; `IAccessGuards`
   contributed to every persist batch that writes a security table; the compile profile for
   permission-bearing queries (`MaxParameters ≥ 1024`, `MaxJoins ≥ 64`).
4. **Web API (0015):** the verb for `me`; the `Tellma-Tags` response header; the status mapping (403/404/409/422/500);
   the endpoint filter order (tenant filter → coarse securable check → handler → witness); `RequireSecurable`
   and `[MemberEndpoint]` on custom endpoints; the step-up middleware reading `IsSensitive`; MCP `whoami`/`check_access`.
5. **Host (0010):** `ICallerContext` shape and population; the fallback policy and the tenant group's
   `RequireAuthorization()`; whether deactivation from the last remaining tenant ends the distribution session
   cookie; the provisioning seam calling `ITenantBootstrapper` with `--bootstrap-admin`; the step-up
   configuration bar.
6. **Core stack (0017):** `UserService.Invite` writes `Subject`, `InvitedAt`, `InviteStatus`, `LastInviteError`
   through the pipeline and sends the "you were added" notice for status `Active`; the delivery-status
   drill-down endpoint; self-service endpoints for the profile, the preference bag (`UserPreferenceList`),
   and the caller's notification preferences; `RoleService` plugs in `RoleAccessRules` and shows per-permission
   drift; the members extra on the role page; packs seed public roles by `Code`; `admin@localhost` bootstrap.
7. **Background tasks (0019):** the inbox counter columns on `core.UserStamps` and their maintenance and
   reconciliation; `core.UserPushSubscriptions`; the notification-type registry with "cannot mute" flags;
   channel resolution against `core.NotificationPreferences` inside the save batch; jobs connecting through
   `ConnectAsUser`/`ConnectAsSystem` with permissions evaluated at run time; the hub close call on deactivation.
8. **Blobs (0016):** `User.ImageId varchar(64)` is a placeholder for whatever key shape the blob theme fixes;
   the image endpoint is the only writer.
9. **Excel (0018):** `User.Email` and `Role.Code` as natural keys; `State`, `Subject`, `InviteStatus`,
   `FilterLanguageVersion` are server-owned and never imported; `core.UserPreferences` is not exportable.
