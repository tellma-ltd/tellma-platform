// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Data.Common;
using System.Globalization;
using System.Text;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.TableTypes;
using Tellma.Core.Data.Saves;
using Tellma.Core.Data.Writes;
using Tellma.Core.Queryex;

namespace Tellma.Core.Data.Batches
{
    // The delete statement of spec 0012 §9.2. By ids: the keys captured under U locks and the read
    // filter, the missing ids a result set before the 404, the grant filter's 403, the stamps' 409.
    // By query: the keys capped and count-verified. With descendants: the roots' 404, the closure
    // by node, its 403. Then the deletes, children deepest first, and a tree's recount (§10.3).
    // A check that embeds a compiled body takes a statement of its own, so each body binds its
    // engine parameters at its own ordinal.
    /// <content>The delete statement: children first, in explicit statements, never by cascade.</content>
    internal sealed partial class DataBatch
    {
        /// <inheritdoc />
        public DeleteHandle Delete<TEntity>(DeleteSpec<TEntity> spec)
            where TEntity : class
        {
            ArgumentNullException.ThrowIfNull(spec);
            (EntityMetadata metadata, EntityDescriptor root) = WriteTarget<TEntity>(nameof(Delete));

            // --- The plan, and the authority over every table it writes ---
            var plan = DeletePlan.For(metadata, _executor.Queries.Metadata);
            CheckAuthority(plan.Writes);

            // --- The statements: the keys and their checks, then the deletes ---
            bool isLong = metadata.Key.ClrType == typeof(long);
            BatchResult<DeleteReceipt> receipt = new();
            List<WriteStatement> statements = [];
            Planned planned = spec switch
            {
                DeleteSpec<TEntity>.ByIds byIds => PlanByIds(plan, root, byIds.Ids, byIds.ReadFilter, byIds.ExpectedStamps, statements, nameof(spec)),
                DeleteSpec<TEntity>.ByQuery byQuery => PlanByQuery(plan, root, byQuery, nameof(spec)),
                DeleteSpec<TEntity>.WithDescendants descendants => PlanWithDescendants(plan, root, descendants.Ids, descendants.ReadFilter, statements, nameof(spec)),
                _ => throw new ArgumentException($"The delete spec {spec.GetType().Name} is not one of the shapes of {nameof(DeleteSpec<>)}.", nameof(spec)),
            };
            FilterTree? grant = spec switch
            {
                DeleteSpec<TEntity>.ByIds byIds => byIds.Filter,
                DeleteSpec<TEntity>.WithDescendants descendants => descendants.Filter,
                _ => null,
            };

            // --- The delete statement: [the grant check], [the stamps], the deletes, a tree's recount ---
            int ordinal = planned.Ordinal;
            string prefix = LocalPrefix(ordinal);
            StringBuilder text = new();
            CompiledQuery? granted = grant is null ? null : CompileKeys(root, grant, arguments: null, planned.Keys, take: null, ordinal);
            bool recompile = planned.Recompile || granted?.Recompile == true;
            DeleteText delete = new(text, plan, prefix, planned.Keys, recompile);
            delete.WriteCaptureDeclarations();
            _ = text.Append(planned.Head);
            if (granted is not null)
            {
                // Every captured row passes the grant filter, or nothing is deleted.
                WriteCountCheck(
                    text,
                    granted,
                    ReservedSqlNames.Statement(ordinal, ReservedSqlNames.StatementNames.Count),
                    $"(SELECT COUNT(*) FROM {planned.Keys})",
                    TellmaSqlErrors.RowSecurity,
                    TellmaSqlErrors.RowSecurityCode,
                    recompile);
            }

            if (planned.Stamps is string stamps)
            {
                delete.WriteStampCheck(stamps, _executor.Saves.ConflictCap(isLong));
            }

            delete.WriteDeletes();

            // --- Its parameters and names: [the cap and count], the slots, the keys, the captures ---
            IReadOnlyList<ScalarParameter> slots = [.. planned.Slots, .. granted is null ? [] : Slots(granted, arguments: null)];
            List<ColumnCapture> captures = [.. plan.Captures.Select(capture => new ColumnCapture(
                capture.Node.Entity.Table, capture.Column, new SqlIdentifier(ReservedSqlNames.Statement(ordinal, capture.Word))))];
            bool conflicts = planned.Stamps is not null;
            statements.Add(new WriteStatement(
                ordinal,
                text.ToString(),
                draft =>
                {
                    planned.Bind(draft);
                    foreach (DeleteCapture capture in plan.Captures)
                    {
                        _ = draft.Name(capture.Word);
                    }

                    Engine(draft, slots);
                },
                planned.Parameters + slots.Count,
                plan.Writes,
                metadata.ClrType,
                async (cursor, cancellationToken) =>
                {
                    // --- The stamps' conflict rows, selected only before their THROW ---
                    if (conflicts && await cursor.NextIfAsync(SaveEmitter.IsConflictSet, cancellationToken).ConfigureAwait(false) is DbDataReader conflict)
                    {
                        await cursor.ReadConflictsAsync(conflict, cancellationToken).ConfigureAwait(false);
                        return;
                    }

                    // --- The deleted ids ---
                    DbDataReader set = await cursor.NextAsync(ordinal, cancellationToken).ConfigureAwait(false);
                    HashSet<long> deleted = await SaveEmitter.ReadIdsAsync(set, cancellationToken).ConfigureAwait(false);
                    cursor.CountRows(deleted.Count);
                    cursor.Complete(receipt, new DeleteReceipt(BoxedSet(deleted, isLong)));
                }));

            AppendWrites(statements);

            // --- Once it ran: the deleted rows, and a tree's recount ---
            string entity = metadata.Name;
            bool tree = metadata.Tree is not null;
            OnExecuted(_ =>
            {
                if (receipt.IsCompleted)
                {
                    _executor.Metrics.RecordRowsDeleted(entity, receipt.Value.DeletedIds.Count);
                    if (tree)
                    {
                        _executor.Metrics.RecordTreeRecompute(entity);
                    }
                }

                return Task.CompletedTask;
            });

            return new DeleteHandle(receipt, new SqlIdentifier(planned.Keys), captures);
        }

