// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Email
{
    /// <summary>
    ///     Classifies whose inbox an email targets — the input to the sandbox-tenant routing
    ///     policy. Every send site states it explicitly; there is no default.
    /// </summary>
    public enum EmailAudience
    {
        /// <summary>
        ///     Addressed exclusively to users of the platform itself — tenant staff and operators
        ///     signed into the sending system (system notifications, workflow alerts). On behalf of
        ///     a sandbox tenant this mail still goes out for real, marked as sandbox-originated.
        /// </summary>
        Internal,

        /// <summary>
        ///     Addressed to anyone outside the sending system — customers, suppliers, arbitrary
        ///     addresses (invoices, statements, portals). On behalf of a sandbox tenant this mail is
        ///     never really delivered. When in doubt — any mixed or unverifiable recipient list —
        ///     classify as external; the failure mode of over-classifying is an undelivered test
        ///     email, the failure mode of under-classifying is a test invoice in a real customer's
        ///     inbox.
        /// </summary>
        External,
    }
}
