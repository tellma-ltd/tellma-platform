// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Webhooks
{
    /// <summary>
    ///     The semantic outcome of a webhook call. The host maps it to an HTTP status code,
    ///     which drives the external system's redelivery behavior.
    /// </summary>
    public enum WebhookOutcome
    {
        /// <summary>Verified and accepted; the host responds 2xx and the caller will not redeliver.</summary>
        Accepted,

        /// <summary>Signature or credential verification failed; the host responds 401. Not redelivered.</summary>
        Unauthorized,

        /// <summary>The payload is malformed or unprocessable; the host responds 400. Not redelivered.</summary>
        Invalid,

        /// <summary>A transient internal failure; the host responds 5xx so the caller redelivers later.</summary>
        TransientFailure,
    }
}
