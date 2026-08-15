// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Webhooks
{
    /// <summary>The webhook fronting's configuration, bound from the <c>Webhooks</c> section.</summary>
    public sealed class WebhookOptions
    {
        /// <summary>The configuration section these options bind from.</summary>
        public const string SectionName = "Webhooks";

        /// <summary>
        ///     The largest request body the fronting will buffer, in bytes. Defaults to 2 MiB —
        ///     comfortably above any provider's event-batch size, and small enough that an oversized
        ///     or hostile request cannot be used to exhaust memory. Anything larger is answered 413
        ///     and metered, never dispatched.
        /// </summary>
        public long MaxRequestBodyBytes { get; set; } = 2 * 1024 * 1024;
    }
}
