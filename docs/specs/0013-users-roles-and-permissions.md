# Spec: Users, roles, and permissions

- **Author:** Ahmad Akra
- **Date:** 4 September 2026

**Status:** Ready for implementation. Frozen once merged: revised only if implementation forces a
design change, then kept as the historical record of what shipped — never updated thereafter as the
code or its dependencies evolve.

## Context

Every tenant database of every distribution answers the same two questions on every request: who is
the caller, and what may they touch. This spec ships the platform's answer: the `core.Users`,
`core.UserStamps`, `core.UserPreferences`, `core.Roles`, `core.RoleMemberships` and
`core.Permissions` tables and their entity classes; the securables registry that every feature
populates without hand-written registration; the evaluator that composes a caller's effective
grants — role memberships, public roles, bespoke criteria, implicit read, disjunctive filters — into
one `AccessDecision` whose `FilterTree` the CRUD pipeline conjoins into every read, update and
delete; the permissions cache validated by version tags; the fixed-text connect prologue that heads
every batch run on a caller's behalf and the guard that skips the body when its premises no longer
hold; the in-transaction invariants that make a tenant lockout impossible; the drift policy for
permissions a deployment invalidates; and the bootstrap that seeds the system user, the
Administrator role and the first human administrator.

The design builds on three frozen specs. Spec 0001 gives every table its user-defined table type and
its reserved id band. Spec 0003 gives the distribution its callers: OIDC relying parties behind a
BFF cookie, `sub` as the stable identity key, a bulk invite API that creates-or-gets an identity by
email, and step-up as a `401 insufficient_user_authentication` challenge. Spec 0008 gives row-level
security its substrate: permission filters are Queryex predicates validated in `Filter` mode against
the tenant's schema, composed structurally with `FilterTree` (an empty `Or` is `false`), compiled
with `me()`, `today()` and `now()` as parameter slots so one parameterised statement serves every
caller with the same grant shape.

Around it sit the concurrently written specs of the CRUD stack, cited here by number and contract
name. Spec 0011 owns the batch (`IDataBatch`), the save emitter and the entity bases this spec's
classes carry; spec 0012 owns version tags (`core.VersionTags`, `VersionedCache`, the tag prelude,
the `50412` guard and the epilogue bumps); spec 0014 owns the pipeline that calls the evaluator and
applies the two-stage pre-check and the in-transaction post-check; spec 0015 owns the web and MCP
surfaces that carry `SecurableEndpointMetadata` and serve `me`; spec 0017 owns `UserService`,
`RoleService`, `AccessService` and the invitation flow; spec 0016 owns the blobs behind
`User.ImageId` and `User.SignatureId`; spec 0020 owns `core.NotificationPreferences` and the inbox.

The connect step is not a separate call. It is a prologue on the first business round trip of every
operation, so a query costs one round trip and a save two, and a stale permission set is repaired
inside the same runner call — one recompose, never a query run under stale premises.

The spec deliberately leaves to later specs: the user and role services and every endpoint that
projects them (spec 0017); invitation and delivery status (spec 0017); the image bytes (spec 0016);
notification preferences (spec 0020); push subscriptions (a later spec); every stack over a child
entity, which is a securable root through its `Owner` (§5.5).

## Goals / Non-goals

**Goals**

- Ship the six tables, their entity classes and their seeds, in the shape every module and
  distribution references (`CreatedById` on every top-level entity is an FK to `core.Users`).
- Ship the securables registry: descriptors, the builder, contributors, aliasing, the deployment
  fingerprint, and the startup audit that fails composition when an endpoint or a securable is
  unregistered.
- Ship permission evaluation as a pure function over a cached, resolved set, with the composition
  contract every consumer relies on: union of memberships and public roles, write implies read,
  `Or` of filters, bespoke criteria, fail-closed drift, `Denied` for a caller with no user.
- Ship the permissions cache validated by the tenant `permissions` tag and the caller's
  `PermissionsTag`, with a bounded re-verification of denials.
- Ship the connect prologue, the `IF @tm_Guard = 1` wrapper, the persist-time re-checks, the
  guarded runner, and the failure-mode contract every service inherits.
- Ship the lockout invariants (`Access.LastAdministrator`, `Access.PublicRoleHasMembers`,
  `Access.AdministratorRoleDamaged`) as SQL under an application lock, and the escalation and
  self-lockout validators that bound delegation.
- Ship the drift policy: a permission a deployment invalidates grants nothing, is diagnosed
  everywhere, and never blocks the caller's other grants.
- Ship "can I, and why": the `me` payload, `access/check`, and the `AccessDecision` explanation.
- Ship service accounts as `Kind = Service` rows: a write-once kind, credential evidence on the row,
  resolution by client id (§7.7); spec 0017 issues the credentials.
- Ship the bootstrap: `HasData` rows in the reserved band and `ITenantBootstrapper` for the first
  human administrator, including the Development in-proc path.

**Non-goals (explicitly out of scope)**

- `UserService`, `RoleService`, `AccessService`, the `me` endpoints, the invite write-back, the
  `issue-credentials` action and the "you were added" notice — spec 0017.
- The batch executor, the save emitter, id allocation, the Queryex schema adapter and the engine
  amendments (`KeySetRestriction`, `level()`, `Via`) — spec 0011.
- `core.VersionTags`, the tag prelude and epilogue statements, `VersionedCache` and the
  `settings`/`entities` cache kinds — spec 0012.
- The pipeline's use of the decision (pre-check, post-check, `AncestorsFilter`, navigation
  traversal) and the closed exception set — spec 0014; HTTP status mapping, headers and problem
  bodies — spec 0015.
- Blob staging and the `user-image` kind — spec 0016. Notification preferences, inbox counters and
  the hub close on deactivation — spec 0020. Excel import of users and roles — spec 0018.
- SQL Server row-level security policies, `SESSION_CONTEXT`-based authorisation, and any logic in
  the database beyond the runtime-emitted statements of this spec (§5.4).

## 1. Placement and architecture

### 1.1 Projects, namespaces, edges

| Piece | Location | References |
|---|---|---|
| Contracts, enums, entity defaults, endpoint metadata records, telemetry names | `src/core/Tellma.Core.Abstractions/`, namespace `Tellma.Core.Abstractions.Access` | `Tellma.Core.Queryex` only (`FilterTree`, `QueryexDiagnostic`); BCL DataAnnotations; no EF, no framework |
| Runtime: registry builder, evaluator, caches, connector and prologue, guarded runner, guards, validators, bootstrapper, drift scanner, the EF configuration of the six tables | `src/core/Tellma.Core/`, namespace `Tellma.Core.Access` | `Tellma.Core.Abstractions` (`[TableType]`, `[ExcludeFromTableType]`, spec 0011 §2.2), `Tellma.Core.EntityFrameworkCore` (spec 0001), EF Core SqlServer, `Microsoft.Data.SqlClient`, `Microsoft.Extensions.*` |
| Unit tests | `test/core/Tellma.Core.Tests/Access/` | offline; every PR |
| Integration tests | `test/core/Tellma.Core.IntegrationTests/Access/` | `Category=Integration`; LocalDB or Testcontainers; every PR |

The entity defaults (`User`, `Role`, `RoleMembership`, `Permission`) live in Abstractions, not in
the runtime package, because every module entity's `CreatedById` references `core.Users` and modules
never reference `Tellma.Core`. They are non-abstract and unsealed; a distribution extends one by
plain inheritance (`MyUser : User`) and substitutes the leaf with spec 0010's
`TellmaBuilder.UseEntity<User, MyUser>()`. Every rule, statement and validator in this spec is
generic over `TUser : User` and `TRole : Role` and names base-class columns only. Replacing an
entity outright is not supported.

Contract blocks are C# sketches: names and shapes are normative; `using` directives, XML
documentation, cancellation-token parameters, method bodies and accessibility details are omitted,
so a block is never pasted into code. SQL statements are the exact shape to emit.

### 1.2 Composition

`CoreFeature` (spec 0010) contributes this spec's content: `contribution.Entity<User,
UserService<User>>()` and `contribution.Entity<Role, RoleService<Role>>()` — the one registration of
the two stacks; spec 0017 supplies the services and adds no `Entity` line for `User` or `Role` — the
two validator components `contribution.Validator<User, UserAccessRules<User>>()` and
`contribution.Validator<Role, RoleAccessRules<Role>>()` (a component registered for a base runs for
the distribution's leaf), `contribution.Securables(...)` for the non-entity securables of §4.3, the
model configuration of the six tables, and the singleton `ISecurableRegistry`, scoped
`IAccessEvaluator`, `IUserConnector`, `IGuardedBatchRunner`, `IAccessGuards`, and the
`IRequestContextInitializer` of §7.8 at `Order` 100. The registry is frozen in the realise phase of
`AddTellma`; every check of §4.5 reports into the realised gate.

### 1.3 Dependencies on sibling specs, by contract name

| Consumed contract | Owner | Used for |
|---|---|---|
| `IDataBatch`, `IDataBatchContributor`, `BatchResult<T>`, `SqlOptions`, `TableName`, `TellmaSqlErrors` | spec 0011 | the prologue contributor, the guards, raw statements, error numbers |
| `TopLevelEntity`, `ChildEntity`, `IActivatable`, `[Temporal]`, `[ServerOwned]`, `[Derived]`, `[WriteOnce]`, `[NaturalKey]`, `[Unique]`, `[Searchable]`, `[Multilingual]`, `[ParentKey]`, `[MaxChildren]` | spec 0011 | the entity shapes |
| `ITenantDatabase` (`Schema`), `KeySetRestriction` | spec 0011 | filter validation (§3.7, §9) and the post-check |
| `VersionTag`, `VersionTagNames`, `UserVersionTagNames`, `[BumpsVersionTag]`, `[BumpsUserVersionTag]`, `VersionedCache<TKey, TValue>`, `TellmaCacheOptions`, `core.VersionTags`, the `50412` guard and the bump statements | spec 0012 | tags, caches, the persist re-check |
| `IStackRegistry`, `StackDescriptor`, `EntityActionDescriptor`, `ApiActionDescriptor`, `[Stack]`, `StackOperations`, `[EntityAction]`, `[ApiAction]`, `[ApiRoute]`, `IEntityValidator<T>`, `SaveContext`, `DeleteContext`, `ActionContext`, `IPersistEffect<T>`, `SavePersistContext<TEntity>`, `ValidationErrors` | spec 0014 | securable registration from stacks; the validators; the companion `UserStamps` insert (§8.2) |
| `RequestContext`, `IRequestContextAccessor`, `IRequestContextInitializer`, `RequestContextInputs`, `TenantState`, `PrincipalKind`, `ITenantProvisioningStep`, `TenantProvisioningContext` | spec 0010 | the connect initializer, ReadOnly premises, the bootstrap step |
| `FeatureContribution.Securables(...)`, `SecurablesContributionItem` | spec 0010 | non-entity securables |
| `MeResult`, `UserProfileView`, `AccessSummary`, `SecurableSummary`, `AccessCheckRequest` | spec 0015 | the `me` payload (shape shared with this spec) and the `access/check` request (§10) |
| `INotifier` predicates on `core.NotificationPreferences` | spec 0020 | the bump rule for `PreferencesTag` |
| `[BlobReference]`, `BlobPreset`, `BlobReadAccess`, `IBlobService`, kind `user-image` | spec 0016 | `User.ImageId` |
| `IBlobService`, kind `user-signature` | spec 0016 | `User.SignatureId` |

## 2. Vocabulary and conventions

- **Tag** — an opaque cache validator: a `uniqueidentifier`, application-generated on every bump,
  compared for equality only, never ordered or indexed. Tenant-level tags live in
  `core.VersionTags` (spec 0012); the two user-level tags live on `core.UserStamps` (§3.3).
- **Stamp** — a timestamp: `LastActiveAt`, and `ModifiedAt`, the optimistic-concurrency token of
  every top-level entity (spec 0011). User-visible saves and actions move `ModifiedAt`; bookkeeping
  never does. The one documented exception is the prologue's `Invited → Joined` flip (§7.3).
- **Fingerprint** — the deployment-derived hash of the securables registry (§4.4). It changes only
  on deploy and travels in `me`; it is never stored in a tenant database.
- **Format version** — a `const int` per cached shape (`UserAccess.FormatVersion = 1`), bumped by
  hand when the shape changes; a mismatch is a cache miss, and the SPA's copy, which outlives
  deploys, is guarded the same way.
- **Securable** — the tuple `(Resource, Action, FilterRoot?, IsSensitive, Feature, Owner?)` a
  permission targets (§4.1). SQL Server's own word for an object a permission applies to.
- **Resource** — the entity name for entity securables (`core.User`, `core.Role`, `gl.Center`; spec
  0011 §3.1's `EntityMetadata.Name`), unique across the tenant schema by construction and
  fork-stable (a distribution extending `User` keeps `core.User`). Non-entity securables follow the
  same dotted grammar (`core.Settings.General`, `core.Settings.<Category>`): two or more segments of
  `[A-Za-z][A-Za-z0-9]*` joined by `.`, ≤ 128 characters, compared ordinal-ignore-case, stored as
  registered.
- **Action** — a PascalCase identifier ≤ 32 characters. Platform set: `Read` (query, details, export
  in any format), `Save` (create and update, import included), `Delete` (by ids, by query, with
  descendants), `Activate` (both directions), `Invite`, `Preferences` (another user's bag, spec
  0017), `Credentials` (a service account's client, spec 0017), `Retry`, `Cancel`, `Resume`,
  `Diagnose`; capability- and service-declared additions follow.
- **Wildcard** — `*` is the only wildcard, valid in either position of a stored permission; the
  registry refuses `*` as a real name. There are no prefix wildcards (`gl.*`).
- **Display form** in logs, diagnostics and the role editor: `resource:action`. Route segments are
  a projection concern of spec 0015 (`users`, `activate`), never a permission key.
- **Tables** plural and schema-qualified (`core.Users`), classes singular (`User`) and Queryex names
  singular and schema-qualified (`core.User`); sequences `core.sq_<Table>` `START WITH 1000`; ids
  1–999 reserved for `HasData`; history tables `core.<Table>History`; enum columns `varchar(n)`
  without an IN-list CHECK; every timestamp other than the audit set and period columns is
  `datetimeoffset(3)`; `Notes` columns are `nvarchar(1024)`.

```csharp
// Tellma.Core.Abstractions.Access
public enum UserKind { Human, System, Service }         // Service: a service account (§7.7)

public enum UserState { New, Invited, Joined }          // Joined: a human who signed in once; a service account holding credentials

public enum InviteStatus { Invited, Reinvited, Active } // the identity server's word, stored verbatim

public enum Gender { Female, Male }

public static class WellKnownIds
{
    public const int SystemUserId = 1;
    public const int AdministratorRoleId = 1;
    public const int AdministratorPermissionId = 1;
    public const int SystemAdministratorMembershipId = 1;
    public const int ReservedIdBandEnd = 999;
}

public static class AccessActions
{
    public const string Read = "Read";
    public const string Save = "Save";
    public const string Delete = "Delete";
    public const string Activate = "Activate";
    public const string Invite = "Invite";
    public const string Preferences = "Preferences";
    public const string Credentials = "Credentials";
    public const string Retry = "Retry";
    public const string Cancel = "Cancel";
    public const string Resume = "Resume";
    public const string Diagnose = "Diagnose";
    public const string Wildcard = "*";
}

public static class CoreResources
{
    public const string User = "core.User";
    public const string Role = "core.Role";
    public const string SettingsGeneral = "core.Settings.General";
    public const string SettingsPrefix = "core.Settings.";
    public const string Job = "core.Job";
    public const string Schedule = "core.Schedule";
    public const string Notification = "core.Notification";
    public const string Export = "core.Export";
    public const string Import = "core.Import";
    public const string Wildcard = "*";
}
```

## 3. The entities and tables

Every table is in schema `core`. Every FK is named. Temporal tables carry `ValidFrom`/`ValidTo
datetime2(7)` as shadow period columns, excluded from every UDTT, with history table
`core.<Table>History` holding the default clustered `(ValidTo, ValidFrom)` index only. The top-level
audit set (`CreatedAt`, `CreatedById`, `ModifiedAt`, `ModifiedById`; `datetimeoffset(7)`; FKs
`FK_<Table>_CreatedById`/`FK_<Table>_ModifiedById → core.Users(Id)`) is spec 0011's standard column
set and is written "audit set" below. Children carry no audit columns; any child change stamps the
parent.

### 3.1 `User` and `core.Users`

```csharp
// Tellma.Core.Abstractions.Access — non-abstract, unsealed default; distributions derive leaves
[Table("Users", Schema = "core"), TableType, Temporal, BumpsUserVersionTag(Preferences, "Id"), ApiResource,
 Stack(Operations = StackOperations.All | StackOperations.Enlist)]
