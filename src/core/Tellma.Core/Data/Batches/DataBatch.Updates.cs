// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Frozen;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Entities;
using Tellma.Core.Abstractions.Hosting;
using Tellma.Core.Data.Saves;
using Tellma.Core.Data.Trees;
using Tellma.Core.Queryex;

namespace Tellma.Core.Data.Batches
{
    // The update statement of spec 0012 §9.1: the keys captured (by ids under the grant filter, or
    // by query, capped and count-verified), then one UPDATE that skips the rows already holding the
    // assigned values; AllOrNothing applies §9.2's not-found and row-security rules first, and an
    // activatable tree recounts over the keys (§10.3).
    /// <content>The update statement: the same values assigned to every selected row, in one statement.</content>
    internal sealed partial class DataBatch
    {
        /// <summary>The properties a uniform update never assigns, beyond what their ownership forbids: the stamps and the tree's columns.</summary>
        private static readonly FrozenSet<string> Unassignable = new[]
        {
            nameof(TopLevelEntity.CreatedAt),
            nameof(TopLevelEntity.CreatedById),
            nameof(TopLevelEntity.ModifiedAt),
            nameof(TopLevelEntity.ModifiedById),
            nameof(TreeEntity.SubtreeCount),
            nameof(ActivatableTreeEntity.ActiveSubtreeCount),
        }.ToFrozenSet(StringComparer.Ordinal);

