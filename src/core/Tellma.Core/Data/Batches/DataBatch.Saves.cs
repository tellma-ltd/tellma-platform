// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Data.Common;
using System.Globalization;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Entities;
using Tellma.Core.Abstractions.TableTypes;
using Tellma.Core.Data.Ids;
using Tellma.Core.Data.Saves;

namespace Tellma.Core.Data.Batches
{
    // The save statement of spec 0012 §8: one statement per save, its text cached per root entity.
    /// <content>The save statement: a payload of roots and their supplied children, synchronised in one statement.</content>
    internal sealed partial class DataBatch
    {
        /// <inheritdoc />
        public SaveHandle Save<TEntity>(IReadOnlyList<TEntity> rows, ConcurrencyMode concurrency = ConcurrencyMode.Check)
            where TEntity : class
        {
            ArgumentNullException.ThrowIfNull(rows);
            EnsureComposing();
            if (!Enum.IsDefined(concurrency))
            {
                throw new ArgumentOutOfRangeException(nameof(concurrency), concurrency, "Not a concurrency mode.");
            }

            if (Purpose is BatchPurpose.Read or BatchPurpose.Validate)
            {
                throw new InvalidOperationException($"A {Purpose} batch writes nothing; a save belongs to a {nameof(BatchPurpose.Persist)} batch.");
            }

            // --- The plan, the authority over every table of the aggregate, and the user the stamps name ---
            SavePlan plan = _executor.Saves.Plan(typeof(TEntity));
            CheckAuthority(plan.Writes);
            int userId = Connection.UserId
                ?? throw new InvalidOperationException("A save stamps the rows it writes with the current user, and the scope has none. Save in a scope that carries a user.");

            // --- The payload, sorted by table into new and existing rows ---
            var payload = SavePayload.Collect(plan, [.. rows.Cast<object>()], _executor.Allocator);

            // --- The statement: its parameters bound in the plan's order, its names declared ---
            BatchResult<SaveReceipt> receipt = new();
            BatchResult<HashSet<long>> stamped = new();
            StatementDraft draft = Begin();
            string saved;
            string created;
            string touched;
            List<ColumnCapture> captures = [];
            try
            {
                foreach (SaveNode node in plan.Nodes)
                {
                    SaveRows part = payload[node];
                    if (node.Parent is SaveNode parent)
                    {
                        _ = parent.IsLong
                            ? draft.Table([.. part.Parents.Select(static id => new BigIdList { Id = id })])
                            : draft.Table([.. part.Parents.Select(static id => new IdList { Id = checked((int)id) })]);
                    }

                    _ = draft.Table(node.Entity.ClrType, part.New);
                    _ = draft.Table(node.Entity.ClrType, part.Existing);
                }

                _ = draft.Scalar(concurrency == ConcurrencyMode.Override);
                _ = draft.Scalar(userId);

                saved = draft.Name(ReservedSqlNames.StatementNames.Saved);
                created = draft.Name(ReservedSqlNames.StatementNames.New);
                touched = draft.Name(ReservedSqlNames.StatementNames.Touched);
                _ = draft.Name(ReservedSqlNames.StatementNames.Now);
                _ = draft.Name(ReservedSqlNames.StatementNames.Conflicts);
                foreach (SaveCapture capture in plan.Captures)
                {
                    captures.Add(new ColumnCapture(capture.Node.Entity.Table, capture.Column, new SqlIdentifier(draft.Name(capture.Word))));
                }

                // A table above the threshold makes every data-modifying statement plan for its rows.
                bool recompile = payload.LargestTable > _executor.Saves.LargeBatchThreshold;
                string text = _executor.Saves.Text(plan, recompile, draft.Ordinal);

                int ordinal = draft.Ordinal;
                string entity = plan.Root.Entity.Name;
                Append(draft, text, plan.Writes, idempotent: false, async (cursor, cancellationToken) =>
                {
                    // --- The guard's conflict rows, selected only before its THROW ---
                    if (await cursor.NextIfAsync(SaveEmitter.IsConflictSet, cancellationToken).ConfigureAwait(false) is DbDataReader conflicts)
                    {
                        await cursor.ReadConflictsAsync(conflicts, cancellationToken).ConfigureAwait(false);
                        IReadOnlyList<ConflictRow> rows = cursor.Conflicts ?? [];
                        int missing = rows.Count(static row => row.IsMissing);
                        _executor.Metrics.RecordConflicts(entity, rows.Count - missing, missing);
                        return;
                    }

                    // --- The stamped roots, then the receipt row ---
                    DbDataReader ids = await cursor.NextAsync(ordinal, cancellationToken).ConfigureAwait(false);
                    HashSet<long> stampedIds = await SaveEmitter.ReadIdsAsync(ids, cancellationToken).ConfigureAwait(false);
                    DbDataReader set = await cursor.NextAsync(ordinal, cancellationToken).ConfigureAwait(false);
                    if (!await set.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture, $"The save at statement {ordinal} returned no receipt row."));
                    }

                    DateTimeOffset stamp = set.GetFieldValue<DateTimeOffset>(0);
                    int childUpdated = set.GetInt32(1);
                    int deleted = set.GetInt32(2);
                    cursor.CountRows(stampedIds.Count + 1);
                    cursor.Complete(receipt, new SaveReceipt(stamp, payload.Inserted, stampedIds.Count + childUpdated, deleted));
                    cursor.Complete(stamped, stampedIds);
                });
            }
            catch
            {
                Discard(draft);
                throw;
            }