public class User : TopLevelEntity, IActivatable
{
    [WriteOnce] public UserKind Kind { get; set; } = UserKind.Human;   // Human | Service at creation; System is row 1 only
    [ServerOwned] public string? Subject { get; set; }            // the OIDC sub, or a service account's client id; written by spec 0017's write-backs and the bootstrap
    [NaturalKey, Searchable] public string? Email { get; set; }   // required for Human; normalised; locked after invite
    public UserState State { get; set; }   // persisted computed column over InvitedAt/JoinedAt; DatabaseOwned (spec 0011 §2.4)
    [ServerOwned] public DateTimeOffset? InvitedAt { get; set; }  // datetimeoffset(3)
    [ServerOwned] public InviteStatus? InviteStatus { get; set; }
    [ServerOwned] public string? LastInviteError { get; set; }    // the identity server's per-user error, verbatim
    [ServerOwned] public DateTimeOffset? JoinedAt { get; set; }   // datetimeoffset(3); first sign-in (Human) or credentials issued (Service)
    [Multilingual, Searchable] public string Name { get; set; }
    public string? Name2 { get; set; }
    public string? Name3 { get; set; }
    [BlobReference("user-image", BlobPreset.Avatar, ReadAccess = BlobReadAccess.AnyMember)] public int? ImageId { get; set; }
    [BlobReference("user-signature", BlobPreset.Signature, MaxSizeBytes = 1_048_576, ReadAccess = BlobReadAccess.AnyMember)] public int? SignatureId { get; set; }   // a transparent PNG of the stylus capture; AnyMember
    public string? PreferredLanguage { get; set; }                // BCP 47; null = tenant primary
    public string? PreferredCalendar { get; set; }                // gc | uq | et; null = tenant primary
    public string? PreferredTimeZone { get; set; }                // IANA; null = tenant zone
    public Gender? Gender { get; set; }                           // the invitation's grammar; the SPA's gender-inflected strings (spec 0015 §3.4)
    public string? ContactEmail { get; set; }                     // null = Email
    public string? ContactMobile { get; set; }                    // E.164
    [ServerOwned] public bool IsActive { get; set; } = true;      // created active; the Activate action only
    [NotMapped] public IReadOnlyList<RoleMembership> RoleMemberships { get; set; }   // child collection
}
```

**`core.Users`** — `[Temporal]` → `core.UsersHistory`; UDTT `UsersList`; sequence `core.sq_Users`.

| Column | Type | Null | Constraints | Notes |
|---|---|---|---|---|
| `Id` | `int` | no | PK clustered; `CK_Users_Id CHECK ([Id] > 0)` | 1 = the system user |
| `Kind` | `varchar(8)` | no | `DF_Users_Kind 'Human'` | write-once |
| `Subject` | `varchar(255)` | yes | `COLLATE Latin1_General_100_BIN2`; `UX_Users_Subject (Subject) WHERE Subject IS NOT NULL INCLUDE (Kind, IsActive, State)` | server-owned; the prologue's covering seek |
| `Email` | `nvarchar(255)` | yes | `UX_Users_Email (Email) WHERE Email IS NOT NULL` | natural key; editable while `New` |
| `State` | `varchar(8)` | no | persisted computed: `CASE WHEN [JoinedAt] IS NOT NULL THEN 'Joined' WHEN [InvitedAt] IS NOT NULL THEN 'Invited' ELSE 'New' END` | database-owned; in the covering index |
| `InvitedAt` | `datetimeoffset(3)` | yes | | server-owned |
| `InviteStatus` | `varchar(9)` | yes | | server-owned |
| `LastInviteError` | `nvarchar(1024)` | yes | | server-owned |
| `JoinedAt` | `datetimeoffset(3)` | yes | | server-owned; the join evidence |
| `Name` | `nvarchar(255)` | no | | |
| `Name2`, `Name3` | `nvarchar(255)` | yes | | present per the tenant's languages |
| `ImageId` | `int` | yes | `FK_Users_ImageId → core.Blobs(Id)` NO ACTION; `UX_Users_ImageId (ImageId) WHERE ImageId IS NOT NULL` | |
| `SignatureId` | `int` | yes | `FK_Users_SignatureId → core.Blobs(Id)` NO ACTION; `UX_Users_SignatureId (SignatureId) WHERE SignatureId IS NOT NULL` | |
| `PreferredLanguage` | `varchar(35)` | yes | | |
| `PreferredCalendar` | `varchar(16)` | yes | | |
| `PreferredTimeZone` | `varchar(64)` | yes | | |
| `Gender` | `varchar(8)` | yes | | |
| `ContactEmail` | `nvarchar(255)` | yes | | |
| `ContactMobile` | `varchar(32)` | yes | | E.164 |
| `IsActive` | `bit` | no | `DF_Users_IsActive 1` | |
| audit set | | no | `FK_Users_CreatedById`, `FK_Users_ModifiedById → core.Users(Id)` | the system user's rows self-reference |
| `ValidFrom`, `ValidTo` | `datetime2(7)` | no | period, shadow | not in the UDTT |

Checks: `CK_Users_KindEmail ((Kind = 'Human') = (Email IS NOT NULL))`;
`CK_Users_SystemIsRowOne (Kind <> 'System' OR Id = 1)`;
`CK_Users_SystemHasNoSubject (Kind <> 'System' OR Subject IS NULL)`;
`CK_Users_HumanEvidence (Kind <> 'Human' OR (((Subject IS NULL) = (InvitedAt IS NULL))
AND (JoinedAt IS NULL OR InvitedAt IS NOT NULL)))`;
`CK_Users_ServiceEvidence (Kind <> 'Service' OR (InvitedAt IS NULL AND InviteStatus IS NULL
AND ((Subject IS NULL) = (JoinedAt IS NULL))))`. Index
`IX_Users_IsActive_Name (IsActive, Name)` for the default list. The child collection
`RoleMemberships` travels with the user (`null` = untouched, `[]` = delete all, list = synchronise).

Rules on the columns:

- **Server-owned columns** are never taken from a payload: the pipeline overwrites them from the
  before image on update and from the fresh-instance default on insert. `Subject`, `InvitedAt`,
  `InviteStatus` and `LastInviteError` are written only by spec 0017's write-backs (`invite`,
  `issue-credentials`) and the bootstrap (§11); `JoinedAt` only by the prologue's flip (§7.3),
  `issue-credentials` and the bootstrap.
- **`Kind`** is write-once: `Human` or `Service` at creation and never changed; `System` in a
  payload is `Users.KindNotAllowed`; a service account is §7.7.
- **`State`** is a persisted computed column over `InvitedAt` and `JoinedAt`, `DatabaseOwned` by
  spec 0011's derivation: never in a payload, the UDTT or a `SET` list, and never at odds with its
  evidence.
- **`Email`** is normalised (trim, lower-case) in preprocessing; required when `Kind = Human`,
  refused otherwise (`Users.EmailNotAllowed`); editable only while `State = New` — afterwards a
  change is `Users.EmailLockedAfterInvite`, because `Email` is the creates-or-gets key at the
  identity server (spec 0003) and editing it desynchronises the two systems. Uniqueness is validated
  in the save's first round trip by spec 0014's key loads (`Unique` at `Email`) and guaranteed by
  `UX_Users_Email`; `UX_Users_Subject` likewise maps to `Unique` at `Subject`.
- **Preferences** are validated by spec 0017's `UserService`: `PreferredLanguage` against the
  distribution's offered languages (`ILanguageCatalog.IsOffered`; a user's culture is not bound
  to the tenant's content languages, spec 0012 §7.3), `PreferredCalendar` against the calendar
  registry's codes (`ICalendarRegistry.Codes`; a code outside the tenant's pair is ignored by
  negotiation, spec 0012 §8.2) and `PreferredTimeZone` against the IANA zone list; `null` means
  the tenant default. They are typed columns on the row, not bag keys, because background work
  addresses a user in their language without a request in flight, and because they are filterable
  and exportable.
- **`IsActive`** is server-owned: every user is created active, and the column changes only
  through the `activate`/`deactivate` actions under the `Activate` securable.
- **`ImageId`** is an `int?` FK to `core.Blobs`; the attach rule, the staged upload and the bytes
  are spec 0016's. The row carries the key only.
- **`SignatureId`** is the same shape for kind `user-signature` (`Signature` preset narrowed to
  1 MiB, `AnyMember`): the SPA captures the stylus strokes and uploads a raster, since spec 0016
  stores no SVG; any member renders it over the approval stamp of a document they can read.
- **Row 1** (the system user) accepts no edits, activation or deletion: `Users.SystemUserImmutable`.
- **`Enlist`** among the stack's operations (spec 0014 §2.2) admits spec 0017's `me/save`, the
  one self-service write; every other edit of a user row is an administrative save under `Save`
  or an action under its own securable, never a self-service path.
- **The self-editable column set** is the member list of `MeSaveRequest` (spec 0017 §3.5), not an
  annotation.

### 3.2 The user state model

`State` moves on the tenant's own evidence only:

| From | To | Trigger | Written |
|---|---|---|---|
| — | `New` | the row is created (UI, import, bootstrap) | `Subject`, `InvitedAt`, `JoinedAt` all `NULL` |
| `New`, `Invited` | `Invited` | the bulk-invite call returns `Invited`, `Reinvited` or `Active` (Human) | `Subject`, `InvitedAt`, `InviteStatus`; `LastInviteError = NULL`; `ModifiedAt` stamped |
| `New`, `Invited` | unchanged | the bulk-invite call returns a per-user error | `LastInviteError`; `ModifiedAt` stamped |
| `Invited` | `Joined` | the first request in which the prologue resolves this subject (Human) | `JoinedAt`; `ModifiedAt` untouched (§7.3) |
| `New`, `Joined` | `Joined` | spec 0017's `issue-credentials` (Service) | `Subject`, `JoinedAt` (once); `ModifiedAt` stamped |

`IsActive` is orthogonal: an administrator's switch that refuses every request regardless of
`State`. Re-inviting a `New` or `Invited` user is allowed (`InviteStatus` keeps the server's word);
re-inviting a `Joined` user is spec 0017's `Users.AlreadyJoined`. The invite status `Active` means
"already holds a credential, no email was sent": the tenant state still becomes `Invited`, and spec
0017's welcome email and `core.user.added` notice reach every invited user. Delivery detail
(`Pending | Sent | Delivered | Bounced | …`) is a live drill-down through the identity server's
delivery-status API from the user details page and is never persisted. The identity server's own
lifecycle is invisible to the tenant except as an invite error. The three states are exhaustive for
what the tenant can know. `State` is computed from `InvitedAt` and `JoinedAt`, so the stored state
cannot drift from its evidence; the evidence CHECKs of §3.1 keep `Subject` in step with it per
kind.

### 3.3 `core.UserStamps` — the non-temporal sibling

The columns that churn (activity, tags, inbox tracking) live beside the temporal row, not on it, so
that an activity stamp or a cache bump never writes a history row.

| Column | Type | Null | Constraints | Notes |
|---|---|---|---|---|
| `UserId` | `int` | no | PK clustered; `FK_UserStamps_UserId → core.Users(Id) ON DELETE CASCADE` | one row per user |
| `LastActiveAt` | `datetimeoffset(3)` | yes | | the throttled activity stamp (§7.3) |
| `PermissionsTag` | `uniqueidentifier` | no | `DF_UserStamps_PermissionsTag NEWID()` | bumped by writes to the user's `RoleMemberships` |
| `PreferencesTag` | `uniqueidentifier` | no | `DF_UserStamps_PreferencesTag NEWID()` | bumped by writes to the user's own `Users` row, `UserPreferences`, `NotificationPreferences` |
| `InboxSeenAt` | `datetimeoffset(3)` | yes | | set by spec 0020's `inbox/seen` |

Not temporal; no UDTT; not an entity; exposed to Queryex read-only as `core.UserStamp` (navigation
`User`) so a report can show `LastActiveAt`. The row is inserted by the companion statement of
§8.2 in the same batch that inserts the user and by `HasData` for the system user; nothing else
inserts it, and the prologue's inner join (§7.3) treats a user without one as unknown. `HasData`:
`(1, NULL, 00000000-0000-0000-0000-000000000001, 00000000-0000-0000-0000-000000000002, NULL)`.

### 3.4 `core.UserPreferences` — the opaque bag

| Column | Type | Null | Constraints | Notes |
|---|---|---|---|---|
| `UserId` | `int` | no | PK clustered `(UserId, Key)`; `FK_UserPreferences_UserId → core.Users(Id) ON DELETE CASCADE` | |
| `Key` | `varchar(128)` | no | | dotted ASCII segments: `grid.users.columns`, `nav.pinned`, `tour.dismissed` |
| `Value` | `nvarchar(max)` | no | | opaque, JSON by convention; ≤ `MaxPreferenceValueBytes` |

Not temporal; **not an entity**: no surrogate id, no sequence, no child synchronisation, no audit;
absent from the Queryex schema and from Excel. At most `MaxPreferenceKeys` keys per user. The server
never interprets a value and caches nothing from the bag; the SPA caches the bag and the profile
under `PreferencesTag`. Pinned quick-access screens are the key `nav.pinned`; an
administrator-managed default for less technical users is a tenant setting (spec 0012) the client
merges when the key is absent. The bag is written through this statement over the standalone table
type `UserPreferenceList (Key varchar(128) PK, Value nvarchar(max))`: by spec 0017's
`me/preferences/set` and `me/preferences/delete` for the caller, executed with
`SqlOptions.ForCaller(writes = { core.UserPreferences })` so the epilogue bumps the caller's
`PreferencesTag`; and by spec 0017's `preferences/set` and `preferences/delete` for a user the
caller's `core.User × Preferences` grant reaches (§4.2, §5.3), the target's id bound as
`@tb{b}_p0` in place of `@tm_UserId` and, as a one-row `IdList` TVP, named in `SqlOptions.UserIds`
so the epilogue bumps the target's:

```sql
-- @tm_UserId: the connected user (preferences/set and preferences/delete bind the target as @tb{b}_p0 instead); @tb{b}_t0 : UserPreferenceList
DELETE P FROM [core].[UserPreferences] AS P
WHERE P.[UserId] = @tm_UserId AND NOT EXISTS (SELECT 1 FROM @tb{b}_t0 AS I WHERE I.[Key] = P.[Key]);
UPDATE P SET [Value] = I.[Value] FROM [core].[UserPreferences] AS P JOIN @tb{b}_t0 AS I ON I.[Key] = P.[Key]
WHERE P.[UserId] = @tm_UserId AND P.[Value] <> I.[Value];
INSERT [core].[UserPreferences] ([UserId], [Key], [Value])
SELECT @tm_UserId, I.[Key], I.[Value] FROM @tb{b}_t0 AS I
WHERE NOT EXISTS (SELECT 1 FROM [core].[UserPreferences] AS P WHERE P.[UserId] = @tm_UserId AND P.[Key] = I.[Key]);
-- The set actions run the UPDATE and INSERT halves only; the delete actions run the DELETE half only.
```

The full three-statement form is the "replace all" shape; the key cap is asserted inside the
statement after the `INSERT` (`THROW 50422, N'Users.TooManyPreferenceKeys'`, `MaxPreferenceKeys`
bound as a parameter) and the value size per item in C# (`Users.PreferenceValueTooLarge`).

Keys fall into two classes the client, never the server, tells apart. **Curated** keys —
`nav.pinned` — change by an explicit act of the user, or of an administrator holding
`core.User × Preferences`, and are written at once. **Write-behind** keys — the pages last visited,
the columns last chosen on a search page, a report's last parameters, the user a document type was
last forwarded to, each under its own key — change as a side effect of use:
the SPA applies them locally at once and flushes coalesced per-key writes, on a timer and on page
hide, and an administrator gains nothing by writing them, since the user's next flush overwrites
them. Either way the writer stays warm: its own write returns the new `PreferencesTag` in the
response header (spec 0015), which the SPA adopts while keeping its bag, so only a write from
another device or an administrator makes a device refetch `me`, which carries the bag (spec
0015), once, and a refetch overlays the device's unflushed keys onto the fetched bag. The server
enforces the key grammar, the two caps and spec 0017's per-request checks for every writer and
interprets nothing; no key is reserved from an administrator.

### 3.5 `Role` and `core.Roles`

```csharp
// Tellma.Core.Abstractions.Access — non-abstract, unsealed default
[Table("Roles", Schema = "core"), TableType, Temporal, BumpsVersionTag("permissions"), ApiResource]
public class Role : TopLevelEntity, IActivatable
{
    [Multilingual, Unique, Searchable] public string Name { get; set; }
    [Unique] public string? Name2 { get; set; }
    [Unique] public string? Name3 { get; set; }
    [NaturalKey] public string? Code { get; set; }
    public bool IsPublic { get; set; } = false;
    [ServerOwned] public bool IsActive { get; set; } = true;
    [NotMapped] public IReadOnlyList<Permission> Permissions { get; set; }   // child collection
}
```

**`core.Roles`** — temporal → `core.RolesHistory`; UDTT `RolesList`; `core.sq_Roles`.

| Column | Type | Null | Constraints | Notes |
|---|---|---|---|---|
| `Id` | `int` | no | PK clustered; `CK_Roles_Id CHECK ([Id] > 0)` | 1 = Administrator |
| `Name` | `nvarchar(255)` | no | `UX_Roles_Name` | |
| `Name2`, `Name3` | `nvarchar(255)` | yes | `UX_Roles_Name2 WHERE Name2 IS NOT NULL`, `UX_Roles_Name3 WHERE Name3 IS NOT NULL` | |
| `Code` | `nvarchar(50)` | yes | `UX_Roles_Code WHERE Code IS NOT NULL` | natural key for seeds and import |
| `IsPublic` | `bit` | no | `DF_Roles_IsPublic 0`; `IX_Roles_IsPublic (Id) WHERE IsPublic = 1` | the public-roles probe |
| `IsActive` | `bit` | no | `DF_Roles_IsActive 1` | |
| audit set | | no | `FK_Roles_CreatedById`, `FK_Roles_ModifiedById → core.Users(Id)` | |
| `ValidFrom`, `ValidTo` | `datetime2(7)` | no | period, shadow | |

**Public permissions** are the `IsPublic` flag: a public role's permissions apply to every active
user of the tenant. A public role has no memberships (`Roles.PublicRoleHasMembers` at validation;
the `Access.PublicRoleHasMembers` guard as backstop; the membership editor hides public roles); it
may not hold a permission with `Resource = '*'` (`Permissions.WildcardResourceOnPublicRole`);
several public roles may exist and union like any other; a deactivated public role grants nothing.
No public role is seeded and no separate table exists; a pack that wants a public lookup grant seeds
an ordinary public role found by `Code`.

The seeded **Administrator** role (`Id = 1`, `Code = 'Administrator'`) is the one role whose
unfiltered `*/*` grant is guaranteed: it cannot be made public, deactivated or deleted, and its
wildcard permission cannot be removed or filtered (`Roles.AdministratorImmutable`; guard L3 as
backstop). Its names remain editable and further permissions may be added. The role page shows
members as a read-only extra with links; memberships are edited from the user only.

### 3.6 `RoleMembership` and `core.RoleMemberships`

```csharp
// Tellma.Core.Abstractions.Access
[Table("RoleMemberships", Schema = "core"), TableType, Temporal, BumpsUserVersionTag(Permissions, "UserId")]
public class RoleMembership : ChildEntity
{
    [ParentKey] public int UserId { get; set; }
    public int RoleId { get; set; }                             // FK -> core.Roles
    public string? Notes { get; set; }
}
```

**`core.RoleMemberships`** — temporal → `core.RoleMembershipsHistory`; UDTT `RoleMembershipsList`;
`core.sq_RoleMemberships`: `Id int PK clustered`; `CK_RoleMemberships_Id CHECK ([Id] > 0)`; `UserId
int NOT NULL FK_RoleMemberships_UserId → core.Users(Id)` NO ACTION; `RoleId int NOT NULL
FK_RoleMemberships_RoleId → core.Roles(Id)` NO ACTION; `Notes nvarchar(1024) NULL`;
`UX_RoleMemberships_UserId_RoleId (UserId, RoleId)`; `IX_RoleMemberships_RoleId_UserId (RoleId,
UserId)` for "members of this role" (the members extra, the invariants); period. A child of `User`
only: synchronised under the user save (a membership absent from the saved list is deleted),
read-only from the role side. `Notes` stays: "added per ticket 4711" is what an access review asks
for. Each membership write bumps that user's `PermissionsTag`; the attribute is inherited by
distribution leaves.

### 3.7 `Permission` and `core.Permissions`

```csharp
// Tellma.Core.Abstractions.Access
[Table("Permissions", Schema = "core"), TableType, Temporal, BumpsVersionTag("permissions")]
public class Permission : ChildEntity
{
    [ParentKey] public int RoleId { get; set; }
    public string Resource { get; set; }                        // resource id or *
    public string Action { get; set; }                          // action or *
    public string? Filter { get; set; }                         // Queryex predicate over the securable's FilterRoot
    [Derived] public int? FilterLanguageVersion { get; set; }    // reset by the pipeline; stamped by the role save's validator
    public string? Notes { get; set; }
}
```

**`core.Permissions`** — temporal → `core.PermissionsHistory`; UDTT `PermissionsList`;
`core.sq_Permissions`: `Id int PK clustered`; `CK_Permissions_Id CHECK ([Id] > 0)`; `RoleId int NOT
NULL FK_Permissions_RoleId → core.Roles(Id)` NO ACTION; `Resource varchar(128) NOT NULL`; `Action
varchar(32) NOT NULL`; `Filter nvarchar(2048) NULL`; `FilterLanguageVersion int NULL`; `Notes
nvarchar(1024) NULL`; `CK_Permissions_WildcardHasNoFilter (Resource <> '*' OR Filter IS NULL)`;
`CK_Permissions_FilterVersion ((Filter IS NULL) = (FilterLanguageVersion IS NULL))`;
`IX_Permissions_RoleId (RoleId) INCLUDE (Resource, Action, Filter, FilterLanguageVersion)` so the
load by role is index-only; period. No unique index (`Filter` exceeds the key limit); exact
duplicates `(Resource, Action, Filter)` within a role are collapsed silently at save.

**Validation at role save** (`RoleAccessRules<TRole>`, the `IEntityValidator<TRole>` component
`CoreFeature` contributes, §1.2), per permission row `[i].Permissions[j]`:

1. `(Resource, Action)` exists in the registry (aliases considered), or is `*` in either position —
   else `Permissions.UnknownSecurable`.
2. A filter is allowed only when `Resource` is not `*` (`CK_Permissions_WildcardHasNoFilter` backs
   it) and the securable's `FilterRoot` is non-null — else `Permissions.FilterNotSupported`. The
   root is `ISecurableRegistry.Find(Resource, Action).FilterRoot`; for `Action = '*'` on a concrete
   resource it is the `FilterRoot` the resource's filterable securables share, and a resource with
   no filterable securable is `Permissions.FilterNotSupported`. Such a filter applies to every
   filterable action of the resource and never grants a non-filterable one (§5.2).
3. The stamp is determined from the before image (`context.Before(i)`, primed from RT1): the text
   is changed when the row is new, when the before image's `Filter` differs ordinally from the
   payload's, or when the stored stamp is null; changed text takes `QueryexLanguage.Version`,
   unchanged text keeps its stored stamp.
4. Unchanged text whose stored stamp is below `QueryexLanguage.Minimum` is
   `Permissions.FilterVersionUnsupported` at `[i].Permissions[j].Filter` (arguments
   `{ version, minimum }`): the user re-enters the text to affirm it under current rules; until then
   the row drifts (§9, `VersionUnsupported`).
5. `Filter` length ≤ `MaxFilterLength` — else `Permissions.FilterTooLong`, before the engine sees
   the text.
6. The filter is validated with `QueryexEngine.Validate(Filter, options)` (spec 0008 §1.4), the
   `ValidationOptions` being `Schema = ITenantDatabase.Schema`, `Root = root`,
   `Mode = QueryexMode.Filter`, `LanguageVersion = stamp`, `HasUser = true`, `Parameters = []` and
   `Limits = AccessLimits` (§12.2; an `@x` is a diagnostic, `me()` is allowed) — else
   `Permissions.FilterInvalid` at the same path with arguments `{ code }` plus the diagnostic's own
   arguments (spec 0008 §14). The schema is the tenant's current one, so a filter naming `Name2` on
   a mono-lingual tenant is invalid until a language is added; the settings save that adds one bumps
   the `permissions` tag (spec 0012 §6.3) and every cached set recompiles.
7. On success the component sets `FilterLanguageVersion` to the stamp of step 3; a null filter
   leaves it null (`CK_Permissions_FilterVersion`).
8. At most `MaxPermissionsPerRole` permissions on the role — else `Roles.TooManyPermissions`.
9. `Permissions.EscalationBeyondSelf` (§8.4).

**`FilterLanguageVersion`** is `[Derived]` (spec 0011 §2.4): the pipeline resets the client's
value on every row, and because the stamp depends on the before image the component sets it in its
continuation after RT1, never in a preprocess hook; the emitter writes it on insert and in every
`SET` list. The stamp is minted here and nowhere else.

### 3.8 Seeds and engine-facing names

`HasData` rows in the reserved band, immutable through the API:

| Row | Id | Values |
|---|---|---|
| `core.Users` | 1 | `Kind = System`, `Subject = NULL`, `Email = NULL`, `JoinedAt = 2000-01-01` (`State` reads `Joined`), `Name = 'System'`, `IsActive = 1`, `CreatedById = ModifiedById = 1` (self-referencing in one insert), `CreatedAt = ModifiedAt = 2000-01-01` |
| `core.UserStamps` | 1 | `PermissionsTag = 00000000-0000-0000-0000-000000000001`, `PreferencesTag = 00000000-0000-0000-0000-000000000002` |
| `core.Roles` | 1 | `Name = 'Administrator'`, `Code = 'Administrator'`, `IsPublic = 0`, `IsActive = 1`, audit → 1 |
| `core.Permissions` | 1 | `RoleId = 1`, `Resource = '*'`, `Action = '*'`, `Filter = NULL`, `FilterLanguageVersion = NULL` |
| `core.RoleMemberships` | 1 | `UserId = 1`, `RoleId = 1` |

The system user is the `CreatedById` of every seeded row, the identity of built-in schedules and
provisioning steps, and the bypass for the guards of §8.3; it can never authenticate (no subject, by
constraint). It is an administrator by membership: the evaluator's `IsSystem` short-circuit (§5.2)
is an optimisation, not a rule. `core.VersionTags` is seeded by the migrator from spec 0012's
registry, never by this spec.

Queryex entity names: `core.User` (`[core].[Users]`), `core.Role`, `core.RoleMembership`,
`core.Permission`, and the read-only `core.UserStamp`; navigations `CreatedBy`/`ModifiedBy →
core.User`, `Image → core.Blob`, `core.RoleMembership.User`/`.Role`, `core.Permission.Role`,
`core.UserStamp.User`. `Kind`, `State`, `InviteStatus`, `Gender` are string-typed enum properties.
`UserPreferences` is not exposed.

### 3.9 Deletion and deactivation

- A `User` is deletable only while `State = New` and `Kind <> System`
  (`Users.OnlyNewUsersDeletable`): every user that ever acted is referenced by audit FKs and must be
  deactivated instead; a `New` service account has no client to delete. The delete recipe removes
  `RoleMemberships` explicitly (children first); `UserStamps`, `UserPreferences` and
  `NotificationPreferences` cascade. Deleting oneself is `Users.CannotDeleteSelf`.
- A `Role` is deletable when it has no memberships (`Roles.HasMembers`) and is not the
  Administrator role; its permissions are deleted explicitly first.
- **Deactivation** — `deactivate` on `core.User` flips `IsActive` through the pipeline (stamps
  `ModifiedAt`; `UserAccessRules` refuses the caller's own row with `Users.CannotDeactivateSelf`,
  §8.4). The next prologue on any instance refuses the user (§7.6); no cached state is consulted.
  After commit, spec 0017's `UserService` calls spec 0010's
  `ISessionTerminationListener.TenantAccessRevokedAsync(tenantId, subject)`, which closes the
  user's hub connections for this tenant. For a service account spec 0017 also deletes the identity
  client after commit (§7.7). The distribution session cookie is untouched: the user may be a
  member of other tenants of the same distribution.

## 4. Securables and the registry

### 4.1 Contracts

```csharp
// Tellma.Core.Abstractions.Access
public sealed record SecurableRef(string Resource, string Action);

public sealed record SecurableOwner(string Resource, string Navigation);   // a child entity's owner: its grants reach the child through Navigation, a to-one path (§5.5)

public sealed record SecurableDescriptor(
    string Resource, string Action, string? FilterRoot, bool IsSensitive, string Feature, SecurableOwner? Owner)
{
    public bool SupportsFilter { get; }                         // derived: FilterRoot is not null
}

public sealed class SecurableRegistryBuilder
{
    public SecurableRegistryBuilder Add(
        string resource, string action, string? filterRoot, bool isSensitive = false, SecurableOwner? owner = null);
    public SecurableRegistryBuilder Alias(string oldResource, string newResource);
    public SecurableRegistryBuilder MarkSensitive(string resource, string action);
    public SecurableRegistryBuilder MarkNotSensitive(string resource, string action);
}

public interface ISecurableContributor
{
    void Contribute(SecurableRegistryBuilder builder);
}

public interface ISecurableRegistry                             // singleton; immutable; frozen at realise
{
    IReadOnlyList<SecurableDescriptor> All { get; }             // ordered by resource, then action
    IReadOnlyList<string> Resources { get; }
    SecurableDescriptor? Find(string resource, string action);  // aliases resolved; ordinal-ignore-case
    IReadOnlyList<SecurableDescriptor> ForResource(string resource);
    string Fingerprint { get; }                                 // hex SHA-256; deployment-scoped
}

public sealed record SecurableEndpointMetadata(string Resource, string Action);

public sealed record MemberEndpointMetadata();

public sealed record NoActivityStampMetadata();
```

| Member | Meaning |
|---|---|
| `SecurableDescriptor.FilterRoot` | The entity name filters bind against (`gl.Center`); `null` = filters unsupported. A name, not a descriptor, because the tenant's schema is rebuilt per configuration and descriptors compare by identity. |
| `SecurableDescriptor.Feature` | The composition feature that registered it; diagnostics only. |
| `Add` | Declares a securable. The same tuple with the same filter root is idempotent; a different filter root is a composition problem. |
| `Alias` | Stored permissions naming `oldResource` resolve to `newResource` for one release while the migrator's data step rewrites them. |
| `MarkSensitive` | Adds a pair to the step-up set (§4.3). |
| `MarkNotSensitive` | Removes a pair from the step-up set, Core's defaults included; marking the same pair both ways is a composition problem. |
| `Find` | The evaluator's and the validators' resolution; `null` for an unknown pair. |
| `Fingerprint` | SHA-256 over the sorted `Resource\|Action\|FilterRoot\|IsSensitive` lines; changes only on deploy; travels in `me` so the role editor caches the securables list under it. Never stored in a tenant database. |
| `SecurableEndpointMetadata` | Stamped on every projected stack endpoint and on hand-mapped endpoints through spec 0010's `RequireSecurable(resource, action)`; metadata for the startup audit, OpenAPI and step-up — enforcement is spec 0014's pipeline and `IApiActionInvoker`. |
| `MemberEndpointMetadata` | Any connected active member; no securable. Stamped by `[ApiAction(MemberOnly = true)]` and `AllowMember`; metadata for the startup audit, OpenAPI and step-up — enforcement is spec 0014's pipeline and `IApiActionInvoker`. |
| `NoActivityStampMetadata` | Polling endpoints; the prologue binds `@tm_StampActivity = 0`. |

### 4.2 Who registers what

A distribution writes no securable registration for its own entities and actions:

- The stack feature's `ISecurableContributor` (spec 0014 §2.7) reads `IStackRegistry` and registers,
  per `StackDescriptor`, every standard operation's action of spec 0014 §2.4 (`Read` when the stack
  has `Query`, `Details` or `Export`; `Save`, `Delete` per `Operations`; `Activate` when the stack
  is activatable and has `Save`) as `(Resource, Action, FilterRoot = Resource)`, every
  `EntityActionDescriptor.Action` (`FilterRoot = Resource` when `SupportsFilter`, else `null`) and
  every non-null `ApiActionDescriptor.Securable` of the service and its companions with
  `FilterRoot = Resource`. Tree stacks add nothing (`get-by-parent-ids` is `Read`,
  `delete-with-descendants` is `Delete`); export is `Read`; import is `Save`. `[Cacheable]` stacks
  register `Read` with `FilterRoot = null`.
- `contribution.ApiService<T>()` registers the `Securable` of every `ApiActionDescriptor` of an
  `[ApiRoute]` service (the attribute's `Resource` and `Action`) with `FilterRoot = null`; a
  member-only action has a null `Securable` and registers nothing.
- `contribution.Securables(configure)` (spec 0010's `SecurablesContributionItem`) is for non-entity
  securables: settings categories (`core.Settings.General` and `core.Settings.<Category>` ×
  `Read | Save`, registered by spec 0012) and hand-mapped endpoints.
- `RequireSecurable(resource, action)` on a hand-mapped endpoint stamps the metadata; the startup
  audit (§4.5) requires the pair to be registered.

### 4.3 Sensitive securables

`IsSensitive = true` marks the step-up set; step-up follows the `(Resource, Action)` pair alone,
so `delete-by-query` is sensitive exactly when `(R, Delete)` is. Core's defaults: `core.User` ×
`Save | Delete | Activate | Invite | Credentials`, `core.Role` × `Save | Delete | Activate`,
`core.Settings.General` × `Save`, `core.Schedule` × `Save | Delete | Activate`. A distribution may
add pairs (`MarkSensitive`) and remove any, Core's defaults included (`MarkNotSensitive`, §4.1).
A sensitive pair is refused on the MCP surface (spec 0015 §11.6), so removing one also opens its
operation to agents. The projection (spec 0015) copies the flag onto the endpoint as spec 0010's
`RequireAssuranceMetadata` from `Tellma:Session:StepUp { Acr, MaxAge }`, the host issues
spec 0003's `401 insufficient_user_authentication` challenge when the session's assurance is
below the bar, and spec 0014's pipeline raises `StepUpRequiredException` through `RequireAsync`
(§5.1) for a caller no endpoint guard saw. Member endpoints are never sensitive.

### 4.4 The registry as data source and oracle

The registry is the role editor's data source (the SPA labels its resource and action keys from
spec 0012's string pack), the validators' oracle (§3.7), and the drift oracle
(§9). `Alias` entries resolve on `Find` only; `All` lists registered names.

### 4.5 Startup validation

Aggregated with the realised gate of spec 0010: no duplicate `(Resource, Action)` with different
filter roots; every `FilterRoot` resolves to an entity of the distribution's Queryex schema; a child
entity is accepted only when the securable declares `Owner`, whose `Navigation` is a to-one path
from `FilterRoot` to the owner resource's filter root; every `Owner` names a registered resource,
and the owner chain is acyclic; `*` is not a resource or action name; every resource and action
matches the grammar of §2; every `SecurableEndpointMetadata` on any endpoint names a registered
pair, which an attribute's pair always is, because §4.2 registers the pair the attribute names (what
that means for a misspelled action: spec 0015 §4.6). The endpoint-shape rules — exactly one of the
two metadata records on every tenant endpoint, `AllowAnonymous` only on a tenantless endpoint — are
spec 0010 §5.3's `TellmaEndpointAudit`, which walks `EndpointDataSource` in the same gate; any
finding of either check fails startup.

### 4.6 Hard to leave unsecured

Four layers, each catching what the previous cannot:

1. **Fallback policy.** The application's fallback authorization policy denies; each tenant route
   group calls `RequireAuthorization(TellmaPolicies.<Surface>)` (spec 0015). An endpoint with no
   authorization metadata is never reachable.
2. **Tenant group.** Every tenant route group carries the tenant endpoint filter (tenant resolution,
   sandbox, state verdicts, the request context and the connect initializer of §7.8), so every
   projected and hand-mapped endpoint inherits it.
3. **Endpoint metadata.** Every endpoint carries one of the two metadata records (§4.5) for the
   startup audit, OpenAPI and step-up; the securable itself is evaluated by spec 0014's pipeline
   and `IApiActionInvoker`, where every caller passes, never by an endpoint filter, and it is
   deliberately not an ASP.NET authorization requirement: the authorization middleware runs before
   the tenant filter has resolved the caller. Filter order on the group is spec 0015 §5.1's; the
   witness filter runs last.
4. **Authorization witness.** `IAccessEvaluator.Evaluate`/`Require` record every evaluated pair on
   the request; the connect initializer records the member witness. A filter installed by the group
   asserts, before a 2xx result is written, that the endpoint's declared securable (or the member
   witness) was evaluated during the request; otherwise it replaces the result with 500, logs
   `AccessEvents.MissingWitness` at Error and increments `tellma.access.witness.missing`. Data
   never leaves through a handler that did not ask.

The record-level check lives only in the service: the pipeline (spec 0014) obtains one
`AccessDecision` per host operation and applies its `FilterTree` to every query and its pre- and
post-checks to every write the operation makes of its own rows (§5.3 covers enlisted groups);
custom service code obtains the same decision through `IAccessEvaluator.Require`, the only API
that yields a filter. MCP tools, jobs and tests reach services without these endpoints, which is
why the check lives where every caller passes.

## 5. Permission evaluation

### 5.1 Contracts

```csharp
// Tellma.Core.Abstractions.Access
public enum AccessOutcome { Denied, Filtered, Unrestricted }

public enum AccessProblemCode { UnknownResource, UnknownAction, FilterUnsupported, FilterInvalid, VersionUnsupported }

public abstract record AccessGrant(string Resource, string Action);

public sealed record RoleAccessGrant(
    string Resource, string Action, int PermissionId, int RoleId, string RoleName, bool IsPublic, string? Filter,
    int? FilterLanguageVersion) : AccessGrant(Resource, Action);

public sealed record BespokeAccessGrant(string Resource, string Action, string? Filter, string Reason)
    : AccessGrant(Resource, Action);

public sealed record SystemAccessGrant(string Resource, string Action) : AccessGrant(Resource, Action);

public sealed record AccessProblem(
    int PermissionId, int RoleId, AccessProblemCode Code, IReadOnlyList<QueryexDiagnostic> Diagnostics);

public sealed record AccessDecision(
    string Resource, string Action, AccessOutcome Outcome, FilterTree? Filter, IReadOnlyList<AccessGrant> Grants,
    IReadOnlyList<AccessProblem> Problems)
{
    public bool IsAllowed { get; }                              // derived: Outcome != Denied; Filter is non-null exactly when Filtered
}

public sealed record AccessCriterion(string Action, FilterTree Filter, string Reason);   // bespoke; from an IAccessCriteriaProvider; a current-version tree

public interface IAccessCriteriaProvider                        // registered per resource; several may exist
{
    string Resource { get; }
    Task<IReadOnlyList<AccessCriterion>> GetCriteriaAsync(int userId);
}

public sealed record UserAccess                                 // the cached set; immutable
{
    public const int FormatVersion = 1;                         // the cached shape's format version
    public int TenantId { get; init; }
    public int UserId { get; init; }
    public Guid Tag { get; init; }
    public Guid UserTag { get; init; }
    public DateTimeOffset ComputedAt { get; init; }
    public DateTimeOffset ValidatedAt { get; init; }
    public bool IsSystem { get; init; }
    public IReadOnlyList<RoleAccessGrant> Grants { get; init; }
    public IReadOnlyList<AccessProblem> Problems { get; init; }
    public AccessDecision Decide(
        SecurableDescriptor securable, IReadOnlyList<AccessCriterion> criteria, AccessDecision? owner);   // owner: the (Owner.Resource, Action) decision, evaluated first; null without Owner
    public static UserAccess System { get; }                    // unrestricted; never loaded
}

public interface IAccessEvaluator                               // scoped
{
    Task<AccessDecision> EvaluateAsync(string resource, string action);   // ambient caller; records a witness
    Task<AccessDecision> RequireAsync(string resource, string action);    // ForbiddenException when Denied; StepUpRequiredException when the pair is sensitive and the session is below spec 0010's bar
    Task<IReadOnlyList<AccessDecision>> EvaluateAsync(IReadOnlyList<SecurableRef> securables);   // several at once, one set
    Task<AccessDecision> EvaluateForAsync(int userId, string resource, string action);   // "can they, and why"; no witness
    Task<IReadOnlyList<AccessDecision>> EvaluateAllAsync();                         // one per registered securable
    Task<UserAccess> GetAccessAsync();
    void Invalidate(int tenantId, int userId);
}
```

| Member | Meaning |
|---|---|
| `AccessGrant` | One grant behind a decision. On the wire (`access/check`, spec 0015 §3.3) a grant carries a `kind` discriminator ∈ `role`, `bespoke`, `system` (JSON-polymorphic under the platform options). |
| `RoleAccessGrant` | One matching stored row of an active role the caller belongs to or of an active public role (`IsPublic`): `Resource`/`Action` as stored, wildcards preserved; `Filter` `null` = unrestricted; `FilterLanguageVersion` the row's stamp, which the leaf of §5.2 step 5 carries. `UserAccess.Grants` holds these. |
| `BespokeAccessGrant` | A bespoke criterion that contributed to a `Filtered` decision: `Resource` the decided securable's, `Action` the criterion's, `Filter` its tree as text, `Reason` its reason key. |
| `SystemAccessGrant` | The system short-circuit (§5.2 step 1). |
| `AccessProblem` | A stored row that grants nothing and why (§9). |
| `AccessCriterion` | A bespoke criterion, supplied only by the `IAccessCriteriaProvider`s registered for a resource (several may serve one resource): `Action` the action it grants, or `*`; `Filter` a current-version tree; `Reason` a short reason key the provider chooses — the platform's own-rows criteria use `self`. `BespokeAccessGrant.Reason` is that key. |
| `UserAccess.Tag` / `UserTag` | The tenant `permissions` tag and the caller's `PermissionsTag` the set was built under; the entry is valid only while both match. |
| `UserAccess.ValidatedAt` | The last prologue that confirmed both tags; bounds the deny re-check. |
| `Evaluate` | Loads or reuses the caller's set (§6), resolves the securable, collects the bespoke criteria of every `IAccessCriteriaProvider` registered for the resource (`GetCriteriaAsync(userId)`, awaited once per evaluation), calls `Decide`, records the witness, counts `tellma.access.decisions{outcome}`. A `Denied` decision from a set whose `ValidatedAt` is older than `FastDenyWindow` is re-verified by one prologue-only round trip and decided again on the refreshed set (§6.4). A caller context with no user is `Denied` for everything. |
| `Require` | `Evaluate`, then `ForbiddenException` on `Denied` and `StepUpRequiredException` when the pair is sensitive (§4.3) and the session's assurance is below spec 0010's bar. The call spec 0014's pipeline makes for every operation and `IApiActionInvoker` for every `[ApiAction]` (spec 0014 §9.1), so the refusal reaches every caller, request or not. |
| `EvaluateFor` | The "can they, and why" path for another user: loads that user's rows through a `ConnectPremises.ForUser` whose nullable expected tags are `null` (§7.5) once per scope — the load is memoised, so every `EvaluateFor` of one scope shares it — without touching the caller's set or the cache. |
| `Invalidate` | Drops the cached set; called by the connector when a prologue reports a deactivated user. |

### 5.2 The composition contract

`UserAccess.Decide` is a pure function over the cached, resolved set — no I/O, no clock, no ambient
state. For a securable `(R, A)`:

1. `IsSystem` → `Unrestricted` with one `SystemAccessGrant`.
2. Candidates = the caller's grants (active-role memberships and every active public role, resolved
   at build time) whose resource equals `R` or is `*` and whose action equals `A`, is `*`, or —
   when `A = Read` — is any action: **a grant on `(R, A' ≠ Read)` also matches `(R, Read)` with the
   same filter**, never a wider one. No other implication exists.
3. When the securable's `FilterRoot` is `null`, candidates keep only unfiltered rows: a filtered row
   never grants a non-filterable action.
4. Any unfiltered candidate → `Unrestricted`; `Grants` lists every candidate.
5. Otherwise leaves = the distinct `(Filter, FilterLanguageVersion)` pairs of the candidates, sorted
   ordinally by text then by stamp, each as `FilterTree.Leaf(text, stamp)` (spec 0011 §11.2), plus
   the `Filter` tree of every bespoke criterion whose `Action` equals `A` or `*`. Empty → `Denied`
   (`Grants` empty; `Problems` = the excluded rows that would have matched). Otherwise `Filtered`
   with `Filter = FilterTree.Or(leaves)`; `Grants` = the filtered candidates and one
   `BespokeAccessGrant` per contributing criterion. For a securable that declares `Owner`, steps 4
   and 5 give the direct side, and the decision is the owner's filter rebased through
   `Via(Owner.Navigation)` per §5.5, unioned with the direct leaves.

Rules encoded: inactive roles were excluded at load and memberships of inactive users never reach
evaluation (the prologue refuses); a public role's rows are in every caller's set as grants with
`IsPublic` set; drifted rows contribute nothing; an unfiltered grant absorbs every filtered one;
filters union; an empty `Or` is `false` and the decision short-circuits to `Denied` before anything
compiles; identical filter texts under one stamp across roles contribute one leaf and leaves are
sorted, so two users with the same grant shape produce byte-identical SQL and the engine's caches
are hit rather than fragmented; bespoke criteria can allow access on their own (the shape "documents
assigned to me" needs) and never absorb or remove anything.

### 5.3 What the pipeline does with a decision

Stated here because this spec owns the semantics; spec 0014 implements them:

- `decision.Filter` is conjoined into every read, update and delete as
  `FilterTree.And([userFilter, decision.Filter])`; the access filter alone is passed as
  `AncestorsFilter` on tree reads.
- A navigation path into an entity `E` other than the query's root may touch every column of `E`
  only when the caller's `Read` decision on `E` is `Unrestricted`; a filtered grant, like no grant,
  limits the path to `E`'s `[RelatedSelect]` projection in select, filter, order and having alike,
  and a column outside it is `ForbiddenException` (403) naming `E` (spec 0014 §5.2 step 4). A
  traversal therefore never reveals more than a direct query of `E` would, apart from the declared
  display projection. The joined rows are not filtered by `E`'s row-level security; only projection
  columns are reachable through a filtered or absent grant, so nothing hidden leaks.
- Collection operations on `Denied` throw `ForbiddenException` (403, so the SPA can hide the page);
  a by-id read whose row is outside the filter is `NotFoundException` (404 — the same response as a
  non-existent row).
- Saves run the **two-stage pre-check** in the validation round trip: the existing rows' before
  images are loaded under the caller's `Read` filter (an id absent → `NotFoundException`, 404) and
  the same ids are counted under the `Save` grant — a short count → `ForbiddenException` (403: the
  caller already knows the id, and 403 stops a probing loop of save attempts). Both are compared in
  C#. Deletes and the built-in actions run the same two stages as statements inside the persist
  batch — the caller's `Read` filter first (404, hidden equals missing), then the action's grant
  (`Delete`, `Activate`; 403); spec 0014 §9.2 owns the statements.
- The **post-check** runs inside the persist transaction over every saved root id: spec 0014
  appends a `50403` count assertion — the compiled count of the decision's filter restricted to
  `KeySetRestriction("Id", "@tb{b}_saved")` against the count of `@tb{b}_saved` — and a mismatch
  is `THROW 50403, N'RowSecurity'` → `ForbiddenException`. The post-check is authoritative for
  the rows as saved; if permissions changed between the two round trips the persist guard fails
  first (§7.4) and the recomposed persist evaluates the new filter.
- Details pages load related entities and extras under the root's `Read` without further
  row-level filtering, narrowed to each target's `[RelatedSelect]` projection — the same bound as
  a traversal, and the same knowing bypass of the row filter: a document's customer name is
  visible to whoever can see the document.
- FK references in a save are validated under the target's `Read` filter (spec 0014).
- Users, jobs, exports, imports and notifications are self-scoped by a bespoke criterion that
  their owning spec's `IAccessCriteriaProvider` supplies (spec 0017 §3.2, spec 0019 §11.2,
  spec 0018 §12.1, spec 0020 §2.2), so a caller with no stored grant sees their own rows.
- An enlisted group (spec 0014 §13.3) carries no decision of its own: the host operation's decision
  — its securable, the job's run-as scope or the step's system scope — authorises every group in the
  frame, and no pre-check or post-check runs over enlisted rows.

### 5.4 Row-level security is `FilterTree`, never a database policy

SQL Server security policies are not used: they are logic in the database; a schema-bound inline
function cannot hold user-authored Queryex with navigations, `descendantOf` and `me()` and cannot be
redeployed on every role edit; schema binding blocks `ALTER COLUMN` and fights expand-then-contract;
history tables are unprotected by default; indexed views are impossible on RLS tables; `dbo` is
filtered too, so the migrator, support tooling and background jobs would need a bypass coded into
the predicate — a fail-open hazard; the "can I, and why" query needs the model in C# regardless; and
tenant isolation, the headline RLS use case, is already physical. `SESSION_CONTEXT` is reserved for
correlation ids, never for authorisation.

### 5.5 Child entities as query roots — `FilterTree.Via`

A child entity exposed as a query root (a read-only reporting stack over `RoleMembership`; later,
invoice lines) is a securable in its own right. Its securable declares its owner,
`Owner = (OwnerResource, OwnerNavigation)` — `core.RoleMembership` → (`core.User`, `User`) — and a
caller's permissions for `(C, A)` are their own grants on `(C, A)` **plus** their grants on
`(OwnerResource, A)`, rebased through `FilterTree.Via(navigation, inner)` (spec 0011 §11.2), under
which every path in `inner` resolves from the navigation's target. The evaluator decides
`(OwnerResource, A)` first, recursively, since the owner may itself be a child with an owner (an
owner pair the registry does not hold is `Denied`), and passes that decision to `Decide`, which
composes it with the direct side of §5.2:

- the direct candidates or the owner's decision `Unrestricted` → `Unrestricted`;
- both `Denied` → `Denied`;
- otherwise `Filtered` with `Filter = Or(directLeaves…, Via(Owner.Navigation, ownerFilter))`,
  omitting whichever side is empty.

`Grants` lists the direct candidates and the owner's grants; `Problems` lists both sides'. Rewriting
permission text (`PostingDate > X` → `Parent.PostingDate > X`) is rejected: it needs a parser on the
host side, mangles literals and `me()`, and would re-stamp language versions.

## 6. The permission set and its cache

### 6.1 Two-level validation

The cached `UserAccess` for `(TenantId, UserId)` is valid while **both** the tenant `permissions`
tag (`core.VersionTags`, bumped by every write to `core.Roles` and `core.Permissions` through
`[BumpsVersionTag("permissions")]`, and by spec 0012's `SettingsService.Save` when a language
column changed, because the Queryex schema changes) **and** the caller's `PermissionsTag`
(`core.UserStamps`, bumped by every write to that user's `RoleMemberships` through
`[BumpsUserVersionTag(Permissions, "UserId")]`) match the values the prologue read. `core.Users`
writes bump neither: `IsActive`, `Kind` and `State` are read live by the prologue on every batch. A
role edit therefore invalidates every member's set at their next request through the tenant tag; a
membership edit invalidates one user's set through their user tag; no fan-out write exists in any
path.

### 6.2 Entry, store, build

- **Entry** — `UserAccess`: the two tags, `ComputedAt`, `ValidatedAt`, the resolved
  `RoleAccessGrant`s, each with its filter text and `FilterLanguageVersion` (filter texts interned
  per instance so a role's filter is one object however many members it has), the problems (§9),
  memoised composed trees per `(resource, action)`, and `FormatVersion = 1`.
- **Store** — spec 0012's `VersionedCache<(int TenantId, int UserId), UserAccess>` of kind
  `permissions`: a private bounded `MemoryCache` sized in entries by spec 0012's
  `TellmaCacheOptions.PermissionsEntries` (no age limit in the store; eviction is the runtime's
  least-recently-used compaction), fed by `Apply` from a prologue result and never by the store's
  loader — the connector single-flights cold connects per `(TenantId, Subject)` (§7.1), so a burst
  from one user on a cold instance issues one prologue — entries replaced never mutated,
  `TrackStatistics` on for the gauges. Every key begins with the tenant id: user ids are
  per-tenant integers and one subject belongs to several tenants; two tenant databases sharing
  user ids never see each other's entries.
- **Maximum age** — `PermissionsMaxAge` (default 2 h): when the cached set is older, the next
  prologue binds `@tm_ReloadPermissions = 1` and returns the rows without failing the guard; the
  set is rebuilt. This bounds the damage from an out-of-band edit that forgot the bump.
- **Build** — rows arrive from the prologue (§7.3), whose tag reads precede the row select, so a
  bump racing the load yields an older tag than the data (one spurious rebuild next request), never
  a newer tag over older data. Each row is resolved against the registry and each distinct
  `(text, stamp)` pair validated once with `Validate` under its stamp (the engine's own caches make
  repeats free); unresolved rows become problems. `tellma.access.set.build.duration` and
  `tellma.access.set.grants` are recorded per build.
- **Staleness window** — one in-flight batch per instance: under read-committed snapshot a role
  edit committing between the prologue's tag read and a body statement is not observed by that
  body; the next request sees the new tag. On a persist batch the window is closed by the
  in-transaction re-check (§7.4).

### 6.3 The connect and profile caches

Two more caches share the store shape: the connect cache keyed `(TenantId, Subject)` →
`ConnectedUser` (not a `VersionedCache` kind — the prologue re-validates its premises on every
request; bounded in entries by `TellmaCacheOptions.PermissionsEntries`, the limit of the set it
composes; the entry composes the caller's `UserAccess`, `UserProfile` and tenant tags), and spec
0012's `preferences` kind keyed `(TenantId, UserId)` → `UserProfile` (size limit
`TellmaCacheOptions.PreferencesEntries`), valid while the caller's `PreferencesTag` matches. Both
are refreshed by `IUserConnector.Apply` from a prologue result and dropped by `Invalidate` and by a
refusing prologue.

### 6.4 Denials and the window

`Evaluate` answers from the cached set. A `Denied` decision from a set whose `ValidatedAt` is older
than `FastDenyWindow` (default 5 s; `ValidatedAt` is the confirming prologue's completion time)
triggers one prologue-only reconnect through the connector — the cold connect of §7.1 — and a second
decision on the refreshed set; a set validated within the window answers with no round trip. A
zero-grant caller hammering an endpoint therefore costs one round trip per window, and a user
granted a permission a moment ago is admitted within one window, never after `PermissionsMaxAge`.

## 7. The connect prologue and the guarded runner

### 7.1 Contracts

```csharp
// Tellma.Core.Abstractions.Access
public sealed record UserProfile(
    string Name, string? Name2, string? Name3, string? Email, string? ContactEmail, string? ContactMobile,
    int? ImageId, int? SignatureId, string? PreferredLanguage, string? PreferredCalendar,
    string? PreferredTimeZone, Gender? Gender);

public sealed record ConnectedUser(
    int UserId, UserKind Kind, UserState State, UserProfile Profile, Guid PreferencesTag, UserAccess Access,
    IReadOnlyDictionary<string, Guid> TenantTags, DateTimeOffset? LastActivityStampedAt);

public abstract record ConnectPremises
{
    public sealed record ForSubject(
        string Subject, int? ExpectedUserId, ConnectExpectations Expected, bool StampActivity, bool AllowStateFlip,
        bool ReloadPermissions) : ConnectPremises;
    public sealed record ForUser(int UserId, ConnectExpectations Expected, bool ReloadPermissions) : ConnectPremises;
    public sealed record ForSystem(Guid ExpectedSettingsTag) : ConnectPremises;       // not "System": that would shadow the namespace
    public static ForSubject Cold(string subject, Guid expectedSettingsTag, bool stampActivity, bool allowStateFlip);
    public static ForSubject For(ConnectedUser user, string subject, bool stampActivity, bool allowStateFlip);
}

public sealed record ConnectExpectations(
    Guid? PermissionsTag, Guid? UserPermissionsTag, Guid? PreferencesTag, Guid SettingsTag);

public sealed record PermissionRow(
    int PermissionId, int RoleId, string RoleName, bool IsPublic, string Resource, string Action, string? Filter,
    int? FilterLanguageVersion);

public sealed record ConnectResult(
    int? UserId, UserKind? Kind, bool IsActive, UserState? State, Guid? PreferencesTag, Guid? UserPermissionsTag,
    IReadOnlyDictionary<string, Guid> TenantTags, bool PermissionsStale, bool ProfileStale, bool SettingsStale,
    bool GuardPassed, IReadOnlyList<PermissionRow>? PermissionRows, UserProfile? Profile);

public interface IUserConnector                                 // scoped
{
    Task<ConnectedUser> ConnectAsync();
    Task<ConnectedUser> ConnectAsUserAsync(int userId);
    Task<ConnectedUser> ConnectAsSystemAsync();
    BatchResult<ConnectResult> Contribute(IDataBatch batch, ConnectPremises premises);
    ConnectedUser Apply(ConnectResult result);
}

public interface IGuardedBatchRunner       // scoped; the only way a service executes a batch on a caller's behalf
{
    Task<TResult> RunAsync<TResult>(
        BatchPurpose purpose, Action<ConnectedUser, IDataBatch> compose, Func<ConnectedUser, BatchOutcome, TResult> read);
}
```

| Member | Meaning |
|---|---|
| `ConnectedUser.LastActivityStampedAt` | The per-instance throttle memo: when within `ActivityStampInterval`, the premises carry `StampActivity = false` and the `UPDATE` is not even attempted. |
| `ConnectPremises` | What one prologue assumes; the case selects the variant of §7.3: `ForSubject` resolves the caller by subject and may stamp and flip, `ForUser` resolves a user by id and never stamps or flips, `ForSystem` looks no user up. |
| `ConnectExpectations` | The cached tags the prologue compares (§7.2); a `null` tag is not cached, and that part reads stale. |
| `ConnectPremises.Cold` | A `ForSubject` with every nullable tag of `Expected` `null`, `ExpectedUserId` `null`, `Expected.SettingsTag` the cached settings entry's tag or `Guid.Empty`: the prologue returns rows and profile unconditionally, and the settings when the tag differs. |
| `ConnectPremises.For` | A `ForSubject` with the expected values from the cached entry; `ReloadPermissions = true` when the set is older than `PermissionsMaxAge`. `StampActivity` and `AllowStateFlip` are `false` on a `ReadOnly` tenant. |
| `Connect` | The cache keyed `(TenantId, Subject)`, or a cold prologue-only round trip (`BatchPurpose.Read`, no body), single-flighted per key. Throws `TenantNotFoundException` for an unknown subject or a deactivated user. |
| `ConnectAsUser` | Job scopes (spec 0019): a `ConnectPremises.ForUser`; no stamp, no flip. |
| `ConnectAsSystem` | A `ConnectPremises.ForSystem`; tags, and the settings on a miss; `UserAccess.System`. Throws `InvalidOperationException` unless `RequestContext.Kind = System` (the migrator, provisioning steps, system job scopes) — never reachable from a request scope. |
| `Contribute` | The `IDataBatchContributor` at `Order` 100 for the prologue stage: prepends the text of §7.3, binds its parameters, and returns the result read from sets 0–3. Spec 0012's contributors at `Order` 50 and 60 yield to it on caller batches: set 1 is the tag read, and the settings sets after set 3 are the cold load's. |
| `Apply` | Replaces the connect, permissions and profile cache entries from a result and sets `RequestContext.UserId`. |
| `Run` | Connects (cache or cold), creates the batch for `purpose`, calls `Contribute(batch, premises)` — which marks the batch as carrying the connect prologue, the mark spec 0012's contributors at `Order` 50 and 60 yield to — composes, executes, reads. On `GuardPassed = false` it applies the result (fresh rows, fresh profile, and the fresh settings spec 0012's contributor read from the same round trip, after which spec 0012's initializer re-runs so the context carries them) and recomposes **once**; a second failure raises `StaleContextException` (503, `Retry-After: 1`) and logs `AccessEvents.GuardThrash`. A `50412` from the persist re-check (§7.4) counts as one failure: the runner re-connects cold, which returns fresh rows, profile, settings and tags, and recomposes once; a second `50412` is the second failure. |

### 7.2 Parameters

Bound by the contributor from the `ConnectPremises` case and its `ConnectExpectations` (`Expected`);
`@tm_` is reserved for the prologue, the tag prelude, the guard and the bump, as `@qx` is for the
engine and `@tb` for per-statement platform names. Distribution SQL uses none of them.

| Parameter | Type | Value |
|---|---|---|
| `@tm_Subject` | `varchar(255)` | `ForSubject.Subject`: the caller's `sub`; a service account's client id when `Kind = Service` |
| `@tm_UserId` | `int` | `ForUser.UserId`, in that variant only: bound as a parameter instead of declared; the resolve reads `WHERE U.[Id] = @tm_UserId` |
| `@tm_StampActivity` | `bit` | `ForSubject.StampActivity`: 0 under `NoActivityStampMetadata`, within the throttle memo and on `ReadOnly` tenants; 0 in the `ForUser`/`ForSystem` variants |
| `@tm_AllowFlip` | `bit` | `ForSubject.AllowStateFlip`: 0 on `ReadOnly` tenants; 0 in the `ForUser`/`ForSystem` variants |
| `@tm_ExpectedUserId` | `int` | `ForSubject.ExpectedUserId`: the cached user id, `NULL` on the cold path; `ForUser.UserId` in that variant |
| `@tm_ExpectedPermissionsTag` | `uniqueidentifier` | `Expected.PermissionsTag`: the cached set's tenant tag; `NULL` when not cached |
| `@tm_ExpectedUserPermissionsTag` | `uniqueidentifier` | `Expected.UserPermissionsTag`: the cached set's user tag; `NULL` when not cached |
| `@tm_ExpectedPreferencesTag` | `uniqueidentifier` | `Expected.PreferencesTag`: the cached profile's tag; `NULL` when not cached |
| `@tm_ExpectedSettingsTag` | `uniqueidentifier` | `Expected.SettingsTag` (`ForSystem.ExpectedSettingsTag`): the tenant `settings` tag the schema was built from |
| `@tm_ReloadPermissions` | `bit` | `ReloadPermissions`: 1 when the cached set is older than `PermissionsMaxAge`; 0 in the `ForSystem` variant |
| `@tm_SettingsStrict` | `bit` | 1 on `Validate` and `Persist` batches: a stale `settings` tag fails the guard (spec 0012's `Rerun` policy for those purposes) |
| `@tm_LoadSettings` | `bit` | bound by spec 0012's cold-load contributor: 1 when its cache missed at composition |

### 7.3 The prologue text (`ForSubject` variant)

```sql
DECLARE @tm_UserId int, @tm_Kind varchar(8), @tm_IsActive bit, @tm_State varchar(8),
        @tm_PreferencesTag uniqueidentifier, @tm_UserPermissionsTag uniqueidentifier,
        @tm_PermissionsTag uniqueidentifier, @tm_SettingsTag uniqueidentifier,
        @tm_PermissionsStale bit = 0, @tm_ProfileStale bit = 0, @tm_SettingsStale bit = 0, @tm_Guard bit = 0,
        @tm_Now datetimeoffset(3) = SYSUTCDATETIME();

-- Resolve the caller: one seek on the covering Subject index, one clustered seek on UserStamps.
SELECT @tm_UserId = U.[Id], @tm_Kind = U.[Kind], @tm_IsActive = U.[IsActive], @tm_State = U.[State],
       @tm_PreferencesTag = S.[PreferencesTag], @tm_UserPermissionsTag = S.[PermissionsTag]
FROM [core].[Users] AS U
JOIN [core].[UserStamps] AS S ON S.[UserId] = U.[Id]
WHERE U.[Subject] = @tm_Subject;                                   -- ForUser variant: WHERE U.[Id] = @tm_UserId

-- Tags are read before any permission rows (the safe direction under a racing bump).
SELECT @tm_PermissionsTag = [Tag] FROM [core].[VersionTags] WHERE [Name] = N'permissions';
SELECT @tm_SettingsTag    = [Tag] FROM [core].[VersionTags] WHERE [Name] = N'settings';

IF @tm_UserId IS NOT NULL AND @tm_IsActive = 1
BEGIN
    -- Throttled activity stamp: matches zero rows on the common request (a seek, no write, no log record).
    IF @tm_StampActivity = 1
        UPDATE [core].[UserStamps] SET [LastActiveAt] = @tm_Now
        WHERE [UserId] = @tm_UserId AND ([LastActiveAt] IS NULL OR [LastActiveAt] < DATEADD(second, -60, @tm_Now));

    -- First authenticated request on this tenant: Invited -> Joined, once per user, ever.
    -- The one write to a temporal table outside the save pipeline; never touches ModifiedAt.
    IF @tm_State = 'Invited' AND @tm_AllowFlip = 1
    BEGIN
        UPDATE [core].[Users] SET [JoinedAt] = @tm_Now WHERE [Id] = @tm_UserId AND [JoinedAt] IS NULL;
        SET @tm_State = 'Joined';
    END;

    SET @tm_PermissionsStale = CASE WHEN @tm_ExpectedPermissionsTag IS NULL OR @tm_ExpectedPermissionsTag <> @tm_PermissionsTag
                                      OR @tm_ExpectedUserPermissionsTag IS NULL OR @tm_ExpectedUserPermissionsTag <> @tm_UserPermissionsTag THEN 1 ELSE 0 END;
    SET @tm_ProfileStale     = CASE WHEN @tm_ExpectedPreferencesTag IS NULL OR @tm_ExpectedPreferencesTag <> @tm_PreferencesTag THEN 1 ELSE 0 END;
    SET @tm_SettingsStale    = CASE WHEN @tm_SettingsTag <> @tm_ExpectedSettingsTag THEN 1 ELSE 0 END;
    SET @tm_Guard = CASE WHEN @tm_UserId = @tm_ExpectedUserId AND @tm_PermissionsStale = 0
                          AND (@tm_SettingsStrict = 0 OR @tm_SettingsStale = 0) THEN 1 ELSE 0 END;
END;

-- Result set 0: the connect row. Always present, exactly one row.
SELECT @tm_UserId AS [UserId], @tm_Kind AS [Kind], @tm_IsActive AS [IsActive], @tm_State AS [State],
       @tm_PreferencesTag AS [PreferencesTag], @tm_UserPermissionsTag AS [UserPermissionsTag],
       @tm_PermissionsStale AS [PermissionsStale], @tm_ProfileStale AS [ProfileStale], @tm_SettingsStale AS [SettingsStale], @tm_Guard AS [GuardPassed];

-- Result set 1: every tenant-level tag. Always present.
SELECT [Name], [Tag] FROM [core].[VersionTags];

-- Result set 2: the caller's effective permission rows. Present iff PermissionsStale = 1 or a reload was asked.
IF (@tm_PermissionsStale = 1 OR @tm_ReloadPermissions = 1) AND @tm_UserId IS NOT NULL AND @tm_IsActive = 1 AND @tm_Kind <> 'System'
    SELECT P.[Id], P.[RoleId], R.[Name], R.[IsPublic], P.[Resource], P.[Action], P.[Filter], P.[FilterLanguageVersion]
    FROM [core].[Permissions] AS P JOIN [core].[Roles] AS R ON R.[Id] = P.[RoleId]
    WHERE R.[IsActive] = 1
      AND (R.[IsPublic] = 1 OR EXISTS (SELECT 1 FROM [core].[RoleMemberships] AS M WHERE M.[RoleId] = R.[Id] AND M.[UserId] = @tm_UserId));

-- Result set 3: the caller's profile. Present iff ProfileStale = 1.
IF @tm_ProfileStale = 1 AND @tm_UserId IS NOT NULL AND @tm_IsActive = 1
    SELECT [Name], [Name2], [Name3], [Email], [ContactEmail], [ContactMobile], [ImageId], [SignatureId], [PreferredLanguage], [PreferredCalendar], [PreferredTimeZone], [Gender]
    FROM [core].[Users] WHERE [Id] = @tm_UserId;

-- Settings load: the two statements of spec 0012 §5.7, absorbed from its Order-60 contributor and placed here under IF @tm_SettingsStale = 1 OR @tm_LoadSettings = 1; the contributor reads their sets from this result.

IF @tm_Guard = 1
BEGIN
    -- The batch body, verbatim: compiled queries, the lock and invariants of §8.3, the emitter's statements, raw SQL,
    -- the epilogue bumps. A Persist body opens its own transaction here (SET XACT_ABORT ON; BEGIN TRAN ... COMMIT),
    -- so the prologue's stamp and flip never sit inside it.
END;
```

**Reader contract.** Sets 0 and 1 are always present; set 2 iff `PermissionsStale = 1` or a reload
was requested (and the caller is an active non-system user); set 3 iff `ProfileStale = 1`; spec
0012's two settings sets iff `SettingsStale = 1` or `@tm_LoadSettings = 1`; the body's sets iff
`GuardPassed = 1`. The contributor's result tells
the executor how many `NextResult()` calls the prologue consumed. Nothing in the prologue is inside
a transaction; the stamp and the flip are autocommit, idempotent, single-row statements, so the
prologue is always retry-safe. Two concurrent first requests flip exactly once. A `settings`
mismatch is reported as `SettingsStale` and, on a `Read` batch, never fails the guard: the result
is served and the fresh settings ride the same round trip (spec 0012's `Refresh` policy). On a
`Validate` or `Persist` batch (`@tm_SettingsStrict = 1`) it fails the guard like stale
permissions (spec 0012's `Rerun` policy), and the fresh settings ride the failed round trip. The
tenant `permissions` and the user's `PermissionsTag` follow the `Rerun` policy through
`@tm_Guard` on every batch.

**Plan cache.** The prologue text is byte-identical for every batch of the same variant and its
parameters always bind with the same SQL types; a batch's plan differs from another's only by its
body, exactly as without the prologue.

**Variants.** The `ConnectPremises` case selects the text. `ForUser`: `@tm_UserId` bound, resolve by
`U.[Id]`, `@tm_StampActivity = 0`, `@tm_AllowFlip = 0` (job scopes running as a user, and the load
of `EvaluateFor`, §5.1). `ForSystem`: no user lookup; sets 0 and 1 (set 0 carries `UserId = 1`,
`Kind = System`, `GuardPassed = 1`, `PermissionsStale = 0`) and, under `@tm_LoadSettings = 1`, spec
0012's settings sets; the set is `UserAccess.System`, never loaded.

### 7.4 The persist-time re-checks

The prologue guard runs outside the transaction; a role, membership or settings save could still
commit
between it and the body's `BEGIN TRAN`. Every `Persist` batch on a caller's behalf therefore begins
its transaction with two re-checks, emitted right after `BEGIN TRAN` and before any write:

```sql
-- Spec 0012's guard: @tm_expectedTags : VersionTagList always holds 'permissions' (= @tm_ExpectedPermissionsTag) and 'settings'
-- plus every declared dependency, Bumped = 1 on the names this batch bumps; REPEATABLEREAD holds the shared locks on the rows
-- this batch only reads to COMMIT, so a concurrent bump of them waits for this transaction; UPDLOCK serialises the writers of a row; FORCESEEK locks the expected rows only.
IF EXISTS (SELECT 1 FROM [core].[VersionTags] AS t WITH (REPEATABLEREAD, ROWLOCK, FORCESEEK) JOIN @tm_expectedTags AS e ON e.[Name] = t.[Name] WHERE e.[Bumped] = 0 AND t.[Tag] <> e.[Tag])
   OR EXISTS (SELECT 1 FROM [core].[VersionTags] AS t WITH (UPDLOCK, ROWLOCK, FORCESEEK) JOIN @tm_expectedTags AS e ON e.[Name] = t.[Name] WHERE e.[Bumped] = 1 AND t.[Tag] <> e.[Tag])
   OR EXISTS (SELECT 1 FROM [core].[UserStamps] AS s WITH (REPEATABLEREAD, ROWLOCK) WHERE s.[UserId] = @tm_UserId AND s.[PermissionsTag] <> @tm_ExpectedUserPermissionsTag)
    THROW 50412, N'StaleVersionTag', 1;
-- This spec's caller re-check: a deactivation committed since the prologue voids the write.
IF NOT EXISTS (SELECT 1 FROM [core].[Users] WHERE [Id] = @tm_UserId AND [IsActive] = 1)
    THROW 50401, N'CallerInvalid', 1;
```

The runner maps `50412` to a guard failure (§7.6: a cold re-connect and one recompose, then
`StaleContextException`) and `50401` to `TenantNotFoundException`. The lock hints and the waits
they impose on tag writers are spec 0012 §2.5's; the caller's stamp row is read under a shared
lock only, so no update lock ever serialises a tenant's saves on it.

### 7.5 Round trips

| Operation (warm cache) | Round trips | Contents |
|---|---|---|
| Query (capped count, ancestors) | 1 | prologue; page query; count; ancestors |
| Details | 1 | prologue; main row; children; related entities; extras; row echo |
| Save (update) | 2 | (1) prologue; validation context; pre-checks — (2) prologue; `BEGIN TRAN`; re-checks; persist; post-check; bumps; guards; read-back; `COMMIT` |
| Save (create, no context needed, no unique property, warm id buffer) | 1 | prologue; transaction; persist; read-back |
| Activate / deactivate / delete by ids (no validation hook) | 1 | prologue; transaction; RLS-filtered update or delete with the affected-count check; bumps; guards; `COMMIT` |
| Activate / deactivate / delete by ids on the users and roles stacks | 2 | (1) prologue; the target rows under the action's grant; the loads `UserAccessRules` and `RoleAccessRules` declare (§8.4) — (2) prologue; transaction; update or delete; bumps; guards; `COMMIT` |
| Role save | 2 | (1) prologue; the roles' before images with their permissions (the stamp rule, §3.7), the roles' members; the caller's set is in memory — (2) persist; bump; guards |
| "Can I, and why" for the caller | 0 | in memory |
| "Can I, and why" for another user | 2 | (1) prologue; the target row under the caller's `core.User × Read` decision — (2) that user's rows through a `ConnectPremises.ForUser`, once per scope (§5.1) |
| `me` at application start | 1 | prologue; the caller's preference bag |

"+1" applies on the cold or stale path: exactly one extra round trip, once per user per instance or
after eviction; "+2" when the persist re-check fires (§7.6: the cold re-connect and the recomposed
persist). Each dependent validation round adds one (spec 0014's `MaxValidationRounds`).

### 7.6 Failure modes

| Condition | Effect |
|---|---|
| Unknown subject (`UserId IS NULL`) | `TenantNotFoundException` (404 `tenant-not-found`); the session cookie is untouched — the user may be a member of another tenant of the distribution; `AccessEvents.ConnectRejected` |
| Deactivated user (`IsActive = 0`) | `TenantNotFoundException` from any instance on the very next batch; the connect and permissions entries are dropped on the refusing instance and die elsewhere on the request that observes it |
| Stale permissions | transparent: one recompose within the same runner call |
| Stale settings | `Read` batch: served, and the fresh settings ride the same round trip; `Validate` or `Persist` batch: the guard fails, the fresh settings ride the failed round trip, spec 0012's initializer re-runs, one recompose; every `Persist` re-checks `settings` under §7.4 |
| Stale profile | no guard failure; set 3 refreshes the profile cache |
| Two guard failures in one call | `StaleContextException` (503, `Retry-After: 1`); `tellma.access.connect.results{result=guard_thrash}` |
| Persist re-check `50412` | the runner re-connects cold — one prologue-only round trip returning the fresh rows, profile, settings and tags — and recomposes the persist once (spec 0014 re-runs its in-memory validators first); a second `50412` in the same call raises `StaleContextException` |
| Persist re-check `50401` | `TenantNotFoundException` |
| Background scope with no user | `Denied` for everything; `ConnectAsUser` on a deactivated or unknown user throws `TenantNotFoundException` before the handler runs |
| `ReadOnly` tenant | premises carry `StampActivity = false`, `AllowStateFlip = false`; the prologue writes nothing and reads succeed against a `READ_ONLY` database; mutations are refused by spec 0010's state verdict before any batch |

### 7.7 Service accounts

A service account is a `core.Users` row with `Kind = Service`: created through the ordinary user
save (`Kind` is write-once, so the row is a service account from birth), named and given memberships
like any user, with no `Email` and no invitation. Its credentials are spec 0017's
`issue-credentials` action under `core.User × Credentials`: the action registers a confidential
client at the identity server, writes back `Subject` = the client id and `JoinedAt`, and returns the
client id and the secret once; nothing tenant-side stores the secret. A row with a subject reads
`Joined`, without one `New`. Re-issuing rotates: a new client, the new subject written back, the old
client deleted after commit. Deactivation deletes the client after commit, so a deactivated service
account obtains no token at all; reactivation needs `issue-credentials` again. A token is resolved
exactly like a human's: `@tm_Subject` binds the client id, the covering index seek finds the row,
and its memberships and the public roles compose its set; a client id with no row is
`TenantNotFoundException`, which is also why an orphaned client is harmless. Sensitive securables
answer `HumanRequiredException` to a service account (spec 0015), and the lockout invariant counts
humans only (§8.3).

### 7.8 The request-context initializer

`ConnectInitializer : IRequestContextInitializer` at `Order` 100 (spec 0010 runs it inside the
tenant endpoint filter and in job scopes): for `PrincipalKind.User` and `ServiceAccount` it calls
`IUserConnector.Connect()` on a request and `ConnectAsUser(UserId)` in a job scope (the context spec
0010's scope factory builds from a `RequestContextSnapshot` carries the user id; §7.1), and returns
the context with `UserId` bound and the member witness recorded; for `System` it calls
`ConnectAsSystem()`; for `Anonymous` on a tenant endpoint it throws `TenantNotFoundException`. On a
request it composes a `ConnectPremises.ForSubject` (`Cold` or `For`, §7.1) from the tenant state
(`ReadOnly` → both flags `false`), the endpoint's `NoActivityStampMetadata`, and the throttle memo;
a job scope's `ForUser` premises stamp nothing and flip nothing (§7.2). Spec 0012's negotiation
initializer at `Order` 200 then reads `User.PreferredLanguage/PreferredCalendar/PreferredTimeZone`
from the connected profile.

## 8. Write paths, tag bumps, and the security guards

### 8.1 Every write declares what it writes

- Entities declare the tags their table bumps: `[BumpsVersionTag("permissions")]` on `Role` and
  `Permission`; `[BumpsUserVersionTag(Permissions, "UserId")]` on `RoleMembership`;
  `[BumpsUserVersionTag(Preferences, "Id")]` on `User`. The attributes are inherited by
  distribution leaves. Spec 0012's executor epilogue derives the bump set from the batch's
  `WrittenTables` and emits the bumps last before `COMMIT`, with one `@tm_tag` Guid per batch bound
  from C#; the affected user ids come from the emitter's rows.
- Raw SQL (`IDataBatch.Sql`) states its `SqlOptions.Writes`; when a written table carries a
  user-level rule it also names the affected users through `SqlOptions.UserIds`
  (`SqlOptions.ForCaller(writes)` when the affected user is the caller). Omission fails the startup
  gate and the analyzer.
- `core.UserPreferences` and `core.NotificationPreferences` carry spec 0012's fixed rules
  (`Preferences`, `"UserId"`); their statements bump the affected user's `PreferencesTag` — the
  caller's through `ForCaller`, another user's through `UserIds` (§3.4).
- The tables that bump no tenant-level tag are listed by spec 0012 §2.3.
- Out-of-band writes (support tooling, hotfix scripts) must end with the bump statements;
  `PermissionsMaxAge` bounds the damage when they do not.
- Fixture tier: change tracking on every table; the executor in test mode compares
  `CHANGETABLE(CHANGES …)` with the declared write set after every batch and fails the test on a
  write outside it.

### 8.2 The companion `UserStamps` insert

The Core feature's `IPersistEffect<User>` appends this statement after every `core.Users` insert, in
the same transaction, over the inserted roots of a save: `SavePersistContext<User>.NewIds`, the
emitter's `@tb{b}_new` (spec 0014 §13.1). An action or a delete inserts no user, so its context gets
no statement. `@tm_tag` is the batch's bump Guid:

```sql
INSERT [core].[UserStamps] ([UserId], [LastActiveAt], [PermissionsTag], [PreferencesTag], [InboxSeenAt])
SELECT N.[Id], NULL, @tm_tag, @tm_tag, NULL
FROM @tb{b}_new AS N
WHERE NOT EXISTS (SELECT 1 FROM [core].[UserStamps] AS S WHERE S.[UserId] = N.[Id]);
```

### 8.3 Guards — in-transaction invariants

```csharp
// Tellma.Core.Abstractions.Access
public interface IAccessGuards
{
    void ContributeLock(IDataBatch batch);                  // the application lock; appended before any write
    void ContributeInvariants(IDataBatch batch);            // L1–L3; appended after every write, before the post-check
}
```

The pipeline (spec 0014) calls both members on every `Persist` batch whose `WrittenTables` meets
any of `core.Users`, `core.Roles`, `core.RoleMemberships`, `core.Permissions` — save, action or
delete alike — except when `RequestContext.Kind = System` (the bootstrap creates the first
administrator from zero). `IDataBatch` is append-only, so the two members are two calls at two
positions of the persist assembly: `ContributeLock` immediately after the re-checks of §7.4 and
before the emitter's first statement; `ContributeInvariants` after the emitter's statements and
every `ContributeAsync` contribution, before spec 0014's row-level post-check and the epilogue
bumps. The lock serialises the batch on a per-tenant-database application lock, which makes the
invariants race-free by construction (two administrators removing each other concurrently would
otherwise both pass an `EXISTS` under read-committed snapshot):

```sql
-- ContributeLock: immediately after the re-checks of §7.4, before any write.
DECLARE @tm_Lock int;
EXEC @tm_Lock = sp_getapplock @Resource = N'tellma.access', @LockMode = N'Exclusive', @LockOwner = N'Transaction', @LockTimeout = 5000;
IF @tm_Lock < 0 THROW 50503, N'Access.LockTimeout', 1;

-- ... the emitter's statements and every ContributeAsync contribution ...

-- ContributeInvariants: after the writes, before the post-check.
-- L1: at least one active, invited-or-joined human administrator remains.
IF NOT EXISTS (SELECT 1 FROM [core].[Users] AS U
               JOIN [core].[RoleMemberships] AS M ON M.[UserId] = U.[Id]
               WHERE M.[RoleId] = 1 AND U.[Kind] = 'Human' AND U.[IsActive] = 1 AND U.[State] IN ('Invited', 'Joined'))
    THROW 50422, N'Access.LastAdministrator', 1;
-- L2: public roles have no members.
IF EXISTS (SELECT 1 FROM [core].[RoleMemberships] AS M JOIN [core].[Roles] AS R ON R.[Id] = M.[RoleId] WHERE R.[IsPublic] = 1)
    THROW 50422, N'Access.PublicRoleHasMembers', 1;
-- L3: the Administrator role is active, not public, and still holds its unfiltered */* grant.
IF NOT EXISTS (SELECT 1 FROM [core].[Roles] AS R JOIN [core].[Permissions] AS P ON P.[RoleId] = R.[Id]
               WHERE R.[Id] = 1 AND R.[IsActive] = 1 AND R.[IsPublic] = 0 AND P.[Resource] = '*' AND P.[Action] = '*' AND P.[Filter] IS NULL)
    THROW 50422, N'Access.AdministratorRoleDamaged', 1;
