// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Jobs;
using Tellma.Core.Data.Batches;
using Tellma.Core.Data.Trees;

namespace Tellma.Core.Jobs
{
    // Spec 0020 §14.5: the weekly backstop for the per-save recount. Every tree table in the model is
    // recounted by spec 0012's verify statement, one maintenance round trip per table on the internal
    // channel, so no stack's authority refuses it; the statement meters the recount and its repairs and
    // warns of a repair. The tables run in the order of their entities' names, and progress is
    // reported after each.
    /// <summary>Recounts every tree table's subtree counts and repairs the rows whose counts drifted.</summary>
    /// <param name="database">The tenant's database, which each table's maintenance batch comes from.</param>
    /// <param name="trees">The tree statements, whose verify recounts a table.</param>
    /// <param name="metadata">The model's entity metadata, which names the tree tables.</param>
    [JobHandler(Key, RunsWhenReadOnly = true)]
    internal sealed class TreeVerifyHandler(ITenantDatabase database, TreeStatements trees, IEntityMetadataProvider metadata) : IJobHandler
    {
        /// <summary>The handler's key, which its built-in schedule names.</summary>
        public const string Key = "core.tree-verify";

        /// <summary>The handler's built-in schedule: its id in the platform's reserved band.</summary>
        public const int ScheduleId = 6;

        /// <summary>The built-in schedule's expression: every Saturday at 01:00.</summary>
        public const string Cron = "0 1 * * 6";

        /// <summary>The key of the progress message, whose arguments count the tables verified and the rows repaired so far.</summary>
        public const string ProgressKey = "Jobs.TreeVerifyProgress";

        /// <inheritdoc />
        public async Task ExecuteAsync(JobBatch<Job> batch)
        {
            ArgumentNullException.ThrowIfNull(batch);

            // --- Each tree table once, through the first entity mapped to it ---
            EntityMetadata[] tables = [.. metadata.All
                .Where(static entity => entity.Tree is not null)
                .OrderBy(static entity => entity.Name, StringComparer.Ordinal)
                .DistinctBy(static entity => entity.Table)];
            foreach (JobItem<Job> item in batch.Items)
            {
                int repaired = 0;
                for (int index = 0; index < tables.Length; index++)
                {
                    item.CancellationToken.ThrowIfCancellationRequested();

                    // --- One table: its own maintenance round trip ---
                    var round = (DataBatch)database.CreateBatch(BatchPurpose.Maintenance);
                    BatchResult<int> result = trees.Verify(round, tables[index]);
                    _ = await round.ExecuteAsync(item.CancellationToken).ConfigureAwait(false);

                    repaired += result.Value;
                    int verified = index + 1;
                    item.Progress.Report(
                        (byte)(verified * JobLimits.MaxProgressPercent / tables.Length),
                        new JobMessage(ProgressKey, new Dictionary<string, object?>(StringComparer.Ordinal)
                        {
                            ["verified"] = verified,
                            ["total"] = tables.Length,
                            ["repaired"] = repaired,
                        }),
                        null);
                }
            }
        }
    }
}
