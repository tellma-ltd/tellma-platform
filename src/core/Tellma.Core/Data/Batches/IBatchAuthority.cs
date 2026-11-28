// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Data;

namespace Tellma.Core.Data.Batches
{
    // Spec 0015 §13.3: the host's stack authority covers the emitter, the service hook and the
    // effects; the caller's covers SaveOptions.OnPersist. The pipeline switches the batch between the
    // two as it calls each participant.
    /// <summary>The platform's channel to the write authority of a batch's current appender.</summary>
    internal interface IBatchAuthority
    {
        /// <summary>Sets the stack-owned tables the statements appended from now on may write.</summary>
        /// <param name="authority">The tables.</param>
        void SetCallerAuthority(IReadOnlySet<TableName> authority);
    }
}
