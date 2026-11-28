// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Testing.Jobs
{
    /// <summary>Which outcome a job handler marked an item with.</summary>
    public enum JobItemOutcomeKind
    {
        /// <summary>The handler called <c>Succeed</c> last.</summary>
        Succeeded,

        /// <summary>The handler called <c>Retry</c> last.</summary>
        Retry,

        /// <summary>The handler called <c>Fail</c> last.</summary>
        Failed,
    }
}
