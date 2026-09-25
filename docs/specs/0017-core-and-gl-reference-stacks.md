# Spec: Core and GL Reference Stacks

- **Author:** Ahmad Akra
- **Date:** 4 September 2026

**Status:** Ready for implementation. Frozen once merged: revised only if implementation forces a
design change, then kept as the historical record of what shipped — never updated thereafter as the
code or its dependencies evolve.

## Context

This spec ships the first end-to-end product on the CRUD stack: the two Core services that every
tenant runs (`UserService<TUser>` and `RoleService<TRole>`), the `AccessService` "can I, and why"
endpoint, the distribution's client of the identity server, the first module package pair
(`Tellma.Module.Gl.Abstractions` and `Tellma.Module.Gl`) with `Center` as the platform's first tree
entity, the GL sample-data provisioning step, the reference distribution's tenant migrations, and
the local-development administrator bootstrap. Everything here is a consumer of machinery other
specs define: the entity contract and batch of spec 0011, the settings and version tags of
spec 0012, the users, roles, permissions, securables and connect prologue of spec 0013, the service
pipeline of spec 0014, the web projection of spec 0015, the blob staging of spec 0016, the
provisioning steps of spec 0010 and the notification enqueue of spec 0020.

The users stack is where the platform first talks to another system on a caller's behalf. The
identity server of spec 0003 owns identities, credentials and invitation email delivery; a tenant
owns only the rows it wrote: a user's tenant-visible state (`New | Invited | Joined`), the outcome
and error of the last invitation the tenant raised, and the subject the identity server assigned.
Invitation is therefore an action with a remote call between two database round trips, never a
call inside a transaction, and delivery is a live drill-down rather than stored state.

The roles stack is where stored Queryex predicates enter the security model. The validation of a
permission's filter text and its language-version stamp are spec 0013 §3.7's
`RoleAccessRules<TRole>`, a Core component that runs on every role save whatever leaf a
distribution substitutes; `RoleService<TRole>` is the stack's service and carries no filter logic.

The GL module is the proof that a module composes: it references only Abstractions packages, ships
a non-abstract unsealed `Center` a distribution extends by plain inheritance, contributes its
entity, service, securables and provisioning step through one feature, and needs no web, MCP or
permission code of its own. Tree maintenance (id-path `hierarchyid` nodes, subtree counts, cycle
fencing) is spec 0011's emitted SQL; this spec adds only the GL business rule that a center may
have children only when its type is a grouping type.

