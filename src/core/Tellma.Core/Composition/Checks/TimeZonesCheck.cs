// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Localization;
using Tellma.Core.Localization;
using Tellma.Core.Settings;

namespace Tellma.Core.Composition.Checks
{
    /// <summary>
    ///     <c>core.time-zones</c>: warns, once per zone, about each value of the time-zone set that
    ///     this host cannot resolve as a tenant's zone. It never fails startup.
    /// </summary>
    /// <remarks>
    ///     An unresolvable zone is the sign of a container image with stale time-zone data: the set
    ///     lists the current zones, and a reader who asks for one the host does not know gets their
    ///     tenant's zone instead. It reads the host, not the composition, so a build step without a
    ///     deployment skips it.
    /// </remarks>
    /// <param name="resolves">Reports whether this host resolves a zone id; the suites substitute it.</param>
    internal sealed class TimeZonesCheck(Func<string, bool> resolves) : IStartupCheck
    {
        /// <inheritdoc />
        public string Name => CoreCheckNames.TimeZones;

        /// <inheritdoc />
        public bool RequiresInfrastructure => true;

        /// <summary>
        ///     Whether this host resolves a zone id exactly as a tenant's settings require: its
        ///     time-zone data has the zone and the pinned table maps it to a Windows name.
        /// </summary>
        /// <param name="id">The zone id.</param>
        /// <returns>True when it does.</returns>
        public static bool HostResolves(string id)
        {
            return TenantSettingsStore.TryResolveZone(id, out _, out _);
        }

        /// <inheritdoc />
        public Task<IReadOnlyList<CompositionProblem>> CheckAsync(IServiceProvider services, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(services);

            ILogger logger = services.GetService<ILoggerFactory>()?.CreateLogger<TimeZonesCheck>() ?? NullLogger<TimeZonesCheck>.Instance;
            ValueSetDescriptor? zones = services.GetRequiredService<IValueSetRegistry>().Find(CoreValueSets.TimeZone);
            foreach (ValueSetEntry zone in zones?.Values ?? [])
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!resolves(zone.Value))
                {
                    LocalizationLog.TimeZoneUnresolvable(logger, zone.Value);
                }
            }

            // A warning, never a problem: stale time-zone data degrades one reader's display zone
            // and must not keep the host from starting.
            return Task.FromResult<IReadOnlyList<CompositionProblem>>([]);
        }
    }
}
