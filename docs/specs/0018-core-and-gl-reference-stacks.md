# Spec: Core and GL Reference Stacks

- **Author:** Ahmad Akra
- **Date:** 4 September 2026

**Status:** Ready for implementation. Frozen once merged: revised only if implementation forces a
design change, then kept as the historical record of what shipped — never updated thereafter as the
code or its dependencies evolve.

## Context

This spec ships the first end-to-end product on the CRUD stack:

- the two Core services that every tenant runs, `UserService<TUser>` and `RoleService<TRole>`;
- the `AccessService` "can I, and why" endpoint;
- the distribution's client of the identity server;
- the first module package pair (`Tellma.Module.Gl.Abstractions` and `Tellma.Module.Gl`), with
  `Center` as the platform's first tree entity, and the GL sample-data provisioning step;
- the reference distribution's own feature (entities, setting keys, reports and development data
  that exercise every client path) and its tenant migrations;
- the local-development administrator bootstrap.

Everything here consumes machinery other specs define: chiefly specs 0011 to 0017, 0021, 0022 and
0033. §1.3 lists every seam it uses.

The users stack is where the platform first talks to another system on a caller's behalf. The
identity server of spec 0003 owns identities, credentials and invitation email delivery; a tenant
owns only the rows it wrote: a user's tenant-visible state (`New | Invited | Joined`), the outcome
and error of the last invitation the tenant raised, and the subject the identity server assigned.
Invitation is therefore an action with a remote call between two database round trips, never a
call inside a transaction, and delivery is a live drill-down rather than stored state.

The roles stack is where stored Queryex predicates enter the security model. The validation of a
permission's filter text and its language-version stamp are spec 0014 §3.7's
`RoleAccessRules<TRole>`, a Core component that runs on every role save whatever leaf a
distribution substitutes; `RoleService<TRole>` is the stack's service and carries no filter logic.

The GL module is the proof that a module composes: it references only Abstractions packages, ships
a non-abstract unsealed `Center` a distribution extends by plain inheritance, contributes its
entity, service, securables and provisioning step through one feature, and needs no web, MCP or
permission code of its own. Tree maintenance (id-path `hierarchyid` nodes, subtree counts, cycle
fencing) is spec 0012's emitted SQL; this spec adds only the GL business rule that a center may
have children only when its type is a grouping type.