        /// <inheritdoc />
        public BatchResult<UpdateReceipt> Update<TEntity>(UpdateSpec<TEntity> spec)
            where TEntity : class
        {
            ArgumentNullException.ThrowIfNull(spec);
            (EntityMetadata metadata, EntityDescriptor root) = WriteTarget<TEntity>(nameof(Update));

            // --- The shape's assignments and stamp, checked before anything is compiled ---
            (IReadOnlyDictionary<string, object?> assignments, bool stamp) = spec switch
            {
                UpdateSpec<TEntity>.ByIds byIds => (byIds.Assignments, byIds.Stamp),
                UpdateSpec<TEntity>.ByQuery byQuery => (byQuery.Assignments, byQuery.Stamp),
                _ => throw new ArgumentException($"The update spec {spec.GetType().Name} is not one of the shapes of {nameof(UpdateSpec<>)}.", nameof(spec)),
            };
            List<(PropertyMetadata Property, object? Value)> assigned = Assigned(metadata, assignments, nameof(spec));
            (PropertyMetadata ModifiedAt, PropertyMetadata ModifiedById)? stamps = stamp ? Stamps(metadata, nameof(spec)) : null;

            FrozenSet<TableName> writes = [metadata.Table];
            CheckAuthority(writes);
            int? userId = stamp
                ? Connection.UserId ?? throw new InvalidOperationException("An update stamps the rows it writes with the current user, and the scope has none. Update in a scope that carries a user, or without stamping.")
                : null;

            // --- The statements: [the not-found check], then the update ---
            bool isLong = metadata.Key.ClrType == typeof(long);
            int first = _statements.Count;
            BatchResult<UpdateReceipt> receipt = new();
            List<WriteStatement> statements = [];
            int ordinal;
            string ids;
            StringBuilder text = new();
            Action<StatementDraft> bindIds;
            int idParameters;
            CompiledQuery keys;
            bool allOrNothing = false;
            bool large = false;
            string? countChecks = null;
            switch (spec)
            {
                case UpdateSpec<TEntity>.ByIds byIds:
                    {
                        long[] values = KeyValues(byIds.Ids, isLong, nameof(spec));
                        large = values.Length > _executor.Saves.LargeBatchThreshold;
                        allOrNothing = byIds.AllOrNothing;
                        if (allOrNothing)
                        {
                            // The not-found check: every id readable under the read filter, a hidden one missing.
                            ids = ReservedSqlNames.Statement(first, ReservedSqlNames.StatementNames.TableValued(0));
                            CompiledQuery readable = CompileKeys(root, byIds.ReadFilter, arguments: null, ids, take: null, first);
                            StringBuilder check = new();
                            WriteNotFoundCheck(check, ids, readable, large || readable.Recompile);
                            IReadOnlyList<ScalarParameter> slots = Slots(readable, arguments: null);
                            statements.Add(new WriteStatement(
                                first,
                                check.ToString(),
                                draft =>
                                {
                                    _ = BindIds(draft, values, isLong);
                                    Engine(draft, slots);
                                },
                                1 + slots.Count,
                                Writes: null,
                                metadata.ClrType,
                                MissingIdsReader(first)));
                            ordinal = first + 1;
                            bindIds = static _ => { };
                            idParameters = 0;
                        }
                        else
                        {
                            ordinal = first;
                            ids = ReservedSqlNames.Statement(ordinal, ReservedSqlNames.StatementNames.TableValued(0));
                            bindIds = draft => BindIds(draft, values, isLong);
                            idParameters = 1;
                        }

                        keys = CompileKeys(root, byIds.Filter, arguments: null, ids, take: null, ordinal);
                        break;
                    }

                case UpdateSpec<TEntity>.ByQuery byQuery:
                    {
                        ArgumentNullException.ThrowIfNull(byQuery.Filter, nameof(spec));
                        int take = CapTake(byQuery.Cap, byQuery.ExpectedCount, nameof(spec));
                        ordinal = first;
                        ids = string.Empty;
                        bindIds = static _ => { };
                        idParameters = 0;
                        keys = CompileKeys(root, byQuery.Filter, byQuery.Arguments, keySet: null, take, ordinal);

                        // The cap and the expected count follow the assignments and the user.
                        int cap = assigned.Count + (stamp ? 1 : 0);
                        countChecks = CapChecks(ordinal, cap, TellmaSqlErrors.UpdateCapExceededCode, TellmaSqlErrors.UpdateCountMismatchCode);
                        break;
                    }

                default:
                    throw new ArgumentException($"The update spec {spec.GetType().Name} is not one of the shapes of {nameof(UpdateSpec<>)}.", nameof(spec));
            }

            // --- The update's text: the stamp, the keys, the checks, the update, an activatable tree's recount ---
            string prefix = LocalPrefix(ordinal);
            string keysTable = ReservedSqlNames.Statement(ordinal, ReservedSqlNames.StatementNames.Keys);
            string now = ReservedSqlNames.Statement(ordinal, ReservedSqlNames.StatementNames.Now);
            bool recompile = keys.Recompile || large;
            if (stamp)
            {
                _ = text.Append(CultureInfo.InvariantCulture, $"DECLARE {now} datetimeoffset(7) = SYSUTCDATETIME();\n");
            }

            _ = text.Append(CultureInfo.InvariantCulture, $"DECLARE {keysTable} TABLE ([Id] {(isLong ? "bigint" : "int")} PRIMARY KEY);\n")
                .Append(keys.Prologue)
                .Append(CultureInfo.InvariantCulture, $"INSERT INTO {keysTable} ([Id])\n")
                .Append(keys.Body)
                .Append(recompile ? "OPTION (RECOMPILE)\n" : string.Empty)
                .Append(";\n");
            _ = text.Append(countChecks ?? string.Empty);
            if (allOrNothing)
            {
                // Every id readable passes the grant filter too, or nothing is updated.
                _ = text.Append(CultureInfo.InvariantCulture, $"IF (SELECT COUNT(*) FROM {keysTable}) <> (SELECT COUNT(*) FROM {ids}) THROW {TellmaSqlErrors.RowSecurity}, N'{TellmaSqlErrors.RowSecurityCode}', 1;\n");
            }

            // The users the updated rows name, before and after, which the bump epilogue reads.
            IReadOnlyList<string> userColumns = UserIdCapture.Columns(_executor.VersionTags, metadata.Table);
            string? userCapture = null;
            if (userColumns.Count > 0)
            {
                userCapture = ReservedSqlNames.Statement(ordinal, UserIdCapture.Word(metadata.Table));
                _ = text.Append(UserIdCapture.Declaration(userCapture, isLong ? "bigint" : "int", userColumns)).Append('\n');
            }

            WriteUpdate(text, metadata, assigned, stamps, ordinal, keysTable, now, recompile, userCapture is null ? null : (userCapture, userColumns));
            bool recount = metadata.Tree is { ActiveSubtreeCount: not null }
                && assigned.Exists(static item => string.Equals(item.Property.Name, nameof(IActivatable.IsActive), StringComparison.Ordinal));
            if (recount)
            {
                TreeText tree = new(text, TreeShape.Of(metadata), prefix, recompile);
                tree.CaptureOld(keysTable);
                tree.Recount(keysTable);
            }

            // --- The parameters, in the order the text numbers them: [ids], the values, [the user], [the cap and count], the slots ---
            IReadOnlyList<ScalarParameter> keySlots = Slots(keys, spec is UpdateSpec<TEntity>.ByQuery query ? query.Arguments : null);
            int scalars = assigned.Count + (stamp ? 1 : 0) + (countChecks is null ? 0 : 2);
            int updateOrdinal = ordinal;
            statements.Add(new WriteStatement(
                ordinal,
                text.ToString(),
                draft =>
                {
                    bindIds(draft);
                    foreach ((PropertyMetadata property, object? value) in assigned)
                    {
                        // A null binds as text, which every column but a binary one converts from;
                        // a binary column's null binds as binary.
                        _ = value is null && property.ClrType == typeof(byte[])
                            ? draft.Scalar(SqlDbType.VarBinary, null, ScalarValues.BinarySize)
                            : draft.Scalar(value);
                    }

                    if (userId is int user)
                    {
                        _ = draft.Scalar(user);
                    }

                    if (spec is UpdateSpec<TEntity>.ByQuery counted)
                    {
                        _ = draft.Scalar(counted.Cap);
                        _ = draft.Scalar(counted.ExpectedCount);
                    }

                    _ = draft.Name(ReservedSqlNames.StatementNames.Keys);
                    if (userCapture is not null)
                    {
                        _ = draft.Name(UserIdCapture.Word(metadata.Table));
                    }

                    Engine(draft, keySlots);
                },
                idParameters + scalars + keySlots.Count,
                writes,
                metadata.ClrType,
                async (cursor, cancellationToken) =>
                {
                    // --- The updated ids: the rows that did not already hold the values ---
                    DbDataReader set = await cursor.NextAsync(updateOrdinal, cancellationToken).ConfigureAwait(false);
                    HashSet<long> updated = await SaveEmitter.ReadIdsAsync(set, cancellationToken).ConfigureAwait(false);
                    cursor.CountRows(updated.Count);
                    cursor.Complete(receipt, new UpdateReceipt(BoxedSet(updated, isLong)));
                }));

            AppendWrites(statements);
            if (userCapture is not null)
            {
                foreach (UserIdSource source in UserIdCapture.Sources(metadata.Table, userColumns, userCapture))
                {
                    AddUserIdSource(source);
                }
            }

            // --- Once it ran: a tree's recount counts as a tree statement ---
            if (recount)
            {
                string entity = metadata.Name;
                OnExecuted(_ =>
                {
                    if (receipt.IsCompleted)
                    {
                        _executor.Metrics.RecordTreeRecompute(entity);
                    }

                    return Task.CompletedTask;
                });
            }

            return receipt;
        }

