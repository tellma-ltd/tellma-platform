// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Composition;

namespace Tellma.Core.Abstractions.Data
{
    /// <summary>
    ///     Adds statements around the caller's statements of every batch: a prologue before them
    ///     and an epilogue after them. A scoped service, registered once per contributor.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Prologues run in ascending <see cref="Order" />, before the first statement is appended
    ///         to the batch; epilogues run in descending <see cref="Order" /> when the batch executes,
    ///         so the contributor that opens first closes last. Contributors with the same order keep
    ///         their registration order in the prologue and the reverse in the epilogue.
    ///     </para>
    ///     <para>
    ///         A contributor reads <see cref="IDataBatch.Purpose" /> to decide what a batch needs and
    ///         appends nothing when it needs nothing. Statements a prologue appends are emitted before
    ///         every other statement of the batch, whatever the order they were appended in.
    ///     </para>
    /// </remarks>
    [CollectionService]
    public interface IDataBatchContributor
    {
        /// <summary>
        ///     The contributor's position: prologues run in ascending order, epilogues in descending
        ///     order.
        /// </summary>
        int Order { get; }

        /// <summary>Appends the contributor's statements for one stage of a batch.</summary>
        /// <param name="batch">The batch being composed.</param>
        /// <param name="stage">The stage being contributed to.</param>
        void Contribute(IDataBatch batch, DataBatchStage stage);
    }
}
