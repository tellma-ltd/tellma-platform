// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Validation;

namespace Tellma.Core.Blobs
{
    // spec 0017 §4.4: the effect, per blob-reference column of every table of the aggregate.
    /// <summary>
    ///     Moves the blobs a persist dereferenced or attached through their states, inside the persist's
    ///     transaction: per blob-reference column the emitter captured, the blobs the change
    ///     dereferenced are released, and on a save, or an action's marked rows, the newly attached
    ///     ones are confirmed after the kind, the uploader and the deadline are checked again. A write
    ///     whose newly attached blob is not attachable rolls back with
    ///     <see cref="ValidationCodes.BlobNotAttachable" />.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Every value comes from the capture the emitter filled from the written rows, never from
    ///         memory, so an override save releases what was actually on the row, and a delete releases
    ///         exactly what it removed. A column the persist captured nothing for contributes nothing,
    ///         so an action that marks no row gains no statement.
    ///     </para>
    ///     <para>
    ///         Only a committed blob is released and only a staged one confirmed, so a double release
    ///         does nothing and a committed blob is never re-pointed. The column's unique index keeps
    ///         one owner row per blob whatever the states say.
    ///     </para>
    /// </remarks>
    /// <typeparam name="TEntity">The stack's leaf.</typeparam>
    /// <param name="metadata">The entity metadata.</param>
    internal sealed class BlobReferenceEffect<TEntity>(IEntityMetadataProvider metadata) : IPersistEffect<TEntity>
        where TEntity : class
    {
        /// <summary>What every statement declares: it writes the blob table, which no stack owns, and is not safe to run twice.</summary>
        private static readonly SqlOptions Writes = new(writes: new HashSet<TableName> { BlobStatements.Blobs }, idempotent: false);

        /// <inheritdoc />
        public Task ContributeAsync(PersistContext<TEntity> context)
        {
            ArgumentNullException.ThrowIfNull(context);
            if (context.Captures.Count == 0)
            {
                return Task.CompletedTask;
            }

            // A save, and an action through the rows it marked, confirm what they attached; a delete
            // only releases what it removed. An action's marked rows are written by the save
            // statement, whose capture holds new values the attach rule must judge (spec 0017 §4.1).
            bool confirms = context is not DeletePersistContext<TEntity>;
            foreach (BlobColumn column in BlobAttachments.Columns(metadata.Get(typeof(TEntity))))
            {
                // The capture is found by its table and column; its name is the emitter's, never formed here.
                if (context.Captures.FirstOrDefault(capture => capture.Table == column.Table && string.Equals(capture.Column, column.Column, StringComparison.Ordinal)) is not ColumnCapture capture)
                {
                    continue;
                }

                if (confirms)
                {
                    int userId = context.Context.UserId
                        ?? throw new InvalidOperationException("A save's or an action's persist runs for a connected user, whose id confirms the blobs the user staged.");
                    Confirm(context.Batch, capture.Identifier, column.Kind, userId);
                }
                else
                {
                    Release(context.Batch, capture.Identifier);
                }
            }

            return Task.CompletedTask;
        }

        /// <summary>Appends the release of the blobs a delete's capture removed.</summary>
        /// <param name="batch">The persist batch.</param>
        /// <param name="capture">The column's capture table.</param>
        private static void Release(IDataBatch batch, SqlIdentifier capture)
        {
            _ = batch.Sql(
                $"""
                UPDATE b SET [State] = 'Released', [ExpiresAt] = SYSUTCDATETIME()
                FROM [core].[Blobs] AS b INNER JOIN {capture} AS c ON c.[OldBlobId] = b.[Id]
                WHERE (c.[NewBlobId] IS NULL OR c.[NewBlobId] <> c.[OldBlobId]) AND b.[State] = 'Committed';
                """,
                Writes);
        }

        // The confirmed ids land in a table of their own, and the check compares it with the capture
        // row by row: a count kept in a variable would need a batch-local name, which only the
        // platform's statements may declare.
        /// <summary>
        ///     Appends a save's statement for one column: the release of what the change dereferenced,
        ///     the confirmation of what it newly attached, re-checked inside the transaction, and the
        ///     refusal of the save when a newly attached blob was not confirmed.
        /// </summary>
        /// <param name="batch">The persist batch.</param>
        /// <param name="capture">The column's capture table.</param>
        /// <param name="kind">The column's kind.</param>
        /// <param name="userId">The saving user, who must have staged every blob the save attaches.</param>
        private static void Confirm(IDataBatch batch, SqlIdentifier capture, string kind, int userId)
        {
            SqlIdentifier confirmed = batch.DeclareIdTable();
            _ = batch.Sql(
                $"""
                UPDATE b SET [State] = 'Released', [ExpiresAt] = SYSUTCDATETIME()
                FROM [core].[Blobs] AS b INNER JOIN {capture} AS c ON c.[OldBlobId] = b.[Id]
                WHERE (c.[NewBlobId] IS NULL OR c.[NewBlobId] <> c.[OldBlobId]) AND b.[State] = 'Committed';
                UPDATE b SET [State] = 'Committed', [CommittedAt] = SYSUTCDATETIME(), [ExpiresAt] = NULL
                OUTPUT inserted.[Id] INTO {confirmed} ([Id])
                FROM [core].[Blobs] AS b INNER JOIN {capture} AS c ON c.[NewBlobId] = b.[Id]
                WHERE (c.[OldBlobId] IS NULL OR c.[OldBlobId] <> c.[NewBlobId])
                  AND b.[State] = 'Staged' AND b.[Kind] = {kind} AND b.[CreatedById] = {userId} AND b.[ExpiresAt] > SYSUTCDATETIME();
                IF EXISTS (SELECT 1 FROM {capture} AS c
                           WHERE c.[NewBlobId] IS NOT NULL AND (c.[OldBlobId] IS NULL OR c.[OldBlobId] <> c.[NewBlobId])
                             AND NOT EXISTS (SELECT 1 FROM {confirmed} AS k WHERE k.[Id] = c.[NewBlobId]))
                    THROW 50422, N'Blob.NotAttachable', 1;
                """,
                Writes);
        }
    }
}
