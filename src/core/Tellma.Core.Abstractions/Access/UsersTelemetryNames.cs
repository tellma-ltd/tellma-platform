// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Access
{
    /// <summary>
    ///     The names of the users stack's instruments and of the identity-server client's, their tags,
    ///     the closed sets of tag values, and the identity client's trace activities, on the platform's
    ///     <see cref="MeterName" /> meter. Dashboards and alerts key on these strings, so they never
    ///     change. No tag carries a tenant or a user.
    /// </summary>
    public static class UsersTelemetryNames
    {
        /// <summary>The meter every Core instrument is created on.</summary>
        public const string MeterName = "Tellma.Core";

        // --- The identity-server client's instruments ---

        /// <summary>
        ///     Counter (<c>{call}</c>): calls of the identity-server client, one per member invoked that
        ///     reached for the server, however many chunks it sent. Tagged
        ///     <see cref="EndpointTag" /> and <see cref="OutcomeTag" />.
        /// </summary>
        public const string IdentityCalls = "tellma.identity.calls";

        /// <summary>Histogram (<c>s</c>): how long each identity-server client call took, chunks and token fetches included. Tagged as <see cref="IdentityCalls" />.</summary>
        public const string IdentityCallDuration = "tellma.identity.call.duration";

        /// <summary>
        ///     The identity endpoint tag: <see cref="EndpointInvite" />, <see cref="EndpointDelivery" /> or
        ///     <see cref="EndpointServiceAccount" />.
        /// </summary>
        public const string EndpointTag = "identity.endpoint";

        /// <summary>
        ///     The outcome tag of an identity call: <see cref="OutcomeOk" />, <see cref="OutcomePartial" />,
        ///     <see cref="OutcomeUnavailable" />, <see cref="OutcomeError" /> or <see cref="OutcomeCancelled" />.
        /// </summary>
        public const string OutcomeTag = "outcome";

        /// <summary>The endpoint value of the bulk invite.</summary>
        public const string EndpointInvite = "invite";

        /// <summary>The endpoint value of the bulk delivery status.</summary>
        public const string EndpointDelivery = "delivery";

        /// <summary>The endpoint value of creating and deleting service accounts.</summary>
        public const string EndpointServiceAccount = "service-account";

        /// <summary>The outcome value of a call that answered in full.</summary>
        public const string OutcomeOk = "ok";

        /// <summary>The outcome value of a bulk call that answered the prefix of the chunks completed before a failure or a cancellation.</summary>
        public const string OutcomePartial = "partial";

        /// <summary>The outcome value of a call that found the identity server unreachable: a transport failure, a timeout or a <c>5xx</c>.</summary>
        public const string OutcomeUnavailable = "unavailable";

        /// <summary>The outcome value of a call the identity server refused: a <c>4xx</c>, a second <c>401</c>, or an answer the client cannot read.</summary>
        public const string OutcomeError = "error";

        /// <summary>The outcome value of a call its caller cancelled before anything completed.</summary>
        public const string OutcomeCancelled = "cancelled";

        // --- The identity-server client's trace activities, one per chunk ---

        /// <summary>The activity of one chunk of the bulk invite. Tagged <see cref="IdentityCountTag" /> and <see cref="IdentityOutcomeTag" />.</summary>
        public const string IdentityInviteActivity = "identity.invite";

        /// <summary>The activity of one chunk of the bulk delivery status. Tagged as <see cref="IdentityInviteActivity" />.</summary>
        public const string IdentityDeliveryActivity = "identity.delivery";

        /// <summary>The chunk activity's tag: how many items the chunk carried.</summary>
        public const string IdentityCountTag = "identity.count";

        /// <summary>
        ///     The chunk activity's outcome tag: <see cref="OutcomeOk" />, <see cref="OutcomeUnavailable" />,
        ///     <see cref="OutcomeError" /> or <see cref="OutcomeCancelled" />.
        /// </summary>
        public const string IdentityOutcomeTag = "identity.outcome";
    }
}
