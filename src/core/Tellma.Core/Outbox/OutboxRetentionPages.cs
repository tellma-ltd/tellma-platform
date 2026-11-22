// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Email;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Data.Batches;

namespace Tellma.Core.Outbox
{
    /// <summary>Reads and deletes the pages of outgoing emails the nightly retention removes.</summary>
    internal interface IOutboxRetentionPages
    {
        /// <summary>Reads one page of the ids the retention deletes, in one read round trip.</summary>
        /// <param name="size">The most ids the page holds.</param>
        /// <param name="retention">How long a row is kept.</param>
        /// <param name="cancellationToken">Cancels the read.</param>
        /// <returns>The ids, at most <paramref name="size" />.</returns>
        Task<IReadOnlyList<int>> ReadAsync(int size, TimeSpan retention, CancellationToken cancellationToken);

        /// <summary>Deletes the rows of one page through the outgoing emails' stack, so its delete rules run.</summary>
        /// <param name="ids">The page's ids; at least one.</param>
        /// <param name="cancellationToken">Cancels the delete's rounds.</param>
        /// <returns>How many rows were deleted.</returns>
        /// <exception cref="ValidationException">The stack refuses a row, which changed since the page was read.</exception>
        /// <exception cref="NotFoundException">A row was deleted since the page was read.</exception>
        Task<int> DeleteAsync(IReadOnlyList<int> ids, CancellationToken cancellationToken);
    }

    // Spec 0022 §7.3: the page is read on the database clock behind the connect prologue, with the
    // delete's own precondition, and deleted through the stack's DeleteByIdsAsync, so the blob effect
    // releases the attachments for the sweep.
    /// <summary>Reads each page through the guarded runner and deletes it through the outgoing emails' stack.</summary>
    /// <param name="runner">Runs each page's read behind the connect prologue.</param>
    /// <param name="emails">The outgoing emails' stack.</param>
    internal sealed class OutboxRetentionPages(IGuardedBatchRunner runner, EntityService<OutgoingEmail, int> emails) : IOutboxRetentionPages
    {
        /// <inheritdoc />
        public Task<IReadOnlyList<int>> ReadAsync(int size, TimeSpan retention, CancellationToken cancellationToken)
        {
            BatchResult<IReadOnlyList<int>>? page = null;
            return runner.RunAsync(
                BatchPurpose.Read,
                (_, data) =>
                {
                    page = OutboxStatements.RetentionPage((DataBatch)data, size, retention);
                    return ValueTask.CompletedTask;
                },
                (_, _) => page!.Value,
                cancellationToken);
        }

        /// <inheritdoc />
        public async Task<int> DeleteAsync(IReadOnlyList<int> ids, CancellationToken cancellationToken)
        {
            return (await emails.DeleteByIdsAsync(ids, cancellationToken: cancellationToken).ConfigureAwait(false)).Count;
        }
    }
}
