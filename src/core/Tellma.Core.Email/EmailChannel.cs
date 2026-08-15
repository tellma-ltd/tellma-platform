// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Email
{
    /// <summary>Which of a transport's two channels a sender instance serves.</summary>
    public enum EmailChannel
    {
        /// <summary>Real delivery toward the recipient.</summary>
        Live,

        /// <summary>
        ///     The transport's no-real-delivery channel — a provider validation mode or a mail trap.
        ///     Everything that leaves through it is reported as sandboxed.
        /// </summary>
        Sandbox,
    }
}
