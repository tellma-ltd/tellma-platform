// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Logging;
using System.Diagnostics.CodeAnalysis;

namespace Tellma.Core.AspNetCore.Observability
{
    /// <summary>The records the client telemetry relay writes for the SPA, under the relay's own logger category.</summary>
    /// <remarks>
    ///     Each record's logger scope carries the item's dimensions and properties, which no message
    ///     template renders. A message names only what the relay checked: an event's name, which
    ///     follows the name grammar, and an error's class and route, each reduced to the value grammar
    ///     (at most 64 letters, digits and <c>_ . : / @ + -</c>) or else <see cref="Other" />, so no
    ///     client text breaks a log line or carries a terminal's control sequence.
    /// </remarks>
    internal static partial class ClientTelemetryLog
    {
        /// <summary>
        ///     The log attribute that makes an exported record a custom event, named by its value, in
        ///     Application Insights.
        /// </summary>
        public const string CustomEventNameAttribute = "microsoft.custom_event.name";

        /// <summary>The value a message names for a dimension the item does not carry.</summary>
        public const string None = "(none)";

        /// <summary>The value a message names for a client value outside the value grammar.</summary>
        public const string Other = "(other)";

        /// <summary>
        ///     The custom event's record: its message template is the attribute alone, so the event's
        ///     name is both the attribute and the rendered message. Defined by hand, since a generated
        ///     message cannot name a parameter after a dotted attribute.
        /// </summary>
        [SuppressMessage(
            "Naming",
            "CA1727:Use PascalCase for named placeholders",
            Justification = "The placeholder is the exporter's attribute, whose name is fixed and dotted.")]
        private static readonly Action<ILogger, string, Exception?> ClientEventRecord = LoggerMessage.Define<string>(
            LogLevel.Information,
            new EventId(0, "ClientEvent"),
            "{" + CustomEventNameAttribute + "}");

        /// <summary>An event or a timing the SPA recorded, exported as a custom event.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="name">The item's name.</param>
        public static void ClientEvent(ILogger logger, string name)
        {
            ClientEventRecord(logger, name, null);
        }

        /// <summary>An error the SPA captured.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="errorClass">The error's class within the value grammar, or <see cref="None" /> or <see cref="Other" />.</param>
        /// <param name="clientRoute">The route template of the page it happened on within the value grammar, or <see cref="None" /> or <see cref="Other" />.</param>
        [LoggerMessage(
            EventName = "ClientError",
            Level = LogLevel.Warning,
            Message = "Client error {ErrorClass} on {ClientRoute}")]
        public static partial void ClientError(ILogger logger, string errorClass, string clientRoute);

        /// <summary>A batch whose items the relay dropped in part; the scope counts them by reason.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="count">How many items it dropped.</param>
        [LoggerMessage(
            EventName = "ClientTelemetryDropped",
            Level = LogLevel.Information,
            Message = "Client telemetry dropped {Count} items")]
        public static partial void ItemsDropped(ILogger logger, int count);
    }
}
