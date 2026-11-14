// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Diagnostics.Metrics;
using Tellma.Core.Abstractions.Access;

namespace Tellma.Core.Access
{
    // The instruments are spec 0014 §13's set-build rows.
    /// <summary>
    ///     The permission set's instruments on the core meter: how long a build took, how many grants
    ///     it produced, and the stored permissions that grant nothing. No instrument carries a tenant,
    ///     a user, a role or a permission; those are log properties.
    /// </summary>
    internal sealed class AccessMetrics
    {
        /// <summary>The unit of a duration.</summary>
        private const string Seconds = "s";

        /// <summary>How long each build took.</summary>
        private readonly Histogram<double> _buildDuration;

        /// <summary>The grants of each set built.</summary>
        private readonly Histogram<long> _setGrants;

        /// <summary>The permissions found to grant nothing.</summary>
        private readonly Counter<long> _unresolved;

        /// <summary>Creates the instruments.</summary>
        /// <param name="meterFactory">The host's meter factory, which owns the meter's lifetime.</param>
        public AccessMetrics(IMeterFactory meterFactory)
        {
            ArgumentNullException.ThrowIfNull(meterFactory);

            Meter meter = meterFactory.Create(AccessTelemetryNames.MeterName);

            _buildDuration = meter.CreateHistogram<double>(
                AccessTelemetryNames.SetBuildDuration,
                Seconds,
                "Duration of building one caller's permission set from its stored permissions.");

            _setGrants = meter.CreateHistogram<long>(
                AccessTelemetryNames.SetGrants,
                "{grant}",
                "Grants of each permission set built.");

            _unresolved = meter.CreateCounter<long>(
                AccessTelemetryNames.PermissionsUnresolved,
                "{permission}",
                "Stored permissions found to grant nothing, once per problem and version of the tenant's permissions, by problem.");
        }

        /// <summary>The tag value of a problem code.</summary>
        /// <param name="code">The code.</param>
        /// <returns>The value.</returns>
        /// <exception cref="ArgumentOutOfRangeException">The code is not one of the defined codes.</exception>
        public static string ProblemValue(AccessProblemCode code)
        {
            return code switch
            {
                AccessProblemCode.UnknownResource => AccessTelemetryNames.ProblemUnknownResource,
                AccessProblemCode.UnknownAction => AccessTelemetryNames.ProblemUnknownAction,
                AccessProblemCode.FilterUnsupported => AccessTelemetryNames.ProblemFilterUnsupported,
                AccessProblemCode.FilterInvalid => AccessTelemetryNames.ProblemFilterInvalid,
                AccessProblemCode.VersionUnsupported => AccessTelemetryNames.ProblemVersionUnsupported,
                AccessProblemCode.WildcardResourceOnPublicRole => AccessTelemetryNames.ProblemWildcardResourceOnPublicRole,
                _ => throw new ArgumentOutOfRangeException(nameof(code), code, "The access problem code is not defined."),
            };
        }

        /// <summary>Records one build.</summary>
        /// <param name="duration">How long it took.</param>
        /// <param name="grants">The grants of the set built.</param>
        public void RecordBuild(TimeSpan duration, int grants)
        {
            _buildDuration.Record(duration.TotalSeconds);
            _setGrants.Record(grants);
        }

        /// <summary>Counts one permission found to grant nothing.</summary>
        /// <param name="code">Why.</param>
        public void CountUnresolved(AccessProblemCode code)
        {
            _unresolved.Add(1, new KeyValuePair<string, object?>(AccessTelemetryNames.ProblemTag, ProblemValue(code)));
        }
    }
}