        /// <summary>
        ///     Plans a delete by ids: a statement of its own captures the existing rows under U locks
        ///     and the read filter, selects the ids it did not capture (absent and hidden alike), and
        ///     throws 50404 when there are any; the delete statement follows it.
        /// </summary>
        /// <param name="plan">The delete's plan.</param>
        /// <param name="root">The entity's descriptor.</param>
        /// <param name="ids">The ids.</param>
        /// <param name="readFilter">The caller's read filter, or null.</param>
        /// <param name="expectedStamps">The stamps the rows were read with, or null.</param>
        /// <param name="statements">The call's statements, which the capture joins.</param>
        /// <param name="parameterName">The caller's parameter name.</param>
        /// <returns>What the delete statement builds on.</returns>
        private Planned PlanByIds(
            DeletePlan plan,
            EntityDescriptor root,
            IReadOnlyList<object> ids,
            FilterTree? readFilter,
            IReadOnlyList<DateTimeOffset?>? expectedStamps,
            List<WriteStatement> statements,
            string parameterName)
        {
            EntityMetadata entity = plan.Root.Entity;
            bool isLong = entity.Key.ClrType == typeof(long);
            long[] keys = KeyValues(ids, isLong, parameterName);
            IdStampList[]? stamped = expectedStamps is null ? null : StampRows(entity, keys, expectedStamps, isLong, parameterName);
            bool large = keys.Length > _executor.Saves.LargeBatchThreshold;

            // --- The capture: the existing rows, readable under the read filter, U-locked to the commit ---
            int ordinal = _statements.Count;
            string list = ReservedSqlNames.Statement(ordinal, ReservedSqlNames.StatementNames.TableValued(0));
            string keysTable = ReservedSqlNames.Statement(ordinal, ReservedSqlNames.StatementNames.Keys);
            CompiledQuery? readable = readFilter is null ? null : CompileKeys(root, readFilter, arguments: null, list, take: null, ordinal);
            bool recompile = large || readable?.Recompile == true;
            StringBuilder text = new();
            _ = text.Append(CultureInfo.InvariantCulture, $"DECLARE {keysTable} TABLE ([Id] {plan.Root.KeyType} PRIMARY KEY);\n")
                .Append(readable?.Prologue ?? string.Empty)
                .Append(CultureInfo.InvariantCulture, $"INSERT INTO {keysTable} ([Id])\n")
                .Append(CultureInfo.InvariantCulture, $"SELECT t.[Id] FROM {plan.Root.Table} AS t WITH (UPDLOCK, ROWLOCK) JOIN {list} AS s ON s.[Id] = t.[Id]");
            if (readable is not null)
            {
                _ = text.Append("\nWHERE t.[Id] IN (\n").Append(readable.Body).Append(')');
            }

            _ = text.Append(Hint(recompile)).Append(";\n")
                .Append(CultureInfo.InvariantCulture, $"SELECT s.[Id] FROM {list} AS s WHERE s.[Id] NOT IN (SELECT [Id] FROM {keysTable});\n")
                .Append(NotFoundThrow());

            IReadOnlyList<ScalarParameter> slots = readable is null ? [] : Slots(readable, arguments: null);
            statements.Add(new WriteStatement(
                ordinal,
                text.ToString(),
                draft =>
                {
                    _ = stamped is null ? BindIds(draft, keys, isLong) : draft.Table(stamped);
                    _ = draft.Name(ReservedSqlNames.StatementNames.Keys);
                    Engine(draft, slots);
                },
                1 + slots.Count,
                Writes: null,
                entity.ClrType,
                MissingIdsReader(ordinal)));

            return new Planned(ordinal + 1, keysTable, string.Empty, static _ => { }, 0, [], stamped is null ? null : list, recompile);
        }