        /// <summary>
        ///     The properties an update assigns, in the entity's property order, each checked: an
        ///     editable or server-owned column of a value it can hold, never the key, a stamp, a
        ///     tree's columns, a write-once column or a blob reference.
        /// </summary>
        /// <param name="metadata">The entity.</param>
        /// <param name="assignments">Property name to value.</param>
        /// <param name="parameterName">The caller's parameter name.</param>
        /// <returns>The properties and their values.</returns>
        /// <exception cref="ArgumentException">An assignment is refused.</exception>
        private static List<(PropertyMetadata Property, object? Value)> Assigned(EntityMetadata metadata, IReadOnlyDictionary<string, object?> assignments, string parameterName)
        {
            ArgumentNullException.ThrowIfNull(assignments, parameterName);
            if (assignments.Count == 0)
            {
                throw new ArgumentException($"The update of {metadata.Name} assigns nothing; assign at least one property.", parameterName);
            }

            foreach (string name in assignments.Keys)
            {
                PropertyMetadata property = metadata.Properties.FirstOrDefault(candidate => string.Equals(candidate.Name, name, StringComparison.Ordinal))
                    ?? throw new ArgumentException($"The update of {metadata.Name} assigns '{name}', which is not one of its properties.", parameterName);
                string? refusal = property switch
                {
                    _ when ReferenceEquals(property, metadata.Key) => "is the key",
                    _ when Unassignable.Contains(property.Name) => "is kept by the data layer",
                    _ when metadata.Tree is TreeMetadata tree && ReferenceEquals(property, tree.ParentKey) => "re-parents a tree row, which a save does, since the rows' paths change",
                    _ when metadata.BlobReferences.Any(blob => ReferenceEquals(blob.Property, property)) => "is a blob reference, which changes only through a save that captures it",
                    { Ownership: PropertyOwnership.WriteOnce } => "is write-once",
                    { Ownership: PropertyOwnership.Derived or PropertyOwnership.DatabaseOwned } => $"is {property.Ownership.ToString().ToLowerInvariant()}, so no statement assigns it",
                    _ => null,
                };
                if (refusal is not null)
                {
                    throw new ArgumentException($"The update of {metadata.Name} assigns '{name}', which {refusal}.", parameterName);
                }

                // The value must be one the column holds: a null only where the column admits one.
                object? value = assignments[name];
                Type type = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
                if (value is null ? !property.IsNullable : !type.IsInstanceOfType(value) || !ScalarValues.IsScalar(value))
                {
                    throw new ArgumentException(
                        $"The update of {metadata.Name} assigns '{name}' {(value is null ? "null" : $"a {value.GetType().Name}")}, which its {property.ClrType.Name} column does not hold.",
                        parameterName);
                }

                // A sized text or binary column holds no longer value: refused here rather than
                // truncated or failed by the database. A string's length counts UTF-16 code units, as
                // an nvarchar column's length does.
                int? length = value switch
                {
                    string text => text.Length,
                    byte[] bytes => bytes.Length,
                    _ => null,
                };
                if (length > property.MaxLength)
                {
                    throw new ArgumentException(
                        $"The update of {metadata.Name} assigns '{name}' a value of length {length}, longer than its column's {property.MaxLength}.",
                        parameterName);
                }

                // An enum binds by the name of one defined member: a combination or an undefined
                // value has none, and is refused here rather than when the statement binds.
                if (value is Enum member && !Enum.IsDefined(member.GetType(), member))
                {
                    throw new ArgumentException(
                        $"The update of {metadata.Name} assigns '{name}' the value {member}, which is not a defined member of {member.GetType().Name}.",
                        parameterName);
                }

                // A decimal column holds as many integral digits as its precision leaves its scale;
                // more fail in the database as an arithmetic overflow. Extra fractional digits round.
                if (value is decimal number && property is { Precision: int precision, Scale: int scale }
                    && IntegralDigits(number) > precision - scale)
                {
                    throw new ArgumentException(
                        $"The update of {metadata.Name} assigns '{name}' the value {number.ToString(CultureInfo.InvariantCulture)}, whose integral digits pass its decimal({precision.ToString(CultureInfo.InvariantCulture)}, {scale.ToString(CultureInfo.InvariantCulture)}) column.",
                        parameterName);
                }
            }

            return [.. metadata.Properties.Where(property => assignments.ContainsKey(property.Name)).Select(property => (property, assignments[property.Name]))];
        }