```

`sp_getapplock` returns a code rather than throwing, hence the explicit check; the lock is
transaction-owned and released at commit or rollback; `@LockTimeout` is `SecurityLockTimeout` in
milliseconds. A `50422` surfaces as `ValidationException` with one error whose code is the message
(`XACT_ABORT ON` rolls the transaction back); `50503` is transient — the executor retries the round
trip, then raises `DependencyUnavailableException` (503). Each triggered guard counts
`tellma.access.guards.triggered{guard}` and logs `AccessEvents.GuardTriggered`. The invariant is
defined over the seeded Administrator role only, which keeps L1 a single `EXISTS` and makes
recovery deterministic: the seeded role is always the way back in. A user narrowing their own
permissions while another administrator remains is allowed.

Recovery from a damaged tenant is `provision --admin-email` (spec 0010's migrator, §11), which
requires database-level access by design. The identity server's break-glass administrator holds no
tenant permissions and is not a tenant recovery path; a permanent hidden administrator row is a
standing credential and does not exist.

### 8.4 Validators — field-level rules before persist

```csharp
// Tellma.Core.Abstractions.Access
public sealed class UserAccessRules<TUser> : IEntityValidator<TUser> where TUser : User;
public sealed class RoleAccessRules<TRole> : IEntityValidator<TRole> where TRole : Role;
```

Both are Core components registered for the base types, so a distribution leaf cannot drop them;
they declare their context loads through spec 0014's validation contract (the roles being saved and
their permissions; the members of the roles a membership names; the caller's set is in memory).
Both implement `ValidateDeleteAsync` and `ValidateActionAsync` for `activate` and `deactivate`, so
delete by ids and the two built-in actions on the users and roles stacks run spec 0014's general
path with the component's loads in RT1: two round trips (§7.5).

`UserAccessRules<TUser>`:

- `Users.SystemUserImmutable` — row 1 in any save, action or delete.
- `Users.CannotDeactivateSelf`, `Users.CannotDeleteSelf` — the caller's own id in a `deactivate`
  or delete request.
- `Users.CannotRemoveOwnAdministratorMembership` — the caller may not delete their own membership
  in role 1. A hand-over is "make Bob an administrator, then Bob removes you".
- `Users.EmailLockedAfterInvite` (§3.1); `Users.OnlyNewUsersDeletable` (§3.9).
- `Users.KindNotAllowed` — `Kind = System` in a payload; `Users.EmailNotAllowed` — an `Email` on a
  row whose `Kind` is not `Human`; `Required` at `Email` on a `Human` row that carries none.
- `Users.TooManyRoles` — more than `MaxRolesPerUser` memberships.
- `Users.MembershipEscalation` — adding a membership to a role requires that every permission of
  that role passes the escalation test below against the caller's set.

`RoleAccessRules<TRole>`: the per-permission rules of §3.7 (securable existence, the filter root,
the language stamp and its floor, the filter text under `AccessLimits`, escalation);
`Roles.PublicRoleHasMembers` (a role saved with `IsPublic = true` while memberships exist);
`Roles.AdministratorImmutable` (row 1 made public, deactivated, deleted, or its `*/*` permission
removed or filtered); `Roles.HasMembers` on delete; `Roles.TooManyPermissions`.

**Escalation.** `Permissions.EscalationBeyondSelf` — a permission `(R, A, F)` may be saved only by
a caller whose own effective set holds `(R, A)` **unrestricted** (or a wildcard covering it);
filtered grants on the caller's side do not qualify because filter implication is undecidable. A
caller holding `*/*` is exempt. `Users.MembershipEscalation` applies the same test to every
permission of a role being granted. Together they bound delegation to "a role editor can hand out
at most what they hold": without them, `Save` on `core.User` lets anyone add themselves to the
Administrator role, and `Save` on `core.Role` lets anyone add `*/*` to a role they belong to. Each
rejection logs `AccessEvents.EscalationRejected` and counts
`tellma.access.guards.triggered{guard=escalation}`.

Validators produce field-level messages at the entity path (`[2].RoleMemberships[0].RoleId`);
the guards of §8.3 produce correctness. Every code is a dotted PascalCase resource key the host
localises (Appendix A).

## 9. Drift policy

A permission row **drifts** — grants nothing, fail closed — when:

| Condition | `AccessProblemCode` |
|---|---|
| `Resource` is not registered (aliases considered) | `UnknownResource` |
| `Action` is not registered for the resource | `UnknownAction` |
| It carries a filter but the securable has no `FilterRoot`, or `Resource = '*'` | `FilterUnsupported` |
| `FilterLanguageVersion` is outside `[QueryexLanguage.Minimum, QueryexLanguage.Version]` | `VersionUnsupported` |
| `Validate` reports any diagnostic against the tenant's current schema — a renamed column, a removed navigation, a language-gated `Name3` on a bilingual tenant | `FilterInvalid` (with the diagnostics) |

A drifted row never affects the user's other rows: an administrator whose one broken permission
removed all their access could not fix it. It is listed in `UserAccess.Problems`; surfaced in the
"why" answer (§10); returned by the role details read as a per-permission diagnostic (code and
location per spec 0008 §14) through spec 0017's `RoleService`, which the role editor renders as a
banner; logged once per `(permission id, code)` per tag cycle (`AccessEvents.PermissionDrift`,
Warning); counted on `tellma.access.permissions.unresolved{problem}`; and listed by the post-deploy
drift report. `*/*` grants never drift. A rename ships an `Alias` for one release and a data
migration that rewrites the strings; there is no shim and no tenant-wide block. A securable removed
by a new deployment while an N−1 instance still runs is accepted by the old instance and drifted by
the new one; the union during the rollout window is bounded by the old version's semantics.

```csharp
// Tellma.Core.Abstractions.Access
public sealed record PermissionDriftItem(
    int PermissionId, int RoleId, string? RoleCode, AccessProblemCode Code, IReadOnlyList<QueryexDiagnostic> Diagnostics);