        /// <summary>
        ///     Plans a delete by query: the delete statement itself captures the query's keys, at
        ///     most the cap plus one, then throws 50413 past the cap and 50428 on another count.
        /// </summary>
        /// <typeparam name="TEntity">The entity type.</typeparam>
        /// <param name="plan">The delete's plan.</param>
        /// <param name="root">The entity's descriptor.</param>
        /// <param name="spec">The spec.</param>
        /// <param name="parameterName">The caller's parameter name.</param>
        /// <returns>What the delete statement builds on.</returns>
        private Planned PlanByQuery<TEntity>(DeletePlan plan, EntityDescriptor root, DeleteSpec<TEntity>.ByQuery spec, string parameterName)
            where TEntity : class
        {
            ArgumentNullException.ThrowIfNull(spec.Filter, parameterName);
            int take = CapTake(spec.Cap, spec.ExpectedCount, parameterName);
            int ordinal = _statements.Count;
            string keysTable = ReservedSqlNames.Statement(ordinal, ReservedSqlNames.StatementNames.Keys);
            CompiledQuery keys = CompileKeys(root, spec.Filter, spec.Arguments, keySet: null, take, ordinal);

            StringBuilder head = new();
            _ = head.Append(CultureInfo.InvariantCulture, $"DECLARE {keysTable} TABLE ([Id] {plan.Root.KeyType} PRIMARY KEY);\n")
                .Append(keys.Prologue)
                .Append(CultureInfo.InvariantCulture, $"INSERT INTO {keysTable} ([Id])\n")
                .Append(keys.Body)
                .Append(keys.Recompile ? "OPTION (RECOMPILE)\n" : string.Empty)
                .Append(";\n")
                .Append(CapChecks(ordinal, cap: 0, TellmaSqlErrors.DeleteCapExceededCode, TellmaSqlErrors.DeleteCountMismatchCode));

            int cap = spec.Cap;
            int expected = spec.ExpectedCount;
            return new Planned(
                ordinal,
                keysTable,
                head.ToString(),
                draft =>
                {
                    _ = draft.Scalar(cap);
                    _ = draft.Scalar(expected);
                    _ = draft.Name(ReservedSqlNames.StatementNames.Keys);
                },
                2,
                Slots(keys, spec.Arguments),
                Stamps: null,
                keys.Recompile);
        }

