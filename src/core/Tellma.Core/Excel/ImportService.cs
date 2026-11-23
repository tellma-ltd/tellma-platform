// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Excel;
using Tellma.Core.Abstractions.TableTypes;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.Access;

namespace Tellma.Core.Excel
{
    // Spec 0019 §12.1: the imports' stack service. A delete is refused, in its one validation round,
    // while the row's job may still run (spec 0020 §2.3's live statuses), so a waiting or running
    // import keeps its uploaded file and its outcome row; the user stops the import first.
    /// <summary>
    ///     The imports' stack service: the reads, the delete, and the enlisted saves the start of an
    ///     import and its job make. A row whose job may still run refuses a delete.
    /// </summary>
    /// <param name="factory">The pipeline factory the scope resolves.</param>
    public sealed class ImportService(IEntityPipelineFactory factory) : EntityService<Import>(factory)
    {
        /// <inheritdoc />
        protected override async Task ValidateDeleteAsync(DeleteContext<Import, int> context)
        {
            ArgumentNullException.ThrowIfNull(context);
            await base.ValidateDeleteAsync(context).ConfigureAwait(false);

            // --- The targets and their jobs' statuses load with the first round ---
            ContextRef<RawResult> live = LiveJobs(context.Loader, context.Ids);
            await context.Loader.LoadAsync().ConfigureAwait(false);
            HashSet<int> refused = Read(live.Value);

            // --- Each visible target whose job may still run is refused at its own position ---
            for (int position = 0; position < context.Entities.Count; position++)
            {
                Import target = context.Entities[position];
                if (refused.Contains(target.Id))
                {
                    context.Errors.Add(AccessRefusals.Target(context.Ids, target.Id, position), ExcelErrorCodes.ImportsJobActive);
                }
            }
        }

        // A load without a grant filter, beside the targets in the first round: each named row whose
        // job, through the row's stored JobId, is live — Pending, Running, Held or
        // PendingConfirmation, the statuses from which a job may still run. A row with no job reads
        // no row here; the caller consults the ids only for the targets the first round found visible.
        /// <summary>Requests the ids of the named rows whose job may still run.</summary>
        /// <param name="loader">The context's loader.</param>
        /// <param name="ids">The ids the request named.</param>
        /// <returns>The pending load: one result set of row ids.</returns>
        private static ContextRef<RawResult> LiveJobs(IContextLoader loader, IReadOnlyList<int> ids)
        {
            List<IdList> keys = [.. ids.Distinct().Order().Select(static id => new IdList { Id = id })];
            return loader.Sql(
                $"SELECT [i].[Id] FROM [core].[Imports] AS [i] INNER JOIN {keys} AS [k] ON [k].[Id] = [i].[Id] INNER JOIN [core].[Jobs] AS [j] ON [j].[Id] = [i].[JobId] WHERE [j].[Status] IN ('Pending', 'Running', 'Held', 'PendingConfirmation');",
                new SqlOptions(resultSets: 1));
        }

        /// <summary>Reads the ids the first round loaded.</summary>
        /// <param name="result">The load's result.</param>
        /// <returns>The ids of the rows whose job may still run.</returns>
        private static HashSet<int> Read(RawResult result)
        {
            QueryRowSet set = result.ResultSets[0];
            int[] rows = (int[])set.GetBuffer(0);
            return [.. rows.AsSpan(0, set.RowCount)];
        }
    }
}
