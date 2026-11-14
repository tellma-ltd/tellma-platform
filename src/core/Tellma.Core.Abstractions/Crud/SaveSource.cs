// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Crud
{
    /// <summary>
    ///     Who asked for a write. Recorded on the operation's telemetry and visible to hooks through the
    ///     options, but never a rule input: a rule that differs by source is a rule that will be
    ///     bypassed, and no source admits an operation a stack omits.
    /// </summary>
    public enum SaveSource
    {
        /// <summary>The web client, through the web projection.</summary>
        Web = 0,

        /// <summary>An integration, through the public API.</summary>
        PublicApi = 1,

        /// <summary>An agent, through the agent tools.</summary>
        Agent = 2,

        /// <summary>The Excel import.</summary>
        Import = 3,

        /// <summary>The platform itself: a provisioning step, a job handler or another server-side caller.</summary>
        System = 4,
    }
}
