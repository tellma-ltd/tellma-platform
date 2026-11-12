// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Tenancy
{
    /// <summary>
    ///     The names of the web host's instruments: its meter, its session and authentication
    ///     counters, and their tags. No instrument carries a tenant or a user: per-tenant identity
    ///     belongs on traces and logs, never on metrics.
    /// </summary>
    public static class HostTelemetryNames
    {
        /// <summary>The web host's meter.</summary>
        public const string MeterName = "Tellma.Core.AspNetCore";

        /// <summary>Counter of session-store operations, by operation and source.</summary>
        public const string SessionOperations = "tellma.session.operations";

        /// <summary>Counter of session token refreshes, by outcome.</summary>
        public const string SessionRefreshes = "tellma.session.refreshes";

        /// <summary>Counter of back-channel logout tokens received, by outcome.</summary>
        public const string BackchannelLogouts = "tellma.auth.backchannel_logouts";

        /// <summary>Counter of requests the CSRF control refused, by rule.</summary>
        public const string CsrfRejections = "tellma.auth.csrf_rejections";

        /// <summary>Counter of step-up challenges written.</summary>
        public const string StepUpChallenges = "tellma.auth.stepup_challenges";

        /// <summary>The tag naming a session-store operation.</summary>
        public const string OperationTag = "operation";

        /// <summary>The tag naming where a session-store read was answered from.</summary>
        public const string SourceTag = "source";

        /// <summary>The tag naming an outcome.</summary>
        public const string OutcomeTag = "outcome";

        /// <summary>The tag naming the CSRF rule a refused request broke.</summary>
        public const string RuleTag = "rule";
    }
}
