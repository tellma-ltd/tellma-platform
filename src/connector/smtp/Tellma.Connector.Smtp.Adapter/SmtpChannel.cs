// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Connector.Smtp.Adapter
{
    /// <summary>Which of the transport's two endpoints a sender instance submits to.</summary>
    internal enum SmtpChannel
    {
        /// <summary>The configured smarthost — real delivery.</summary>
        Live,

        /// <summary>The configured mail trap — inspectable, never delivered.</summary>
        Sandbox,
    }
}