Deliberately left to other specs: the settings edit API (`settings/save` and its patch record are
spec 0012's `SettingsService`), the inbox and notification preferences (spec 0020), Excel export
and import of users, roles and centers (spec 0018's operations project onto these stacks
unchanged), the outbox for the welcome email (a later spec; until then the send is a
post-commit call), and the second GL entity.

## Goals / Non-goals

**Goals**

- Ship `IIdentityServerClient` and its runtime implementation: bulk invite (with the sandbox-only
  `ExistingOnly` flag) and bulk delivery status, chunked at 1,000, with a cached service token.
- Ship `UserService<TUser>`: the `invite` action, `invitation-status`, `me`, `me/save`,
  `me/preferences/set`, `me/preferences/delete`, `preferences/get`, `preferences/set`,
  `preferences/delete`, `me/test-notification`, the user-specific validation and preprocessing
  rules, the post-commit side effects (session revocation, catalog membership hint, the
  `core.user.added` notice and the welcome email).
- Ship `RoleService<TRole>`: the standard projection over `Role` and the `members` details extra.
- Ship `AccessService` (`access/check`).
- Ship `Tellma.Module.Gl.Abstractions` (`Center`, `CenterType`, `GlModule`, `GlResources`,
  `GlValidationCodes`) and `Tellma.Module.Gl` (`CenterService<TCenter>`, `GlFeature`,
  `GlSampleCentersStep`), the `gl.Centers` table, and the `Gl` entry in `taxonomy.json`.
- Ship the reference distribution's tenant migration set and its Development bootstrap: the
  `admin@localhost` administrator that the in-proc identity server also seeds, and the deployed
  flow in which the migrator invites a tenant's first administrator.
- Pin every budget: two database round trips per invite, one per self-service write, one for `me`.

**Non-goals (explicitly out of scope)**

- The `User`, `Role`, `RoleMembership`, `Permission` entities, their tables, `HasData` rows,
  `UserAccessRules`, `RoleAccessRules`, the lockout guards, the securables registry, the connect
  prologue and `ITenantBootstrapper` — spec 0013.
- `core.Settings`, `SettingKey<T>`, `SettingsService` and every settings endpoint — spec 0012.
- The batch, emitter, id allocator, tree statements, entity metadata, Queryex adapter — spec 0011.
- `EntityService`, the pipeline, `StackDescriptor` — spec 0014.
- Endpoint projection, wire records, problem details, the `MeResult` shape — spec 0015.
- `core.Blobs`, the `user-image` and `user-signature` kinds, staging and the download endpoint —
  spec 0016.
- `core.Notifications`, `core.NotificationPreferences`, `INotifier`, `TellmaHub` — spec 0020.
- Provisioning steps as a mechanism, the migrator commands, the catalog, `taxonomy.json` itself —
  spec 0010.
- Push subscriptions, an SMS transport, the email outbox.

## 1. Placement and architecture

### 1.1 Projects and packages

| Piece | Location | References | Notes |
|---|---|---|---|
| Identity client contract, user-stack result records | `src/core/Tellma.Core.Abstractions/` (namespaces `Tellma.Core.Abstractions.Identity`, `.Access`) | `Tellma.Core.Queryex` only | Grown by this spec; no new package. |
| Identity client, user and role services, access service | `src/core/Tellma.Core/` (namespaces `Tellma.Core.Identity`, `Tellma.Core.Users`, `Tellma.Core.Access`) | as spec 0011's `Tellma.Core` | `IdentityServerClient` uses `IHttpClientFactory`; nothing else new is referenced. |
| GL contracts | `src/module/gl/Tellma.Module.Gl.Abstractions/` (namespace `Tellma.Module.Gl`) | `Tellma.Core.Abstractions` | `Center`, `CenterType`, `GlModule`, `GlResources`, `GlValidationCodes`. Creates `src/module/`. |
| GL runtime | `src/module/gl/Tellma.Module.Gl/` (namespace `Tellma.Module.Gl`) | `Tellma.Module.Gl.Abstractions` | `CenterService<TCenter>`, `GlFeature`, `GlSampleCentersStep`, `Resources/Strings.resx`. Never references `Tellma.Core`. |
| Reference distribution | `distributions/acme/src/Tellma.Distro.Acme.Web/`, `.Migrator/` | gain `Tellma.Module.Gl`, `Tellma.Module.Gl.Abstractions` | Migrations live in the Migrator project (§7). |
| Tests | `test/core/Tellma.Core.Tests`, `test/core/Tellma.Core.IntegrationTests`, `test/module/gl/Tellma.Module.Gl.Tests`, `distributions/acme/test/Tellma.Distro.Acme.IntegrationTests` | | §9. |

`Tellma.Module.Gl` compiles against `EntityService<TEntity, TKey>` in Abstractions and receives the
pipeline factory through its constructor; the module never names a `Tellma.Core` type. A unit test
in `Tellma.Module.Gl.Tests` asserts the project's reference closure contains no `Tellma.Core`
package.

Contract blocks are C# sketches: names and shapes are normative; `using` directives, XML
documentation, cancellation-token parameters, method bodies and accessibility details are omitted,
so a block is never pasted into code. SQL statements are the exact shape to emit.

### 1.2 Composition

`CoreFeature` (spec 0010) registers `contribution.Entity<User, UserService<User>>()`,
`contribution.Entity<Role, RoleService<Role>>()`, `contribution.ApiService<AccessService>()`,
`contribution.Singleton<IIdentityServerClient, IdentityServerClient>()`, and binds
`IdentityServerClientOptions` from `Tellma:Identity`; the notification type `core.user.added` is
spec 0020's own `CoreFeature` contribution, consumed here (§3.3). `GlFeature`
(§5.5) is added by a distribution with `tellma.AddFeature<GlFeature>()`. A distribution substitutes
its leaves with `tellma.UseEntity<User, MyUser>()`, `tellma.UseEntity<Role, MyRole>()`,
`tellma.UseEntity<Center, MyCenter>()`; the platform closes `UserService<MyUser>`,
`RoleService<MyRole>`, `CenterService<MyCenter>`.

Illustration (the reference distribution's shared `AcmeComposition.Compose` after this spec, invoked
by both hosts as spec 0010 §1.3 shows):

```csharp
public static void Compose(TellmaBuilder tellma) => tellma.UseAzureDefaults().AddFeature<GlFeature>().Languages(["en", "ar"]);
```

### 1.3 Seams consumed

| Seam | Owner | Members used here |
|---|---|---|
| `EntityService<TEntity, TKey>`, hooks, `[EntityAction]`, `[ApiAction]`, `[ApiRoute]`, `SaveOptions`, `ActionContext`, `SaveContext`, `PersistContext`, `PersistOutcome`, `IContextLoader`, `ValidationErrors` | spec 0014 | §3, §4, §5 |
| `IDataBatch.Sql`, `Tvp`, `OnCommitted`, `SqlOptions(Writes, UserIds, ForCaller)`, `BatchOutcome` | spec 0011's batch contract | §3.3, §3.5 |
| `IGuardedBatchRunner.Run`, `IUserConnector`, `ConnectedUser` | spec 0013's connect contract | §3.4, §3.5, §3.7 |
| `IAccessEvaluator`, `AccessDecision`, `AccessGrant`, `AccessGrantSource`, `SecurableRef`, `ISecurableRegistry`, `UserAccessRules<TUser>`, `RoleAccessRules<TRole>`, `IAdministratorDirectory`, `ITenantBootstrapper`, `WellKnownIds`, `CoreResources`, `AccessActions` | spec 0013 | §3, §4, §6 |
| `User`, `Role`, `RoleMembership`, `Permission`, `UserState`, `InviteStatus`, `UserKind`, `Gender`, `UserProfile` | spec 0013's entities | §3 |
| `MeResult`, `AccessCheckRequest`, `IdsRequest`, `SaveRequest<T>`, `EntitiesResult<T>`, `PartialFailureException` | spec 0015's wire records and exception set | §3, §3.7 |
| `[BlobReference]` on `User.ImageId` and `User.SignatureId`, kinds `user-image` and `user-signature` | spec 0016 | §3.5 |
| `INotifier.Notify`, `NotificationRequest`, type `core.user.added` | spec 0020 | §3.3 |
| `TenantSettings`, `ILanguageCatalog`, `ICalendarRegistry`, `IStringLocalizer` | spec 0012 | §3.2, §3.3, §3.5 |
| `ITenantProvisioningStep`, `TenantProvisioningContext`, `dbo.__TellmaProvisioning`, migrator commands | spec 0010 | §5.6, §6 |
| `RequestContext`, `ITenantMembershipDirectory.RecordAsync`, `ISessionTerminationListener.TenantAccessRevokedAsync`, `Tellma:PublicOrigin` | spec 0010 | §3.3, §3.6 |
| `IEmailSender`, `EmailAudience`, `ISandboxContext` | spec 0007 §3 | §3.3, §3.5 |

## 2. The identity-server client

### 2.1 Contract

```csharp
// Tellma.Core.Abstractions.Identity
public interface IIdentityServerClient   // one per distribution; tenant-agnostic; machine-to-machine; never called inside a transaction
{
    Task<IReadOnlyList<IdentityInvitationResult>> InviteAsync(IReadOnlyList<IdentityInvitation> invitations);
    Task<IReadOnlyList<IdentityDeliveryStatus>> GetInvitationDeliveryAsync(IReadOnlyList<string> subjects);
    Task<IdentityServiceAccount> CreateServiceAccountAsync(string displayName);
    Task DeleteServiceAccountAsync(string clientId);   // not-found is success
}

public sealed record IdentityInvitation(
    string Email, string? DisplayName, string? Locale, Gender? Gender, string? ReturnUrl, bool ExistingOnly = false);

public sealed record IdentityInvitationResult(
    string Email, string? Subject, InviteStatus? Status, string? Error);

public sealed record IdentityDeliveryStatus(
    string Subject, IdentityDeliveryState State, bool ExpectsDeliveryEvents,
    DateTimeOffset? SentUtc, DateTimeOffset? UpdatedUtc, string? Reason);

public enum IdentityDeliveryState
{
    NotFound, Pending, Sent, Delivered, Bounced, Complained, Rejected, Abandoned, Accepted
}

public sealed record IdentityServiceAccount(string ClientId, string ClientSecret);

public sealed class IdentityServerClientOptions              // Tellma:Identity; validated at startup
{
    public Uri Authority { get; set; }                       // required; the issuer URL, path included
    public string ClientId { get; set; }                     // set by the platform after binding: "<slug>-svc"; not a configuration key
    public string ServiceClientSecret { get; set; }          // required
}

// Tellma.Core.Identity (runtime)
public sealed class IdentityServerClient : IIdentityServerClient;
```

| Member | Meaning |
|---|---|
| `InviteAsync` | Creates-or-gets identity users by email and queues invitation links (spec 0003's bulk invite API). Input order is preserved; the input is split into chunks of at most 1,000, each one HTTP call. A chunk that succeeds yields one result per invitation: `Subject` and `Status` on success, `Error` (the server's per-user text, verbatim) otherwise. A cancelled or failed call after `k` complete chunks returns the `k` chunks' results as a prefix (§2.3). |
| `IdentityInvitation.ExistingOnly` | Get-by-email that never creates a user and never sends mail, with three outcomes (spec 0021 §8): an existing user who holds a credential answers `Status = Active` with their subject; an existing user without one answers the per-user error `existing_user_has_no_credential`; an unknown address answers `unknown_email`. A spec 0021 amendment (`existingOnly` per invitation); set by `UserService` on sandbox tenants (§3.3). |
| `IdentityInvitationResult.Status` | Spec 0013's `InviteStatus`: `Invited` (new identity, link queued), `Reinvited` (existing credential-less identity re-linked, link queued), `Active` (credential exists; no email sent; the tenant notifies the user itself). |
| `GetInvitationDeliveryAsync` | Delivery state of the latest invitation this client raised per subject (spec 0003's bulk delivery-status API), chunked at 1,000, input order. A subject invited by another client, or whose invite returned `Active`, is `NotFound`. |
| `CreateServiceAccountAsync` | Registers a confidential `client_credentials` client at the identity server (spec 0003 §10.2): one call, `DisplayName` the row's `Name`, `Resources = [Tellma:PublicOrigin]`, the audience of spec 0010's bearer surface (the MCP audience is per tenant and spec 0015's to add). The secret is returned once and never logged. |
| `DeleteServiceAccountAsync` | Deletes a client this distribution created (ownership-scoped at the server); a `404` is success. |
| `IdentityServerClientOptions` | `Authority` must be absolute; `https` except in Development; `ServiceClientSecret` non-empty. `ClientId` is `<slug>-svc` from the deployment identity (spec 0010 §5.7), set by the platform after binding. The same section binds spec 0010's OIDC options; the migrator host binds it too (§6.2). |

### 2.2 Transport and token

- **HTTP.** A named client `Tellma.Identity` from `IHttpClientFactory`; base address `Authority`;
  per-call timeout 30 s; `Accept: application/json`; a `User-Agent` of `Tellma.Core/<version>`.
  Request and response bodies are the shapes spec 0003 documents for the three APIs plus the
  `existingOnly` member; JSON through the platform's source-generated options.
- **Token.** `client_credentials` at `Authority`'s token endpoint with `scope=tellma_identity` and
  no `resource` parameter: the identity server sets `aud` to that scope's fixed platform audience
  (spec 0003 §6.2), the one its management API accepts; the client is authenticated with
  `ClientId`/`ServiceClientSecret`. The token is cached process-wide until 60 s before its expiry
  under a single-flight lock (concurrent callers await one fetch). One `401` on an API call
  discards the cached token, fetches once and retries the call once; a second `401` is final.
- **Failures.** A transport failure, a `5xx`, or a token failure before any chunk completed raises
  `DependencyUnavailableException` (503, `Retry-After: 1`); after at least one chunk completed, the
  client returns the prefix and the caller decides (§3.3). A `4xx` other than `401` is an
  `InvalidOperationException` (a 500 with a trace id: the distribution's registration is wrong, not
  the caller's request). Every call is logged at Information as
  `IdentityCall(endpoint, count, outcome, elapsedMs)`; secrets and tokens never appear in logs.
- **Telemetry.** `tellma.identity.calls` (counter; tags `identity.endpoint` ∈
  `invite | delivery | service-account`, `outcome` ∈ `ok | partial | unavailable | error`) and
  `tellma.identity.call.duration` (histogram, s, same tags).

### 2.3 Prefix semantics

Chunk `i` completes atomically on the server. The client processes chunks sequentially; when chunk
`i` fails or the caller's cancellation token fires after chunk `i − 1`, the client returns the
results of chunks `1..i − 1` and stops. The caller distinguishes the prefix from a complete result
by length. No chunk is ever retried by the client except the single `401` retry: the server's
`Reinvited` answer queues a fresh email, so a blind retry would spam users.

## 3. The users stack

### 3.1 Service and operations

```csharp
// Tellma.Core.Users (runtime)
public class UserService<TUser> : EntityService<TUser> where TUser : User
{
    public Task<IReadOnlyList<InviteResult>> InviteAsync(ActionContext<TUser, int> context);
    public Task<IReadOnlyList<CredentialsResult>> IssueCredentialsAsync(ActionContext<TUser, int> context);
    public Task<IReadOnlyList<InvitationStatus>> GetInvitationStatusAsync(IdsRequest request);
    public Task<MeResult> MeAsync();
    public Task<EntitiesResult<TUser>> SaveMeAsync(SaveRequest<TUser> request);
    public Task<PreferencesResult> SetMyPreferencesAsync(IReadOnlyList<KeyValueItem> items);
    public Task<PreferencesResult> DeleteMyPreferencesAsync(IReadOnlyList<string> keys);
    public Task<PreferencesResult> GetPreferencesAsync(UserPreferencesRequest request);
    public Task SetPreferencesAsync(ActionContext<TUser, int> context, UserPreferencesArguments arguments);
    public Task DeletePreferencesAsync(ActionContext<TUser, int> context, UserPreferenceKeysArguments arguments);
    public Task<TestNotificationResult> SendTestNotificationAsync(TestNotificationChannel channel);
}

// Tellma.Core.Abstractions.Access — results and requests
public sealed record InviteResult(int Id, InviteStatus? Status, string? Error);
public sealed record CredentialsResult(int Id, string ClientId, string ClientSecret);
public sealed record UserPreferencesRequest(int UserId);
public sealed record UserPreferencesArguments(IReadOnlyList<KeyValueItem> Items);
public sealed record UserPreferenceKeysArguments(IReadOnlyList<string> Keys);

public sealed record InvitationStatus(
    int Id, UserState State, InviteStatus? InviteStatus, string? LastInviteError,
    IdentityDeliveryStatus? Delivery);

public sealed record KeyValueItem(string Key, string Value);

public sealed record PreferencesResult(IReadOnlyDictionary<string, string> Preferences, string PreferencesTag);

public enum TestNotificationChannel { Email, Sms }

public enum TestNotificationResult { Sent, NoContactAddress, NotConfigured }

[TableType("UserInvitationOutcomeList")]
public sealed class UserInvitationOutcome                    // standalone; the invite write-back row
{
    public int UserId { get; set; }                          // key
    public string? Subject { get; set; }                     // varchar(255)
    public string? InviteStatus { get; set; }                // varchar(9)
    public string? LastInviteError { get; set; }             // nvarchar(1024)
}

public static class UsersTelemetryNames
{
    public const string MeterName = "Tellma.Core";
    public const string Invites = "tellma.users.invites";
    public const string IdentityCalls = "tellma.identity.calls";
    public const string IdentityCallDuration = "tellma.identity.call.duration";
    public const string TestNotifications = "tellma.users.test_notifications";
    public const string OutcomeTag = "outcome";
    public const string EndpointTag = "identity.endpoint";
    public const string ChannelTag = "channel";
}
```

| Member | Annotation and route |
|---|---|
| `Invite` | `[EntityAction("invite", Description = "Invite users through the identity server")]`; securable action `Invite` (`core.User × Invite`, filtered, sensitive by spec 0013's default); `POST /{tenantId}/api/web/users/invite` with `IdsRequest`. |
| `IssueCredentials` | `[EntityAction("issue-credentials", Action = "Credentials", Description = "Issue or rotate a service account's client credentials")]`; securable action `Credentials` (`core.User × Credentials`, filtered, sensitive by spec 0013's default); `POST /{tenantId}/api/web/users/issue-credentials` with `IdsRequest` of exactly one id. |
| `GetInvitationStatus` | `[ApiAction("invitation-status", Action = "Read", Idempotent = true, Mutation = false)]`; `users/invitation-status`. |
| `Me` | `[ApiAction("me", MemberOnly = true, Idempotent = true, Mutation = false)]`; `users/me`. |
| `SaveMe` | `[ApiAction("me/save", MemberOnly = true)]`; `users/me/save`. |
| `SetMyPreferences`, `DeleteMyPreferences` | `[ApiAction("me/preferences/set", MemberOnly = true)]`, `[ApiAction("me/preferences/delete", MemberOnly = true)]`. |
| `GetPreferences` | `[ApiAction("preferences/get", Action = "Preferences", Idempotent = true, Mutation = false)]`; `users/preferences/get`; body `{ userId }`. |
| `SetPreferences`, `DeletePreferences` | `[EntityAction("preferences/set", Action = "Preferences")]`, `[EntityAction("preferences/delete", Action = "Preferences")]`; exactly one id; `users/preferences/set`, `users/preferences/delete`. |
| `SendTestNotification` | `[ApiAction("me/test-notification", MemberOnly = true)]`; body `{ channel }`. |

Every other operation (`query`, `get`, `get-by-ids`, `save`, `delete`, `delete-by-query`,
`activate`, `deactivate`, the Excel four) is the standard projection of spec 0014 over the stack
descriptor; this service adds hooks, never operations. Securables: `core.User × Read | Save |
Delete | Activate | Invite | Preferences | Credentials`, all with `FilterRoot = core.User`,
registered by the stack feature; `Preferences` is not sensitive.

### 3.2 Hooks

- **`BespokeGrant(action, context)`** returns `FilterTree.Leaf("Id = me()")` for `Read` and `Save`
  and `null` otherwise: every connected member can read and save their own row. The evaluator
  disjoins it with stored grants as an `AccessCriterion` (spec 0013), so a caller with no role grant
  on `core.User` still gets `Filtered` on their own id.
- **`PreprocessAsync`.** `Email`, `ContactEmail`: trim, lower-case the domain and the local part
  (the identity server compares case-insensitively), `null` when empty. `Name`, `Name2`, `Name3`,
  `ContactMobile`: trim. `PreferredLanguage`: canonical BCP 47 casing, extensions stripped.
  `PreferredCalendar`: lower-case.
- **`ValidateAsync`** (before spec 0013's `UserAccessRules<TUser>`, which owns escalation,
  self-lockout, the email lock, the `Kind`/`Email` pair and the delete policy):

| Rule | Code (path) |
|---|---|
| `PreferredLanguage` is `null` or `ILanguageCatalog.IsOffered` | `Users.LanguageNotOffered` (`PreferredLanguage`) |
| `PreferredCalendar` is `null` or in `ICalendarRegistry.Codes` | `Users.CalendarUnknown` (`PreferredCalendar`) |
| `PreferredTimeZone` is `null` or resolvable by `TimeZoneInfo.TryFindSystemTimeZoneById` | `Users.TimeZoneUnknown` (`PreferredTimeZone`) |
| `ContactEmail` is `null` or a valid address (`MailAddress` parse, one `@`) | `Users.ContactEmailInvalid` (`ContactEmail`) |
| `ContactMobile` is `null` or E.164 (`^\+[1-9][0-9]{6,14}$`) | `Users.MobileInvalid` (`ContactMobile`) |
| A save whose `Save` decision on `core.User` is satisfied only by the bespoke criterion (every matching `AccessGrant.Source = Bespoke`) changes only the self-editable columns `UserService` lists (`Name`, `Name2`, `Name3`, `ImageId`, `SignatureId`, `PreferredLanguage`, `PreferredCalendar`, `PreferredTimeZone`, `Gender`, `ContactEmail`, `ContactMobile`) | `Users.NotSelfEditable` (the changed column) |
| The same save carries `RoleMemberships = null` or an unchanged set | `Users.NotSelfEditable` (`RoleMemberships`) |

The two rules make the admin `save` endpoint and `me/save` one path for a member who holds only the
bespoke self grant: the pipeline admits the row (it is visible under `Id = me()`), and the rules
confine the change to the self-editable columns.

- **`ValidateActionAsync("invite")`** per target row: `Kind = Human` else `Users.NotHuman`;
  `IsActive = 1` else `Users.Inactive`; `State ≠ Joined` else `Users.AlreadyJoined`. Rows the
  caller cannot see under the `Invite` grant are absent from `context.Entities` and are
  `Entity.NotFound` by the pipeline's rule.
- **`ValidateActionAsync("issue-credentials")`** per §3.8.
- **`AfterCommitAsync`** and the action `OnCommitted` callbacks: §3.6.

### 3.3 The `invite` action

The action runs in three phases across two round trips; no transaction is open during the remote
call.

1. **Load and validate (RT1, the action's load round trip).** The pipeline loads the target rows
   under the caller's `Invite` grant and runs `ValidateActionAsync`. Every row that survives is
   invited; a single invalid row fails the whole action (422), because the identity call has not
   happened yet and the admin fixes the list.
2. **Remote call (no batch in flight).** One `IdentityInvitation` per row: `Email`;
   `DisplayName = Name`; `Locale = PreferredLanguage ?? TenantSettings.Languages[0].Code`; `Gender`;
   `ReturnUrl = <Tellma:PublicOrigin>/<tenantId>`; `ExistingOnly = RequestContext.IsSandbox`. A
   sandbox tenant never causes the identity server to email anyone: a result whose `Error` is
   `unknown_email` or `existing_user_has_no_credential` (spec 0021 §8) is the per-row error
   `Users.SandboxRequiresExistingIdentity` (a row in the result, not a 422), and that code — not the
   server's text — is what `LastInviteError` records, the one exception to §2.1's verbatim rule.
3. **Write-back (RT2, the action's persist batch).** The results are loaded into
   `@tb{b}_t0 : UserInvitationOutcomeList` (one row per invited id; `Subject`, `InviteStatus`,
   `LastInviteError` from the result) beside `@tb{b}_t1 : IdList` holding the same ids, and one
   statement is appended through `context.Batch.Sql` with `SqlOptions(Writes = { core.Users },
   UserIds = @tb{b}_t1)` so the epilogue bumps each invited user's `PreferencesTag` (the
   `[BumpsUserVersionTag(Preferences, "Id")]` rule on `core.Users`):

```sql
DECLARE @tb{b}_now datetimeoffset(7) = SYSUTCDATETIME();
UPDATE u SET
    u.[Subject]          = COALESCE(u.[Subject], o.[Subject]),
    u.[InvitedAt]        = CASE WHEN o.[Subject] IS NULL THEN u.[InvitedAt] ELSE @tb{b}_now END,
    u.[InviteStatus]     = COALESCE(o.[InviteStatus], u.[InviteStatus]),
    u.[LastInviteError]  = o.[LastInviteError],
    u.[ModifiedAt]       = @tb{b}_now,
    u.[ModifiedById]     = @tm_UserId
FROM [core].[Users] AS u
JOIN @tb{b}_t0 AS o ON o.[UserId] = u.[Id]
WHERE u.[Subject] IS NULL OR o.[Subject] IS NULL OR u.[Subject] = o.[Subject];
SELECT o.[UserId] FROM @tb{b}_t0 AS o JOIN [core].[Users] AS u ON u.[Id] = o.[UserId]
WHERE o.[Subject] IS NOT NULL AND u.[Subject] <> o.[Subject];
```

   - The second result set lists rows whose stored `Subject` differs from the returned one: the
     email now belongs to a different identity account. Those ids come back as
     `InviteResult(Id, null, "Users.SubjectMismatch")` and their rows are untouched.
   - A `2627` on `UX_Users_Subject` (two tenant rows resolving to one identity account) is spec
     0011's unique mapping → `ValidationException` with `Unique` at `Subject`.
   - `ModifiedAt` is stamped: an admin's stale details page gets a concurrency error on its next
     save, which is the right signal; the statement ignores the stamp it finds (an invite outcome
     never conflicts semantically with a rename).
   - For every result with a `Status` (`Invited`, `Reinvited` or `Active`) whose loaded row holds
     no `Subject` or the returned one, the action calls `context.Notify` with
     `NotificationRequest("core.user.added", [id], { tenantName, actorName },
     TargetResource = null, ActorUserId = caller, DedupKey = "user.added")` (the descriptor is
     spec 0020's; `actorName` is the caller's `Name`; an invited user reads the notice on their
     first sign-in) and registers through `context.Batch.OnCommitted` one `IEmailSender.SendAsync`
     batch (audience `Transactional`) to each such user's `Email`: the welcome email, the one
     message a user keeps to find the distribution again. Template `UserAdded`, rendered under the
     user's `PreferredLanguage` falling back to the tenant primary; subject "You have been added to
     {TenantName}"; the body names the actor and the tenant and links `ReturnUrl`. For `Invited` and
     `Reinvited` the body adds that a separate message from the identity server carries the link
     that sets up their sign-in, to be used before the distribution link; it never says that message
     has arrived, because the identity server queues its link and this send follows the commit, so
     either can land first. On a sandbox tenant the send follows spec 0007's sandbox routing
     (`existingOnly` yields `Active` alone there). A send failure is logged
     (`UserAddedEmailFailed(userId)`), never thrown; the inbox notice is the durable copy. A
     mismatch the statement alone detects (a concurrent change between the round trips) keeps the
     notice already composed for it; the email skips the ids the second result set excluded.
   - The `OnCommitted` callback also calls `ITenantMembershipDirectory.RecordAsync` with
     `(TenantId, Subject, IsActive)` for every row that gained a subject (§3.6).
4. **Result.** `list<InviteResult>` in request order: `Status` and `Error` from the identity result,
   `Users.SubjectMismatch` for excluded rows. `tellma.users.invites` counts one per id with
   `outcome` ∈ `Invited | Reinvited | Active | Error | SubjectMismatch | NotAttempted`.

**Partial failure.** When the client returns a prefix (§2.3), the action composes the write-back
for the prefix only and throws `PartialFailureException("partial-failure", Results = the
prefix's InviteResults, Failed = the ids not attempted, as strings)`. The pipeline executes the
persist batch the action composed before the exception surfaces (spec 0014's partial-failure rule
for actions), so subjects that exist on the identity server are recorded; the web layer answers
502 `partial-failure` with `errorDetails.results` (the prefix) and `errorDetails.failed[]` (the
not-attempted ids), and the admin re-invites `failed`. Re-running the action never re-issues the
remote call before the load round trip has succeeded under fresh permissions: a stale-context
recompose (spec 0013's guard) happens in RT1, before phase 2.

### 3.4 Invitation status

`GetInvitationStatus(IdsRequest)`: one `IGuardedBatchRunner.Run(BatchPurpose.Read, …)` loading
`Id, State, Subject, InviteStatus, LastInviteError` of the requested ids under the caller's
`core.User × Read` filter (ids the caller cannot see are omitted, the pipeline's `get-by-ids` rule);
then one `GetInvitationDeliveryAsync` call for the subjects of rows with `State = Invited` and
`InviteStatus ≠ Active` (chunked at 1,000); no writes. The result per id:

| Stored state | `Delivery` |
|---|---|
| `State = New` | `null` (nothing was raised) |
| `InviteStatus = Active` | `IdentityDeliveryStatus(Subject, NotFound, false, …)` — the wire form the SPA renders as "no invitation email was needed" |
| `State = Joined` | `null` (the user has signed in; delivery is moot) |
| otherwise | the identity server's answer; `NotFound` here means "raised by another client registration"; `Sent` with `ExpectsDeliveryEvents = false` is terminal; `Reason` is present only for `Bounced`, `Rejected`, `Abandoned`, `Complained` |

### 3.5 Self-service

- **`Me()`** returns spec 0015's `MeResult` built from `ConnectedUser` (the profile),
  `IAccessEvaluator.EvaluateAll()` for `AccessSummary`, `ISecurableRegistry.Fingerprint`, the
  tenant tags from `BatchOutcome.VersionTags` and the caller's `PreferencesTag` from
  `BatchOutcome.UserVersionTags` (spec 0012 §2.5) as wire tags, and the caller's preference bag,
  read by one `Read` batch that is also the cold prologue's round trip
  (`SELECT [Key], [Value] FROM [core].[UserPreferences] WHERE [UserId] = @tm_UserId`). One round
  trip.
- **`SaveMe(SaveRequest<TUser>)`.** Exactly one entity (`BadRequestException` otherwise). Runs
  `SaveAsync` with `SaveOptions(ReturnEntities = true, Details = DetailsRequest(Select =
  request.Select, Include = request.Include), Concurrency = request.Concurrency, Source = Web)`
  under the caller's `Save` decision on `core.User`, which this service's `BespokeGrant`
  (`Id = me()`, §3.2) satisfies for the caller's own row; another id is invisible or unsaveable and
  fails the pipeline's pre-check. The `Users.NotSelfEditable` rule of §3.2 confines the change to
  the self-editable columns, the hooks of §3.2 run, and the epilogue bumps the caller's
  `PreferencesTag` through the entity's declared rule. A changed `ImageId` or `SignatureId` goes
  through spec 0016's attach rule (its kind, staged by the caller, unexpired). Two round trips
  (before image, then persist).
- **`SetMyPreferences(items)` / `DeleteMyPreferences(keys)`.** Validation in memory: a key matches
  `^[a-z][a-zA-Z0-9]*(\.[a-z][a-zA-Z0-9]*)*$` and is at most 128 characters
  (`Users.PreferenceKeyInvalid`, path `items[i].key`); a value is at most spec 0013's
  `MaxPreferenceValueBytes` of UTF-8 (`Users.PreferenceValueTooLarge`); duplicate keys in one
  request are `Users.PreferenceKeyDuplicate`.
  One `IGuardedBatchRunner.Run(BatchPurpose.Persist, …)` executes spec 0013's `core.UserPreferences`
  statement — the `UPDATE` + `INSERT` halves for `set`, the `DELETE` half for `delete` — with
  `@tb{b}_t0 : UserPreferenceList` and `SqlOptions.ForCaller({ core.UserPreferences })`, followed by
  `SELECT [Key], [Value] FROM [core].[UserPreferences] WHERE [UserId] = @tm_UserId`. A `set` that
  would leave more than spec 0013's `MaxPreferenceKeys` keys is `Users.TooManyPreferenceKeys` —
  asserted inside the same statement, the cap bound as `@tb{b}_p1`
  (`IF (SELECT COUNT(*) FROM [core].[UserPreferences] WHERE [UserId] = @tm_UserId) > @tb{b}_p1
  THROW 50422, N'Users.TooManyPreferenceKeys', 1;` after the `INSERT`). The result carries the bag
  and the caller's new `PreferencesTag` from `BatchOutcome.UserVersionTags` (spec 0012 §2.5) as a
  wire tag. One round trip.
- **`SendTestNotification(channel)`.** `Email`: the address is `ContactEmail ?? Email` from
  `ConnectedUser.Profile` (zero round trips); absent → `NoContactAddress`; otherwise one
  `IEmailSender.SendAsync` (audience `Transactional`, template `TestNotification` rendered under the
  caller's `PreferredLanguage` and `PreferredCalendar` with the current tenant time) → `Sent`, or
  `DependencyUnavailableException("email")` when the sender throws. `Sms` → `NotConfigured` (no
  transport this release). `tellma.users.test_notifications` counts by `channel` and `outcome`.

- **`GetPreferences(UserPreferencesRequest)`, `SetPreferences`, `DeletePreferences`.** The path
  for a caller holding `core.User × Preferences` (spec 0013 §3.4): `get` runs one `Read` batch
  loading the target's `Id` under the grant's filter (an id the filter hides is
  `NotFoundException`), the target's rows from `core.UserPreferences` and its `PreferencesTag` from
  `core.UserStamps`, returned as `PreferencesResult`; `set` and `delete` are entity actions over
  exactly one id (`Users.OnePreferencesTarget` otherwise) whose RT1 loads the target under the same
  grant and whose method appends the bag statement's halves through `context.Batch` with
  `Writes = { core.UserPreferences }` and `SqlOptions.UserIds` naming a one-row `IdList` TVP holding
  the target's id, so RT2's epilogue bumps the target's `PreferencesTag`; they return nothing beyond
  the projected `EntitiesResult`, the caller re-reading through `get` when it needs the bag. The key
  grammar, the caps and the per-request checks apply as for `me/preferences/set` (spec 0013 §3.4
  for what the server does and does not enforce).

### 3.6 Post-commit side effects

| Event | Effect (post-commit; failures logged, never thrown) |
|---|---|
| A user deactivated (the `deactivate` action; `IsActive` is server-owned, so a save never flips it) | `ISessionTerminationListener.TenantAccessRevokedAsync(tenantId, subject)` for each row with a subject; `ITenantMembershipDirectory.RecordAsync([(tenantId, subject, false)])`; for a `Service` row, `DeleteServiceAccountAsync(subject)` (§3.8), so the client obtains no token; reactivation needs `issue-credentials` again. The BFF cookie is untouched; the connect prologue refuses the user on their next request regardless. |
| A user activated | `RecordAsync([(tenantId, subject, true)])` for rows with a subject. |
| A user gained a subject (§3.3, §3.8) | `RecordAsync([(tenantId, subject, isActive)])`. |
| A user deleted | Only `State = New` rows are deletable (spec 0013's `Users.OnlyNewUsersDeletable`); such rows have no subject, so no listener and no identity call. |

The hint table is navigation only (spec 0010 §3.8); a missed call is repaired by
`MembershipReconcileService` (spec 0010's hosted timer, daily by default), never by the request
path.

### 3.7 `AccessService`

```csharp
// Tellma.Core.Access (runtime)
[ApiRoute("access")]
public sealed class AccessService
{
    public Task<IReadOnlyList<AccessDecision>> CheckAsync(AccessCheckRequest request);
}
```

`[ApiAction("check", MemberOnly = true, Idempotent = true, Mutation = false)]`. `UserId = null` →
the caller: `IAccessEvaluator.EvaluateAsync(request.Securables)`. Another user →
`IAccessEvaluator.RequireAsync("core.Role", "Read", [])` (effective grants are role-editor
information) and one `Read` round trip asserting the target row is visible under the caller's
`core.User × Read` decision (absent → `NotFoundException`); then `EvaluateForAsync(userId,
resource, action)` per securable, in request order, over the one `ForUser` load the scope memoises
(spec 0013 §5.1): two round trips. Unknown securables are `Denied` with
`AccessProblemCode.UnknownResource`/`UnknownAction`, never a 400. At most 200 securables per request
(`LimitExceededException`).

### 3.8 The `issue-credentials` action

Exactly one id (`Users.OneCredentialsTarget` at `Ids` otherwise); three phases across two round
trips with no transaction open during the remote call, as for `invite`:

1. **Load and validate (RT1).** The pipeline loads the row under the caller's `Credentials` grant;
   `ValidateActionAsync("issue-credentials")`: `Kind = Service` else `Users.NotService`;
   `IsActive = 1` else `Users.Inactive`.
2. **Remote call.** `CreateServiceAccountAsync(Name)`. A failure is
   `DependencyUnavailableException` with nothing written.
3. **Write-back (RT2).** One statement through `context.Batch.Sql` with
   `SqlOptions(Writes = { core.Users }, UserIds = @tb{b}_t0)`, a one-row `IdList`, so the epilogue
   bumps the row's `PreferencesTag`:

```sql
DECLARE @tb{b}_now datetimeoffset(7) = SYSUTCDATETIME();
UPDATE u SET
    u.[Subject]      = @tb{b}_p0,
    u.[JoinedAt]     = COALESCE(u.[JoinedAt], @tb{b}_now),
    u.[ModifiedAt]   = @tb{b}_now,
    u.[ModifiedById] = @tm_UserId
FROM [core].[Users] AS u JOIN @tb{b}_t0 AS t ON t.[Id] = u.[Id]
WHERE u.[Kind] = 'Service';
```

   `OnCommitted`: `ITenantMembershipDirectory.RecordAsync([(tenantId, clientId, true)])` and, when
   the row held a previous subject, `DeleteServiceAccountAsync(previous)`: the old client dies
   after the new one is recorded, so a failure between the two leaves working credentials and the
   action is simply re-run; a delete failure is logged (`ServiceAccountDeleteFailed(userId)`), and
   the identity server's ownership-scoped delete is the operator's path. A persist failure after
   the remote call deletes the new client, best effort, before surfacing; an orphaned client is
   harmless (spec 0013 §7.7).
4. **Result.** `CredentialsResult(Id, ClientId, ClientSecret)`: the secret crosses once, in this
   response, and is never logged or stored; the SPA shows it once. The call is tagged
   `identity.endpoint = service-account`.

## 4. The roles stack

```csharp
// Tellma.Core.Users (runtime)
public class RoleService<TRole> : EntityService<TRole> where TRole : Role;
```

`RoleService` adds no operations: `query`, `get`, `get-by-ids`, `save`, `delete`,
`delete-by-query`, `activate`, `deactivate` and the Excel four are the standard projection;
securables `core.Role × Read | Save | Delete | Activate` (`FilterRoot = core.Role`).
`RoleMemberships` is a child of `User` only; the role details page shows members through a details
extra (`IDetailsContributor<Role>` named `members`, one `Rows` query over `core.RoleMembership`
joined to `core.User` restricted by joining the plan's `IdsSource` (spec 0014's details plan; the
contributor interpolates the identifier and never spells its name), projecting
`UserId, User.Name, User.Name2, User.Name3, User.Email, Notes`, served only when the caller holds
`core.User × Read`); adding members is a bulk user save.
Spec 0013's `RoleAccessRules<TRole>` owns securable existence, wildcard, public-role,
`Administrator`-immutability and escalation rules, and the filter text (§4.1).

### 4.1 Filter validation and the language-version stamp

Both are spec 0013 §3.7's, run by `RoleAccessRules<TRole>` in the validation round of every role
save on any `TRole` leaf; RT1 primes the before images the stamp rule reads.

### 4.2 Persist and tags

A role save writes `core.Roles` and `core.Permissions`, both `[BumpsVersionTag("permissions")]`;
the executor's epilogue bumps the tenant `permissions` tag and every caller's cached set
invalidates at its next prologue. No per-member `PermissionsTag` bump is needed: the tenant tag
covers every role-side change, and the user-level tag is bumped only by membership writes (spec
0013). `activate`/`deactivate` on a role are the standard actions; spec 0013's `IAccessGuards`
appends the lockout invariants to every persist that touches a security table, actions and
deletes included. Round trips: save, delete, activate and deactivate 2 each — the before images
and `RoleAccessRules`'s loads in RT1, the persist in RT2 (spec 0013 §7.5).

## 5. The GL module

### 5.1 Packages

`src/module/gl/Tellma.Module.Gl.Abstractions/` (package `Tellma.Module.Gl.Abstractions`, namespace
`Tellma.Module.Gl`, references `Tellma.Core.Abstractions`) holds the entity, the enum, the resource
constant, `GlModule.FeatureName` (what a dependent's `[Requires]` names, spec 0010 §2.1) and the
validation codes: a distribution's own modules must reference `gl.Centers` without `Tellma.Core`.
`src/module/gl/Tellma.Module.Gl/` (package `Tellma.Module.Gl`, same namespace, references its
Abstractions) holds the service, the feature, the provisioning step and `Resources/Strings.resx`
(base name `Tellma.Module.Gl.Resources.Strings`; keys `Gl_Center`, `Gl_Center_Plural`,
`Gl_Center_Name`, `Gl_Center_Code`, `Gl_Center_CenterType`, `Gl_Center_ParentId`,
`Gl_Center_IsActive`, `Gl_CenterType_Abstract` … `Gl_CenterType_Sale`,
`Centers_ParentMustBeGrouping`, `Centers_HasChildren`, in English and Arabic; the key grammar of
spec 0012 §10.1). Both carry a README and the Apache-2.0 header; both build with warnings-as-errors
and XML docs on every member.

### 5.2 `Center` and `CenterType`

```csharp
// Tellma.Module.Gl.Abstractions — namespace Tellma.Module.Gl
public enum CenterType { Abstract, BusinessUnit, Service, Operation, Sale }

[Table("Centers", Schema = "gl"), TableType, Tree(MaxDepth = 32)]
[ApiResource(Description = "Responsibility centers")]
public class Center : ActivatableTreeEntity                  // non-abstract, unsealed; no generic base; no CLR Parent
{
    public CenterType CenterType { get; set; }                                     // required
    [Multilingual, Searchable] public string Name { get; set; }                    // required; max 255
    public string? Name2 { get; set; }                                             // max 255
    public string? Name3 { get; set; }                                             // max 255
    [NaturalKey, Searchable(SearchKind.Prefix)] public string Code { get; set; }   // required; max 50
}

public static class GlModule
{
    public const string FeatureName = "gl";                    // GlFeature.Name; what a dependent's [Requires(GlModule.FeatureName)] names
}

public static class GlResources
{
    public const string Center = "gl.Center";
}

public static class GlValidationCodes
{
    public const string ParentMustBeGrouping = "Centers.ParentMustBeGrouping";
    public const string HasChildren = "Centers.HasChildren";
}
```

| Value | Meaning | May have children |
|---|---|---|
| `Abstract` | A grouping node that never receives postings | yes |
| `BusinessUnit` | A profit or investment center grouping its operating centers | yes |
| `Service` | A service cost center (IT, HR, maintenance) | no |
| `Operation` | A production or operating cost center | no |
| `Sale` | A revenue center | no |

`ParentId`, `SubtreeCount`, `ActiveSubtreeCount` and `IsActive` (server-owned) come from
`ActivatableTreeEntity`; `Node` is the platform's shadow column; the Queryex navigation `Parent`
derives from `ParentId`. `CenterType` is stored under spec 0011's enum-as-string convention:
`varchar(12)`, no `IN` check. A distribution extends it by plain inheritance and
`tellma.UseEntity<Center, MyCenter>()`. Queryex entity `gl.Center`; navigations
`Parent → gl.Center`, `CreatedBy`/`ModifiedBy → core.User`; the default related projection is
`Id, Name, Name2, Name3, Code`.

### 5.3 `gl.Centers`

`ActivatableTreeEntity`; non-temporal; UDTT `CentersList` (excludes `Node`); sequence
`gl.sq_Centers AS int START WITH 1000 INCREMENT BY 1 NO CYCLE`.

| Column | Type | Null | Constraints | Notes |
|---|---|---|---|---|
| `Id` | `int` | no | `PK_Centers` clustered | |
| `ParentId` | `int` | yes | `FK_Centers_ParentId → gl.Centers(Id)` NO ACTION; `IX_Centers_ParentId` | |
| `CenterType` | `varchar(12)` | no | | enum as string |
| `Name` | `nvarchar(255)` | no | | |
| `Name2`, `Name3` | `nvarchar(255)` | yes | | absent from the schema when the tenant lacks the language |
| `Code` | `nvarchar(50)` | no | `UX_Centers_Code` | natural key |
| `IsActive` | `bit` | no | `DF_Centers_IsActive 1` | server-owned |
| `Node` | `hierarchyid` | no | `UX_Centers_Node` | shadow; id path; provisional `/0/<Id>/` on insert |
| `SubtreeCount` | `int` | no | `DF_Centers_SubtreeCount 1` | server-owned |
| `ActiveSubtreeCount` | `int` | no | `DF_Centers_ActiveSubtreeCount 1` | server-owned |
| `CreatedAt`, `ModifiedAt` | `datetimeoffset(7)` | no | | `ModifiedAt` is the concurrency token |
| `CreatedById`, `ModifiedById` | `int` | no | `FK_Centers_CreatedById`, `FK_Centers_ModifiedById → core.Users(Id)` | |

Index `IX_Centers_IsActive_Name (IsActive, Name)` for the default listing. No history table. The
tree statements of spec 0011 (affected set, re-path, cycle fence `Tree.Cycle`, scoped recount) are
appended by the emitter after every save, `activate`, `deactivate`, `delete` and
`delete-with-descendants` on this table; the weekly `core.tree-verify` job repairs drift and meters
`tellma.data.tree.repairs`. Deleting a center whose children are not in the same delete fails the
self-referencing FK; spec 0011 maps the `547` to `Fk.InUse`, which this stack narrows to
`Centers.HasChildren` at `Ids[i]` through `ValidateDeleteAsync` (§5.4) before the statement runs,
so the FK failure is only the race backstop.

### 5.4 `CenterService<TCenter>`

```csharp
// Tellma.Module.Gl
public class CenterService<TCenter> : EntityService<TCenter> where TCenter : Center;
```

- **`ValidateAsync`.** Declares one `context.Loader.ByIds<TCenter, int>` ref for the parents named
  by `ParentId` that are not in the payload (`select = "Id, CenterType, SubtreeCount"`) and awaits
  `context.Loader.LoadAsync()`, so the load shares the round with every other validator's; parents
  in the payload are read from the payload. Rules:

| Rule | Code (path) |
|---|---|
| A row's parent (payload or loaded) has `CenterType ∈ { Abstract, BusinessUnit }` | `Centers.ParentMustBeGrouping` (`[i].ParentId`) |
| An updated row whose `CenterType` changes to `Service`, `Operation` or `Sale` has no children: before image `SubtreeCount = 1` and no payload row names it as parent | `Centers.ParentMustBeGrouping` (`[i].CenterType`) |
| A parent must not be the row itself | `Tree.Cycle` (`[i].ParentId`) — the platform's `TreeCycleValidator` reports the general case |

  A parent id that resolves to no visible row is the pipeline's `Fk.NotFound` (references are
  validated under the target's `Read` filter).
- **`ValidateDeleteAsync`.** Loads `SubtreeCount` of the targets; a target with `SubtreeCount > 1`
  is `Centers.HasChildren` at `Ids[i]` (use `delete-with-descendants`).
- **`SearchFilter`** is left to the platform disjunction over `Code` (prefix) and the `Name` group
  (contains).
- No `PreprocessAsync`, `ContributeAsync` or `AfterCommitAsync` overrides: the tree recompute is the
  emitter's.

Round trips: create with in-payload parents 2 (the `Code` key load in RT1); update 2; delete 2
(the `SubtreeCount` load in RT1); activate/deactivate 1 (plus the recount inside the same batch).

### 5.5 `GlFeature`

```csharp
// Tellma.Module.Gl
public sealed class GlFeature : ITellmaFeature                // Name = GlModule.FeatureName; no [Requires]: Core is implicit (spec 0010 §2.1)
{
    public void Declare(FeatureDeclaration declaration);      // declares no required feature
    public void Contribute(FeatureContribution contribution); // Entity<Center, CenterService<Center>>(); ProvisioningStep<GlSampleCentersStep>(); Model<CenterConfiguration>()
}
```

`CenterConfiguration` is the EF `IEntityTypeConfiguration<Center>` for §5.3 (table, sequence,
indexes, the string conversion of `CenterType`); the tree convention adds `Node`, the FK and the
unique index. Securables `gl.Center × Read | Save | Delete | Activate` (`FilterRoot = gl.Center`)
are registered by the stack feature from the descriptor; the module registers none by hand. The
projected routes are `/{tenantId}/api/web/centers/{operation}` with `{operation}` one of `query`,
`get`, `get-by-ids`, `get-by-parent-ids`, `save`, `delete`, `delete-by-query`,
`delete-with-descendants`, `activate`, `deactivate`, `export`, `export-for-import`,
`inspect-import` and `import`; the MCP exposure is `Full`.

### 5.6 `gl.sample-centers`

```csharp
// Tellma.Module.Gl
public sealed class GlSampleCentersStep : ITenantProvisioningStep;   // Name = "gl.sample-centers"; Order = 100; Version = 1
```

Runs after the platform steps in the migrator's step-runner scope (spec 0010's
`ITenantScopeFactory.CreateScopeAsync(snapshot, allowNonActive: true)`, `Kind = System`). When
`IHostEnvironment.IsDevelopment()` is false the step records completion and writes nothing (sample
data is a development convenience). Otherwise it resolves the existing rows first — one
`CenterService<TCenter>.QueryAsync` filtered by `Code` over the eight codes, projecting `Id, Code`
— then saves the tree below through `CenterService<TCenter>.SaveAsync` with
`SaveOptions(Source = System, ReturnEntities = false)`, each resolved row carrying its id, so a
re-run at a higher `Version` updates names and types and never duplicates; parents are referenced
by temporary negative ids within the payload. Round trips: the read, then the save's two (§5.4).

| Code | Name (en / ar) | Type | Parent |
|---|---|---|---|
| `HQ` | Headquarters / المقر الرئيسي | `Abstract` | — |
| `BU-TRD` | Trading / التجارة | `BusinessUnit` | `HQ` |
| `OP-TRD` | Trading Operations / عمليات التجارة | `Operation` | `BU-TRD` |
| `SL-TRD` | Trading Sales / مبيعات التجارة | `Sale` | `BU-TRD` |
| `BU-MFG` | Manufacturing / التصنيع | `BusinessUnit` | `HQ` |
| `OP-MFG` | Production / الإنتاج | `Operation` | `BU-MFG` |
| `SV-IT` | Information Technology / تقنية المعلومات | `Service` | `HQ` |
| `SV-HR` | Human Resources / الموارد البشرية | `Service` | `HQ` |

`Name2` is written only when the tenant's second language is Arabic (`TenantSettings.Shape`
gates the twin; the step reads `Languages` and drops the value otherwise).

### 5.7 `taxonomy.json`

This spec adds `"Gl"` to `modules` in the repository-root `taxonomy.json` created by spec 0010.
A unit test in `Tellma.Core.Tests` asserts that every `src/module/<m>/` folder has a
`Tellma.Module.<M>` project whose `<M>` is listed (PascalCase, two-letter segments such as `Gl`),
and that every listed module has a folder.

## 6. Provisioning, bootstrap and local development

### 6.1 The first administrator

Spec 0010's step `10 core.bootstrap-administrator` calls spec 0013's
`ITenantBootstrapper.BootstrapAdministrator(TenantBootstrapRequest(Email = AdminEmail,
Name = AdminEmail's local part, PreferredLanguage = the distribution default,
Subject = AdminSubject))` in the system scope, creating (or finding) the user with `State = New`
and a membership in role 1. What this spec adds is the second half:

- **Development (in-proc identity).** `Tellma:Seed:AdminEmail = admin@localhost` and the fixed
  development subject `00000000-0000-0000-0000-000000000001` — the same principal the in-proc
  identity server seeds (spec 0003) — reach the bootstrapper as `Subject`, which sets `Subject`,
  `InvitedAt` and `InviteStatus` directly, so the row reads `Invited`. No identity call is made.
  The administrator's first sign-in flips the row to `Joined` through the connect prologue. A
  database reset re-provisions it.
- **Deployed (`provision --admin-email`).** After the steps complete, the migrator's `provision`
  command resolves `UserService<TUser>` from the same system scope and calls
  `ExecuteActionAsync("invite", [adminId], null, ActionOptions())`. `RequestContext.Kind = System`
  bypasses the `Invite` securable (`UserAccess.System`); the flow of §3.3 runs unchanged, so the
  administrator receives the welcome email and the `core.user.added` notice, and the identity
  server's invitation email unless they already hold a credential. The migrator therefore binds
  `IdentityServerClientOptions` from the same `Tellma:Identity` section as the web host, holding
  `ServiceClientSecret` (spec 0010's secret policy). A failed invite fails the `provision` command
  after the steps are recorded; re-running `provision` re-invites (idempotent: the identity server
  answers `Reinvited` or `Active`).
- **Sandbox tenants.** The flow runs with `ExistingOnly = true` (§3.3); provisioning a sandbox for
  an administrator with no usable identity account — an unknown address, or an existing one holding
  no credential — fails with `Users.SandboxRequiresExistingIdentity` in the report, and the operator
  provisions the live tenant first.

### 6.2 Composition parity

The Web and Migrator hosts of the reference distribution build the same composition (spec 0010),
so `UserService`, `IIdentityServerClient`, `INotifier` and `IEmailSender` resolve identically in
both; the migrator's `IEmailSender` is spec 0007's configured sender with the same sandbox routing.
The distribution's Development configuration sets `Tellma:Identity:Mode = InProc`,
`Authority = https://localhost:4200/id` (`{PublicOrigin}/id`, spec 0010 §5.7; 7052 is only the Web
project's Kestrel port behind the SPA proxy) and the seeded stable secrets; `ClientId` is
`acme-svc` from the slug (spec 0010 §5.7); a test asserts no secret is tracked (spec 0010's rule).

### 6.3 From a fresh clone

`dotnet run --project distributions/acme/src/Tellma.Distro.Acme.Migrator -- migrate` creates the
catalog, provisions tenants 1 (`Acme`, Live) and 2 (`Acme Sandbox`) with `admin@localhost`, runs
`core.bootstrap-administrator`, `core.settings`, `core.blob-container` and `gl.sample-centers`;
`dotnet run --project distributions/acme/src/Tellma.Distro.Acme.Web` serves the site; the
administrator signs in with the console code, and the centers tree lists the eight sample rows on
tenant 1. Both commands run unchanged on Windows and Linux.

## 7. Reference distribution migrations

The tenant database schema is owned by the platform's `TellmaDbContext` plus the module and
distribution configurations; the **migrations** are the distribution's (spec 0010: only the
distribution generates and ships migrations, through `AcmeDesignTimeFactory`). This spec ships the
reference distribution's initial tenant migration and the rules every later one follows.

- **`Migrations/Tenant/20260904000000_InitialTenant`** in `Tellma.Distro.Acme.Migrator`, generated
  by `dotnet ef migrations add InitialTenant --context TellmaDbContext` and reviewed by hand. It
  contains, in this order: schemas `core`, `gl`; every `core` table of specs 0011–0020 with its
  sequence, indexes, checks, history table and period (`core.Users`, `UsersHistory`, `UserStamps`,
  `UserPreferences`, `Roles`, `RoleMemberships`, `Permissions` and histories, `Settings`,
  `SettingEntries` and histories, `VersionTags`, `Blobs`, `Jobs`, `Schedules`, `ScheduleStates`,
  `JobWorkerState`, `Notifications`, `NotificationPreferences`, `Exports`, `Imports`); `gl.Centers`
  (§5.3); `dbo.__TellmaProvisioning` and `dbo.__TellmaSchema` (spec 0011 §4.3); every UDTT of
  spec 0001's derivation (`UsersList`, `RolesList`, `RoleMembershipsList`, `PermissionsList`,
  `SettingsList`, `SettingEntriesList`, `CentersList`, …) and the standalone types of spec 0011 §4.4
  (`IdList`, `BigIdList`, `GuidList`, `StringList`, `DateList`, `IdStampList`,
  `UserInvitationOutcomeList`, `UserPreferenceList`, `NotificationPreferenceList`,
  `NotificationRowList`, `JobRequestList`, `JobOutcomeList`, `JobProgressList`, `JobLeaseList`,
  `ScheduleNextList`, `VersionTagList`; `TenantMembershipList` is a catalog type, spec 0010's); the
  `HasData` rows of the reserved band (spec 0013's system user, Administrator role, permission and
  membership, `UserStamps` row; spec 0012's `core.Settings` placeholder; spec 0019's built-in
  schedules and states; `JobWorkerState`). No `IDENTITY`, no `rowversion`, no trigger, no `MERGE`.
- **Not in a migration.** RCSI (`provision` sets it), the `tellma_app` role and grants (recomputed
  by the migrator after every run), `core.VersionTags` seeding (the migrator's registry seed), the
  first administrator and the sample centers (provisioning steps).
- **Later migrations** follow spec 0011's N−1 rule: a new column is nullable or defaulted; a column
  is dropped only when no application version that can still be running writes it; a rename is
  add, copy, then drop across three releases (spec 0011 §4.3); every UDTT change is a new physical
  type name (spec 0001's `<Logical>_<hash8>`) with the old one dropped one release later.
- **Model parity test.** `distributions/acme/test/Tellma.Distro.Acme.IntegrationTests` asserts the
  migrator's model snapshot has no
  pending model changes against the composed context (`GetPendingModelChanges()` empty), on
  Windows and Linux.

## 8. Budgets and observability

### 8.1 Round trips

Counted with the connect prologue riding the first business round trip (warm caches; +1 on a cold
or stale path).

| Operation | Database round trips | Remote calls |
|---|---|---|
| `users/invite` (n ids) | 2 | ⌈n/1000⌉ invite calls between them; one email batch after commit |
| `users/issue-credentials` | 2 | one create call between them; on rotation one delete call after commit |
| `users/invitation-status` | 1 | ⌈m/1000⌉ delivery calls (m = invited subjects) |
| `users/save` (admin) | 2 | — |
| `users/me` | 1 | — |
| `users/me/save` | 2 | — |
| `users/me/preferences/set` / `delete` | 1 | — |
| `users/preferences/get` | 1 | — |
| `users/preferences/set` / `delete` | 2 | — |
| `users/me/test-notification` | 0 | 1 send |
| `users/activate` / `deactivate` | 2 (spec 0013 §7.5: `UserAccessRules` loads in RT1) | — |
| `users/delete` | 2 (spec 0013 §7.5) | — |
| `access/check` (self / other) | 0 / 2 | — |
| `roles/save` | 2 | — |
| `roles/delete` | 2 (spec 0013 §7.5) | — |
| `roles/activate` / `deactivate` | 2 (spec 0013 §7.5) | — |
| `centers/save` | 2 | — |
| `centers/activate` / `deactivate` | 1 | — |
| `centers/delete` | 2 | — |
| `gl.sample-centers` step | 3 | — |

Every statement is parameterised by TVP or scalar; no per-id SQL text; no lock is held across a
remote call. The integration suite asserts each row through spec 0011's `DataAccessScope`
(`tellma.data.roundtrips`).

### 8.2 Instruments and logs

Meter `Tellma.Core` (constants in `UsersTelemetryNames`): `tellma.users.invites` (counter; tag
`outcome`), `tellma.identity.calls` (counter; tags `identity.endpoint`, `outcome`),
`tellma.identity.call.duration` (histogram, s; same tags), `tellma.users.test_notifications`
(counter; tags `channel`, `outcome`). `Tellma.Module.Gl` declares no meter: the tree recompute is
metered by spec 0011's `tellma.data.tree.recomputes` (tag `entity`). No tenant or user tag on any
instrument.

Log events (structured, Information unless stated):
`IdentityCall(endpoint, count, outcome, elapsedMs)`;
`UsersInvited(count, invited, reinvited, active, errors)`;
`UsersInvitePartial(processed, remaining)` (Warning); `UserAddedEmailFailed(userId)` (Warning, with
the exception); `ServiceAccountDeleteFailed(userId)` (Warning, with the exception);
`UserAccessRevoked(userId)`; `SampleCentersProvisioned(tenantId, count)`;
`TenantAdministratorInvited(tenantId, userId, status)`.
Traces: the invite action's activity carries child activities per identity chunk (`identity.invite`)
tagged with `identity.count` and `identity.outcome`.

## 9. Testing

| Project | Tier | What it pins |
|---|---|---|
| `test/core/Tellma.Core.Tests` | unit | `IdentityServerClient` against a fake `HttpMessageHandler`: chunking at 1,000 and input order; token caching, the 60 s margin and single flight (`FakeTimeProvider`); one `401` retry then final; prefix on a failed chunk and on cancellation; `DependencyUnavailableException` before any chunk; `ExistingOnly` serialised; options validation (`https` outside Development). `UserService` with a fake client and a fake pipeline: invite argument mapping (`Locale` fallback, `ReturnUrl`, sandbox `ExistingOnly`); `InviteResult` order and `SubjectMismatch`; `PartialFailureException` carries the prefix in `Results` and the not-attempted ids in `Failed`; issue-credentials mapping (`DisplayName = Name`, the resource), the secret absent from every log; §3.2 rules including the bespoke-only save restriction; preference key grammar and limits; test-notification outcomes. `AccessService`: self vs other, the `core.Role × Read` requirement, the 200 cap. `taxonomy.json` module consistency. |
| `test/module/gl/Tellma.Module.Gl.Tests` | unit | `CenterService` rules with a fake loader (parent in payload, parent loaded, type change with children, self-parent); delete of a row with children; the sample tree is acyclic, codes unique, Arabic twins present; the package's reference closure excludes `Tellma.Core`; `GlFeature` declares no required feature, `GlModule.FeatureName` is its `Name`, and it contributes exactly three items; spec 0012's resource audit over the GL assemblies. |
| `test/core/Tellma.Core.IntegrationTests` | `Category=Integration` (LocalDB or Testcontainers) | The write-back statement: state transitions, `InvitedAt` set once, `LastInviteError` cleared by a later success, `SubjectMismatch` rows untouched, `2627` on `UX_Users_Subject` → `Unique` at `Subject`, `PreferencesTag` bumped for invited users only, `ModifiedAt` stamped; `core.user.added` inserted for every outcome with a status and deduplicated; the welcome email queued for each, with the sign-in paragraph for `Invited` and `Reinvited` only; the membership hint recorded; the credentials write-back (`Subject`, `JoinedAt` set once, `PreferencesTag` bumped), rotation deleting the old client after commit, deactivation deleting the client, `Kind` write-once, a `Service` row refused by `invite` and a `Human` row by `issue-credentials`; `SaveMe` with a membership row is 422 and grants nothing; a member's admin `save` on their own row confined to the self-editable columns; the preferences statement (set, delete, the `MaxPreferenceKeys` cap, tag bump, second user unaffected); `gl.Centers` save with in-payload parents in two round trips (the `Code` key load in RT1), the parent-type rule, `Centers.HasChildren`, `delete-with-descendants`, activate/deactivate recounts, `ParentMustBeGrouping` on a type change; every §8.1 budget through `DataAccessScope`; `preferences/get`, `set` and `delete` for another user under the `Preferences` filter, bumping the target's `PreferencesTag`, a hidden target not found; `SignatureId` saved through `me/save` and the user save. |
| `test/core/Tellma.Core.IntegrationTests` | `Live=true` | Against the in-proc identity server: invite end to end (`Invited`, `Reinvited`, `Active`, a refused account's per-user error), `ExistingOnly` on a sandbox tenant, delivery status after a real send through the email log sink; a service account created end to end, a `client_credentials` token obtained and the connect prologue resolving the row as `ServiceAccount` through a test-mapped bearer endpoint under spec 0010's `Tellma.Api` policy. |
| `distributions/acme/test/Tellma.Distro.Acme.IntegrationTests` | `Category=Integration` | `migrate` then `provision` from an empty server creates `gl.Centers`, records `gl.sample-centers` with `Version = 1`, seeds eight centers in Development and none otherwise; `admin@localhost` is `Invited` and becomes `Joined` on first sign-in; a deployed-style `provision --admin-email` invites through the in-proc identity server; a bumped step version re-runs idempotently; model parity (§7); spec 0012's resource audit over the distribution's assemblies. |

PR runs the unit tier and `Category=Integration` on LocalDB (Windows) and Testcontainers (Linux);
nightly adds `Live=true`. Fixture: tenant 1 with `en`/`ar`, the reserved-band rows, one
administrator, three plain users (`New`, `Invited`, `Joined`), one service account, two roles (one
public), the sample centers.

## 10. Definition of done

- **Projects**: `src/module/gl/Tellma.Module.Gl.Abstractions`, `src/module/gl/Tellma.Module.Gl`,
  `test/module/gl/Tellma.Module.Gl.Tests` (new), `src/core/Tellma.Core.Abstractions`,
  `src/core/Tellma.Core`, `test/core/Tellma.Core.Tests`, `test/core/Tellma.Core.IntegrationTests`,
  `distributions/acme/src/Tellma.Distro.Acme.Web`,
  `distributions/acme/src/Tellma.Distro.Acme.Migrator`,
  `distributions/acme/test/Tellma.Distro.Acme.IntegrationTests` (grown) — each with a README, XML
  docs on every member, building and testing on Windows and Linux under warnings-as-errors, wired
  into `Tellma.slnx`; `taxonomy.json` lists `Gl`.
- **Behavior**: §2 (client, token, prefix), §3 (hooks, invite, status, self-service, side effects,
  `access/check`), §3.8 (credentials), §4 (the roles stack), §5 (`Center`, table, rules,
  feature, sample step), §6 (bootstrap in both modes), §7 (the initial migration and parity) —
  implemented and pinned by the suites of §9, green in CI.
- **Observability**: every instrument and log event of §8.2 emitted and asserted; every §8.1
  budget asserted.
- **CI**: unit and `Category=Integration` on PR (Windows LocalDB, Linux Testcontainers); `Live=true`
  nightly against the in-proc identity server; the `acme` smoke deployment provisions a tenant
  and invites its administrator.
- **Docs**: ARCHITECTURE.md updated where this spec touches it — the migrations/migrator section
  (the migrator holds the distribution's service-client credentials and invites the first
  administrator through the `invite` action of `UserService` after the provisioning steps), the
  entity-class section (a self-referencing tree entity is a single non-abstract class extended by
  plain inheritance, `Center` being the first), the package-naming section (the first
  `Tellma.Module.<M>` pair, `src/module/gl/`), and the reserved-slug/taxonomy section
  (`taxonomy.json` gains the `Gl` module). Public XML docs and error messages reference no
  `docs/` paths, per repo rule.
- **Not in scope of done**: the identity-server work itself (spec 0021 — the `Live=true` sandbox
  row of §9 requires an in-proc identity server carrying spec 0021 §8); the settings edit API
  (spec 0012), Excel over these stacks (spec 0018), the inbox page (spec 0020), the email outbox,
  SMS.

## Decisions record

The load-bearing decisions, where not already evident above:

1. **The identity call sits between two round trips, never inside a transaction** — a remote call
   under row locks blocks every other save of the tenant, and a transaction that rolled back after
   `Reinvited` would have spammed users (§3.3).
2. **Prefix on failure, `PartialFailureException` to the caller, subjects recorded first** — a
   subject the identity server created is real whether or not this tenant wrote it; recording the
   prefix and returning 502 with the remainder lets the admin retry only what was not attempted
   (§2.3, §3.3).
3. **`ExistingOnly` on sandbox tenants** — a sandbox must never cause an email, yet sandbox
   sign-in needs a real identity; get-by-email without mail is the only shape that satisfies both
   (§2.1, §3.3).
4. **Delivery state is a live drill-down, never stored** — the identity server owns delivery; the
   tenant stores only the outcome it needs to interpret `NotFound` (§3.4).
5. **The bespoke self grant covers `Read` and `Save`, and a bespoke-only save is confined by
   validation** — one save path and one RLS mechanism for self-service, with the admin endpoint
   made safe by a rule rather than a second grant model (§3.2, §3.5).
6. **`RoleService<TRole>` carries no filter logic** — filter validation and the language-version
   stamp are spec 0013's `RoleAccessRules<TRole>`, a component a substituted leaf cannot bypass
   (§4).
7. **No per-member tag bump on role save** — the tenant `permissions` tag invalidates every cached
   set; the user-level tag is for membership edits only (§4.2).
8. **`Center` is one non-abstract class with no generic and no CLR `Parent`** — the Queryex
   `Parent` navigation derives from `ParentId`, so the self-navigation that forced a generic base
   is gone and every entity extends by plain inheritance (§5.2).
9. **Five center types with the grouping rule in the service** — `Abstract` and `BusinessUnit`
   are the structural types every posting rule relies on; the rule is a validator, not a CHECK,
   because it depends on the parent's row (§5.2, §5.4).
10. **Sample centers are a Development-only versioned provisioning step** — reference data through
    the pipeline as the system user, idempotent by `Code`, never `HasData` (§5.6).
11. **The migrator invites the deployed administrator** — the migrator already hosts the full
    composition and holds the service secret (spec 0010), so one bootstrap path serves Development
    (subject known) and deployment (invite) (§6.1).
12. **Migrations are the distribution's; the schema is the platform's** — the reference migration
    is generated from the composed context and kept in parity by test (§7).
13. **Service accounts are rows the save creates and an action credentials** — one stack, one
    prologue, one permission model; the secret crosses once and the old client dies after the new
    one is recorded (§3.8).

## Review flags

1. **`Invited → Joined` inside the connect prologue** (§3.3, §6.1; the flip is spec 0013's
   statement) versus a nightly job that flips lazily. Chosen: in the prologue — once per user
   lifetime, and the admin's page is current. Flips if the temporal write on the request path
   shows up in history churn measurements.
2. **The pipeline executes a composed action batch before surfacing `PartialFailureException`**
   (§3.3) versus the action performing the prefix write-back through a second
   `IGuardedBatchRunner.Run` and then throwing. Chosen: the pipeline rule, one code path for the
   write-back. Flips if spec 0014's action path cannot honour it cheaply.
3. **Bespoke self grant on `Save` with a validation confinement** (§3.2) versus a `Read`-only
   bespoke grant and membership-only authorisation for `me/save`. Chosen: the grant, so the
   witness and the pre-check see an ordinary decision. Flips if the `AccessGrant.Source` inspection
   in a validator proves fragile.
4. **`RoleMemberships` single-owned by `User`, members as a read-only role extra** (§4) versus
   dual ownership with cross-owner stamp bumps. Chosen: single ownership — a simpler emitter and
   no stale-owner deletion race. Flips if admins reject "add members" as a bulk user save.
5. **Weak rows carry no audit columns** (§4; `Permissions`, `RoleMemberships` are spec 0013's)
   versus `ModifiedById` on the two security children. Chosen: none — the owner's stamp plus the
   child's period answer "who changed this". Flips on an auditor requirement for a per-row actor.
6. **`Gender` kept on `core.Users`** (§3.3) versus dropping it and sending no gender to the
   identity server. Chosen: kept — inflecting invitation grammars need it. Flips if the identity
   server adopts neutral templates.
7. **`ExistingOnly` as a spec 0021 amendment** (§2.1) versus refusing invites on sandboxes
   outright. Chosen: the amendment. Flips if the identity server cannot ship it before this spec
   is implemented, in which case sandbox invites return `Users.SandboxRequiresExistingIdentity`
   for every row until it lands.
8. **Five `CenterType` values** (§5.2) versus the three operating types. Chosen: five — the
   grouping rule needs the two structural types. Flips never; the question is whether to add
   the SG&A types (`Administration`, `Marketing`, `FinanceCost`, `OtherPL`) now — chosen: later,
   additively, when the first posting rule needs them.
9. **Lockout invariant accepts `Invited` administrators with a subject** (spec 0013's guard, which
   this spec's invite flow relies on) versus requiring `State = Joined`. Chosen: accept `Invited`
   — otherwise a tenant whose only administrator has not signed in yet is unmanageable. Flips if
   lost invitations prove common.
10. **`ExpectedModifiedAt` required on the settings patch** (spec 0012's `TenantSettingsPatch`;
    the API left this spec's scope) versus optional. Chosen: required. Flips if MCP agents cannot
    reliably read before writing.
11. **Reserved band `1..999` with positive ids** (spec 0013's `HasData`; the sample step uses
    ordinary ids) versus negative ids for well-known rows. Chosen: positive — readable in URLs and
    logs. Flips if an import that resets a sequence low ever collides.
12. **Delete restricted to `State = New` users** (§3.6; spec 0013's rule) versus deleting any user
    and mapping FK failures per referencing table. Chosen: restricted — predictable. Flips if
    admins need to purge mistaken invitations after sign-in.
13. **The migrator invites the deployed first administrator** (§6.1) versus provisioning only
    seeding the row and a web-side bootstrap endpoint sending the invite. Chosen: the migrator —
    one path, no second privileged endpoint; the cost is the service secret in the migrator's
    configuration. Flips if operators forbid secrets on the migration host.
14. **Sample centers only in Development** (§5.6) versus seeding them on every provisioned tenant
    as a starter tree. Chosen: Development — a customer's tree is their own. Flips if onboarding
    wants a starter chart.
15. **`Tellma.Module.Gl` declares no meter** (§8.2) versus a `Tellma.Module.Gl` meter for future GL
    instruments. Chosen: none until an instrument exists; adding one later is additive.
