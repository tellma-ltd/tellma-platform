// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Email;
using Tellma.Core.Abstractions.Identity;
using Tellma.Core.Abstractions.Localization;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Access;

namespace Tellma.Core.Users
{
    // A distribution closes UserService over its own leaf, so the service's constructor is public
    // and can name only public types; the platform's internal collaborators reach it through this
    // one public bundle, which only the platform can build.
    /// <summary>
    ///     What the users' stack service needs from the platform beyond its pipeline: the identity
    ///     server, the membership hints, the links, the language catalogue and value sets its rules
    ///     check against, the read path of the invitation status, and what the welcome email is
    ///     rendered and enqueued with. One per scope.
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
        /// <param name="connection">The scope's user connection, which names the caller who invites.</param>
        /// <param name="outbox">The scope's email outbox, which the welcome emails are enqueued through.</param>
        /// <param name="negotiator">Negotiates each invited user's localization.</param>
        /// <param name="localizers">Creates the localizer of Core's server resources, which hold the welcome email's text.</param>
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
            IScopeConnection connection,
            IEmailOutbox outbox,
            ILocalizationNegotiator negotiator,
            IStringLocalizerFactory localizers,
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
            Connection = connection ?? throw new ArgumentNullException(nameof(connection));
            Outbox = outbox ?? throw new ArgumentNullException(nameof(outbox));
            Negotiator = negotiator ?? throw new ArgumentNullException(nameof(negotiator));
            Localizers = localizers ?? throw new ArgumentNullException(nameof(localizers));
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

        /// <summary>The guarded batch runner the invitation status reads through.</summary>
        internal IGuardedBatchRunner Runner { get; }

        /// <summary>The securable registry.</summary>
        internal ISecurableRegistry Securables { get; }

        /// <summary>The scope's access evaluator, whose read decision the invitation status reads under.</summary>
        internal AccessEvaluator Evaluator { get; }

        /// <summary>The scope's user connection, whose connected caller names who invites.</summary>
        internal IScopeConnection Connection { get; }

        /// <summary>The scope's email outbox, which the welcome emails are enqueued through.</summary>
        internal IEmailOutbox Outbox { get; }

        /// <summary>Negotiates each invited user's localization from their preferences and the tenant's settings.</summary>
        internal ILocalizationNegotiator Negotiator { get; }

        /// <summary>Creates the localizer of Core's server resources.</summary>
        internal IStringLocalizerFactory Localizers { get; }

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
    }
}