public interface IPermissionDriftScanner                        // runs in a System tenant scope
{
    Task<IReadOnlyList<PermissionDriftItem>> ScanAsync();       // Validate over every stored filter and every (Resource, Action) of the tenant
}
```

Spec 0010's migrator invokes `IPermissionDriftScanner.ScanAsync` per tenant database after
`migrate` and prints the items, so a deploy that breaks a permission is known before a user
notices. The scan is read-only and takes no lock.

## 10. "Can I, and why"

- **`me`** — spec 0017's `UserService.Me` (member-only, idempotent, POST) returns spec 0015 §3.4's
  `MeResult`: the connected profile (`UserProfileView`), the caller's `PreferencesTag` as a wire
  tag, the caller's preference bag (§3.4), `AccessSummary(Tag, FormatVersion, IsSystem, Securables:
  list<SecurableSummary(Resource, Action, HasFilter)>, Problems)` computed from `EvaluateAllAsync()`
  (one entry per registered securable the caller is allowed), the four wire tags (`settings`,
  `permissions`, `preferences`, `entities`) exactly as `Tellma-Version-Tags` carries them, and the
  registry `Fingerprint`. The SPA's first call after sign-in and its refresh call whenever a
  response's `Tellma-Version-Tags` header (spec 0015) carries a tag the SPA does not hold (§3.4
  for the `preferences` tag after its own write). One round trip, which is also the cold
  prologue's.
- **`access/check`** — spec 0017's `AccessService.Check` (route `access/check`, member-only,
  idempotent) takes spec 0015's `AccessCheckRequest(UserId?, Securables: list<SecurableRef>)` and
  returns one `AccessDecision` per securable: outcome, the composed filter rendered for display,
  grants (the `AccessGrant` cases of §5.1 under their `kind` discriminator: a role grant with its
  permission, role and filter, a bespoke criterion with its reason, or the system short-circuit),
  problems. `UserId` omitted means the caller (in memory). Naming another user requires `core.User`
  × `Read` on that user's row (filtered) and `core.Role` × `Read` (effective grants are role-editor
  information), costs two round trips (the visibility read, then the one `ForUser` load every
  `EvaluateFor` of the request shares; §5.1, §7.5), and never records a witness for the target.
- **`IAdministratorDirectory`** — the recipients of administrative notices:

```csharp
// Tellma.Core.Abstractions.Access
public interface IAdministratorDirectory
{
    Task<IReadOnlyList<int>> GetAdministratorIdsAsync();   // active human members of role 1 with State Invited or Joined; ordered by Id; capped at 20
}
```

- **MCP** — spec 0015's `tellma_whoami` carries the `me` payload compacted to the user (id, name,
  email, locale), the tenant, the securables fingerprint, the version tags, and entities with
  their operations and actions outside the step-up set (§4.3) and which of them are filtered;
  `tellma_check_access` is reserved.

## 11. Bootstrap

```csharp
// Tellma.Core.Abstractions.Access
public sealed record TenantBootstrapRequest(string Email, string Name, string? PreferredLanguage, string? Subject);

