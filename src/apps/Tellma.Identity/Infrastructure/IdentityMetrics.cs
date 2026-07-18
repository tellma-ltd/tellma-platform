// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Diagnostics.Metrics;

namespace Tellma.Identity.Infrastructure
{
    /// <summary>
    ///     The engine's OpenTelemetry metrics, published under the <c>Tellma.Identity</c> meter.
    ///     Subscribe to this meter name in the host's OpenTelemetry pipeline to export them.
    /// </summary>
    public sealed class IdentityMetrics
    {
        /// <summary>The meter name hosts subscribe to.</summary>
        public const string MeterName = "Tellma.Identity";

        private readonly Counter<long> _loginAttempts;
        private readonly Counter<long> _tokensIssued;
        private readonly Counter<long> _refreshReuseDetected;
        private readonly Counter<long> _emailCodes;
        private readonly Counter<long> _invitations;
        private readonly Counter<long> _backchannelLogoutDeliveries;

        /// <summary>Creates the metric instruments.</summary>
        /// <param name="meterFactory">The meter factory.</param>
        public IdentityMetrics(IMeterFactory meterFactory)
        {
            ArgumentNullException.ThrowIfNull(meterFactory);

            Meter meter = meterFactory.Create(MeterName);
            _loginAttempts = meter.CreateCounter<long>("tellma.identity.login.attempts", description: "Sign-in attempts by method, result, and step.");
            _tokensIssued = meter.CreateCounter<long>("tellma.identity.tokens.issued", description: "Tokens issued by grant type.");
            _refreshReuseDetected = meter.CreateCounter<long>("tellma.identity.refresh.reuse_detected", description: "Refresh-token reuse (replay) detections.");
            _emailCodes = meter.CreateCounter<long>("tellma.identity.email_codes", description: "Email one-time codes by purpose and result.");
            _invitations = meter.CreateCounter<long>("tellma.identity.invitations", description: "Invitations by status.");
            _backchannelLogoutDeliveries = meter.CreateCounter<long>("tellma.identity.backchannel_logout.deliveries", description: "Back-channel logout deliveries by result.");
        }

        /// <summary>Records a sign-in attempt.</summary>
        /// <param name="method">The method used.</param>
        /// <param name="result">The outcome (success/failure).</param>
        /// <param name="step">The step (primary/second_factor/step_up).</param>
        public void LoginAttempt(string method, string result, string step)
        {
            _loginAttempts.Add(1, new KeyValuePair<string, object?>("method", method),
                new KeyValuePair<string, object?>("result", result), new KeyValuePair<string, object?>("step", step));
        }

        /// <summary>Records a token issuance.</summary>
        /// <param name="grantType">The grant type.</param>
        public void TokenIssued(string grantType)
        {
            _tokensIssued.Add(1, new KeyValuePair<string, object?>("grant_type", grantType));
        }

        /// <summary>Records a refresh-token reuse detection.</summary>
        public void RefreshReuseDetected()
        {
            _refreshReuseDetected.Add(1);
        }

        /// <summary>Records an email-code event.</summary>
        /// <param name="purpose">The code purpose.</param>
        /// <param name="result">The outcome.</param>
        public void EmailCode(string purpose, string result)
        {
            _emailCodes.Add(1, new KeyValuePair<string, object?>("purpose", purpose), new KeyValuePair<string, object?>("result", result));
        }

        /// <summary>Records an invitation outcome.</summary>
        /// <param name="status">The invitation status.</param>
        public void Invitation(string status)
        {
            _invitations.Add(1, new KeyValuePair<string, object?>("status", status));
        }

        /// <summary>Records a back-channel logout delivery outcome.</summary>
        /// <param name="result">The delivery result.</param>
        public void BackchannelLogoutDelivery(string result)
        {
            _backchannelLogoutDeliveries.Add(1, new KeyValuePair<string, object?>("result", result));
        }
    }
}
