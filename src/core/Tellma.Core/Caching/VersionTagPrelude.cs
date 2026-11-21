// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Data.Common;
using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Data.Batches;

namespace Tellma.Core.Caching
{
    // The prelude is spec 0013 §2.5: the first statement after the schema guard of every tenant batch.
    /// <summary>
    ///     The prelude: every tenant-level tag, read at the head of every tenant batch so that every
    ///     cold load in the batch is stamped with a tag read before its rows. The connect prologue
    ///     carries the same statement and reads it with <see cref="ReadAsync" />.
    /// </summary>
    internal static class VersionTagPrelude
    {
        /// <summary>The prelude's text.</summary>
        public static readonly string Text = $"SELECT [Name], [Tag] FROM {VersionTagRowConfiguration.Table.Delimited};";

        /// <summary>
        ///     Reads the prelude's result set, on which the cursor's next set stands, and reports the
        ///     tags to the batch as its read snapshot.
        /// </summary>
        /// <param name="batch">The batch.</param>
        /// <param name="cursor">The round trip's cursor.</param>
        /// <param name="ordinal">The reading statement's ordinal.</param>
        /// <param name="time">The clock that stamps the snapshot.</param>
        /// <param name="cancellationToken">Cancels the read.</param>
        /// <returns>A task that completes when the set is read.</returns>
        public static async ValueTask ReadAsync(DataBatch batch, ResultSetCursor cursor, int ordinal, TimeProvider time, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(batch);
            ArgumentNullException.ThrowIfNull(cursor);
            ArgumentNullException.ThrowIfNull(time);

            DbDataReader set = await cursor.NextAsync(ordinal, cancellationToken).ConfigureAwait(false);
            Dictionary<string, VersionTag> tags = new(StringComparer.Ordinal);
            while (await set.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                tags[set.GetString(0)] = new VersionTag(set.GetGuid(1));
            }

            cursor.CountRows(tags.Count);
            batch.ReportVersionTags(new VersionTagSnapshot(batch.Connection.TenantId, tags, time.GetUtcNow()));
        }
    }

    /// <summary>
    ///     Contributes the prelude to every tenant batch that carries no connect prologue: a cache
    ///     loader's, a maintenance batch, the migrator's. A batch marked with the connect prologue reads
    ///     the same rows from the prologue's own result set, so this contributor yields to it.
    /// </summary>
    /// <param name="time">The clock that stamps the snapshot.</param>
    internal sealed class VersionTagPreludeContributor(TimeProvider time) : IDataBatchContributor
    {
        /// <summary>The contributor's order: after the connect prologue's, before every later prologue.</summary>
        public const int PreludeOrder = 50;

        /// <inheritdoc />
        public int Order => PreludeOrder;

        /// <inheritdoc />
        public void Contribute(IDataBatch batch, DataBatchStage stage)
        {
            ArgumentNullException.ThrowIfNull(batch);
            if (stage != DataBatchStage.Prologue || batch is not DataBatch { CarriesConnectPrologue: false } data)
            {
                return;
            }

            // --- One statement, read into the batch's snapshot ---
            StatementDraft draft = data.Begin();
            int ordinal = draft.Ordinal;
            data.Append(
                draft,
                VersionTagPrelude.Text,
                reader: (cursor, cancellationToken) => VersionTagPrelude.ReadAsync(data, cursor, ordinal, time, cancellationToken));
        }
    }
}
