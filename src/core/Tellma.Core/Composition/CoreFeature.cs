// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Calendars;
using Tellma.Core.Abstractions.Client;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Email;
using Tellma.Core.Abstractions.Identity;
using Tellma.Core.Abstractions.Jobs;
using Tellma.Core.Abstractions.Localization;
using Tellma.Core.Abstractions.Notifications;
using Tellma.Core.Abstractions.Realtime;
using Tellma.Core.Abstractions.Settings;
using Tellma.Core.Abstractions.TableTypes;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Access;
using Tellma.Core.Api;
using Tellma.Core.Blobs;
using Tellma.Core.Caching;
using Tellma.Core.Calendars;
using Tellma.Core.Client;
using Tellma.Core.Data;
using Tellma.Core.Identity;
using Tellma.Core.Jobs;
using Tellma.Core.Localization;
using Tellma.Core.Notifications;
using Tellma.Core.Outbox;
using Tellma.Core.Provisioning;
using Tellma.Core.Settings;
using Tellma.Core.Users;

namespace Tellma.Core.Composition
{
    /// <summary>
    ///     The platform's own feature, named <c>core</c>. The composition root adds it first and
    ///     unconditionally, so every other feature is realised after it without declaring it.
    /// </summary>
    /// <remarks>
    ///     It carries the users, roles, permissions, settings and cache content of the platform as
    ///     those areas land; each adds its items to <see cref="Contribute" />. A distribution never
    ///     adds it: <c>AddFeature&lt;CoreFeature&gt;()</c> is accepted as the idempotent no-op it is.
    /// </remarks>
    public sealed class CoreFeature : ITellmaFeature
    {
        /// <summary>The feature's name.</summary>
        public const string FeatureName = "core";

        /// <summary>The key of the inbox channel: the notifier's own rows.</summary>
        public const string InboxChannel = "inbox";

        /// <summary>The key of the email channel.</summary>
        public const string EmailChannel = "email";

        /// <inheritdoc />
        public string Name => FeatureName;

        /// <inheritdoc />
        public void Declare(FeatureDeclaration declaration)
        {
            // Core requires nothing and binds its options as platform options, not through here.
            ArgumentNullException.ThrowIfNull(declaration);
        }

