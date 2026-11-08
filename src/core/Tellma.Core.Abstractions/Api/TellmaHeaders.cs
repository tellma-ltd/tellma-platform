// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Api
{
    /// <summary>The names of the platform's own HTTP headers, shared by the server, the client and the tests.</summary>
    public static class TellmaHeaders
    {
        /// <summary>
        ///     Request: the caller's display zone as an IANA id (<c>Asia/Riyadh</c>). It formats
        ///     instants in messages and Excel only; the query functions <c>today()</c> and
        ///     <c>now()</c> always bind the tenant's zone.
        /// </summary>
        public const string TimeZone = "Tellma-Time-Zone";

        /// <summary>
        ///     Request: the calendar dates are formatted in (<c>gc</c>, <c>uq</c>, <c>et</c>).
        ///     Response: the effective calendar.
        /// </summary>
        public const string Calendar = "Tellma-Calendar";

        /// <summary>
        ///     Request: the calling client as <c>&lt;name&gt;/&lt;version&gt;</c>. Required on the web
        ///     surface as its cross-site request forgery control; the name is a closed set.
        /// </summary>
        public const string Client = "Tellma-Client";

        /// <summary>
        ///     Request: <c>background</c> for polling and refreshes, which then stamp no activity and
        ///     renew no session cookie. Ignored on a mutation.
        /// </summary>
        public const string Activity = "Tellma-Activity";

        /// <summary>Response: the wire version tags after the request's changes (<c>settings</c>, <c>permissions</c>, <c>preferences</c>, <c>entities</c>).</summary>
        public const string VersionTags = "Tellma-Version-Tags";

        /// <summary>Response, on every response: the deployment's version, with its source revision.</summary>
        public const string Build = "Tellma-Build";

        /// <summary>Response, on every response: the request's trace id, the support reference a problem's <c>instance</c> also carries.</summary>
        public const string TraceId = "Tellma-Trace-Id";

        /// <summary>
        ///     Request: the contract fingerprint of the endpoint the client was built against. A value
        ///     that differs from the endpoint's refuses the request as client-outdated before it runs.
        /// </summary>
        public const string Contract = "Tellma-Contract";

        /// <summary>Request: the comma-separated warning keys the caller confirms for this request.</summary>
        public const string Confirm = "Tellma-Confirm";

        /// <summary>Request: <c>low</c> puts a report run in the batch lane; any other value is ignored.</summary>
        public const string Priority = "Tellma-Priority";
    }
}