            // --- What the round trip needs: the inserted roots for the commit probe, one transaction, kept ids ---
            List<object> newRoots = payload[plan.Root].New;
            if (plan.Root.IsLong)
            {
                DeclareInsertedRoots(plan.Root.Entity.Table, [.. newRoots.Select(row => PayloadRows.KeyOf(plan.Root.Entity, row))]);
            }
            else
            {
                DeclareInsertedRoots(plan.Root.Entity.Table, [.. newRoots.Select(row => checked((int)PayloadRows.KeyOf(plan.Root.Entity, row)))]);
            }

            _holds.UnionWith(payload.Holds);
            _needsTransaction = true;

            // --- Once it ran: the rows carry the stamps the statement wrote, and the new rows are existing ---
            OnExecuted(_ =>
            {
                if (receipt.IsCompleted)
                {
                    Stamp(plan, payload, receipt.Value, stamped.Value, userId);
                    _executor.Allocator.Committed(payload.NewRows);
                    _executor.Metrics.RecordRowsSaved(plan.Root.Entity.Name, receipt.Value.Inserted + receipt.Value.Updated);
                }

                return Task.CompletedTask;
            });

            return new SaveHandle(receipt, new SqlIdentifier(saved), new SqlIdentifier(created), new SqlIdentifier(touched), captures);
        }

        /// <summary>
        ///     Writes the stamps a save's statement wrote back to its root rows: the created and modified
        ///     stamps of an inserted root, the modified stamps of a root the statement updated.
        /// </summary>
        /// <param name="plan">The plan.</param>
        /// <param name="payload">The payload.</param>
        /// <param name="receipt">The receipt, whose stamp every written row carries.</param>
        /// <param name="stamped">The ids of the existing roots the statement updated.</param>
        /// <param name="userId">The user the stamps name.</param>
        private static void Stamp(SavePlan plan, SavePayload payload, SaveReceipt receipt, HashSet<long> stamped, int userId)
        {
            EntityMetadata root = plan.Root.Entity;
            PropertyMetadata? createdAt = AuditProperty(root, nameof(TopLevelEntity.CreatedAt));
            PropertyMetadata? createdById = AuditProperty(root, nameof(TopLevelEntity.CreatedById));
            PropertyMetadata? modifiedAt = AuditProperty(root, nameof(TopLevelEntity.ModifiedAt));
            PropertyMetadata? modifiedById = AuditProperty(root, nameof(TopLevelEntity.ModifiedById));

            SaveRows part = payload[plan.Root];
            foreach (object row in part.New)
            {
                createdAt?.Setter(row, receipt.Stamp);
                createdById?.Setter(row, userId);
                modifiedAt?.Setter(row, receipt.Stamp);
                modifiedById?.Setter(row, userId);
            }

            foreach (object row in part.Existing)
            {
                if (stamped.Contains(PayloadRows.KeyOf(plan.Root.Entity, row)))
                {
                    modifiedAt?.Setter(row, receipt.Stamp);
                    modifiedById?.Setter(row, userId);
                }
            }
        }

        /// <summary>A root's audit property, by name, when the entity has one.</summary>
        /// <param name="root">The root entity.</param>
        /// <param name="name">The property's name.</param>
        /// <returns>The property, or null.</returns>
        private static PropertyMetadata? AuditProperty(EntityMetadata root, string name)
        {
            return root.Properties.FirstOrDefault(property => string.Equals(property.Name, name, StringComparison.Ordinal));
        }
    }
}