public interface ITenantBootstrapper                            // runs in a System tenant scope; idempotent on Email
{
    Task<int> BootstrapAdministratorAsync(TenantBootstrapRequest request);
}
```

The `HasData` rows of §3.8 seed the system user, the Administrator role, its `*/*` permission and
the system user's membership. The **first human administrator** is not `HasData` (its email is
environment-specific): spec 0010's platform provisioning step `10 core.bootstrap-administrator`
calls `BootstrapAdministrator` from `TenantProvisioningContext.AdminEmail`/`AdminSubject`
(`Tellma:Seed:AdminEmail` in Development; `provision --admin-email` otherwise; `AdminSubject` is
supplied in Development only, spec 0010 §6.2). The bootstrapper runs as the system user
(`ConnectAsSystem`; guards bypassed) and, in one persist batch through the pipeline, creates the
user with no invitation evidence (`State` reads `New`), the normalised email, `Name`,
`PreferredLanguage`, and one membership in role 1. Idempotent on email: an existing row keeps its
state and gains the membership if missing; the returned `int` is the user's id. In deployed
instances the provisioning flow then invites the user through the `invite` action of spec 0017's
`UserService` in the migrator's system scope.

**Development in-proc mode.** When `Subject` is supplied the bootstrapper appends one fixed-text
statement after the save (`SqlOptions.Writes = { core.Users }`, `UserIds` = the new id) that sets
`Subject`, `InvitedAt = SYSUTCDATETIME()`, `InviteStatus = 'Invited'`; the reference distribution's
local setup passes `admin@localhost` with `00000000-0000-0000-0000-000000000001` (spec 0003's fixed
dev subject), and the first sign-in flips the row to `Joined` through the ordinary prologue. A
`Subject` outside the Development environment is refused with `InvalidOperationException`: a
deployed tenant obtains subjects only from the invite API.

## 12. Limits and configuration

### 12.1 `AccessOptions` — `Tellma:Access`

```csharp
// Tellma.Core.Abstractions.Access
public sealed class AccessOptions
{
    public int MaxPermissionsPerRole { get; set; } = 300;
    public int MaxRolesPerUser { get; set; } = 32;
    public int MaxFilterLength { get; set; } = 2048;
    public TimeSpan PermissionsMaxAge { get; set; } = TimeSpan.FromHours(2);
    public TimeSpan ActivityStampInterval { get; set; } = TimeSpan.FromSeconds(60);
    public TimeSpan FastDenyWindow { get; set; } = TimeSpan.FromSeconds(5);   // the age beyond which a denial is re-verified
    public int MaxPreferenceKeys { get; set; } = 1024;
    public int MaxPreferenceValueBytes { get; set; } = 32768;
    public TimeSpan SecurityLockTimeout { get; set; } = TimeSpan.FromSeconds(5);
}
```

Validated at startup (`ValidateOnStart`): every count ≥ 1, every span > 0, `MaxFilterLength` ≤ 2048
(the column width), `FastDenyWindow` ≤ `PermissionsMaxAge`. `ActivityStampInterval` is fixed in the
prologue text at 60 s; the option governs the per-instance memo only. Cache sizes are spec 0012's
`TellmaCacheOptions` (`Tellma:Cache`), never duplicated here (§6.2, §6.3).

### 12.2 Queryex profiles

`AccessLimits` (validating one stored filter): `MaxInputLength = 2048`, `MaxJoins = 8`,
`MaxParameters = 64`, `MaxTypedNodes = 512`, every other limit at the engine default.
`PipelineLimits`, the profile of every pipeline compile (spec 0014 §5.2), must admit the composed
disjunction: `MaxParameters ≥ 1024`, `MaxJoins ≥ 64`. A compile that still exceeds a ceiling is a
plain 500, logged as `AccessEvents.FilterTooComplex` with the diagnostics and the role ids
involved: an administrator's configuration problem, surfaced rather than silently denied.

## 13. Telemetry and logging

Meter `Tellma.Core`; names as constants in `AccessTelemetryNames`; no per-tenant or per-user tags
(ids go to structured log events).

```csharp
// Tellma.Core.Abstractions.Access
public static class AccessTelemetryNames
{
    public const string MeterName = "Tellma.Core";
    public const string Decisions = "tellma.access.decisions";
    public const string CacheHits = "tellma.access.cache.hits";
    public const string CacheMisses = "tellma.access.cache.misses";
    public const string CacheEntries = "tellma.access.cache.entries";
    public const string SetBuildDuration = "tellma.access.set.build.duration";
    public const string SetGrants = "tellma.access.set.grants";
    public const string PermissionsUnresolved = "tellma.access.permissions.unresolved";
    public const string GuardsTriggered = "tellma.access.guards.triggered";
    public const string WitnessMissing = "tellma.access.witness.missing";
    public const string ConnectResults = "tellma.access.connect.results";
    public const string ConnectPrologueDuration = "tellma.access.connect.prologue.duration";
    public const string OutcomeTag = "outcome";
    public const string ReasonTag = "reason";
    public const string ProblemTag = "problem";
    public const string GuardTag = "guard";
    public const string ResultTag = "result";
    public const string VariantTag = "variant";
}
```

| Instrument | Kind | Tags |
|---|---|---|
| `tellma.access.decisions` | counter | `outcome` ∈ `denied \| filtered \| unrestricted` |
| `tellma.access.cache.hits` | counter | — |
| `tellma.access.cache.misses` | counter | `reason` ∈ `absent \| tag \| age \| format` |
| `tellma.access.cache.entries` | observable gauge | — |
| `tellma.access.set.build.duration` | histogram (s) | — |
| `tellma.access.set.grants` | histogram | — (effective grants per built set) |
| `tellma.access.permissions.unresolved` | counter | `problem` ∈ the codes of §9 |
| `tellma.access.guards.triggered` | counter | `guard` ∈ `last_administrator \| public_role_members \| administrator_damaged \| lock_timeout \| self_deactivation \| self_deletion \| own_administrator_membership \| escalation` |
| `tellma.access.witness.missing` | counter | — |
| `tellma.access.connect.results` | counter | `result` ∈ `warm \| cold \| stale_permissions \| stale_settings \| stale_profile \| unknown_subject \| deactivated \| guard_thrash` |
| `tellma.access.connect.prologue.duration` | histogram (s) | `variant` ∈ `subject \| user \| system` |

Log events (`AccessEvents`, event ids 3100–3107, logger category `Tellma.Core.Access`):
`MissingWitness` 3100 Error; `PermissionDrift` 3101 Warning; `GuardThrash` 3102 Warning;
`GuardTriggered` 3103 Warning; `EscalationRejected` 3104 Information; `ConnectRejected` 3105
Information; `AccessSetBuilt` 3106 Debug; `FilterTooComplex` 3107 Error. Tenant id, user id,
permission id and role ids are structured properties, never in metric tags. Alert queries under
`infra/monitoring/`: drift > 0 over any 15-minute window; cache hit ratio below 90 % over 15
minutes; any `witness.missing`.

## 14. Testing

Test projects mirror `src/`: `test/core/Tellma.Core.Tests/Access/` (unit) and
`test/core/Tellma.Core.IntegrationTests/Access/` (`Category=Integration`). No `Live=true` suite:
the identity-server client is spec 0017's; this spec's tests stub it.

**Unit (every PR).** `Decide` over hand-built sets: absorption, union, implied read with the same
filter, wildcard-and-filter interaction, `(R, *)` with a filter granting no non-filterable action,
inactive and public roles, the system set, criterion-only access, drift exclusion, leaf
deduplication and ordering (byte-identical SQL for equal grant shapes), each leaf carrying its
grant's `FilterLanguageVersion`, empty set → `Denied`, no user → `Denied`. `Grants` of every kind
(`RoleAccessGrant`, `BespokeAccessGrant`, `SystemAccessGrant`) and their `kind` discriminator
through the platform JSON options. Both escalation rules and every validator code of Appendix A;
`RoleAccessRules` over a scripted before image: the stamping matrix (new, changed, unchanged, null
stamp, below `Minimum`), engine diagnostics → `Permissions.FilterInvalid`, wildcard root resolution.
The resource and action grammar. Registry freezing, duplicates, aliases, `MarkNotSensitive` removing
a Core default, a pair marked both ways refused, `Fingerprint` stability across registration order.
The startup audit over an in-memory `EndpointDataSource` (an endpoint naming an unregistered
securable, a child-entity `FilterRoot` without `Owner`; the endpoint-shape rules are spec 0010's).
The prologue reader against scripted result sets: every presence combination of sets 2–3 and the
body. `FormatVersion` mismatch → miss with `reason=format`. `Evaluate`'s deny re-check inside and
outside `FastDenyWindow`. The drift table of §9 row by row. The owner composition of §5.5: direct
grants only, owner grants only, both (`Or` of the direct leaves and the `Via` node), neither
(`Denied`), either side `Unrestricted`, and an owner that is itself a child.

**Integration (every PR; LocalDB or Testcontainers; fixture tenant database with change tracking).**
The schema of §3 applied by migrations and every constraint name present; the `HasData` rows. The
prologue end to end: cold, warm, stale permissions (tenant tag), stale permissions (user tag),
stale settings served on a read and refused on a validate with the rows in the same round trip,
stale profile, unknown subject, deactivated, `ConnectPremises.ForUser` and `ForSystem`, the join
flip exactly once under two concurrent first requests, and a service account resolved by its client
id. `State` derived from its evidence for every kind and `Kind` write-once. The throttled stamp
writes once per minute under 1,000 requests and `core.UsersHistory` gains zero rows from traffic.
Tag bumps for every declared write path (role save → `permissions`; membership save → that user's
`PermissionsTag` only; user save → that user's `PreferencesTag`; the bag statement →
`PreferencesTag`) and none for `core.Users`-only writes on `permissions`. The companion `UserStamps`
insert. A role re-save of an untouched filter keeps its stamp, a changed filter takes the current
version, and a stamp below `Minimum` is refused. The persist re-checks: a role save committed
between the two round trips of one save forces exactly one recompose; a deactivation committed
between them is `50401`. L1–L3 under two concurrent administrator removals (exactly one succeeds;
the lock serialises); guard violations roll the whole transaction back; the system context bypasses.
The write-set audit catches an undeclared write. The witness filter replaces a 2xx with 500.
Temporal history rows per save (unchanged children write none). Two tenant databases sharing user
ids and subjects never see each other's cache entries. The bootstrap: idempotent on email, the
Development subject path, refusal outside Development. `ReadOnly`: reads succeed against a
`READ_ONLY` database with no prologue write.

**Nightly.** The drift scanner against a database seeded with a filter over a column the next
migration renames, and against a tenant lacking a language a stored filter names.

## 15. Definition of done

- **Projects**: `Tellma.Core.Abstractions` (namespace `.Access`) and `Tellma.Core` (namespace
  `Tellma.Core.Access`) gain the content of §1–§13; `test/core/Tellma.Core.Tests/Access/` and
  `test/core/Tellma.Core.IntegrationTests/Access/` exist — each project with a README, XML docs on
  every member, building and testing on Windows and Linux under warnings-as-errors, wired into
  `Tellma.slnx`.
- **Behavior**: the six tables and seeds of §3 pinned by the integration suite; the registry,
  audit and four layers of §4 pinned by the unit suite and the endpoint audit test; the
  composition contract of §5.2 pinned by the `Decide` suite; the caches and the deny re-check of §6;
  the prologue, re-checks, runner, round-trip table and failure modes of §7 pinned end to end;
  the guards and validators of §8 including both escalation rules; the drift policy of §9 with the
  scanner; the `me`/`access/check` semantics of §10 (endpoints by spec 0017); the bootstrap of §11.
- **Observability**: every instrument of §13 emitted and asserted through `MetricCollector<T>`;
  every `AccessEvents` id asserted with a capturing logger.
- **CI**: unit and integration suites green on every PR on both platforms; the nightly drift job.
- **Docs**: ARCHITECTURE.md updated where this spec touches it — the Data Layer entity-class row
  (Abstractions also holds Core's default leaves `User` and `Role`, because modules reference
  `core.Users` and never `Tellma.Core`), and the Identity row (the distribution owns sensitive
  securables with a Core default set that step-up reads). Public XML docs and error messages
  reference no `docs/` paths, per repo rule.
- **Not in scope of done**: `UserService`, `RoleService`, `AccessService` and every endpoint (spec
  0017); the executor, emitter and engine amendments (spec 0011); `core.VersionTags` and the
  epilogue (spec 0012); the pipeline's use of the decision (spec 0014); status mapping (spec 0015).

## Decisions record

The load-bearing decisions, where not already evident above:

1. **Entity defaults in `Tellma.Core.Abstractions`, unsealed, extended by inheritance** — every
   module's `CreatedById` references `core.Users` and modules never reference `Tellma.Core` (§1.1).
2. **`Subject varchar(255)` binary-collated, server-owned** — OIDC bounds `sub` at 255 ASCII
   characters, case-sensitive; the authority stays swappable; only spec 0017's write-backs
   (`invite`, `issue-credentials`) and the bootstrap learn a subject (§3.1).
3. **`State` as a persisted computed column over its evidence** — three states are all the tenant
   can know, the column cannot disagree with `InvitedAt`/`JoinedAt`, and delivery detail is a live
   drill-down (§3.2).
4. **Churn on `core.UserStamps`, not on the temporal row** — an activity stamp or a tag bump never
   writes history (§3.3).
5. **`core.UserPreferences` is a composite-keyed bag, not an entity, written only through its own
   statement** — the root-stamp rule would otherwise write a history row per grid resize (§3.4).
6. **Public permissions are `Role.IsPublic`** — no magic role id, several public sets allowed, one
   load predicate (§3.5).
7. **`RoleMemberships` are owned by `User` only** — onboarding is "create the user, tick the
   roles"; two synchronise scopes over one table would have no authority rule (§3.6).
8. **Unchanged filter text keeps its `FilterLanguageVersion`** — the stamp records when the text
   was validated, not when the row was last saved (§3.7).
9. **The entity name as the resource key, `*` as the only wildcard, PascalCase actions** —
   fork-stable, collision-free, decoupled from routing (§2).
10. **Securables derive from stack capabilities; endpoint metadata plus a startup audit plus a
    witness** — a distribution registers nothing by hand and cannot forget to secure an endpoint
    (§4.2, §4.6).
11. **Write implies read with the same filter; filters union; unfiltered absorbs; empty is
    `Denied`** — the composition contract every consumer relies on (§5.2).
12. **No SQL Server security policies** — logic in the database, schema binding, unprotected
    history and a fail-open bypass for `dbo` (§5.4).
13. **Two-level permission tags** — the tenant tag for role-side edits, the user tag for the
    frequent membership edit, no fan-out write anywhere (§6.1).
14. **A private bounded `MemoryCache` per kind with `PermissionsMaxAge` as the backstop** — not
    `HybridCache`; out-of-band edits are bounded, not trusted (§6.2).
15. **The connect step is a prologue with an `IF @tm_Guard = 1` wrapper** — never a body run under
    stale premises, and the fresh rows arrive in the failed round trip (§7.3).
16. **Persist batches re-check every expected tag row inside the transaction — shared locks on
    the rows they read, an update lock on the rows they bump — and the caller's `IsActive`** —
    closes the window a prologue outside the transaction leaves; the writers of those rows wait,
    as does a persist whose guard arrives while such a bump is queued, and two writers of one row
    never deadlock (§7.4).
17. **Non-members and deactivated users get `TenantNotFoundException`** — the same response as a
    tenant that does not exist; the cookie is untouched (§7.6).
18. **Guards as SQL under an application lock, over the seeded Administrator role only** —
    race-free by construction; recovery deterministic (§8.3).
19. **Escalation rules bound delegation** — a role editor hands out at most what they hold
    unrestricted; filter implication is undecidable (§8.4).
20. **Drift fails closed per row and blocks nothing** — diagnosed everywhere, never a shim (§9).
21. **The first administrator is a provisioning step, not `HasData`** — its email is environment-
    specific; a fixed subject only in Development (§11).
22. **Service accounts are user rows: created by the save, credentialed by an action, the secret
    crossing once** — one table, one prologue, one permission model (§7.7).
23. **Filter validation and the language stamp are `RoleAccessRules<TRole>`'s, never the stack
    service's** — a Core component registered for the base runs whatever leaf a distribution
    substitutes, so no role save reaches the emitter with an unvalidated predicate (§3.7).
24. **The pipeline calls `RequireAsync` with the securable alone; criteria come from providers** —
    one call carries the grant, the step-up refusal and the filter of every registered provider's
    criteria, so MCP tools, jobs and tests meet the same bar as a request, and no service composes
    a filter of its own (§5.1).
25. **An enlisted group carries no decision of its own** — a row another stack writes as a
    participant of the host's frame is platform-written by declaration, so a second evaluation would
    grant nothing the host's decision did not (§5.3).
26. **Access grants and connect premises are record hierarchies** — each case carries only the
    members it has: a role grant its permission, role and filter stamp, a bespoke grant its reason,
    a subject connect its two flags, a system connect its settings tag alone (§5.1, §7.1).

## Review flags

1. **Two-level permission tags** (tenant `permissions` + user `PermissionsTag`; §6.1) versus a
   tenant-level tag only, where every membership edit rebuilds every user's set on their next
   request. Flips if the per-user bump proves easy to miss in a write path the audit does not cover.
2. **`IF @tm_Guard = 1` wrapper** (§7.3) versus `THROW`-ing guards versus run-then-discard. Flips
   only if plan-cache measurements show the guarded body fragmenting plans beyond the body's own.
3. **`State` as a persisted computed column** (§3.1) versus a stored column with CHECK constraints.
   Flips if a state ever needs evidence the row does not carry.
4. **Service accounts as `Kind = Service` user rows credentialed by `issue-credentials`** (§7.7)
   versus a dedicated entity. Flips if the identity server's service-account shape needs columns the
   row lacks.
5. **The entity name as the resource key** (§2) versus the table name (`core.Users`) or an
   unqualified name (`User`). Flips if the qualified form proves unwieldy in the role editor.
6. **`*` as the only wildcard** (§2) versus `All` or prefix wildcards (`gl.*`). Flips if role
   editors routinely need "every resource of a module".
7. **One `Activate` action for both directions** (§2) versus `Activate` + `Deactivate`. Flips if a
   tenant needs to delegate reactivation without deactivation.
8. **A filter on `(R, *)` is valid** and applies to every filterable action (§3.7) versus refusing
   filters whenever either position is a wildcard. Flips if administrators misread it as "filtered
   delete".
9. **Escalation rules** (§8.4) versus documenting `core.Role`/`core.User` `Save` as administrative
   and dropping both rules. Flips if the rules block a legitimate delegation pattern in practice.
10. **`Users.CannotRemoveOwnAdministratorMembership`** (§8.4) versus allowing "make Bob admin,
    then step down" in one save (the invariant alone would permit it). Flips on user-experience
    evidence from hand-overs.
11. **The lockout invariant over the seeded Administrator role only** (§8.3) versus "any active user
    holding an unfiltered `*/*` through any active role". Flips if tenants routinely rename the
    seeded role out of use.
12. **An application lock on every security-table write** (§8.3) versus `UPDLOCK`/key-range locks on
    the checked rows. Flips if the lock measurably serialises unrelated user saves.
13. **The witness turning a forgotten service check into a 500** (§4.6) versus Error logging only.
    Flips if a legitimate endpoint shape cannot evaluate its declared securable.
14. **The securable evaluated in the service, with endpoint metadata as an audit record** (§4.6)
    versus an `IAuthorizationRequirementData` attribute in the authorization middleware. Flips if
    the framework gains a hook that runs after tenant resolution.
15. **`core.NotificationPreferences (UserId, Type, Channel, Enabled)`, owned by spec 0020** versus
    a column per channel. The shape and its flip conditions are spec 0020's (its review flag 6).
16. **`core.UserPreferences` composite-keyed, written by the user and by an administrator holding
    `Preferences` through one statement** (§3.4) versus a child entity edited through the user
    save. Flips if a bag key ever needs per-key audit.
17. **Typed `PreferredLanguage/Calendar/TimeZone` on the temporal row** (§3.1) versus bag keys.
    Flips if the columns churn history measurably.
18. **`ModifiedAt` kept beside `ValidFrom` on temporal rows** (§2) versus `ValidFrom` as the stamp.
    Flips only with a uniform non-temporal alternative.
19. **Inbox tracking as `InboxSeenAt` plus capped counts** (§3.3) versus materialised counters.
    Flips if the capped count query shows on the profile.
20. **Sensitive securables default-on for role and user edits, removable per distribution through
    `MarkNotSensitive`** (§4.3) versus opt-in per distribution. Flips if step-up friction on
    routine user edits is reported.
21. **`PermissionsMaxAge` of two hours** (§6.2) versus none. Flips with evidence that no
    out-of-band path exists or that the reload cost shows.
22. **`Users.OnlyNewUsersDeletable`** (§3.9) versus allowing deletes with audit FKs surfacing as
    errors. Flips if GDPR-style erasure needs more than deactivation.
23. **Writes on readable-but-not-writable rows return 403** (§5.3) versus 404 uniformly. Flips if
    the 403 leaks existence the tenant considers sensitive.
24. **`Subject varchar(255)` binary-collated** (§3.1) versus `uniqueidentifier`. Flips only if the
    authority is fixed forever.
25. **Filter column 2048 characters** (§3.7) versus 4000. Flips if real filters exceed it.
26. **Namespace `Tellma.Core.Abstractions.Access`** (§1.1) versus `.Security`. Naming preference.
27. **Guarded prologue on every batch including `Persist`, the in-transaction `50412`/`50401`
    re-checks, no stamp-row `UPDLOCK`** (§7.4) versus a lock-based closure of the window on the
    caller's stamp row. Flips if the waits on the tag rows measurably delay permission or settings
    writers.
28. **Non-member and deactivated callers both get 404 `tenant-not-found`** (§7.6) versus 403 or
    401. Flips if support tooling needs to distinguish the two from the client.
29. **`Gender` kept on `core.Users` and `UserProfile`** (§3.1, §7.1) for the invitation's grammar
    and for spec 0015's `UserProfileView`, whose `Gender` feeds the SPA's gender-inflected strings
    about the current user, versus dropping it. Flips if neither the identity server nor the SPA
    needs it.
30. **`RoleMemberships` single-owned by `User`** (§3.6) versus dual ownership with cross-owner
    stamp bumps. Flips if role-side bulk membership editing becomes the dominant workflow.
31. **The opaque preference bag** (§3.4) versus declared `SettingKey<T>` user-scope keys. Flips if
    the server ever needs to read a preference.
32. **A denial re-verified once per `FastDenyWindow`** (§6.4) versus cache-only decisions. Flips if
    the re-check round trip shows in the histograms.
33. **The permissions and connect caches are sized by spec 0012's `TellmaCacheOptions`** (§6.2,
    §6.3; one options surface for every cache kind, no sliding expiration) versus an
    `AccessOptions.MaxCachedUsers` with a sliding expiration owned here. Flips if the connect
    cache needs an idle timeout the LRU compaction does not give.
34. **`Permissions.FilterVersionUnsupported` as a distinct code** (§3.7) versus
    `Permissions.FilterInvalid` with an argument. The fix (re-enter the text) differs from a syntax
    error. Flips if the role editor never needs to tell the two apart.
35. **`delete-by-query` sensitive only through its `(R, Delete)` pair** (§4.3) versus every
    `delete-by-query` endpoint sensitive by shape. Flips if a bulk delete on a non-sensitive
    resource proves to need step-up in practice.

## Appendix A — Validation and guard codes

| Code | Raised by | Condition |
|---|---|---|
| `Users.EmailLockedAfterInvite` | `UserAccessRules` | `Email` changed while `State <> New` |
| `Users.KindNotAllowed` | `UserAccessRules` | `Kind = System` in a payload |
| `Users.EmailNotAllowed` | `UserAccessRules` | an `Email` on a row whose `Kind` is not `Human` |
| `Users.CannotDeactivateSelf` | `UserAccessRules` | own id in `deactivate` |
| `Users.CannotDeleteSelf` | `UserAccessRules` | own id in a delete |
| `Users.CannotRemoveOwnAdministratorMembership` | `UserAccessRules` | own membership in role 1 removed |
| `Users.SystemUserImmutable` | `UserAccessRules` | row 1 in any write |
| `Users.OnlyNewUsersDeletable` | `UserAccessRules` | delete of a user not `New`, or of the system user |
| `Users.MembershipEscalation` | `UserAccessRules` | a granted role holds a permission the caller lacks unrestricted |
| `Users.TooManyRoles` | `UserAccessRules` | memberships > `MaxRolesPerUser` |
| `Users.TooManyPreferenceKeys` | the bag statement's `50422` assertion | keys > `MaxPreferenceKeys` |
| `Users.PreferenceValueTooLarge` | the bag statement's C# check | value > `MaxPreferenceValueBytes` |
| `Roles.PublicRoleHasMembers` | `RoleAccessRules` | `IsPublic` with memberships |
| `Roles.AdministratorImmutable` | `RoleAccessRules` | role 1 made public, deactivated, deleted, or its `*/*` removed or filtered |
| `Roles.HasMembers` | `RoleAccessRules` | delete of a role with memberships |
| `Roles.TooManyPermissions` | `RoleAccessRules` | permissions > `MaxPermissionsPerRole` |
| `Permissions.UnknownSecurable` | `RoleAccessRules` | `(Resource, Action)` not registered |
| `Permissions.FilterNotSupported` | `RoleAccessRules` | filter on a securable without `FilterRoot`, or on `Resource = '*'` |
| `Permissions.FilterInvalid` | `RoleAccessRules` | `Validate` diagnostics |
| `Permissions.FilterTooLong` | `RoleAccessRules` | filter > `MaxFilterLength` |
| `Permissions.FilterVersionUnsupported` | `RoleAccessRules` | an unchanged filter whose stored stamp is below `QueryexLanguage.Minimum` |
| `Permissions.WildcardResourceOnPublicRole` | `RoleAccessRules` | `Resource = '*'` on a public role |
| `Permissions.EscalationBeyondSelf` | `RoleAccessRules` | permission the caller lacks unrestricted |
| `Access.LastAdministrator` | guard L1, `THROW 50422` | no active invited-or-joined human administrator remains |
| `Access.PublicRoleHasMembers` | guard L2, `THROW 50422` | a public role has a membership |
| `Access.AdministratorRoleDamaged` | guard L3, `THROW 50422` | role 1 inactive, public, or without its `*/*` |
| `Access.LockTimeout` | `THROW 50503` | the application lock was not acquired within `SecurityLockTimeout`; transient |
| `RowSecurity` | spec 0014's post-check, `THROW 50403` | a saved row lies outside the decision's filter; `ForbiddenException` |
| `CallerInvalid` | persist re-check, `THROW 50401` | the caller was deactivated since the prologue; `TenantNotFoundException` |
| `StaleVersionTag` | spec 0012's guard, `THROW 50412` | a tag changed since the prologue; one recompose, then `StaleContextException` |

The codes spec 0017's `UserService` and invitation flow raise are published there (spec 0017 §3.2,
§3.3, §3.5, §3.8).
