// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Data;
using System.Globalization;
using System.Text;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Jobs;
using Tellma.Core.Abstractions.TableTypes;
using Tellma.Core.Data.Batches;

namespace Tellma.Core.Jobs
{
    // Spec 0020 §11.1: the four job actions' statements, each over the ids the action loaded and
    // pre-checked, on the database clock. Each repeats its precondition in its WHERE, so a repeat
    // after an ambiguous failure matches no row (save a flagged row's cancel flag, which it sets
    // again): each is idempotent.
    internal static partial class JobStatements
    {
        /// <summary>Appends the retry: each failed job back to pending, due now, with no attempt counted and its error cleared.</summary>
        /// <param name="batch">The action's persist batch.</param>
        /// <param name="jobIds">The target jobs.</param>
        public static void Retry(DataBatch batch, IReadOnlyList<int> jobIds)
        {
            AppendAction(batch, jobIds, static (ids, now) => string.Create(
                CultureInfo.InvariantCulture,
                $"""
                DECLARE {now} datetimeoffset(3) = SYSUTCDATETIME();
                UPDATE [j] SET [Status] = '{JobStatus.Pending}', [Attempts] = 0, [DueAt] = {now}, [CompletedAt] = NULL, [CancelRequestedAt] = NULL,
                    [ErrorCode] = NULL, [ErrorArgumentsJson] = NULL, [ErrorTraceId] = NULL
                FROM {Jobs.Delimited} AS [j] INNER JOIN {ids} AS [i] ON [i].[Id] = [j].[Id]
                WHERE [j].[Status] = '{JobStatus.Failed}';
                """));
        }

        // The cancel's three kinds: F only flags a running row, or a pending row already flagged, whose
        // item token is cancelled at the next renewal or whose next claim cancels it; Q re-queues a row
        // pending confirmation, or a pending or held row with a checkpoint, flagged, so its next claim
        // runs the cancel path and a handler that commits in steps records its stop; C cancels outright.
        /// <summary>Appends the cancel of each target job that has not ended, by its kind: flagged, re-queued flagged, or cancelled.</summary>
        /// <param name="batch">The action's persist batch.</param>
        /// <param name="jobIds">The target jobs.</param>
        public static void Cancel(DataBatch batch, IReadOnlyList<int> jobIds)
        {
            AppendAction(batch, jobIds, static (ids, now) => string.Create(
                CultureInfo.InvariantCulture,
                $"""
                DECLARE {now} datetimeoffset(3) = SYSUTCDATETIME();
                UPDATE [j]
                SET [Status] = CASE [k].[Kind] WHEN 'F' THEN [j].[Status] WHEN 'Q' THEN '{JobStatus.Pending}' ELSE '{JobStatus.Cancelled}' END,
                    [DueAt] = CASE [k].[Kind] WHEN 'Q' THEN {now} ELSE [j].[DueAt] END,
                    [CancelRequestedAt] = CASE [k].[Kind] WHEN 'C' THEN [j].[CancelRequestedAt] ELSE {now} END,
                    [CompletedAt] = CASE [k].[Kind] WHEN 'C' THEN {now} ELSE [j].[CompletedAt] END,
                    [ErrorCode] = CASE [k].[Kind] WHEN 'C' THEN N'{JobErrorCodes.Cancelled}' ELSE [j].[ErrorCode] END,
                    [ErrorArgumentsJson] = CASE [k].[Kind] WHEN 'C' THEN NULL ELSE [j].[ErrorArgumentsJson] END,
                    [ErrorTraceId] = CASE [k].[Kind] WHEN 'C' THEN NULL ELSE [j].[ErrorTraceId] END,
                    [PendingConfirmationsJson] = NULL
                FROM {Jobs.Delimited} AS [j] INNER JOIN {ids} AS [i] ON [i].[Id] = [j].[Id]
                CROSS APPLY (SELECT CASE WHEN [j].[Status] = '{JobStatus.Running}' OR ([j].[Status] = '{JobStatus.Pending}' AND [j].[CancelRequestedAt] IS NOT NULL) THEN 'F'
                                         WHEN [j].[Status] = '{JobStatus.PendingConfirmation}' OR [j].[StateJson] IS NOT NULL THEN 'Q' ELSE 'C' END) AS [k] ([Kind])
                WHERE [j].[Status] IN ('{JobStatus.Pending}', '{JobStatus.Held}', '{JobStatus.Running}', '{JobStatus.PendingConfirmation}');
                """));
        }

