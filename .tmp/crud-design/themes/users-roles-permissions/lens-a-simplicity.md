# Users, roles, and permissions — design proposal (distro-author simplicity and AI-native authoring)

Theme key `users-roles-permissions`, future spec 0013. Optimised for the distribution author (a coding
agent) writing the least, most mechanical code, and for the MCP consumer. The yardstick used throughout:
a distribution that adds an entity with `IsActive` and a tree writes **zero** access-control code — its
securables, permission actions, and row filters all derive from the capabilities it already declared —
and a custom action costs one attribute.

---

## 1. Critique

### 1.1 The general design

The draft notes get the big things right: permissions are grant-only and disjunctive; row-level security is
Queryex text composed as a `FilterTree`; the tenant owns membership and the identity server owns only
authentication; public grants are unioned in; inactive roles are ignored; write implies read; and services
may add bespoke criteria. Every mature product surveyed (Odoo record rules, Salesforce sharing rules,
Dataverse security roles) converges on exactly this grant-only, union-of-roles shape, so the model needs no
inventing. What the notes leave open is mostly mechanism, and a few of the open questions dissolve once the
mechanism is fixed.

Four gaps are structural rather than cosmetic:

1. **There is no system principal.** Background schedules, the migrator's runtime seeds, and audit columns
   on seeded rows all need an actor id. Without a seeded system user the first `CreatedById` in the
   database has nowhere to point. The notes mention a "built-in system user" only in the scheduler
   section; it belongs here, in the reserved id band, with evaluator semantics ("full permissions, cannot
   sign in").
2. **The permissions tag is placed on the wrong row.** `PermissionsVersion` on `User` implies that editing
   one role must fan out a tag bump to every member — and editing a public role to every user in the
   tenant. That bump is a join the generic save emitter cannot know about. A single tenant-level tag,
   bumped by any write to the three permission tables, is trivially correct and costs only a cheap
   per-user rebuild on the next request.
3. **The user state model persists what it cannot own.** "Email queued / sent / failed / bounced" are
   identity-server and email-provider facts that arrive unreliably and change after the fact. The tenant
   can own three states on its own evidence — `New`, `Invited`, `Active` — and must treat everything finer
   as a live drill-down through the delivery-status API, never as stored state.
4. **Nothing prevents the tenant from locking itself out.** The notes state the goal ("at least one admin
   user will continue to be active") but not the invariant or where it is checked. The check must run
   inside the same transaction as the write that could violate it, as one SQL statement appended to the
   persist batch; C# reasoning before the write is write-skew-prone under row versioning.

### 1.2 The detailed choices

- **`core.User` (singular) versus `gl.Invoices` (plural).** The architecture document and spec 0001 use
  plural canonical table names throughout (`[gl].[Invoices]`, `[gl].[InvoiceLines]`). This theme uses
  plural: `core.Users`, `core.Roles`, `core.Permissions`, `core.RoleMemberships`.
- **`SavedAt` / `SavedById` next to `ValidFrom` / `ValidTo`.** `SavedAt` duplicates the period start, and
  creation is lost entirely on a temporal entity unless someone walks the history table. Four audit
  columns on every top-level entity (`CreatedAt`, `CreatedById`, `ModifiedAt`, `ModifiedById`) with
  system-versioning as an additive per-table capability gives one vocabulary, keeps creation cheap, and
  gives the concurrency token (`ModifiedAt`) a home the application controls. The one datetime2 column
  that then overlaps with the period start is the price of uniformity.
- **`UserSettings` as a name** collides with the tenant's `Settings` table and with the "UI settings DTO".
  The bag stores personal customisations; `core.UserPreferences` says so.
- **`LastActive`, `UserSettingsVersion`, `PermissionsVersion`, `InboxTracking` on the temporal `User`
  row** are correctly flagged as churn: every `UPDATE` of a temporal row writes a history row even when
  nothing changed. They move to a non-temporal sibling keyed by the user id (`core.UserStamps`), except
  `PermissionsVersion`, which becomes a tenant-level tag (1.1 point 2).
- **`PushSettings` "?? what shape"** conflates two things: whether the user wants push notifications (a
  preference, three booleans and a mute list) and the browser push *subscriptions* (endpoint plus keys,
  one per device, rotated by the browser). The first is a JSON column on the user; the second is a
  device table the inbox theme owns and must never be edited through the user save.
- **`Permission.Resource` "distro specific e.g. invoices, or 'all'"** needs a real scheme. The only
  identifier the architecture guarantees stable across forks and distributions is the canonical
  schema-qualified table name, so `core.Users` and `gl.Centers` are the resource names, `*` is the
  wildcard, and non-entity resources use the same `<schema>.<Name>` grammar (`core.Settings`).
- **`ImageFitJson` on the user row** belongs to whatever the blob theme decides about image metadata; the
  user row keeps `ImageId` only.
- **`RoleMembership` "edited and saved together with the User"** is right for the save pipeline, but the
  role page also needs to show members. One owner (User) for writes, a read-only members list on the role
  page. Two write paths for one child table is the thing the synchronise-children emitter cannot do.
- **"Accessing a record that I have no read permission on returns the same response as a non-existent
  record"** is right for by-id reads (404) and wrong for collection endpoints: a user with no `Read` grant
  on an entity at all should get 403 so the SPA can hide the page, not an empty 200.
- **"If you can read an entity then you can read all the related entities and extras … including related
  entities that are inaccessible"** is a deliberate, acceptable disclosure (a document's customer name is
  visible to whoever can see the document) and must be stated as such in the spec, because it is the one
  place the row filter is knowingly bypassed.

### 1.3 Internal inconsistencies

- Inactive *roles* are excluded but inactive *users* are not mentioned; a deactivated user must be refused
  at the connect step before any permission is consulted, and the refusal must also end the session.
- The `Permission.Filter` "only on resources and actions that support it" rule has no answer for the
  wildcard rows the same section introduces: what does `(*, *, 'IsActive = true')` mean? A filter binds
  against one root entity, so a filtered wildcard *resource* is unbindable (validation error), while a
  filtered wildcard *action* on a concrete resource is fine.
- The save flow authorises with "any write permission on the entity type" and then pre-checks RLS in a
  separate call; with cached permissions both are one decision object (`AccessDecision`) and the pre-check
  rides the first batch.
- The connect step is described as DB call #1 of every operation, and a separate open question asks
  whether it can collapse into the next call. It can, and the answer changes the shape of the request
  context (the endpoint filter must not connect eagerly for projected endpoints).

### 1.4 What the notes do not ask but the spec must answer

The identity invite API's `Active` status ("already holds a credential; no email sent"); service accounts
as tenant principals (the notes defer the table, but the MCP surface needs them on day one, and the connect
step should not need a second lookup path); the securable that defines "administrator" for the lockout
invariant; whether permission filters may use declared parameters (no) and which Queryex limits apply to
them; that a permission set's format has a version the SPA must respect; how a filter that references a
language-gated column (`Name3` on a bilingual tenant) behaves (drift: grants nothing); and that the
connect step's `LastActive` write must be throttled or it becomes a write per request.

---

## 2. Decisions

### D1 — Vocabulary

**Decision.** Plural canonical table names in the `core` schema. Four audit columns on every top-level
entity: `CreatedAt datetime2(7)` (UTC), `CreatedById int`, `ModifiedAt datetime2(7)` (UTC),
`ModifiedById int`, all `NOT NULL`, FK to `core.Users`; `ModifiedAt` is the concurrency token. System
versioning is an additive per-entity capability (`[Temporal]`) whose period columns are EF shadow
properties `ValidFrom`/`ValidTo` (history table `<Table>History` in the same schema). Instants are
`datetime2(7)` UTC everywhere in this theme. Opaque cache validators are **tags** (`uniqueidentifier`,
application-generated, compared for equality only); the hardcoded shape version of a cached item is its
**format version** (`const int FormatVersion`). The (resource, action) pair is a **securable**.

**Rationale.** One vocabulary for temporal and non-temporal entities keeps the save emitter and the
distribution author's mental model identical across entities; `datetime2(7)` matches the period columns
and the concurrency-token hint; "tag" avoids HTTP's reserved meaning of ETag and the ordering implied by
"version"; "securable" is SQL Server's own term for the things permissions apply to.

**Rejected.** `SavedAt/SavedById` (loses creation; two vocabularies). `rowversion` as a tag (bumps on
bookkeeping writes). Monotonic `bigint` tags (restore-unsafe; ordering is not needed anywhere in this
theme). "Etag" (reserved for the blob endpoint). "Metaversion" (not a recognised term; Django's precedent
is a plain version constant).

**Confidence.** High for tags and securable; medium for the audit vocabulary, which the data-access theme
owns. **Review flag:** four audit columns on temporal entities versus `SavedById` + period.

### D2 — Placement: the entity classes and the runtime

**Decision.** The concrete, unsealed default entity classes `User`, `Role`, `Permission`,
`RoleMembership`, `UserPreference`, `UserStamp` live in `Tellma.Core.Abstractions`, namespace
`Tellma.Core.Abstractions.Access`, alongside the access contracts (`ISecurableRegistry`,
`IAccessEvaluator`, `AccessDecision`, `IUserConnector`, `AccessTelemetryNames`). The runtime — the
evaluator, the permission cache, the connect statement, the securable registry builder, the Core stack
feature that registers the entities and their securables — lives in `Tellma.Core`, namespace
`Tellma.Core.Access`. `[TableType]`, temporal, and index configuration are applied fluently by the Core
feature's contribute step (the `TableType` attribute lives in `Tellma.Core.EntityFrameworkCore`, which
Abstractions cannot reference).

A distribution extends `User` by inheritance (`public sealed class User : Tellma.Core.Abstractions.Access.User { public string? EmployeeCode { get; set; } }`)
and selects it at the feature's selection site; the Core services are generic over `TUser : User` and
`TRole : Role`, and every statement this theme emits names only the columns declared on the base classes.
Replacing an entity outright is not supported: the base class's columns are the contract the connect
statement and the permission loader compile against.

**Rationale.** Modules never reference `Tellma.Core`, yet every module entity carries `CreatedById` with
a `User` navigation; the class must therefore be reachable through Abstractions. Generic services over
leaf types are the architecture's own reconciliation of "code against the class" with "distros extend".

**Rejected.** `Tellma.Module.Core` as a module package (Core is not optional). A `Tellma.Core.Access`
package (a fourth Core package with no host that would want access without CRUD).

**Confidence.** High.

### D3 — The `User` entity

**Decision.** Columns (types in §4): `Id`, `Kind` (`Human` | `Service` | `System`, stored as string),
`Subject` (the identity principal id: the OIDC `sub` for humans, the OAuth `client_id` for service
accounts, null for the system user; write-once, server-owned), `State` (`New` | `Invited` | `Active`;
server-owned), `Name`, `Name2`, `Name3`, `Email` (required for humans, write-once after invitation,
natural key), `Language`, `Calendar`, `TimeZone` (the user's UI language, calendar, and IANA zone; null
means the tenant default), `ContactEmail`, `ContactMobile`, `NotificationSettings` (JSON, D7), `ImageId`
(server-owned; set by the blob endpoint), `InvitedAt`, `LastInviteStatus`, `LastInviteError`,
`ActivatedAt` (all server-owned), `IsActive`, and the four audit columns. Temporal. Child collection
`RoleMemberships` travels on the entity as a `[NotMapped]` list (the wire-shape convention of the
data-access theme).

Write-once and server-owned columns are enforced by declaration, not by a second UDTT: `[ServerOwned]`
properties are never taken from the client payload — the save emitter's `UPDATE` does not `SET` them and
the `INSERT` takes their defaults — and dedicated statements owned by the connect step, the invitation
service, and the blob endpoint write them. `Email` is editable while `State = New` and frozen afterwards;
that is one validation rule in `UserService`, not a marker, because it depends on another column.

`Kind` exists now so that service accounts (the identity server issues them `client_credentials` tokens
carrying `client_id` and no `sub`) are rows in the same table with the same memberships, and the MCP
surface needs no second principal lookup. Service-account provisioning itself is the Core stack theme's
work; the column, the `CHECK` that a human has an email, and the connect step's indifference to `Kind`
ship now. `Users.Id = 1` is the seeded system user (D20); the evaluator short-circuits it to unrestricted.