        /// <summary>The digits of a decimal's integral part; none for a value below one in magnitude.</summary>
        /// <param name="value">The value.</param>
        /// <returns>The digit count.</returns>
        private static int IntegralDigits(decimal value)
        {
            decimal integral = decimal.Truncate(Math.Abs(value));
            return integral == 0 ? 0 : integral.ToString(CultureInfo.InvariantCulture).Length;
        }

        /// <summary>The stamp properties of an entity an update stamps.</summary>
        /// <param name="metadata">The entity.</param>
        /// <param name="parameterName">The caller's parameter name.</param>
        /// <returns>The modified-at and modified-by properties.</returns>
        /// <exception cref="ArgumentException">The entity has no stamp columns.</exception>
        private static (PropertyMetadata ModifiedAt, PropertyMetadata ModifiedById) Stamps(EntityMetadata metadata, string parameterName)
        {
            PropertyMetadata? at = AuditProperty(metadata, nameof(TopLevelEntity.ModifiedAt));
            PropertyMetadata? by = AuditProperty(metadata, nameof(TopLevelEntity.ModifiedById));
            return at is not null && by is not null
                ? (at, by)
                : throw new ArgumentException($"The entity {metadata.Name} has no stamp columns, so its update does not stamp: set Stamp to false.", parameterName);
        }

        /// <summary>The row limit of a capped key capture: the cap plus one, so a capture past the cap shows.</summary>
        /// <param name="cap">The cap.</param>
        /// <param name="expected">The expected count.</param>
        /// <param name="parameterName">The caller's parameter name.</param>
        /// <returns>The limit.</returns>
        /// <exception cref="ArgumentOutOfRangeException">The cap or the expected count is out of range.</exception>
        private static int CapTake(int cap, int expected, string parameterName)
        {
            return cap is < 0 or int.MaxValue
                ? throw new ArgumentOutOfRangeException(parameterName, cap, "A cap is zero or more, and less than the largest integer.")
                : expected >= 0
                ? cap + 1
                : throw new ArgumentOutOfRangeException(parameterName, expected, "An expected count is zero or more.");
        }