        /// <summary>Appends the resume: each held job back to pending, due now, with its cancel flag and error cleared.</summary>
        /// <param name="batch">The action's persist batch.</param>
        /// <param name="jobIds">The target jobs.</param>
        public static void Resume(DataBatch batch, IReadOnlyList<int> jobIds)
        {
            AppendAction(batch, jobIds, static (ids, now) => string.Create(
                CultureInfo.InvariantCulture,
                $"""
                DECLARE {now} datetimeoffset(3) = SYSUTCDATETIME();
                UPDATE [j] SET [Status] = '{JobStatus.Pending}', [DueAt] = {now}, [CancelRequestedAt] = NULL,
                    [ErrorCode] = NULL, [ErrorArgumentsJson] = NULL, [ErrorTraceId] = NULL
                FROM {Jobs.Delimited} AS [j] INNER JOIN {ids} AS [i] ON [i].[Id] = [j].[Id]
                WHERE [j].[Status] = '{JobStatus.Held}';
                """));
        }

        // The keys confirmed are the job's recorded ones, merged into its confirmed keys, never the
        // caller's list: the precondition holds the caller's list to cover every recorded key, and the
        // statement repeats it with the requester's check.
        /// <summary>
        ///     Appends the confirm: each target job waiting for confirmation, whose requester is the
        ///     caller and whose every recorded warning key the caller confirmed, has its recorded keys
        ///     merged into its confirmed keys and goes back to pending, due now, with no attempt counted.
        /// </summary>
        /// <param name="batch">The action's persist batch.</param>
        /// <param name="jobIds">The target jobs.</param>
        /// <param name="keys">The keys the caller confirmed.</param>
        /// <param name="callerId">The caller, who must be each job's requester.</param>
        public static void Confirm(DataBatch batch, IReadOnlyList<int> jobIds, IReadOnlyList<string> keys, int callerId)
        {
            ArgumentNullException.ThrowIfNull(batch);
            ArgumentNullException.ThrowIfNull(jobIds);
            ArgumentNullException.ThrowIfNull(keys);

            StatementDraft draft = batch.Begin();
            try
            {
                // --- The names, the two tables and the caller, in the order the statement numbers them ---
                string now = draft.Name(ReservedSqlNames.StatementNames.Now);
                string ids = draft.Table(Ids(jobIds));
                string confirmed = draft.Table([.. keys.Distinct(StringComparer.Ordinal).Select(static key => new StringList { Id = key })]);
                string caller = draft.Scalar(SqlDbType.Int, callerId);

                // --- The text: the recorded keys merged, the requester and the coverage checked again ---
                StringBuilder text = new();
                _ = text.Append(CultureInfo.InvariantCulture, $"DECLARE {now} datetimeoffset(3) = SYSUTCDATETIME();\n")
                    .Append("UPDATE [j]\n")
                    .Append("SET [ConfirmedKeys] = CONCAT_WS(',', [j].[ConfirmedKeys], (SELECT STRING_AGG([w].[Key], ',') FROM (SELECT DISTINCT [Key]\n")
                    .Append("        FROM OPENJSON([j].[PendingConfirmationsJson], '$.items') WITH ([Key] nvarchar(450) '$.key')) AS [w])),\n")
                    .Append(CultureInfo.InvariantCulture, $"    [Status] = '{JobStatus.Pending}', [Attempts] = 0, [DueAt] = {now}, [PendingConfirmationsJson] = NULL\n")
                    .Append(CultureInfo.InvariantCulture, $"FROM {Jobs.Delimited} AS [j] INNER JOIN {ids} AS [i] ON [i].[Id] = [j].[Id]\n")
                    .Append(CultureInfo.InvariantCulture, $"WHERE [j].[Status] = '{JobStatus.PendingConfirmation}' AND [j].[RequestedById] = {caller}\n")
                    .Append("  AND NOT EXISTS (SELECT 1 FROM OPENJSON([j].[PendingConfirmationsJson], '$.items') WITH ([Key] nvarchar(450) '$.key') AS [w]\n")
                    .Append(CultureInfo.InvariantCulture, $"                  WHERE NOT EXISTS (SELECT 1 FROM {confirmed} AS [k] WHERE [k].[Id] = [w].[Key]));");
                batch.Append(draft, text.ToString(), JobWrites, idempotent: true);
            }
            catch
            {
                batch.Discard(draft);
                throw;
            }
        }

        /// <summary>Appends one of the actions whose statement takes the target ids alone.</summary>
        /// <param name="batch">The action's persist batch.</param>
        /// <param name="jobIds">The target jobs.</param>
        /// <param name="compose">Composes the text from the ids' table and the clock's name.</param>
        private static void AppendAction(DataBatch batch, IReadOnlyList<int> jobIds, Func<string, string, string> compose)
        {
            ArgumentNullException.ThrowIfNull(batch);
            ArgumentNullException.ThrowIfNull(jobIds);

            StatementDraft draft = batch.Begin();
            try
            {
                string now = draft.Name(ReservedSqlNames.StatementNames.Now);
                string ids = draft.Table(Ids(jobIds));
                batch.Append(draft, compose(ids, now).ReplaceLineEndings("\n"), JobWrites, idempotent: true);
            }
            catch
            {
                batch.Discard(draft);
                throw;
            }
        }
    }
}
