// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Internal;

namespace Tellma.Core.Abstractions.Jobs
{
    /// <summary>
    ///     What one handler invocation receives: the claimed items that run as one user, in claim
    ///     order, and the batch whose statements commit in the same transaction as their outcomes.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A claim may return jobs that run as different users; the worker invokes the handler once
    ///         per user, so every item of one batch runs under the same identity and permissions.
    ///     </para>
    ///     <para>
    ///         Statements appended to <see cref="Batch" /> execute in the completion transaction, after
    ///         the statement that records the outcomes, and only if the lease still holds: nothing a
    ///         handler appends survives a lost lease. They may write only tables no stack owns; a row a
    ///         stack owns is written by enlisting it with the job's write frame, which places it on this
    ///         same batch.
    ///     </para>
    ///     <para>
    ///         A class rather than a record: a batch has identity, and a record's generated text would
    ///         read <see cref="Single" />, which throws for a batch of several items.
    ///     </para>
    /// </remarks>
    /// <typeparam name="TItem">The items' type: <see cref="Job" /> for an arguments-only handler, the entity for an entity-backed one.</typeparam>
    public sealed class JobBatch<TItem>
        where TItem : class
    {
        /// <summary>The items of this invocation, in claim order; at least one.</summary>
        /// <exception cref="ArgumentNullException">Set to null.</exception>
        /// <exception cref="ArgumentException">Set to an empty list or one holding a null item.</exception>
        public required IReadOnlyList<JobItem<TItem>> Items
        {
            get;
            init => field = ReadOnlyCopies.OfEntries(value, requireOne: true, nameof(Items));
        }

        /// <summary>
        ///     The only item, for a handler whose batch size is one.
        /// </summary>
        /// <exception cref="InvalidOperationException">The batch holds more than one item.</exception>
        [SuppressMessage(
            "Naming",
            "CA1720:Identifier contains type name",
            Justification = "The single item of a batch of one; the member is not about the floating-point type.")]
        public JobItem<TItem> Single => Items.Count == 1
            ? Items[0]
            : throw new InvalidOperationException(string.Create(
                CultureInfo.InvariantCulture,
                $"The job batch holds {Items.Count} items; Single serves a handler that declares a batch size of one. Iterate Items instead."));

        /// <summary>
        ///     A persist batch in the run-as user's scope whose statements execute in the completion
        ///     transaction, after the outcomes are recorded and only if the lease still holds.
        /// </summary>
        public required IDataBatch Batch
        {
            get;
            init => field = value ?? throw new ArgumentNullException(nameof(Batch));
        }
    }
}
