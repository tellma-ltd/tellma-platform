// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Diagnostics;
using Tellma.Core.Abstractions.Email;

namespace Tellma.Core.Email
{
    /// <summary>
    ///     The email pipeline's tracing surface and the tag-value spellings its instruments share.
    /// </summary>
    internal static class EmailDiagnostics
    {
        /// <summary>The name of the activity that wraps one transport call.</summary>
        internal const string SendActivityName = "email.send";

        /// <summary>The name of the activity that wraps one delivery-event dispatch.</summary>
        internal const string DispatchActivityName = "email.dispatch_events";

        /// <summary>Activity tag: how many messages the wrapped transport call carried.</summary>
        internal const string BatchSizeTag = "email.batch.size";

        /// <summary>Activity tag: how many events the wrapped dispatch carried.</summary>
        internal const string EventCountTag = "email.event.count";

        /// <summary>Activity tag: how many distinct owners the wrapped dispatch fanned out to.</summary>
        internal const string OwnerCountTag = "email.owner.count";

        /// <summary>
        ///     The source hosts add to their OpenTelemetry configuration to see email spans. Static,
        ///     so the type owns no disposable instance state.
        /// </summary>
        internal static readonly ActivitySource ActivitySource = new(EmailTelemetryNames.ActivitySourceName);

        /// <summary>The metric tag value for a send outcome.</summary>
        /// <param name="outcome">The outcome to spell.</param>
        /// <returns>A lowercase snake_case name, stable across releases because dashboards key on it.</returns>
        internal static string ToTagValue(EmailSendOutcome outcome)
        {
            // Spelled out rather than derived from the enum name: a rename in code must never
            // silently rewrite a dimension that alert queries are written against.
            return outcome switch
            {
                EmailSendOutcome.Sent => "sent",
                EmailSendOutcome.TransientFailure => "transient_failure",
                EmailSendOutcome.Rejected => "rejected",
                EmailSendOutcome.Sandboxed => "sandboxed",
                _ => "unknown",
            };
        }

        /// <summary>The metric tag value for an audience.</summary>
        /// <param name="audience">The audience to spell.</param>
        /// <returns>A lowercase name.</returns>
        internal static string ToTagValue(EmailAudience audience)
        {
            return audience switch
            {
                EmailAudience.Internal => "internal",
                EmailAudience.External => "external",
                _ => "unknown",
            };
        }

        /// <summary>The metric tag value for a delivery-event type.</summary>
        /// <param name="type">The event type to spell.</param>
        /// <returns>A lowercase snake_case name.</returns>
        /// <remarks>
        ///     Forwards to the contract assembly's table rather than holding one of its own. Connector
        ///     adapters cannot reference this assembly, yet they meter the same dimension, so this is
        ///     the one tag-value mapping that cannot live here — it is kept reachable through
        ///     <c>EmailDiagnostics</c> only so every call site spells telemetry the same way.
        /// </remarks>
        internal static string ToTagValue(EmailDeliveryEventType type)
        {
            return EmailTelemetryTagValues.ToTagValue(type);
        }

        /// <summary>The metric tag value for the channel a sender served.</summary>
        /// <param name="channel">The channel to spell.</param>
        /// <returns>The matching <c>email.delivery</c> tag value.</returns>
        internal static string ToTagValue(EmailChannel channel)
        {
            return channel == EmailChannel.Sandbox
                ? EmailTelemetryNames.SandboxDelivery
                : EmailTelemetryNames.LiveDelivery;
        }
    }
}