        /// <inheritdoc />
        public void Contribute(FeatureContribution contribution)
        {
            ArgumentNullException.ThrowIfNull(contribution);

            // --- Calendars: the platform's three systems, first in the registry, and the registry ---
            _ = contribution
                .Calendar<GregorianCalendarSystem>()
                .Calendar<UmAlQuraCalendarSystem>()
                .Calendar<EthiopianCalendarSystem>()
                .Singleton<ICalendarRegistry, CalendarRegistry>();

            // --- Languages, value sets and locale negotiation ---
            // The catalogue reads the distribution's language declaration, which the composition
            // root registers; the value-set registry composes the platform's four sets with every
            // set a feature contributes.
            _ = contribution
                .Singleton<ILanguageCatalog, LanguageCatalog>()
                .Singleton<IValueSetRegistry, ValueSetRegistry>()
                .Singleton<ILocalizationNegotiator, LocalizationNegotiator>();

            // --- The client metadata's distribution, features and routing sections (spec 0024 §3.2) ---
            _ = contribution.Singleton<IClientAssetContributor, CoreClientAssetContributor>();

            // --- The string packs in the client bundle (spec 0013 §10.4) ---
            // The composer itself, the pack provider and the label provider are the localization
            // composition's, registered with the framework's localizers.
            _ = contribution.Singleton<IClientAssetContributor, StringPackClientAssetContributor>();

            // --- The web surface's route model and the client metadata's contracts (spec 0016 §3.10) ---
            // One model over the stack registry; the endpoint projection maps its routes and the
            // contributor writes their fingerprints, long and idempotent lists.
            _ = contribution
                .Singleton<IRouteContracts, RouteContracts>()
                .Singleton<IClientAssetContributor, ContractsClientAssetContributor>();

            // --- The client entity model: summaries, metadata, named enums and typed declarations (spec 0029 §2, §15) ---
            _ = contribution
                .Singleton<IClientEntityModel, ClientEntityModel>()
                .Singleton<IClientAssetContributor, EntityClientAssetContributor>();

            // --- The platform's own tables in every tenant database, mapped for migrations only ---
            _ = contribution
                .Model<TellmaSchemaRowConfiguration>()
                .Model<TellmaProvisioningRowConfiguration>();

            // --- Access: users and roles with their children, each configuration closed over the leaf ---
            // spec 0014 §1.2, §8.2, §8.4 and §11: the two stacks' services, their access rules, the
            // companion stamp row of every inserted user, and the first administrator's bootstrap:
            // its subject statement and its provisioning step at 10.
            _ = contribution
                .Entity<User, UserService<User>>()
                .Entity<Role, RoleService<Role>>()
                .Validator<User, UserAccessRules<User>>()
                .Validator<Role, RoleAccessRules<Role>>()
                .PersistEffect<User, UserStampsCompanion<User>>()
                .PersistEffect<User, BootstrapSubjectEffect<User>>()
                .ProvisioningStep<BootstrapAdministratorStep>()
                .Model(typeof(UserConfiguration<>))
                .Model(typeof(RoleConfiguration<>))
                .Model(typeof(RoleMembershipConfiguration<>))
                .Model(typeof(PermissionConfiguration<>))
                .Model<UserStampConfiguration>()
                .Model<UserPreferenceConfiguration>()
                .TableType<UserPreferenceList>();

            // --- The users' stack beyond spec 0014 (spec 0018 §1.2, §3): the own-row read criterion,
            // the activity details extra and the invite write-back's table type ---
            _ = contribution
                .Singleton<IAccessCriteriaProvider, UserAccessCriteria>()
                .DetailsContributor<User, UserActivityContributor<User>>()
                .TableType<UserInvitationOutcome>();

            // --- The access endpoints: a user's decisions and a securable's holders (spec 0018 §3.7) ---
            _ = contribution.ApiService<AccessService>();

            // --- The identity-server client, one per distribution (spec 0018 §1.2, §2) ---
            // Its options and named HTTP client are registered with the platform's options.
            _ = contribution.Singleton<IIdentityServerClient, IdentityServerClient>();

            // --- Access: the securable registry, its client metadata section, and Core's own securables ---
            // spec 0014 §1.2, §4.2. The user stamps are a query root of their own, reached through
            // the caller's grants on the users along the stamp's User navigation; core.User × Read,
            // the owner it needs, is the users' stack's, which the stack registry registers.
            _ = contribution
                .Singleton<ISecurableRegistry, SecurableRegistry>()
                .Singleton<IClientAssetContributor, SecurablesClientAssetContributor>()
                .Securables(static securables => securables
                    .Add(UserStampConfiguration.QueryexName, AccessActions.Read, UserStampConfiguration.QueryexName, owner: new SecurableOwner(CoreResources.User, "User")));

            // --- Version tags: the table, the registry, the process's snapshots, the prelude and the bump ---
            // spec 0013 §1.2 and §2. The migrator seeds the table through the seeder; the bump
            // announces every move to the clients through cache.changed.
            _ = contribution
                .Model<VersionTagRowConfiguration>()
                .Singleton<IVersionTagRegistry, VersionTagRegistry>()
                .Singleton<IVersionTagSnapshots, VersionTagSnapshots>()
                .Singleton<IUserVersionTagSnapshots, UserVersionTagSnapshots>()
                .Singleton<ITenantVersionTagSeeder, TenantVersionTagSeeder>()
                .Scoped<IDataBatchContributor, VersionTagPreludeContributor>()
                .Scoped<IDataBatchContributor, VersionTagBumpContributor>()
                .ClientEvent(VersionTagBumpContributor.CacheChangedEvent);

            // --- The cacheable entities' lists: the process's store, the scope's view of it ---
            // spec 0013 §3.2 and §4. The store is the entities kind of the versioned cache; the
            // façade passes it the scope's tenant, tag and database handle.
            _ = contribution
                .Singleton<CacheableEntityStore, CacheableEntityStore>()
                .Scoped<ICacheableEntities, CacheableEntities>();

            // --- Tenant settings: the two tables, the store and the scope's view, the cold load, the
            // negotiation, the securables and the routed service ---
            // spec 0013 §1.2, §5 and §6. Settings and SettingEntry are mapped leaves, never stacks; the
            // cold load rides the settings store's own batch after the prelude (§5.7), and the
            // initializer at 200 negotiates the locale once the user is connected (§9.3). The
            // securables are Read and Save per settings resource (§5.5); the service is routed at
            // settings (§6.1).
            _ = contribution
                .Model<SettingsConfiguration>()
                .Model<SettingEntryConfiguration>()
                .Singleton<ISettingKeyRegistry, SettingKeyRegistry>()
                .Singleton<TenantSettingsStore, TenantSettingsStore>()
                .Scoped<ITenantSettingsCache, TenantSettingsCache>()
                .Scoped<IDataBatchContributor, SettingsColdLoadContributor>()
                .Scoped<IRequestContextInitializer, LocaleNegotiationInitializer>()
                .ProvisioningStep<SettingsProvisioningStep>()
                .Singleton<IClientAssetContributor, SettingsClientAssetContributor>()
                .Singleton<ISecurableContributor, SettingsSecurableContributor>()
                .Singleton<SettingsPatchMerger, SettingsPatchMerger>()
                .ApiService<SettingsService>();

            // --- The Queryex authoring endpoint: an editor's text checked as a query or a role save compiles it (spec 0029 §9) ---
            _ = contribution.ApiService<QueryexService>();

            // --- The Umm al-Qura month table the query language's calendar map reads (spec 0013 §8.2) ---
            _ = contribution.Model<CalendarMonthRowConfiguration>();

            // --- Blobs: the table every blob reference resolves to (never a stack), and the tenant's container at provisioning ---
            _ = contribution
                .Model<BlobConfiguration>()
                .ProvisioningStep<BlobContainerStep>();

            // --- Notifications: the tables, the notifier and renderer, the registries, the client metadata and the catalogue Core owns ---
            // spec 0021 §11. Notification is a read-only stack with the platform's default service,
            // whose read the stack registry registers (spec 0021 §8); the preferences are a platform
            // table, never a stack. Each type is declared by the feature
            // code that raises it; core.user.added is the one this contribution declares for the
            // users' invitation. The two baseline channels and the two client events the
            // notifications and the hub publish come first. The self-scope criterion lets every member
            // read their own rows; the inbox and preference services, the delivery and retention handlers
            // and retention's nightly schedule (spec 0021 §4.3, §4.5, §5, §7) follow.
            _ = contribution
                .Entity<Notification>()
                .Model<NotificationConfiguration>()
                .Model<NotificationPreferenceRowConfiguration>()
                .TableType<NotificationRowList>()
                .TableType<NotificationSliceList>()
                .TableType<NotificationPreferenceList>()
                .Scoped<INotifier, Notifier>()
                .Scoped<INotificationRenderer, NotificationRenderer>()
                .Singleton<INotificationTypeRegistry, NotificationTypeRegistry>()
                .Singleton<INotificationChannelRegistry, NotificationChannelRegistry>()
                .Singleton<IClientEventRegistry, ClientEventRegistry>()
                .Singleton<IClientAssetContributor, NotificationsClientAssetContributor>()
                .Singleton<IAccessCriteriaProvider, NotificationAccessCriteria>()
                .ApiService<InboxService>()
                .ApiService<NotificationPreferencesService>()
                .JobHandler<NotificationDeliveryHandler>()
                .JobHandler<NotificationRetentionHandler>()
                .BuiltInSchedule(2, NotificationRetentionHandler.Key, "30 3 * * *")
                .NotificationType(new NotificationTypeDescriptor("core.user.added", "users", Mutable: true, TargetResource: null))
                .NotificationChannel(new NotificationChannelDescriptor(InboxChannel, DefaultEnabled: true))
                .NotificationChannel(new NotificationChannelDescriptor(EmailChannel, DefaultEnabled: false))
                .ClientEvent(Notifier.InboxChangedEvent)
                .ClientEvent(SessionEndedEvent.Name);

            // --- The email outbox: the rows and their stack, the outbox, the sending jobs' handler and
            // its failure notice, the attachments' blob kind and the two table types ---
            // spec 0022 §9.3. OutgoingEmail is a Query | Details | Delete | Enlist stack whose rows
            // arrive only through the outbox; its registration as a scoped service gives the registry
            // the outbox's enlistment pair. An email attachment needs no preview, so its kind renders
            // no thumbnail; the kind itself comes from the attachment's blob reference. The commit
            // effect counts the rows a frame committed (spec 0022 §9.2).
            _ = contribution
                .Entity<OutgoingEmail, OutgoingEmailService>()
                .PersistEffect<OutgoingEmail, OutboxEnqueuedEffect>()
                .Model<OutgoingEmailConfiguration>()
                .Scoped<IEmailOutbox, EmailOutbox>()
                .JobHandler<OutgoingEmailHandler>()
                .NotificationType(OutgoingEmailHandler.FailedDescriptor)
                .BlobKind(OutgoingEmailAttachment.BlobKind, static policy => policy with { Thumbnail = null })
                .TableType<OutgoingEmailOutcomeList>()
                .TableType<EmailDeliveryEventList>();

            // --- Background jobs: the job and schedule tables, their machinery's tables and table types,
            // and the notices and client event a completion raises ---
            // spec 0020 §2, §7, §10, §11, §14.4, §14.5, §15. Job's service serves the reads and the four
            // job actions, and its self-scope lets every member read, cancel and confirm the jobs they
            // requested; Schedule's service and rules are closed over the leaf, like the users' and
            // roles'. The schedule state and the worker heartbeat are platform tables, never stacks.
            // The handlers Core owns come with their built-in schedules, retention at 03:00 ahead of
            // the other nightly retentions, and the client metadata lists every registered handler.
            _ = contribution
                .Entity<Job, JobService>()
                .Singleton<IAccessCriteriaProvider, JobAccessCriteria>()
                .Singleton<IClientAssetContributor, JobHandlersClientAssetContributor>()
                .JobHandler<JobRetentionHandler>()
                .BuiltInSchedule(JobRetentionHandler.ScheduleId, JobRetentionHandler.Key, JobRetentionHandler.Cron)
                .JobHandler<TreeVerifyHandler>()
                .BuiltInSchedule(TreeVerifyHandler.ScheduleId, TreeVerifyHandler.Key, TreeVerifyHandler.Cron)
                .Entity<Schedule, ScheduleService<Schedule>>()
                .Validator<Schedule, ScheduleRules<Schedule>>()
                .Model<JobConfiguration>()
                .Model(typeof(ScheduleConfiguration<>))
                .Model<ScheduleStateConfiguration>()
                .Model<JobWorkerStateConfiguration>()
                .TableType<JobRequestList>()
                .TableType<JobOutcomeList>()
                .TableType<JobProgressList>()
                .TableType<JobLeaseList>()
                .TableType<ScheduleNextList>()
                .NotificationType(JobNotices.FailedDescriptor)
                .NotificationType(JobNotices.PendingConfirmationDescriptor)
                .NotificationType(JobNotices.HeldDescriptor)
                .NotificationType(JobNotices.GapDescriptor)
                .NotificationType(JobNotices.SchedulePausedDescriptor)
                .ClientEvent(JobNotices.ChangedEvent);

            // --- The standalone table types: the canonical bulk-operation shapes, and the version-tag guard's ---
            _ = contribution
                .TableType<IdList>()
                .TableType<BigIdList>()
                .TableType<GuidList>()
                .TableType<StringList>()
                .TableType<DateList>()
                .TableType<IdStampList>()
                .TableType<VersionTagList>();
        }
    }
}
