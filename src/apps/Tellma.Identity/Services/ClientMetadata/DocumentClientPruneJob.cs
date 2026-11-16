// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quartz;
using Tellma.Identity.Options;

namespace Tellma.Identity.Services.ClientMetadata
{
    /// <summary>
    ///     Deletes the document-client rows nobody uses: a row holding no authorization and no
    ///     unexpired token, created more than <see cref="TellmaIdentityClientMetadataOptions.UnusedClientLifetime" />
    ///     ago. A document client that was fetched but never consented to would otherwise keep
    ///     its row, and its place under the cap, forever. A consented client keeps its row; if a
    ///     pruned client comes back, its row is created again from its document.
    /// </summary>
    /// <param name="store">The document-client queries.</param>
    /// <param name="options">The engine options.</param>
    /// <param name="timeProvider">The clock.</param>
    /// <param name="logger">Diagnostics.</param>
    [DisallowConcurrentExecution]
    public sealed class DocumentClientPruneJob(
        IDocumentClientStore store,
        IOptions<TellmaIdentityOptions> options,
        TimeProvider timeProvider,
        ILogger<DocumentClientPruneJob> logger) : IJob
    {
        /// <summary>The scheduler identity of this job and its trigger.</summary>
        public const string Name = "Tellma.Identity.DocumentClientPrune";

        /// <inheritdoc />
        public async Task Execute(IJobExecutionContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            await PruneAsync(context.CancellationToken);
        }

        /// <summary>Runs one sweep.</summary>
        /// <param name="cancellationToken">Aborts the sweep.</param>
        /// <returns>How many rows were deleted.</returns>
        public async Task<int> PruneAsync(CancellationToken cancellationToken)
        {
            DateTimeOffset now = timeProvider.GetUtcNow();
            int deleted = await store.PruneAsync(now - options.Value.ClientMetadata.UnusedClientLifetime, now, cancellationToken);

            // Quiet on the common no-op sweep.
            if (deleted > 0)
            {
                DocumentClientPruneLog.Pruned(logger, deleted);
            }

            return deleted;
        }
    }

    /// <summary>Source-generated log messages for <see cref="DocumentClientPruneJob" />.</summary>
    internal static partial class DocumentClientPruneLog
    {
        /// <summary>A sweep that deleted rows.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="deleted">How many document-client rows were deleted.</param>
        [LoggerMessage(Level = LogLevel.Information, Message = "Pruned {Deleted} unused document-client rows.")]
        public static partial void Pruned(ILogger logger, int deleted);
    }
}
