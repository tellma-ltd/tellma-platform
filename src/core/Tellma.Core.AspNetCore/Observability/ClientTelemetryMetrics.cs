// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Frozen;
using System.Diagnostics.Metrics;
using Tellma.Core.Abstractions.Api;

namespace Tellma.Core.AspNetCore.Observability
{
    /// <summary>
    ///     The client telemetry relay's two counters, on the web host's meter. Each tag takes a closed
    ///     set of values: a value the client sends outside its set counts as <see cref="Other" />, and a
    ///     value inside it is replaced by the set's own string, so no client string ever becomes a tag.
    /// </summary>
    internal sealed class ClientTelemetryMetrics
    {
        /// <summary>The tag value of a client value outside the tag's closed set, or of a source or outcome the item does not carry.</summary>
        public const string Other = "other";

        /// <summary>The kind tag of an error that is not an API error: one whose item carries no kind.</summary>
        public const string NoKind = "none";

        /// <summary>How an error reached the client's error handler.</summary>
        public static readonly FrozenSet<string> Sources = new[] { "api", "render", "global" }.ToFrozenSet(StringComparer.Ordinal);

        /// <summary>
        ///     The kinds of an API error the client reports, which the client's own list of API error
        ///     kinds defines; a test reads that list and holds this set to it.
        /// </summary>
        public static readonly FrozenSet<string> ErrorKinds = new[]
        {
            "cancelled", "offline", "unauthenticated", "step-up", "forbidden", "not-found", "tenant-not-found",
            "tenant-unavailable", "read-only", "validation", "query-invalid", "bad-request", "conflict", "confirmation",
            "limit", "client-outdated", "unavailable", "partial-failure", "server",
        }.ToFrozenSet(StringComparer.Ordinal);

        /// <summary>How a slow request ended, or that it was still pending when the page was hidden.</summary>
        public static readonly FrozenSet<string> Outcomes = new[] { "ok", "error", "cancelled", "pending" }.ToFrozenSet(StringComparer.Ordinal);

        private readonly Counter<long> _errors;
        private readonly Counter<long> _slowRequests;

        /// <summary>Creates the instruments.</summary>
        /// <param name="meterFactory">The meter factory.</param>
        public ClientTelemetryMetrics(IMeterFactory meterFactory)
        {
            ArgumentNullException.ThrowIfNull(meterFactory);
            Meter meter = meterFactory.Create(ClientTelemetryNames.MeterName);
            _errors = meter.CreateCounter<long>(
                ClientTelemetryNames.Errors,
                unit: "{error}",
                description: "Errors the SPA reported through the relay, by how they reached its error handler and by API error kind.");
            _slowRequests = meter.CreateCounter<long>(
                ClientTelemetryNames.SlowRequests,
                unit: "{request}",
                description: "SPA requests that reached 3 s, reported through the relay, by how they ended.");
        }

        /// <summary>Counts an error item the relay accepted.</summary>
        /// <param name="source">The value of the item's kept <c>source</c> property; null when it keeps none.</param>
        /// <param name="kind">The value of the item's kept <c>kind</c> property; null when it keeps none, which is not an API error.</param>
        public void RecordError(object? source, object? kind)
        {
            _errors.Add(
                1,
                new KeyValuePair<string, object?>(ClientTelemetryNames.SourceTag, Closed(Sources, source)),
                new KeyValuePair<string, object?>(ClientTelemetryNames.KindTag, kind is null ? NoKind : Closed(ErrorKinds, kind)));
        }

        /// <summary>Counts a slow request the relay accepted.</summary>
        /// <param name="outcome">The value of the item's kept <c>outcome</c> property; null when it keeps none.</param>
        public void RecordSlowRequest(object? outcome)
        {
            _slowRequests.Add(1, new KeyValuePair<string, object?>(ClientTelemetryNames.OutcomeTag, Closed(Outcomes, outcome)));
        }

        /// <summary>The set's own string equal to a client value, or <see cref="Other" />.</summary>
        /// <param name="set">The closed set.</param>
        /// <param name="value">The client's value: text, a number or a boolean; null for none.</param>
        /// <returns>The tag value.</returns>
        private static string Closed(FrozenSet<string> set, object? value)
        {
            return value is string text && set.TryGetValue(text, out string? own) ? own : Other;
        }
    }
}
