// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Diagnostics.Metrics;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Realtime;

namespace Tellma.Core.AspNetCore.Realtime
{
    /// <summary>
    ///     The realtime hub's instruments, on the web host's meter: the open connections by hosting
    ///     mode, the connection attempts by outcome, the server's closes by reason and the events
    ///     pushed by name. The tenant and the user are never tags.
    /// </summary>
    internal sealed class RealtimeMetrics
    {
        /// <summary>The outcome of a connection the hub accepted.</summary>
        public const string ConnectedOutcome = "connected";

        /// <summary>The outcome of a connection attempt the front door or the hub refused.</summary>
        public const string RefusedOutcome = "refused";

        /// <summary>The outcome of a client invocation, which the hub always refuses.</summary>
        public const string InvocationRefusedOutcome = "invocation-refused";

        /// <summary>The told tag of a close whose <c>session.ended</c> was handed to the transport first.</summary>
        public const string ToldValue = "true";

        /// <summary>The told tag of a close whose <c>session.ended</c> send failed or did not finish in time.</summary>
        public const string UntoldValue = "false";

        private readonly UpDownCounter<long> _connections;
        private readonly Counter<long> _connects;
        private readonly Counter<long> _closes;
        private readonly Counter<long> _events;

        /// <summary>Creates the instruments.</summary>
        /// <param name="meterFactory">The meter factory.</param>
        public RealtimeMetrics(IMeterFactory meterFactory)
        {
            ArgumentNullException.ThrowIfNull(meterFactory);
            Meter meter = meterFactory.Create(RealtimeTelemetryNames.MeterName);
            _connections = meter.CreateUpDownCounter<long>(
                RealtimeTelemetryNames.Connections,
                unit: "{connection}",
                description: "Hub connections this instance holds open, by hosting mode.");
            _connects = meter.CreateCounter<long>(
                RealtimeTelemetryNames.Connects,
                unit: "{attempt}",
                description: "Hub connection attempts by outcome, and the client invocations the hub refused.");
            _closes = meter.CreateCounter<long>(
                RealtimeTelemetryNames.Closes,
                unit: "{connection}",
                description: "Hub connections the server closed, by why and whether they were told first.");
            _events = meter.CreateCounter<long>(
                ApiTelemetryNames.RealtimeEvents,
                unit: "{event}",
                description: "Client events handed to the hub's transport, by event name.");
        }

        /// <summary>Records a connection the hub accepted.</summary>
        /// <param name="mode">The hosting mode's tag value.</param>
        public void RecordConnected(string mode)
        {
            _connects.Add(1, new KeyValuePair<string, object?>(RealtimeTelemetryNames.OutcomeTag, ConnectedOutcome));
            _connections.Add(1, new KeyValuePair<string, object?>(RealtimeTelemetryNames.ModeTag, mode));
        }

        /// <summary>Records an accepted connection that ended.</summary>
        /// <param name="mode">The hosting mode's tag value.</param>
        public void RecordDisconnected(string mode)
        {
            _connections.Add(-1, new KeyValuePair<string, object?>(RealtimeTelemetryNames.ModeTag, mode));
        }

        /// <summary>Records a connection attempt that was refused.</summary>
        public void RecordRefused()
        {
            _connects.Add(1, new KeyValuePair<string, object?>(RealtimeTelemetryNames.OutcomeTag, RefusedOutcome));
        }

        /// <summary>Records a client invocation the hub refused.</summary>
        public void RecordInvocationRefused()
        {
            _connects.Add(1, new KeyValuePair<string, object?>(RealtimeTelemetryNames.OutcomeTag, InvocationRefusedOutcome));
        }

        /// <summary>Records connections the server closed.</summary>
        /// <param name="reason">Why: a <c>session.ended</c> reason.</param>
        /// <param name="count">How many.</param>
        /// <param name="told">Whether their <c>session.ended</c> was handed to the transport before the close.</param>
        public void RecordClosed(string reason, int count, bool told)
        {
            if (count > 0)
            {
                _closes.Add(
                    count,
                    new KeyValuePair<string, object?>(RealtimeTelemetryNames.ReasonTag, reason),
                    new KeyValuePair<string, object?>(RealtimeTelemetryNames.ToldTag, told ? ToldValue : UntoldValue));
            }
        }

        /// <summary>Records an event handed to the transport.</summary>
        /// <param name="name">The event's registered name.</param>
        public void RecordEvent(string name)
        {
            _events.Add(1, new KeyValuePair<string, object?>(ApiTelemetryNames.EventTag, name));
        }
    }
}