        /// <summary>The checks of a capped capture: the cap, then the expected count, from two consecutive scalars.</summary>
        /// <param name="ordinal">The statement's ordinal.</param>
        /// <param name="cap">The index of the cap's scalar; the expected count's is the next.</param>
        /// <param name="capCode">The code of a capture past the cap.</param>
        /// <param name="countCode">The code of a capture of another count.</param>
        /// <returns>The text.</returns>
        private static string CapChecks(int ordinal, int cap, string capCode, string countCode)
        {
            string keys = ReservedSqlNames.Statement(ordinal, ReservedSqlNames.StatementNames.Keys);
            string capName = ReservedSqlNames.Statement(ordinal, ReservedSqlNames.StatementNames.Scalar(cap));
            string countName = ReservedSqlNames.Statement(ordinal, ReservedSqlNames.StatementNames.Scalar(cap + 1));
            return string.Create(
                CultureInfo.InvariantCulture,
                $"IF (SELECT COUNT(*) FROM {keys}) > {capName} THROW {TellmaSqlErrors.LimitExceeded}, N'{capCode}', 1;\nIF (SELECT COUNT(*) FROM {keys}) <> {countName} THROW {TellmaSqlErrors.CountMismatch}, N'{countCode}', 1;\n");
        }

        /// <summary>
        ///     Writes the update itself: the assigned values and the stamps on every captured row that
        ///     does not already hold the values (compared null-safe, character columns as binary),
        ///     the updated ids its result set.
        /// </summary>
        /// <param name="text">Where the text is written.</param>
        /// <param name="metadata">The entity.</param>
        /// <param name="assigned">The assigned properties, bound as the first scalars.</param>
        /// <param name="stamps">The stamp properties, or null when not stamping.</param>
        /// <param name="ordinal">The statement's ordinal.</param>
        /// <param name="keys">The keys table.</param>
        /// <param name="now">The stamp variable.</param>
        /// <param name="recompile">Whether the statement carries <c>OPTION (RECOMPILE)</c>.</param>
        /// <param name="users">The user-id capture and its columns, when the table has a user-level version tag rule; else null.</param>
        private static void WriteUpdate(
            StringBuilder text,
            EntityMetadata metadata,
            List<(PropertyMetadata Property, object? Value)> assigned,
            (PropertyMetadata ModifiedAt, PropertyMetadata ModifiedById)? stamps,
            int ordinal,
            string keys,
            string now,
            bool recompile,
            (string Capture, IReadOnlyList<string> Columns)? users)
        {
            List<string> set = [];
            List<string> stored = [];
            List<string> values = [];
            for (int index = 0; index < assigned.Count; index++)
            {
                PropertyMetadata property = assigned[index].Property;
                string column = SqlNames.Quote(property.Column);
                string parameter = ReservedSqlNames.Statement(ordinal, ReservedSqlNames.StatementNames.Scalar(index));
                string binary = property.ClrType == typeof(string) ? " COLLATE " + DatabaseCollations.Binary : string.Empty;
                set.Add($"t.{column} = {parameter}");
                stored.Add($"t.{column}{binary}");
                values.Add($"{parameter}{binary}");
            }

            if (stamps is { } stamped)
            {
                set.Add($"t.{SqlNames.Quote(stamped.ModifiedAt.Column)} = {now}");
                set.Add($"t.{SqlNames.Quote(stamped.ModifiedById.Column)} = {ReservedSqlNames.Statement(ordinal, ReservedSqlNames.StatementNames.Scalar(assigned.Count))}");
            }

            _ = text.Append(CultureInfo.InvariantCulture, $"UPDATE t SET {string.Join(", ", set)}\n");
            if (users is { } capture)
            {
                _ = text.Append(CultureInfo.InvariantCulture, $"OUTPUT {UserIdCapture.Values("inserted.[Id]", "deleted", "inserted", capture.Columns)} INTO {capture.Capture} ({UserIdCapture.Targets(capture.Columns)})\n");
            }

            _ = text.Append("OUTPUT inserted.[Id]\n")
                .Append(CultureInfo.InvariantCulture, $"FROM {metadata.Table.Delimited} AS t\n")
                .Append(CultureInfo.InvariantCulture, $"WHERE t.[Id] IN (SELECT [Id] FROM {keys})\n")
                .Append(CultureInfo.InvariantCulture, $"  AND EXISTS (SELECT {string.Join(", ", stored)} EXCEPT SELECT {string.Join(", ", values)})")
                .Append(Hint(recompile))
                .Append(";\n");
        }

        /// <summary>Binds a compiled query's slots to a draft.</summary>
        /// <param name="draft">The draft.</param>
        /// <param name="slots">The slots.</param>
        private static void Engine(StatementDraft draft, IReadOnlyList<ScalarParameter> slots)
        {
            foreach (ScalarParameter slot in slots)
            {
                draft.Engine(slot);
            }
        }
    }
}