Deliberately left to other specs: the settings edit API (`settings/save` and its patch record are
spec 0013's `SettingsService`), the inbox and notification preferences (spec 0021), Excel export
and import of users, roles and centers (spec 0019's operations project onto these stacks
unchanged), and the second GL entity.

## Goals / Non-goals

**Goals**

- Ship `IIdentityServerClient` and its runtime implementation: bulk invite (with the sandbox-only
  `ExistingOnly` flag) and bulk delivery status, chunked at 1,000, with a cached service token.
- Ship `UserService<TUser>`:
  - the `invite` and `issue-credentials` actions;
  - `invitation-status`, `me`, `me/save` (an enlisted write of the caller's own row), `me/locale`,
    `me/preferences/set`, `me/preferences/delete`, `preferences/get`, `preferences/set`,
    `preferences/delete` and `me/test-notification`;
  - the `activity` details extra;
  - the user-specific validation and preprocessing rules;
  - the `core.user.added` notice and the welcome email (§3.3);
  - the post-commit side effects (session revocation, catalog membership hint).
- Ship `UserAccessCriteria` (every connected member reads their own row) and
  `EmailNotificationChannel` (the `email` channel's `DeliverAsync`, spec 0022 §4, and test send).
- Ship `RoleService<TRole>`: the standard projection over `Role` and the `members` and `problems`
  details extras.
- Ship `AccessService` (`access/check`).
- Ship `Tellma.Module.Gl.Abstractions` (`Center`, `CenterType`, `GlModule`, `GlResources`,
  `GlValidationCodes`, `IGlSampleCenters`, `SampleCenter`, `SampleCenterName`) and
  `Tellma.Module.Gl` (`CenterService<TCenter>`, `GlFeature`, `GlSampleCentersStep`,
  `DefaultGlSampleCenters`), the `gl.Centers` table, and the `Gl` entry in `taxonomy.json`.
- Ship the reference distribution's feature (§1.4), its tenant migration set and its Development
  bootstrap: the `admin@localhost` administrator that the in-proc identity server also seeds, and
  the deployed flow in which the migrator invites a tenant's first administrator.
- Pin every budget (§8.1): two database round trips per invite, one per self-service preference
  write, one for `me/locale`, three for `me/save`, one for `me`.

**Non-goals (explicitly out of scope)**

- The `User`, `Role`, `RoleMembership`, `Permission` entities, their tables, `HasData` rows,
  `UserAccessRules`, `RoleAccessRules`, the lockout guards, the securables registry, the connect
  prologue and `ITenantBootstrapper` — spec 0014.
- `core.Settings`, `SettingKey<T>`, `SettingsService` and every settings endpoint — spec 0013.
- The batch, emitter, id allocator, tree statements, entity metadata, Queryex adapter — spec 0012.
- `EntityService`, the pipeline, `StackDescriptor` — spec 0015.
- Endpoint projection, wire records, problem details, the `MeResult` shape — spec 0016.
- `core.Blobs`, the `user-image` and `user-signature` kinds, staging and the download endpoint —
  spec 0017.
- `core.Notifications`, `core.NotificationPreferences`, `INotifier`, the channel registry and the
  `INotificationChannel` seam, `TellmaHub` — spec 0021.
- Provisioning steps as a mechanism, the migrator commands, the catalog, `taxonomy.json` itself —
  spec 0011.
- `core.OutgoingEmails`, `IEmailOutbox`, the sending job and delivery events — spec 0022.
- Report definitions, report seeds and coded-report plumbing — spec 0033.
- The pages over these stacks (Users, Roles, Centers, My profile) — spec 0032; the reference
  distribution's client — spec 0024.
- Push subscriptions, an SMS transport.

## 1. Placement and architecture

### 1.1 Projects and packages

| Piece | Location | References | Notes |
|---|---|---|---|
| Identity client contract, user-stack result records | `src/core/Tellma.Core.Abstractions/` (namespaces `Tellma.Core.Abstractions.Identity`, `.Access`) | `Tellma.Core.Queryex` only | Grown by this spec; no new package. |
| Identity client, user and role services, access service, email notification channel | `src/core/Tellma.Core/` (namespaces `Tellma.Core.Identity`, `Tellma.Core.Users`, `Tellma.Core.Access`, `Tellma.Core.Notifications`) | as spec 0012's `Tellma.Core` | `IdentityServerClient` uses `IHttpClientFactory`; nothing else new is referenced. |
| GL contracts | `src/module/gl/Tellma.Module.Gl.Abstractions/` (namespace `Tellma.Module.Gl`) | `Tellma.Core.Abstractions` | `Center`, `CenterType`, `GlModule`, `GlResources`, `GlValidationCodes`, `IGlSampleCenters`, `SampleCenter`, `SampleCenterName`. Creates `src/module/`. |
| GL runtime | `src/module/gl/Tellma.Module.Gl/` (namespace `Tellma.Module.Gl`) | `Tellma.Module.Gl.Abstractions` | `CenterService<TCenter>`, `GlFeature`, `GlSampleCentersStep`, `DefaultGlSampleCenters`, `Resources/Strings.resx`. Never references `Tellma.Core`. |
| Reference distribution | `distributions/acme/src/Tellma.Distro.Acme/`, `Tellma.Distro.Acme.Web/`, `Tellma.Distro.Acme.Migrator/` | `Tellma.Distro.Acme` gains `Tellma.Module.Gl`, `Tellma.Module.Gl.Abstractions` | `Tellma.Distro.Acme` is the composition library the Web and Migrator projects reference (spec 0011 §1.2) and holds `AcmeFeature` (§1.4); migrations live in the Migrator project (§7). |
| Tests | `test/core/Tellma.Core.Tests`, `test/core/Tellma.Core.IntegrationTests`, `test/module/gl/Tellma.Module.Gl.Tests`, `distributions/acme/test/Tellma.Distro.Acme.IntegrationTests` | | §9. |

`Tellma.Module.Gl` compiles against `EntityService<TEntity, TKey>` in Abstractions and receives the
pipeline factory through its constructor; the module never names a `Tellma.Core` type. A unit test
in `Tellma.Module.Gl.Tests` asserts the project's reference closure contains no `Tellma.Core`
package.

Contract blocks are C# sketches: names and shapes are normative; `using` directives, XML
documentation, cancellation-token parameters, method bodies and accessibility details are omitted,
so a block is never pasted into code. SQL statements are the exact shape to emit.

### 1.2 Composition

`CoreFeature` (spec 0011) registers:

- `contribution.Entity<User, UserService<User>>()` and
  `contribution.Entity<Role, RoleService<Role>>()`;
- `contribution.ApiService<AccessService>()`;
- `contribution.Singleton<IIdentityServerClient, IdentityServerClient>()`;
- `contribution.Singleton<IAccessCriteriaProvider, UserAccessCriteria>()` (§3.2);
- `contribution.DetailsContributor<User, UserActivityContributor>()` (§3.1);
- `contribution.Scoped<INotificationChannel, EmailNotificationChannel>()` (§3.5).

It also binds `IdentityServerClientOptions` from `Tellma:Identity`. The notification type
`core.user.added` (§3.3) and the `email` channel's descriptor are spec 0021's own `CoreFeature`
contributions, consumed here.

`GlFeature` (§5.5) is added by a distribution with `tellma.AddFeature<GlFeature>()`. A distribution
substitutes its leaves with `tellma.UseEntity<User, MyUser>()`, `tellma.UseEntity<Role, MyRole>()`,
`tellma.UseEntity<Center, MyCenter>()`; the platform closes `UserService<MyUser>`,
`RoleService<MyRole>`, `CenterService<MyCenter>`.

Illustration (the reference distribution's shared `AcmeComposition.Compose` after this spec, invoked
by both hosts as spec 0011 §1.3 shows):

```csharp
public static void Compose(TellmaBuilder tellma) => tellma.UseAzureDefaults()
    .AddFeature<GlFeature>().AddFeature<AcmeFeature>()            // AcmeFeature: §1.4
    .UseEntity<Center, MyCenter>().Languages(["en", "ar"]);
```

### 1.3 Seams consumed

| Seam | Owner | Members used here |
|---|---|---|
| `EntityService<TEntity, TKey>`, hooks, `[EntityAction]` with `SingleTarget`, `[EntityRef]`, `[ApiAction]`, `[ApiRoute]`, `SaveOptions`, `ActionContext` with `Update`, `Persisted` and `Host`, `SaveContext`, `PersistContext`, `PersistOutcome`, `IContextLoader`, `ValidationErrors` | spec 0015 | §1.4, §3, §4, §5 |
| `IEnlists<TTarget>`, `IOpenWriteHost`, `EnlistSaveOptions`, `EnlistedSave<T>`, `EnlistmentOutcome` | spec 0015 §13.3 | §3.5 |
| `IDetailsContributor<T>`, `DetailsPlan` (`IdsSource`, `AddExtra`, `AddComputedExtra`), `ConcurrencyMode` | spec 0015 §3.2, §5.3, §8 | §3.1, §3.5, §4 |
| `[Assignable]`, `ActionContext.Assignments`, `IAssignments.AssignAsync` | spec 0028 §3.1 | §1.4, §6.3 |
| `FilterTree.Via` | spec 0010 §F.7 | §4 |
| `IDataBatch.Sql`, `Tvp`, `OnCommitted`, `SqlOptions(Writes, ResultSets, UserIds, ForCaller)`, `BatchResult`, `BatchOutcome` | spec 0012's batch contract | §3.3, §3.5 |
| `IGuardedBatchRunner.Run`, `IUserConnector`, `ConnectedUser` | spec 0014's connect contract | §3.4, §3.5, §3.7 |
| `IAccessEvaluator`, `AccessDecision`, `AccessGrant`, `IAccessCriteriaProvider`, `AccessCriterion`, `SecurableRef`, `ISecurableRegistry`, `UserAccessRules<TUser>`, `RoleAccessRules<TRole>`, `IAdministratorDirectory`, `ITenantBootstrapper`, `WellKnownIds`, `CoreResources`, `AccessActions` | spec 0014 | §3, §4, §6 |
| `User`, `Role`, `RoleMembership`, `Permission`, `UserState`, `InviteStatus`, `UserKind`, `Gender`, `UserProfile` | spec 0014's entities | §3 |
| `MeResult`, `AccessCheckRequest`, `IdsRequest`, `IdRequest`, `IdRequest<TArguments>`, `QueryDiagnostic`, `PartialFailureException` | spec 0016's wire records and exception set | §3, §3.7, §4 |
| `[BlobReference]` on `User.ImageId` and `User.SignatureId`, kinds `user-image` and `user-signature` | spec 0017 | §3.5 |
| `INotifier.Notify`, `NotificationRequest`, `NotificationTypeDescriptor`, type `core.user.added` | spec 0021 | §1.4, §3.3 |
| `INotificationChannel`, `INotificationChannelRegistry`, `TestNotificationResult`, code `Notifications.UnknownChannel` | spec 0021 | §3.1, §3.5 |
| `TenantSettings`, `SettingKey<T>`, `ILanguageCatalog`, `ICalendarRegistry`, `IStringLocalizer`, `LocalizationContext`, `ILocalizationNegotiator`, `LocalizationScope` | spec 0013 | §1.4, §3.2, §3.3, §3.5 |
| `ITenantProvisioningStep`, `TenantProvisioningContext`, `dbo.__TellmaProvisioning`, migrator commands | spec 0011 | §5.6, §6 |
| `RequestContext`, `ITenantMembershipDirectory.RecordAsync`, `ISessionTerminationListener.TenantAccessRevokedAsync`, `Tellma:PublicOrigin` | spec 0011 | §2.1, §3.3, §3.6 |
| `ITellmaLinks.Tenant` | spec 0011 §5.10 | §3.3 |
| `ReportSeed`, `ShareAccess`, `IReportResults` | spec 0033 §2.1, §11, §13 | §1.4 |
| `IEmailSender`, `EmailAudience` | spec 0007 §3 | §3.3, §3.5 |
| `IEmailOutbox`, `EmailEnqueueRequest` | spec 0022 §3 | §3.3, §6.1 |

### 1.4 The reference distribution's feature

The reference distribution exercises every server path the client specs rely on through one
feature in `Tellma.Distro.Acme`. Everything it adds on the server is contributed there.

```csharp
// Tellma.Distro.Acme — namespace Tellma.Distro.Acme
[Requires(GlModule.FeatureName)]
public sealed class AcmeFeature : ITellmaFeature            // Name = "acme": the key of the client's acmeFeature
{
    public void Declare(FeatureDeclaration declaration);     // nothing beyond [Requires]
    public void Contribute(FeatureContribution contribution);
    // Entity<Currency>(); Entity<Customer, CustomerService>(); Entity<SalesInvoice, SalesInvoiceService>();
    // DetailsContributor<SalesInvoice, RelatedDocumentsContributor>(); ApiService<AcmeReports>();
    // SettingKeys(typeof(AcmeSettings)); ReportSeed(AcmeReportSeeds.SalesByCenter);
    // NotificationType(AcmeNotifications.InvoiceAssigned);
    // Singleton<IGlSampleCenters, AcmeSampleCenters>(); ProvisioningStep<AcmeSampleDataStep>();
    // Model<…>() once per entity configuration
}

public class MyCenter : Center { public int? ManagerId { get; set; } }   // FK_Centers_ManagerId -> core.Users; navigation Manager
```

| Entity | Queryex name | Declared as | Exercises |
|---|---|---|---|
| `Currency` | `acme.Currency` | `[Cacheable]` | a cacheable list (spec 0025 §3.3) |
| `Customer` | `acme.Customer` | activatable | an enum, a decimal, a cacheable navigation, an avatar, a child grid |
| `SalesInvoice` | `acme.SalesInvoice` | `[DisplayNameSource(nameof(SerialNumber))]`, `[Assignable]` | a scalar display name, the child query surface `acme.SalesInvoiceLine` (spec 0015 §2.2), file attachments, assignments (spec 0028) |
| `MyCenter` | `gl.Center` | a leaf of `Center` | a leaf extending a pack entity |

Their members beyond the base:

- **`Currency`:** `Code` (natural key), the `Name` group.
- **`Customer`:** `Code` (natural key, prefix search), the `Name` group, `CustomerType` (`Retail`,
  `Corporate`, `Government`), `CurrencyId` → `acme.Currency`, `CreditLimit` (`decimal(19,4)`),
  `ImageId` (`[BlobReference("customer-image", BlobPreset.Avatar)]`). Child `Contacts`
  (`acme.CustomerContact`: `Name`, `Email`, `Mobile`).
- **`SalesInvoice`:** `SerialNumber` (natural key), `PostingDate` (`date`), `CustomerId` →
  `acme.Customer`, `IsClosed` and `IsPosted` (server-owned). Child `Lines`
  (`acme.SalesInvoiceLine`, parent key `InvoiceId`: `CenterId` → `gl.Center`, `Description`,
  `Amount`). Child `Attachments` (`acme.SalesInvoiceAttachment`: `FileId`,
  `[BlobReference("invoice-attachment")]`).
- **`MyCenter`:** `ManagerId` → `core.User` (the sketch above).

Its actions, extras, reports, data and settings:

- **`set-credit-limit`.** `CustomerService` declares `[EntityAction("set-credit-limit")]` with
  `SetCreditLimitArguments(decimal CreditLimit)` and writes `CreditLimit` through `context.Update`
  (spec 0015 §11.2): an action with arguments.
- **`close`.** `SalesInvoiceService` declares `[EntityAction("close")]`, which takes no arguments
  and sets `IsClosed`.
- **`assign` and `post`.** `SalesInvoiceService` declares both `[EntityAction]`s as spec 0028 §3.5
  shows. `assign` takes `AssignInvoiceArguments`, whose assignee carries `[EntityRef("core.User")]`
  (spec 0015 §11.2), and hands each invoice on. `post` takes no arguments, sets `IsPosted` and
  completes the open assignment.
- **`acme.invoice.assigned`.** `AcmeFeature` registers this channel-only notification type
  (category `sales`, spec 0028 §3.6). `assign` raises it for the assignee.
- **`related-documents`.** `RelatedDocumentsContributor` answers the details extra of that name: the
  same customer's other invoices (`Id`, `SerialNumber`, `PostingDate`), latest first, at most 10.
- **Reports.** `AcmeReports` is spec 0033 §11's coded report `acme-reports/aging` under the
  securable `acme.AgingReport × Read`. The seed `acme.sales-by-center` (spec 0033 §13) is a pivot
  over `acme.SalesInvoiceLine` by the `Center` tree, published with `View` access to the role
  `Sales`.
- **Development data.** `AcmeSampleCenters` and `AcmeSampleDataStep`, which also writes demo
  invoices and assignments (§6.3).
- **Settings.** `AcmeSettings` declares the keys below (spec 0013 §5.3). `Strings.resx` and
  `Strings.ar.resx` carry their labels and descriptions, the `Acme_Settings_*` keys of the
  invoicing tab (spec 0032 §7.5), and the labels of every entity, member and action (spec 0013
  §10.1).

| Setting key | Type | Default | Category | Visibility |
|---|---|---|---|---|
| `acme.invoicing.prefix` | `string` | `"INV-"` | `invoicing` | `Client` |
| `acme.invoicing.padding` | `int` | `5` | `invoicing` | `Server` |
| `acme.sales.defaultCurrencyId` | `int?`, references `Currency` | `null` | `sales` | `Server` |
| `acme.sales.allowedCustomerTypes` | `CustomerType[]` | all three | `sales` | `Server` |
| `acme.integration.marminApiKey` | `string?` | `null` | `integration` | `Secret` |

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
| `IdentityInvitation.ExistingOnly` | Get-by-email that never creates a user and never sends mail, with three outcomes (spec 0010 §A.7): an existing user who holds a credential answers `Status = Active` with their subject; an existing user without one answers the per-user error `existing_user_has_no_credential`; an unknown address answers `unknown_email`. A spec 0010 Part A amendment (`existingOnly` per invitation); set by `UserService` on sandbox tenants (§3.3). |
| `IdentityInvitationResult.Status` | Spec 0014's `InviteStatus`: `Invited` (new identity, link queued), `Reinvited` (existing credential-less identity re-linked, link queued), `Active` (credential exists; no email sent; the tenant notifies the user itself). |
| `GetInvitationDeliveryAsync` | Delivery state of the latest invitation this client raised per subject (spec 0003's bulk delivery-status API), chunked at 1,000, input order. A subject invited by another client, or whose invite returned `Active`, is `NotFound`. |
| `CreateServiceAccountAsync` | Registers a confidential `client_credentials` client at the identity server (spec 0003 §10.2): one call, `DisplayName` the row's `Name`, `Resources = [Tellma:PublicOrigin]`, the audience of spec 0011's bearer surface (the MCP audience is per tenant and spec 0016's to add). The secret is returned once and never logged. |
| `DeleteServiceAccountAsync` | Deletes a client this distribution created (ownership-scoped at the server); a `404` is success. |
| `IdentityServerClientOptions` | `Authority` must be absolute; `https` except in Development; `ServiceClientSecret` non-empty. `ClientId` is `<slug>-svc` from the deployment identity (spec 0011 §5.7), set by the platform after binding. The same section binds spec 0011's OIDC options; the migrator host binds it too (§6.2). |

### 2.2 Transport and token

- **HTTP.** A named client `Tellma.Identity` from `IHttpClientFactory`; base address `Authority`;
  per-call timeout 30 s; `Accept: application/json`; a `User-Agent` of `Tellma.Core/<version>`.
  The routes are spec 0010 §A.8's `v1` routes. Request and response bodies are the shapes spec 0003
  documents for the three APIs plus the `existingOnly` member; JSON through the platform's
  source-generated options.
- **Token.** `client_credentials` at `Authority`'s token endpoint with `scope=tellma_identity` and
  no `resource` parameter: the identity server sets `aud` to that scope's fixed platform audience
  (spec 0003 §6.2), the one its management API accepts; the client is authenticated with
  `ClientId`/`ServiceClientSecret`. The token is cached process-wide until 60 s before its expiry
  under a single-flight lock (concurrent callers await one fetch). One `401` on an API call
  discards the cached token, fetches once and retries the call once.
- **Failures.** Before any chunk completed, a failure raises by its cause. The identity server
  unreachable — a transport failure or a `5xx` from the token endpoint or the API — is
  `DependencyUnavailableException` (503, `Retry-After: 1`). The identity server refusing the
  distribution — a `4xx` from the token endpoint (`invalid_client`, `invalid_scope`), a second
  `401`, any other `4xx` from the API — is `InvalidOperationException` (a 500 with a trace id: the
  distribution's credentials or registration are wrong, not the caller's request, and no retry
  fixes them). The identity server's `401` never reaches the caller, whose own session it does not
  concern. After at least one chunk completed, any failure returns the prefix and the caller
  decides (§3.3). Every call is logged at Information as
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
public class UserService<TUser> : EntityService<TUser>, IEnlists<TUser> where TUser : User
{
    public Task<IReadOnlyList<InviteResult>> InviteAsync(ActionContext<TUser, int> context);
    public Task<CredentialsResult> IssueCredentialsAsync(ActionContext<TUser, int> context);
    public Task<IReadOnlyList<InvitationStatus>> GetInvitationStatusAsync(IdsRequest request);
    public Task<MeResult> MeAsync();
    public Task<MeResult> SaveMeAsync(MeSaveRequest request);
    public Task<MeLocaleResult> SetMyLocaleAsync(MeLocaleRequest request);
    public Task<PreferencesResult> SetMyPreferencesAsync(IReadOnlyList<KeyValueItem> items);
    public Task<PreferencesResult> DeleteMyPreferencesAsync(IReadOnlyList<string> keys);
    public Task<UserPreferencesResult> GetPreferencesAsync(UserPreferencesRequest request);
    public Task<UserPreferencesResult> SetPreferencesAsync(ActionContext<TUser, int> context, UserPreferencesArguments arguments);
    public Task<UserPreferencesResult> DeletePreferencesAsync(ActionContext<TUser, int> context, UserPreferenceKeysArguments arguments);
    public Task<TestNotificationResult> SendTestNotificationAsync(string channel);
}

public sealed class UserAccessCriteria : IAccessCriteriaProvider;   // Resource = core.User; one criterion: ("Read", Leaf("Id = me()"), "self")
public sealed class EmailNotificationChannel : INotificationChannel;   // Tellma.Core.Notifications; Key = "email"; DeliverAsync as spec 0022 §4 describes; SendTestAsync through IEmailSender
public sealed class UserActivityContributor : IDetailsContributor<User>;   // Extras = ["activity"]

// Tellma.Core.Abstractions.Access — results and requests
public sealed record InviteResult(int Id, InviteStatus? Status, string? Error);
public sealed record CredentialsResult(int Id, string ClientId, string ClientSecret);
public sealed record UserPreferencesRequest(int UserId);
public sealed record UserPreferencesArguments(IReadOnlyList<KeyValueItem> Items);
public sealed record UserPreferenceKeysArguments(IReadOnlyList<string> Keys);

public sealed record MeSaveRequest(
    string Name, string? Name2, string? Name3, int? ImageId, int? SignatureId,
    string? PreferredLanguage, string? PreferredCalendar, string? PreferredTimeZone, Gender? Gender,
    string? ContactEmail, string? ContactMobile, DateTimeOffset ModifiedAt,
    ConcurrencyMode Concurrency = ConcurrencyMode.Check);   // the self-editable members, the expected stamp, and Override after the user chose Overwrite

public sealed record MeLocaleRequest(string? Language, string? Calendar);   // null member = unchanged; at least one is set
public sealed record MeLocaleResult(
    string? PreferredLanguage, string? PreferredCalendar, DateTimeOffset ModifiedAt, string PreferencesTag);   // the stored values, the row's stamp, the caller's tag

public sealed record InvitationStatus(
    int Id, UserState State, InviteStatus? InviteStatus, string? LastInviteError,
    IdentityDeliveryStatus? Delivery);

public sealed record KeyValueItem(string Key, string Value);

public sealed record PreferencesResult(IReadOnlyDictionary<string, string> Preferences, string PreferencesTag);
public sealed record UserPreferencesResult(IReadOnlyDictionary<string, string> Preferences);   // another user's bag, for the administrative operations

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
| `Invite` | `[EntityAction("invite", Description = "Invite users through the identity server")]`; securable action `Invite` (`core.User × Invite`, filtered, sensitive by spec 0014's default); `POST /{tenantId}/api/web/users/invite` with `IdsRequest`, answering the method's `list<InviteResult>` (an action with its own result, spec 0015 §3.1). |
| `IssueCredentials` | `[EntityAction("issue-credentials", Action = "Credentials", SingleTarget = true, Description = "Issue or rotate a service account's client credentials")]`; securable action `Credentials` (`core.User × Credentials`, filtered, sensitive by spec 0014's default); `POST /{tenantId}/api/web/users/issue-credentials` with `IdRequest`, answering the method's `CredentialsResult` (a single-target action with its own result, spec 0015 §2.3, §3.1). |
| `GetInvitationStatus` | `[ApiAction("invitation-status", Action = "Read", Idempotent = true, Mutation = false)]`; `users/invitation-status`. |
| `Me` | `[ApiAction("me", MemberOnly = true, Idempotent = true, Mutation = false)]`; `users/me`. |
| `SaveMe` | `[ApiAction("me/save", MemberOnly = true)]`; `users/me/save`; body `MeSaveRequest`, answering `MeResult`. |
| `SetMyLocale` | `[ApiAction("me/locale", MemberOnly = true)]`; `users/me/locale`; body `MeLocaleRequest`, answering `MeLocaleResult`. |
| `SetMyPreferences`, `DeleteMyPreferences` | `[ApiAction("me/preferences/set", MemberOnly = true)]`, `[ApiAction("me/preferences/delete", MemberOnly = true)]`. |
| `GetPreferences` | `[ApiAction("preferences/get", Action = "Preferences", Idempotent = true, Mutation = false)]`; `users/preferences/get`; body `{ userId }`, answering `UserPreferencesResult`. |
| `SetPreferences`, `DeletePreferences` | `[EntityAction("preferences/set", Action = "Preferences", SingleTarget = true)]`, `[EntityAction("preferences/delete", Action = "Preferences", SingleTarget = true)]`; `users/preferences/set`, `users/preferences/delete`, bodies `IdRequest<UserPreferencesArguments>` and `IdRequest<UserPreferenceKeysArguments>`, answering the method's `UserPreferencesResult`. |
| `SendTestNotification` | `[ApiAction("me/test-notification", MemberOnly = true)]`; `users/me/test-notification`; body `{ channel }`, a channel key of spec 0021 §4.1's registry, answering `TestNotificationResult`. |

Every other operation (`query`, `get`, `get-by-ids`, `save`, `delete`, `delete-by-query`,
`activate`, `deactivate`, the Excel operations of spec 0019 §1.2) is the standard projection of
spec 0015 over the stack descriptor; this service adds hooks, never operations. Securables:
`core.User × Read | Save | Delete | Activate | Invite | Preferences | Credentials`, all with
`FilterRoot = core.User`, registered by the stack feature; `Preferences` is not sensitive.

The details extra `activity` feeds the "Last active" column of the Users page (spec 0032 §4).
`UserActivityContributor` produces it, and `CoreFeature` registers it (§1.2). Its statement joins
the plan's `IdsSource` (spec 0015 §5.3):

```sql
SELECT s.[UserId], s.[LastActiveAt] FROM [core].[UserStamps] AS s JOIN {IdsSource} AS k ON k.[Id] = s.[UserId];
```

### 3.2 Hooks

- **Own-row read.** `UserAccessCriteria` (§3.1) supplies `Read` with `Id = me()`, so every connected
  member reads their own row with no role grant; the evaluator disjoins it with stored grants
  (spec 0014 §5.1). No criterion covers `Save`: the admin `save` is administrative, a member holding
  no `Save` grant is refused on their own row like any other, and self-service writes go through
  `me/save` (§3.5).
- **`PreprocessAsync`.** `Email`, `ContactEmail`: trim, lower-case the domain and the local part
  (the identity server compares case-insensitively), `null` when empty. `Name`, `Name2`, `Name3`,
  `ContactMobile`: trim. `PreferredLanguage`: canonical BCP 47 casing, extensions stripped.
  `PreferredCalendar`: lower-case.
- **`ValidateAsync`** (before spec 0014's `UserAccessRules<TUser>`, which owns escalation,
  self-lockout, the email lock, the `Kind`/`Email` pair and the delete policy):

| Rule | Code (path) |
|---|---|
| `PreferredLanguage` is `null` or `ILanguageCatalog.IsOffered` | `Users.LanguageNotOffered` (`PreferredLanguage`) |
| `PreferredCalendar` is `null` or in `ICalendarRegistry.Codes` | `Users.CalendarUnknown` (`PreferredCalendar`) |
| `PreferredTimeZone` is `null` or resolvable by `TimeZoneInfo.TryFindSystemTimeZoneById` | `Users.TimeZoneUnknown` (`PreferredTimeZone`) |
| `ContactEmail` is `null` or a valid address (`MailAddress` parse, one `@`) | `Users.ContactEmailInvalid` (`ContactEmail`) |
| `ContactMobile` is `null` or E.164 (`^\+[1-9][0-9]{6,14}$`) | `Users.MobileInvalid` (`ContactMobile`) |
| A `me/locale` request sets `Language`, `Calendar` or both | `Users.LocaleEmpty` (`language`) |

`me/locale` (§3.5) applies three of these rows in memory: the `PreferredLanguage` row, the
`PreferredCalendar` row and the last row. It reports them at the paths `language` and `calendar`.

- **`ValidateActionAsync("invite")`** per target: `Kind = Individual` else `Users.NotIndividual`;
  `IsActive = 1` else `Users.Inactive`; `State ≠ Joined` else `Users.AlreadyJoined`. Rows the
  caller cannot see under the `Invite` grant are absent from `context.Entities` and are
  `Entity.NotFound` by the pipeline's rule.
- **`ValidateActionAsync("issue-credentials")`** per §3.8.
- **`AfterCommitAsync`** and the action `OnCommitted` callbacks: §3.6; the hook tells `activate`
  from `deactivate` by `ActionPersistOutcome<TUser>.Action` (spec 0015 §13.2).

### 3.3 The `invite` action

The action runs in three phases across two round trips; no transaction is open during the remote
call.

1. **Load and validate (RT1, the action's load round trip).** The pipeline loads the target rows
   under the caller's `Invite` grant and runs `ValidateActionAsync`. Every row that survives is
   invited; a single invalid row fails the whole action (422), because the identity call has not
   happened yet and the admin fixes the list.
2. **Remote call (no batch in flight).** One `IdentityInvitation` per row:
   - `Email`;
   - `Locale = PreferredLanguage ?? TenantSettings.Languages[0].Code`;
   - `DisplayName`: the name in `Locale`'s content language, so the invitation email greets the
     user in the language it is written in. That is the column at the position of the tenant
     language whose language subtag equals `Locale`'s, else `Name`, with spec 0013 §10.3's
     fallback (`Name2 ?? Name`, `Name3 ?? Name`) — the rule spec 0013 §9.2 uses for
     `ContentLanguageIndex`;
   - `Gender`;
   - `ReturnUrl = ITellmaLinks.Tenant(tenantId)` (spec 0011 §5.10);
   - `ExistingOnly = RequestContext.IsSandbox`.

   A sandbox tenant never causes the identity server to email anyone. A result whose `Error` is
   `unknown_email` or `existing_user_has_no_credential` (spec 0010 §A.7) is the per-row error
   `Users.SandboxRequiresExistingIdentity`: a row in the result, not a 422. That code, not the
   server's text, is what `LastInviteError` records — the one exception to §2.1's verbatim rule.
3. **Write-back (RT2, the action's persist batch).** The results are loaded into
   `@tb{b}_t0 : UserInvitationOutcomeList` (one row per invited id; `Subject`, `InviteStatus`,
   `LastInviteError` from the result) beside `@tb{b}_t1 : IdList` holding the same ids, and one
   statement is appended through `context.Batch.Sql` with `SqlOptions(Writes = { core.Users },
   UserIds = @tb{b}_t1, ResultSets = 1)` so the epilogue bumps each invited user's
   `PreferencesTag` (the `[BumpsUserVersionTag(Preferences, "Id")]` rule on `core.Users`):

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
     0012's unique mapping → `ValidationException` with `Unique` at `Subject`.
   - `ModifiedAt` is stamped: an admin's stale details page gets a concurrency error on its next
     save, which is the right signal; the statement ignores the stamp it finds (an invite outcome
     never conflicts semantically with a rename).
   - For every result with a `Status` (`Invited`, `Reinvited` or `Active`) whose loaded row holds no
     `Subject` or the returned one, the action calls `context.Notify` with a `NotificationRequest`
     of type `core.user.added` to `[id]`, with arguments `{ tenantName, actorName }`, no target,
     `ActorUserId = caller` and `DedupKey = "core.user.added"` (the descriptor is spec 0021's;
     `actorName` is the caller's `Name`; an invited user reads the notice on their first sign-in).
   - For the same users the action enqueues the welcome email, the one message a user keeps to find
     the distribution again, through one `IEmailOutbox.EnqueueAsync(context.Host, …)` call (spec
     0022 §3), so its rows commit with the write-back. Each user gets one `EmailEnqueueRequest`:
     `To` the single address `ContactEmail ?? Email`, `Audience = Internal`, `RecipientUserId` the
     user, `RegardingEntity = "core.User"` and `RegardingId` the user's id. Template `UserAdded`
     renders per user inside spec 0013 §10.2's `LocalizationScope` entered with the user's
     `LocalizationContext`, which `ILocalizationNegotiator` derives from the loaded row's
     `Preferred*` columns and the tenant's settings, ignoring the caller's headers. The subject is
     "You have been added to {TenantName}"; the body names the actor and the tenant and links the
     tenant (`ITellmaLinks.Tenant(tenantId)`, the invitation's `ReturnUrl`). For `Invited` and
     `Reinvited` the body adds that a separate message from the identity server carries the link
     that sets up their sign-in, to be used before the distribution link; it never says that message
     has arrived, because the identity server queues its link and the outbox sends this one after
     the commit, so either can land first. On a sandbox tenant `ExistingOnly` yields `Active` alone,
     and spec 0007's routing marks the email when the outbox sends it (spec 0022 §7.2).
   - A mismatch only the statement detects (a concurrent change between the round trips) keeps the
     notice and the email already composed for it.
   - The action registers through `context.Batch.OnCommitted` a callback that calls
     `ITenantMembershipDirectory.RecordAsync` with `(TenantId, Subject, IsActive)` for every row
     that gained a subject (§3.6).
4. **Result.** The method awaits `context.Persisted` (spec 0015 §11.2) and reads the second result
   set, then answers `list<InviteResult>` in request order: `Status` and `Error` from the identity
   result, `Users.SubjectMismatch` for mismatched rows. `tellma.users.invites` counts one per id
   with `outcome` ∈ `Invited | Reinvited | Active | Error | SubjectMismatch | NotAttempted`.

**Partial failure.** When the client returns a prefix (§2.3), the action composes the write-back
for the prefix only and throws `PartialFailureException("partial-failure", Results = the
prefix's InviteResults, Failed = the ids not attempted, as strings)`. The pipeline executes the
persist batch the action composed before the exception surfaces (spec 0015's partial-failure rule
for actions), so subjects that exist on the identity server are recorded; the web layer answers
502 `partial-failure` with `errorDetails.results` (the prefix) and `errorDetails.failed[]` (the
not-attempted ids), and the admin re-invites `failed`. Re-running the action never re-issues the
remote call before the load round trip has succeeded under fresh permissions: a stale-context
recompose (spec 0014's guard) happens in RT1, before phase 2.

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

- **`Me()`** returns spec 0016's `MeResult` in one round trip. It is built from:
  - `ConnectedUser`, for the profile;
  - `IAccessEvaluator.EvaluateAllAsync()`, for `AccessSummary`;
  - the tenant tags from `BatchOutcome.VersionTags` and the caller's `PreferencesTag` from
    `BatchOutcome.UserVersionTags` (spec 0013 §2.5), as wire tags;
  - the caller's preference bag, read by one `Read` batch that is also the cold prologue's round
    trip (`SELECT [Key], [Value] FROM [core].[UserPreferences] WHERE [UserId] = @tm_UserId`).
- **`SaveMe(MeSaveRequest)`.** Three round trips:
  1. **Load.** One `Read` batch through `IGuardedBatchRunner` loads the caller's row by id — every
     mapped column of the leaf, no children — and the caller's bag
     (`SELECT [Key], [Value] FROM [core].[UserPreferences] WHERE [UserId] = @tm_UserId`). A
     `request.ModifiedAt` that differs from the row's stamp is `ConcurrencyException` (409) with
     nothing written, unless `request.Concurrency = Override`, which skips the comparison.
  2. **Enlist and validate.** The method copies the request's eleven self-editable members onto the
     loaded row and leaves `RoleMemberships` null. It enlists the row with the invoker's frame:
     `EnlistSaveAsync` into the injected `IOpenWriteHost`, with `EnlistSaveOptions.Concurrency`
     equal to `request.Concurrency` (`Check` on the loaded stamp, or `Override`). A `MapPath` drops
     the row index, so `Users.LanguageNotOffered` lands at `preferredLanguage` and
     `Blob.NotAttachable` at `imageId`. The host's `PersistAsync` runs the validation round: the
     before image, spec 0017's blob loads, the §3.2 rules and spec 0014's `UserAccessRules<TUser>`.
  3. **Persist.** The epilogue bumps the caller's `PreferencesTag` through the entity's rule. Under
     `Check`, a stale stamp that slipped the first check conflicts here (409).

  The result is `MeResult` composed as `Me` composes it: the profile from the saved row
  (`EnlistedSave.Rows[0]`, stamped), the bag from the first batch, `AccessSummary` from
  `EvaluateAllAsync` over the cached set, and the tenant tags and the caller's `PreferencesTag`
  from `EnlistmentOutcome` (spec 0015 §13.3). The self-editable set is `MeSaveRequest`'s member
  list.
- **`SetMyLocale(MeLocaleRequest)`.** The session menu's language and calendar switches (spec 0027
  §10.1) call it instead of `me/save`. One round trip:
  1. **Validation in memory** (§3.2), 422 on failure. A calendar outside the tenant's pair is
     accepted; negotiation ignores it (spec 0013 §8.2).
  2. **One `IGuardedBatchRunner.Run(BatchPurpose.Persist, …)`** carrying, in order:
     - spec 0014's `ContributeLock`;
     - spec 0012 §9.1's `Update<TUser>` by the caller's id, assigning only the members that are
       set, stamped;
     - `ContributeInvariants`;
     - `SELECT [PreferredLanguage], [PreferredCalendar], [ModifiedAt] FROM [core].[Users] WHERE
       [Id] = @tm_UserId`.
  3. **Tags.** The entity's rule bumps the caller's `PreferencesTag` in the epilogue (spec 0014
     §8.1). A value equal to the stored one skips the row, so no history row is written and neither
     the stamp nor the tag moves.
  4. **Result.** `MeLocaleResult` carries the stored values, the stamp and the caller's tag, which
     the calling tab adopts (spec 0025 §9.5). A read-only tenant answers 403 `tenant-read-only`.
- **`SetMyPreferences(items)` / `DeleteMyPreferences(keys)`.** One round trip.
  - **Validation in memory.** A key matches `^[a-z][a-zA-Z0-9]*(\.[a-zA-Z0-9][a-zA-Z0-9-]*)*$` and
    is at most 128 characters (`Users.PreferenceKeyInvalid`, path `items[i].key`). A value is at
    most spec 0014's `MaxPreferenceValueBytes` of UTF-8 (`Users.PreferenceValueTooLarge`).
    Duplicate keys in one request are `Users.PreferenceKeyDuplicate`. **Example —**
    `report.acme-aging.params` passes; `Report.aging` and `report.-aging` fail.
  - **The statement.** One `IGuardedBatchRunner.Run(BatchPurpose.Persist, …)` executes spec 0014's
    `core.UserPreferences` statement — the `UPDATE` + `INSERT` halves for `set`, the `DELETE` half
    for `delete` — with `@tb{b}_t0 : UserPreferenceList` and
    `SqlOptions.ForCaller({ core.UserPreferences })`. It is followed by
    `SELECT [Key], [Value] FROM [core].[UserPreferences] WHERE [UserId] = @tm_UserId`.
  - **The key cap.** A `set` that would leave more than spec 0014's `MaxPreferenceKeys` keys is
    `Users.TooManyPreferenceKeys`. It is asserted inside the same statement, after the `INSERT`,
    with the cap bound as `@tb{b}_p1`:
    `IF (SELECT COUNT(*) FROM [core].[UserPreferences] WHERE [UserId] = @tm_UserId) > @tb{b}_p1
    THROW 50422, N'Users.TooManyPreferenceKeys', 1;`.
  - **The result** carries the bag and the caller's new `PreferencesTag` from
    `BatchOutcome.UserVersionTags` (spec 0013 §2.5) as a wire tag.
- **`SendTestNotification(channel)`.** `inbox` is `Users.ChannelNotTestable` at `channel` (422); a
  key the registry does not hold is spec 0021 §4.3's `Notifications.UnknownChannel` at `channel`
  (422); a registered channel with no `INotificationChannel` answers `NotConfigured`; any other
  channel answers its `SendTestAsync` (spec 0021 §4.4). `EmailNotificationChannel` sends to
  `ContactEmail ?? Email` from `ConnectedUser.Profile` (zero round trips) through one
  `IEmailSender.SendAsync` (audience `Internal`, template `TestNotification` rendered under the
  caller's `PreferredLanguage` and `PreferredCalendar` with the current tenant time).
  `tellma.users.test_notifications` counts by `channel` (the key) and `outcome`.
- **`GetPreferences(UserPreferencesRequest)`, `SetPreferences`, `DeletePreferences`.** The path for
  a caller holding `core.User × Preferences` (spec 0014 §3.4): `get` runs one `Read` batch loading
  the target's `Id` under the grant's filter (an id the filter hides is `NotFoundException`) and
  the target's rows from `core.UserPreferences`, answered as `UserPreferencesResult`; `set` and
  `delete` are single-target entity actions (spec 0015 §2.3) whose RT1 loads the target under the
  same grant. The method appends the bag statement's halves and the trailing `SELECT [Key], [Value]`
  for the target through `context.Batch` (`Writes = { core.UserPreferences }`, `SqlOptions.UserIds`
  naming the one-row `IdList` holding the target's id, `ResultSets = 1`), so RT2's epilogue bumps
  the target's `PreferencesTag`; it awaits `context.Persisted` (spec 0015 §11.2) and answers
  `UserPreferencesResult` from that result set. The key grammar, the caps and the per-request checks
  apply as for `me/preferences/set` (spec 0014 §3.4 for what the server does and does not enforce).

### 3.6 Post-commit side effects

| Event | Effect (post-commit; failures logged, never thrown) |
|---|---|
| A user deactivated (the `deactivate` action; `IsActive` is server-owned, so a save never flips it) | `ISessionTerminationListener.TenantAccessRevokedAsync(tenantId, subject)` for each row with a subject; `ITenantMembershipDirectory.RecordAsync([(tenantId, subject, false)])`; for a `Service` row, `DeleteServiceAccountAsync(subject)` (§3.8), so the client obtains no token; reactivation needs `issue-credentials` again. The BFF cookie is untouched; the connect prologue refuses the user on their next request regardless. |
| A user activated | `RecordAsync([(tenantId, subject, true)])` for rows with a subject. |
| A user gained a subject (§3.3, §3.8) | `RecordAsync([(tenantId, subject, isActive)])`. |
| A user deleted | Only `State = New` rows are deletable (spec 0014's `Users.OnlyNewUsersDeletable`); such rows have no subject, so no listener and no identity call. |

The hint table is navigation only (spec 0011 §3.8); a missed call is repaired by
`MembershipReconcileService` (spec 0011's hosted timer, daily by default), never by the request
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
`IAccessEvaluator.RequireAsync("core.Role", "Read")` (effective grants are role-editor information)
and one `Read` round trip asserting the target row is visible under the caller's `core.User × Read`
decision (absent → `NotFoundException`); then `EvaluateForAsync(userId, resource, action)` per
securable, in request order, over the one `ForUser` load the scope memoises (spec 0014 §5.1): two
round trips. An unknown securable is `Denied` with `AccessProblemCode.UnknownResource` or
`UnknownAction`, never a 400. At most 200 securables per request (`LimitExceededException`).

### 3.8 The `issue-credentials` action

A `SingleTarget` action (spec 0015 §2.3) in three phases across two round trips, with no transaction
open during the remote call, as for `invite`:

1. **Load and validate (RT1).** The pipeline loads the row under the caller's `Credentials` grant;
   `ValidateActionAsync("issue-credentials")`: `Kind = Service` else `Users.NotService`;
   `IsActive = 1` else `Users.Inactive`.
2. **Remote call.** `CreateServiceAccountAsync(Name)`. A failure raises per §2.2 with nothing
   written.
3. **Write-back (RT2).** `context.Update` (spec 0015 §11.2) with `Subject = clientId` and, when the
   loaded row's `JoinedAt` is null, `JoinedAt = RequestContext.Now`: one `UpdateSpec<TUser>.ByIds`
   over the target id under the grant's filter, stamped by the emitter, its `PreferencesTag` bump
   from the entity's rule.

   `OnCommitted`: `ITenantMembershipDirectory.RecordAsync([(tenantId, clientId, true)])` and, when
   the row held a previous subject, `DeleteServiceAccountAsync(previous)`: the old client dies
   after the new one is recorded, so a failure between the two leaves working credentials and the
   action is simply re-run; a delete failure is logged (`ServiceAccountDeleteFailed(userId)`), and
   the identity server's ownership-scoped delete is the operator's path. The method awaits
   `context.Persisted` (spec 0015 §11.2): a faulted persist deletes the new client, best effort,
   before the exception surfaces; an orphaned client is harmless (spec 0014 §7.7).
4. **Result.** One `CredentialsResult(Id, ClientId, ClientSecret)`: the secret crosses once, in this
   response, and is never logged or stored; the SPA shows it once. The call is tagged
   `identity.endpoint = service-account`.

## 4. The roles stack

```csharp
// Tellma.Core.Users (runtime)
public class RoleService<TRole> : EntityService<TRole> where TRole : Role;
```

`RoleService` adds no operations: `query`, `get`, `get-by-ids`, `save`, `delete`, `delete-by-query`,
`activate`, `deactivate` and the Excel operations of spec 0019 §1.2 are the standard projection;
securables `core.Role × Read | Save | Delete | Activate` (`FilterRoot = core.Role`). It declares
two details extras, `members` and `problems` (§4.3). `RoleMemberships` is a child of `User` only,
so adding members is a bulk user save. Spec 0014's `RoleAccessRules<TRole>` owns securable
existence, wildcard, public-role, `Administrator`-immutability and escalation rules, and the filter
text (§4.1).

### 4.1 Filter validation and the language-version stamp

Both are spec 0014 §3.7's, run by `RoleAccessRules<TRole>` in the validation round of every role
save on any `TRole` leaf; RT1 primes the before images the stamp rule reads.

### 4.2 Persist and tags

A role save writes `core.Roles` and `core.Permissions`, both `[BumpsVersionTag("permissions")]`;
the executor's epilogue bumps the tenant `permissions` tag and every caller's cached set
invalidates at its next prologue. No per-member `PermissionsTag` bump is needed: the tenant tag
covers every role-side change, and the user-level tag is bumped only by membership writes (spec
0014). `activate`/`deactivate` on a role are the standard actions; spec 0014's `IAccessGuards`
appends the lockout invariants to every persist that touches a security table, actions and
deletes included. Round trips: save, delete, activate and deactivate 2 each — the before images
and `RoleAccessRules`'s loads in RT1, the persist in RT2 (spec 0014 §7.5).

### 4.3 The `members` and `problems` extras

The role page reads its members and its drifted permissions with the details read, in the same
round trip (spec 0015 §5.3); neither needs a route of its own.

```csharp
// Tellma.Core.Abstractions.Access — one row of the problems extra; RoleService<TRole>.Extras = ["members", "problems"]
public sealed record PermissionProblem(int PermissionId, AccessProblemCode Code, IReadOnlyList<QueryDiagnostic> Diagnostics);
```

- **`members`.** One `AddExtra` query (spec 0015 §5.3) over `core.RoleMembership`, restricted to
  the plan's `IdsSource` on `RoleId`. The service interpolates the identifier and never spells its
  name. It projects `UserId, User.Name, User.Name2, User.Name3, User.Email, User.ImageId,
  User.IsActive, User.State, Notes`, ordered by `User.Name`, at most 500 rows.
- **The members filter.** The query's filter is the caller's `core.User × Read` decision rebased
  through `User` (`FilterTree.Via`, spec 0014 §5.5), so a filtered grant reveals no hidden user.
  When that decision is `Denied`, the service adds no statement and the extra is absent.
- **`problems`.** The role's drifted permissions, in permission order. A permission drifts when it
  meets one of spec 0014 §9's five conditions, checked against the registry and the tenant's
  current schema (`QueryexEngine.Validate` under `AccessLimits` and the row's stamp). `*`/`*`
  never drifts. The role editor renders the extra as a banner and row markers (spec 0032 §5.5).
- **No statement for `problems`.** It is a computed extra (`DetailsPlan.AddComputedExtra`, spec
  0015 §5.3) over the `Permissions` collection the details read already loads.

## 5. The GL module

### 5.1 Packages

- **`Tellma.Module.Gl.Abstractions`** (`src/module/gl/Tellma.Module.Gl.Abstractions/`, namespace
  `Tellma.Module.Gl`, references `Tellma.Core.Abstractions`) holds the entity, the enum, the
  resource constant, the sample-data seam (`IGlSampleCenters`, `SampleCenter`, `SampleCenterName`,
  §5.6), `GlModule.FeatureName` (what a dependent's `[Requires]` names, spec 0011 §2.1) and the
  validation codes. A distribution's own modules must reference `gl.Centers` without `Tellma.Core`.
- **`Tellma.Module.Gl`** (`src/module/gl/Tellma.Module.Gl/`, same namespace, references its
  Abstractions) holds the service, the feature, the provisioning step, `DefaultGlSampleCenters` and
  `Resources/Strings.resx`.
- **`Strings.resx`** has the base name `Tellma.Module.Gl.Resources.Strings` and follows the key
  grammar of spec 0013 §10.1, in English and Arabic. Its keys: `Gl_Center`, `Gl_Center_Plural`,
  `Gl_Center_Name`, `Gl_Center_Code`, `Gl_Center_CenterType`, `Gl_Center_ParentId`,
  `Gl_Center_IsActive`, `Gl_Center_CodeHint` (the Code field's hint), `Gl_CenterType_Abstract` …
  `Gl_CenterType_Sale`, `Gl_Nav_Financials` (the launcher section, "Financials" / "المالية"),
  `Centers_ParentMustBeGrouping`, `Centers_HasChildren`.
- **Both packages** carry a README and the Apache-2.0 header, and build with warnings-as-errors
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
derives from `ParentId`. `CenterType` is stored under spec 0012's enum-as-string convention:
`varchar(12)`, no `IN` check. A distribution extends it by plain inheritance and
`tellma.UseEntity<Center, MyCenter>()`. Queryex entity `gl.Center`; navigations
`Parent → gl.Center`, `CreatedBy`/`ModifiedBy → core.User`; the default related projection is
`Id, Name, Name2, Name3, Code`.

### 5.3 `gl.Centers`

`ActivatableTreeEntity`; non-temporal; UDTT `CentersList` (excludes `Node`); sequence
`gl.sq_Centers AS int START WITH 1000 INCREMENT BY 1 NO CYCLE`.

| Column | Type | Null | Constraints | Notes |
|---|---|---|---|---|
| `Id` | `int` | no | `PK_Centers` clustered; `CK_Centers_Id CHECK ([Id] > 0)` | |
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
tree statements of spec 0012 (affected set, re-path, cycle fence `Tree.Cycle`, scoped recount) are
appended by the emitter after every save, `activate`, `deactivate`, `delete` and
`delete-with-descendants` on this table; the weekly `core.tree-verify` job repairs drift and meters
`tellma.data.tree.repairs`. Deleting a center whose children are not in the same delete fails the
self-referencing FK; spec 0012 maps the `547` to `Fk.InUse`, which this stack narrows to
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

Round trips: create 2 (RT1 carries the `Code` key load and, for parents outside the payload, their
load); update 2; delete 2 (the `SubtreeCount` load in RT1); activate/deactivate 1 (plus the recount
inside the same batch).

### 5.5 `GlFeature`

```csharp
// Tellma.Module.Gl
public sealed class GlFeature : ITellmaFeature                // Name = GlModule.FeatureName; no [Requires]: Core is implicit (spec 0011 §2.1)
{
    public void Declare(FeatureDeclaration declaration);      // declares no required feature
    public void Contribute(FeatureContribution contribution); // Entity<Center, CenterService<Center>>(); ProvisioningStep<GlSampleCentersStep>(); Singleton<IGlSampleCenters, DefaultGlSampleCenters>(); Model<CenterConfiguration>()
}
```

`CenterConfiguration` is the EF `IEntityTypeConfiguration<Center>` for §5.3 (table, sequence,
indexes, the string conversion of `CenterType`); the tree convention adds `Node`, the FK and the
unique index. Securables `gl.Center × Read | Save | Delete | Activate` (`FilterRoot = gl.Center`)
are registered by the stack feature from the descriptor; the module registers none by hand. The
projected routes are `/{tenantId}/api/web/centers/{operation}` with `{operation}` one of `query`,
`get`, `get-by-ids`, `get-by-parent-ids`, `save`, `delete`, `delete-by-query`,
`delete-with-descendants`, `activate`, `deactivate`, `export`, `export/start`, `export-for-import`,
`export-for-import/start`, `inspect-import`, `import` and `import/start`; the MCP exposure is
`Full`.

### 5.6 `gl.sample-centers`

```csharp
// Tellma.Module.Gl.Abstractions (namespace Tellma.Module.Gl)
public sealed record SampleCenterName(string Language, string Text);   // Language: a language subtag ("en", "ar")

public sealed record SampleCenter(
    string Code, IReadOnlyList<SampleCenterName> Names, CenterType CenterType, string? ParentCode);   // Names: the first is the fallback

public interface IGlSampleCenters                                    // singleton; replaceable by a dependent feature
{
    int Version { get; }                                             // the step's Version
    IReadOnlyList<SampleCenter> Centers { get; }
}

// Tellma.Module.Gl
public sealed class DefaultGlSampleCenters : IGlSampleCenters;       // Version = 1; the rows below
public sealed class GlSampleCentersStep : ITenantProvisioningStep;   // Name = "gl.sample-centers"; Order = 100; Version = IGlSampleCenters.Version
```

The step writes the rows `IGlSampleCenters` supplies. `GlFeature` registers
`DefaultGlSampleCenters`; a distribution built for one customer or sector replaces it from a
feature that requires `GlModule.FeatureName` and contributes
`Singleton<IGlSampleCenters, TImplementation>()` (spec 0011's rule: a dependent feature's
registration overrides its dependency's). An empty `Centers` makes the step write nothing. The
implementation bumps `Version` when its rows change, so developer tenants re-run the step. Every
`ParentCode` must name another row of the list; a `ParentCode` that does not is an
`InvalidOperationException` naming the implementation type, raised before any write, and the save's
own validation reports the rest (duplicate codes, parent types, cycles).

The step runs after the platform steps below 100 in the migrator's step-runner scope (spec 0011's
`ITenantScopeFactory.CreateScopeAsync(snapshot, allowNonActive: true)`, `Kind = System`).

- **Outside Development** (`IHostEnvironment.IsDevelopment()` is false) the step records completion
  and writes nothing: sample data is a development convenience.
- **Resolve.** Otherwise it first resolves the existing rows: one
  `CenterService<TCenter>.QueryAsync` filtered by `Code` over the list's codes, projecting
  `Id, Code`.
- **Save.** It then saves the list through `CenterService<TCenter>.SaveAsync` with
  `SaveOptions(Source = System, ReturnEntities = false)`. Each resolved row carries its id, so a
  re-run at a higher `Version` updates names and types and never duplicates. Parents are
  referenced by temporary negative ids within the payload.
- **Names.** `Name` is the text for the tenant's primary language, else the row's first `Names`
  entry. `Name2` and `Name3` are the texts for the second and third languages, else `null`
  (`TenantSettings.Languages`, matched by language subtag).
- **Round trips:** the read, then the save's two (§5.4); none when `Centers` is empty.

The default rows (`DefaultGlSampleCenters`):

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

### 5.7 `taxonomy.json`

This spec adds `"Gl"` to `modules` in the repository-root `taxonomy.json` created by spec 0011.
A unit test in `Tellma.Core.Tests` asserts that every `src/module/<m>/` folder has a
`Tellma.Module.<M>` project whose `<M>` is listed (PascalCase, two-letter segments such as `Gl`),
and that every listed module has a folder.

## 6. Provisioning, bootstrap and local development

### 6.1 The first administrator

Spec 0011's step `10 core.bootstrap-administrator` calls spec 0014's
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
  `ExecuteActionAsync<IReadOnlyList<InviteResult>>("invite", [adminId], null, ActionOptions())`, the
  overload of spec 0015 §3.1 for an action with its own result. `RequestContext.Kind = System`
  bypasses the `Invite` securable (`UserAccess.System`); the flow of §3.3 runs unchanged, so the
  administrator receives the `core.user.added` notice, the welcome email (its row waits `Pending`
  until `provision` sets the tenant `Active`, spec 0022 §3.2) and, unless they already hold a
  credential, the identity server's invitation email. The migrator therefore binds
  `IdentityServerClientOptions` from the same `Tellma:Identity` section as the web host, holding
  `ServiceClientSecret` (spec 0011's secret policy). A failed invite fails the `provision` command
  after the steps are recorded; re-running `provision` re-invites (idempotent: the identity server
  answers `Reinvited` or `Active`).
- **Sandbox tenants.** The flow runs with `ExistingOnly = true` (§3.3); provisioning a sandbox for
  an administrator with no usable identity account — an unknown address, or an existing one holding
  no credential — fails with `Users.SandboxRequiresExistingIdentity` in the report, and the operator
  provisions the live tenant first.

### 6.2 Composition parity

The Web and Migrator hosts of the reference distribution both reference `Tellma.Distro.Acme` and
build its composition (spec 0011 §1.2), so `UserService`, `IIdentityServerClient`, `INotifier` and
`IEmailOutbox` resolve identically in both. The distribution's Development configuration sets:

- **Identity.** `Tellma:Identity:Mode = InProc`, the seeded stable secrets, and
  `Authority = {PublicOrigin}/id` (spec 0011 §5.7), that is `https://localhost:<client port>/id`.
  The client port comes from `.dev-ports.local`, else 4200 (spec 0011 §1.2); the Web project's own
  Kestrel port sits behind the SPA proxy and never appears in a browser URL. `ClientId` is
  `acme-svc` from the slug (spec 0011 §5.7).
- **Support.** `Tellma:Support:Enabled = true`, so the reference distribution exercises support
  requests (spec 0026 §12.1).

A test asserts no secret is tracked (spec 0011's rule).

### 6.3 From a fresh clone

Spec 0011 §6.6 gives the commands (`pnpm install` at the repository root, `migrate`, then the Web
project). The commands run unchanged on Windows and Linux.

- **`migrate`** provisions tenants 1 and 2 with `admin@localhost`. Tenant 1 offers `en`, `ar` and
  `gc`, `uq` (spec 0011 §5.7).
- **Steps.** It runs `core.bootstrap-administrator`, `core.settings`, `core.blob-container`,
  `gl.sample-centers`, `acme.sample-data` and `core.report-seeds`.
- **Sign-in.** The administrator signs in with the console code.

Acme's development data exercises paging, tiles, tree restore and search ancestors:

- **Centers.** `AcmeSampleCenters` (§1.4) replaces the module's eight default rows (§5.6) with a
  40-node, four-level tree.
- **`acme.sample-data`.** `AcmeSampleDataStep` (`Order` 200, `Version` 1) runs, like §5.6's step,
  in Development only. Through the stacks it writes:
  - two currencies;
  - about 60 customers (all three types, both currencies, images on some, a few deactivated);
  - the role `Sales` (`Code = 'Sales'`);
  - 20 sales invoices with lines over the sample centers.

  It deactivates four of the sample centers. It then assigns three of the invoices to the
  administrator through `IAssignments.AssignAsync`, so the tray and the home lists have rows on
  first sign-in.
- **Reports.** `core.report-seeds` runs last and writes `acme.sales-by-center` (§1.4) with its grant
  to `Sales`.

## 7. Reference distribution migrations

The tenant database schema is owned by the platform's `TellmaDbContext` plus the module and
distribution configurations; the **migrations** are the distribution's (spec 0011: only the
distribution generates and ships migrations, through `AcmeDesignTimeFactory`). This spec ships the
reference distribution's initial tenant migration and the rules every later one follows.

- **`Migrations/Tenant/20260904000000_InitialTenant`** in `Tellma.Distro.Acme.Migrator`, generated
  by `dotnet ef migrations add InitialTenant --context TellmaDbContext` and reviewed by hand. It
  contains, in this order:
  1. Schemas `core`, `gl`, `acme`.
  2. Every `core` table of specs 0012–0021, 0026, 0028, 0033 and 0035 with its sequence, indexes,
     checks, history table and period: `core.Users`, `UsersHistory`, `UserStamps` (with
     `AssignmentsSeenAt`), `UserPreferences`, `Roles`, `RoleMemberships`, `Permissions` and
     histories, `Settings`, `SettingEntries` and histories, `VersionTags`, `CalendarMonths`,
     `Blobs`, `Jobs` (with `LabelKey` and `LabelArgumentsJson`), `Schedules`, `ScheduleStates`,
     `JobWorkerState`, `Notifications`, `NotificationPreferences`, `Exports`, `Imports`,
     `SupportRequests`, `Assignments`, `ReportDefinitions`, `ReportDefinitionShares`, `Dashboards`,
     `DashboardWidgets`, `DashboardShares`.
  3. `gl.Centers` (§5.3) with `MyCenter`'s `ManagerId`, and Acme's tables (§1.4):
     `acme.Currencies`, `acme.Customers`, `acme.CustomerContacts`, `acme.SalesInvoices`,
     `acme.SalesInvoiceLines`, `acme.SalesInvoiceAttachments`.
  4. `dbo.__TellmaProvisioning` and `dbo.__TellmaSchema` (spec 0012 §4.3).
  5. Every UDTT of spec 0001's derivation for the tables above (`UsersList`, `RolesList`,
     `RoleMembershipsList`, `PermissionsList`, `SettingsList`, `SettingEntriesList`, `CentersList`,
     `CustomersList`, …) and the standalone types of spec 0012 §4.4 (`IdList`, `BigIdList`,
     `GuidList`, `StringList`, `DateList`, `IdStampList`, `UserInvitationOutcomeList`,
     `UserPreferenceList`, `NotificationPreferenceList`, `NotificationRowList`,
     `NotificationSliceList`, `JobRequestList`, `JobOutcomeList`, `JobProgressList`,
     `JobLeaseList`, `ScheduleNextList`, `VersionTagList`; `TenantMembershipList` is a catalog
     type, spec 0011's).
  6. The `HasData` rows: spec 0014's system user, Administrator role, permission and membership,
     and `UserStamps` row; spec 0013's `core.Settings` placeholder and `core.CalendarMonths` rows
     (spec 0013 §8.2); spec 0020's built-in schedules and states; `JobWorkerState`.

  No `IDENTITY`, no `rowversion`, no trigger, no `MERGE`.
- **Not in a migration.** RCSI (`provision` sets it), the `tellma_app` role and grants (recomputed
  by the migrator after every run), `core.VersionTags` seeding (the migrator's registry seed), the
  first administrator, the report seeds, the sample centers and Acme's development data
  (provisioning steps).
- **Later migrations** follow spec 0012's N−1 rule: a new column is nullable or defaulted; a column
  is dropped only when no application version that can still be running writes it; a rename is
  add, copy, then drop across three releases (spec 0012 §4.3); every UDTT change is a new physical
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
| `users/invite` (n ids) | 2 | ⌈n/1000⌉ invite calls between them |
| `users/issue-credentials` | 2 | one create call between them; on rotation one delete call after commit |
| `users/invitation-status` | 1 | ⌈m/1000⌉ delivery calls (m = invited subjects) |
| `users/save` (admin) | 2 | — |
| `users/me` | 1 | — |
| `users/me/save` | 3 | — |
| `users/me/locale` | 1 | — |
| `users/me/preferences/set` / `delete` | 1 | — |
| `users/preferences/get` | 1 | — |
| `users/preferences/set` / `delete` | 2 | — |
| `users/me/test-notification` | 0 | 1 send |
| `users/activate` / `deactivate` | 2 (spec 0014 §7.5: `UserAccessRules` loads in RT1) | — |
| `users/delete` | 2 (spec 0014 §7.5) | — |
| `access/check` (self / other) | 0 / 2 | — |
| `roles/save` | 2 | — |
| `roles/delete` | 2 (spec 0014 §7.5) | — |
| `roles/activate` / `deactivate` | 2 (spec 0014 §7.5) | — |
| `centers/save` | 2 | — |
| `centers/activate` / `deactivate` | 1 | — |
| `centers/delete` | 2 | — |
| `gl.sample-centers` step | 3 | — |

Every statement is parameterised by TVP or scalar; no per-id SQL text; no lock is held across a
remote call. The integration suite asserts each row through spec 0012's `DataAccessScope`
(`tellma.data.roundtrips`).

### 8.2 Instruments and logs

Meter `Tellma.Core` (constants in `UsersTelemetryNames`): `tellma.users.invites` (counter; tag
`outcome`), `tellma.identity.calls` (counter; tags `identity.endpoint`, `outcome`),
`tellma.identity.call.duration` (histogram, s; same tags), `tellma.users.test_notifications`
(counter; tags `channel`, `outcome`). `Tellma.Module.Gl` declares no meter: the tree recompute is
metered by spec 0012's `tellma.data.tree.recomputes` (tag `entity`). No tenant or user tag on any
instrument.

Log events (structured, Information unless stated):
`IdentityCall(endpoint, count, outcome, elapsedMs)`;
`UsersInvited(count, invited, reinvited, active, errors)`;
`UsersInvitePartial(processed, remaining)` (Warning); `ServiceAccountDeleteFailed(userId)` (Warning,
with the exception); `UserAccessRevoked(userId)`; `SampleCentersProvisioned(tenantId, count)`;
`TenantAdministratorInvited(tenantId, userId, status)`.
Traces: the invite action's activity carries child activities per identity chunk (`identity.invite`)
tagged with `identity.count` and `identity.outcome`.

## 9. Testing

Four test projects pin this spec.

**`test/core/Tellma.Core.Tests`** (unit) pins:

- **`IdentityServerClient`** against a fake `HttpMessageHandler`: chunking at 1,000 and input
  order; token caching, the 60 s margin and single flight (`FakeTimeProvider`); one `401` retry, a
  second `401` and a token-endpoint `4xx` as `InvalidOperationException`; prefix on a failed chunk
  and on cancellation; `DependencyUnavailableException` before any chunk on a transport failure or
  `5xx`; `ExistingOnly` serialised; options validation (`https` outside Development).
- **`UserService`** with a fake client and a fake pipeline:
  - invite argument mapping (`Locale` fallback, `DisplayName` in `Locale`'s content language with
    its `Name` fallback, `ReturnUrl`, sandbox `ExistingOnly`); `InviteResult` order and
    `SubjectMismatch`;
  - the welcome-email requests through a fake `IEmailOutbox`, with every field §3.3 sets;
  - `PartialFailureException` carries the prefix in `Results` and the not-attempted ids in
    `Failed`;
  - issue-credentials mapping (`DisplayName = Name`, the resource), the secret absent from every
    log;
  - §3.2 rules;
  - `SaveMe`: the request mapped onto the loaded row, `RoleMemberships` left null, a stamp
    mismatch as 409 with no enlistment, `Override` skipping the comparison and enlisting with
    `Override`;
  - preference key grammar (hyphenated and numeric segments such as `report.acme-aging.params`
    accepted) and limits;
  - test-notification: `inbox` refused, an unknown key refused, a channel without a sender
    `NotConfigured`, the email send.
- **`RoleService`:** `problems` per drift condition against a scripted registry and schema.
- **`AccessService`:** self vs other, the `core.Role × Read` requirement, the 200 cap.
- **`taxonomy.json`** module consistency.

**`test/module/gl/Tellma.Module.Gl.Tests`** (unit) pins:

- **Rules:** `CenterService` rules with a fake loader (parent in payload, parent loaded, type
  change with children, self-parent); delete of a row with children.
- **Sample data:** the default sample tree is acyclic, codes unique, Arabic twins present. The step
  with a fake `IGlSampleCenters`: a replacement's rows written instead of the default's, an empty
  list writing nothing, an unknown `ParentCode` refused before any write, names mapped to the
  tenant's language positions with the first-entry fallback.
- **Packaging:** the package's reference closure excludes `Tellma.Core`; `GlFeature` declares no
  required feature, `GlModule.FeatureName` is its `Name`, and it contributes exactly four items;
  spec 0013's resource audit over the GL assemblies.

**`test/core/Tellma.Core.IntegrationTests`** (`Category=Integration`) pins, on LocalDB or
Testcontainers unless noted:

- **The invite write-back statement:** state transitions, `InvitedAt` set once, `LastInviteError`
  cleared by a later success, `SubjectMismatch` rows untouched, `2627` on `UX_Users_Subject` →
  `Unique` at `Subject`, `PreferencesTag` bumped for invited users only, `ModifiedAt` stamped.
- **Invite side effects:** `core.user.added` inserted for every outcome with a status and
  deduplicated; a `Pending` welcome email in `core.OutgoingEmails` for each, committed with the
  write-back, rendered in the invitee's language and calendar rather than the caller's, with the
  sign-in paragraph for `Invited` and `Reinvited` only; the membership hint recorded.
- **Credentials:** the credentials write-back (`Subject`, `JoinedAt` set once, `PreferencesTag`
  bumped), rotation deleting the old client after commit, deactivation deleting the client, `Kind`
  write-once, a `Service` row refused by `invite` and an `Individual` row by `issue-credentials`.
- **Self-service:** `me/save` by a member holding no `Save` grant changes the request's members
  only and bumps `PreferencesTag`; the admin `save` of their own row by the same member is 403; a
  stale `ModifiedAt` on `me/save` is 409; a member holding no role grant reads their own row and no
  other (`UserAccessCriteria`); `SignatureId` saved through `me/save` and the user save.
- **`me/locale`:** a language only, a calendar only, both, neither (422 `Users.LocaleEmpty`), a
  language not offered (422), an unchanged value (no history row, tag unchanged), a read-only
  tenant (403), and the tag bump visible in `Tellma-Version-Tags`.
- **Preferences:** the preferences statement (set, delete, the `MaxPreferenceKeys` cap, tag bump,
  second user unaffected); `preferences/get`, `set` and `delete` for another user under the
  `Preferences` filter, `set` and `delete` answering the target's bag and bumping the target's
  `PreferencesTag`, a hidden target not found.
- **Centers:** `gl.Centers` save with in-payload parents in two round trips (the `Code` key load
  in RT1), the parent-type rule, `Centers.HasChildren`, `delete-with-descendants`,
  activate/deactivate recounts, `ParentMustBeGrouping` on a type change.
- **Extras:** the `members` extra under a filtered `core.User × Read` grant (hidden users absent),
  at the 500-row cap, and absent when that decision is `Denied`; the `activity` extra; a role
  holding a drifted permission answering its `problems` row.
- **Budgets:** every §8.1 budget through `DataAccessScope`.
- **Against the in-proc identity server:** invite end to end (`Invited`, `Reinvited`, `Active`, a
  refused account's per-user error); `ExistingOnly` on a sandbox tenant (skipped until spec 0010
  §A.7 ships, the skip reason naming it); delivery status after a real send through the email log
  sink; a service account created end to end, a `client_credentials` token obtained, and the
  connect prologue resolving the row as `ServiceAccount` through a test-mapped bearer endpoint
  under spec 0011's `Tellma.Api` policy.

**`distributions/acme/test/Tellma.Distro.Acme.IntegrationTests`** (`Category=Integration`) pins:

- **Provisioning:** `migrate` then `provision` from an empty server creates `gl.Centers` and
  Acme's tables, and records `gl.sample-centers` and `acme.sample-data` with `Version = 1`.
- **Development data:** in Development, Acme's 40 centers (four inactive), two currencies, about
  60 customers, the role `Sales`, 20 invoices and the administrator's three open assignments; none
  of them otherwise. `acme.sales-by-center` on every tenant, with its grant to `Sales` in
  Development.
- **Actions:** `assign` hands an invoice on, and `post` completes its assignment.
- **Bootstrap:** `admin@localhost` is `Invited` and becomes `Joined` on first sign-in; a
  deployed-style `provision --admin-email` invites through the in-proc identity server and
  enqueues the administrator's welcome email.
- **Re-runs and parity:** a bumped step version re-runs idempotently; model parity (§7); spec
  0013's resource audit over the distribution's assemblies.

- **CI.** PR runs the unit tier and `Category=Integration` on LocalDB (Windows) and Testcontainers
  (Linux). No suite here is `Live=true`, since none reaches a third-party account.
- **Fixture.** Tenant 1 with `en`/`ar`, the reserved-band rows, one administrator, three plain
  users (`New`, `Invited`, `Joined`), one service account, two roles (one public, one holding a
  drifted permission), and the sample centers.

## 10. Definition of done

- **Projects**: `src/module/gl/Tellma.Module.Gl.Abstractions`, `src/module/gl/Tellma.Module.Gl`,
  `test/module/gl/Tellma.Module.Gl.Tests` (new), `src/core/Tellma.Core.Abstractions`,
  `src/core/Tellma.Core`, `test/core/Tellma.Core.Tests`, `test/core/Tellma.Core.IntegrationTests`,
  `distributions/acme/src/Tellma.Distro.Acme`, `distributions/acme/src/Tellma.Distro.Acme.Web`,
  `distributions/acme/src/Tellma.Distro.Acme.Migrator`,
  `distributions/acme/test/Tellma.Distro.Acme.IntegrationTests` (grown) — each with a README, XML
  docs on every member, building and testing on Windows and Linux under warnings-as-errors, wired
  into `Tellma.slnx`; `taxonomy.json` lists `Gl`.
- **Behavior**: §1.4 (the reference distribution's feature), §2 (client, token, prefix), §3
  (hooks, invite, status, self-service including `me/locale`, side effects, `access/check`), §3.8
  (credentials), §4 (the roles stack and its two extras), §5 (`Center`, table, rules, feature,
  sample step), §6 (bootstrap in both modes, the development data), §7 (the initial migration and
  parity) — implemented and pinned by the suites of §9, green in CI.
- **Observability**: every instrument and log event of §8.2 emitted and asserted; every §8.1
  budget asserted.
- **CI**: unit and `Category=Integration` on PR (Windows LocalDB, Linux Testcontainers), the
  in-proc identity server included; the `acme` smoke deployment provisions a tenant and invites its
  administrator.
- **Docs**: ARCHITECTURE.md updated where this spec touches it — the migrations/migrator section
  (the migrator holds the distribution's service-client credentials and invites the first
  administrator through the `invite` action of `UserService` after the provisioning steps), the
  entity-class section (a self-referencing tree entity is a single non-abstract class extended by
  plain inheritance, `Center` being the first), the package-naming section (the first
  `Tellma.Module.<M>` pair, `src/module/gl/`), and the reserved-slug/taxonomy section
  (`taxonomy.json` gains the `Gl` module). Public XML docs and error messages reference no
  `docs/` paths, per repo rule.
- **Not in scope of done**: the identity-server work itself (spec 0010 Part A); the report
  definitions and seeds machinery (spec 0033); the settings edit API (spec 0013), Excel over these
  stacks (spec 0019), the inbox page (spec 0021), the email outbox itself (spec 0022), the pages
  over these stacks (spec 0032), SMS. §9's sandbox `ExistingOnly` test stays skipped until the
  in-proc identity server carries spec 0010 §A.7.

## Decisions record

The load-bearing decisions, where not already evident above:

1. **The identity call sits between two round trips, never inside a transaction** — a remote call
   under row locks blocks every other save of the tenant, and a transaction that rolled back after
   `Reinvited` would have spammed users (§3.3).
2. **Prefix on failure, `PartialFailureException` to the caller, subjects recorded first** — a
   subject the identity server created is real whether or not this tenant wrote it; recording the
   prefix and returning 502 with the remainder lets the admin retry only what was not attempted
   (§2.3, §3.3).
3. **`ExistingOnly` on sandbox tenants** — a sandbox must never make the identity server send mail,
   yet sandbox sign-in needs a real identity; get-by-email without mail is the only shape that
   satisfies both (§2.1, §3.3).
4. **Delivery state is a live drill-down, never stored** — the identity server owns delivery; the
   tenant stores only the outcome it needs to interpret `NotFound` (§3.4).
5. **Self-service is an enlisted write of the caller's own row, never a bespoke `Save` grant** —
   `me/save` loads the row, applies `MeSaveRequest` and enlists it with the invoker's frame
   (spec 0015 §13.3), so the admin `save` stays administrative and no per-column confinement rule
   exists (§3.5).
6. **`RoleService<TRole>` carries no filter logic** — filter validation and the language-version
   stamp are spec 0014's `RoleAccessRules<TRole>`, a component a substituted leaf cannot bypass
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
    composition and holds the service secret (spec 0011), so one bootstrap path serves Development
    (subject known) and deployment (invite) (§6.1).
12. **Migrations are the distribution's; the schema is the platform's** — the reference migration
    is generated from the composed context and kept in parity by test (§7).
13. **Service accounts are rows the save creates and an action credentials** — one stack, one
    prologue, one permission model; the secret crosses once and the old client dies after the new
    one is recorded (§3.8).
14. **`me/locale` is its own action** — the session menu's switches cost one round trip and hold
    no stamp, where `me/save` costs three (§3.5).
15. **Drift is a details extra of the roles stack, not a route** — the role page already reads the
    role and its permissions, so the extra computes drift from them in memory (§4.3).
16. **`members` is filtered by the caller's user grant and capped** — a filtered grant on users
    reveals no hidden user through a role page (§4.3).
17. **The reference distribution's server is one feature** — `AcmeFeature` exercises every client
    path with entities, actions, setting keys and reports alone (§1.4).

## Review flags

1. **`Invited → Joined` inside the connect prologue** (§3.3, §6.1; the flip is spec 0014's
   statement) versus a nightly job that flips lazily. Chosen: in the prologue — once per user
   lifetime, and the admin's page is current. Flips if the temporal write on the request path
   shows up in history churn measurements.
2. **The pipeline executes a composed action batch before surfacing `PartialFailureException`**
   (§3.3) versus the action performing the prefix write-back through a second
   `IGuardedBatchRunner.Run` and then throwing. Chosen: the pipeline rule, one code path for the
   write-back. Flips if spec 0015's action path cannot honour it cheaply.
3. **`me/save` as an enlisted write** (§3.5) versus a bespoke `Save` grant confined by a validation
   rule. Chosen: the enlisted write — one authority model, no grant-kind test. Flips if a
   distribution needs a leaf's extra columns self-editable, which `MeSaveRequest` does not carry;
   until then such a distribution adds its own `[ApiAction]`.
4. **`RoleMemberships` single-owned by `User`, members as a read-only role extra** (§4) versus
   dual ownership with cross-owner stamp bumps. Chosen: single ownership — a simpler emitter and
   no stale-owner deletion race. Flips if admins reject "add members" as a bulk user save.
5. **Weak rows carry no audit columns** (§4; `Permissions`, `RoleMemberships` are spec 0014's)
   versus `ModifiedById` on the two security children. Chosen: none — the owner's stamp plus the
   child's period answer "who changed this". Flips on an auditor requirement for a per-row actor.
6. **`Gender` kept on `core.Users`** (§3.3) versus dropping it and sending no gender to the identity
   server. Chosen: kept — for the invitation's grammar and for the SPA's gender-inflected strings
   about the current user through `UserProfileView` (spec 0016 §3.4). Flips if both the identity
   server's templates and the SPA's strings go neutral.
7. **`ExistingOnly` as a spec 0010 Part A amendment** (§2.1) versus refusing invites on sandboxes
   outright. Chosen: the amendment. Flips if the identity server cannot ship it before this spec
   is implemented, in which case sandbox invites return `Users.SandboxRequiresExistingIdentity`
   for every row until it lands.
8. **Five `CenterType` values** (§5.2) versus the three operating types. Chosen: five — the
   grouping rule needs the two structural types. Flips never; the question is whether to add
   the SG&A types (`Administration`, `Marketing`, `FinanceCost`, `OtherPL`) now — chosen: later,
   additively, when the first posting rule needs them.
9. **Lockout invariant accepts `Invited` administrators with a subject** (spec 0014's guard, which
   this spec's invite flow relies on) versus requiring `State = Joined`. Chosen: accept `Invited`
   — otherwise a tenant whose only administrator has not signed in yet is unmanageable. Flips if
   lost invitations prove common.
10. **`ExpectedModifiedAt` required on the settings patch** (spec 0013's `TenantSettingsPatch`;
    the API left this spec's scope) versus optional. Chosen: required. Flips if MCP agents cannot
    reliably read before writing.
11. **Delete restricted to `State = New` users** (§3.6; spec 0014's rule) versus deleting any user
    and mapping FK failures per referencing table. Chosen: restricted — predictable. Flips if
    admins need to purge mistaken invitations after sign-in.
12. **The migrator invites the deployed first administrator** (§6.1) versus provisioning only
    seeding the row and a web-side bootstrap endpoint sending the invite. Chosen: the migrator —
    one path, no second privileged endpoint; the cost is the service secret in the migrator's
    configuration. Flips if operators forbid secrets on the migration host.
13. **Sample centers only in Development** (§5.6) versus seeding them on every provisioned tenant
    as a starter tree. Chosen: Development — a customer's tree is their own. Flips if onboarding
    wants a starter chart.
14. **`Tellma.Module.Gl` declares no meter** (§8.2) versus a `Tellma.Module.Gl` meter for future GL
    instruments. Chosen: none until an instrument exists; adding one later is additive.
15. **The `members` extra capped at 500 rows** (§4.3) versus a paged members list over the
    `core.RoleMembership` child query surface. Chosen: the cap — one round trip with the role.
    Flips if roles with more than 500 members are common.
