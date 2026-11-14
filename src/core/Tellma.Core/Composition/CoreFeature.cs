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
using Tellma.Core.Abstractions.Identity;
using Tellma.Core.Abstractions.Jobs;
using Tellma.Core.Abstractions.Localization;
using Tellma.Core.Abstractions.Notifications;
using Tellma.Core.Abstractions.Realtime;
using Tellma.Core.Abstractions.TableTypes;
using Tellma.Core.Access;
using Tellma.Core.Blobs;
using Tellma.Core.Caching;
using Tellma.Core.Calendars;
using Tellma.Core.Data;
using Tellma.Core.Identity;
using Tellma.Core.Jobs;
using Tellma.Core.Localization;
using Tellma.Core.Notifications;
using Tellma.Core.Provisioning;

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

            // --- The platform's own tables in every tenant database, mapped for migrations only ---
            _ = contribution
                .Model<TellmaSchemaRowConfiguration>()
                .Model<TellmaProvisioningRowConfiguration>();

            // --- Access: users and roles with their children, each configuration closed over the leaf ---
            // spec 0014 §1.2. The services of the two stacks join these lines when they land.
            _ = contribution
                .Entity<User>()
                .Entity<Role>()
                .Model(typeof(UserConfiguration<>))
                .Model(typeof(RoleConfiguration<>))
                .Model(typeof(RoleMembershipConfiguration<>))
                .Model(typeof(PermissionConfiguration<>))
                .Model<UserStampConfiguration>()
                .Model<UserPreferenceConfiguration>()
                .TableType<UserPreferenceList>();

            // --- The identity-server client, one per distribution (spec 0018 §1.2, §2) ---
            // Its options and named HTTP client are registered with the platform's options.
            _ = contribution.Singleton<IIdentityServerClient, IdentityServerClient>();

            // --- Access: the securable registry, its client metadata section, and Core's own securables ---
            // spec 0014 §1.2, §4.2. The user stamps are a query root of their own, reached through
            // the caller's grants on the users along the stamp's User navigation. core.User × Read,
            // the owner it needs, is also the users' stack's to register once stacks register their
            // securables; the same registration twice is one.
            _ = contribution
                .Singleton<ISecurableRegistry, SecurableRegistry>()
                .Singleton<IClientAssetContributor, SecurablesClientAssetContributor>()
                .Securables(static securables => securables
                    .Add(CoreResources.User, AccessActions.Read, CoreResources.User)
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

            // --- Blobs: the table every blob reference resolves to; never a stack ---
            _ = contribution.Model<BlobConfiguration>();

            // --- Notifications: the registries, the client metadata and the catalogue Core owns ---
            // spec 0021 §11. Each type is declared by the feature code that raises it; core.user.added
            // is the one this contribution declares for the users' invitation. The two baseline
            // channels and the two client events the notifications and the hub publish come first.
            _ = contribution
                .Singleton<INotificationTypeRegistry, NotificationTypeRegistry>()
                .Singleton<INotificationChannelRegistry, NotificationChannelRegistry>()
                .Singleton<IClientEventRegistry, ClientEventRegistry>()
                .Singleton<IClientAssetContributor, NotificationsClientAssetContributor>()
                .NotificationType(new NotificationTypeDescriptor("core.user.added", "users", Mutable: true, TargetResource: null))
                .NotificationChannel(new NotificationChannelDescriptor(InboxChannel, DefaultEnabled: true))
                .NotificationChannel(new NotificationChannelDescriptor(EmailChannel, DefaultEnabled: false))
                .ClientEvent("inbox.changed")
                .ClientEvent("session.ended");

            // --- Background jobs: the job and schedule tables, their machinery's tables and table types ---
            // spec 0020 §2, §7, §15. Job and Schedule are entities without services until their
            // services land; the schedule state and the worker heartbeat are platform tables, never
            // stacks. The handlers and built-in schedules Core owns are contributed with their handlers.
            _ = contribution
                .Entity<Job>()
                .Entity<Schedule>()
                .Model<JobConfiguration>()
                .Model(typeof(ScheduleConfiguration<>))
                .Model<ScheduleStateConfiguration>()
                .Model<JobWorkerStateConfiguration>()
                .TableType<JobRequestList>()
                .TableType<JobOutcomeList>()
                .TableType<JobProgressList>()
                .TableType<JobLeaseList>()
                .TableType<ScheduleNextList>();

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
