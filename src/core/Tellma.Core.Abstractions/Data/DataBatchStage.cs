// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Data
{
    /// <summary>The stage a <see cref="IDataBatchContributor" /> is asked to contribute to.</summary>
    public enum DataBatchStage
    {
        /// <summary>
        ///     Statements that run before the caller's, in ascending contributor order. The stage runs
        ///     once, before the first statement is appended to the batch, or when the batch executes
        ///     if nothing was appended.
        /// </summary>
        Prologue,

        /// <summary>
        ///     Statements that run after the caller's, in descending contributor order, when the batch
        ///     executes. On a transactional batch they are the last statements before the commit.
        /// </summary>
        Epilogue,
    }
}
