// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tellma.Core.Abstractions.Settings;

namespace Tellma.Core.Testing.Settings
{
    /// <summary>Registers <see cref="FixedTenantSettingsCache" /> in a composed host.</summary>
    public static class FixedTenantSettingsServiceCollectionExtensions
    {
        /// <summary>
        ///     Replaces the composed tenant settings with fixed ones, so a host over an in-memory
        ///     catalog completes its tenant scopes without reaching a tenant database.
        /// </summary>
        /// <param name="services">The composed services; call it after the composition.</param>
        /// <param name="configure">Adjusts the settings served; null serves them as built.</param>
        /// <returns>The services, for chaining.</returns>
        public static IServiceCollection UseFixedTenantSettings(this IServiceCollection services, Func<TenantSettings, TenantSettings>? configure = null)
        {
            ArgumentNullException.ThrowIfNull(services);
            services.Replace(ServiceDescriptor.Singleton(new FixedTenantSettingsOptions { Configure = configure }));
            services.Replace(ServiceDescriptor.Scoped<ITenantSettingsCache, FixedTenantSettingsCache>());
            return services;
        }
    }
}
