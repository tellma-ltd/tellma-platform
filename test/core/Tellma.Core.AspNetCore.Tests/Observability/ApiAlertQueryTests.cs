// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Http;
using System.Globalization;
using System.Reflection;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.AspNetCore.Api;
using Tellma.Core.Crud;
using Tellma.Testing.Support.Observability;

namespace Tellma.Core.AspNetCore.Tests.Observability
{
    /// <summary>
    ///     The web surface's checked-in alert queries, checked back against the API's instruments and
    ///     the data layer's round trips per operation: every metric, dimension and compared value
    ///     resolves to something the platform can emit, and every API instrument but the four that
    ///     size the workload or diagnose a client backs an alert.
    /// </summary>
    public sealed class ApiAlertQueryTests
    {
        /// <summary>The checked-in queries, copied beside the suite.</summary>
        private static IReadOnlyList<AlertQuery> Queries => AlertQueries.Load(Path.Combine(AppContext.BaseDirectory, "ApiAlertQueries"));

        /// <summary>The instruments: the API's, and the data layer's round trips per unit of work.</summary>
        private static HashSet<string> Instruments =>
        [
            .. AlertQueries.Constants([typeof(ApiTelemetryNames)], static name => name != nameof(ApiTelemetryNames.MeterName) && !name.EndsWith("Tag", StringComparison.Ordinal)),
            DataTelemetryNames.RoundTrips,
        ];

        [Fact]
        public void Every_alert_query_references_only_instruments_tags_and_values_the_platform_emits()
        {
            IReadOnlyList<AlertQuery> queries = Queries;
            Assert.NotEmpty(queries);

            // --- The tags: the API's, a problem's status and code, and a unit of work's operation ---
            HashSet<string> tags =
            [
                .. AlertQueries.Constants([typeof(ApiTelemetryNames)], static name => name.EndsWith("Tag", StringComparison.Ordinal)),
                ApiMetrics.StatusTag,
                ApiMetrics.CodeTag,
                DataTelemetryNames.OperationTag,
            ];

            // --- The values: the server-error status, the pipeline's operation kinds, the platform's
            // API action names (the kind of a unit of work an API action runs), and the separator a
            // unit of work's operation puts between its resource and its kind ---
            HashSet<string> values =
            [
                StatusCodes.Status500InternalServerError.ToString(CultureInfo.InvariantCulture),
                .. AlertQueries.Constants([typeof(CrudOperationNames)], static _ => true),
                .. PlatformApiActions(),
                ":",
            ];

            Assert.Empty(AlertQueries.Unresolved(queries, Instruments, tags, values));
        }

        [Fact]
        public void Every_instrument_but_the_workload_s_sizes_and_the_client_diagnostics_backs_an_alert()
        {
            // Rate-limited and outdated clients are a normal part of a busy day or a deployment, the
            // duration sizes the workload, unknown members diagnose a client, and the realtime
            // queries read the hub's own instruments rather than its event counter.
            Assert.Equal(
                [ApiTelemetryNames.OperationDuration, ApiTelemetryNames.RequestsRejected, ApiTelemetryNames.UnknownMembers, ApiTelemetryNames.RealtimeEvents],
                AlertQueries.Uncovered(Queries, Instruments));
        }

        /// <summary>The name of every API action the platform's own services and stacks declare.</summary>
        /// <returns>The names.</returns>
        private static IEnumerable<string> PlatformApiActions()
        {
            const BindingFlags Declared = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
            return typeof(CrudOperationNames).Assembly.GetTypes()
                .SelectMany(static type => type.GetMethods(Declared))
                .Select(static method => method.GetCustomAttribute<ApiActionAttribute>()?.Name)
                .OfType<string>();
        }
    }
}
