// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Diagnostics;

namespace Tellma.Core.AspNetCore.Observability
{
    /// <summary>
    ///     The inbound trace context of a request a browser may have sent: the <c>traceparent</c>'s trace
    ///     and parent ids are adopted, so a failed call still has a reference the user can quote, but its
    ///     <c>sampled</c> flag is cleared, so recording stays the server sampler's decision, and
    ///     <c>tracestate</c> and <c>baggage</c> are dropped. Outbound injection is the W3C default.
    /// </summary>
    internal sealed class BrowserTraceContextPropagator : DistributedContextPropagator
    {
        /// <summary>The W3C trace context header.</summary>
        public const string TraceParent = "traceparent";

        /// <summary>The propagator outbound requests are injected with.</summary>
        private static readonly DistributedContextPropagator Outbound = CreateW3CPropagator();

        /// <inheritdoc />
        public override IReadOnlyCollection<string> Fields => Outbound.Fields;

        /// <inheritdoc />
        public override void Inject(Activity? activity, object? carrier, PropagatorSetterCallback? setter)
        {
            Outbound.Inject(activity, carrier, setter);
        }

        /// <inheritdoc />
        public override void ExtractTraceIdAndState(
            object? carrier, PropagatorGetterCallback? getter, out string? traceId, out string? traceState)
        {
            // Inbound state is the browser's to make up; none of it is kept.
            traceState = null;
            traceId = null;
            if (getter is null)
            {
                return;
            }

            getter(carrier, TraceParent, out string? value, out IEnumerable<string>? values);
            if (value is null && values is not null && values.Take(2).Count() == 1)
            {
                value = values.First();
            }

            traceId = Adopt(value);
        }

        /// <inheritdoc />
        public override IEnumerable<KeyValuePair<string, string?>>? ExtractBaggage(object? carrier, PropagatorGetterCallback? getter)
        {
            // A browser's baggage would reach every log line of the request; it is dropped.
            return null;
        }

        /// <summary>The <c>traceparent</c> to adopt from an inbound one: its ids, never its flags.</summary>
        /// <param name="traceParent">The inbound header.</param>
        /// <returns>The header to parent the request on, or null to start a new trace.</returns>
        internal static string? Adopt(string? traceParent)
        {
            // A malformed header, or an all-zero id, starts a fresh trace.
            return traceParent is not null
                && ActivityContext.TryParse(traceParent, traceState: null, isRemote: true, out ActivityContext parsed)
                ? $"00-{parsed.TraceId.ToHexString()}-{parsed.SpanId.ToHexString()}-00"
                : null;
        }
    }
}
