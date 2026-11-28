// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.AspNetCore.Csrf;
using Tellma.Core.AspNetCore.Observability;
using Tellma.Core.Tenancy;
using Tellma.Testing.Support.Observability;

namespace Tellma.Core.AspNetCore.Tests.Observability
{
    /// <summary>
    ///     The host's checked-in alert queries, checked back against the tenancy and the web host's
    ///     instruments: every metric, dimension and compared value resolves to something the host can
    ///     emit, and every instrument meant to back an alert backs one.
    /// </summary>
    public sealed class AlertQueryTests
    {
        /// <summary>The types whose constants name the host's instruments and tags.</summary>
        private static readonly Type[] TelemetryNames = [typeof(TenancyTelemetryNames), typeof(HostTelemetryNames)];

        /// <summary>The checked-in queries, copied beside the suite.</summary>
        private static IReadOnlyList<AlertQuery> Queries => AlertQueries.Load(Path.Combine(AppContext.BaseDirectory, "AlertQueries"));

        /// <summary>The instruments: every constant but the meters' names and the tag keys.</summary>
        private static HashSet<string> Instruments => AlertQueries.Constants(
            TelemetryNames, static name => name != nameof(TenancyTelemetryNames.MeterName) && !name.EndsWith("Tag", StringComparison.Ordinal));

        [Fact]
        public void Every_alert_query_references_only_instruments_tags_and_values_the_host_emits()
        {
            IReadOnlyList<AlertQuery> queries = Queries;
            Assert.NotEmpty(queries);

            Assert.Empty(AlertQueries.Unresolved(
                queries,
                Instruments,
                AlertQueries.Constants(TelemetryNames, static name => name.EndsWith("Tag", StringComparison.Ordinal)),
                EmittableTagValues()));
        }

        [Fact]
        public void Every_instrument_but_the_workload_s_sizes_backs_an_alert()
        {
            // These size the workload or count a normal step of signing in; everything else must
            // answer some operational question.
            Assert.Equal(
                [HostTelemetryNames.StepUpChallenges, HostTelemetryNames.SessionOperations,
                 TenancyTelemetryNames.CatalogTenants, TenancyTelemetryNames.Scopes],
                AlertQueries.Uncovered(Queries, Instruments));
        }

        /// <summary>Every tag value the host can emit.</summary>
        /// <returns>The values.</returns>
        private static HashSet<string> EmittableTagValues()
        {
            // The constant spellings: refresh and membership outcomes, session operations, sources
            // and outcomes, and the CSRF rules.
            HashSet<string> values = AlertQueries.Constants(
                [typeof(TenancyMetrics), typeof(HostMetrics), typeof(CsrfRules)], static _ => true);

            // The spellings that live in a switch, so a rename in code cannot silently rewrite a
            // dimension: driving the switch is what ties them to the queries.
            foreach (TenantResolutionOutcome outcome in Enum.GetValues<TenantResolutionOutcome>())
            {
                _ = values.Add(TenancyMetrics.OutcomeValue(outcome));
            }

            foreach (TenantMembershipWriteSource source in Enum.GetValues<TenantMembershipWriteSource>())
            {
                _ = values.Add(TenancyMetrics.SourceValue(source));
            }

            foreach (TenantScopeKind kind in Enum.GetValues<TenantScopeKind>())
            {
                _ = values.Add(TenancyMetrics.KindValue(kind));
            }

            return values;
        }
    }
}
