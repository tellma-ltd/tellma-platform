// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Diagnostics.Metrics;
using Tellma.Core.Abstractions.Tenancy;

namespace Tellma.Core.AspNetCore.Observability
{
    /// <summary>The web host's authentication and session instruments, on the host's meter.</summary>
    internal sealed class HostMetrics
    {
        /// <summary>The session operation of a new session.</summary>
        public const string StoreOperation = "store";

        /// <summary>The session operation of a read.</summary>
        public const string RetrieveOperation = "retrieve";

        /// <summary>The session operation of a renewal.</summary>
        public const string RenewOperation = "renew";

        /// <summary>The session operation of a delete.</summary>
        public const string RemoveOperation = "remove";

        /// <summary>The session operation of a revocation by identity server session or subject.</summary>
        public const string RevokeOperation = "revoke";

        /// <summary>The session operation of the sweep of expired sessions.</summary>
        public const string SweepOperation = "sweep";

        /// <summary>The source of an operation the instance's cache answered.</summary>
        public const string CacheSource = "cache";

        /// <summary>The source of an operation the catalog answered.</summary>
        public const string DatabaseSource = "database";

        /// <summary>A refresh that redeemed the refresh token and wrote the new ticket.</summary>
        public const string RefreshedOutcome = "refreshed";

        /// <summary>A refresh that took the ticket another request or instance had already refreshed.</summary>
        public const string AdoptedOutcome = "adopted";

        /// <summary>A refresh the authority refused, which ended the session.</summary>
        public const string RejectedOutcome = "rejected";

        /// <summary>A refresh put off because the authority could not be reached.</summary>
        public const string DeferredOutcome = "deferred";

        /// <summary>A back-channel logout token that was valid.</summary>
        public const string AcceptedOutcome = "accepted";

        private readonly Counter<long> _csrfRejections;
        private readonly Counter<long> _stepUpChallenges;
        private readonly Counter<long> _sessionOperations;
        private readonly Counter<long> _sessionRefreshes;
        private readonly Counter<long> _backchannelLogouts;

        /// <summary>Creates the instruments.</summary>
        /// <param name="meterFactory">The meter factory.</param>
        public HostMetrics(IMeterFactory meterFactory)
        {
            ArgumentNullException.ThrowIfNull(meterFactory);
            Meter meter = meterFactory.Create(HostTelemetryNames.MeterName);
            _csrfRejections = meter.CreateCounter<long>(
                HostTelemetryNames.CsrfRejections,
                description: "Requests the CSRF control refused, by the rule they broke.");
            _stepUpChallenges = meter.CreateCounter<long>(
                HostTelemetryNames.StepUpChallenges,
                description: "Step-up challenges written to callers whose session fell short of an operation's assurance bar.");
            _sessionOperations = meter.CreateCounter<long>(
                HostTelemetryNames.SessionOperations,
                description: "Operations of the BFF's session store, by operation and by whether the cache or the catalog answered.");
            _sessionRefreshes = meter.CreateCounter<long>(
                HostTelemetryNames.SessionRefreshes,
                description: "Token refreshes of BFF sessions, by outcome.");
            _backchannelLogouts = meter.CreateCounter<long>(
                HostTelemetryNames.BackchannelLogouts,
                description: "Back-channel logout tokens received, by whether they were accepted.");
        }

        /// <summary>Counts a CSRF refusal.</summary>
        /// <param name="rule">The rule the request broke.</param>
        public void RecordCsrfRejection(string rule)
        {
            _csrfRejections.Add(1, new KeyValuePair<string, object?>(HostTelemetryNames.RuleTag, rule));
        }

        /// <summary>Counts a step-up challenge.</summary>
        public void RecordStepUpChallenge()
        {
            _stepUpChallenges.Add(1);
        }

        /// <summary>Counts a session store operation.</summary>
        /// <param name="operation">The operation.</param>
        /// <param name="source">Whether the cache or the catalog answered.</param>
        public void RecordSessionOperation(string operation, string source)
        {
            _sessionOperations.Add(
                1,
                new KeyValuePair<string, object?>(HostTelemetryNames.OperationTag, operation),
                new KeyValuePair<string, object?>(HostTelemetryNames.SourceTag, source));
        }

        /// <summary>Counts a session refresh.</summary>
        /// <param name="outcome">The outcome.</param>
        public void RecordSessionRefresh(string outcome)
        {
            _sessionRefreshes.Add(1, new KeyValuePair<string, object?>(HostTelemetryNames.OutcomeTag, outcome));
        }

        /// <summary>Counts a back-channel logout token.</summary>
        /// <param name="accepted">Whether it was valid.</param>
        public void RecordBackchannelLogout(bool accepted)
        {
            _backchannelLogouts.Add(1, new KeyValuePair<string, object?>(HostTelemetryNames.OutcomeTag, accepted ? AcceptedOutcome : RejectedOutcome));
        }
    }
}
