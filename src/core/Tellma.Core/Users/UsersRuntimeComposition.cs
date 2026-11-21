// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Identity;
using Tellma.Core.Abstractions.Localization;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Access;
using Tellma.Core.Crud;
using Tellma.Core.Data.Queries;

namespace Tellma.Core.Users
{
    /// <summary>
    ///     Registers what the users' stack service needs beyond <see cref="Composition.CoreFeature" />'s
    ///     registration of the stack itself: its instruments and the bundle of platform collaborators
    ///     its public constructor takes.
    /// </summary>
    internal static class UsersRuntimeComposition
    {
        /// <summary>Registers the instruments and the collaborators' bundle; each registration is idempotent.</summary>
        /// <param name="services">The composition's services.</param>
        public static void Register(IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services);

            // --- The instruments, one set per process ---
            services.TryAddSingleton<UsersMetrics>();

            // --- The collaborators' bundle, one per scope, built here since its constructor is the platform's own ---
            services.TryAddScoped(static provider => new UserServiceDependencies(
                provider,
                provider.GetRequiredService<IIdentityServerClient>(),
                provider.GetRequiredService<ITenantMembershipDirectory>(),
                provider.GetRequiredService<ITellmaLinks>(),
                provider.GetRequiredService<ILanguageCatalog>(),
                provider.GetRequiredService<IValueSetRegistry>(),
                provider.GetRequiredService<IGuardedBatchRunner>(),
                provider.GetRequiredService<ISecurableRegistry>(),
                provider.GetRequiredService<AccessEvaluator>(),
                provider.GetRequiredService<IAccessGuards>(),
                provider.GetRequiredService<IRequestContextAccessor>(),
                provider.GetRequiredService<WriteFrames>(),
                provider.GetRequiredService<IOpenWriteHost>(),
                provider.GetRequiredService<IOptions<AccessOptions>>(),
                provider.GetRequiredService<IEntityMetadataProvider>(),
                provider.GetRequiredService<QueryexSchemaProvider>(),
                provider.GetRequiredService<TimeProvider>(),
                provider.GetRequiredService<UsersMetrics>(),
                provider.GetRequiredService<ILoggerFactory>()));
        }
    }
}
