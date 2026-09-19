# T8 — Core and GL reference stacks (future spec 0017): design proposal

This file designs the first end-to-end product on the CRUD stack: `UserService`, `RoleService`, the
settings edit API, the `Tellma.Module.Gl` package with `Center` as the first tree entity, seed data,
and the reference distribution's migrations and local-dev bootstrap. It is written for a spec author
who has not read the brain dump or the other theme files. Every name below is meant to be final.

Conventions used throughout: "the pipeline" is the generic save pipeline owned by the service-pipeline
theme (spec 0014); "the batch" is the single-round-trip statement batch owned by the data-access theme
(spec 0011); "the securables registry" and "the permission evaluator" are owned by the users-roles-
permissions theme (spec 0013); "tags" are the opaque cache version values owned by the settings theme
(spec 0012). Where this file needs something from those seams it states the exact contract in §3 and
§6.

---

## 0. Lens checks

### 0.1 Distro-author simplicity (Lens A)

**How many lines does the reference distribution write to get Users, Roles and Centers end to end?**
Twelve lines of C# that are not generated:

| File | Lines | What they say |
|---|---|---|
| `Program.cs` | 3 | `builder.Services.AddTellma(t => t.AddCore().AddGl());` plus `app.MapTellma();` and `app.Run();` (the `AddTellma`/`MapTellma` shape is the host theme's; the `AddCore()`/`AddGl()` extension methods are this theme's). |
| `ReferenceDbContext.cs` | 8 | A sealed `DbContext` whose `OnModelCreating` calls `modelBuilder.ApplyTellma(this)`; no entity is named. |
| `Migrator/Program.cs` | 1 | `return await TellmaMigrator.RunAsync<ReferenceDbContext>(args);` |

Zero entity classes, zero services, zero endpoints, zero seed code. The migration files are generated.

**How many lines does a distribution write to extend `User` with one column and keep every Core
behaviour (invitation, self-service, lockout guards)?** Seven: a `public sealed class User :
Tellma.Core.User { [MaxLength(20)] public string? EmployeeNumber { get; set; } }` (five lines with
the header) and `t.AddCore(c => c.UseUser<User>())` in place of `t.AddCore()` (the `UseUser` call is
the one line that changes; the second line is the `using`). The generic `UserService<TUser, TRoleMembership>`
is closed over the distribution's leaf; nothing else changes.

**How many lines does the GL module write for `Center`?** About 95 across three files: the entity
class (about 35 lines, of which 20 are XML docs), the behaviour class with the two Center-specific
validation rules (about 45 lines), and the `AddGl()` registration (about 15 lines including the
securable and seed contributions). No SQL, no endpoint, no permission-action code, no tree code.

**How many lines does a distribution write to add a brand-new entity with `IsActive` and a tree?**
The entity class (roughly 25 lines: `[TableType] public sealed class Thing : TreeEntity<Thing>,
IActivatable, IMultilingual { … }` with its columns) and one registration line
`c.AddEntity<Thing>()`. Nothing else: the `activate` permission action, the `Activate`/`Deactivate`
service methods, the tree endpoints, the tree-recompute statement, the `IsActive` default filter, the
`Search` columns (from `[Searchable]`) and the natural key (from `[NaturalKey]`) are all projected
from the interfaces and attributes on the class.

**Is every capability declared once?** Yes, and the declaration is the interface (or attribute) on
the entity class, never a builder call that repeats it:

| Capability | Declared by | Projects |
|---|---|---|
| Activatable | `IActivatable` on the class | `IsActive` column, `activate` action, `ActivateAsync`/`DeactivateAsync`, two endpoints, default filter `IsActive = true`, the `ActiveSubtreeCount` refresh when combined with the tree |
| Tree | `TreeEntity<TSelf>` base | `ParentId`, `Parent`, shadow `Node`, computed `Level`, `SubtreeCount`, `ActiveSubtreeCount`, `GetByParentIdsAsync`, `DeleteWithDescendantsAsync`, the recompute statement appended to every mutating batch, cycle validation |
| Multilingual | `IMultilingual` on the class | `Name`/`Name2`/`Name3` gating in the Queryex schema, the "Name (E)" label convention, Excel column mapping |
| Audited | `Entity` base (every top-level entity) | four audit columns, stamping, `ModifiedAt` concurrency token |
| System-versioned | `[SystemVersioned]` on the class | `SYSTEM_VERSIONING` + history table, period shadow columns |
| Natural key | `[NaturalKey]` on a property | export-for-import and import row identity |
| Searchable | `[Searchable]` on properties | the `Search` parameter's filter |
| Children | `[Child]` on a `[NotMapped]` collection property | synchronise-under-parent save, details echo, parent `ModifiedAt` bump when a child changes |

The one thing declared twice today is the **securable label** (a localized display name for the role
editor), which lives in the resource file rather than on the class — deliberate, because labels are
localization data.

### 0.2 Tier-2 performance and operations (Lens B)

**DB round trips per operation, common case** (the connect step is folded into the first business
batch as the connect-collapse seam proposes; a stale permissions tag costs one extra round trip):

| Operation | Round trips | Batch contents |
|---|---|---|
| Users query page | 1 | connect + count query + page query |
| User details | 1 | connect + user row + memberships + related roles dictionary + row echo |
| User save (details page or import) | 2 | (1) connect + validation context: current rows of the updated users (concurrency, write-once), the referenced roles' `Id`/`IsPublic`/`IsActive`, the caller's post-save full-access check; (2) `SET XACT_ABORT ON; BEGIN TRAN` + upsert `Users` + synchronise `RoleMemberships` + `UserActivity` upsert for new users + `PermissionsTag` bump for every user whose memberships changed + RLS post-check + read-back `COMMIT` |
| Invite | 3 + ⌈n/1000⌉ HTTP | (1) connect + load invitable users; HTTP to the identity server per chunk of 1000, outside any transaction; (2) write-back of `Subject`, `State`, `InvitedAt`, `InviteError` for the whole batch in one `UPDATE … FROM @rows`; (3) none — the "you were added" notice for `Active` results goes through `IEmailSender` after the write-back |
| Invitation delivery status | 1 + ⌈n/1000⌉ HTTP | (1) connect + load subjects; HTTP per chunk; nothing written |
| Activate / deactivate users | 2 | (1) connect + guard query (self, system user, remaining full-access users); (2) `UPDATE … WHERE Id IN (SELECT Id FROM @ids)` + audit stamp + `PermissionsTag` bump (deactivation) |
| Role save | 2 | (1) connect + validation context: current rows, the securables registry is in memory, Queryex `Validate` is in memory, the caller's post-save full-access check; (2) upsert `Roles` + synchronise `Permissions` + `PermissionsTag` bump for every member of the saved roles (one statement) or the tenant-level `PublicPermissionsTag` bump when a saved role is public |
| Center save | 2 | (1) connect + validation context: current rows, the ancestor chains of every new `ParentId` (`ancestorOf`), the parent rows' `CenterType`; (2) upsert `Centers` + tree recompute (`Node`, then `SubtreeCount`/`ActiveSubtreeCount`) + read-back |
| Center activate / deactivate / delete | 2 | (1) guard; (2) update or delete + tree recompute |
| Settings category save | 2 | (1) connect + current row; (2) `UPDATE core.Settings` on that category's columns + `SettingsTag` bump |
| Set / delete my preferences | 1 | upsert or delete `UserPreferences` rows from a TVP + `PreferencesTag` bump, no validation context needed |
| Save my profile | 2 | same as user save with the column mask and the `Id = me()` criterion |
| Test email | 1 | connect only; the send is an HTTP call after the response is prepared |

