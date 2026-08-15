// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Connector.SendGrid.Adapter
{
    /// <summary>Which SendGrid mode a sender instance uses.</summary>
    internal enum SendGridChannel
    {
        /// <summary>Ordinary delivery.</summary>
        Live,

        /// <summary>
        ///     Sandbox mode: SendGrid validates the whole payload, consumes no credits, delivers
        ///     nothing, and emits no events. The provider's own purpose-built no-delivery mode, which
        ///     is why this channel needs no configuration of its own.
        /// </summary>
        Sandbox,
    }
}
