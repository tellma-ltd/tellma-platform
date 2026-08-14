// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Email
{
    /// <summary>
    ///     The wire spellings of the email telemetry tag values that more than one assembly has to
    ///     produce.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Here for the same reason <see cref="EmailTelemetryNames" /> is. Almost every email
    ///         measurement comes from the pipeline, but an adapter that drops a delivery event at
    ///         translation meters that event itself, and it must spell the dimension exactly as the
    ///         pipeline would or the two halves of one instrument disagree. Adapters reference only
    ///         this assembly, so a shared table is the only way to say that once — two hand-written
    ///         copies drift apart silently, and the drift surfaces as an alert that quietly matches
    ///         nothing.
    ///     </para>
    ///     <para>
    ///         Values are spelled out rather than derived from the enum member names: a rename in
    ///         code must never rewrite a dimension that alert queries are written against.
    ///     </para>
    /// </remarks>
    public static class EmailTelemetryTagValues
    {
        /// <summary>
        ///     The <see cref="EmailTelemetryNames.EventTypeTag" /> value for a delivery-event type.
        /// </summary>
        /// <param name="type">The event type to spell.</param>
        /// <returns>A lowercase snake_case name, stable across releases because dashboards key on it.</returns>
        public static string ToTagValue(EmailDeliveryEventType type)
        {
            return type switch
            {
                EmailDeliveryEventType.Delivered => "delivered",
                EmailDeliveryEventType.Deferred => "deferred",
                EmailDeliveryEventType.Bounced => "bounced",
                EmailDeliveryEventType.Dropped => "dropped",
                EmailDeliveryEventType.Failed => "failed",
                EmailDeliveryEventType.Opened => "opened",
                EmailDeliveryEventType.Clicked => "clicked",
                EmailDeliveryEventType.SpamReported => "spam_reported",
                EmailDeliveryEventType.Other => "other",
                _ => "unknown",
            };
        }
    }
}