**N+1s found and removed.** (a) The brain dump's per-user invitation loop becomes one HTTP call per
1000 users and one write-back statement. (b) Role save bumps every member's `PermissionsTag` with one
`UPDATE … WHERE UserId IN (SELECT UserId FROM core.RoleMemberships WHERE RoleId IN (SELECT Id FROM @ids))`,
not one statement per member. (c) Tree maintenance is one recursive CTE per batch, not one
`GetReparentedValue` per moved row. (d) Membership validation ("the role exists, is active, is not
public") is one TVP-restricted query over the distinct role ids in the payload. (e) The lockout guard
is one query that returns the callers' full-access status after the save, not a permission
evaluation per role.

**Locks held across I/O: none.** The invitation HTTP calls run before any tenant transaction opens;
the write-back is its own short transaction. The test-notification send happens after the connect
batch returns. Blob writes for the profile image precede the save transaction (the blob theme's
staging), and blob deletes follow commit. Session revocation on deactivation runs after commit.
Every transaction in this theme is one batch text with `SET XACT_ABORT ON; BEGIN TRAN … COMMIT`
inside it, so no transaction spans two round trips.

**Plan-cache friendliness.** Every list restriction is a TVP (`@ids` of `IdList`), so the statement
text is constant per operation. The tree recompute is one fixed statement per tree entity. The tag
bumps are fixed statements.

**Multi-instance safety.** Invitation is idempotent at the identity server (creates-or-gets by
email), so two instances inviting the same user concurrently write the same `Subject`. The tree
recompute is idempotent (`UPDATE … WHERE Node <> path`) and runs under the save transaction, which
holds the saved rows' locks; two concurrent Center saves serialize on the unique `Node` index.

### 0.3 Correctness, security, and maintainability (Lens C)

**Where could a stale cache leak data, and what closes it?**

| Cache | Staleness vector | Closed by |
|---|---|---|
| Per-user permissions | Role's permissions edited | The role save batch bumps `UserActivity.PermissionsTag` for every member in the same transaction as the `Permissions` rows; the next request's connect reads the tag |
| Per-user permissions | User's memberships edited | The user save batch bumps that user's `PermissionsTag` |
| Per-user permissions | Public role edited or toggled | The batch bumps the tenant-level `PublicPermissionsTag`; the per-user cache entry is keyed on both tags |
| Per-user permissions | Role deactivated or deleted | Same statement as edited: members' tags bumped |
| Per-user permissions | Securables registry changed by a deployment | The evaluator's cache key includes `ISecurableRegistry.Fingerprint`; old entries are unreachable |
| Subject → user lookup | User invited (Subject assigned) | The connect step never caches a miss; a hit is keyed on `Subject` and validated by the row read in the same batch |
| User active flag | User deactivated | Not cached: the connect statement reads `IsActive` on every request and fails closed; sessions are revoked after commit as defence in depth |
| Settings and Queryex schema | Languages changed | The settings save batch bumps `SettingsTag`; the schema cache is keyed on it |
| Preferences | Set through the KV endpoint (bypasses the entity pipeline) | The KV statement carries the `PreferencesTag` bump in the same batch text; there is no other write path to `UserPreferences` |
| Invitation delivery state | — | Never cached; always a live call |

**Which write path bypasses the tag bump?** Three writes touch `core.Users` or its siblings without
going through the entity save pipeline, and each is audited here: the connect step's `LastActiveAt`
stamp and first-sign-in state flip (bumps nothing; nothing cached depends on them), the invitation
write-back (bumps nothing; permissions and preferences are unchanged), and the preferences KV
upsert (bumps `PreferencesTag` itself). The migrator's runtime seeds go through the pipeline.

**What breaks a distribution on the next platform minor?** (a) A column added to `Tellma.Core.User`
appears in the distribution's leaf and requires a scaffolded migration — expected and mechanical.
(b) A new `CenterType` enum member changes the generated CHECK constraint (migration) and an N−1 app
that reads a row carrying the new value fails to parse the enum; the rule is that the migrator seeds
nothing with a new value, so the N−1 app never sees one before the swap. (c) A renamed securable
makes stored permissions drift; they grant nothing, are flagged in the role editor, and `RoleService`
refuses to save the role until the row is fixed. (d) A Queryex language version bump is caught by
the differential migration the Queryex spec requires; `RoleService` rejects a stored stamp below
`QueryexLanguage.Minimum` with a validation error naming the row.

**Fail-closed points.** Unknown resource or action in a permission grants nothing; a filter that
no longer compiles grants nothing for that row; a deactivated user is refused at connect; a
self-service save can only ever touch the caller's row through the `Id = me()` criterion composed
by the evaluator, not by the service; the system user (`Id = 1`) is refused by every mutating
operation of `UserService`.

**Schema evolution.** `Users` gains columns additively; `State` is a string enum with a CHECK
constraint so an old value never becomes unreadable; the history tables inherit column additions
automatically. Dropping `IsLeaf` and `SavedById` before the first migration ships avoids a contract
change later.

---

## 1. Critique of the brain dump for this theme

**The general design is right; the entities are under-specified where it matters.** The three-entity
scope (User, Role, Center) is the correct minimum: one temporal top-level entity with a child
collection (User + RoleMemberships), one with stored expressions to validate (Role + Permissions), and
one tree. The critique is about the columns and the missing flows.

1. **`User` mixes four lifetimes in one temporal row.** Identity facts (`Subject`, `Email`), profile
   facts (`Name*`, image, contact), per-request bookkeeping (`LastActive`, version stamps, inbox
   tracking) and notification preferences. The brain dump already sees the churn problem; the fix is
   not "make the User table non-temporal" but to move every column written by something other than
   an explicit user action into a sibling non-temporal table (`core.UserActivity`, D3). What remains
   in `Users` is written only by saves and actions, which makes `ModifiedAt` a sound concurrency token.

2. **`SavedAt`/`SavedById` next to `ValidFrom`/`ValidTo` is a redundant vocabulary.** `ValidFrom` is
   engine-written and equals the last write time; `SavedAt` duplicates it. But dropping `SavedAt` and
   keeping only the period loses the *creation* time and creator once history is trimmed, and it
   forces a different entity contract for temporal and non-temporal entities. One vocabulary wins:
   `CreatedAt`, `CreatedById`, `ModifiedAt`, `ModifiedById` on every top-level entity, with
   system-versioning as an additive attribute (D1). `Center` already uses this vocabulary in the
   brain dump; `User` and `Role` should too.

3. **`SavedById` on weak entities is redundant.** A child changes only through its parent's save,
   which stamps the parent's `ModifiedById`; with the child table system-versioned, "who changed this
   permission row" is the parent's `ModifiedById` at the child's `ValidFrom`. Drop it (D1).

4. **The user state model conflates two axes.** `State` (New → Invited → Active) and `IsActive`
   (enabled/disabled) are orthogonal, and naming the terminal invitation state `Active` next to an
   `IsActive` flag guarantees confusion in filters (`State = 'Active' and IsActive = true`). Rename the
   terminal state to `Joined` (D2). The list is exhaustive for the tenant's own state; every finer
   state (queued, sent, bounced, accepted) belongs to the identity server and is read live, never
   stored, because the tenant cannot keep it fresh.

5. **The invitation flow is described as a state machine but not as a transaction boundary.** The
   invite call is an HTTP call to another service; it must run outside any tenant transaction, and
   its per-user results must be written back in one statement. The brain dump does not say what a
   result of `Active` (no email sent) means for the tenant: the tenant must send its own "you were
   added" message, because the identity server cannot tell a new membership from an old one (D6).

6. **`ImageFitJson` is a symptom of doing image fitting at read time.** Fit the image at upload time
   (the blob theme's server-side processing), store only `ImageId`, and let the immutable blob id be
   the etag. The brain dump's question "here or in a centralized blob metadata table" dissolves: there
   is no metadata to centralize (D2, consuming the blob theme).

7. **`PushSettings` "what shape" — not a column.** Push subscriptions are per device, arrive and expire
   independently of saves, and are many per user; they are rows in a device table owned by the inbox
   theme, not a JSON column on a temporal row (D5).

8. **`UserSettings` is the wrong name and the wrong key.** It is a per-user preference bag, and
   `(UserId, Key)` is its natural primary key; an `Id` column adds a sequence and an index for
   nothing. Rename to `UserPreferences` with a composite key (D4). The brain dump's question about
   pinned screens being a separate table admin-manageable for less tech-savvy users is answered "not
   now": the bag is self-service; admin-curated defaults are a tenant setting key later.

9. **`Center` carries two derivable columns.** `IsLeaf` is `SubtreeCount = 1`; `Level` is
   `Node.GetLevel()`. Keep `SubtreeCount`/`ActiveSubtreeCount` (they save a correlated subquery per
   node in the tree view), drop `IsLeaf`, and make `Level` a persisted computed column so Queryex
   sees it as a plain property — which also makes the requested `level()` engine function unnecessary
   (D13, D14).

10. **`CenterType = Service | Operation | Sale` has no grouping node.** A tree of posting centres needs
    non-posting parents; the legacy model had `Abstract` and `BusinessUnit` and the invariant that
    only those may have children. Without it the reference module cannot express "Operations →
    Plant A → Line 1" (D13).

11. **The "Extensibility" note asks for interface-driven services but the architecture rejects
    paired interfaces per entity.** The reconciliation is generic services closed over the leaf type,
    with behaviour supplied by composition (`IEntityBehavior<T>`) rather than inheritance. This also
    answers "base class vs composition" for Core services: `UserService<TUser, TRoleMembership>` is a
    Core class whose Core-specific behaviour is fixed and whose leaf type is the distribution's (D10).

12. **The settings edit question ("patch vs load-and-save") is a false dichotomy once settings are
    categorized.** A category is a small typed DTO over a subset of the single-row table; saving a
    category whole through the ordinary pipeline with a column mask is both a patch (of the row) and a
    load-and-save (of the category), with `ModifiedAt` concurrency and the `SettingsTag` bump for
    free. No JSON Patch, no merge-patch package, no field mask (D11).

13. **Gaps.** No delete policy for users (a joined user is referenced by audit FKs everywhere); no
    statement of who creates the first admin in a deployed tenant (the migrator cannot call the
    invite API); no re-invite rule (the link lives 7 days); no rate limit on test notifications; no
    statement that `Email` becomes server-owned once a `Subject` exists; no seed-data contributor
    shape for modules ("baseline permissions and roles" are promised by the architecture); no naming
    for the weak-entity uniqueness constraints (`(UserId, RoleId)`, `(RoleId, Resource, Action, Filter)`);
    nothing on how a module registers a securable.

14. **Internally inconsistent.** "Save admits a single entity" versus "every API is bulk shaped";
    `core.User` singular versus `gl.Invoices` plural in the architecture; `Role.Name2` "unique when
    not null" while `User.Name2` has no uniqueness rule (correct, but should be stated as a per-entity
    decision, not a multilingual convention); `RoleMembership` is "edited and saved together with the
    User" yet the lockout guard is described under roles.

---

## 2. Decisions

### D1 — Vocabulary: plural tables, four audit columns everywhere, temporal as an attribute, no `SavedById` on children

**Decision.** Tables are plural and schema-qualified: `core.Users`, `core.UserActivity`,
`core.UserPreferences`, `core.UserNotificationPreferences`, `core.RoleMemberships`, `core.Roles`,
`core.Permissions`, `gl.Centers`. Entity classes are singular. Sequences are `core.sq_Users`,
`core.sq_Roles`, `core.sq_RoleMemberships`, `core.sq_Permissions`, `gl.sq_Centers`. Every top-level
entity carries `CreatedAt datetime2(7)`, `CreatedById int`, `ModifiedAt datetime2(7)`,
`ModifiedById int` (FKs to `core.Users`), stamped by the emitter. `ModifiedAt` is the concurrency
token; the save payload echoes it and the persist batch checks `(Id, ExpectedModifiedAt)` through a
standalone TVP. System-versioning is opted into per class with `[SystemVersioned]`; `Users`, `Roles`,
`RoleMemberships` and `Permissions` are system-versioned (history tables `core.UsersHistory`,
`core.RolesHistory`, `core.RoleMembershipsHistory`, `core.PermissionsHistory`); `Centers` is not.
Weak entities carry no audit columns; any change to a child stamps the parent's `ModifiedAt`/`ModifiedById`.
The reserved id band is `1..999` on every table; every sequence starts at `1000`.

**Rationale.** Plural matches the architecture's `gl.Invoices` and the legacy schema; one audit
vocabulary keeps creation cheap to answer and the entity contract uniform; `ModifiedAt` as the token
works because everything written by non-user paths lives in sibling tables (D3); authorization
history (`Roles`, `Permissions`, `RoleMemberships`) is a compliance need; `Centers` history would be
churned by the subtree counters on every insert.

**Rejected.** `rowversion` (bumps on bookkeeping writes and cannot ride the UDTT as a comparable
value); `SavedAt`/`SavedById` + period (loses creation, two vocabularies); temporal `Centers`
(counter churn); singular table names (contradicts the architecture and every FK naming precedent).

**Confidence.** High. **Review flag.** Whether `Centers` should be temporal anyway for audit of
reference data — plausible, at the cost of a history row per ancestor per insert.

### D2 — `User`: columns, the state model, write-once rules, typed preferences

**Decision.** `core.Users` holds identity, profile and admin-set facts only (full column list in §4).
The membership state is `State nvarchar(16) NOT NULL CHECK IN ('New','Invited','Joined')`, C# enum
`UserState { New, Invited, Joined }`:

- `New`: created in the tenant, `Subject IS NULL`, never invited.
- `Invited`: the invite API returned `Invited` or `Reinvited`, or `Active` (identity already held a
  credential); `Subject` is set, `InvitedAt` is set. Re-invite is allowed while `Invited` (the link
  lives seven days) and is the same operation.
- `Joined`: the user's `sub` has been seen by the connect step at least once in this tenant;
  `JoinedAt` is set. The transition is performed by the connect statement itself:
  `UPDATE core.Users SET State = 'Joined', JoinedAt = @now, ModifiedAt = @now, ModifiedById = Id WHERE Subject = @sub AND State = 'Invited'`
  (zero rows in the steady state, so no history churn).

`IsActive` is orthogonal (enabled/disabled) and is the activatable capability. The system user is
`Id = 1`, `State = New`, `IsActive = 1`, `Subject NULL`, `Email = 'system@tellma.invalid'` (RFC 2606
reserved TLD; never mailed), seeded with `HasData`; every mutating `UserService` operation refuses it.

Write-once and server-owned rules: `Subject`, `State`, `InvitedAt`, `JoinedAt`, `InviteError`,
`ImageId` are `[ServerOwned]` — the pipeline overwrites whatever the client sent with the stored
value (or null for inserts). `Email` is client-settable while `State = New` and fixed afterwards;
sending a different `Email` for an invited user is the validation error `Users_EmailIsFixedAfterInvitation`
on `Email`, not a silent reset, because a silent reset hides a client bug. Uniqueness of `Email` and
`Subject` is enforced by unique indexes; errors 2601/2627 map to field-level validation errors.

Typed preference columns on `Users` (server-read, admin-settable, self-editable): `PreferredLanguage
nvarchar(10)` (BCP 47, required; the invitation `locale` and the language of every message to the
user), `PreferredCalendar nvarchar(16) NOT NULL` (a calendar code from the settings theme's
registry; the calendar of dates in messages to the user), `TimeZone nvarchar(64) NULL` (IANA id;
null means the tenant's zone), `Gender nvarchar(8) NULL` (`female`|`male`; the invitation's
grammatical gender and the `select` argument of gendered message templates). Everything the server
never reads (pinned screens, grid column widths, dismissed tours) lives in the KV bag (D4).

**Rationale.** A stored `State` is the column the users page filters and groups on; Queryex cannot
derive it from `Subject`/`JoinedAt`, so it earns its keep. Making the connect statement perform the
`Joined` flip costs nothing on the steady path and keeps the transition in the one place that knows
a sign-in happened. The typed/KV split criterion is mechanical: the server reads it → column.

**Rejected.** Deriving `State` (unfilterable); a separate create-UDTT for write-once columns (two
row images, the rejected `ForSave` pattern); storing identity delivery states in the tenant (cannot
be kept fresh); keeping `Email` editable after invitation (the identity server owns the mailbox).

**Confidence.** High on the state model and write-once rules; medium on `Gender` and `TimeZone`.
**Review flag.** `Joined` versus `Active` as the terminal state name; whether `Gender` belongs in the
first release.

### D3 — The sibling table `core.UserActivity`

**Decision.** One non-temporal row per user, primary key `UserId` (FK to `Users`, cascade delete),
inserted by the user save batch when a user is created and by the bootstrap seed. Columns:
`LastActiveAt datetime2(7) NULL` (stamped by the connect step on user-initiated calls only),
`PermissionsTag uniqueidentifier NOT NULL`, `PreferencesTag uniqueidentifier NOT NULL`,
`InboxSeenAt datetime2(7) NULL`, `InboxUnreadCount int NOT NULL DEFAULT 0` (the inbox theme owns the
last two and may replace them). Tags are app-generated `Guid.NewGuid()` values (restore-safe, no
ordering assumption). The users-roles-permissions theme owns the table's name and the connect
statement; this theme requires exactly these columns and the two bump statements in §3.5.

**Rationale.** Separating per-request bookkeeping from the temporal row is what makes `Users`
history meaningful and `ModifiedAt` a sound token. Two tags rather than one because a permissions
recompute is expensive and security-relevant while a preferences reload is neither; sharing a tag
would recompute permissions on every pinned-screen change.

**Rejected.** A single `Version` column (couples the two); `bigint` monotonic tags (a restore
rewinds them below values instances may still hold); keeping tags on `Users` (history churn).

**Confidence.** High. **Review flag.** Table name (`UserActivity` versus `UserStamps`).

### D4 — `core.UserPreferences`: a composite-keyed KV bag, JSON values, self-service only

**Decision.** `core.UserPreferences (UserId int, Key nvarchar(128), Value nvarchar(max))`, primary
key `(UserId, Key)`, no `Id`, no audit, not temporal. `Key` is `[a-z0-9.-]+` (vscode-style dotted
keys; validated by regex); `Value` is a JSON text the server never interprets. Two self-service
operations: `SetMyPreferencesAsync(IReadOnlyList<UserPreferenceEntry>)` (upsert from a
`[TableType] UserPreferenceEntry(Key, Value)` TVP in one statement) and
`DeleteMyPreferencesAsync(IReadOnlyList<string> keys)`; both append the `PreferencesTag` bump to the
same batch text. Limits: 200 keys per user, 64 KB per value, enforced by validation. Pinned screens
are one JSON value under key `nav.pinned`; there is no admin-managed table.

**Rationale.** The bag is read whole into the UI cache and validated by one tag; a composite key is
the natural identity and removes a sequence. JSON keeps the schema out of the server.

**Rejected.** A surrogate `Id` (nothing references the row); a typed preferences table (the UI
changes faster than migrations); admin-editable pinned screens now (a tenant setting key later).

**Confidence.** High.

### D5 — Notification settings: typed contact columns, a per-type preference child, devices deferred

**Decision.** `Users.ContactEmail nvarchar(255) NULL` (null means use `Email`) and
`Users.ContactMobile nvarchar(32) NULL` (E.164, validated) are typed columns. Per-type opt-outs are
rows of the weak entity `core.UserNotificationPreferences (Id, UserId, NotificationType nvarchar(64),
Email bit, Sms bit, Push bit)`, unique `(UserId, NotificationType)`, saved as a `[Child]` collection of
`User` through both the admin save and the self-service profile save; a missing row means the
notification type's defaults, and types the inbox theme marks as unmutable are rejected by validation
if a row tries to mute them. Push device subscriptions are not on `Users` and not in this theme: the
inbox theme owns a `core.UserDevices` table with its own endpoints.

**Rationale.** Addresses are read by the sender on every send and belong in typed columns; per-type
preferences are a small matrix that admins should be able to see and edit for a user; devices churn
independently of saves.

**Rejected.** One JSON column for everything (unqueryable, admin-invisible, mixes lifetimes).

**Confidence.** Medium; the inbox theme may reshape the child. **Review flag.** Whether the matrix
should be tenant-wide policy plus per-user overrides.

### D6 — Invitation through the identity server

**Decision.** `UserService.InviteAsync(IReadOnlyList<int> ids)` requires the `User`/`invite`
permission (filter-capable). Flow:

1. Batch 1 (connect folded): load the requested users through the RLS-filtered query restricted by
   `@ids` (TVP), selecting `Id, Email, Name, PreferredLanguage, Gender, State, IsActive, Subject`.
   Validation: every id found (else 404 semantics per the pipeline's by-ids rule); `IsActive = 1`;
   `State ∈ {New, Invited}`; not the system user; not the caller (an admin cannot invite themselves —
   they are already joined). Failures are per-id validation errors and abort the whole call (the UI
   multi-select is small; import never invites).
2. HTTP: `IUserInvitationClient.InviteAsync(items, ct)` in chunks of 1000, `client_credentials` with
   `scope=tellma_identity` and `resource=<issuer origin>` (token cached until 60 s before expiry),
   `returnUrl = <distribution origin>/{tenantId}` (must equal the registered origin). No tenant
   transaction is open. A transport failure of a chunk after some chunks succeeded returns the
   successful prefix's write-back plus a `502`-mapped `ExternalServiceException` naming the failed
   range; the operation is safe to repeat.
3. Batch 2: one `UPDATE core.Users SET Subject = r.Subject, State = 'Invited', InvitedAt = @now,
   InviteError = r.Error, ModifiedAt = @now, ModifiedById = @me FROM core.Users u JOIN @results r ON r.Id = u.Id WHERE r.Error IS NULL`
   plus `UPDATE … SET InviteError = r.Error … WHERE r.Error IS NOT NULL` from a `[TableType]
   UserInviteResultRow(Id, Subject, Error)` TVP. A per-user error does not change `State` or
   `Subject`.
4. After batch 2: for every result with identity status `Active`, enqueue the tenant's own
   "You have been added to {tenant}" email through `IEmailSender` (audience `Transactional`), because
   the identity server sent nothing. This is a post-commit side effect; failure is logged, not surfaced.

Response: `InviteResult { Id, Outcome, Error }` with `InviteOutcome { Invited, Reinvited, AlreadyRegistered, Failed }`.
`GetInvitationStatusAsync(ids)` calls the delivery-status API with the users' subjects (chunks of
1000) and returns `InvitationStatus { Id, State, ExpectsDeliveryEvents, SentAt, UpdatedAt, Reason }`
where `State` is the identity enum mapped 1:1 plus `Unknown` for `NotFound` (the invitation was
raised by another client or the identity store was reset); nothing is written.

**Rationale.** The identity API is bulk and idempotent; the only correct transaction shape is
HTTP-first, then one write-back. The `Active` outcome must produce a tenant email or the user is
never told.

**Rejected.** Persisting delivery states (cannot be kept fresh); inviting inside the save pipeline as
a side effect (couples an HTTP call to every user save); a background task for invitations (adds a
round trip and a delay to the common case; the identity server already has its own sweep).

**Confidence.** High. **Review flag.** Whether `InviteError` (free English text from the identity
server) should be persisted or only returned; persisted here because the admin's later
troubleshooting view needs it.

### D7 — Self-service: profile, image, preferences, test notification

**Decision.** Four operations on `UserService`, none requiring a permission (the criterion is
`Id = me()`, composed by the permission evaluator as a bespoke criterion, never by the service):

- `GetMyProfileAsync()` → the caller's `User` in details shape (memberships read-only, related roles).
- `SaveMyProfileAsync(TUser profile)` → the ordinary save pipeline with the column mask
  `{ Name, Name2, Name3, ContactEmail, ContactMobile, PreferredLanguage, PreferredCalendar, TimeZone, Gender, ImageId }`
  and the child `NotificationPreferences`; every other column is taken from the stored row.
  `ImageId` carries a blob staging token (the blob theme's seam) which the pipeline confirms in the
  save transaction; the previous blob is deleted after commit.
- `SetMyPreferencesAsync` / `DeleteMyPreferencesAsync` (D4).
- `SendTestNotificationAsync(NotificationChannel channel)` → `Email` sends a localized test message to
  `ContactEmail ?? Email` through `IEmailSender` (audience `Transactional`) and returns nothing;
  `Sms` and `Push` return `501`-mapped `NotSupportedException` until their connectors exist.
  In-memory rate limit: one per channel per user per minute.

**Rationale.** Reusing the pipeline with a column mask means self-service gets concurrency, audit,
validation and tag bumps without a second code path; the mask is the same seam the settings API
needs (D11).

**Rejected.** A separate `MyProfile` DTO and hand-written update (a second save path to keep in
sync); allowing self-service to edit `Email` (identity-owned).

**Confidence.** High.

### D8 — Activation, deactivation, deletion, and the lockout guards

**Decision.** `ActivateAsync(ids)`/`DeactivateAsync(ids)` come from the activatable capability
(permission action `activate`). `UserService` adds guards as validation in the guard round trip:
the caller's own id is refused (`Users_CannotDeactivateSelf`); `Id = 1` is refused; after the
operation at least one user other than the system user must remain with `IsActive = 1`,
`State = Joined`, and an unfiltered `all`/`all` permission through an active, non-public role
(`Users_LastAdministrator`). Deactivation's persist batch bumps `PermissionsTag` and, after commit,
calls `IUserSessionTerminator.TerminateAsync(tenantId, subjects)` (revokes the BFF sessions and
closes SignalR connections; the host and inbox themes implement it). The connect step refuses
inactive users regardless, so the terminator is defence in depth.

`DeleteAsync(ids)` is permitted only for users with `State = New` (`Users_OnlyNewUsersCanBeDeleted`);
everyone else is deactivated. This avoids FK failures from audit columns and keeps history readable.
Self-delete is refused by the same rule that refuses self-deactivation.

For roles: `RoleService` refuses deactivating or deleting a role when the post-operation state leaves
no full-access administrator, and refuses a role save (and a user save of memberships) that strips
the *caller's* own full access when they had it before (`Roles_CannotRemoveOwnAdministratorAccess`).
Both checks are one query over the post-save picture (§3.6).

**Rationale.** The guards must be evaluated against the post-save state in the same round trip as
the other validation context, or two admins can lock the tenant out by racing; the "at least one"
rule is the tenant-level invariant, the "not yourself" rule is the UX invariant.

**Rejected.** Hard-coded administrator role id (a seeded, editable row should not be special-cased by
id); allowing delete of joined users (audit FKs).

**Confidence.** High.

### D9 — `Role`, `Permission`, and `RoleService` validation

**Decision.** `core.Roles`: `Name` unique, `Name2`/`Name3` unique where not null, `Code` unique
where not null, `IsPublic bit NOT NULL`, `IsActive`, four audit columns, system-versioned.
`core.Permissions`: `RoleId`, `Resource nvarchar(128)`, `Action nvarchar(32)`, `Filter nvarchar(max) NULL`,
`FilterLanguageVersion int NULL`, `Notes nvarchar(1024) NULL`, system-versioned, no audit columns.
`core.RoleMemberships`: `UserId`, `RoleId`, `Notes`, unique `(UserId, RoleId)`, system-versioned,
child of `User`.

Public permissions are roles with `IsPublic = 1`: their permissions apply to every active user;
a membership pointing at a public role is the validation error `RoleMemberships_RoleIsPublic`.
`Resource` is the entity's logical name as registered in the securables registry — the singular
PascalCase class name (`User`, `Role`, `Center`) — or a dotted non-entity securable (`Settings.General`),
or `all`. `Action` is a lowercase kebab token (`read`, `save`, `delete`, `activate`, `invite`) or `all`.

`RoleService` validation per permission row, in order, each failure a field-level error on
`Permissions[i].<Field>`: (1) `Resource` is `all` or registered; (2) `Action` is `all` or one of the
securable's actions; (3) `Filter` is null unless the securable `SupportsFilter`; (4) `Filter`, when
present, passes `QueryexEngine.Validate(text, new ValidationOptions { Schema = tenantSchema, Root = securable.FilterRoot, Mode = QueryexMode.Predicate, LanguageVersion = stamp, HasUser = true, Limits = permissionLimits })`
where `stamp` is the stored `FilterLanguageVersion` when the text is unchanged from the stored row
and `QueryexLanguage.Version` when it is new or changed — so an unchanged filter keeps its stamp
and a changed one is re-minted; a stored stamp below `QueryexLanguage.Minimum` is the error
`Permissions_FilterMustBeReauthored`; every Queryex diagnostic becomes a validation error carrying
the diagnostic code, arguments, and span; (5) two rows with identical `(Resource, Action, Filter)`
are the error `Permissions_Duplicate` (different filters on the same pair are allowed; they OR).
A role with `IsPublic = 1` cannot have memberships (checked from the current memberships in the
validation context: `Roles_PublicRoleHasMembers`).

The persist batch bumps `PermissionsTag` for every member of every saved role, and the tenant-level
`PublicPermissionsTag` when any saved role is or was public.

**Rationale.** `Validate` is the one binder the evaluator will compile with, so validation cannot
drift; the stamp rule follows the Queryex spec's "the stamp is minted at Validate". `IsPublic` reuses
the entire role editor and this validation for public permissions.

**Rejected.** A seeded well-known public role (cannot have two, id-special-cased); a separate
role-less permissions table (a second editor and a second validator); re-stamping unchanged filters
to the current version (asserts a meaning-equivalence nobody checked).

**Confidence.** High on validation; medium on `IsPublic` (owned by the users-roles-permissions theme).

### D10 — Services by composition: `EntityService<T>` + `IEntityBehavior<T>`; Core services closed over the leaf

**Decision.** The pipeline theme ships one generic `EntityService<TEntity>` in `Tellma.Core` that
implements every standard operation for any registered entity, and calls an optional
`IEntityBehavior<TEntity>` (preprocess, validate with context requests, transactional side effects,
post-commit side effects) supplied by the package that owns the entity. Modules implement behaviours
against `Tellma.Core.Abstractions` only and never subclass a Core class. Core's own services are
`UserService<TUser, TRoleMembership>` and `RoleService<TRole>` in `Tellma.Core`: they wrap the
generic service (composition), add the custom operations (D6–D8), and are registered as
`IUserService<TUser>`/`IRoleService<TRole>` by `AddCore()`; the default closes them over
`Tellma.Core.User`/`Role`. `CenterService` does not exist as a class: `Tellma.Module.Gl` registers
`Center` with `CenterBehavior : IEntityBehavior<Center>` and consumers use
`IEntityService<Center>`. A distribution overrides the leaf with `c.UseUser<MyUser>()`,
`c.UseRole<MyRole>()`, `g.UseCenter<MyCenter>()`.

**Rationale.** Composition gives "reuse `UserService` logic with an extended `User`" without a
paired interface per entity; the generic parameter is the only per-entity type the service needs.

**Rejected.** An abstract `CrudService<T>` to inherit (locks modules into a Core base class, which
the dependency rules forbid); non-generic services over `User` with runtime casts.

**Confidence.** High. **Review flag.** Whether `UserService` should be reachable as
`IEntityService<TUser>` plus an `IUserOperations<TUser>` for the extras instead of one interface.

### D11 — Settings edit API: per-category load-and-save with a column mask

**Decision.** `ISettingsService` exposes `GetSettingsAsync()` (every category, public to any
authenticated user, cached by `SettingsTag`) and one `Save<Category>Async(<Category>Settings dto)`
per category. The reference distribution has two categories over the single-row `core.Settings`
table: `GeneralSettings { TenantName, TenantName2, TenantName3, ModifiedAt }` and
`LocalizationSettings { PrimaryLanguage, SecondaryLanguage, TernaryLanguage, PrimaryCalendar, SecondaryCalendar, TimeZone, ModifiedAt }`.
Each save runs the standard pipeline on the `Settings` entity with the category's column mask
(the mask is the DTO's property set), `ModifiedAt` concurrency on the row, validation (languages
distinct, in the distribution's catalogue; calendars in the registry; time zone valid), and the
persist batch bumps `SettingsTag`. The permission resource is `Settings.<Category>` with the single
action `save`; reads need no permission. Ad-hoc entries (`core.SettingEntries (Category, Key, Value, …)`,
owned by the settings theme) get `SetSettingEntriesAsync(category, entries)` and
`DeleteSettingEntriesAsync(category, keys)` under the same `Settings.<Category>`/`save` resource,
upserting from a TVP with the tag bump in the batch.

**Rationale.** Categories bound the payload, the resource and the DTO at once; a full-category save
is a patch of the row without patch machinery; the column mask is shared with self-service (D7).

**Rejected.** `JsonPatchDocument<T>` (operation-based attack surface, in-place mutation, awkward
with source-generated JSON); merge-patch (no first-party support; cannot distinguish "set null" from
"omit" without a field mask); one endpoint for all settings (one resource, so every settings admin
can edit everything).

**Confidence.** High.

### D12 — The GL module package

**Decision.** Two projects: `src/module/gl/Tellma.Module.Gl.Abstractions/` (references only
`Tellma.Core.Abstractions`; contains `Center<TCenter>`, `Center`, `CenterType`, the `GlSecurables`
constants, the `IGlBuilder` options) and `src/module/gl/Tellma.Module.Gl/` (references
`Tellma.Module.Gl.Abstractions` only; contains `CenterBehavior`, `GlFeature`, `AddGl()`, the resource
files, the seed contributor). Tests under `test/module/gl/Tellma.Module.Gl.Tests/`. `taxonomy.json` is
created at the repo root with `{"modules":["Gl","Sales","Procurement","Inventory","Hr","Manufacturing"],"compliance":["Sa","Et","Ifrs","UsGaap"]}`
(the module names in package-segment form; `Gl` not `GL`). The module's feature declares
`Requires = [CoreFeature]`, contributes the `Center` entity, the `Center` securable with actions
`read|save|delete|activate` and filter root `Center`, and no seed rows.

**Rationale.** The architecture's package rules, applied literally; the feature contract is the host
theme's minimal-fidelity shape.

**Confidence.** High. **Review flag.** `Gl` versus `GL` as the registry value (PascalCase segment
form says `Gl`).

### D13 — `Center`: columns, `CenterType`, the parent-type rule

**Decision.** `gl.Centers` (full list in §4): `Id`, `ParentId` (self FK, nullable, `NO ACTION`),
`CenterType nvarchar(32) NOT NULL` with CHECK over the enum names, `Name` (required), `Name2`,
`Name3`, `Code nvarchar(50) NOT NULL` unique and `[NaturalKey]`, `IsActive`, four audit columns,
`Node hierarchyid NOT NULL` (EF shadow property, unique index), `Level AS Node.GetLevel() PERSISTED`
(mapped as a read-only `short Level` with `[DatabaseGenerated(Computed)]`), `SubtreeCount int NOT NULL`,
`ActiveSubtreeCount int NOT NULL`. `IsLeaf` is dropped. `CenterType` is the C# enum
`CenterType { Abstract, BusinessUnit, Service, Operation, Sale }` stored as its name (the
data-access theme's enum-as-string convention, which also emits the CHECK constraint). Validation in
`CenterBehavior`: a row whose `ParentId` points at a centre whose `CenterType ∉ {Abstract, BusinessUnit}`
is the error `Centers_ParentMustBeGroupingCenter` on `ParentId`; changing a centre's type to a
non-grouping type while it has children (from the validation context's child count) is
`Centers_GroupingCenterHasChildren` on `CenterType`; a cycle is `Centers_ParentCreatesCycle` on
`ParentId` (D14). `[Searchable]` on `Name`, `Name2`, `Name3`, `Code`.

**Rationale.** The grouping/posting split is what makes a centre tree usable; `Abstract`/`BusinessUnit`
are the legacy grouping types and map to "no posting" and "profit/investment centre". `Level` as a
persisted computed column is visible to Queryex as a property, which is what `level()` was for.
`Code` required makes the natural key total, so export-for-import always round-trips.

**Rejected.** A `gl.CenterTypes` lookup table (extensible by distributions, but a table, a seed and a
service for five values; revisit if a distribution needs its own types); the three-value set
(no grouping node); `IsLeaf` stored (derivable); `Level` stored (derivable, and the computed column
is indexable).

**Confidence.** Medium on the value set, high on the rest. **Review flag.** The five values versus the
legacy thirteen; `Code` required versus nullable-unique.

### D14 — Tree maintenance: one recompute statement appended to every mutating batch; cycles validated in C#

**Decision.** The tree capability (data-access theme emits, this theme is the first consumer)
appends the statement in §3.7 after the upsert/delete/activate statements of every batch that
mutates a tree entity. It recomputes `Node` for the whole table from `ParentId` with a recursive CTE
and `ROW_NUMBER() OVER (PARTITION BY ParentId ORDER BY Id)`, updating only rows whose `Node` differs,
then recomputes `SubtreeCount`/`ActiveSubtreeCount` with an `IsDescendantOf` self-join, updating
only rows whose counts differ. `Node` is excluded from the UDTT (shadow property; nothing binds it).
Cycle validation runs in C# before persisting: the validation context loads
`ancestorOf(Id, @newParentIds)` rows (`Id, ParentId`) for every updated row whose `ParentId` changed;
the validator walks from each row's new parent up through the in-memory payload first and the loaded
chain second and fails when it meets the row itself.

**Rationale.** Whole-table recompute is O(N) once per save with N in the hundreds to low thousands
for reference trees, deterministic, collision-free, idempotent, and needs no `GetDescendant`,
no serializable isolation, and no `Microsoft.SqlServer.Types` in the binder. A CTE cannot detect
cycles (it recurses to `MAXRECURSION`), so C# must.

**Rejected.** In-memory `Node` computation (needs `Microsoft.SqlServer.Types` in the TVP path and
knowledge of untouched siblings); per-row `GetReparentedValue` (a loop); subtree-scoped recompute
(root-level sibling numbering shifts on deletes anyway; scoping is an optimization the emitter may
add later without changing semantics).

**Confidence.** High. **Review flag.** Whether to number siblings by `Id` (nodes shift after a
delete) or by existing `Node` order (stable, but needs a two-phase statement).

### D15 — Seed data

**Decision.** `HasData` seeds exactly one row in this theme: the system user (`core.Users`, `Id = 1`,
`Name = 'System'`, `Email = 'system@tellma.invalid'`, `State = New`, `IsActive = 1`,
`PreferredLanguage = 'en'`, `PreferredCalendar = 'gregorian'`, audit columns = `Id 1` and the
constant `2000-01-01T00:00:00Z`) plus its `core.UserActivity` row. Everything else is a runtime seed
applied by the migrator through the bulk pipeline as the system user, contributed by features through
`ISeedContributor` and recorded in `__SeedHistory` by `(Feature, Key)`:

- `core.roles.administrator` → role `Code = 'Administrator'`, `Name = 'Administrator'`, one permission `all`/`all`.
- `core.roles.everyone` → role `Code = 'Everyone'`, `IsPublic = 1`, no permissions (modules add
  public read permissions to it by code through later seeds).
- `core.users.dev-admin` (Development only) → user `Email = 'admin@localhost'`,
  `Subject = '00000000-0000-0000-0000-000000000001'`, `State = Invited`, `Name = 'Local Admin'`,
  membership in `Administrator`; `Joined` on first sign-in as usual.
- `core.users.first-admin` (deployed) → user from the provisioning input (`FirstAdminEmail`,
  `FirstAdminName`, `FirstAdminLanguage`) with `State = New` and membership in `Administrator`; the
  provisioning flow (host theme) calls `InviteAsync` for that user after the migrator returns, because
  only the web process holds the identity client.
- The GL module seeds no centres.

**Rationale.** Only ids that code references are `HasData`; the system user is the one such row
(audit columns, task credentials). Seeds through the pipeline draw ids from the allocator and pass
validation.

**Rejected.** `HasData` for roles (editable rows should not be re-asserted by migrations); a
well-known administrator role id.

**Confidence.** High.

### D16 — Migrations, model configuration, and package pins

**Decision.** The reference distribution's migrator owns one migration set; the initial migration
creates `core.*` and `gl.*` with sequences (`START WITH 1000`), history tables, the tree indexes,
unique filtered indexes, CHECK constraints, and enables `READ_COMMITTED_SNAPSHOT` when it is off
(`IF (SELECT is_read_committed_snapshot_on FROM sys.databases WHERE name = DB_NAME()) = 0 ALTER DATABASE CURRENT SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE`).
Model configuration is convention-driven from `Tellma.Core` (`ApplyTellma`): `[SystemVersioned]`
→ `IsTemporal(t => t.UseHistoryTable("<Table>History"))`, `IMultilingual` → `Name*` lengths,
`TreeEntity<T>` → shadow `Node`, computed `Level`, indexes, UDTT exclusion, enums → string + CHECK.
Package pins: `Microsoft.Data.SqlClient` bumped to `6.1.6` (or later 6.1.x) and
`Microsoft.EntityFrameworkCore.SqlServer.HierarchyId` `10.0.11` added (the latter requires the former).
`Microsoft.SqlServer.Types` arrives transitively and is never referenced by Abstractions.

**Confidence.** High. **Unverified.** Whether the UDTT derivation of the table-types spec includes
shadow properties (if it does, the convention excludes `Node` fluently; if not, nothing to do).

### D17 — Telemetry names

**Decision.** Meter `Tellma.Core` (the package), constants in `Tellma.Core.Abstractions`:
`tellma.users.invitations` (counter, tag `outcome`), `tellma.users.invitation_status_requests` (counter),
`tellma.users.session_terminations` (counter), `tellma.roles.filter_validation_failures` (counter,
tag `code`), `tellma.tree.recompute_duration` (histogram, tag `entity`). No per-tenant tags. The
identity client's HTTP calls are traced as client spans with `peer.service = tellma-identity`.

### D18 — Test tiers

**Decision.** `test/core/Tellma.Core.Tests`: behaviour unit tests with the pipeline's in-memory
fixture (validation rules, lockout guards, stamp rules). `test/core/Tellma.Core.IntegrationTests`
(`Category=Integration`, LocalDB): user/role/settings saves end to end, tag bumps observed, tree
recompute correctness under concurrent saves. `test/module/gl/Tellma.Module.Gl.Tests`: Center
validation. Invitation tests run against a `FakeUserInvitationClient` in `Tellma.Core.Testing`; a
`Live=true` suite exercises the real identity server in-proc.

---

## 3. Contracts

Shapes this theme owns are marked *(owned)*; shapes it needs from another theme are marked
*(needed from …)* and state the minimum this theme relies on. Apache-2.0 headers, primary
constructors and block bodies are omitted for brevity; the real files follow the repo's style rules.

### 3.1 Entity base shapes *(needed from the data-access theme)*

```csharp
namespace Tellma.Core.Abstractions.Entities;

/// <summary>A top-level entity: app-assigned integer id plus the four audit columns; <see cref="ModifiedAt"/> is the concurrency token.</summary>
public abstract class Entity
{
    /// <summary>The app-assigned surrogate key; 0 or null on the wire means insert.</summary>
    public int Id { get; set; }
    /// <summary>When the row was created (UTC). Server-owned.</summary>
    public DateTime CreatedAt { get; set; }
    /// <summary>The user that created the row. Server-owned.</summary>
    public int CreatedById { get; set; }
    /// <summary>When the row was last changed by a user action (UTC); the optimistic-concurrency token. Server-owned; echoed by clients.</summary>
    public DateTime ModifiedAt { get; set; }
    /// <summary>The user that last changed the row. Server-owned.</summary>
    public int ModifiedById { get; set; }
}

/// <summary>A weak entity: owned by a parent row, saved by synchronising the parent's child collection, no audit columns.</summary>
public abstract class ChildEntity
{
    /// <summary>The app-assigned surrogate key; 0 or null on the wire means insert.</summary>
    public int Id { get; set; }
}

/// <summary>A top-level tree entity. <c>Node</c> is a shadow <c>hierarchyid</c> column maintained by the tree recompute statement; <see cref="Level"/> is a persisted computed column.</summary>
/// <typeparam name="TSelf">The leaf type, so that <see cref="Parent"/> is typed against it.</typeparam>
public abstract class TreeEntity<TSelf> : Entity where TSelf : TreeEntity<TSelf>
{
    /// <summary>The parent row, or null for a root.</summary>
    public int? ParentId { get; set; }
    /// <summary>The parent navigation (child to parent only).</summary>
    public TSelf? Parent { get; set; }
    /// <summary>The depth of the node; root is 0. Read-only, computed from the node.</summary>
    [DatabaseGenerated(DatabaseGeneratedOption.Computed)] public short Level { get; set; }
    /// <summary>Descendants plus self. Server-owned.</summary>
    public int SubtreeCount { get; set; }
    /// <summary>Active descendants plus self when active. Server-owned; equals <see cref="SubtreeCount"/> for entities that are not activatable.</summary>
    public int ActiveSubtreeCount { get; set; }
}

/// <summary>Marks an entity that can be activated and deactivated; projects the <c>activate</c> action and the two service methods.</summary>
public interface IActivatable { bool IsActive { get; set; } }

/// <summary>Marks an entity with up to three content-language names.</summary>
public interface IMultilingual { string Name { get; set; } string? Name2 { get; set; } string? Name3 { get; set; } }

/// <summary>Opts a table into SQL Server system-versioning; the history table is <c>&lt;Table&gt;History</c> in the same schema.</summary>
[AttributeUsage(AttributeTargets.Class)] public sealed class SystemVersionedAttribute : Attribute;

/// <summary>A property the client may send but the pipeline always overwrites from the stored row (or the insert default).</summary>
[AttributeUsage(AttributeTargets.Property)] public sealed class ServerOwnedAttribute : Attribute;

/// <summary>A <c>[NotMapped]</c> collection of child entities saved by synchronisation under this parent.</summary>
[AttributeUsage(AttributeTargets.Property)] public sealed class ChildAttribute : Attribute;

/// <summary>The property that identifies a row in export-for-import and import; must be unique.</summary>
[AttributeUsage(AttributeTargets.Property)] public sealed class NaturalKeyAttribute : Attribute;

/// <summary>A text property the <c>Search</c> parameter matches against.</summary>
[AttributeUsage(AttributeTargets.Property)] public sealed class SearchableAttribute : Attribute;
```

### 3.2 Core entities *(owned)*

```csharp
namespace Tellma.Core.Abstractions.Users;

/// <summary>The tenant-side membership state of a user; orthogonal to <see cref="IActivatable.IsActive"/>.</summary>
public enum UserState { /// <summary>Created, never invited; no subject.</summary> New, /// <summary>Invited; subject assigned; not yet signed in here.</summary> Invited, /// <summary>Has signed in to this tenant at least once.</summary> Joined }

/// <summary>A human user of the tenant. Generic over its membership child so distributions can extend either.</summary>
[TableType, SystemVersioned]
public abstract class User<TRoleMembership> : Entity, IActivatable, IMultilingual where TRoleMembership : RoleMembership
{
    /// <summary>The identity server subject (36-char GUID string); null until invited. Server-owned.</summary>
    [MaxLength(36), ServerOwned] public string? Subject { get; set; }
    /// <summary>The membership state. Server-owned.</summary>
    [ServerOwned] public UserState State { get; set; }
    /// <summary>The sign-in email; unique; editable only while <see cref="State"/> is <see cref="UserState.New"/>.</summary>
    [Required, MaxLength(255), EmailAddress, NaturalKey, Searchable] public string Email { get; set; } = "";
    [Required, MaxLength(255), Searchable] public string Name { get; set; } = "";
    [MaxLength(255), Searchable] public string? Name2 { get; set; }
    [MaxLength(255), Searchable] public string? Name3 { get; set; }
    /// <summary>BCP 47 tag of the language the user is addressed in (invitations, messages, UI default).</summary>
    [Required, MaxLength(10)] public string PreferredLanguage { get; set; } = "en";
    /// <summary>Calendar code for dates in messages to the user.</summary>
    [Required, MaxLength(16)] public string PreferredCalendar { get; set; } = "gregorian";
    /// <summary>IANA time zone; null means the tenant's zone.</summary>
    [MaxLength(64)] public string? TimeZone { get; set; }
    /// <summary>"female" or "male" for grammatical agreement; null when unstated.</summary>
    [MaxLength(8)] public string? Gender { get; set; }
    /// <summary>Notification address; null means <see cref="Email"/>.</summary>
    [MaxLength(255), EmailAddress] public string? ContactEmail { get; set; }
    /// <summary>E.164 mobile number for SMS notifications.</summary>
    [MaxLength(32)] public string? ContactMobile { get; set; }
    /// <summary>The profile image blob id; on the wire a staging token on save. Server-owned after confirmation.</summary>
    public Guid? ImageId { get; set; }
    /// <summary>When the last invitation was raised (UTC). Server-owned.</summary>
    [ServerOwned] public DateTime? InvitedAt { get; set; }
    /// <summary>When the user first signed in here (UTC). Server-owned.</summary>
    [ServerOwned] public DateTime? JoinedAt { get; set; }
    /// <summary>The identity server's per-user error from the last invitation, for troubleshooting. Server-owned.</summary>
    [MaxLength(1024), ServerOwned] public string? InviteError { get; set; }
    public bool IsActive { get; set; } = true;
    /// <summary>The role memberships, synchronised on save.</summary>
    [NotMapped, Child] public IList<TRoleMembership> RoleMemberships { get; set; } = [];
    /// <summary>Per-type notification opt-outs, synchronised on save.</summary>
    [NotMapped, Child] public IList<UserNotificationPreference> NotificationPreferences { get; set; } = [];
}

/// <summary>The Core default user leaf; distributions inherit from it to add columns.</summary>
public class User : User<RoleMembership>;

/// <summary>A user's membership in a role; child of <see cref="User"/>.</summary>
[TableType, SystemVersioned]
public class RoleMembership : ChildEntity
{
    public int UserId { get; set; }
    public int RoleId { get; set; }
    [MaxLength(1024)] public string? Notes { get; set; }
}

/// <summary>A per-type channel opt-out; absent means the type's defaults.</summary>
[TableType]
public class UserNotificationPreference : ChildEntity
{
    public int UserId { get; set; }
    [Required, MaxLength(64)] public string NotificationType { get; set; } = "";
    public bool Email { get; set; } = true;
    public bool Sms { get; set; } = true;
    public bool Push { get; set; } = true;
}

/// <summary>A row of the per-user preference bag; composite key (UserId, Key).</summary>
[TableType]
public class UserPreference
{
    [Key] public int UserId { get; set; }
    [Key, Required, MaxLength(128)] public string Key { get; set; } = "";
    /// <summary>JSON text the server never interprets.</summary>
    [Required] public string Value { get; set; } = "";
}

/// <summary>Well-known user ids in the reserved band.</summary>
public static class WellKnownUsers { /// <summary>The system user: creator of seeds, principal of built-in schedules, cannot sign in.</summary> public const int SystemUserId = 1; }
```

```csharp
namespace Tellma.Core.Abstractions.Roles;

/// <summary>A role: a named bundle of permissions; public roles apply to everyone and have no members.</summary>
[TableType, SystemVersioned]
public abstract class Role<TPermission> : Entity, IActivatable, IMultilingual where TPermission : Permission
{
    [Required, MaxLength(255), Searchable] public string Name { get; set; } = "";
    [MaxLength(255), Searchable] public string? Name2 { get; set; }
    [MaxLength(255), Searchable] public string? Name3 { get; set; }
    [MaxLength(50), NaturalKey, Searchable] public string? Code { get; set; }
    /// <summary>When true the role's permissions apply to every active user and memberships are rejected.</summary>
    public bool IsPublic { get; set; }
    public bool IsActive { get; set; } = true;
    [NotMapped, Child] public IList<TPermission> Permissions { get; set; } = [];
}

/// <summary>The Core default role leaf.</summary>
public class Role : Role<Permission>;

/// <summary>One grant: a resource, an action, and an optional row filter in Queryex.</summary>
[TableType, SystemVersioned]
public class Permission : ChildEntity
{
    public int RoleId { get; set; }
    /// <summary>A securable resource name or <c>all</c>.</summary>
    [Required, MaxLength(128)] public string Resource { get; set; } = "";
    /// <summary>An action of the securable or <c>all</c>.</summary>
    [Required, MaxLength(32)] public string Action { get; set; } = "";
    /// <summary>A Queryex predicate over the securable's filter root; null grants every row.</summary>
    public string? Filter { get; set; }
    /// <summary>The Queryex language version <see cref="Filter"/> was validated under. Server-owned.</summary>
    [ServerOwned] public int? FilterLanguageVersion { get; set; }
    [MaxLength(1024)] public string? Notes { get; set; }
}
```

### 3.3 Core services *(owned)*

```csharp
namespace Tellma.Core.Abstractions.Users;

/// <summary>The user stack: every standard operation of <see cref="IEntityService{TEntity}"/> plus invitation, self-service, and the guarded activation rules.</summary>
public interface IUserService<TUser> : IEntityService<TUser> where TUser : User
{
    /// <summary>Invites (or re-invites) users through the identity server; requires the <c>User</c>/<c>invite</c> permission on every id.</summary>
    Task<IReadOnlyList<InviteResult>> InviteAsync(IReadOnlyList<int> ids, CancellationToken ct);
    /// <summary>Reads the live delivery state of the users' invitations; requires <c>User</c>/<c>read</c>.</summary>
    Task<IReadOnlyList<InvitationStatus>> GetInvitationStatusAsync(IReadOnlyList<int> ids, CancellationToken ct);
    /// <summary>The caller's own profile in details shape; no permission required.</summary>
    Task<DetailsResult<TUser>> GetMyProfileAsync(CancellationToken ct);
    /// <summary>Saves the caller's profile columns and notification preferences; no permission required.</summary>
    Task<DetailsResult<TUser>> SaveMyProfileAsync(TUser profile, SaveOptions options, CancellationToken ct);
    /// <summary>Upserts preference entries for the caller and bumps the preferences tag.</summary>
    Task SetMyPreferencesAsync(IReadOnlyList<UserPreferenceEntry> entries, CancellationToken ct);
    /// <summary>Deletes preference entries for the caller and bumps the preferences tag.</summary>
    Task DeleteMyPreferencesAsync(IReadOnlyList<string> keys, CancellationToken ct);
    /// <summary>Sends a test message to the caller on the given channel; rate-limited to one per channel per minute.</summary>
    Task SendTestNotificationAsync(NotificationChannel channel, CancellationToken ct);
}

/// <summary>The outcome of inviting one user.</summary>
public sealed record InviteResult(int Id, InviteOutcome Outcome, string? Error);
/// <summary>Mirrors the identity server's statuses plus a failure.</summary>
public enum InviteOutcome { Invited, Reinvited, AlreadyRegistered, Failed }
/// <summary>The live delivery state of a user's invitation.</summary>
public sealed record InvitationStatus(int Id, InvitationDeliveryState State, bool ExpectsDeliveryEvents, DateTimeOffset? SentAt, DateTimeOffset? UpdatedAt, string? Reason);
/// <summary>The identity server's delivery states plus <see cref="Unknown"/> for a subject it does not report.</summary>
public enum InvitationDeliveryState { Unknown, Pending, Sent, Delivered, Bounced, Complained, Rejected, Abandoned, Accepted }
/// <summary>A preference key/value pair; the row image of the <c>UserPreferenceEntry</c> table type.</summary>
[TableType] public sealed class UserPreferenceEntry { [Key, Required, MaxLength(128)] public string Key { get; set; } = ""; [Required] public string Value { get; set; } = ""; }
/// <summary>A notification channel.</summary>
public enum NotificationChannel { Email, Sms, Push }
```

```csharp
namespace Tellma.Core.Abstractions.Roles;

/// <summary>The role stack: the standard operations; validation against the securables registry and Queryex is internal.</summary>
public interface IRoleService<TRole> : IEntityService<TRole> where TRole : Role;
```

```csharp
namespace Tellma.Core.Abstractions.Settings;

/// <summary>Reads every settings category and saves one category at a time under the <c>Settings.&lt;Category&gt;</c>/<c>save</c> securable.</summary>
public interface ISettingsService
{
    /// <summary>All categories; public to any authenticated user.</summary>
    Task<TenantSettings> GetSettingsAsync(CancellationToken ct);
    Task<GeneralSettings> SaveGeneralAsync(GeneralSettings settings, SaveOptions options, CancellationToken ct);
    Task<LocalizationSettings> SaveLocalizationAsync(LocalizationSettings settings, SaveOptions options, CancellationToken ct);
    /// <summary>Upserts ad-hoc entries under a category.</summary>
    Task SetEntriesAsync(string category, IReadOnlyList<SettingEntry> entries, CancellationToken ct);
    Task DeleteEntriesAsync(string category, IReadOnlyList<string> keys, CancellationToken ct);
}

/// <summary>The general category: a column subset of the single-row settings table.</summary>
public sealed record GeneralSettings([property: Required, MaxLength(255)] string TenantName, string? TenantName2, string? TenantName3, DateTime ModifiedAt);
/// <summary>The localization category.</summary>
public sealed record LocalizationSettings([property: Required, MaxLength(10)] string PrimaryLanguage, string? SecondaryLanguage, string? TernaryLanguage, [property: Required] string PrimaryCalendar, string? SecondaryCalendar, [property: Required, MaxLength(64)] string TimeZone, DateTime ModifiedAt);
/// <summary>Every category plus the settings tag for client caching.</summary>
public sealed record TenantSettings(GeneralSettings General, LocalizationSettings Localization, IReadOnlyDictionary<string, IReadOnlyList<SettingEntry>> Entries, Guid SettingsTag);
/// <summary>An ad-hoc setting entry.</summary>
[TableType] public sealed class SettingEntry { [Key, Required, MaxLength(128)] public string Key { get; set; } = ""; [Required] public string Value { get; set; } = ""; }
```

### 3.4 The identity client and the session terminator *(owned; implemented in `Tellma.Core`, the host wires the base address and credentials)*

```csharp
namespace Tellma.Core.Abstractions.Identity;

/// <summary>Typed client for the identity server's distribution-facing invitation APIs. Chunks to 1000 per call; never opens a tenant transaction.</summary>
public interface IUserInvitationClient
{
    /// <summary>Creates-or-gets each user by email and queues invitation links; one result per item in order.</summary>
    Task<IReadOnlyList<IdentityInviteResult>> InviteAsync(IReadOnlyList<IdentityInviteItem> items, CancellationToken ct);
    /// <summary>The delivery state of the latest invitation this client raised per subject; one result per subject in order.</summary>
    Task<IReadOnlyList<IdentityDeliveryStatus>> GetDeliveryStatusAsync(IReadOnlyList<string> subjects, CancellationToken ct);
}
/// <summary>One user to invite; <paramref name="ReturnUrl"/> must be under the distribution's registered origin.</summary>
public sealed record IdentityInviteItem(string Email, string? DisplayName, string? Locale, string? Gender, string? ReturnUrl);
/// <summary>Status plus subject, or an error; never both.</summary>
public sealed record IdentityInviteResult(string Email, string? Subject, IdentityInviteStatus? Status, string? Error);
public enum IdentityInviteStatus { Invited, Reinvited, Active }
public sealed record IdentityDeliveryStatus(string Subject, string State, bool ExpectsDeliveryEvents, DateTimeOffset? SentUtc, DateTimeOffset? UpdatedUtc, string? Reason);

/// <summary>Ends every session of the given subjects in a tenant: BFF cookies revoked, SignalR connections closed.</summary>
public interface IUserSessionTerminator { Task TerminateAsync(int tenantId, IReadOnlyList<string> subjects, CancellationToken ct); }
```

### 3.5 What this theme needs from the batch and the pipeline *(needed from the data-access and pipeline themes)*

```csharp
namespace Tellma.Core.Abstractions.Data;

/// <summary>Builds one round trip. Every statement declares the tables it writes so the executor appends the right tag bumps.</summary>
public interface IBatchBuilder
{
    /// <summary>Adds a compiled Queryex query; the reader yields typed rows.</summary>
    IBatchReader<T> Query<T>(QuerySpec spec, QueryOptions options);
    /// <summary>Adds an upsert of top-level rows with child synchronisation; <paramref name="columns"/> restricts the UPDATE column list (self-service, settings categories).</summary>
    IBatchReader<SaveEcho> Save<TEntity>(IReadOnlyList<TEntity> rows, SaveOptions options, ColumnMask? columns = null) where TEntity : class;
    /// <summary>Adds raw SQL with TVP or scalar parameters; <paramref name="writes"/> names the tables it writes for tag bumps and retry decisions.</summary>
    IBatchReader<T> Sql<T>(string sql, IReadOnlyList<BatchParameter> parameters, IReadOnlySet<string> writes, bool mayRetry);
    /// <summary>Appends a tag bump; the executor deduplicates by tag and scope.</summary>
    void BumpTag(TagScope scope, string tagName, IBatchReader<int>? userIds = null);
    /// <summary>Restricts a query to ids in a TVP (the engine amendment: <c>Id IN (SELECT Id FROM @list)</c>).</summary>
    ListRestriction Restrict(IReadOnlyList<int> ids);
}
/// <summary>The tag scopes: one row per tenant or one row per user.</summary>
public enum TagScope { Tenant, User }
```

Statements this theme needs the executor to emit on request (exact text in §3.7 and §4):
`BumpTag(User, "PermissionsTag", memberIds)` → `UPDATE core.UserActivity SET PermissionsTag = NEWID() WHERE UserId IN (SELECT Id FROM @qx_ids)`;
`BumpTag(Tenant, "PublicPermissionsTag")`, `BumpTag(Tenant, "SettingsTag")`, `BumpTag(User, "PreferencesTag", [me])`.

```csharp
namespace Tellma.Core.Abstractions.Services;

/// <summary>Per-entity behaviour supplied by the package that owns the entity; every member has a no-op default.</summary>
public interface IEntityBehavior<TEntity> where TEntity : class
{
    /// <summary>Trims, normalises, defaults; runs before validation.</summary>
    ValueTask PreprocessAsync(SaveContext<TEntity> context, CancellationToken ct) => default;
    /// <summary>Declares context requests against the batch, then validates once they are loaded; may request a further bounded round.</summary>
    ValueTask ValidateAsync(ValidationContext<TEntity> context, CancellationToken ct) => default;
    /// <summary>Appends statements to the persist batch (tree recompute, tag bumps, notification inserts).</summary>
    ValueTask OnPersistingAsync(PersistContext<TEntity> context, CancellationToken ct) => default;
    /// <summary>Runs after commit with the read-back rows (blob deletes, emails, session termination).</summary>
    ValueTask AfterCommitAsync(CommittedContext<TEntity> context, CancellationToken ct) => default;
}

/// <summary>The standard operations projected for every registered entity.</summary>
public interface IEntityService<TEntity> where TEntity : class
{
    Task<QueryResult> QueryAsync(QueryRequest request, CancellationToken ct);
    Task<DetailsResult<TEntity>> GetByIdAsync(int id, DetailsRequest request, CancellationToken ct);
    Task<IReadOnlyList<TEntity>> GetByIdsAsync(IReadOnlyList<int> ids, CancellationToken ct);
    Task<SaveResult<TEntity>> SaveAsync(IReadOnlyList<TEntity> entities, SaveOptions options, CancellationToken ct);
    Task DeleteByIdsAsync(IReadOnlyList<int> ids, CancellationToken ct);
    Task<int> DeleteByQueryAsync(FilterTree filter, CancellationToken ct);
    Task ActivateAsync(IReadOnlyList<int> ids, bool isActive, CancellationToken ct);           // activatable only
    Task<QueryResult> GetByParentIdsAsync(IReadOnlyList<int?> parentIds, QueryRequest request, CancellationToken ct); // tree only
    Task DeleteWithDescendantsAsync(IReadOnlyList<int> ids, CancellationToken ct);              // tree only
}

/// <summary>Save options shared by every save entry point.</summary>
public sealed record SaveOptions(bool OverrideConcurrency = false, bool ReturnEntities = true);
```

### 3.6 What this theme needs from the securables registry and the evaluator *(needed from the users-roles-permissions theme)*

```csharp
namespace Tellma.Core.Abstractions.Security;

/// <summary>A securable: a resource, its actions, and whether row filters apply.</summary>
public sealed record Securable(string Resource, IReadOnlySet<string> Actions, bool SupportsFilter, string? FilterRoot);

/// <summary>The registry every feature contributes to at startup; immutable afterwards.</summary>
public interface ISecurableRegistry
{
    bool TryGet(string resource, out Securable securable);
    IReadOnlyList<Securable> All { get; }
    /// <summary>A stable hash of <see cref="All"/>; part of every permissions cache key.</summary>
    string Fingerprint { get; }
}

/// <summary>Answers "may this user do this, and on which rows".</summary>
public interface IPermissionEvaluator
{
    /// <summary>The decision for the current user; <see cref="Decision.Filter"/> is null for unrestricted access and an empty <c>Or</c> when denied.</summary>
    ValueTask<Decision> EvaluateAsync(string resource, string action, CancellationToken ct);
    /// <summary>The same decision for a hypothetical membership set, used by the lockout guards over the post-save picture.</summary>
    ValueTask<bool> WouldHaveFullAccessAsync(int userId, IReadOnlyList<int> roleIds, IReadOnlyList<PermissionRow> pending, CancellationToken ct);
}
public sealed record Decision(bool Allowed, FilterTree? Filter, IReadOnlyList<string> GrantingRoles);
```

Contributions this theme makes (through the host theme's feature contract):
`CoreFeature` registers securables `User {read, save, delete, activate, invite; filter root User}`,
`Role {read, save, delete, activate; filter root Role}`, `Settings.General {save}`,
`Settings.Localization {save}`; `GlFeature` registers `Center {read, save, delete, activate; filter root Center}`.

### 3.7 The tree recompute statement *(owned as the first consumer; emitted by the data-access theme per tree entity)*

```sql
-- Parameters: none. Table and key names are substituted from the EF model. Runs inside the save transaction
-- after the upsert/delete/activate statements of the same batch. Idempotent.
;WITH Numbered AS (
    SELECT Id, ParentId, ROW_NUMBER() OVER (PARTITION BY ParentId ORDER BY Id) AS Ordinal
    FROM gl.Centers
), Paths AS (
    SELECT Id, CAST('/' + CAST(Ordinal AS varchar(20)) + '/' AS varchar(892)) AS Path
    FROM Numbered WHERE ParentId IS NULL
    UNION ALL
    SELECT n.Id, CAST(p.Path + CAST(n.Ordinal AS varchar(20)) + '/' AS varchar(892))
    FROM Numbered n JOIN Paths p ON n.ParentId = p.Id
)
UPDATE c SET Node = CAST(p.Path AS hierarchyid)
FROM gl.Centers c JOIN Paths p ON p.Id = c.Id
WHERE c.Node <> CAST(p.Path AS hierarchyid)
OPTION (MAXRECURSION 200);

;WITH Counts AS (
    SELECT p.Id,
           COUNT(*) AS SubtreeCount,
           SUM(CASE WHEN d.IsActive = 1 THEN 1 ELSE 0 END) AS ActiveSubtreeCount
    FROM gl.Centers p JOIN gl.Centers d ON d.Node.IsDescendantOf(p.Node) = 1
    GROUP BY p.Id
)
UPDATE c SET SubtreeCount = k.SubtreeCount, ActiveSubtreeCount = k.ActiveSubtreeCount
FROM gl.Centers c JOIN Counts k ON k.Id = c.Id
WHERE c.SubtreeCount <> k.SubtreeCount OR c.ActiveSubtreeCount <> k.ActiveSubtreeCount;
```

Newly inserted rows arrive with `Node` set to a placeholder the emitter supplies in the INSERT
(`hierarchyid::GetRoot().GetDescendant(NULL, NULL)` is not unique, so the emitter inserts
`CAST('/' + CAST(-Id AS varchar(20)) + '/' AS hierarchyid)`, a negative ordinal no recompute ever
produces, which the first statement immediately replaces). `MAXRECURSION 200` bounds depth; a
deeper tree is refused by validation (`Tree_TooDeep`, max depth 100).

### 3.8 The GL module *(owned)*

```csharp
namespace Tellma.Module.Gl.Abstractions;

/// <summary>The responsibility-centre type; grouping types may have children, posting types are leaves.</summary>
public enum CenterType { /// <summary>Non-posting grouping node.</summary> Abstract, /// <summary>Profit or investment centre; grouping.</summary> BusinessUnit, /// <summary>Service cost centre.</summary> Service, /// <summary>Production or operating cost centre.</summary> Operation, /// <summary>Revenue centre.</summary> Sale }

/// <summary>A responsibility centre; the first tree entity. Generic over itself so <c>Parent</c> is typed against the leaf.</summary>
[TableType]
public abstract class Center<TCenter> : TreeEntity<TCenter>, IActivatable, IMultilingual where TCenter : Center<TCenter>
{
    public CenterType CenterType { get; set; }
    [Required, MaxLength(255), Searchable] public string Name { get; set; } = "";
    [MaxLength(255), Searchable] public string? Name2 { get; set; }
    [MaxLength(255), Searchable] public string? Name3 { get; set; }
    [Required, MaxLength(50), NaturalKey, Searchable] public string Code { get; set; } = "";
    public bool IsActive { get; set; } = true;
}
/// <summary>The GL default centre leaf.</summary>
public class Center : Center<Center>;

/// <summary>Securable names owned by the GL module.</summary>
public static class GlSecurables { public const string Center = "Center"; }

/// <summary>Options for the GL feature.</summary>
public interface IGlBuilder { /// <summary>Substitutes the centre leaf.</summary> IGlBuilder UseCenter<TCenter>() where TCenter : Center; }
```

```csharp
namespace Tellma.Module.Gl;

/// <summary>Registers the GL feature: the Center entity, its securable, its behaviour, and its resources.</summary>
public static class GlServiceCollectionExtensions
{
    /// <summary>Adds the GL module to a Tellma composition; requires the Core feature.</summary>
    public static ITellmaBuilder AddGl(this ITellmaBuilder builder, Action<IGlBuilder>? configure = null);
}

/// <summary>Centre-specific validation: grouping parents, no type change under children, cycles come from the tree capability.</summary>
public sealed class CenterBehavior<TCenter> : IEntityBehavior<TCenter> where TCenter : Center<TCenter>;
```

### 3.9 Composition surface *(owned: `AddCore`; needed from the host theme: `ITellmaBuilder`, `TellmaFeature`, `ISeedContributor`)*

```csharp
namespace Tellma.Core;

/// <summary>Registers the Core stack feature: users, roles, settings, their securables and seeds.</summary>
public static class CoreServiceCollectionExtensions
{
    public static ITellmaBuilder AddCore(this ITellmaBuilder builder, Action<ICoreBuilder>? configure = null);
}
/// <summary>Leaf substitutions for Core entities.</summary>
public interface ICoreBuilder
{
    ICoreBuilder UseUser<TUser>() where TUser : User;
    ICoreBuilder UseRole<TRole>() where TRole : Role;
    /// <summary>Registers a distribution-owned entity with the generic stack.</summary>
    ICoreBuilder AddEntity<TEntity>(Action<IEntityOptions<TEntity>>? configure = null) where TEntity : Entity;
}

namespace Tellma.Core.Abstractions.Hosting;
/// <summary>A runtime seed step, applied by the migrator through the bulk pipeline as the system user and recorded by key.</summary>
public interface ISeedContributor
{
    /// <summary>Stable key recorded in <c>__SeedHistory</c>, e.g. <c>core.roles.administrator</c>.</summary>
    string Key { get; }
    /// <summary>Environments the seed applies in; null means all.</summary>
    IReadOnlySet<string>? Environments { get; }
    Task SeedAsync(ISeedContext context, CancellationToken ct);
}
```

<!-- CONTINUE -->
