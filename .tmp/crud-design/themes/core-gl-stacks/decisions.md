# Core and GL reference stacks (spec 0017) — settled design

Scope: `UserService` (invitation through the identity server's bulk invite and delivery-status APIs, state transitions, re-invite, self-service profile and preferences, image, notification settings, test notification, activate/deactivate with lockout guards), `RoleService` (validation against the securables registry, filter validation through Queryex `Validate` with the language-version stamp), the settings edit API, the `Tellma.Module.Gl` package pair with `Center` as the first tree entity, seed data and well-known rows, the reference distribution's migrations, and the local-development admin bootstrap. Tables and contracts owned by other specs (users, roles and permissions by 0013; settings and tags by 0012; the batch, emitter and entity contract by 0011; the pipeline by 0014) are stated here in the exact shape this spec consumes, and section 10 lists what those specs must reconcile.

## 1. Critique of the brain dump for this theme

**The user state model conflates three axes.** "New → Invitation submitted → Active" mixes what the tenant wrote itself, what the identity server knows about email delivery, and the `IsActive` capability flag. The identity server's implemented contract separates them cleanly: the tenant knows `State ∈ New | Invited | Active` plus the outcome and error of the last invite it raised; the identity server knows nine delivery states scoped to the invitations this client raised; `IsActive` is orthogonal. The brain dump's list is not exhaustive: it misses the `Active` invite outcome (the user already holds a credential, **no email is sent**, and the delivery-status API reports `NotFound` for that user forever, so the tenant must send its own notice and must remember the outcome to interpret `NotFound`), refusal (`disabled`/`purged` accounts return a per-user error string, which the admin needs to see without a second call), and the prefix-result rule when the caller cancels. D2 replaces the list.

**Write-once columns are a service-layer rule.** Two UDTTs per table would reintroduce the rejected `ForSave` drift. `Subject` is never client-settable; `Email` becomes write-once once the user is invited, enforced by one validation rule (D5).

**`SavedAt` next to `ValidFrom` is redundant, and splitting the audit vocabulary by temporality is a mistake.** A report that asks "who created this role" would query history for one entity and a column for another. D4 adopts four audit columns on every top-level entity, with system-versioning as an additive capability. `ModifiedAt` doubles as the concurrency token.

**`Center` carries derivable columns and a mis-scoped type set.** `IsLeaf` is `SubtreeCount = 1`; `Level` is `Node.GetLevel()`; both go. `SubtreeCount`/`ActiveSubtreeCount` stay because the tree view needs "does this node have (active) children" for every rendered row. `Service | Operation | Sale` drops the two structural types the legacy schema relied on (`Abstract`, `BusinessUnit`, the only types allowed to have children) and with them the rule that keeps postings on leaves. D12 restores the five-value set and the parent-type rule.

**The hierarchyid question is answered by the driver.** `Node` cannot ride a TVP without `Microsoft.SqlServer.Types.SqlHierarchyId`, so in-memory recomputation is off the table for bulk save; the recompute is SQL appended to the batch (D13, verified on LocalDB).

**The settings question is "field mask vs null-means-remove", not "patch vs load-and-save".** JSON Patch is first-party but brings in-place mutation and an operation-based attack surface; merge patch has no maintained package and cannot say "clear this nullable setting" distinctly from "leave it". D9 chooses a typed partial DTO with an explicit `Fields` list.

**Naming.** `core.User` (singular) contradicts `gl.Invoices` in ARCHITECTURE.md and the legacy `dbo.Centers`; tables are plural, classes singular. `UserSettings` becomes `UserPreferences` ("settings" is the tenant, "preferences" is the person). `CenterType` keeps its name (`Type = 'Service'` reads badly in Queryex and collides with `System.Type`). "`RoleMembership` is edited and saved together with the User" is half the truth: an admin creating a role adds fifty members from the role page, so the membership is a weak entity with two owners (D7).

**Gaps.** No home for the language, calendar and time zone the server needs when rendering to a user who is not making a request (invitation locale, notifications, scheduled reports) — D6 gives them typed columns. No statement of who runs seeds and as whom, no bootstrap beyond "match `admin@localhost`", no seed versioning — D15 and D16. The self-service endpoints' "no permissions needed" is not reconciled with the RLS model — D8 makes self-service a bespoke criterion `Id = me()` on the same pipeline. Nothing says what happens when a stored permission filter was stamped under a Queryex language version the engine no longer compiles, or when a role is re-saved unchanged after a version bump — D7 settles both (an unchanged filter keeps its stamp; only changed text is re-stamped).

## 2. Decisions

### D1 — Package, project and namespace layout
- **Decision.** `Tellma.Core.Abstractions` gains `Tellma.Core.Abstractions.Users` (`User`, `Role`, `RoleMembership`, `Permission`, `UserPreference`, `UserNotificationOptOut`, `UserState`, `NotificationChannel`, `CoreWellKnownIds`, `IUserService<TUser>`), `Tellma.Core.Abstractions.Settings` (settings DTOs, `ISettingsService`), `Tellma.Core.Abstractions.Identity` (`IIdentityServerClient` and its records) and `Tellma.Core.Abstractions.Seeding` (`ISeed`, `ISeedContext`, `ITenantBootstrapSource`). `Tellma.Core` gains `Tellma.Core.Users` (`UserService<TUser>`, `RoleService<TRole>`, invitation orchestration, the lockout invariant), `Tellma.Core.Settings` (`SettingsService`), `Tellma.Core.Identity` (`IdentityServerClient` with its token cache) and `Tellma.Core.Seeding` (`SeedRunner`, `BootstrapAdminSeed`). The GL module lives at `src/module/gl/Tellma.Module.Gl.Abstractions/` (namespace `Tellma.Module.Gl`: `Center<TCenter>`, `Center`, `CenterType`) and `src/module/gl/Tellma.Module.Gl/` (`CenterService<TCenter>`, `GlFeature`); `src/module/` does not exist today and is created by this spec. A root `taxonomy.json` (it does not exist today) is created as `{ "modules": ["Gl", "Sales", "Procurement", "Inventory", "Hr", "Manufacturing"], "compliance": ["Sa", "Et", "Ifrs", "UsGaap"] }` in the PascalCase segment form package names use, and a test asserts every `Tellma.Module.<m>` project against it. Tests mirror `src/`: `test/core/Tellma.Core.Tests`, `test/core/Tellma.Core.IntegrationTests` (LocalDB, `Category=Integration`; a `Live=true` subset drives an in-process identity server), `test/module/gl/Tellma.Module.Gl.Tests`.
- **Rationale.** The module must never reference `Tellma.Core`, so every base it inherits (`TreeCrudService<T, TKey>`) is an Abstractions type and the concrete pipeline arrives through DI. The identity client's contract sits in Abstractions so tests substitute it; its implementation has no extension points and does not deserve a package.
- **Rejected.** A `Tellma.Core.Identity` package (ceremony for one class). `Center` in Core (it is a GL noun and the first proof that a module composes). `GL` as the segment (the compliance registry already uses PascalCase two-letter segments, `Sa`, `Et`).
- **Confidence.** High.

### D2 — The user state model
- **Decision.** `Users.State nvarchar(16) NOT NULL` holds `New | Invited | Active` (enum `UserState`, enum-as-string). Three server-owned columns record the last invite: `InvitedAt datetime2(3) NULL`, `InvitationOutcome nvarchar(16) NULL` ∈ `Invited | Reinvited | Active` (the identity server's status verbatim), `InvitationError nvarchar(1024) NULL` (the per-user error verbatim; cleared by the next successful invite). Transitions: `New → Invited` when an invite call returns a `sub` for the row (any of the three outcomes); `Invited → Active` when the subject first connects to this tenant — the connect step of spec 0013 performs a targeted `UPDATE core.Users SET State = N'Active' WHERE Id = @id AND State = N'Invited'` that does **not** bump `ModifiedAt`/`ModifiedById` (`State` is server-owned and hydrated from the DB image on every save, so an admin's open page cannot lose it, and the write is once per user lifetime); a refused invite leaves `State` unchanged and sets `InvitationError`. Re-invite is allowed from `Invited` (the identity server answers `Reinvited` and queues a fresh link, or `Active` if the user enrolled meanwhile). Inviting an `Active` user is a per-id validation error `Users.AlreadyActive`; inviting an `IsActive = 0` user is `Users.Inactive`. `IsActive` is the standard activatable capability and is orthogonal. The fine-grained view is not stored: `GetInvitationStatusAsync` calls the delivery-status API live and renders `NotFound` as "no invitation email was needed" when `InvitationOutcome = 'Active'` and as "raised by another client registration" otherwise, `Sent` with `expectsDeliveryEvents = false` as terminal, and `reason` only for `Bounced | Rejected | Abandoned`.
- **Rationale.** The tenant stores only what it wrote; the identity server owns delivery and is asked on demand, so there is no reconciliation job and no drift. Storing the outcome is what makes `NotFound` interpretable.
- **Rejected.** Persisting delivery states (needs polling or webhooks the identity server does not offer distributions). A `Refused` state (the refusal is transient — fix the account, retry — so it is an error on the row, not a state). A `Deactivated` state (that is `IsActive`). Deriving `State` from `Subject` and a `FirstConnectedAt` column in the sibling activity table (removes the temporal write but hides the admin-visible state behind a join the grid cannot express).
- **Confidence.** High.

### D3 — Invitation through the identity server
- **Decision.** `UserService<TUser>.InviteAsync(IReadOnlyList<int> ids, CancellationToken)`: (1) permission `users:invite` with the caller's RLS filter; (2) one batch round trip loading the users by id through the TVP list restriction, asserting every id resolved under RLS (a missing id is the not-found-shaped validation error per id) and validating state per D2; (3) chunks of at most 1000 `IdentityInvitation(Email, DisplayName: Name, Locale: PreferredLanguage ?? tenant primary language, Gender, ReturnUrl: "<distribution origin>/<tenantId>")` posted through `IIdentityServerClient.InviteAsync` **with no transaction open and no row locked**; (4) one batch round trip containing a single targeted update from the standalone table type `UserInvitationOutcomeList`:

```sql
UPDATE u SET
    u.Subject = COALESCE(u.Subject, o.Subject),
    u.State = CASE WHEN o.Subject IS NULL OR u.State = N'Active' THEN u.State ELSE N'Invited' END,
    u.InvitedAt = CASE WHEN o.Subject IS NULL THEN u.InvitedAt ELSE @now END,
    u.InvitationOutcome = COALESCE(o.Outcome, u.InvitationOutcome),
    u.InvitationError = o.Error,
    u.ModifiedAt = @now, u.ModifiedById = @me
FROM [core].[Users] u JOIN @outcomes o ON o.UserId = u.Id
WHERE u.Subject IS NULL OR o.Subject IS NULL OR u.Subject = o.Subject;
```

  followed in the same batch by the executor's automatic bump of the tenant security tag (a subject assignment changes who can connect). A row whose stored `Subject` differs from the returned `sub` is excluded by the `WHERE` and reported as `Users.SubjectMismatch` (the email now belongs to a different identity account; an operator intervenes). A unique-index violation on `Subject` (two tenant rows resolving to one identity account) maps to a field error on `Email`. (5) Post-commit: users whose outcome was `Active` receive the tenant's own "you have been added to {TenantName}" email through `IEmailSender` in one batch (audience `Transactional`, the user's preferred language and calendar); under a sandbox tenant this email follows the sandbox routing of the email pipeline, while the identity server's own invitation email is real — a sandbox tenant inviting a person creates a real identity account, which is exactly what signing in to the sandbox requires. (6) Returns `InviteResult(Id, Outcome, Error)` per id in request order. If the identity call throws after some chunks succeeded, the users already processed are written back (their subjects are real) and a `PartialFailureException` carries the ids not attempted; the web layer maps it to a 502 with the partial results in the body so the admin retries only the remainder. Cancellation is forwarded to the identity call; a prefix result is written back exactly like a partial failure.
- **Rationale.** The identity server's `Reinvited` queues a fresh email, so re-running the whole call after a write-back concurrency error would spam users; the targeted update ignores `ModifiedAt` (an invite outcome does not conflict semantically with an admin's concurrent rename) but bumps it, so the admin's stale details page gets a concurrency error on its next save, which is the right signal.
- **Rejected.** Re-saving the loaded entities through the bulk pipeline (whole-row upsert with a stamp check: a concurrent rename would fail the write-back after the email went out). Calling the identity server inside the save transaction (a lock held across I/O). Inviting automatically on creation (admins create users in bulk and invite when the roles are right; import needs the two-step flow too).
- **Confidence.** High.

### D4 — Audit vocabulary and the concurrency token
- **Decision.** Every top-level entity in this spec carries `CreatedAt datetime2(7) NOT NULL`, `CreatedById int NOT NULL FK core.Users`, `ModifiedAt datetime2(7) NOT NULL`, `ModifiedById int NOT NULL FK core.Users`. `ModifiedAt` is the concurrency token: the client echoes it, the persist batch checks `(Id, ExpectedModifiedAt)` and fails with `ConcurrencyException` unless the save carries `Override = true`. Every user-visible mutation bumps it (save, activate, deactivate, invite, and a change to a weak child — including a membership written from the other owner, D7); bookkeeping never does, because bookkeeping columns (`LastActiveAt`, tags, inbox counters) live in `core.UserActivity`, and the once-per-lifetime `Invited → Active` write is the one server-owned exception (D2). `Users`, `Roles`, `RoleMemberships` and `Permissions` are additionally system-versioned (`ISystemVersioned`; history tables `core.Users_History` etc.); `Centers`, `UserPreferences`, `UserNotificationOptOuts`, `Settings` and `SettingOverrides` are not. Weak rows carry no audit columns: the owner's stamp is bumped whenever any child changes, and the child's temporal period plus the owner's history answers "who changed this permission". The emitter's synchronize step must skip unchanged child rows (a temporal `UPDATE` writes a history row even when nothing changed).
- **Rationale.** One vocabulary for every report; `rowversion` bumps on bookkeeping and cannot ride a UDTT as a stamp cleanly; a `datetime2(7)` stamp is human-readable in history and survives restore.
- **Rejected.** `rowversion`. `SavedById` on weak rows (redundant under the owner-bump rule).
- **Confidence.** High for the vocabulary; medium for "no audit on weak rows" (see review flags).

### D5 — Editable, server-owned and write-once columns on `User`; delete policy
- **Decision.** Client-settable through the admin save: `Email`, `Name`, `Name2`, `Name3`, `PreferredLanguage`, `PreferredCalendar`, `PreferredTimeZone`, `Gender`, `ContactEmail`, `ContactMobile`, `ImageId`, `IsActive` (also through the capability endpoints), `RoleMemberships[]`, `NotificationOptOuts[]`. Server-owned, hydrated from the DB image before validation on every save: `Subject`, `State`, `InvitedAt`, `InvitationOutcome`, `InvitationError`, the four audit columns. Write-once rule: when the existing row has `State <> 'New'`, a changed `Email` is the field error `Users.EmailLockedAfterInvitation`; the data layer has one UDTT and knows nothing about it. `Email` is normalised (trim, lower-case) in preprocessing and is unique per tenant by index; `Subject` is unique by filtered index. Delete: `users:delete` applies only to rows with `State <> 'Active'` (`Users.CannotDeleteActive` otherwise); a user who has connected has acted, is referenced by audit FKs, and is deactivated instead. A user cannot delete or deactivate their own row (`Users.CannotDeactivateSelf`).
- **Rationale.** The entity stays the single source of truth and the data layer stays dumb; the pipeline's "server-owned columns are hydrated from the DB image" step makes a client-sent `Subject` harmless. Restricting delete to never-connected users turns an unpredictable FK failure into a predictable rule.
- **Confidence.** High.

### D6 — Typed profile columns, preferences, and notification opt-outs
- **Decision.** `Users` carries `PreferredLanguage nvarchar(16) NULL` (BCP 47; must be one of the distribution's resource languages; null means tenant primary), `PreferredCalendar nvarchar(16) NULL` (`gregorian | umalqura | ethiopic`; null means tenant primary), `PreferredTimeZone nvarchar(64) NULL` (IANA id; null means tenant time zone), `Gender nvarchar(8) NULL` (`Female | Male`; forwarded only to the identity server's invitation grammar), `ContactEmail nvarchar(256) NULL` (null means `Email`), `ContactMobile nvarchar(32) NULL` (E.164). UI-only preferences live in `core.UserPreferences (Id, UserId, Key nvarchar(128), Value nvarchar(max))`, unique `(UserId, Key)`, dotted keys such as `nav.pinned` and `grid.users.columns`, at most 200 keys per user and 32 KB per value (validation limits); the server never reads them and does not cache them; a change bumps the user's `PreferencesTag` and the endpoint returns the new tag. Notification muting is an opt-out list: `core.UserNotificationOptOuts (Id, UserId, NotificationType nvarchar(64), Channel nvarchar(16))`, unique `(UserId, NotificationType, Channel)`, `Channel ∈ Email | Sms | Push` (enum `NotificationChannel`); a row exists only where the user muted a type on a channel, so adding a channel is no schema change and the dispatcher's bulk query is one `NOT EXISTS`. Spec 0019 owns the type registry and the non-mutable set; a validation rule rejects an opt-out on a non-mutable type (`Users.NotificationCannotBeMuted`). Push subscriptions are out of scope; when they arrive they are a per-device table.
- **Rationale.** Everything the server needs when the user is not on the line is typed and queryable; everything only the SPA needs is a bag it owns. Pinned screens stay a key; an admin-curated navigation is a tenant setting when needed, not a user preference.
- **Rejected.** One `NotificationSettingsJson` column (`OPENJSON` in the dispatcher's bulk query; not indexable). A per-type row with one bit per channel (five columns, default semantics, a schema change per channel). All preferences as JSON in one row (the SPA writes one key at a time).
- **Confidence.** High for the split; medium for `Gender` (review flag).

### D7 — Roles, memberships, permissions and `RoleService`
- **Decision.** `Roles`: `Name/Name2/Name3 nvarchar(255)` (unique; filtered unique when nullable), `Code nvarchar(50) NULL` (filtered unique), `IsPublic bit NOT NULL DEFAULT 0`, `IsActive`, the four audit columns. `RoleMemberships (Id, UserId, RoleId, Notes nvarchar(1024) NULL)` is a weak entity with **two owners**: `UserService` synchronises it under `UserId`, `RoleService` under `RoleId`; unique `(UserId, RoleId)`; a membership on a role with `IsPublic = 1` is `Roles.PublicRoleHasMembers` from both owners. Because "save replaces the child set", a stale owner could silently delete a membership the other owner just added, so each owner's persist batch bumps the *other* owner's stamp for every membership it inserted or deleted (`UPDATE core.Users SET ModifiedAt = @now, ModifiedById = @me WHERE Id IN (SELECT UserId FROM #membershipDelta)` from a role save, and symmetrically `core.Roles` from a user save); the emitter exposes the synchronize delta to later statements in the batch. `Permissions (Id, RoleId, Resource nvarchar(128), Action nvarchar(64), Filter nvarchar(2048) NULL, FilterLanguageVersion int NULL, Notes nvarchar(1024) NULL)` is owned by `RoleService`; two rows on the same securable with different filters are legal and OR together.

  `RoleService<TRole>.ValidateAsync` checks every permission against the securables registry: the resource exists or is `*`; the action exists for that resource or is `*`; a non-null `Filter` is allowed only when the securable declares a filter root, and on `resource:*` only when the resource has one root shared by all its actions; a filter on `*:*` or `*:action` is `Roles.FilterOnWildcard`. Filter text is validated with `QueryexEngine.Validate(filter, new ValidationOptions { Schema = tenantSchema, Root = securable.FilterRoot, Mode = QueryexMode.Filter, LanguageVersion = stamp, HasUser = true, Parameters = [], Limits = QueryexLimits.Default })` where `stamp` is `QueryexLanguage.Version` for a permission whose `Filter` text is new or differs from the DB image (or whose stored stamp is null), and the **stored** `FilterLanguageVersion` for unchanged text; an unchanged filter whose stamp is below `QueryexLanguage.Minimum` is `Roles.FilterVersionUnsupported` (the user re-enters the text to affirm it under current rules); every engine diagnostic becomes a field error at `Permissions[i].Filter` localized from its code and arguments; on success the stamp used is what is persisted, whatever the client sent. A role save bumps the tenant security tag and, in the same batch, `PermissionsTag` of every current member and of every member removed by this save (`UPDATE core.UserActivity SET PermissionsTag = NEWID() WHERE UserId IN (…)`); a public role save bumps only the tenant security tag, which every user's cache key includes; activating or deactivating a role bumps its members' tags the same way.
- **Rationale.** Dual ownership is what admins do; the cross-owner stamp bump is the price of "save replaces the set" and costs one statement in a round trip that already exists. `IsPublic` reuses the whole role UI and the securables validation; the membership rejection is the only extra rule. Re-stamping only changed text is what makes spec 0008's language-version stamp meaningful: a meaning-altering engine change must never silently re-stamp an untouched security predicate.
- **Rejected.** A seeded public role with a hardcoded id as the mechanism (still seeded as a convenience, D15). A separate role-less permissions table (a second editor and validator). Always re-stamping with the current version on save (silently changes the meaning of untouched filters after a version bump). Single ownership with a read-only member list on the role page (see review flags).
- **Confidence.** High for validation and stamping; medium for dual ownership.

### D8 — Self-service, lockout guards, deactivation side effects, well-known-row protection, "can I, and why"
- **Decision.** Self-service is a bespoke permission criterion, not a permission-free path: `UserService` grants `users:read` and `users:save` on the filter `Id = me()` to every signed-in user, restricted to the self-editable set (`Name/Name2/Name3`, `PreferredLanguage`, `PreferredCalendar`, `PreferredTimeZone`, `ContactEmail`, `ContactMobile`, `ImageId`, `NotificationOptOuts[]`); any other changed column under the self path is `Users.NotSelfEditable`. Endpoints: `users/me` (the details envelope of the caller plus `preferences` and `preferencesTag`), `users/me/save` (one entity, the standard pipeline under the self criterion), `users/me/preferences/set` `{ items: [{ key, value }] }` and `users/me/preferences/delete` `{ keys }` (both return `{ preferencesTag }`), `users/me/test-notification` `{ channel }` (renders the "test" template in the user's language and calendar and sends to `ContactEmail`/`ContactMobile`; `Sms` and `Push` return `NotConfigured` until a connector exists).

  The lockout invariant is one assertion appended inside the transaction of every batch that writes `Users`, `Roles`, `RoleMemberships` or `Permissions` (skipped when the actor is the system user): after the writes, at least one user exists with `IsActive = 1 AND State = N'Active' AND Subject IS NOT NULL` holding an active membership in an active role that has a permission `Resource = N'*' AND Action = N'*' AND Filter IS NULL` (else `Users.LastAdministrator`); and the acting user, if they held that grant before the batch, still holds it (else `Users.CannotStripOwnAdministration`). The batch's assert statement raises a mapped error that the executor turns into the typed exception and the transaction rolls back. Rows in the reserved band (D15) are protected in validation: the system user cannot be saved, deleted or deactivated; the `Administrator` and `Public` roles cannot be deleted or deactivated and the wildcard permission row cannot be removed, filtered or narrowed; their names and codes are editable. Deactivation and deletion of a user enqueue, post-commit, `IUserSessionTerminator.TerminateAsync(tenantId, subject)` (closes SignalR connections and revokes the BFF session; the host owns the implementation), and even if that call fails the connect step refuses the inactive user on their next request. `users/explain-permission` `{ userId, resource, action }` → `{ allowed, filter, grants: [{ roleId, roleName, isPublic, filter }] }` delegates to the permission evaluator's explain API; the caller must hold `users:read` on the target or be the target.
- **Rationale.** One save path, one audit trail, one RLS mechanism. The invariant as a post-write assertion costs no round trip and cannot be raced by a concurrent admin edit, because it runs under the row locks the writes took; a pre-check can be passed by two admins deactivating each other.
- **Rejected.** Separate self-service DTOs and handlers. Checking the invariant before persist.
- **Confidence.** High.

### D9 — The settings edit API
- **Decision.** Settings are read publicly: `settings` (POST, no body) returns `SettingsEnvelope { general, overrides: { [category]: { [key]: value } }, tag }`; the SPA caches on `tag`. The typed row is edited by `settings/general/save` with `GeneralSettingsPatch { Fields: string[], TenantName?, TenantName2?, TenantName3?, PrimaryLanguage?, SecondaryLanguage?, TernaryLanguage?, PrimaryCalendar?, SecondaryCalendar?, TimeZone?, ExpectedModifiedAt? }`: only members named in `Fields` are applied (`SecondaryLanguage: null` with `"SecondaryLanguage"` listed clears it; unlisted members are untouched); an unknown name in `Fields` is a 400. The service takes the current row from the tag-validated server cache, applies the listed fields, validates the whole row (languages distinct and within the distribution's resource languages; calendars distinct and supported; time zone resolvable; primary values required), and persists the single row through the bulk pipeline with the stamp check when `ExpectedModifiedAt` is present, in one round trip; the executor bumps the settings tag because the statement writes `core.Settings`. Key-value settings are edited by `settings/{category}/entries/set` `{ items: [{ key, value }] }` and `settings/{category}/entries/delete` `{ keys }`; keys match `^[a-z][a-zA-Z0-9]*(\.[a-z][a-zA-Z0-9]*)*$`; values are JSON text validated against the key's registered `SettingDefinition`; an unregistered key is a validation error. Securables: `settings.general:save`, and `settings.<category>:save` per registered category; there is no read action. Removing a content language is allowed, hides the corresponding `Name2`/`Name3` from the Queryex schema, retains data, and the UI warns.
- **Rationale.** Field-mask semantics distinguish "clear" from "leave", fit source-generated JSON, and cost one round trip because the current row is already cached and tag-validated. Per-category resources keep the permission model uniform with entities.
- **Rejected.** `JsonPatchDocument<T>` (operation-based attack surface, in-place mutation, source-generation unverified). RFC 7396 merge patch (no maintained package; cannot express "set to null"). Whole-row load-and-save (an MCP tool call would echo eleven fields to change one).
- **Confidence.** High (review flag on the optional stamp).

### D10 — Securable names this spec requires
- **Decision.** Resources are the entity's Queryex logical name in lower kebab-case plural (`users`, `roles`, `centers`) or a dotted non-entity name (`settings.general`, `settings.<category>`); actions are lower-case verbs (`read`, `save`, `delete`, `activate`, `deactivate`, `invite`); `*` is the wildcard on either axis; the pair is written `resource:action` in logs, diagnostics and explain output. Spec 0013's registry owns the scheme; this spec needs exactly these names to exist, plus the ability for a service to register a custom action (`users:invite`) with the entity's filter root.
- **Confidence.** Medium (naming is spec 0013's call).

### D11 — The GL package, `Center` classes and `GlFeature`
- **Decision.** `Tellma.Module.Gl.Abstractions` ships `public abstract class Center<TCenter> : TreeEntity<TCenter, int>, IActivatable, IMultilingual, IAudited where TCenter : Center<TCenter>` (carrying `CenterType`, `Name/Name2/Name3`, `Code`, `IsActive`; `ParentId`, `Parent` typed `TCenter?`, `Node`, `SubtreeCount`, `ActiveSubtreeCount` come from the tree base) and `public class Center : Center<Center>` as the non-abstract default. A distribution that adds columns closes the generic base directly (`public sealed class Center : Tellma.Module.Gl.Center<Center>`), because inheriting the default leaf would leave `Parent` typed against an unmapped class. `Tellma.Module.Gl` ships `CenterService<TCenter> : TreeCrudService<TCenter, int> where TCenter : Center<TCenter>` (validation hooks only) and `GlFeature`:

```csharp
public void Declare(FeatureBuilder features)
{
    features.Requires<CoreFeature>();
    features.Entity<Center>(e => e
        .Table("gl", "Centers")
        .Tree(siblingOrder: c => c.Id)
        .Activatable()
        .Audited()
        .Multilingual(c => c.Name)
        .NaturalKey(c => c.Code)
        .Searchable(c => c.Code, c => c.Name, c => c.Name2, c => c.Name3)
        .Service<CenterService<Center>>());
    features.Seed<GlSampleCentersSeed>();
}
```

  Standard operations, endpoints (`centers/query|details|save|delete|delete-by-query|children|delete-with-descendants|activate|deactivate`), securables (`centers:read|save|delete|activate|deactivate`, all filterable with root `Center`) and the "hide inactive" default filter are projected from the declaration. A distribution using its own leaf calls `.AddModule<GlFeature>(g => g.UseCenter<Center>())`. `User` and `Role` are single non-abstract unsealed classes (no navigation typed against a generic parameter), so a distribution extends them by plain inheritance and registers `.AddCore(c => c.UseUser<User>().UseRole<Role>())`.
- **Rationale.** The generic on `Center` is forced by the self-navigation; `User`/`Role` follow the architecture's "single non-abstract class when no cross-entity navigation needs a generic" rule, which an abstract `User` would violate by forcing every distribution to declare a leaf.
- **Confidence.** High for the shape; the builder method names are spec 0014's.

### D12 — `Center` columns and `CenterType`
- **Decision.** `public enum CenterType { Abstract, BusinessUnit, Service, Operation, Sale }` in `Tellma.Module.Gl.Abstractions`, stored as `nvarchar(32) NOT NULL` under the enum-as-string convention with a generated `CHECK ([CenterType] IN (N'Abstract', N'BusinessUnit', N'Service', N'Operation', N'Sale'))`. Validation: a center may have children only if its type is `Abstract` or `BusinessUnit` — `Centers.ParentMustBeGrouping` on `ParentId` of the child, and on `CenterType` of a parent whose type changes to a leaf type while `SubtreeCount > 1` (the validation context loads the new parents by id and the existing rows being updated). `IsLeaf` and `Level` are dropped as stored columns; `Level` exists as a non-persisted computed column `AS [Node].GetLevel()` so the Queryex `level()` amendment has a store column; `SubtreeCount int NOT NULL` and `ActiveSubtreeCount int NOT NULL` stay. `Code nvarchar(50) NOT NULL` unique is the natural key. `Node hierarchyid NOT NULL` unique, `[ExcludeFromTableType]`. Deleting a center that has children fails FK and maps to `Centers.HasChildren` (use delete-with-descendants); delete-with-descendants is one `DELETE … WHERE Node.IsDescendantOf(@root) = 1` per requested root in one statement over the TVP of roots (a self-referencing FK is checked at statement end, so parent and children go together).
- **Rationale.** The five values are the legacy schema's structural core mapped onto the textbook taxonomy (grouping, profit/investment centre, service cost centre, production cost centre, revenue centre); SG&A and expenditure-control types are additive enum members for later packs. A CHECK constraint keeps the invariant in the database for every reviewer of a posting rule.
- **Rejected.** An extensible string type with a registry (posting rules key on the type; an unknown type is a GL bug). `Type` as the column name.
- **Confidence.** High.

### D13 — Tree maintenance in SQL (verified on LocalDB, 2026-09-02)
- **Decision.** The tree capability appends two statements to every persist batch that writes a tree table (save, delete, delete-with-descendants, activate, deactivate; the last two skip the node statement). Inserted rows get a placeholder node the emitter supplies for the excluded column — `Node = CAST('/0/' + CAST([Id] AS varchar(11)) + '/' AS hierarchyid)` — unique per id and disjoint from every real path because sibling numbers start at 1 (`/0/1000/` parses and sorts below `/1/`, verified). The recompute, parameterised only by table name and sibling order key:

```sql
WITH Numbered AS (
    SELECT Id, ParentId, ROW_NUMBER() OVER (PARTITION BY ParentId ORDER BY Id) AS Num FROM [gl].[Centers]),
Paths AS (
    SELECT Id, CAST('/' + CAST(Num AS varchar(11)) + '/' AS varchar(892)) AS Path FROM Numbered WHERE ParentId IS NULL
    UNION ALL
    SELECT n.Id, CAST(p.Path + CAST(n.Num AS varchar(11)) + '/' AS varchar(892)) FROM Numbered n JOIN Paths p ON n.ParentId = p.Id)
UPDATE c SET Node = CAST(p.Path AS hierarchyid) FROM [gl].[Centers] c JOIN Paths p ON p.Id = c.Id
WHERE c.Node <> CAST(p.Path AS hierarchyid) OPTION (MAXRECURSION 64);

WITH Counts AS (
    SELECT p.Id, COUNT(*) AS SubtreeCount, SUM(CASE WHEN d.IsActive = 1 THEN 1 ELSE 0 END) AS ActiveSubtreeCount
    FROM [gl].[Centers] p JOIN [gl].[Centers] d ON d.Node.IsDescendantOf(p.Node) = 1 GROUP BY p.Id)
UPDATE c SET SubtreeCount = k.SubtreeCount, ActiveSubtreeCount = k.ActiveSubtreeCount
FROM [gl].[Centers] c JOIN Counts k ON k.Id = c.Id
WHERE c.SubtreeCount <> k.SubtreeCount OR c.ActiveSubtreeCount <> k.ActiveSubtreeCount;
```

  A recursive member referencing a prior windowed CTE is accepted by SQL Server (verified), so no temp-table numbering step is needed. Whole-table scope is the default for reference trees and is what `Center` uses; the data-access spec's facility accepts an `IdList` of affected roots for large trees. Cycle validation runs in C# before persist over the loaded ancestor chains of every new parent (`ancestorOf` through the TVP list restriction) plus in-payload parents; a cycle is `Centers.Cycle` on `ParentId`. `MAXRECURSION 64` doubles as the depth cap (`Centers.TooDeep` when the statement fails). Node values are not stable across deletes (later siblings renumber); `Node` is never exposed as an identifier, only consumed by `descendantOf`/`ancestorOf`/`level()`.
- **Rationale.** Deterministic, collision-free, never calls `GetDescendant`, rides the same transaction, needs no `Microsoft.SqlServer.Types` in the binder; ordering siblings by `Id` means renames never rewrite nodes.
- **Rejected.** `GetReparentedValue` per moved root (a loop under serializable isolation). In-memory recomputation (needs every sibling loaded and a TVP that cannot carry `hierarchyid`). Nullable `Node` with a filtered unique index.
- **Confidence.** High for the shape; medium for the counts statement's cost at scale (measure the `IsDescendantOf` self-join against a recursive `ParentId` CTE in 0011's fixture tests).

### D14 — Driver and package pins
- **Decision.** Add `Microsoft.EntityFrameworkCore.SqlServer.HierarchyId` and align the EF Core family to the version it ships against (10.0.11 at the time of writing); bump `Microsoft.Data.SqlClient` from 6.1.1 to at least 6.1.6 (the HierarchyId package's floor; 6.1.5 fixed a transaction-zombie bug). `Microsoft.SqlServer.Types` arrives transitively and is referenced by no Tellma code.
- **Confidence.** High.

### D15 — Well-known rows and the reserved band
- **Decision.** Every `sq_<Table>` sequence is declared `START WITH 1000`; ids `1..999` are the reserved band; a test enumerates `IEntityType.GetSeedData()` across the model and asserts the band. `HasData` rows: `core.Users` id 1 = the system user (`Email = 'system@tellma.invalid'`, `Name = 'System'`, `State = 'Active'`, `Subject = NULL`, `IsActive = 1`, audit columns pointing at itself, `CreatedAt = ModifiedAt = '2000-01-01'`); `core.Roles` id 1 = `Administrator` (`Code = 'ADMIN'`) with `core.Permissions` id 1 = `(*, *, NULL)`; `core.Roles` id 2 = `Public` (`Code = 'PUBLIC'`, `IsPublic = 1`) with no permissions; `core.Settings` single row id 1 with a tenant-name placeholder and `en`/`gregorian`/`UTC` defaults, overwritten by provisioning or the bootstrap. The system user cannot sign in (no subject; connect refuses subject-less rows), holds every permission by code (`IRequestContext.IsSystem` short-circuits evaluation and the lockout invariant), and is the actor for seeds, built-in schedules and background work without a triggering user. No center is seeded as well-known. Ordinary reference data (the sample centers for the reference distribution's Development environment) is a versioned seed through the pipeline under the system user.
- **Rationale.** Low positive ids read well in URLs and logs; 999 rows is ample for rows code references. A system user by code rather than by membership keeps the lockout invariant and the "cannot strip" rule simple.
- **Confidence.** High (review flag on negative ids).

### D16 — Seeds, migrations and the bootstrap
- **Decision.** The Migrator runs `migrate` then `seed` per tenant. A seed implements `ISeed { string Id; int Version; Task ApplyAsync(ISeedContext, CancellationToken) }`, is registered by a feature (`features.Seed<T>()`), ordered by feature dependency then declaration, executed one transaction each through the bulk pipeline as the system user, and recorded in `dbo.__SeedHistory (SeedId nvarchar(150) PK, Version int, AppliedAt datetime2(3))`; a seed re-runs when its `Version` exceeds the recorded one and must be idempotent by natural key. The reference distribution's initial migration enables RCSI with `migrationBuilder.Sql("IF (SELECT is_read_committed_snapshot_on FROM sys.databases WHERE name = DB_NAME()) = 0 ALTER DATABASE CURRENT SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE", suppressTransaction: true)` (an `ALTER DATABASE` cannot run inside the migration transaction; on Azure SQL the guard is a no-op). Bootstrap: `BootstrapAdminSeed` (Core, version-less, runs on every migrator run) asks `ITenantBootstrapSource.GetAdminAsync(tenantId)` for `TenantBootstrapAdmin { Email, Name, Subject?, Language? }` — the reference distribution's implementation reads `Tellma:Bootstrap:Admin` from configuration (a single-live distribution); a multi-live distribution's implementation reads the provisioning record, which spec 0010 owns. The seed creates-or-gets the user by email, ensures membership in role 1, and — when `Subject` is supplied — sets `Subject` and `State = 'Invited'` directly (Development only; the seed refuses a supplied `Subject` outside Development), otherwise leaves the row `New` and calls `IIdentityServerClient.InviteAsync` for it so a deployed tenant's first admin receives the invitation from the migrator, which hosts the same composition and holds the distribution's client credentials. The reference distribution's `appsettings.Development.json` sets `Email = admin@localhost`, `Subject = 00000000-0000-0000-0000-000000000001`, matching the in-process identity server's dev admin; the admin's first connect flips the row to `Active`; a database reset re-seeds it.
- **Rationale.** One code path for dev and deployed bootstrap, differing only in whether the subject is known; seeds versioned like migrations so packs can evolve reference data; the system user as actor keeps `CreatedById` non-null and honest; `Invited` rather than `Active` for the dev admin exercises the real transition.
- **Rejected.** `HasData` for the dev admin (environment-specific data in the model snapshot). A web-process bootstrap endpoint (a second privileged path).
- **Confidence.** High.

### D17 — Round-trip budget
- **Decision.** Counted with the connect step folded into the first business round trip; add one if it is not folded. Invite: 2 DB round trips plus ⌈n/1000⌉ HTTPS calls between them with no transaction open, plus one post-commit email batch. Invitation status: 1 plus ⌈n/1000⌉ HTTPS calls, no writes. User save: 2 (validation context; persist with membership synchronisation, cross-owner stamp bump, tag bumps and the lockout assertion in one batch). Role save: 2 (validation context; persist with the same statements); securables and Queryex validation are in memory. Center save: 2 (ancestor chains and existing rows; persist followed by the node and count recompute). Activate/deactivate: 1 for users (update with the RLS predicate, affected-row assertion and the lockout assertion), 1 for centers (update plus the count recompute). Settings patch: 1. Preference set/delete: 1. `users/me`: 1. Test notification: 1 read plus the send. Every statement is parameterised by TVP or scalar; no per-id SQL text; no lock is held across I/O. These budgets are asserted by the DB-call meter in integration tests.
- **Confidence.** High.

## 3. Contracts

### 3.1 Owned by this spec — `Tellma.Core.Abstractions`

```csharp
namespace Tellma.Core.Abstractions.Identity;

/// <summary>The distribution's client of the identity server's management API. One instance per
///     distribution; tenant-agnostic; every call is machine-to-machine and outside any database transaction.</summary>
public interface IIdentityServerClient
{
    /// <summary>Creates-or-gets identity users by email and queues their invitation links. Chunks at the
    ///     server's limit of 1000 and returns one result per input in input order; a cancelled call returns
    ///     the prefix already processed.</summary>
    Task<IReadOnlyList<IdentityInvitationResult>> InviteAsync(IReadOnlyList<IdentityInvitation> invitations, CancellationToken cancellationToken);

    /// <summary>Reads the delivery state of the latest invitation this client raised for each subject;
    ///     subjects invited by another client come back as <see cref="IdentityDeliveryState.NotFound"/>.</summary>
    Task<IReadOnlyList<IdentityDeliveryStatus>> GetInvitationDeliveryAsync(IReadOnlyList<string> subjects, CancellationToken cancellationToken);
}

/// <summary>One user to invite.</summary>
public sealed record IdentityInvitation(string Email, string? DisplayName, string? Locale, IdentityGender? Gender, string? ReturnUrl);

/// <summary>The identity server's answer for one invited user: a subject with a status, or an error.</summary>
public sealed record IdentityInvitationResult(string Email, string? Subject, IdentityInvitationStatus? Status, string? Error);

/// <summary>Why the identity server sent, or did not send, an invitation email.</summary>
public enum IdentityInvitationStatus
{
    /// <summary>A new identity user was created and a link was queued.</summary>
    Invited,
    /// <summary>An existing credential-less or orphaned user was re-linked and a link was queued.</summary>
    Reinvited,
    /// <summary>The user already holds a credential; no email was sent and the caller must notify them.</summary>
    Active,
}

/// <summary>Grammatical gender forwarded to the identity server's invitation templates.</summary>
public enum IdentityGender { Female, Male }

/// <summary>Delivery state of one subject's latest invitation raised by this client.</summary>
public sealed record IdentityDeliveryStatus(string Subject, IdentityDeliveryState State, bool ExpectsDeliveryEvents, DateTimeOffset? SentUtc, DateTimeOffset? UpdatedUtc, string? Reason);

/// <summary>The identity server's delivery states, in its own order.</summary>
public enum IdentityDeliveryState { NotFound, Pending, Sent, Delivered, Bounced, Complained, Rejected, Abandoned, Accepted }

/// <summary>Configuration of <see cref="IIdentityServerClient"/>; validated at startup.</summary>
public sealed class IdentityServerClientOptions
{
    /// <summary>The issuer origin, e.g. <c>https://id.tellma.com</c>; also the token's <c>resource</c>.</summary>
    public required Uri Authority { get; set; }
    /// <summary>The distribution's confidential client id (<c>&lt;slug&gt;-svc</c>).</summary>
    public required string ClientId { get; set; }
    /// <summary>The client secret, supplied through a Key Vault reference in SaaS.</summary>
    public required string ClientSecret { get; set; }
    /// <summary>Path base of an in-process identity host; empty for a shared server.</summary>
    public string PathBase { get; set; } = "";
}
```

```csharp
namespace Tellma.Core.Abstractions.Users;

/// <summary>The tenant-known progress of a user from creation to first sign-in.</summary>
public enum UserState
{
    /// <summary>Created; the identity server has not accepted an invitation yet.</summary>
    New,
    /// <summary>The identity server assigned a subject; the user has not connected here yet.</summary>
    Invited,
    /// <summary>The subject has connected to this tenant at least once.</summary>
    Active,
}

/// <summary>A human user of one tenant. Distributions extend it by inheritance; the platform's user
///     service is generic over the leaf.</summary>
public class User : Entity<int>, IActivatable, IMultilingual, IAudited, ISystemVersioned
{
    /// <summary>The identity server's subject; assigned by invitation and never client-settable.</summary>
    [MaxLength(36), ServerOwned] public string? Subject { get; set; }
    /// <summary>The sign-in address; unique per tenant; locked once the user is invited.</summary>
    [Required, MaxLength(256), EmailAddress] public string Email { get; set; } = "";
    /// <summary>Invitation progress; server-owned.</summary>
    [ServerOwned] public UserState State { get; set; }
    /// <summary>When the last successful invite call ran; server-owned.</summary>
    [ServerOwned] public DateTime? InvitedAt { get; set; }
    /// <summary>The identity server's status from the last invite; server-owned.</summary>
    [ServerOwned] public IdentityInvitationStatus? InvitationOutcome { get; set; }
    /// <summary>The identity server's per-user error from the last invite; server-owned.</summary>
    [MaxLength(1024), ServerOwned] public string? InvitationError { get; set; }
    [Required, MaxLength(255), SelfEditable] public string Name { get; set; } = "";
    [MaxLength(255), SelfEditable] public string? Name2 { get; set; }
    [MaxLength(255), SelfEditable] public string? Name3 { get; set; }
    /// <summary>BCP 47 tag used for invitations and notifications; null means the tenant's primary language.</summary>
    [MaxLength(16), SelfEditable] public string? PreferredLanguage { get; set; }
    /// <summary>Calendar code used when rendering dates for this user out of band; null means the tenant's primary.</summary>
    [MaxLength(16), SelfEditable] public string? PreferredCalendar { get; set; }
    /// <summary>IANA time zone id; null means the tenant's time zone.</summary>
    [MaxLength(64), SelfEditable] public string? PreferredTimeZone { get; set; }
    /// <summary>Grammatical gender forwarded to invitation templates; optional.</summary>
    public IdentityGender? Gender { get; set; }
    /// <summary>Address for notifications; null means <see cref="Email"/>.</summary>
    [MaxLength(256), EmailAddress, SelfEditable] public string? ContactEmail { get; set; }
    /// <summary>E.164 mobile number for SMS notifications.</summary>
    [MaxLength(32), SelfEditable] public string? ContactMobile { get; set; }
    /// <summary>Blob id of the profile image, minted by the blob staging endpoint.</summary>
    [MaxLength(64), SelfEditable] public string? ImageId { get; set; }
    public bool IsActive { get; set; } = true;
    /// <summary>Role memberships, synchronised under this user on save. Not an EF navigation.</summary>
    [NotMapped, Child] public List<RoleMembership> RoleMemberships { get; set; } = [];
    /// <summary>Muted notification types per channel, synchronised under this user on save. Not an EF navigation.</summary>
    [NotMapped, Child, SelfEditable] public List<UserNotificationOptOut> NotificationOptOuts { get; set; } = [];
}

/// <summary>Membership of a user in a role. A weak entity with two owners: saved under the user and under the role.</summary>
public class RoleMembership : Entity<int>, ISystemVersioned
{
    public int UserId { get; set; }
    public int RoleId { get; set; }
    [MaxLength(1024)] public string? Notes { get; set; }
}

/// <summary>A named bundle of permissions. Public roles apply to every user and may have no members.</summary>
public class Role : Entity<int>, IActivatable, IMultilingual, IAudited, ISystemVersioned
{
    [Required, MaxLength(255)] public string Name { get; set; } = "";
    [MaxLength(255)] public string? Name2 { get; set; }
    [MaxLength(255)] public string? Name3 { get; set; }
    [MaxLength(50)] public string? Code { get; set; }
    /// <summary>When true the role's permissions apply to every signed-in user and memberships are rejected.</summary>
    public bool IsPublic { get; set; }
    public bool IsActive { get; set; } = true;
    [NotMapped, Child] public List<Permission> Permissions { get; set; } = [];
    [NotMapped, Child] public List<RoleMembership> Members { get; set; } = [];
}

/// <summary>One grant: a securable (resource and action) and an optional row-level filter.</summary>
public class Permission : Entity<int>, ISystemVersioned
{
    public int RoleId { get; set; }
    /// <summary>The securable's resource name, or <c>*</c>.</summary>
    [Required, MaxLength(128)] public string Resource { get; set; } = "";
    /// <summary>The securable's action name, or <c>*</c>.</summary>
    [Required, MaxLength(64)] public string Action { get; set; } = "";
    /// <summary>A Queryex predicate over the securable's filter root; null grants every row.</summary>
    [MaxLength(2048)] public string? Filter { get; set; }
    /// <summary>The Queryex language version the filter text was validated under; stamped by the role service.</summary>
    [ServerOwned] public int? FilterLanguageVersion { get; set; }
    [MaxLength(1024)] public string? Notes { get; set; }
}

/// <summary>A user-interface preference the server stores and never interprets.</summary>
public class UserPreference : Entity<int>
{
    public int UserId { get; set; }
    [Required, MaxLength(128)] public string Key { get; set; } = "";
    [Required] public string Value { get; set; } = "";
}

/// <summary>A muted notification type on one channel; a row exists only where the user opted out.</summary>
public class UserNotificationOptOut : Entity<int>
{
    public int UserId { get; set; }
    [Required, MaxLength(64)] public string NotificationType { get; set; } = "";
    public NotificationChannel Channel { get; set; }
}

/// <summary>A notification delivery channel.</summary>
public enum NotificationChannel { Email, Sms, Push }

/// <summary>Ids of rows the platform seeds in the reserved band and references by id.</summary>
public static class CoreWellKnownIds
{
    /// <summary>Highest id of the reserved band; every sequence starts above it.</summary>
    public const int ReservedBandEnd = 999;
    /// <summary>The actor for seeds, schedules and background work with no triggering user.</summary>
    public const int SystemUserId = 1;
    /// <summary>The seeded role holding the wildcard permission.</summary>
    public const int AdministratorRoleId = 1;
    /// <summary>The seeded public role, empty by default.</summary>
    public const int PublicRoleId = 2;
    /// <summary>The seeded wildcard permission row on the administrator role.</summary>
    public const int AdministratorPermissionId = 1;
}

/// <summary>Outcome of inviting one user.</summary>
public sealed record InviteResult(int Id, IdentityInvitationStatus? Outcome, string? Error);

/// <summary>Tenant state and live delivery state of one user's invitation.</summary>
public sealed record InvitationStatus(int Id, UserState State, IdentityInvitationStatus? Outcome, string? Error, IdentityDeliveryStatus? Delivery);

/// <summary>A key-value pair written by the preference and override endpoints.</summary>
public sealed record KeyValueItem(string Key, string Value);

/// <summary>The caller's preferences bag and its tag.</summary>
public sealed record PreferencesResult(IReadOnlyDictionary<string, string> Preferences, Guid PreferencesTag);

/// <summary>Result of a test notification.</summary>
public enum TestNotificationResult { Sent, NoContactAddress, NotConfigured }

/// <summary>Why a permission holds, for the explain endpoint.</summary>
public sealed record PermissionExplanation(bool Allowed, string? Filter, IReadOnlyList<PermissionGrant> Grants);
public sealed record PermissionGrant(int RoleId, string RoleName, bool IsPublic, string? Filter);

/// <summary>The operations the user service adds beyond the standard set; the leaf type is the distribution's.</summary>
public interface IUserService<TUser> : ICrudService<TUser, int>, IActivatableService<int> where TUser : User
{
    /// <summary>Invites the users by id through the identity server; requires <c>users:invite</c>.</summary>
    Task<IReadOnlyList<InviteResult>> InviteAsync(IReadOnlyList<int> ids, CancellationToken cancellationToken);
    /// <summary>Reads tenant and live delivery state of the users' invitations; requires <c>users:read</c>.</summary>
    Task<IReadOnlyList<InvitationStatus>> GetInvitationStatusAsync(IReadOnlyList<int> ids, CancellationToken cancellationToken);
    /// <summary>The caller's own details envelope plus preferences; always allowed.</summary>
    Task<(DetailsResult<TUser> Details, PreferencesResult Preferences)> GetMeAsync(CancellationToken cancellationToken);
    /// <summary>Saves the caller's self-editable columns and opt-outs through the standard pipeline.</summary>
    Task<DetailsResult<TUser>> SaveMeAsync(TUser me, SaveOptions options, CancellationToken cancellationToken);
    /// <summary>Upserts the caller's preferences and returns the new preferences tag.</summary>
    Task<Guid> SetMyPreferencesAsync(IReadOnlyList<KeyValueItem> items, CancellationToken cancellationToken);
    /// <summary>Deletes the caller's preferences by key and returns the new preferences tag.</summary>
    Task<Guid> DeleteMyPreferencesAsync(IReadOnlyList<string> keys, CancellationToken cancellationToken);
    /// <summary>Sends a test message to the caller's contact address on the given channel.</summary>
    Task<TestNotificationResult> SendTestNotificationAsync(NotificationChannel channel, CancellationToken cancellationToken);
    /// <summary>Answers "may this user do this, and why"; the caller must be the user or hold <c>users:read</c> on them.</summary>
    Task<PermissionExplanation> ExplainPermissionAsync(int userId, string resource, string action, CancellationToken cancellationToken);
}
```

```csharp
namespace Tellma.Core.Abstractions.Settings;

/// <summary>The typed tenant settings row as read by every client.</summary>
public sealed record GeneralSettings(string TenantName, string? TenantName2, string? TenantName3, string PrimaryLanguage, string? SecondaryLanguage, string? TernaryLanguage, string PrimaryCalendar, string? SecondaryCalendar, string TimeZone, DateTime ModifiedAt);

/// <summary>A partial update of <see cref="GeneralSettings"/>: only members named in <see cref="Fields"/> are applied,
///     so a null member named in the list clears the setting and an unnamed member is untouched.</summary>
public sealed class GeneralSettingsPatch
{
    [Required, MinLength(1)] public required IReadOnlyList<string> Fields { get; init; }
    public string? TenantName { get; init; }
    public string? TenantName2 { get; init; }
    public string? TenantName3 { get; init; }
    public string? PrimaryLanguage { get; init; }
    public string? SecondaryLanguage { get; init; }
    public string? TernaryLanguage { get; init; }
    public string? PrimaryCalendar { get; init; }
    public string? SecondaryCalendar { get; init; }
    public string? TimeZone { get; init; }
    /// <summary>The stamp the caller last read; a mismatch is a concurrency error. Optional; absence is logged.</summary>
    public DateTime? ExpectedModifiedAt { get; init; }
}

/// <summary>Everything a client needs to render a tenant, cached on <see cref="Tag"/>.</summary>
public sealed record SettingsEnvelope(GeneralSettings General, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Overrides, Guid Tag);

/// <summary>The settings operations; securables <c>settings.general:save</c> and <c>settings.&lt;category&gt;:save</c>; reads are public.</summary>
public interface ISettingsService
{
    Task<SettingsEnvelope> GetAsync(CancellationToken cancellationToken);
    Task<GeneralSettings> PatchGeneralAsync(GeneralSettingsPatch patch, CancellationToken cancellationToken);
    Task SetOverridesAsync(string category, IReadOnlyList<KeyValueItem> items, CancellationToken cancellationToken);
    Task DeleteOverridesAsync(string category, IReadOnlyList<string> keys, CancellationToken cancellationToken);
}

/// <summary>A registered override key: its category, JSON schema and default, declared once by a feature.</summary>
public sealed record SettingDefinition(string Category, string Key, string JsonSchema, string DefaultJson);
```

```csharp
namespace Tellma.Core.Abstractions.Seeding;

/// <summary>Reference data applied by the migrator after migrations, versioned and idempotent by natural key.</summary>
public interface ISeed
{
    /// <summary>Stable id recorded in the seed history, e.g. <c>Gl.SampleCenters</c>.</summary>
    string Id { get; }
    /// <summary>Re-applied when greater than the recorded version.</summary>
    int Version { get; }
    /// <summary>Applies the seed through the bulk pipeline as the system user in one transaction.</summary>
    Task ApplyAsync(ISeedContext context, CancellationToken cancellationToken);
}

/// <summary>What a seed may use: the tenant's service scope (whose request context is the system user) and the environment.</summary>
public interface ISeedContext
{
    IServiceProvider Services { get; }
    string EnvironmentName { get; }
}

/// <summary>The first administrator of a tenant, as the bootstrap seed needs it.</summary>
public sealed record TenantBootstrapAdmin(string Email, string Name, string? Subject, string? Language);

/// <summary>Where the bootstrap seed learns a tenant's first administrator. The reference distribution reads
///     configuration; a multi-live distribution reads its provisioning record.</summary>
public interface ITenantBootstrapSource
{
    /// <summary>Returns the administrator to bootstrap, or null when the tenant has none configured.</summary>
    Task<TenantBootstrapAdmin?> GetAdminAsync(int tenantId, CancellationToken cancellationToken);
}
```

### 3.2 Owned by this spec — `Tellma.Core`

```csharp
namespace Tellma.Core.Users;

/// <summary>The platform user service, generic over the distribution's user leaf.</summary>
public class UserService<TUser> : CrudService<TUser, int>, IUserService<TUser> where TUser : User
{
    public UserService(CrudServiceContext<TUser> context, IIdentityServerClient identity, IEmailSender email, IUserSessionTerminator sessions) { … }
    /// <summary>Adds the self criterion <c>Id = me()</c> for read and save.</summary>
    protected override FilterTree? BespokeCriteria(string action) { … }
    /// <summary>Email normalisation and write-once, state, self-editable, membership, opt-out and well-known-row rules.</summary>
    protected override ValueTask ValidateAsync(ValidationContext<TUser> context, CancellationToken cancellationToken) { … }
    /// <summary>Appends the cross-owner role stamp bump, the permissions-tag bump and the lockout assertion to every persist batch.</summary>
    protected override void OnPersisting(PersistBatch<TUser> batch) { … }
    /// <summary>Terminates sessions of deactivated or deleted users after commit.</summary>
    protected override Task OnCommittedAsync(CommittedChanges<TUser> changes, CancellationToken cancellationToken) { … }
}

/// <summary>The platform role service; validates permissions against the securables registry and the Queryex engine.</summary>
public class RoleService<TRole> : CrudService<TRole, int>, IActivatableService<int> where TRole : Role
{
    public RoleService(CrudServiceContext<TRole> context, ISecurablesRegistry securables, QueryexEngine engine, ITenantQueryexSchemaProvider schemas) { … }
    protected override ValueTask ValidateAsync(ValidationContext<TRole> context, CancellationToken cancellationToken) { … }
    protected override void OnPersisting(PersistBatch<TRole> batch) { … }
}

/// <summary>Row image for the invite write-back; a standalone table type.</summary>
[TableType(Name = "UserInvitationOutcomeList")]
public sealed class UserInvitationOutcome
{
    [Key] public int UserId { get; set; }
    [MaxLength(36)] public string? Subject { get; set; }
    [MaxLength(16)] public string? Outcome { get; set; }
    [MaxLength(1024)] public string? Error { get; set; }
}
```

```csharp
namespace Tellma.Module.Gl;

/// <summary>Kind of responsibility centre; only <see cref="Abstract"/> and <see cref="BusinessUnit"/> may have children.</summary>
public enum CenterType
{
    /// <summary>A grouping node that never receives postings.</summary>
    Abstract,
    /// <summary>A profit or investment centre grouping its operating centres.</summary>
    BusinessUnit,
    /// <summary>A service cost centre (IT, HR, maintenance).</summary>
    Service,
    /// <summary>A production or operating cost centre.</summary>
    Operation,
    /// <summary>A revenue centre.</summary>
    Sale,
}

/// <summary>A responsibility centre in a tree. The generic parameter types the parent navigation against the mapped leaf.</summary>
public abstract class Center<TCenter> : TreeEntity<TCenter, int>, IActivatable, IMultilingual, IAudited where TCenter : Center<TCenter>
{
    public CenterType CenterType { get; set; }
    [Required, MaxLength(255)] public string Name { get; set; } = "";
    [MaxLength(255)] public string? Name2 { get; set; }
    [MaxLength(255)] public string? Name3 { get; set; }
    [Required, MaxLength(50)] public string Code { get; set; } = "";
    public bool IsActive { get; set; } = true;
}

/// <summary>The default leaf for distributions that add no columns.</summary>
public class Center : Center<Center> { }

/// <summary>The centre service; adds the parent-type rule to the tree capability's validation.</summary>
public class CenterService<TCenter> : TreeCrudService<TCenter, int> where TCenter : Center<TCenter>
{
    protected override ValueTask ValidateAsync(ValidationContext<TCenter> context, CancellationToken cancellationToken) { … }
}

/// <summary>The GL feature: declares <see cref="Center"/>, its sample seed, and requires Core.</summary>
public sealed class GlFeature : ITellmaFeature { public void Declare(FeatureBuilder features) { … } }
```

### 3.3 Needed from other specs (the exact shapes consumed here)

- **Entity contract (0011):** `Entity<TKey>`, `TreeEntity<TSelf, TKey>` (`ParentId`, `Parent`, `Node` with `[ExcludeFromTableType]`, `SubtreeCount`, `ActiveSubtreeCount`), `IActivatable`, `IMultilingual`, `IAudited` (four columns), `ISystemVersioned` (system-versioning marker), `[ServerOwned]`, `[SelfEditable]` (a column-set marker services may query), `[Child]` on `[NotMapped]` collections, enum-as-string mapping with a generated CHECK constraint, natural-key declaration, the emitter option for an insert expression on an excluded column (the placeholder node), the synchronize step skipping unchanged rows and exposing its inserted/deleted keys to later statements in the batch.
- **Batch (0011):** `BatchBuilder.Query(CompiledQuery)`, `.Save(SaveSpec)`, `.Update<TTableType>(sql, rows, writes: [tables])`, `.Sql(sql, parameters, writes:)`, `.Assert(sql, errorCode → exceptionFactory)` for the lockout invariant and the tree depth cap, `MayRetry` per statement, automatic tag bumps from the declared write set.
- **Tags (0012):** tenant tags `Settings` and `Security` in a non-temporal tenant table; per-user `PermissionsTag` and `PreferencesTag` in `core.UserActivity`; Guid values; a `ITenantTagService.BumpAllAsync` for the operator runbook.
- **Permissions (0013):** `ISecurablesRegistry.TryGet(resource, action, out Securable { SupportsFilter, EntityDescriptor? FilterRoot })` plus registration of custom actions; the evaluator's `ExplainAsync(userId, resource, action)`; the connect step's `Invited → Active` write and its refusal of subject-less and inactive rows; `core.UserActivity`.
- **Pipeline (0014):** `CrudService<T, TKey>` hooks `BespokeCriteria`, `ValidateAsync`, `OnPersisting`, `OnCommittedAsync`; `TreeCrudService<T, TKey>`; `IActivatableService<TKey>`; `ConcurrencyException`, `ValidationException` with property paths, `PartialFailureException`; hydration of `[ServerOwned]` columns from the DB image before validation; the FK-violation (547) and unique-violation (2601/2627) mapping to field errors.
- **Web (0015):** projection of the custom operations to `POST /{tenantId}/api/web/users/{invite|invitation-status|me|me/save|me/preferences/set|me/preferences/delete|me/test-notification|explain-permission}`, `/settings`, `/settings/general/save`, `/settings/{category}/entries/{set|delete}`; `PartialFailureException` → 502 with body.
- **Blobs (0016):** `ImageId` as a staging token confirmed by the save and released by delete; the `users/{id}/image` etag GET.
- **Host (0010):** `IUserSessionTerminator.TerminateAsync(tenantId, subject)`; `IRequestContext { TenantId, UserId, Subject, Language, Calendar, TimeZone, IsSystem }`; the distribution origin for `ReturnUrl`; `ITellmaFeature`/`FeatureBuilder` with `Requires<T>()`, `Entity<T>()`, `Seed<T>()`, `Setting(SettingDefinition)`; the Migrator's `seed` step calling `SeedRunner`; the provisioning record behind `ITenantBootstrapSource` for multi-live distributions.
- **Inbox (0019):** the notification type registry and the non-mutable set, read against `UserNotificationOptOuts`.

## 4. Schema

Conventions: plural table names, singular classes; every table has `sq_<Table>` (`START WITH 1000`); `datetime2(7)` for audit stamps, `datetime2(3)` elsewhere; `PK_`, `FK_<Table>__<Column>`, `UX_`/`IX_<Table>__<Columns>`, `CK_` names; history tables `<Table>_History` in the same schema. Tables owned by spec 0013 or 0012 are shown as this spec consumes them.

```
core.Users  (system-versioned → core.Users_History; UDTT UsersList)
Id                 int             NOT NULL  PK clustered
Subject            nvarchar(36)    NULL      UX_Users__Subject WHERE Subject IS NOT NULL
Email              nvarchar(256)   NOT NULL  UX_Users__Email
State              nvarchar(16)    NOT NULL  CK IN ('New','Invited','Active')
InvitedAt          datetime2(3)    NULL
InvitationOutcome  nvarchar(16)    NULL      CK IN ('Invited','Reinvited','Active')
InvitationError    nvarchar(1024)  NULL
Name               nvarchar(255)   NOT NULL
Name2              nvarchar(255)   NULL
Name3              nvarchar(255)   NULL
PreferredLanguage  nvarchar(16)    NULL
PreferredCalendar  nvarchar(16)    NULL
PreferredTimeZone  nvarchar(64)    NULL
Gender             nvarchar(8)     NULL      CK IN ('Female','Male')
ContactEmail       nvarchar(256)   NULL
ContactMobile      nvarchar(32)    NULL
ImageId            nvarchar(64)    NULL
IsActive           bit             NOT NULL  DEFAULT 1
CreatedAt          datetime2(7)    NOT NULL
CreatedById        int             NOT NULL  FK core.Users(Id)
ModifiedAt         datetime2(7)    NOT NULL  (concurrency token)
ModifiedById       int             NOT NULL  FK core.Users(Id)
ValidFrom/ValidTo  datetime2(7)    period, shadow, GENERATED ALWAYS
```

```
core.UserActivity  (spec 0013; non-temporal; no UDTT; written only by targeted statements)
UserId             int              NOT NULL  PK, FK core.Users(Id) ON DELETE CASCADE
LastActiveAt       datetime2(3)     NULL
PermissionsTag     uniqueidentifier NOT NULL  DEFAULT NEWID()
PreferencesTag     uniqueidentifier NOT NULL  DEFAULT NEWID()
(inbox counters as spec 0019 defines)
```

```
core.UserPreferences  (non-temporal; UDTT UserPreferencesList)
Id                 int             NOT NULL  PK
UserId             int             NOT NULL  FK core.Users(Id) ON DELETE CASCADE
Key                nvarchar(128)   NOT NULL
Value              nvarchar(max)   NOT NULL
UX_UserPreferences__UserId_Key (UserId, Key)
```

```
core.UserNotificationOptOuts  (non-temporal; UDTT UserNotificationOptOutsList)
Id                 int             NOT NULL  PK
UserId             int             NOT NULL  FK core.Users(Id) ON DELETE CASCADE
NotificationType   nvarchar(64)    NOT NULL
Channel            nvarchar(16)    NOT NULL  CK IN ('Email','Sms','Push')
UX_UserNotificationOptOuts__UserId_Type_Channel (UserId, NotificationType, Channel)
```

```
core.Roles  (system-versioned → core.Roles_History; UDTT RolesList)
Id                 int             NOT NULL  PK
Name               nvarchar(255)   NOT NULL  UX_Roles__Name
Name2              nvarchar(255)   NULL      UX_Roles__Name2 WHERE Name2 IS NOT NULL
Name3              nvarchar(255)   NULL      UX_Roles__Name3 WHERE Name3 IS NOT NULL
Code               nvarchar(50)    NULL      UX_Roles__Code WHERE Code IS NOT NULL
IsPublic           bit             NOT NULL  DEFAULT 0
IsActive           bit             NOT NULL  DEFAULT 1
CreatedAt, CreatedById, ModifiedAt, ModifiedById   as core.Users
ValidFrom/ValidTo  period, shadow
```

```
core.RoleMemberships  (system-versioned → core.RoleMemberships_History; UDTT RoleMembershipsList)
Id                 int             NOT NULL  PK
UserId             int             NOT NULL  FK core.Users(Id)
RoleId             int             NOT NULL  FK core.Roles(Id)
Notes              nvarchar(1024)  NULL
UX_RoleMemberships__UserId_RoleId (UserId, RoleId);  IX_RoleMemberships__RoleId (RoleId)
ValidFrom/ValidTo  period, shadow
```

```
core.Permissions  (system-versioned → core.Permissions_History; UDTT PermissionsList)
Id                     int            NOT NULL  PK
RoleId                 int            NOT NULL  FK core.Roles(Id);  IX_Permissions__RoleId
Resource               nvarchar(128)  NOT NULL
Action                 nvarchar(64)   NOT NULL
Filter                 nvarchar(2048) NULL
FilterLanguageVersion  int            NULL      CK (Filter IS NULL OR FilterLanguageVersion IS NOT NULL)
Notes                  nvarchar(1024) NULL
ValidFrom/ValidTo      period, shadow
```

```
core.Settings  (spec 0012; single row Id = 1; UDTT SettingsList)
Id                 int             NOT NULL  PK, CK (Id = 1)
TenantName         nvarchar(255)   NOT NULL
TenantName2        nvarchar(255)   NULL
TenantName3        nvarchar(255)   NULL
PrimaryLanguage    nvarchar(16)    NOT NULL
SecondaryLanguage  nvarchar(16)    NULL
TernaryLanguage    nvarchar(16)    NULL
PrimaryCalendar    nvarchar(16)    NOT NULL
SecondaryCalendar  nvarchar(16)    NULL
TimeZone           nvarchar(64)    NOT NULL
CreatedAt, CreatedById, ModifiedAt, ModifiedById   as core.Users  (ModifiedAt is the patch's stamp)
```

```
core.SettingOverrides  (spec 0012; UDTT SettingOverridesList)
Id                 int             NOT NULL  PK
Category           nvarchar(64)    NOT NULL
Key                nvarchar(128)   NOT NULL
Value              nvarchar(max)   NOT NULL  (JSON text)
ModifiedAt, ModifiedById
UX_SettingOverrides__Category_Key (Category, Key)
```

```
gl.Centers  (non-temporal; UDTT CentersList excludes Node and Level)
Id                 int             NOT NULL  PK clustered
ParentId           int             NULL      FK gl.Centers(Id);  IX_Centers__ParentId
CenterType         nvarchar(32)    NOT NULL  CK IN ('Abstract','BusinessUnit','Service','Operation','Sale')
Name               nvarchar(255)   NOT NULL
Name2              nvarchar(255)   NULL
Name3              nvarchar(255)   NULL
Code               nvarchar(50)    NOT NULL  UX_Centers__Code
IsActive           bit             NOT NULL  DEFAULT 1
Node               hierarchyid     NOT NULL  UX_Centers__Node (depth-first)
Level              AS [Node].GetLevel()      (computed, not persisted)
SubtreeCount       int             NOT NULL  DEFAULT 1
ActiveSubtreeCount int             NOT NULL  DEFAULT 1
CreatedAt, CreatedById, ModifiedAt, ModifiedById   as core.Users (FK core.Users)
```

```
dbo.__SeedHistory
SeedId             nvarchar(150)   NOT NULL  PK
Version            int             NOT NULL
AppliedAt          datetime2(3)    NOT NULL
```

Sequences: `core.sq_Users`, `core.sq_UserPreferences`, `core.sq_UserNotificationOptOuts`, `core.sq_Roles`, `core.sq_RoleMemberships`, `core.sq_Permissions`, `core.sq_SettingOverrides`, `gl.sq_Centers` — all `AS int START WITH 1000 INCREMENT BY 1 CACHE 50`. Standalone table types: `UserInvitationOutcomeList` plus the four canonical lists.

## 5. Answers to the brain dump's open questions in this theme

| Question (abridged) | Answer |
|---|---|
| Record + blobs: staging or multipart? | Staging with a token (`ImageId`) confirmed by the save; spec 0016 owns the sweep (D5, seam 12). |
| JSON for user preferences? Pinned screens in their own table? | Key-value rows in `core.UserPreferences`; server never reads them; pinned screens stay a key; admin-curated navigation is a future tenant setting (D6). |
| Notification settings shape? | Typed contact columns on `Users` plus an opt-out list `core.UserNotificationOptOuts (type, channel)`; push deferred (D6). |
| Separate `SettingsVersion` and `PermissionsVersion`? | Separate: `PermissionsTag` and `PreferencesTag` per user in `core.UserActivity`, plus tenant `Security` and `Settings` tags (D7, seam 5). |
| Version, ETag or fingerprint? | "Tag" (`*Tag` columns, Guid), with the hardcoded metaversion in the cache key (seam 5). |
| Inbox tracking shape? | Spec 0019; columns live in `core.UserActivity`. |
| Are the user states exhaustive? | No; replaced by `New/Invited/Active` plus last-invite outcome and error, with delivery read live (D2). |
| Two UDTTs for write-once columns? | One UDTT; `Subject` is server-owned, `Email` is locked by validation after invitation (D5). |
| Public permissions: system role, `IsPublic`, or a table? | `IsPublic` on `Role`; a `Public` role is seeded as a convenience (D7, D15). |
| `SavedById` on weak entities? | No; the owner's stamp is bumped on any child change (D4). |
| hierarchyid maintenance: memory or SQL? | SQL appended to the batch with a placeholder node on insert; cycles validated in C# (D13). |
| How to model `CenterType`? | Enum stored as string with a CHECK constraint; five values; parent-type rule (D12). |
| Resource encoding convention? | `resource:action`, kebab plural for entities, dotted for settings, `*` wildcard (D10). |
| Custom endpoints on `UserService`? | Invite, invitation status, me, me/save, preferences set/delete, test notification, explain permission (D3, D8). |
| Settings: one edit API or per category? Resource per category? | `general` typed patch plus per-category override set/delete; securable `settings.<category>:save` (D9). |
| Patch or load-and-save? | Field-mask partial DTO; one round trip (D9). |
| Which layer names? | Data, Service, Web are fine as folders; spec 0010 decides. |
| Migrations and the first admin | Migrator: migrate, versioned seeds, `BootstrapAdminSeed` matching `admin@localhost` in Development (D16). |

## 6. Seams

1. **Batch abstraction.** Needed statement kinds: compiled query, save, targeted `UPDATE … FROM @tvp`, raw SQL with a declared write set, and an assertion statement whose mapped error becomes a typed exception (lockout invariant, tree depth). Tag bumps derive from the declared write set; the synchronize step exposes its inserted/deleted keys so a later statement can bump the other owner's stamp.
2. **Entity vs wire shape.** Single entity class with `[NotMapped, Child]` collections and `[ServerOwned]`/`[SelfEditable]` markers; server-owned values are hydrated from the DB image before validation; write-once columns are validation rules.
3. **One capability, declared once.** Tree × Activatable composes: activate/deactivate on a tree entity appends the count recompute; a capability must be able to contribute persist-batch statements; a service must be able to register a custom action (`invite`) on its entity's securable.
4. **Queryex schema per tenant.** The role service validates filters against the current tenant schema (languages gate `Name2/Name3`); a settings patch that changes languages rebuilds it. This spec needs `Parent`, `CreatedBy` and `ModifiedBy` paths to exist on `Center`, `User` and `Role`; whether the adapter derives them from EF navigations or from FK metadata decides whether the `Center<TCenter>` generic is needed (conflict 3).
5. **Version tags.** Tenant `Settings` and `Security` tags in a non-temporal tenant table; `PermissionsTag`/`PreferencesTag` per user in `core.UserActivity`; Guid values; connect reads all four.
6. **Feature composition.** `GlFeature` and `CoreFeature` use one `ITellmaFeature.Declare(FeatureBuilder)` shape with `Requires<T>()`, entity declarations, seeds, setting definitions and securables.
7. **Natural keys.** `Users.Email`, `Roles.Code` (fallback `Name`), `Centers.Code`, declared on the builder.
8. **Background-task columns.** Not consumed here; the system user (id 1) is the actor built-in schedules run as.
9. **Request context.** `IsSystem`, `UserId`, `Subject`, `Language`, `Calendar`, `TimeZone`; seeds run in a scope whose context is the system user.
10. **Platform exceptions.** `ValidationException` (422 with paths), `ConcurrencyException` (409), `ForbiddenException` (403, or 404 shape where the row must look absent), `PartialFailureException` (502 with partial results), `NotConfiguredException`.
11. **Permission evaluation.** `(resource, action) → allowed + FilterTree + grants`; bespoke criteria hook returning a `FilterTree` (`Id = me()`); explain API; the evaluator compiles each stored filter under its own `FilterLanguageVersion`.
12. **Blob staging tokens.** `ImageId` is the token; save confirms; delete releases.
13. **Wire shapes.** Details envelope reused by `users/me` (plus `preferences` and `preferencesTag`); invite and status results are arrays in request order.
14. **Telemetry.** Meter `Tellma.Core`: `tellma.users.invitations` (counter, tag `outcome`), `tellma.users.invitation_status_calls` (counter), `tellma.identity.client.requests` (histogram, tags `endpoint`, `status`), `tellma.settings.patches` (counter, tag `category`), `tellma.seeds.applied` (counter, tag `seed`); meter `Tellma.Module.Gl` reuses the data-access spec's `tellma.tree.recompute.duration` (histogram, tag `table`). Constants live in the package's `.Abstractions`; no per-tenant tags.
15. **Notification enqueue.** The "you were added" notice for `Active` outcomes is a post-commit email now and moves to the outbox when it ships.
16. **Connect-call collapse.** Supported: every operation here tolerates a re-run after a permissions-tag mismatch because the identity call is issued only after the load-and-validate round trip succeeded under fresh permissions.
17. **Vocabulary.** Plural tables, singular classes; four audit columns; `UserPreferences`; `UserNotificationOptOuts`; `SettingOverrides`; `CenterType`; `*Tag`; `int` ids.

## 7. Departures from ARCHITECTURE.md

- A self-referencing tree entity is extended by closing its generic base, not by inheriting the default leaf (D11); the "extend by inheriting the default" rule holds only for entities whose navigations do not point at themselves (conditional on conflict 3).
- `taxonomy.json` is created (it does not exist) with the module and compliance registries, and the GL segment is `Gl`, not `GL` (D1).
- The reserved id band is concretised as `1..999` with every sequence starting at 1000 (D15).
- The migrator gains a bootstrap seed that may call the identity server and therefore holds the distribution's client credentials (D16); ARCHITECTURE.md describes the migrator as migrate-and-seed only.
- The reference distribution lives in the platform repo, which the package-naming table says a `Tellma.Distro.*` project never does; spec 0010 owns the location, and this spec's tests require it in-repo.
- Package pins: EF Core family to 10.0.11 and `Microsoft.Data.SqlClient` to at least 6.1.6 (D14).

## 8. Verification

Verified by this design on LocalDB (2026-09-02): a recursive CTE whose recursive member references a prior non-recursive CTE containing `ROW_NUMBER()` is accepted and produces the expected paths (`/1/`, `/1/1/`, `/1/1/1/`, `/1/2/`, `/2/`); `hierarchyid::Parse('/0/1000/')` is accepted and sorts below `/1/`; the `IsDescendantOf` self-join yields the expected `SubtreeCount`/`ActiveSubtreeCount`. Verified against the repo: `QueryexMode` is a record struct whose predicate-row mode is `QueryexMode.Filter` (there is no `QueryexMode.Predicate`); `ValidationOptions` has exactly `Schema`, `Root`, `Mode`, `LanguageVersion`, `Directions`, `Parameters`, `HasUser`, `HasGroupingKeys`, `Limits`; `QueryexLanguage.Version`/`Minimum`/`IsSupported` exist; `Microsoft.Data.SqlClient` is pinned at 6.1.1 and no HierarchyId package is pinned; `taxonomy.json` and `src/module/` do not exist; `ISandboxContext`/`SandboxContext.Never` exist under `Tellma.Core.Abstractions.Tenancy`.

Relied on from the research file (verified 2026-09-01): the invite and delivery-status request and response shapes, statuses, error strings, chunk limit, scope and token recipe; the `Active` outcome sending no email and reporting `NotFound` afterwards; the dev admin's fixed subject; `hierarchyid` TVP binding requiring `SqlHierarchyId`; the HierarchyId package's SqlClient floor; JSON Patch availability and caveats; the absence of a maintained merge-patch package; the API style guides' preference for field masks; the legacy `Centers` schema and the responsibility-centre taxonomy; SignalR `Clients.User` semantics. From the briefing digest: MERGE exclusion, RCSI default by platform, unique-index errors 2601/2627 as the uniqueness guarantee, temporal churn on no-op updates, JSON as `nvarchar(max)`.

Unverified, with the fallback if false: (1) the relative cost of the `IsDescendantOf` self-join versus a recursive `ParentId` CTE for counts at scale — measure in 0011's fixture tests, switch if the CTE wins; (2) whether an in-process identity host serves `api/identity/...` under `PathBase` — the client option exists either way; (3) whether spec 0001's derivation tolerates a `hierarchyid` store type on an excluded column — if not, the exclusion attribute must short-circuit before type mapping; (4) `JsonPatchDocument<T>` with source generation — moot under D9.

## 9. Review flags

- **Weak rows without audit columns (D4).** `Permissions` is a security table; an auditor may prefer `ModifiedById` directly on the row instead of joining `Roles_History` by period. Alternative: `ModifiedById` on `Permissions` and `RoleMemberships` only.
- **`Gender` on `Users` (D6).** Alternative: drop the column and send no gender to the identity server (neutral grammar in inflecting languages).
- **Dual ownership of memberships with cross-owner stamp bumps (D7).** Alternative: single ownership under `User`; the role page lists members read-only and "add members" is a bulk user save. Simpler emitter, weaker admin UX.
- **Re-stamping only changed filter text (D7).** Alternative: always re-stamp with the current version on any role save, accepting that a meaning-altering engine change re-affirms untouched filters; simpler, less safe.
- **`Invited → Active` inside the connect step (D2).** Alternative: a nightly job flips the state lazily; removes the once-per-lifetime temporal write from the request path at the cost of a stale admin page.
- **Lockout invariant requires a remaining admin in `State = 'Active'` (D8).** Alternative: accept `Invited` admins with a subject; fewer refusals, real lockout risk when the invitation never lands.
- **`ExpectedModifiedAt` optional on the settings patch (D9).** Alternative: mandatory (strict concurrency; an MCP agent must read before writing).
- **Ship the legacy SG&A types now (D12).** `Administration`, `Marketing`, `FinanceCost`, `OtherPL` if the first GL posting rules need them; otherwise additive later.
- **Negative ids for the reserved band (D15).** Never collide with imports that reset a sequence low; less readable in URLs.
- **`users:delete` restricted to never-connected users (D5).** Alternative: allow deleting any user and map FK failures to a validation error per referencing table; more permissive, less predictable.
- **The migrator invites the deployed tenant's first admin (D16).** Alternative: provisioning (spec 0010) invites and the migrator only seeds the row; keeps client credentials out of the migrator at the cost of a second bootstrap path.

## 10. Conflicts

1. **Entity classes (0013).** This spec needs `User`, `Role`, `RoleMembership`, `Permission`, `UserPreference` and `UserNotificationOptOut` with the exact columns in section 4, `User` and `Role` non-abstract and unsealed, the `[ServerOwned]`/`[SelfEditable]`/`[Child]` markers, the opt-out list instead of per-type channel bits, and the connect step's `Invited → Active` write that does not bump `ModifiedAt`.
2. **Settings row temporality and stamp (0012).** The brain dump makes `core.Settings` temporal; this spec needs `ModifiedAt` on the row as the patch's concurrency stamp and the `Settings` tag bumped by the executor from the write set; whether the row is system-versioned is 0012's call, and its tags must be in a non-temporal table.
3. **Queryex navigations (0011).** If the schema adapter derives many-to-one navigations from FK metadata rather than EF navigations, `Center` needs no generic parameter and the D11 departure disappears; this spec needs `Parent`, `CreatedBy` and `ModifiedBy` paths either way.
4. **Emitter requirements (0011).** An insert expression for an excluded column (the placeholder node), unchanged-row skipping in synchronize, the synchronize delta visible to later statements, and an assert statement kind; the `IdList`-scoped recompute variant.
5. **Securables registry (0013).** Custom actions registered by a service (`users:invite`), the `resource:action` naming, wildcard rules for filters, and the evaluator compiling each filter under its stored `FilterLanguageVersion` (refusing, fail-closed, a stamp below `QueryexLanguage.Minimum`).
6. **Host seams (0010).** `IUserSessionTerminator`, `IRequestContext.IsSystem`, the distribution origin for `ReturnUrl`, `ITenantBootstrapSource` fed by configuration in single-live and by the provisioning record in multi-live distributions, and the identity client credentials available to both the web host and the migrator.
7. **Pipeline hooks (0014).** `BespokeCriteria`, `OnPersisting` with access to the persist batch, `OnCommittedAsync`, server-owned hydration, and the 547/2601/2627 mapping; the connect-call collapse re-run must not re-issue the identity call (this spec orders it after the validated load).
8. **Inbox (0019).** The notification type registry, the non-mutable set, and the dispatcher's `NOT EXISTS` against `core.UserNotificationOptOuts`; the "you were added" email moves to the outbox when it ships.
9. **Web (0015).** The `PartialFailureException` → 502-with-body mapping, the `users/me` envelope extension, and public (unauthenticated-permission) reads of `settings`.
