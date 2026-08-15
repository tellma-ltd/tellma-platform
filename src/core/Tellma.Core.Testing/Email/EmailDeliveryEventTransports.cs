// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Testing.Email
{
    /// <summary>
    ///     The transports that have a delivery-event callback at all, by configuration name.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Membership is a property of the transport, not of a deployment: a connector belongs
    ///         here if it can register a delivery-event receiver, even though whether it actually
    ///         does depends on whether that deployment configured the webhook. SMTP and the
    ///         development log sink are absent because neither has a callback to configure.
    ///     </para>
    ///     <para>
    ///         This exists because the "mail is going out but no delivery event came back" alert can
    ///         only be asked of a transport that has a callback, and an alert query is a text file
    ///         that fails silently — a transport missing from its list is never evaluated by the one
    ///         alert designed to catch its silence. Two suites that cannot see each other have to
    ///         agree on that list: the core suite checks it against the query, and each connector's
    ///         own suite checks its membership against what its composition registers. It lives in
    ///         the test-support package rather than in the contract package because it is a fact
    ///         about the fleet that only tests consume — no production code branches on it, and the
    ///         contract package deliberately knows nothing about which connectors exist.
    ///     </para>
    /// </remarks>
    public static class EmailDeliveryEventTransports
    {
        /// <summary>
        ///     The configuration names, ordinally sorted. Adding a connector that registers a
        ///     delivery-event receiver means adding it here.
        /// </summary>
        public static IReadOnlyList<string> Names { get; } = ["acs-email", "sendgrid"];
    }
}