**Rationale.** One table, one membership table, one connect statement for every principal kind. Typed
`Language`/`Calendar`/`TimeZone` columns rather than preference-bag keys because the invitation call needs
the language before the user has any preferences, and background work that renders an email or an inbox
item for the user needs all three without a second lookup.

**Rejected.** A separate `ServiceAccounts` table (doubles the evaluator's inputs and the membership
table). Deriving `State` from `InvitedAt`/`ActivatedAt` (a stored string makes `State = 'Invited'` a
one-token Queryex filter for an agent; the derivation would be three tokens and a null test). Two UDTTs
for create versus update (the rejected `ForSave` pattern in miniature).

**Confidence.** High on the column set; medium on `Kind`. **Review flags:** `Kind` now versus a
service-account table later; typed `Language`/`Calendar`/`TimeZone` versus preference keys.

### D4 — The user state model

**Decision.** `State` moves on the tenant's own evidence only:

| From | To | Trigger | Stored |
|---|---|---|---|
| — | `New` | admin creates the row (UI, import, provisioning seed) | `Subject = null` |
| `New` / `Invited` | `Invited` | the bulk-invite call returns `Invited`, `Reinvited`, or `Active` for the email | `Subject`, `InvitedAt`, `LastInviteStatus` (the server's word verbatim), `LastInviteError = null` |
| `New` / `Invited` | unchanged | the bulk-invite call returns a per-user error | `LastInviteError` (the server's free text, stored for troubleshooting, never localised or branched on) |
| `Invited` | `Active` | the first request in which the connect step resolves this `Subject` | `ActivatedAt` |

`IsActive` is orthogonal (an administrator's switch); a deactivated user is refused at connect
regardless of `State`. Re-inviting an `Invited` user is allowed (the server re-sends; status
`Reinvited`). An `Active` user is never re-invited (validation error). The identity server's `Active`
invite status means "already holds a credential, no email was sent" and is stored as
`LastInviteStatus = 'Active'` with `State = 'Invited'`; the UI renders "invited — no email sent, the user
already has a Tellma account", and it is `UserService`'s job (Core stack theme) to send its own "you were
added" notice. Delivery detail (`Pending | Sent | Delivered | Bounced | Complained | Rejected | Abandoned
| Accepted | NotFound`, plus `expectsDeliveryEvents` deciding whether `Sent` is terminal) is a live
drill-down through the delivery-status API from the user details page and is never persisted.

The states are exhaustive for the tenant's own knowledge: everything finer is either identity-server
state (only reachable live) or email-provider state (only reachable through the identity server).

**Rationale.** The tenant persists only what it can keep true. Two of the three transitions are the
tenant's own writes; the third is one conditional statement inside the connect step.

**Rejected.** Persisting delivery states (stale by construction, changes after the fact). A `Bounced`
tenant state (would need a webhook the identity server does not offer and would go stale on re-invite).

**Confidence.** High.

### D5 — The sibling table `core.UserStamps`

**Decision.** One non-temporal row per user, keyed by `UserId` (PK and FK to `core.Users`, cascade
delete), holding everything that changes on ordinary use: `LastActiveAt datetime2(7) NULL` (stamped by
the connect step, throttled to once per minute), `PreferencesTag uniqueidentifier NOT NULL` (bumped by
every write to that user's `core.UserPreferences` rows), `InboxSeenAt datetime2(7) NULL` (reserved for
the inbox theme: the instant the user last opened the inbox dropdown; that theme adds what else it needs
to this row). The row is inserted by the same persist batch that inserts the user (an `INSERT … SELECT`
appended by the Core stack feature's save hook) and by `HasData` for the system user, so the connect
step never has to upsert it.

**Rationale.** Every `UPDATE` of a temporal row writes a history row even when nothing changed; a
per-request activity stamp on `core.Users` would double every user's history table every minute. Keying
the sibling on the user id keeps the connect read a single `LEFT JOIN` on the primary key.

**Rejected.** Making `core.Users` non-temporal (the history of who had which name, email, and
memberships is a security audit requirement). A generic "entity stamps" table (nothing else needs it).
Names considered: `UserStates` (collides with `Users.State`), `UserTracking`/`UserActivity` (mass nouns;
"activity" reads as a log). **Review flag:** the name `UserStamps`.

**Confidence.** High on the split; medium on the name.

### D6 — Preferences: `core.UserPreferences`

**Decision.** A key-value bag `core.UserPreferences (Id, UserId, Key, Value)`, non-temporal, unique on
`(UserId, Key)`. `Key` is a dotted lowercase name (`nav.pinned`, `grid.core.Users.columns`,
`tour.dismissed`); `Value` is JSON or a plain string. The bag is written only through the self-service
endpoints (`PUT /me/preferences`, `DELETE /me/preferences/{key}`), never through the user save pipeline,
so preference churn never touches the temporal `core.Users` row or its `ModifiedAt`. Every write bumps
`UserStamps.PreferencesTag` for that user in the same statement group. The server caches nothing from the
bag; `Language`, `Calendar`, `TimeZone` are typed columns on `core.Users` read by the connect step. The
SPA caches the bag keyed by the tag and refreshes it when the connect result carries a different tag.
Pinned screens are the key `nav.pinned` (a JSON array of route ids); an administrator customising them
for a less technical user edits the same bag through the user details page under `core.Users / Save`.
No separate table.

**Rationale.** One shape for every personal customisation; nothing the server needs lives in it, so it
has no server-side cache and no format-version obligation beyond the SPA's own.

**Rejected.** A typed preferences row (every new preference would be a migration). A pinned-screens
table (adds a stack for a list an administrator edits once a year).

**Confidence.** High.

### D7 — Notification settings

**Decision.** `ContactEmail` (defaults to `Email` when null) and `ContactMobile` (E.164) are ordinary
editable columns on `core.Users`. `NotificationSettings` is one JSON column mapped to the complex type
`UserNotificationSettings { bool Email = true; bool Sms = false; bool Push = true; string[] Muted = []; }`
where `Muted` holds notification-type keys from the catalog the inbox theme defines, which also decides
the types that may not be muted and validates the keys on save. Browser push *subscriptions* (endpoint,
keys, device label) are a separate table owned by the inbox theme, written only by the subscribe
endpoint. The user row states preferences; it never holds device credentials.

**Rationale.** One record, one column, no join for "does this user want email"; the type catalog can grow
without a migration to `core.Users`.

**Rejected.** A `UserNotificationPreferences (UserId, Type, Email, Sms, Push)` table (a stack for a matrix
nobody filters on). A `notifications` preference key (splits the admin-editable contact channels from the
user-editable choices across two save paths).

**Confidence.** Medium. **Review flag:** JSON column versus a child table.

### D8 — `RoleMembership`

**Decision.** `core.RoleMemberships (Id, UserId, RoleId, Notes)`, temporal, unique on `(UserId, RoleId)`,
a weak child of `User` for the save pipeline (synchronised under the user; a membership absent from the
saved user's list is deleted). The role details page shows its members as a read-only extra (a query the
Core stack theme supplies) with a link to each user. Weak entities carry **no** audit columns and no
`SavedById`: the owning row's `ModifiedAt`/`ModifiedById` are stamped by any change to its children (a
rule the save pipeline needs anyway for concurrency), and the child's own history row carries the same
transaction-begin `ValidFrom` as the parent's, so "who" is one join away for the rare audit question.
`Notes nvarchar(1024)` stays: "added per ticket 4711" is the kind of justification an access review asks
for, and a column the author never has to fill costs nothing.

**Rationale.** One owner, one synchronise path. A membership edited from both the user page and the role
page would need two synchronise scopes over one table and a way to say which side is authoritative.

**Rejected.** Membership as a child of `Role` (the user page is where administrators work; onboarding a
user is "create the user, tick the roles"). Both-sided editing (see above). `SavedById` on children
(redundant with the parent's stamp).

**Confidence.** Medium. **Review flag:** ownership side; `Notes` retention.

### D9 — `Role` and public permissions

**Decision.** `core.Roles (Id, Name, Name2, Name3, Code, IsPublic, IsActive, audit)`, temporal. `Name`
unique; `Name2`, `Name3`, `Code` unique when not null. `IsPublic = 1` means the role's permissions apply
to every principal in the tenant; memberships in a public role are rejected by validation and the members
tab is hidden. Several public roles may exist and union like any other roles; a deactivated public role
grants nothing. Packs seed baseline roles through the runtime seed pipeline (allocator ids, found by
`Code`), never by hardcoded id; the Core seed creates `Administrators` (`Code = 'ADMIN'`, one permission
`(*, *)`) and `Everyone` (`Code = 'EVERYONE'`, public, initially empty; module seeds add their lookup
`Read` grants to it).

**Rationale.** `IsPublic` reuses the whole role editor, the securables validation, the drift diagnostics,
activation, and the audit history for public grants; every product surveyed keeps public grants inside the
same validation and UI as other grants and none uses a separate table.

**Rejected.** A system role with a hardcoded id (a reserved-band `HasData` row an administrator cannot
rename or deactivate, and a second code path in the evaluator). A role-less permissions table in
settings (a second editor, a second validator, a second drift path).

**Confidence.** High.

### D10 — `Permission`: resources, actions, filters

**Decision.** `core.Permissions (Id, RoleId, Resource, Action, Filter, FilterLanguageVersion, Notes)`,
temporal, weak child of `Role`, no unique index (a duplicate row is harmless; the editor warns).

**Resource names** are the canonical schema-qualified table name for entity resources (`core.Users`,
`core.Roles`, `gl.Centers`) and the same `<schema>.<PascalName>[.<Sub>]` grammar for non-entity resources
(`core.Settings`, `core.Settings.General`). The wildcard `*` matches every resource. Matching is exact
and case-insensitive; there are no prefix wildcards.

**Action names** are PascalCase verbs from a closed set per resource, declared by the securables registry:
the CRUD capabilities contribute `Read`, `Save`, `Delete`, and `Activate` (activate and deactivate both);
custom actions add their own (`Invite` on `core.Users`). The wildcard `*` matches every action of the
resource. `Read` is implied by every other action on the same resource, with the same filter; no other
implication exists.

**Filters** are Queryex predicate text bound in `Filter` mode against the securable's filter root, with
no declared parameters (an `@x` is QX3007), `HasUser = true` (`me()` allowed), and the stricter limits
profile `AccessLimits` (`MaxInputLength 2048`, `MaxJoins 8`, `MaxParameters 64`, `MaxTypedNodes 512`,
others default). `FilterLanguageVersion` is required exactly when `Filter` is not null and is stamped by
`RoleService` from `QueryexLanguage.Version` at the `Validate` call that admitted the text. A filter on
a securable that has no filter root is a validation error (`Access.FilterUnsupported`); a filter with
`Resource = '*'` is a validation error (`Access.WildcardFilter`) because there is no single root to bind
against; a filter with `Action = '*'` on a concrete resource is valid and applies to every action of that
resource.

**Rationale.** The canonical table name is the one identifier the architecture keeps stable across forks
and distributions, is what an operator sees in SSMS, and reads unambiguously to an agent authoring a role
through MCP. A closed action set per resource is what makes the role editor a picker instead of a text
box and makes drift detectable. No declared parameters because a permission is evaluated in someone
else's request, where nothing could bind them.

**Rejected.** Queryex logical entity names as resources (`User`) — bare singular names are not obviously
unique across packs and are not what the database shows. Route segments (`users`) — a surface concern
that may differ between the web and public surfaces. `All` instead of `*` (IAM precedent; `*` is
unmistakable as "not a name"). `<schema>.*` prefix wildcards (one `StartsWith` in code, but every "why"
answer and every drift diagnostic must then explain a match nobody wrote). **Review flag:** prefix
wildcards; `Activate` as one action for both directions (versus `Activate` + `Deactivate`).

**Confidence.** High on resources and filters; medium on the wildcard scope.

### D11 — The securables registry

**Decision.** The term is **securable**. A securable is `(Resource, Action, FilterRoot?, Owner?)` where
`FilterRoot` is the Queryex logical entity name the filter binds against (null when the action is not
filterable) and `Owner` is reserved for weak entities (D14). `ISecurableRegistry` is an immutable
singleton built once in `AddTellma`'s realize phase from every `ISecurableSource` the composition
contributes:

- the CRUD stack feature contributes, per entity stack, one securable per capability it realises —
  `Read` always; `Save` when editable; `Delete` when deletable; `Activate` when the entity carries
  `IsActive`; tree entities add nothing (`GetByParentIds` is `Read`, `DeleteWithDescendants` is
  `Delete`); export is `Read`; import is `Save`;
- a service method decorated `[Securable("Invite")]` (resource defaulting to the service's entity) or
  `[Securable("core.Settings.General", "Save")]` contributes that securable and the endpoint projection
  copies it into the endpoint's metadata;
- a feature with non-endpoint securables adds them in its contribute step.

Startup validation (one aggregated diagnostic with the rest of `AddTellma`'s gate): no duplicate
`(Resource, Action)`; every `FilterRoot` resolves to an entity of the distribution's Queryex schema; every
`Owner` names a registered resource and a navigation that exists on the weak entity; every endpoint under
the tenant API group carries at least one securable or the explicit `[MemberEndpoint]` marker ("any
connected member", used by `/me` and the preferences endpoints); every securable named in endpoint
metadata exists in the registry; `[AllowAnonymous]` inside the tenant group is an error.

The registry is also the role editor's data source (`GET /access/securables`, localised display names
resolved by resource and action keys) and the drift oracle (D18).

**Rationale.** Declared once: an entity stack's securables fall out of its capabilities; the author
writes nothing for the common case and one attribute for a custom action.

**Rejected.** "Resource" as the term (too generic; the tuple is the thing). Registration by explicit
list in the distribution (forgettable; duplicates the capability declaration).

**Confidence.** High.

### D12 — Hard to leave unsecured

**Decision.** Four layers, in order of coarseness:

1. The host's `FallbackPolicy` is `RequireAuthenticatedUser()`: an endpoint with no authorization
   metadata at all is at least authenticated.
2. The tenant route group `/{tenantId}/api/web` carries `RequireAuthorization(TenantMemberPolicy)` and
   the tenant endpoint filter, so every projected and custom endpoint inherits membership resolution.
3. Securable endpoint metadata: the projection turns each `[Securable]` into a `SecurableRequirement`
   (an `IAuthorizationRequirementData` attribute in the web layer) that a dynamic
   `IAuthorizationPolicyProvider` resolves; the authorization middleware evaluates the coarse decision
   (`Denied` → 403) before the handler runs. The startup audit of D11 refuses to start when an endpoint
   in the group carries neither a securable nor `[MemberEndpoint]`.
4. Row filters and record checks are imperative inside the service: the CRUD pipeline obtains the
   `AccessDecision` once per operation and applies its `FilterTree` to every query and its pre/post
   checks to every write; custom service code obtains the same decision through the service base
   (`RequireAsync(action)`), which is the only API that yields a filter. There is no way to obtain SQL
   for an RLS-bearing query through the stack without an `AccessDecision` in hand.

The connect step (D16) is eager by default: the tenant endpoint filter connects before the handler unless
the endpoint's metadata carries `ConnectInBatchMetadata`, which the CRUD projection adds to every
endpoint whose handler runs the connect statement inside its first batch. A custom endpoint therefore
pays one extra round trip until its author opts into the collapse.

**Rationale.** The coarse check is declarative and audited at startup; the fine check has one entry
point; the only way to skip membership resolution is to take a documented opt-in that the platform's own
endpoints use.

**Confidence.** High.

### D13 — Permission evaluation

**Decision.** Evaluation is a pure function over the principal's **resolved permission set** (D15) and
the securable:

```
Decide(set, resource, action, bespoke):
  if set.IsSystem: return Unrestricted (grant: System)
  rows = set.Grants where (Resource = resource or '*')
                    and (Action = action or '*' or (action = 'Read' and Action is any registered action of resource))
  rows = rows where the row resolves (D18) — unresolved rows are excluded and reported as Problems
  if action is not filterable (securable.FilterRoot is null): rows = rows where Filter is null
  leaves = rows where Filter is not null, as FilterTree.Leaf(Filter)  (+ bespoke criteria leaves)
  if any row has Filter = null: return Unrestricted (grants: those rows)
  if leaves is empty: return Denied (grants: none; problems: any excluded rows)
  return Filtered (Filter = FilterTree.Or(leaves), grants: the filtered rows and bespoke sources)
```

Rules the function encodes: public roles' rows are in the set like any other rows (marked `IsPublic`);
inactive roles were excluded when the set was built; an unfiltered grant absorbs every filtered one;
filters union (`Or`); an empty `Or` is `false` (denied, fail closed); a filtered row never grants a
non-filterable action; `Read` inherits the filter of the write grant that implies it; bespoke criteria
from `IAccessCriteriaProvider` implementations registered for the resource are additional leaves (never
absorptions) and appear in the "why" as `Bespoke` grants with the provider's reason key.

Consumers: the CRUD pipeline calls `EvaluateAsync(resource, action)` once per operation; queries take
`decision.Filter` into `QuerySpec.Filter` as `And([userFilter, decision.Filter])`; by-id reads whose row
is filtered out return not-found; collection operations on `Denied` throw the forbidden exception; writes
pre-check the ids they update against the `Save` (or `Delete`/`Activate`) filter in the first batch and
post-check the persisted rows against the same filter inside the persist batch (both are count queries
with the list restriction the data-access theme adds to the engine); details pages load related
entities without filters (the accepted disclosure of §1.2).

**Rationale.** A pure function over an immutable set is unit-testable without a database, is the same
code path for "can I" and "can user X", and is what the cache stores.

**Rejected.** SQL Server native row-level security (logic in the database; static predicates cannot hold
user-authored Queryex; schema binding fights expand/contract; history tables unprotected; indexed views
impossible; dbo filtered too). Deny rules (every surveyed product's role rules are grant-only; Odoo's
intersecting global rules are documented as a lockout hazard).

**Confidence.** High.

### D14 — Weak entities as query roots: `FilterTree.Via`

**Decision.** When weak entities become queryable (later), the securable for a weak entity declares
`Owner = (OwnerResource, OwnerPath)` — `core.RoleMemberships` → (`core.Users`, `User`) — and the
evaluator decides the owner's securable and rebases the resulting filter through the navigation with a
new `FilterTree` node: `FilterTree.Via(string navigation, FilterTree inner)`, under which every path in
`inner` resolves from the navigation's target instead of the root, and context functions bind as usual.
The engine amendment is small (the binder pushes a root-rebasing frame; diagnostics carry
`Filter.Via[User]` locations) and is documented by the data-access spec since spec 0008 is frozen. The
node is reserved now; the Core securables for `core.RoleMemberships` and `core.Permissions` are not
registered until weak roots exist.

**Rationale.** Rewriting permission *text* (`PostingDate > X` → `Parent.PostingDate > X`) needs a parser
on the host side and would re-stamp language versions; a structural node keeps the stored text untouched
and the composition safe.

**Rejected.** Text rewriting. Storing separate filters per weak entity (an administrator would have to
author the same rule twice).

**Confidence.** Medium (the engine amendment is unbuilt). **Review flag:** `Via` node versus a
`Root`-rebasing option on `QuerySpec`.

### D15 — The permission set, its cache, and its tag

**Decision.** A tenant-level tag named `Permissions` in the settings theme's tenant-tag table is bumped
by every batch that writes `core.Roles`, `core.Permissions`, or `core.RoleMemberships` (the three entity
classes carry `[CacheTag("Permissions")]`; the save emitter bumps automatically; raw SQL writing those
tables must declare them as written, which the batch API requires of every raw statement). `core.Users`
writes do **not** bump it: `IsActive`, `Kind`, and `State` are read live by the connect step.

The evaluator holds one private bounded `MemoryCache` per process, keyed `(TenantId, UserId)`, value
`UserAccess { Tag, FormatVersion, IsSystem, Grants[], Problems[] }`, `SizeLimit` in entries (default
20 000, LRU), single-flight per key. On every request the connect step returns the current tenant tags;
a cache entry whose `Tag` differs is a miss. A miss runs the load statement (§3.4) — the tag row first,
then the rows, in that order, so that a bump racing the load is caught on the next request rather than
masked — then resolves each row against the registry and validates each distinct filter text once
(`Validate` with the stored `FilterLanguageVersion`; the engine's own caches make repeats free) and
stores the set. `UserAccess.FormatVersion = 1` is returned to the SPA with the set so a client-side copy
that outlives a deploy is discarded when the shape changes.

**Rationale.** Any role edit invalidates every principal's set, and the rebuild is one small query per
principal on their next request: seconds of spread-out work against a bump rule the emitter can apply
blindly. A per-user tag would need the emitter to know which users a role write affects and would still
have to bump everyone for a public role.

**Rejected.** Per-user `PermissionsVersion` (fan-out join in the emitter). `HybridCache` (serialises
every write, shares the DI memory cache, process-local tag stamps that cannot be validated against a
database value). Caching the compiled SQL (the engine already does, keyed on schema identity).

**Confidence.** High on the tag placement; medium on the cache implementation. **Review flag:**
tenant-level versus per-user tag.

### D16 — The connect step and its collapse into the first round trip

**Decision.** The connect step is one statement group (§3.4, `ConnectStatement`) that: resolves the
request principal (`sub` from the session cookie, `client_id` from a bearer token) to `core.Users` by
`Subject`; refuses unknown or deactivated principals; stamps `UserStamps.LastActiveAt` when the previous
stamp is older than one minute; flips `State` from `Invited` to `Active` once; returns the user row
(joined to its stamps) and the tenant-tag rows. Its result is `ConnectOutcome { Status, User, Tags }`.

It executes in one of two places, chosen by endpoint metadata (D12): eagerly in the tenant endpoint
filter, or as the first statements of the operation's first batch. In the collapsed form the CRUD
pipeline builds the batch from the **cached** permission set and cached tenant configuration:

- **Read** (`GetByQuery`, `GetById`, `GetByParentIds`, exports): one round trip — connect + queries.
  If `ConnectOutcome.Status ≠ Connected`, the query results are discarded and the request fails 403; if
  the returned `Permissions` tag differs from the cached set's tag, the set is rebuilt and the batch is
  re-run once; likewise for a differing settings tag. Cold cache (no set for this principal on this
  instance): two round trips (connect + permission load, then the queries).
- **Save**: two round trips — (connect + RLS pre-check + validation context) then (persist + RLS
  post-check + tag guard + tag bumps). The persist batch asserts the `Permissions` tag it was built
  against (`TagGuard`, §3.4) and rolls back with `Access.PermissionsChanged` if it moved; the pipeline
  then rebuilds the set and re-runs from the first batch, once.
- **Background scopes**: the job runner connects by user id (`ConnectAsUserAsync`) — no principal
  lookup, no activity stamp, the same tag read — so permissions are evaluated at run time with the same
  evaluator.

Failure modes, stated: a deactivated user is refused before any write in every shape (the connect
statement precedes the persist batch); a stale permission set can only over-restrict or over-permit a
*read* batch's queries for one round trip whose results are discarded on tag mismatch, and never a
persist batch (the guard); a filter that references a row the same batch updates is evaluated against the
pre-update image in the pre-check and the post-update image in the post-check, which is the intended
pair; the `Invited → Active` flip is idempotent under concurrent first requests (conditional `UPDATE`);
the activity stamp is throttled so a burst of requests writes once.

**Rationale.** One round trip for the common read and two for the common save, with correctness resting
on the tag guard inside the transaction rather than on cache freshness.

**Rejected.** Always-eager connect (a round trip per request). Membership cached without a tag
(unbounded staleness for endpoints that never touch the database).

**Confidence.** High on the design; medium on the pipeline plumbing, which the service-pipeline theme
owns.

### D17 — Self-lockout guards

**Decision.** Two guards, no more:

1. **No self-deactivation or self-deletion.** `UserService` rejects `Activate`/`Delete` requests whose
   ids include the acting user (`Access.SelfDeactivation`, `Access.SelfDeletion`), in C#, before any
   batch.
2. **The last administrator invariant**, checked in SQL inside the persist batch of every save, delete,
   or activation of `core.Users`, `core.Roles`, `core.RoleMemberships`, or `core.Permissions`: after the
   write, at least one row of `core.Users` with `Kind = 'Human'`, `IsActive = 1`, `State IN ('Invited',
   'Active')` must hold an unfiltered grant covering `(core.Roles, Save)` through an active role (public
   or by membership). The statement (§3.4, `LastAdministratorGuard`) throws with the code
   `Access.LastAdministrator`, which the batch executor maps to a validation error and which rolls the
   transaction back.

`(core.Roles, Save)` unfiltered is the administrator capability: whoever holds it can grant themselves
everything else. A user narrowing their own permissions while another administrator remains is allowed;
the invariant, not a self-rule, protects the tenant. The system user is excluded from the count and
cannot be edited, deactivated, or deleted (`Access.SystemUser`).

**Rationale.** Matches the common denominator of Entra ID, GitHub organisations, and Odoo: no
self-removal, last holder protected, out-of-band recovery documented. Checking in SQL in the same
transaction is the only write-skew-safe place.

**Rejected.** "Cannot remove your own administrator grant" (no surveyed product does it; it blocks the
legitimate hand-over "make Bob admin, then step down" in one save). A seeded break-glass tenant user (the
identity server's break-glass administrator plus a documented operator procedure is the recovery path;
a second always-on administrator row is a standing credential).

**Confidence.** High. **Review flag:** the self-narrowing guard.

### D18 — Drift

**Decision.** A permission row that no longer resolves grants nothing and never blocks the principal's
other rows. Unresolved means: `Resource` not in the registry (`Access.UnknownResource`); `Action` not
registered for the resource (`Access.UnknownAction`); a filter on a non-filterable securable
(`Access.FilterUnsupported`); a filter that fails `Validate` against the tenant's current schema
(`Access.FilterInvalid`, carrying the Queryex diagnostics — this is also how a filter that references a
language-gated column such as `Name3` on a bilingual tenant fails); a `FilterLanguageVersion` below
`QueryexLanguage.Minimum` (`Access.FilterVersionUnsupported`). Problems are computed when the set is
built, kept in `UserAccess.Problems`, surfaced in the "why" answer, in the role details response as a
per-permission diagnostic, in the role editor as a banner, in structured logs at set build, and counted
by `tellma.access.permissions.unresolved`. A pack rename ships a recipe that rewrites `Resource`/`Action`
strings and re-validates filters; until it runs the affected grants are simply inert. There is no shim
and no tenant-wide block.

**Rationale.** Fail closed at the row, not at the user: an administrator whose one broken permission
removed *all* their access could not fix it.

**Confidence.** High.

### D19 — "Can I, and why", and the bootstrap call

**Decision.**

- `GET /{tenantId}/api/web/me` (`[MemberEndpoint]`): the connect result for the caller — user profile,
  preferences, `UserAccess` (tag, format version, grants, problems), tenant tags. The SPA's first call
  after sign-in and its refresh call whenever a connect result reports a changed tag.
- `POST /{tenantId}/api/web/access/check` with `{ userId?: int, securables: [{ resource, action }] }`
  returns one `AccessDecision` per securable — outcome, filter text (the composed tree rendered for
  display), grants (role id and name, permission id, resource, action, filter, `IsPublic`, or the bespoke
  reason key, or `System`), and problems. `userId` omitted means the caller; naming another user
  requires `(core.Users, Read)` on that user (filtered), so an administrator can answer "why can't Sara
  see this?" from the user details page.
- MCP tools (the web-API theme owns the surface; these are the shapes): `whoami` returns the `/me`
  payload compacted to name, kind, and the list of resources with their actions and whether each is
  filtered; `check_access` wraps the check endpoint.

**Rationale.** The "why" is a by-product of the evaluator's grant list; exposing it makes the security
model legible to agents and administrators alike.

**Confidence.** High.

### D20 — Bootstrap: the system user, the first administrator, local development

**Decision.**

- **System user.** `HasData` in the reserved band: `core.Users` `Id = 1`, `Kind = 'System'`, `Subject =
  NULL`, `State = 'Active'`, `Name = 'System'`, `Email = NULL`, `IsActive = 1`, audit columns pointing at
  itself, plus its `core.UserStamps` row. The reserved band is ids `1–999` for Core's well-known rows;
  every `sq_*` sequence starts at `1000`. The evaluator returns unrestricted for `UserId = 1` with a
  `System` grant; the connect step never resolves it (no subject); scheduled work runs as it; validation
  refuses to save, deactivate, or delete it.
- **Roles.** The Core runtime seed (through the bulk pipeline, allocator ids) creates `Administrators`
  (`Code = 'ADMIN'`, `(*, *)`) and `Everyone` (`Code = 'EVERYONE'`, `IsPublic = 1`, no permissions).
  Module seeds add lookup `Read` grants to `Everyone` by code.
- **First administrator.** Tenant provisioning (the host theme's seam) passes
  `TenantBootstrap { AdminEmail, AdminName, Language }` to the migrator's seed step, which creates the
  user (`State = New`), the `Administrators` membership, and then calls `UserService.InviteAsync` — the
  same code path an administrator uses later. The invariant of D17 accepts an `Invited` administrator.
- **Local development.** In the `Development` environment only, the reference distribution's seed
  creates the administrator with `Subject = '00000000-0000-0000-0000-000000000001'`, `Email =
  'admin@localhost'`, `State = 'Invited'` (no invite call; the in-proc identity server seeds the matching
  identity), so the first sign-in flips it to `Active` through the ordinary connect step.

**Rationale.** One reserved row gives every audit column an actor and every schedule a principal; roles
are ordinary data found by code so nothing in code depends on a role id.

**Rejected.** A negative id for the system user (readable, but every seeded `CreatedById = -1` looks like
an error to a reader). Seeding the administrator by `HasData` (its subject differs per environment).

**Confidence.** High.

### D21 — Telemetry

**Decision.** Meter `Tellma.Core` (the package), instruments as `const`s in
`Tellma.Core.Abstractions.Access.AccessTelemetryNames`: `tellma.access.decisions` (counter; tag
`outcome` ∈ `denied | filtered | unrestricted`), `tellma.access.cache.hits`, `tellma.access.cache.misses`,
`tellma.access.cache.entries` (gauge), `tellma.access.set.build.duration` (histogram, seconds),
`tellma.access.permissions.unresolved` (counter; tag `problem` ∈ the codes of D18),
`tellma.access.guards.triggered` (counter; tag `guard` ∈ `last_administrator | self_deactivation |
self_deletion | system_user`), `tellma.connect.duration` (histogram, seconds; tag `mode` ∈ `eager |
batched | job`), `tellma.connect.rejections` (counter; tag `reason` ∈ `unknown_principal |
deactivated`). No tenant or user tags; tenant and user ids go to structured log events
(`ConnectRejected`, `AccessSetBuilt`, `PermissionUnresolved`, `GuardTriggered`).

**Confidence.** High.

### D22 — Testing

**Decision.** `test/core/Tellma.Core.Tests` (unit, every PR): `Decide` over hand-built sets (absorption,
union, implied read, wildcard/filter interaction, inactive and public roles, system user, bespoke
leaves, every drift code); registry validation (duplicates, unknown roots, endpoint audit over an
in-memory `EndpointDataSource`); `Via` composition shape; `FilterTree` rendering for the "why" answer.
`test/core/Tellma.Core.IntegrationTests` (`Category=Integration`, LocalDB or Testcontainers, every PR):
the connect statement (unknown, deactivated, throttle, the one-time flip under two concurrent first
requests), the permission load ordering under a racing bump, the last-administrator guard (rollback on
violation; an `Invited` administrator counts; the system user does not), tag bumps on each of the three
tables and not on `core.Users`, and the collapsed read and save shapes end to end against the fixture
distribution. No `Live=true` suite: the identity invite API is exercised by the Core stack theme's suite
with a capturing sender.

---

## 3. Contracts

Code blocks are normative for shape, not formatting.

### 3.1 Entities (`Tellma.Core.Abstractions.Access`)

Base classes, `[Temporal]`, `[ServerOwned]`, `[NaturalKey]`, `[Searchable]`, `[CacheTag]`, and the
`[NotMapped]` child-collection convention are the data-access theme's; they are used here as needed
from that seam (§6.2).

```csharp
namespace Tellma.Core.Abstractions.Access;

/// <summary>What a <see cref="User"/> row represents: a person, an autonomous client, or the
///     platform itself. Stored as text so an expression can say <c>Kind = 'Service'</c>.</summary>
public enum UserKind
{
    /// <summary>A person who signs in through the identity server; <see cref="User.Subject"/> is
    ///     the OpenID Connect subject.</summary>
    Human,
    /// <summary>A service account issued client-credentials tokens; <see cref="User.Subject"/> is
    ///     its client id.</summary>
    Service,
    /// <summary>The seeded platform principal that runs unattended work. Exactly one row, never
    ///     signs in, never edited.</summary>
    System,
}

/// <summary>Where a user is on the way from creation to first use of this tenant. Moves only on
///     the tenant's own evidence; finer invitation-delivery detail is read live from the identity
///     server and never stored.</summary>
public enum UserState
{
    /// <summary>Created here; the identity server has not been asked to invite the user.</summary>
    New,
    /// <summary>The invitation call succeeded; the user has not yet made a request here.</summary>
    Invited,
    /// <summary>The user has made at least one authenticated request in this tenant.</summary>
    Active,
}

/// <summary>The tenant's own record of a principal. Temporal; the churn-prone bookkeeping lives in
///     <see cref="UserStamp"/>.</summary>
[Temporal]
public class User : AuditedEntity
{
    /// <summary>Human, service account, or the system principal.</summary>
    public UserKind Kind { get; set; } = UserKind.Human;

    /// <summary>The identity principal id this row belongs to: the subject claim for humans, the
    ///     client id for service accounts, null for the system user and for users not yet invited.
    ///     Case-sensitive; unique among non-null values; set once by the invitation and never by a
    ///     client payload.</summary>
    [ServerOwned, MaxLength(255)]
    public string? Subject { get; set; }

    /// <summary>The onboarding state. Advanced by the invitation service and the connect step.</summary>
    [ServerOwned]
    public UserState State { get; set; } = UserState.New;

    /// <summary>Display name in the tenant's primary language.</summary>
    [Required, MaxLength(255), Searchable]
    public string Name { get; set; } = null!;

    /// <summary>Display name in the tenant's secondary language, when configured.</summary>
    [MaxLength(255), Searchable]
    public string? Name2 { get; set; }

    /// <summary>Display name in the tenant's ternary language, when configured.</summary>
    [MaxLength(255), Searchable]
    public string? Name3 { get; set; }

    /// <summary>The sign-in email. Required for humans, unique among non-null values, editable only
    ///     while <see cref="State"/> is <see cref="UserState.New"/>. The natural key for import.</summary>
    [MaxLength(255), NaturalKey, Searchable]
    public string? Email { get; set; }

    /// <summary>Preferred UI and message language (BCP 47); null means the tenant's primary language.
    ///     Passed to the identity server as the invitation locale.</summary>
    [MaxLength(16)]
    public string? Language { get; set; }

    /// <summary>Preferred calendar code; null means the tenant's primary calendar.</summary>
    [MaxLength(16)]
    public string? Calendar { get; set; }

    /// <summary>Preferred IANA time zone; null means the tenant's time zone.</summary>
    [MaxLength(64)]
    public string? TimeZone { get; set; }

    /// <summary>Where notifications are emailed; null means <see cref="Email"/>.</summary>
    [MaxLength(255)]
    public string? ContactEmail { get; set; }

    /// <summary>Where notifications are texted, in E.164 form.</summary>
    [MaxLength(32)]
    public string? ContactMobile { get; set; }

    /// <summary>Which channels and notification types the user wants. Stored as JSON.</summary>
    public UserNotificationSettings NotificationSettings { get; set; } = new();

    /// <summary>The profile image's blob id; written only by the image endpoint.</summary>
    [ServerOwned]
    public Guid? ImageId { get; set; }

    /// <summary>When the last invitation call for this user succeeded.</summary>
    [ServerOwned]
    public DateTime? InvitedAt { get; set; }

    /// <summary>The identity server's status word from the last successful invitation call:
    ///     "Invited", "Reinvited", or "Active" (no email was sent; the user already had an account).</summary>
    [ServerOwned, MaxLength(16)]
    public string? LastInviteStatus { get; set; }

    /// <summary>The identity server's error text from the last failed invitation call; cleared on
    ///     success. Free text for troubleshooting, never localised or interpreted.</summary>
    [ServerOwned, MaxLength(1024)]
    public string? LastInviteError { get; set; }

    /// <summary>When the connect step first resolved this user's subject in this tenant.</summary>
    [ServerOwned]
    public DateTime? ActivatedAt { get; set; }

    /// <summary>False refuses every request from this user and removes them from the
    ///     last-administrator count. Changed only through the activation action.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>The user's role memberships, saved and synchronised with the user. Not an EF
    ///     navigation.</summary>
    [NotMapped]
    public List<RoleMembership>? RoleMemberships { get; set; }
}

/// <summary>Channel switches and muted notification types. The notification-type catalog and the
///     types that cannot be muted are defined with the inbox.</summary>
public sealed class UserNotificationSettings
{
    /// <summary>Send notifications to <see cref="User.ContactEmail"/>.</summary>
    public bool Email { get; set; } = true;

    /// <summary>Send notifications to <see cref="User.ContactMobile"/>.</summary>
    public bool Sms { get; set; }

    /// <summary>Send notifications to the user's registered push subscriptions.</summary>
    public bool Push { get; set; } = true;

    /// <summary>Notification-type keys the user does not want on any channel.</summary>
    public IList<string> Muted { get; set; } = [];
}

/// <summary>Per-user bookkeeping that changes on ordinary use. Non-temporal; one row per user,
///     created with the user.</summary>
public class UserStamp
{
    /// <summary>The user; primary key and foreign key.</summary>
    [Key]
    public int UserId { get; set; }

    /// <summary>The last user-initiated request, stamped by the connect step at most once a minute.</summary>
    public DateTime? LastActiveAt { get; set; }

    /// <summary>Opaque tag of the user's preference bag; regenerated by every preference write.</summary>
    public Guid PreferencesTag { get; set; }

    /// <summary>When the user last opened the inbox; maintained by the inbox.</summary>
    public DateTime? InboxSeenAt { get; set; }
}

/// <summary>One personal customisation. Written only through the self-service preference
///     endpoints, never through the user save.</summary>
public class UserPreference
{
    /// <summary>Surrogate key from <c>core.sq_UserPreferences</c>.</summary>
    public int Id { get; set; }

    /// <summary>The owning user.</summary>
    public int UserId { get; set; }

    /// <summary>Dotted lowercase key, e.g. <c>nav.pinned</c>; unique per user.</summary>
    [Required, MaxLength(128)]
    public string Key { get; set; } = null!;

    /// <summary>The value: JSON or a plain string.</summary>
    [Required]
    public string Value { get; set; } = null!;
}

/// <summary>A user's membership in a role. A weak child of <see cref="User"/>.</summary>
[Temporal]
public class RoleMembership : WeakEntity<User>
{
    /// <summary>The role.</summary>
    public int RoleId { get; set; }

    /// <summary>Why the membership exists; free text for access reviews.</summary>
    [MaxLength(1024)]
    public string? Notes { get; set; }

    /// <summary>Typed reference to the role (child-to-parent navigation).</summary>
    public Role? Role { get; set; }
}

/// <summary>A named bundle of permissions. Temporal.</summary>
[Temporal, CacheTag(AccessTags.Permissions)]
public class Role : AuditedEntity
{
    /// <summary>Unique display name in the primary language.</summary>
    [Required, MaxLength(255), Searchable]
    public string Name { get; set; } = null!;

    /// <summary>Unique among non-null values.</summary>
    [MaxLength(255), Searchable]
    public string? Name2 { get; set; }

    /// <summary>Unique among non-null values.</summary>
    [MaxLength(255), Searchable]
    public string? Name3 { get; set; }

    /// <summary>Stable code for seeds and import; unique among non-null values. The natural key.</summary>
    [MaxLength(50), NaturalKey, Searchable]
    public string? Code { get; set; }

    /// <summary>True applies the role's permissions to every principal in the tenant; such a role
    ///     has no members.</summary>
    public bool IsPublic { get; set; }

    /// <summary>False removes the role's permissions from every evaluation.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>The role's permissions, saved and synchronised with the role. Not an EF navigation.</summary>
    [NotMapped]
    public List<Permission>? Permissions { get; set; }
}

/// <summary>One grant: an action on a resource, optionally restricted to rows matching a Queryex
///     predicate. A weak child of <see cref="Role"/>.</summary>
[Temporal, CacheTag(AccessTags.Permissions)]
public class Permission : WeakEntity<Role>
{
    /// <summary>The securable's resource: a canonical table name such as <c>core.Users</c>, a
    ///     non-entity resource such as <c>core.Settings.General</c>, or <c>*</c>.</summary>
    [Required, MaxLength(128)]
    public string Resource { get; set; } = null!;

    /// <summary>The securable's action: <c>Read</c>, <c>Save</c>, <c>Delete</c>, <c>Activate</c>, a
    ///     custom action, or <c>*</c>.</summary>
    [Required, MaxLength(32)]
    public string Action { get; set; } = null!;

    /// <summary>Row predicate in Queryex, bound against the securable's filter root; null grants
    ///     every row. No declared parameters; context functions allowed.</summary>
    [MaxLength(2048)]
    public string? Filter { get; set; }

    /// <summary>The Queryex language version <see cref="Filter"/> was validated under; required
    ///     exactly when a filter is present.</summary>
    public int? FilterLanguageVersion { get; set; }

    /// <summary>Why the grant exists; free text for access reviews.</summary>
    [MaxLength(1024)]
    public string? Notes { get; set; }
}

/// <summary>Well-known ids in the reserved seed band.</summary>
public static class WellKnownUsers
{
    /// <summary>The system principal's id.</summary>
    public const int SystemUserId = 1;
}

/// <summary>Tenant-level tag names this area bumps and reads.</summary>
public static class AccessTags
{
    /// <summary>Bumped by every write to roles, permissions, or memberships.</summary>
    public const string Permissions = "Permissions";
}
```

`[CacheTag(AccessTags.Permissions)]` on `RoleMembership` as well (omitted above for brevity): all three
tables bump the same tag.

### 3.2 Securables

```csharp
namespace Tellma.Core.Abstractions.Access;

/// <summary>The closed set of platform action names, plus the wildcard.</summary>
public static class AccessActions
{
    /// <summary>Query, read by id, export. Implied by every other action on the same resource.</summary>
    public const string Read = "Read";
    /// <summary>Create and update, including import.</summary>
    public const string Save = "Save";
    /// <summary>Delete by ids, by query, and with descendants.</summary>
    public const string Delete = "Delete";
    /// <summary>Activate and deactivate.</summary>
    public const string Activate = "Activate";
    /// <summary>Matches every resource or every action.</summary>
    public const string Wildcard = "*";
}

/// <summary>Resource and custom action names of the Core entities.</summary>
public static class CoreSecurables
{
    /// <summary>The users resource.</summary>
    public const string Users = "core.Users";
    /// <summary>The roles resource.</summary>
    public const string Roles = "core.Roles";
    /// <summary>Custom action on <see cref="Users"/>: send or resend invitations.</summary>
    public const string Invite = "Invite";
}

/// <summary>One thing a permission can name: an action on a resource, and where its row filter
///     binds.</summary>
public sealed record SecurableDescriptor
{
    /// <summary>The resource name (D10 grammar).</summary>
    public required string Resource { get; init; }

    /// <summary>The action name.</summary>
    public required string Action { get; init; }

    /// <summary>The Queryex entity a filter on this securable binds against; null when the action
    ///     takes no filter (a settings category, for example).</summary>
    public string? FilterRoot { get; init; }

    /// <summary>For weak entities exposed as query roots: the owning resource whose grants apply,
    ///     and the navigation from this entity to the owner through which the owner's filters are
    ///     rebased. Null for top-level resources.</summary>
    public SecurableOwner? Owner { get; init; }

    /// <summary>True when a row filter can be attached.</summary>
    public bool SupportsFilter => FilterRoot is not null;
}

/// <summary>The owner link of a weak-entity securable.</summary>
/// <param name="Resource">The owning resource, e.g. <c>core.Users</c>.</param>
/// <param name="Navigation">The navigation from the weak entity to its owner, e.g. <c>User</c>.</param>
public sealed record SecurableOwner(string Resource, string Navigation);

/// <summary>Contributes securables to the registry. Implemented by the CRUD stack feature (one per
///     capability per entity stack) and by any feature with non-entity securables.</summary>
public interface ISecurableSource
{
    /// <summary>The securables this source declares.</summary>
    IEnumerable<SecurableDescriptor> GetSecurables();
}

/// <summary>Declares the securable a service method requires. The endpoint projection copies it
///     into the endpoint's authorization metadata; the registry registers it.</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class SecurableAttribute(string action, string? resource = null) : Attribute
{
    /// <summary>The action, e.g. <c>Invite</c>.</summary>
    public string Action { get; } = action;

    /// <summary>The resource; null means the declaring service's entity resource.</summary>
    public string? Resource { get; } = resource;

    /// <summary>The filter root; null (default) means the declaring service's entity for entity
    ///     services and "no filter" elsewhere.</summary>
    public string? FilterRoot { get; init; }
}

/// <summary>Marks an endpoint that any connected member may call; the startup audit accepts it in
///     place of a securable.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class MemberEndpointAttribute : Attribute;

/// <summary>Every securable the composition knows, immutable after startup.</summary>
public interface ISecurableRegistry
{
    /// <summary>All securables, ordered by resource then action.</summary>
    IReadOnlyList<SecurableDescriptor> Securables { get; }

    /// <summary>Finds a securable; null when unknown. Case-insensitive.</summary>
    SecurableDescriptor? Find(string resource, string action);

    /// <summary>The securables of one resource; empty when unknown.</summary>
    IReadOnlyList<SecurableDescriptor> ForResource(string resource);

    /// <summary>The resource names, for pickers.</summary>
    IReadOnlyList<string> Resources { get; }
}
```

### 3.3 Evaluation

```csharp
namespace Tellma.Core.Abstractions.Access;

/// <summary>The three possible answers to "may this principal perform this action".</summary>
public enum AccessOutcome
{
    /// <summary>No grant applies.</summary>
    Denied,
    /// <summary>Grants apply, every one with a row filter; <see cref="AccessDecision.Filter"/>
    ///     is their disjunction.</summary>
    Filtered,
    /// <summary>At least one grant applies without a filter.</summary>
    Unrestricted,
}

/// <summary>Where a grant came from.</summary>
public enum AccessGrantSource
{
    /// <summary>A permission of a role the principal is a member of.</summary>
    Role,
    /// <summary>A permission of a public role.</summary>
    PublicRole,
    /// <summary>A criterion a service contributed in code.</summary>
    Bespoke,
    /// <summary>The system principal.</summary>
    System,
}

/// <summary>One reason the outcome is what it is.</summary>
/// <param name="Source">Where it came from.</param>
/// <param name="RoleId">The role, for role-sourced grants.</param>
/// <param name="RoleName">The role's name, for display.</param>
/// <param name="PermissionId">The permission row, for role-sourced grants.</param>
/// <param name="Resource">The matched row's resource (may be <c>*</c>).</param>
/// <param name="Action">The matched row's action (may be <c>*</c>).</param>
/// <param name="Filter">The row filter, or null for an unrestricted grant.</param>
/// <param name="Reason">The bespoke criterion's reason key, for display; null otherwise.</param>
public sealed record AccessGrant(
    AccessGrantSource Source,
    int? RoleId,
    string? RoleName,
    int? PermissionId,
    string Resource,
    string Action,
    string? Filter,
    string? Reason);

/// <summary>A permission row that grants nothing because it no longer resolves.</summary>
/// <param name="PermissionId">The row.</param>
/// <param name="RoleId">Its role.</param>
/// <param name="Code">One of the <c>Access.*</c> drift codes.</param>
/// <param name="Diagnostics">Queryex diagnostics when the filter failed to bind; empty otherwise.</param>
public sealed record AccessProblem(
    int PermissionId,
    int RoleId,
    string Code,
    IReadOnlyList<QueryexDiagnostic> Diagnostics);

/// <summary>The answer for one securable: what, subject to which rows, and why.</summary>
public sealed record AccessDecision
{
    /// <summary>The resource asked about.</summary>
    public required string Resource { get; init; }

    /// <summary>The action asked about.</summary>
    public required string Action { get; init; }

    /// <summary>The outcome.</summary>
    public required AccessOutcome Outcome { get; init; }

    /// <summary>The composed row predicate when <see cref="Outcome"/> is
    ///     <see cref="AccessOutcome.Filtered"/>; null otherwise. Conjoin it with the caller's own
    ///     filter; never render it to text and concatenate.</summary>
    public FilterTree? Filter { get; init; }

    /// <summary>The grants that produced the outcome; empty when denied.</summary>
    public required IReadOnlyList<AccessGrant> Grants { get; init; }

    /// <summary>Rows that would have applied but no longer resolve.</summary>
    public required IReadOnlyList<AccessProblem> Problems { get; init; }

    /// <summary>True unless denied.</summary>
    public bool IsAllowed => Outcome != AccessOutcome.Denied;
}

/// <summary>A principal's resolved permission set — the unit the cache stores and the pure
///     evaluation runs over.</summary>
public sealed record UserAccess
{
    /// <summary>The shape version of this record as serialised to clients; bumped by hand when the
    ///     shape changes so a cached copy that outlived a deployment is discarded.</summary>
    public const int FormatVersion = 1;

    /// <summary>The principal.</summary>
    public required int UserId { get; init; }

    /// <summary>The tenant's <c>Permissions</c> tag the set was built under.</summary>
    public required Guid Tag { get; init; }

    /// <summary>True for the system principal; every decision is unrestricted.</summary>
    public required bool IsSystem { get; init; }

    /// <summary>The resolved rows, unresolved ones excluded.</summary>
    public required IReadOnlyList<AccessGrant> Grants { get; init; }

    /// <summary>The excluded rows and why.</summary>
    public required IReadOnlyList<AccessProblem> Problems { get; init; }

    /// <summary>The pure decision function (D13).</summary>
    public AccessDecision Decide(
        SecurableDescriptor securable,
        IReadOnlyList<AccessCriterion> bespoke);
}

/// <summary>A criterion a service contributes in code for one resource, in addition to the
///     role-derived grants.</summary>
/// <param name="Action">The action it applies to, or <c>*</c>.</param>
/// <param name="Filter">Queryex predicate text bound against the resource's filter root; authored
///     under the current language version.</param>
/// <param name="Reason">A localisable reason key for the "why" answer, e.g. <c>Access.AssignedToMe</c>.</param>
public sealed record AccessCriterion(string Action, string Filter, string Reason);

/// <summary>Contributes bespoke criteria for one resource: rows a principal may reach because of
///     a relationship the permission table cannot express ("documents assigned to me").</summary>
public interface IAccessCriteriaProvider
{
    /// <summary>The resource the criteria apply to.</summary>
    string Resource { get; }

    /// <summary>The criteria for the current principal; may be empty.</summary>
    ValueTask<IReadOnlyList<AccessCriterion>> GetCriteriaAsync(int userId, CancellationToken cancellationToken);
}

/// <summary>Decides access for the connected principal, or for a named user, from the cached
///     permission set validated against the tenant's <c>Permissions</c> tag.</summary>
public interface IAccessEvaluator
{
    /// <summary>Decides one securable for the connected principal.</summary>
    ValueTask<AccessDecision> EvaluateAsync(string resource, string action, CancellationToken cancellationToken);

    /// <summary>Decides one securable for a named user (the "can they, and why" question).</summary>
    ValueTask<AccessDecision> EvaluateAsync(int userId, string resource, string action, CancellationToken cancellationToken);

    /// <summary>The connected principal's whole resolved set, for menus, the bootstrap call, and MCP.</summary>
    ValueTask<UserAccess> GetAccessAsync(CancellationToken cancellationToken);

    /// <summary>Discards the cached set for a user; used after the connect step reports a changed tag.</summary>
    void Invalidate(int userId);
}
```

The `Access.*` codes: `Access.NotMember`, `Access.UserDeactivated`, `Access.Denied`,
`Access.PermissionsChanged`, `Access.LastAdministrator`, `Access.SelfDeactivation`,
`Access.SelfDeletion`, `Access.SystemUser`, `Access.PublicRoleMembership`, `Access.EmailFrozen`,
`Access.UnknownResource`, `Access.UnknownAction`, `Access.FilterUnsupported`, `Access.WildcardFilter`,
`Access.FilterInvalid`, `Access.FilterVersionUnsupported`, `Access.FilterVersionRequired`,
`Access.AlreadyActive` (re-inviting an active user). The host localises.

### 3.4 Connect

```csharp
namespace Tellma.Core.Abstractions.Access;

/// <summary>Why the connect step refused, or that it did not.</summary>
public enum ConnectStatus
{
    /// <summary>The principal is an active member.</summary>
    Connected,
    /// <summary>No user row carries this principal's subject.</summary>
    UnknownPrincipal,
    /// <summary>The user row is deactivated.</summary>
    Deactivated,
}

/// <summary>The connected user, as the connect statement returned it.</summary>
public sealed record ConnectedUser(
    int Id,
    UserKind Kind,
    UserState State,
    string Name,
    string? Name2,
    string? Name3,
    string? Email,
    string? Language,
    string? Calendar,
    string? TimeZone,
    Guid? ImageId,
    Guid PreferencesTag,
    DateTime? InboxSeenAt);

/// <summary>The connect step's result: the user and the tenant's tags as of the same round trip.</summary>
/// <param name="Status">Whether the principal may proceed.</param>
/// <param name="User">The user when connected.</param>
/// <param name="Tags">The tenant tags (settings, permissions, cacheable entities) read in the same
///     round trip; the shape is the settings theme's.</param>
public sealed record ConnectOutcome(ConnectStatus Status, ConnectedUser? User, TenantTags? Tags);

/// <summary>Resolves the request principal to a tenant user, stamps activity, and reads the tenant
///     tags — eagerly from the tenant endpoint filter, or as the first statements of an operation's
///     first batch.</summary>
public interface IUserConnector
{
    /// <summary>Connects the request principal in its own round trip.</summary>
    ValueTask<ConnectOutcome> ConnectAsync(CancellationToken cancellationToken);

    /// <summary>Connects a known user without a principal lookup or activity stamp; for background
    ///     scopes running as a user or as the system principal.</summary>
    ValueTask<ConnectOutcome> ConnectAsUserAsync(int userId, CancellationToken cancellationToken);

    /// <summary>Appends the connect statements to a batch under construction and returns the reader
    ///     that yields the outcome once the batch has executed.</summary>
    BatchReader<ConnectOutcome> AppendTo(IBatchBuilder batch);
}
```

The connect statement (raw SQL owned by this theme, appended through the batch API; host-side names use
the `@tm_` prefix, which the batch builder namespaces per statement so several raw statements coexist):

```sql
DECLARE @tm_userId int, @tm_isActive bit, @tm_state nvarchar(16);

SELECT @tm_userId = U.[Id], @tm_isActive = U.[IsActive], @tm_state = U.[State]
FROM [core].[Users] AS U
WHERE U.[Subject] = @tm_subject;                      -- varchar(255), binary collation

IF @tm_userId IS NOT NULL AND @tm_isActive = 1
BEGIN
    UPDATE [core].[UserStamps]
    SET [LastActiveAt] = @tm_now
    WHERE [UserId] = @tm_userId
      AND ([LastActiveAt] IS NULL OR [LastActiveAt] < DATEADD(minute, -1, @tm_now));

    IF @tm_state = N'Invited'
        UPDATE [core].[Users]
        SET [State] = N'Active', [ActivatedAt] = @tm_now
        WHERE [Id] = @tm_userId AND [State] = N'Invited';
END;

-- Result set 1: the user (zero rows = unknown principal; IsActive = 0 = deactivated)
SELECT U.[Id], U.[Kind], U.[State], U.[IsActive], U.[Name], U.[Name2], U.[Name3], U.[Email],
       U.[Language], U.[Calendar], U.[TimeZone], U.[ImageId], S.[PreferencesTag], S.[InboxSeenAt]
FROM [core].[Users] AS U
LEFT JOIN [core].[UserStamps] AS S ON S.[UserId] = U.[Id]
WHERE U.[Id] = @tm_userId;

-- Result set 2: the tenant tags (the settings theme's table; read after the user so the tags are
-- at least as fresh as the row)
SELECT [Name], [Tag] FROM [core].[TenantTags];
```

`ConnectAsUserAsync` runs the same text with `WHERE U.[Id] = @tm_userId` and without the two
`UPDATE`s. The `State` flip does not touch `ModifiedAt`: it is bookkeeping, and `State` is server-owned
so no client save can overwrite it.

The permission-set load statement (tag first, then rows):

```sql
SELECT [Tag] FROM [core].[TenantTags] WHERE [Name] = N'Permissions';

SELECT P.[Id], P.[RoleId], R.[Name], R.[IsPublic], P.[Resource], P.[Action], P.[Filter], P.[FilterLanguageVersion]
FROM [core].[Permissions] AS P
JOIN [core].[Roles] AS R ON R.[Id] = P.[RoleId]
WHERE R.[IsActive] = 1
  AND (R.[IsPublic] = 1
       OR EXISTS (SELECT 1 FROM [core].[RoleMemberships] AS M
                  WHERE M.[RoleId] = R.[Id] AND M.[UserId] = @tm_userId));
```

The tag guard, appended to every persist batch that was built from a cached permission set:

```sql
IF NOT EXISTS (SELECT 1 FROM [core].[TenantTags] WHERE [Name] = N'Permissions' AND [Tag] = @tm_expectedTag)
    THROW 51002, N'Access.PermissionsChanged', 1;
```

The last-administrator guard, appended by the User and Role services to every persist batch that writes
`core.Users`, `core.Roles`, `core.RoleMemberships`, or `core.Permissions` (after the writes, before
commit):

```sql
IF NOT EXISTS (
    SELECT 1
    FROM [core].[Users] AS U
    WHERE U.[Kind] = N'Human' AND U.[IsActive] = 1 AND U.[State] IN (N'Invited', N'Active')
      AND EXISTS (
          SELECT 1
          FROM [core].[Permissions] AS P
          JOIN [core].[Roles] AS R ON R.[Id] = P.[RoleId]
          WHERE R.[IsActive] = 1
            AND P.[Filter] IS NULL
            AND P.[Resource] IN (N'core.Roles', N'*')
            AND P.[Action]   IN (N'Save', N'*')
            AND (R.[IsPublic] = 1
                 OR EXISTS (SELECT 1 FROM [core].[RoleMemberships] AS M
                            WHERE M.[RoleId] = R.[Id] AND M.[UserId] = U.[Id]))))
    THROW 51001, N'Access.LastAdministrator', 1;
```

Platform guard statements throw in the range `51000–51999` with the validation code as the message; the
batch executor maps that range to the validation exception (§6.10) and the transaction rolls back.

The preference write (self-service; `@tm_userId` is the connected user):

```sql
UPDATE [core].[UserPreferences] SET [Value] = @tm_value WHERE [UserId] = @tm_userId AND [Key] = @tm_key;
IF @@ROWCOUNT = 0
    INSERT INTO [core].[UserPreferences] ([Id], [UserId], [Key], [Value]) VALUES (@tm_id, @tm_userId, @tm_key, @tm_value);
UPDATE [core].[UserStamps] SET [PreferencesTag] = @tm_newTag WHERE [UserId] = @tm_userId;
```

(`@tm_id` is allocator-assigned; `@tm_newTag` is `Guid.NewGuid()` from the application.)

### 3.5 Telemetry names

```csharp
namespace Tellma.Core.Abstractions.Access;

/// <summary>Instrument and tag names of the access area; the meter is the package's.</summary>
public static class AccessTelemetryNames
{
    public const string Meter = "Tellma.Core";
    public const string Decisions = "tellma.access.decisions";
    public const string CacheHits = "tellma.access.cache.hits";
    public const string CacheMisses = "tellma.access.cache.misses";
    public const string CacheEntries = "tellma.access.cache.entries";
    public const string SetBuildDuration = "tellma.access.set.build.duration";
    public const string PermissionsUnresolved = "tellma.access.permissions.unresolved";
    public const string GuardsTriggered = "tellma.access.guards.triggered";
    public const string ConnectDuration = "tellma.connect.duration";
    public const string ConnectRejections = "tellma.connect.rejections";
    public const string OutcomeTag = "outcome";
    public const string ProblemTag = "problem";
    public const string GuardTag = "guard";
    public const string ModeTag = "mode";
    public const string ReasonTag = "reason";
}
```

### 3.6 Wire shapes owned here (the web-API theme projects them)

```jsonc
// GET /{tenantId}/api/web/me
{
  "user": { "id": 42, "kind": "Human", "state": "Active", "name": "…", "name2": null, "name3": null,
            "email": "…", "language": "ar", "calendar": "UmAlQura", "timeZone": "Asia/Riyadh", "imageId": null },
  "preferences": { "tag": "…", "values": { "nav.pinned": ["core.Users", "gl.Centers"] } },
  "access": { "tag": "…", "formatVersion": 1, "isSystem": false,
              "grants": [ { "source": "Role", "roleId": 7, "roleName": "Finance", "permissionId": 91,
                            "resource": "gl.Centers", "action": "*", "filter": "IsActive = true", "reason": null } ],
              "problems": [] },
  "tags": { "Settings": "…", "Permissions": "…" }
}

// POST /{tenantId}/api/web/access/check
{ "userId": null, "securables": [ { "resource": "gl.Centers", "action": "Save" } ] }
// →
[ { "resource": "gl.Centers", "action": "Save", "outcome": "Filtered", "filter": "(IsActive = true)",
    "grants": [ … ], "problems": [ … ] } ]
```

---

## 4. Schema

All tables in schema `core`; instants are UTC `datetime2(7)`; every FK is a real constraint with an
explicit name; every sequence is `core.sq_<Table>` with `START WITH 1000`. Temporal tables use period
columns `ValidFrom`/`ValidTo` (`datetime2(7) GENERATED ALWAYS AS ROW START/END`, hidden from the UDTT)
and history table `core.<Table>History` with the default clustered index on the period; no
nonclustered index on any history table.

```
core.Users  (temporal → core.UsersHistory; UDTT core.UsersList)
  Id                    int              NOT NULL  PK  (sq_Users; 1 = system user, HasData)
  Kind                  nvarchar(16)     NOT NULL  CHECK IN ('Human','Service','System')
  Subject               varchar(255)     NULL      COLLATE Latin1_General_100_BIN2; unique filtered (WHERE Subject IS NOT NULL)
  State                 nvarchar(16)     NOT NULL  CHECK IN ('New','Invited','Active')
  Name                  nvarchar(255)    NOT NULL
  Name2                 nvarchar(255)    NULL
  Name3                 nvarchar(255)    NULL
  Email                 nvarchar(255)    NULL      unique filtered (WHERE Email IS NOT NULL); CHECK (Kind <> 'Human' OR Email IS NOT NULL)
  Language              nvarchar(16)     NULL
  Calendar              nvarchar(16)     NULL
  TimeZone              nvarchar(64)     NULL
  ContactEmail          nvarchar(255)    NULL
  ContactMobile         nvarchar(32)     NULL
  NotificationSettings  nvarchar(max)    NOT NULL  (JSON; carried as nvarchar(max) in the UDTT)
  ImageId               uniqueidentifier NULL
  InvitedAt             datetime2(7)     NULL
  LastInviteStatus      nvarchar(16)     NULL
  LastInviteError       nvarchar(1024)   NULL
  ActivatedAt           datetime2(7)     NULL
  IsActive              bit              NOT NULL  DEFAULT 1
  CreatedAt             datetime2(7)     NOT NULL
  CreatedById           int              NOT NULL  FK_Users_CreatedById → core.Users(Id)
  ModifiedAt            datetime2(7)     NOT NULL  (concurrency token)
  ModifiedById          int              NOT NULL  FK_Users_ModifiedById → core.Users(Id)
  ValidFrom, ValidTo    datetime2(7)     period (shadow)
  IX_Users_Subject      unique filtered (Subject)
  IX_Users_Email        unique filtered (Email)
  IX_Users_State        (State) INCLUDE (IsActive)     -- the admin's "pending invitations" list
```

```
core.UserStamps  (non-temporal; no UDTT)
  UserId                int              NOT NULL  PK; FK_UserStamps_UserId → core.Users(Id) ON DELETE CASCADE
  LastActiveAt          datetime2(7)     NULL
  PreferencesTag        uniqueidentifier NOT NULL  DEFAULT NEWID()
  InboxSeenAt           datetime2(7)     NULL
  (HasData: UserId = 1)
```

```
core.UserPreferences  (non-temporal; UDTT core.UserPreferencesList)
  Id                    int              NOT NULL  PK  (sq_UserPreferences)
  UserId                int              NOT NULL  FK_UserPreferences_UserId → core.Users(Id) ON DELETE CASCADE
  Key                   nvarchar(128)    NOT NULL
  Value                 nvarchar(max)    NOT NULL
  IX_UserPreferences_UserId_Key  unique (UserId, Key)
```

```
core.Roles  (temporal → core.RolesHistory; UDTT core.RolesList)
  Id                    int              NOT NULL  PK  (sq_Roles)
  Name                  nvarchar(255)    NOT NULL  unique
  Name2                 nvarchar(255)    NULL      unique filtered
  Name3                 nvarchar(255)    NULL      unique filtered
  Code                  nvarchar(50)     NULL      unique filtered
  IsPublic              bit              NOT NULL  DEFAULT 0
  IsActive              bit              NOT NULL  DEFAULT 1
  CreatedAt / CreatedById / ModifiedAt / ModifiedById   as on Users (FK_Roles_CreatedById, FK_Roles_ModifiedById)
  ValidFrom, ValidTo    period (shadow)
  IX_Roles_Name, IX_Roles_Name2, IX_Roles_Name3, IX_Roles_Code
  IX_Roles_IsPublic     filtered (WHERE IsPublic = 1)   -- the public-roles probe in the load statement
```

```
core.RoleMemberships  (temporal → core.RoleMembershipsHistory; UDTT core.RoleMembershipsList)
  Id                    int              NOT NULL  PK  (sq_RoleMemberships)
  UserId                int              NOT NULL  FK_RoleMemberships_UserId → core.Users(Id)
  RoleId                int              NOT NULL  FK_RoleMemberships_RoleId → core.Roles(Id)
  Notes                 nvarchar(1024)   NULL
  ValidFrom, ValidTo    period (shadow)
  IX_RoleMemberships_UserId_RoleId  unique (UserId, RoleId)
  IX_RoleMemberships_RoleId         (RoleId) INCLUDE (UserId)    -- the guard's and members list's direction
```

```
core.Permissions  (temporal → core.PermissionsHistory; UDTT core.PermissionsList)
  Id                    int              NOT NULL  PK  (sq_Permissions)
  RoleId                int              NOT NULL  FK_Permissions_RoleId → core.Roles(Id)
  Resource              nvarchar(128)    NOT NULL
  Action                nvarchar(32)     NOT NULL
  Filter                nvarchar(2048)   NULL
  FilterLanguageVersion int              NULL      CHECK ((Filter IS NULL) = (FilterLanguageVersion IS NULL))
  Notes                 nvarchar(1024)   NULL
  ValidFrom, ValidTo    period (shadow)
  IX_Permissions_RoleId (RoleId)
```

Tenant tags (the settings theme's table; the row this theme requires):

```
core.TenantTags
  Name   nvarchar(64)      NOT NULL  PK      -- 'Permissions' seeded by HasData with a fixed initial Guid
  Tag    uniqueidentifier  NOT NULL
```

Seeds by `HasData` (reserved band): `core.Users` id 1 and its `core.UserStamps` row; the `Permissions`
tag row. Seeds through the runtime pipeline: the `Administrators` and `Everyone` roles, the first
administrator and their membership.

---

## 5. Answers

| Open question (abridged) | Answer |
|---|---|
| "What is the better approach for the record+blobs pattern?" (user image) | Out of this theme; the user row keeps `ImageId` (server-owned, written by the image endpoint) and no fit metadata (D3). |
| "Is JSON the right shape for storing User preferences? Should pinned screens move to a distinct table so an admin can customise them?" | A key-value bag with JSON values, written only by self-service endpoints; pinned screens are the key `nav.pinned`; administrators edit the same bag from the user page. No separate table (D6). |
| "What do we store for notification settings? One JSON field?" | `ContactEmail` and `ContactMobile` as columns; channel switches and muted types as one JSON column `NotificationSettings`; push subscriptions in the inbox theme's device table, never on the user (D7). |
| "Separate SettingsVersion and PermissionsVersion, or one field?" | Separate and differently scoped: `PreferencesTag` per user on `core.UserStamps`; `Permissions` as a tenant-level tag. Security-bearing state never shares a validator with cosmetic state (D5, D15). |
| "Version, ETag, or fingerprint?" | Tag (opaque `uniqueidentifier`, equality only); "format version" for the hardcoded shape version (D1). |
| "What is the shape of inbox tracking?" | `UserStamps.InboxSeenAt` reserved for the inbox theme, which adds whatever else it needs to the same row (D5). |
| "Are the user states exhaustive?" | For what the tenant can own, yes: `New`, `Invited`, `Active` plus orthogonal `IsActive`; every finer state is a live drill-down (D4). |
| "Write-once columns like Subject and Email: two UDTTs, or the service layer?" | One UDTT; `[ServerOwned]` columns are never taken from the payload (the emitter does not `SET` them) and are written by dedicated statements; `Email` freezes after invitation by one service rule (D3). |
| "Global permissions: a system role with a hardcoded id, `IsPublic` on Roles, or a separate role-less table?" | `IsPublic` on `Role`; memberships rejected; several public roles union; `Everyone` seeded by code, not id (D9). |
| "Do we need SavedById on weak entities?" | No; the owner's `ModifiedAt`/`ModifiedById` are stamped by any child change and the histories share `ValidFrom` (D8). |
| "What is the best convention to encode the Resource a permission secures?" | The canonical schema-qualified table name (`core.Users`); `<schema>.<Name>[.<Sub>]` for non-entity resources; `*` wildcard; actions are PascalCase verbs from the registry (D10). |
| "How do we make registering securables and enforcing access control hard to forget?" | Securables derive from capabilities; endpoint metadata plus a startup audit over the endpoint table; fallback authentication policy; group-level membership policy; filters only obtainable through `AccessDecision` (D11, D12). |
| "Is 'Securable' the right word?" | Yes (SQL Server's own term for the object of a permission) (D11). |
| "Permissions invalidated by a schema change: shim, or block users?" | Neither: the row grants nothing, is reported everywhere, and never blocks the principal's other rows; a rename ships a recipe (D18). |
| "If permissions are cached, can DB calls #1 and #2 collapse?" | Yes: the connect statement rides the first batch; a tag mismatch reruns once; the persist batch carries a tag guard (D16). |
| "How do we guarantee the cache tag is bumped when a cacheable entity is updated?" (for permissions) | `[CacheTag("Permissions")]` on the three entities; the emitter bumps on every write; raw statements must declare written tables (D15). |
| "Is 'version' / 'metaversion' the accurate name?" | Tag; format version (D1). |
| "A user cannot delete or deactivate their own user, and should not strip their own admin permissions" | Self-deactivation and self-deletion are refused; the tenant-wide last-administrator invariant is checked in SQL inside the transaction; self-narrowing is allowed while another administrator remains (D17). |
| "We need an endpoint: does user X have permission to do Y on Z, subject to what filter, and why" | `POST /access/check` returning `AccessDecision`s with grants and problems; `GET /me` for the caller's whole set; MCP `whoami` and `check_access` (D19). |
| "UserService self-service endpoints for profile, preferences, notification tests" | Shapes for preferences and `/me` are here; the service and the test-notification endpoints are the Core stack theme's (D6, D19). |
| "Invite: send invitations in bulk" | The state transitions and stored columns are here (D4); the client and chunking are the Core stack theme's. |

---

## 6. Seams

### 6.1 The batch abstraction (owner: data access)

Needed: `IBatchBuilder.Raw(string sql, IReadOnlyList<SqlParameter> parameters, IReadOnlyList<string> writes, bool mayRetry)`
returning a `BatchReader<T>` that can consume several result sets (the connect statement returns two);
per-statement variable namespacing for the `@tm_` prefix; `IBatchBuilder.BumpTag(string name)` so the
preference write and any raw write can bump explicitly; automatic bumps from the emitter for entities
annotated `[CacheTag]`; a `THROW 51000–51999` → validation-exception mapping in the executor with the
message as the code; the persist batch shape `SET XACT_ABORT ON; BEGIN TRAN … COMMIT` (or an explicit
`SqlTransaction`) so a guard `THROW` rolls back; the ability to place the connect statement first in any
batch and read its outcome before the pipeline interprets later readers.

### 6.2 Entity class versus wire shape (owner: data access)

Needed: `AuditedEntity` (`Id int`, four audit columns), `WeakEntity<TOwner>` (`Id int`, the owner FK
named `<Owner>Id`), `[Temporal]`, `[ServerOwned]` (excluded from the client-driven `SET` list and from
the insert image's client values), `[NaturalKey]`, `[Searchable]`, `[CacheTag(name)]`, enum-as-string
convention (`nvarchar(16)` here), JSON complex-type mapping for `UserNotificationSettings`, and the
`[NotMapped] List<TChild>?` child-collection convention with synchronise-under-parent semantics. Position:
a single entity class with `[NotMapped]` children and the `[ServerOwned]` split is the right shape; the
risk that a client-sent server-owned value leaks is closed by the emitter never reading those columns
from the payload, not by validation.

### 6.3 One capability, declared once (owner: service pipeline)

Position: each capability contributes its securable through `ISecurableSource` — `IsActive` contributes
`(resource, Activate, filterRoot: entity)`; editable contributes `Save`; deletable contributes `Delete`;
every stack contributes `Read`. The stack feature is the single `ISecurableSource` per entity stack. A
custom action is `[Securable("Invite")]` on the service method and nothing else.

### 6.4 The Queryex schema per tenant configuration (owner: data access; settings consumes)

Needed: the tenant's `QueryexSchema` instance (rebuilt when languages change) and `FindEntity(filterRoot)`
for validation; the list restriction through a TVP (`IdList`) for the RLS pre/post count queries; and the
`FilterTree.Via(navigation, inner)` node (D14). Position: the permission set is *not* keyed on the schema
instance — filters are validated at set build against the schema current then, and a schema rebuild (a
language change) bumps the `Permissions` tag through the settings save so sets rebuild.

### 6.5 Version tags (owner: settings)

Position: tenant-level tags live in `core.TenantTags (Name, Tag)`; the `Permissions` row is this theme's;
user-level tags live on `core.UserStamps`; tags are `uniqueidentifier` generated by the application
(`Guid.NewGuid()`); the connect statement reads the whole tenant-tag table (a handful of rows) as its
second result set; the emitter bumps automatically from `[CacheTag]`.

### 6.6 Feature composition (owner: host)

Needed: a feature contribute step that can register `ISecurableSource`s and `IAccessCriteriaProvider`s,
and a startup validation slot into which the securables audit (D11) reports its diagnostics.

### 6.7 Natural keys (owner: data access)

Position: `User.Email` and `Role.Code` (fallback `Name`) declared by `[NaturalKey]`.

### 6.8 Background-task columns (owner: background tasks)

Not touched. Needed from them: jobs connect through `ConnectAsUserAsync(userId)` with the system user
(`WellKnownUsers.SystemUserId`) for built-in schedules and the owning user otherwise; the runner
evaluates permissions through the same `IAccessEvaluator`.

### 6.9 Request context (owner: host)

Needed: a scoped holder exposing `TenantId`, `IsSandbox`, the request principal id (`sub` or
`client_id`) and its source, into which the connect step writes `ConnectedUser` and `TenantTags`; a
copy of the same record for job scopes (never `AsyncLocal` as the source of truth). Session revocation
on deactivation is the host's: the connect step reports `Deactivated`, the web layer answers 403
`Access.UserDeactivated`, and the host's cookie handler signs the session out on that code.

### 6.10 Platform exceptions (owner: service pipeline; mapping: web API)

Needed: `ForbiddenException(string code)` → 403; `NotFoundException` → 404; `ValidationException`
carrying `(code, arguments, propertyPath?)` items → 422; the batch executor's `THROW 51xxx` mapping
into `ValidationException`. Position on the mapping this theme relies on: connect refusals are 403 with
codes `Access.NotMember` / `Access.UserDeactivated`; a `Denied` decision on a collection operation is 403
`Access.Denied`; a by-id read of a filtered-out row is 404; guard violations are 422 with their code.

### 6.11 Permission evaluation API (owner: this theme)

Owned: `IAccessEvaluator`, `AccessDecision`, `UserAccess`, `IAccessCriteriaProvider`,
`ISecurableRegistry`, `SecurableAttribute` (§3). Consumers: the CRUD pipeline (one `EvaluateAsync` per
operation; `decision.Filter` conjoined into every query; pre/post checks), the web layer (coarse
`Denied` in the authorization middleware via `SecurableRequirement`), the role editor and MCP.

### 6.12 Blob staging tokens (owner: blobs)

Not touched beyond `User.ImageId` being server-owned and written by the image endpoint.

### 6.13 Wire shapes (owner: web API)

Owned here: the `/me` and `/access/check` payloads (§3.6); the `access` object's `formatVersion`.

### 6.14 Telemetry names (owner: data access for the DB budget; each theme names its own)

Owned here: §3.5. The DB-call budget attributes the connect statement to the operation it rode with.

### 6.15 Notification enqueue (owner: background tasks)

Not touched; `UserNotificationSettings.Muted` is the filter the enqueue step consults.

### 6.16 Connect-call collapse (owner: this theme with the service pipeline)

Position: D16 in full — collapse by endpoint metadata, eager by default for custom endpoints, tag guard
in every persist batch, one re-run on mismatch.

### 6.17 Vocabulary

Position: plural tables, `core` schema, `int` ids, four audit columns, `datetime2(7)` UTC, "tag",
"format version", "securable", `UserPreferences` not `UserSettings`, `UserStamps` for the sibling.

---

## 7. Departures

- **Concrete default entity classes in `Tellma.Core.Abstractions`.** The architecture lists "base/abstract
  entity classes" for Abstractions; the unsealed defaults `User`, `Role`, `Permission`, `RoleMembership`
  must also live there because every module entity's `CreatedBy` navigation targets `User` and modules
  never reference `Tellma.Core`. This is a clarification of the rule's intent rather than a reversal; the
  architecture text should say "base classes and Core's own default leaves".
- **`Tellma.Core` gains a runtime area (`Tellma.Core.Access`)** as the first concrete content of that
  package; no architectural rule changes.
- **No use of SQL Server row-level security policies** — consistent with "no logic in the database";
  recorded here because it is the question every reviewer asks.
- **Endpoint verbs.** The `/me` and `/access/check` shapes assume the web surface's verb decision
  (`GET` for `/me`, `POST` for the check); if the web-API theme goes all-POST, `/me` becomes a POST and
  nothing else here changes. The architecture's "read → GET" projection is the web-API theme's to
  keep or change.
- **Audit vocabulary and the concurrency token** are stated here (four columns, `ModifiedAt` as token)
  where the architecture is silent; no departure, but the architecture should record the decision once
  the data-access theme confirms it.

---

## 8. Verification

Facts relied on from the research file `research/users-roles-permissions.md` (all verified 2026-09-01
unless noted):

- ASP.NET Core 10 authorization: the fallback policy applies only to endpoints with no authorization
  metadata; `AllowAnonymous` is absolute; `IAuthorizationRequirementData` is honoured on Minimal API
  endpoints; a custom `IAuthorizationPolicyProvider` resolves dynamic policy names; resource-based checks
  are imperative; cookie authentication returns 401/403 on `IApiEndpointMetadata` endpoints (docs dated
  2026-07-21 to 2026-07-29).
- Route-group conventions apply to every endpoint in the group (Minimal API route-handlers doc,
  2026-04-28; from `research/host-tenancy.md`).
- SQL Server native RLS mechanics and limitations (schema binding, history tables unprotected, indexed
  views impossible, dbo filtered, `SESSION_CONTEXT` parallel-plan issue with workaround) — the basis for
  rejecting it.
- Temporal tables: every `UPDATE` writes a history row even when nothing changed; period columns are
  shadow properties in EF 10; period values are UTC transaction-begin times; only a hierarchy root can be
  temporal (from `research/data-access.md`).
- `rowversion` bumps on any update; `Guid` v7 is not sequential in SQL Server order; an application
  `Guid` tag is restore-safe; EF's application-managed concurrency tokens are the documented shape.
- The identity server as implemented: invite statuses `Invited | Reinvited | Active`, free-text errors,
  prefix results on abort, delivery states and `expectsDeliveryEvents`, `sub` as a 36-character GUID
  string, dev admin subject `00000000-0000-0000-0000-000000000001`, lifecycle `Active | Orphaned |
  Disabled | Purged`, `client_credentials` with `scope=tellma_identity`; no distribution-side client
  exists.
- Permission models: Odoo (group-less access rights as the public grant; no self-deactivation; cannot
  delete the seeded admin), Salesforce sharing rules (grant-only), Dataverse cumulative roles (no
  last-admin guard, documented lockouts), Entra ID and GitHub last-owner protections.
- RCSI is on by default on Azure SQL and off on-prem; C#-side uniqueness checks are write-skew-prone;
  the stamp check inside the persist statement is sound under row versioning (from
  `research/service-pipeline.md` and `research/data-access.md`).
- `HybridCache` serialises every write and its tag stamps are process-local; a private `MemoryCache`
  with single-flight is the recommended shape (from `research/settings-cache-l10n.md`, medium
  confidence there; adopted with a review flag).

Verified independently for this proposal (2026-09-01):

- OpenID Connect Core 1.0 §2: the `sub` claim "MUST NOT exceed 255 ASCII characters in length" and "is a
  case-sensitive string" — hence `Subject varchar(255)` with a binary collation.
  https://openid.net/specs/openid-connect-core-1_0.html
- `[TableType]` and `[ExcludeFromTableType]` are defined in `Tellma.Core.EntityFrameworkCore` (repo,
  `src/core/Tellma.Core.EntityFrameworkCore/TableTypes/`), so Abstractions entity classes cannot carry
  them and the Core feature applies `HasTableType()` fluently.
- `Tellma.Core` is an empty project today (`src/core/Tellma.Core/` holds only the csproj); the access
  runtime is its first content.

Unverified or inferred:

- That `RouteGroupBuilder` metadata is visible to `AuthorizationMiddleware` exactly as endpoint metadata
  (high-confidence inference from the documented API shape; the research file marks it the same).
- That EF Core 10 maps a non-owned complex property `ToJson()` onto a `[TableType]`-derived UDTT column as
  `nvarchar(max)` exactly as spec 0001 describes for owned `ToJson()` navigations (spec 0001 lists
  "complex property" explicitly; not re-tested).
- The cost of the last-administrator guard on a tenant with tens of thousands of permission rows was
  not measured; the query is index-supported (`IX_Roles_IsPublic`, `IX_RoleMemberships_RoleId`) and runs
  once per role/user save.
- The throttle window of one minute for `LastActiveAt` is a judgment, not a measurement.
- Whether the batch executor will place transaction control in the batch text or in a `SqlTransaction`
  is the data-access theme's call; both make a guard `THROW` roll back.