        /// <summary>
        ///     Plans a delete of tree roots with every descendant: a statement of its own selects the
        ///     roots not readable under the read filter and throws 50404 when there are any, then
        ///     captures the closure by node; the delete statement follows it.
        /// </summary>
        /// <param name="plan">The delete's plan.</param>
        /// <param name="root">The entity's descriptor.</param>
        /// <param name="ids">The roots' ids.</param>
        /// <param name="readFilter">The caller's read filter, or null.</param>
        /// <param name="statements">The call's statements, which the capture joins.</param>
        /// <param name="parameterName">The caller's parameter name.</param>
        /// <returns>What the delete statement builds on.</returns>
        /// <exception cref="ArgumentException">The entity is not a tree.</exception>
        private Planned PlanWithDescendants(
            DeletePlan plan, EntityDescriptor root, IReadOnlyList<object> ids, FilterTree? readFilter, List<WriteStatement> statements, string parameterName)
        {
            EntityMetadata entity = plan.Root.Entity;
            if (entity.Tree is not TreeMetadata tree)
            {
                throw new ArgumentException($"The entity {entity.Name} is not a tree, so it has no descendants to delete with its rows.", parameterName);
            }

            bool isLong = entity.Key.ClrType == typeof(long);
            long[] keys = KeyValues(ids, isLong, parameterName);

            // --- The roots' check, then the closure: every row at or under a root's node ---
            int ordinal = _statements.Count;
            string list = ReservedSqlNames.Statement(ordinal, ReservedSqlNames.StatementNames.TableValued(0));
            string keysTable = ReservedSqlNames.Statement(ordinal, ReservedSqlNames.StatementNames.Keys);
            CompiledQuery readable = CompileKeys(root, readFilter, arguments: null, list, take: null, ordinal);
            bool recompile = keys.Length > _executor.Saves.LargeBatchThreshold || readable.Recompile;
            string node = SqlNames.Quote(tree.NodeColumn);
            StringBuilder text = new();
            _ = text.Append(CultureInfo.InvariantCulture, $"DECLARE {keysTable} TABLE ([Id] {plan.Root.KeyType} PRIMARY KEY);\n");
            WriteNotFoundCheck(text, list, readable, recompile);
            _ = text.Append(CultureInfo.InvariantCulture, $"INSERT INTO {keysTable} ([Id])\n")
                .Append(CultureInfo.InvariantCulture, $"SELECT d.[Id] FROM {plan.Root.Table} AS d\n")
                .Append(CultureInfo.InvariantCulture, $"WHERE EXISTS (SELECT 1 FROM {plan.Root.Table} AS a JOIN {list} AS s ON s.[Id] = a.[Id]\n")
                .Append(CultureInfo.InvariantCulture, $"              WHERE d.{node}.IsDescendantOf(a.{node}) = 1)")
                .Append(Hint(recompile))
                .Append(";\n");

            IReadOnlyList<ScalarParameter> slots = Slots(readable, arguments: null);
            statements.Add(new WriteStatement(
                ordinal,
                text.ToString(),
                draft =>
                {
                    _ = BindIds(draft, keys, isLong);
                    _ = draft.Name(ReservedSqlNames.StatementNames.Keys);
                    Engine(draft, slots);
                },
                1 + slots.Count,
                Writes: null,
                entity.ClrType,
                MissingIdsReader(ordinal)));

            return new Planned(ordinal + 1, keysTable, string.Empty, static _ => { }, 0, [], Stamps: null, recompile);
        }

        /// <summary>The rows of a stamp list: each id with the stamp it was read with.</summary>
        /// <param name="entity">The entity.</param>
        /// <param name="keys">The ids.</param>
        /// <param name="stamps">The stamps, in the order of the ids.</param>
        /// <param name="isLong">Whether the key is a <see cref="long" />.</param>
        /// <param name="parameterName">The caller's parameter name.</param>
        /// <returns>The rows.</returns>
        /// <exception cref="ArgumentException">The stamps do not pair with the ids, one is missing, or the entity has no stamp.</exception>
        private static IdStampList[] StampRows(EntityMetadata entity, long[] keys, IReadOnlyList<DateTimeOffset?> stamps, bool isLong, string parameterName)
        {
            if (AuditProperty(entity, nameof(Abstractions.Entities.TopLevelEntity.ModifiedAt)) is null)
            {
                throw new ArgumentException($"The entity {entity.Name} has no stamp, so its delete compares none.", parameterName);
            }

            if (isLong)
            {
                throw new ArgumentException($"The entity {entity.Name} has a long key, and a stamp list carries int ids; delete it without stamps.", parameterName);
            }

            if (stamps.Count != keys.Length)
            {
                throw new ArgumentException(
                    string.Create(CultureInfo.InvariantCulture, $"The delete names {keys.Length} ids and {stamps.Count} stamps; each id has its stamp."), parameterName);
            }

            var rows = new IdStampList[keys.Length];
            for (int index = 0; index < keys.Length; index++)
            {
                rows[index] = stamps[index] is DateTimeOffset stamp
                    ? new IdStampList { Id = checked((int)keys[index]), ModifiedAt = stamp }
                    : throw new ArgumentException(
                        string.Create(CultureInfo.InvariantCulture, $"The stamp of the id {keys[index]} is missing; a delete that compares stamps compares every row's."), parameterName);
            }

            return rows;
        }

        /// <summary>What the delete statement builds on, from the statement that captured its keys or from its own head.</summary>
        /// <param name="Ordinal">The delete statement's ordinal.</param>
        /// <param name="Keys">The keys table.</param>
        /// <param name="Head">The text the delete statement starts with after its capture tables: its own key capture, or empty.</param>
        /// <param name="Bind">Binds the head's parameters and names, ahead of the delete's own.</param>
        /// <param name="Parameters">How many scalar and table parameters the head binds.</param>
        /// <param name="Slots">The head's compiled slots.</param>
        /// <param name="Stamps">The stamp list to compare, or null.</param>
        /// <param name="Recompile">Whether the delete's statements carry <c>OPTION (RECOMPILE)</c>.</param>
        private sealed record Planned(
            int Ordinal,
            string Keys,
            string Head,
            Action<StatementDraft> Bind,
            int Parameters,
            IReadOnlyList<ScalarParameter> Slots,
            string? Stamps,
            bool Recompile);
    }
}
