// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Diagnostics.Metrics;
using Tellma.Core.Abstractions.Access;

namespace Tellma.Core.Access
{
    // The instruments are spec 0014 §13's decision, witness, set-build, guard and connect rows.
    /// <summary>
    ///     The permission set's, the evaluator's and the user connection's instruments on the core
    ///     meter: the decisions taken and their outcomes, the responses that carried no access
    ///     witness, how long a build took, how many grants it produced, the stored permissions that
    ///     grant nothing, the security guards that refused a write, each connect prologue's result and
    ///     how long it took. No instrument carries a
    ///     tenant, a user, a role or a permission; those are log properties.
    /// </summary>
    internal sealed class AccessMetrics
    {
        /// <summary>The unit of a duration.</summary>
        private const string Seconds = "s";

        /// <summary>The decisions taken for the scope's caller, by outcome.</summary>
        private readonly Counter<long> _decisions;

        /// <summary>The successful responses that carried no access witness.</summary>
        private readonly Counter<long> _witnessMissing;

        /// <summary>How long each build took.</summary>
        private readonly Histogram<double> _buildDuration;

        /// <summary>The grants of each set built.</summary>
        private readonly Histogram<long> _setGrants;

        /// <summary>The permissions found to grant nothing.</summary>
        private readonly Counter<long> _unresolved;

        /// <summary>The security guards that refused a write, by guard.</summary>
        private readonly Counter<long> _guards;

        /// <summary>The connect prologues' results.</summary>
        private readonly Counter<long> _connectResults;

        /// <summary>How long each connect prologue took.</summary>
        private readonly Histogram<double> _prologueDuration;

        /// <summary>Creates the instruments.</summary>
        /// <param name="meterFactory">The host's meter factory, which owns the meter's lifetime.</param>
        public AccessMetrics(IMeterFactory meterFactory)
        {
            ArgumentNullException.ThrowIfNull(meterFactory);

            Meter meter = meterFactory.Create(AccessTelemetryNames.MeterName);

            _decisions = meter.CreateCounter<long>(
                AccessTelemetryNames.Decisions,
                "{decision}",
                "Access decisions taken for the scope's caller, by outcome.");

            _witnessMissing = meter.CreateCounter<long>(
                AccessTelemetryNames.WitnessMissing,
                "{response}",
                "Successful responses replaced with an error because the endpoint's securable, or the member witness, was never evaluated.");

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
            _guards = meter.CreateCounter<long>(
                AccessTelemetryNames.GuardsTriggered,
                "{guard}",
                "Security guards that refused a write: an invariant of the security tables, the security lock, or a validator's self-protection or escalation rule, by guard.");

            _connectResults = meter.CreateCounter<long>(
                AccessTelemetryNames.ConnectResults,
                "{connect}",
                "Connect prologue results: warm, cold, stale by part, refused, or a guard that failed twice in one call.");

            _prologueDuration = meter.CreateHistogram<double>(
                AccessTelemetryNames.ConnectPrologueDuration,
                Seconds,
                "Duration of the round trip that carried a connect prologue, by prologue variant.");
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

        /// <summary>The tag value of a decision's outcome.</summary>
        /// <param name="outcome">The outcome.</param>
        /// <returns>The value.</returns>
        /// <exception cref="ArgumentOutOfRangeException">The outcome is not one of the defined outcomes.</exception>
        public static string OutcomeValue(AccessOutcome outcome)
        {
            return outcome switch
            {
                AccessOutcome.Denied => AccessTelemetryNames.OutcomeDenied,
                AccessOutcome.Filtered => AccessTelemetryNames.OutcomeFiltered,
                AccessOutcome.Unrestricted => AccessTelemetryNames.OutcomeUnrestricted,
                _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "The access outcome is not defined."),
            };
        }

        /// <summary>Counts one decision taken for the scope's caller.</summary>
        /// <param name="outcome">Its outcome.</param>
        public void CountDecision(AccessOutcome outcome)
        {
            _decisions.Add(1, new KeyValuePair<string, object?>(AccessTelemetryNames.OutcomeTag, OutcomeValue(outcome)));
        }

        /// <summary>Counts one successful response that carried no access witness.</summary>
        public void CountMissingWitness()
        {
            _witnessMissing.Add(1);
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

        /// <summary>Counts one security guard that refused a write.</summary>
        /// <param name="guard">The guard's tag value, one of the <c>Guard…</c> values of <see cref="AccessTelemetryNames" />.</param>
        public void CountGuard(string guard)
        {
            _guards.Add(1, new KeyValuePair<string, object?>(AccessTelemetryNames.GuardTag, guard));
        }

        /// <summary>Counts one connect result.</summary>
        /// <param name="result">The result's tag value, one of the <c>Result…</c> values of <see cref="AccessTelemetryNames" />.</param>
        public void CountConnect(string result)
        {
            _connectResults.Add(1, new KeyValuePair<string, object?>(AccessTelemetryNames.ResultTag, result));
        }

        /// <summary>Records how long a round trip that carried a connect prologue took.</summary>
        /// <param name="variant">The prologue's variant tag value.</param>
        /// <param name="duration">The round trip's duration.</param>
        public void RecordPrologue(string variant, TimeSpan duration)
        {
            _prologueDuration.Record(duration.TotalSeconds, new KeyValuePair<string, object?>(AccessTelemetryNames.VariantTag, variant));
        }
    }
}
