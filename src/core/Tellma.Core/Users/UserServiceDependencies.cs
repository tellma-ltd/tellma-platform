// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Email;
using Tellma.Core.Abstractions.Identity;
using Tellma.Core.Abstractions.Localization;
using Tellma.Core.Abstractions.Notifications;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Access;
using Tellma.Core.Crud;
using Tellma.Core.Data.Queries;

namespace Tellma.Core.Users
{
    // A distribution closes UserService over its own leaf, so the service's constructor is public
    // and can name only public types; the platform's internal collaborators reach it through this
    // one public bundle, which only the platform can build.
    /// <summary>
    ///     What the users' stack service needs from the platform beyond its pipeline: the identity
    ///     server, the membership hints, the links, the language catalogue and value sets its rules
    ///     check against, the guarded batches of the invitation status and the self-service writes,
    ///     the open frame a profile edit enlists into, what the welcome email is rendered and
    ///     enqueued with, and the notification registries the invitation's notice, the test message
    ///     and another user's notification settings are checked against. One per scope.
    /// </summary>
    /// <remarks>
    ///     The platform registers and builds it; a service that derives from the users' stack service
    ///     receives it through its own constructor and passes it on unchanged.
    /// </remarks>
    public sealed class UserServiceDependencies
    {
        /// <summary>Gathers the collaborators.</summary>
        /// <param name="services">The scope's services, which the session listeners resolve from when a deactivation needs them.</param>
        /// <param name="identity">The identity-server client.</param>
        /// <param name="memberships">The tenant-membership hint directory.</param>
        /// <param name="links">The server-built links.</param>
        /// <param name="languages">The language catalogue.</param>
        /// <param name="valueSets">The value sets.</param>
        /// <param name="runner">The guarded batch runner.</param>
        /// <param name="securables">The securable registry.</param>
        /// <param name="evaluator">The scope's access evaluator.</param>
        /// <param name="guards">The access guards a write of the users' table joins.</param>
        /// <param name="accessor">The scope's request context.</param>
        /// <param name="frames">The scope's open write frames, whose verdict on the tenant's state a self-service write takes.</param>
        /// <param name="openWrites">The open frame of the calling action, which a profile edit enlists into.</param>
        /// <param name="accessOptions">The access options: the preference bags' caps.</param>
        /// <param name="metadata">The entity metadata, whose user leaf a profile edit loads whole.</param>
        /// <param name="schemas">The query schemas, which declare the columns a profile edit loads.</param>
        /// <param name="connection">The scope's user connection, which names the caller who invites.</param>
        /// <param name="outbox">The scope's email outbox, which the welcome emails are enqueued through.</param>
        /// <param name="negotiator">Negotiates each invited user's localization.</param>
        /// <param name="localizers">Creates the localizer of Core's server resources, which hold the welcome email's text.</param>
        /// <param name="notificationTypes">The notification types another user's notification settings are checked against.</param>
        /// <param name="notificationChannels">The notification channels a test message and another user's notification settings are checked against.</param>
        /// <param name="notificationsOptions">The notifications options: how many recipients one notification request names.</param>
        /// <param name="time">The host's clock.</param>
        /// <param name="metrics">The users' stack's instruments.</param>
        /// <param name="loggerFactory">Creates the users' stack's logger.</param>
        internal UserServiceDependencies(
            IServiceProvider services,
            IIdentityServerClient identity,
            ITenantMembershipDirectory memberships,
            ITellmaLinks links,
            ILanguageCatalog languages,
            IValueSetRegistry valueSets,
            IGuardedBatchRunner runner,
            ISecurableRegistry securables,
            AccessEvaluator evaluator,
            IAccessGuards guards,
            IRequestContextAccessor accessor,
            WriteFrames frames,
            IOpenWriteHost openWrites,
            IOptions<AccessOptions> accessOptions,
            IEntityMetadataProvider metadata,
            QueryexSchemaProvider schemas,
            IScopeConnection connection,
            IEmailOutbox outbox,
            ILocalizationNegotiator negotiator,
            IStringLocalizerFactory localizers,
            INotificationTypeRegistry notificationTypes,
            INotificationChannelRegistry notificationChannels,
            IOptions<NotificationsOptions> notificationsOptions,
            TimeProvider time,
            UsersMetrics metrics,
            ILoggerFactory loggerFactory)
        {
            Services = services ?? throw new ArgumentNullException(nameof(services));
            Identity = identity ?? throw new ArgumentNullException(nameof(identity));
            Memberships = memberships ?? throw new ArgumentNullException(nameof(memberships));
            Links = links ?? throw new ArgumentNullException(nameof(links));
            Languages = languages ?? throw new ArgumentNullException(nameof(languages));
            ValueSets = valueSets ?? throw new ArgumentNullException(nameof(valueSets));
            Runner = runner ?? throw new ArgumentNullException(nameof(runner));
            Securables = securables ?? throw new ArgumentNullException(nameof(securables));
            Evaluator = evaluator ?? throw new ArgumentNullException(nameof(evaluator));
            Guards = guards ?? throw new ArgumentNullException(nameof(guards));
            Accessor = accessor ?? throw new ArgumentNullException(nameof(accessor));
            Frames = frames ?? throw new ArgumentNullException(nameof(frames));
            OpenWrites = openWrites ?? throw new ArgumentNullException(nameof(openWrites));
            AccessOptions = (accessOptions ?? throw new ArgumentNullException(nameof(accessOptions))).Value;
            Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
            Schemas = schemas ?? throw new ArgumentNullException(nameof(schemas));
            Connection = connection ?? throw new ArgumentNullException(nameof(connection));
            Outbox = outbox ?? throw new ArgumentNullException(nameof(outbox));
            Negotiator = negotiator ?? throw new ArgumentNullException(nameof(negotiator));
            Localizers = localizers ?? throw new ArgumentNullException(nameof(localizers));
            NotificationTypes = notificationTypes ?? throw new ArgumentNullException(nameof(notificationTypes));
            NotificationChannels = notificationChannels ?? throw new ArgumentNullException(nameof(notificationChannels));
            NotificationsOptions = (notificationsOptions ?? throw new ArgumentNullException(nameof(notificationsOptions))).Value;
            Time = time ?? throw new ArgumentNullException(nameof(time));
            Metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
            Logger = (loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory))).CreateLogger(UsersLog.Category);
        }

        /// <summary>The scope's services, which the session listeners resolve from.</summary>
        internal IServiceProvider Services { get; }

        /// <summary>The identity-server client.</summary>
        internal IIdentityServerClient Identity { get; }

        /// <summary>The tenant-membership hint directory.</summary>
        internal ITenantMembershipDirectory Memberships { get; }

        /// <summary>The server-built links.</summary>
        internal ITellmaLinks Links { get; }

        /// <summary>The language catalogue the preferred language and region are checked against.</summary>
        internal ILanguageCatalog Languages { get; }

        /// <summary>The value sets the preferred calendar and time zone are checked against.</summary>
        internal IValueSetRegistry ValueSets { get; }

        /// <summary>The guarded batch runner the invitation status and the self-service operations run through.</summary>
        internal IGuardedBatchRunner Runner { get; }

        /// <summary>The securable registry.</summary>
        internal ISecurableRegistry Securables { get; }

        /// <summary>The scope's access evaluator, whose decisions the invitation status, the access summary and the preferences read under.</summary>
        internal AccessEvaluator Evaluator { get; }

        /// <summary>The access guards a write of the users' table joins: the security lock and the invariants.</summary>
        internal IAccessGuards Guards { get; }

        /// <summary>The scope's request context.</summary>
        internal IRequestContextAccessor Accessor { get; }

        /// <summary>The scope's open write frames, whose verdict on the tenant's state a self-service write takes.</summary>
        internal WriteFrames Frames { get; }

        /// <summary>The open frame of the calling action, which a profile edit enlists into and persists.</summary>
        internal IOpenWriteHost OpenWrites { get; }

        /// <summary>The access options: the preference bags' caps.</summary>
        internal AccessOptions AccessOptions { get; }

        /// <summary>The entity metadata, whose user leaf a profile edit loads whole.</summary>
        internal IEntityMetadataProvider Metadata { get; }

        /// <summary>The query schemas, which declare the columns a profile edit loads.</summary>
        internal QueryexSchemaProvider Schemas { get; }

        /// <summary>The scope's user connection, whose connected caller names who invites.</summary>
        internal IScopeConnection Connection { get; }

        /// <summary>The scope's email outbox, which the welcome emails are enqueued through.</summary>
        internal IEmailOutbox Outbox { get; }

        /// <summary>Negotiates each invited user's localization from their preferences and the tenant's settings.</summary>
        internal ILocalizationNegotiator Negotiator { get; }

        /// <summary>Creates the localizer of Core's server resources.</summary>
        internal IStringLocalizerFactory Localizers { get; }

        /// <summary>The notification types another user's notification settings are checked against.</summary>
        internal INotificationTypeRegistry NotificationTypes { get; }

        /// <summary>The notification channels a test message and another user's notification settings are checked against.</summary>
        internal INotificationChannelRegistry NotificationChannels { get; }

        /// <summary>The notifications options: how many recipients one notification request names.</summary>
        internal NotificationsOptions NotificationsOptions { get; }

        /// <summary>The host's clock.</summary>
        internal TimeProvider Time { get; }

        /// <summary>The users' stack's instruments.</summary>
        internal UsersMetrics Metrics { get; }

        /// <summary>The users' stack's logger.</summary>
        internal ILogger Logger { get; }

        /// <summary>The session-termination listeners, resolved when a deactivation needs them.</summary>
        /// <returns>The listeners, in registration order.</returns>
        internal IReadOnlyList<ISessionTerminationListener> SessionListeners()
        {
            return [.. Services.GetServices<ISessionTerminationListener>()];
        }

        /// <summary>
        ///     The sender of one notification channel, resolved when a test message needs it: the
        ///     host's other senders are never created for it.
        /// </summary>
        /// <param name="key">The channel's key.</param>
        /// <returns>The sender; null when the host registers none for the channel.</returns>
        internal INotificationChannel? ChannelSender(string key)
        {
            foreach (INotificationChannel sender in Services.GetServices<INotificationChannel>())
            {
                if (string.Equals(sender.Key, key, StringComparison.Ordinal))
                {
                    return sender;
                }
            }

            return null;
        }
    }
}
