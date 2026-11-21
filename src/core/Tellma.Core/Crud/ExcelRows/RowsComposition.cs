// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Excel;
using Tellma.Core.Abstractions.TableTypes;
using Tellma.Core.Crud.Query;
using Tellma.Core.Crud.Validation;
using Tellma.Core.Queryex;

namespace Tellma.Core.Crud.ExcelRows
{
    // The statements are spec 0015 §15: a query on the row source's own root conjoins the source's
    // clauses with the caller's read filter and restricts on the source's ids and the query's key
    // set; a dependent rides its owner's statement through the keys it captured (spec 0012 §5.4),
    // or a bound list of the owner's ids on a follow-up; a lookup target reads under the caller's
    // read decision on its entity, a denial reading nothing. The traversal and select-type rules
    // are §5.2's, held to every statement as to a query.
    /// <summary>
    ///     One composition of an Excel row source's read batch, under the connected user it is
    ///     composed for: each statement's decision taken once from that user's set, planned and
    ///     compiled with every diagnostic before the round trip, and appended to the batch with the
    ///     statements of its dependents.
    /// </summary>
    /// <param name="operation">The scope's row-source services.</param>
    /// <param name="binding">The row source's binding: its surface, its source and what admitting them found.</param>
    /// <param name="user">The connected user the batch is composed for.</param>
    /// <param name="batch">The batch.</param>
    /// <param name="refusedBy">Told the entity whose decision refused a path, before the refusal is thrown.</param>
    internal sealed class RowsComposition(ExcelRowsOperation operation, RowsBinding binding, ConnectedUser user, IDataBatch batch, Action<string> refusedBy)
    {
        /// <summary>The problem code of a refused securable.</summary>
        private const string ForbiddenCode = "forbidden";

        /// <summary>The most characters a bound string key holds: the string key-set table's width.</summary>
        private const int MaxStringKey = 450;

        /// <summary>The decisions of this composition, each taken once from the connected user's set, by entity.</summary>
        private readonly Dictionary<string, AccessDecision> _decided = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>The tenant's schema, read once per composition.</summary>
        private QueryexSchema Schema => field ??= operation.Database.Schema;

        /// <summary>
        ///     Composes one chunk of a node's rows and, when asked, the first chunk of each of its
        ///     dependents at every depth, restricted to the rows of the chunk through the keys it
        ///     captures.
        /// </summary>
        /// <param name="node">The node.</param>
        /// <param name="owner">The owner ids a follow-up of a dependent is restricted to; null for a root, or for a dependent riding its owner's statement.</param>
        /// <param name="skip">The rows to skip.</param>
        /// <param name="take">The chunk's size.</param>
        /// <param name="countCap">Where the chunk's capped count stops; null for no count.</param>
        /// <param name="withDependents">Whether the node's dependents ride the chunk.</param>
        /// <returns>The chunk's handles.</returns>
        /// <exception cref="ForbiddenException">The connected user's set no longer grants the read of the row source's root, or a path is refused.</exception>
        public Chunk Compose(RowsNode node, IReadOnlyList<long>? owner, int skip, int take, int? countCap, bool withDependents)
        {
            ArgumentNullException.ThrowIfNull(node);
            return Compose(node, owner, captured: null, skip, take, countCap, withDependents);
        }

        /// <summary>Composes a chunk, restricted to an owner's ids or to the keys an owner statement captured.</summary>
        /// <param name="node">The node.</param>
        /// <param name="owner">The owner ids a follow-up is restricted to; null otherwise.</param>
        /// <param name="captured">The key table the owner statement captured; null otherwise.</param>
        /// <param name="skip">The rows to skip.</param>
        /// <param name="take">The chunk's size.</param>
        /// <param name="countCap">Where the chunk's capped count stops; null for no count.</param>
        /// <param name="withDependents">Whether the node's dependents ride the chunk.</param>
        /// <returns>The chunk's handles.</returns>
        private Chunk Compose(RowsNode node, IReadOnlyList<long>? owner, SqlIdentifier? captured, int skip, int take, int? countCap, bool withDependents)
        {
            // Every pipeline batch compiles under the pipeline's ceilings, so the batch admits what the plan admitted.
            batch.Limits = QueryexProfiles.PipelineLimits;
            EntityDescriptor root = Schema.FindEntity(node.Entity.Name)
                ?? throw new InvalidOperationException($"The tenant's schema has no entity for the row source root '{node.Entity.Name}'.");

            // --- The decision on the root: the row source's own refuses when it grants nothing, a
            // lookup target's denial reads nothing, a dependent reads under its owner's rows ---
            FilterTree? accessFilter = null;
            switch (node.Kind)
            {
                case RowsRoot.Own:
                    AccessDecision own = Decide(root.Name);
                    if (own.Outcome == AccessOutcome.Denied)
                    {
                        throw new ForbiddenException(ForbiddenCode, own.Resource, own.Action, ForbiddenReason.NoGrant);
                    }

                    accessFilter = own.Filter;
                    break;
                case RowsRoot.Lookup:
                    // The decision comes from the set the batch is composed under, which the batch's
                    // prologue validates: a moved permissions tag recomposes the batch from the fresh
                    // set, so a stale denial never stands (spec 0014 §6.4).
                    TargetRead read = operation.Reads.Of(user, node.Entity);
                    if (read.IsDenied)
                    {
                        return Chunk.Empty(node, take, countCap);
                    }

                    if (read.IsSensitive)
                    {
                        operation.Reads.EnsureStepUp();
                    }

                    accessFilter = read.Decision.Filter;
                    break;
                case RowsRoot.Dependent:
                    break;
                default:
                    throw new InvalidOperationException($"A row source root is its own, a dependent or a lookup target, not {node.Kind}.");
            }

            // --- The restrictions: the source's ids, the query's key set, the owner's rows ---
            List<KeySetRestriction> restrictions = [];
            if (node.Kind == RowsRoot.Own && binding.Ids is IReadOnlyList<long> ids)
            {
                if (ids.Count == 0)
                {
                    return Chunk.Empty(node, take, countCap);
                }

                restrictions.Add(new KeySetRestriction(root.Key.Name, BindKeys(binding.Surface.KeyType, ids).Name));
            }

            if (node.Query.Restriction is KeySetRestriction restriction)
            {
                if (BindValues(root, restriction, node.Query.RestrictionValues!) is not SqlIdentifier values)
                {
                    return Chunk.Empty(node, take, countCap);
                }

                restrictions.Add(new KeySetRestriction(restriction.Path, values.Name));
            }

            if (captured is SqlIdentifier keys)
            {
                restrictions.Add(new KeySetRestriction(node.ParentKey!, keys.Name));
            }
            else if (owner is not null)
            {
                restrictions.Add(new KeySetRestriction(node.ParentKey!, BindKeys(node.OwnerKeyType!, owner).Name));
            }

            // --- The plan, compiled: the source's clauses on the own root, any diagnostic refusing here ---
            ExportSource.ByQuery? clauses = node.Kind == RowsRoot.Own ? binding.Source as ExportSource.ByQuery : null;
            var shape = QueryShape.AdmitRows(node.Surface, node.Select, node.OrderBy, clauses, skip, take);
            PlannedQuery plan = operation.Planner.Plan(
                node.Surface,
                shape,
                Schema,
                root,
                accessFilter,
                node.Kind == RowsRoot.Own ? binding.CustomSearch : null,
                hasUser: operation.Database.Context.UserId is not null);

            // --- The traversal rule over the statement's paths, which also asks for the step-up of a
            // sensitive entity read beyond its projection: the first refusal, its entity told ---
            IReadOnlyList<TraversalRefusal> refusals = operation.Traversal.Check(Schema, root.Name, plan.Discovery, Decide);
            if (refusals.Count > 0)
            {
                TraversalRefusal first = refusals[0];
                refusedBy(first.Entity);
                throw new ForbiddenException(ForbiddenCode, first.Entity, AccessActions.Read, ForbiddenReason.RestrictedPath, string.Join('.', first.Path.Segments));
            }

            // --- The select-item types ---
            QueryPlanner.RequireSelectable(plan, node.Entity.Name);

            // --- The row read, capturing its keys for its dependents, which ride it at every depth ---
            bool capture = withDependents && node.Dependents.Count > 0;
            BatchResult<RowPage> rows = batch.Rows(
                plan.Spec with { Restrictions = restrictions },
                plan.Arguments,
                new RowQueryOptions { CountCap = countCap, CaptureKeys = capture });
            if (!capture)
            {
                return new Chunk(node, rows, take, dependents: null);
            }

            SqlIdentifier ownKeys = KeyTables.Of(batch).KeysOf(rows)
                ?? throw new InvalidOperationException($"The row read of {node.Entity.Name} captured no keys for its dependents.");
            List<Chunk> dependents = [.. node.Dependents.Select(dependent => Compose(dependent, owner: null, ownKeys, skip: 0, binding.MaxTake, countCap: null, withDependents: true))];
            return new Chunk(node, rows, take, dependents);
        }

        /// <summary>The connected user's read decision on an entity, taken once per composition.</summary>
        /// <param name="entity">The entity's name.</param>
        /// <returns>The decision; a denial for an entity no read securable guards.</returns>
        private AccessDecision Decide(string entity)
        {
            if (!_decided.TryGetValue(entity, out AccessDecision? decision))
            {
                decision = operation.Reads.Decide(user, entity, AccessActions.Read);
                _decided[entity] = decision;
            }

            return decision;
        }

        /// <summary>Binds keys as a table-valued parameter of the key's width.</summary>
        /// <param name="keyType">The key type, <see cref="int" /> or <see cref="long" />.</param>
        /// <param name="keys">The keys, distinct.</param>
        /// <returns>The parameter's name.</returns>
        private SqlIdentifier BindKeys(Type keyType, IReadOnlyList<long> keys)
        {
            return keyType == typeof(int)
                ? KeyTables.Bind(batch, [.. keys.Select(static key => checked((int)key))])
                : KeyTables.Bind(batch, keys);
        }

        // Spec 0019 §10.1 binds a lookup's values by the key's CLR type: StringList (nvarchar(450)),
        // IdList, BigIdList, GuidList or DateList, named as the restriction's table source.
        /// <summary>
        ///     Binds a restriction's values as the table-valued parameter of the restricted property's
        ///     type, distinct as the store compares them. A number no integer key can hold matches no
        ///     row, so it is left out.
        /// </summary>
        /// <param name="root">The query's root.</param>
        /// <param name="restriction">The restriction.</param>
        /// <param name="values">The values.</param>
        /// <returns>The parameter's name; null when no value can match a row.</returns>
        /// <exception cref="ArgumentException">The path names no property, its type takes no key-set table, or a value is not of its type.</exception>
        private SqlIdentifier? BindValues(EntityDescriptor root, KeySetRestriction restriction, IReadOnlyList<object> values)
        {
            PropertyDescriptor property = Resolve(root, restriction.Path);
            QueryexStoreFamily? family = property.StoreType?.Family;
            if (property.Type == QueryexType.QxString)
            {
                string[] strings = [.. values.Select(value => String(value, restriction.Path)).Distinct(ContextLoader.CollationKeyComparer.Instance)];
                return strings.Length == 0 ? null : batch.Tvp([.. strings.Select(static value => new StringList { Id = value })]);
            }

            if (property.Type == QueryexType.QxGuid)
            {
                Guid[] guids = [.. values.Select(value => value is Guid guid ? guid : throw Mistyped(restriction.Path, value, "GUID")).Distinct()];
                return guids.Length == 0 ? null : batch.Tvp([.. guids.Select(static value => new GuidList { Id = value })]);
            }

            if (property.Type == QueryexType.QxDate)
            {
                DateOnly[] dates = [.. values.Select(value => value is DateOnly date ? date : throw Mistyped(restriction.Path, value, "date")).Distinct()];
                return dates.Length == 0 ? null : batch.Tvp([.. dates.Select(static value => new DateList { Id = value })]);
            }

            if (property.Type == QueryexType.QxNumeric && family is QueryexStoreFamily.QxInt or QueryexStoreFamily.QxSmallInt or QueryexStoreFamily.QxTinyInt)
            {
                int[] ints = [.. Integers(values, restriction.Path, int.MinValue, int.MaxValue).Select(static value => (int)value)];
                return ints.Length == 0 ? null : batch.Tvp([.. ints.Select(static value => new IdList { Id = value })]);
            }

            if (property.Type == QueryexType.QxNumeric && family is QueryexStoreFamily.QxBigInt)
            {
                long[] longs = [.. Integers(values, restriction.Path, long.MinValue, long.MaxValue)];
                return longs.Length == 0 ? null : batch.Tvp([.. longs.Select(static value => new BigIdList { Id = value })]);
            }

            throw new ArgumentException(
                $"The restriction path '{restriction.Path}' of {root.Name} ends at a {property.Type} property, which no key-set table holds.",
                nameof(restriction));
        }

        /// <summary>The property a restriction path ends at, through its navigations.</summary>
        /// <param name="root">The query's root.</param>
        /// <param name="path">The dotted path.</param>
        /// <returns>The property.</returns>
        /// <exception cref="ArgumentException">The path does not resolve.</exception>
        private static PropertyDescriptor Resolve(EntityDescriptor root, string path)
        {
            string[] segments = path.Split('.');
            EntityDescriptor at = root;
            for (int index = 0; index < segments.Length - 1; index++)
            {
                at = SchemaLookup.Navigation(at, segments[index])?.Target
                    ?? throw new ArgumentException($"The restriction path '{path}' of {root.Name} does not resolve.", nameof(path));
            }

            return SchemaLookup.Property(at, segments[^1])
                ?? throw new ArgumentException($"The restriction path '{path}' of {root.Name} does not resolve.", nameof(path));
        }

        /// <summary>A string key value, within the key-set table's width.</summary>
        /// <param name="value">The value.</param>
        /// <param name="path">The restriction's path, for a refusal.</param>
        /// <returns>The string.</returns>
        /// <exception cref="ArgumentException">The value is no string, or is wider than the table.</exception>
        private static string String(object value, string path)
        {
            return value is not string text
                ? throw Mistyped(path, value, "string")
                : text.Length > MaxStringKey
                    ? throw new ArgumentException(string.Create(CultureInfo.InvariantCulture, $"A value of the restriction path '{path}' is longer than {MaxStringKey} characters."), nameof(value))
                    : text;
        }

        /// <summary>The integer key values in a range, distinct; a fraction or a number beyond the range matches no row and is left out.</summary>
        /// <param name="values">The values.</param>
        /// <param name="path">The restriction's path, for a refusal.</param>
        /// <param name="minimum">The least value the key holds.</param>
        /// <param name="maximum">The greatest value the key holds.</param>
        /// <returns>The values.</returns>
        /// <exception cref="ArgumentException">A value is no number.</exception>
        private static IEnumerable<long> Integers(IReadOnlyList<object> values, string path, long minimum, long maximum)
        {
            HashSet<long> seen = [];
            foreach (object value in values)
            {
                decimal? number = value switch
                {
                    int narrow => narrow,
                    long wide => wide,
                    short small => small,
                    byte tiny => tiny,
                    sbyte signed => signed,
                    ushort unsignedSmall => unsignedSmall,
                    uint unsignedNarrow => unsignedNarrow,
                    ulong unsignedWide => unsignedWide,
                    decimal exact => exact,
                    double real when double.IsFinite(real) && Math.Abs(real) < 7.9e28 => (decimal)real,
                    double => null,
                    float single when float.IsFinite(single) && Math.Abs(single) < 7.9e28f => (decimal)single,
                    float => null,
                    _ => throw Mistyped(path, value, "number"),
                };
                if (number is decimal held && held == decimal.Truncate(held) && held >= minimum && held <= maximum && seen.Add((long)held))
                {
                    yield return (long)held;
                }
            }
        }

        /// <summary>The refusal of a value whose type is not the restricted property's.</summary>
        /// <param name="path">The restriction's path.</param>
        /// <param name="value">The value.</param>
        /// <param name="expected">The type the property takes.</param>
        /// <returns>The exception.</returns>
        private static ArgumentException Mistyped(string path, object? value, string expected)
        {
            return new ArgumentException(
                $"A value of the restriction path '{path}' is a {value?.GetType().Name ?? "null"}, where the property takes a {expected}.",
                nameof(value));
        }
    }

    /// <summary>The handles of one composed chunk of a node's rows, and of its dependents' first chunks.</summary>
    /// <param name="node">The node.</param>
    /// <param name="rows">The row read's handle; null for a chunk that reads nothing.</param>
    /// <param name="take">The chunk's size, which a full chunk reaches.</param>
    /// <param name="dependents">The dependents' first chunks, parallel to the node's; null when they do not ride this chunk.</param>
    internal sealed class Chunk(RowsNode node, BatchResult<RowPage>? rows, int take, IReadOnlyList<Chunk>? dependents)
    {
        /// <summary>The node.</summary>
        public RowsNode Node { get; } = node;

        /// <summary>The row read's handle; null for a chunk that reads nothing.</summary>
        public BatchResult<RowPage>? Rows { get; } = rows;

        /// <summary>The chunk's size.</summary>
        public int Take { get; } = take;

        /// <summary>The dependents' first chunks; null when they do not ride this chunk.</summary>
        public IReadOnlyList<Chunk>? Dependents { get; } = dependents;

        /// <summary>The count a chunk that reads nothing answers: zero when a count was asked for.</summary>
        private int? EmptyCount { get; init; }

        /// <summary>A chunk that reads nothing: a denied lookup target, or a restriction no value of which can match.</summary>
        /// <param name="node">The node.</param>
        /// <param name="take">The chunk's size.</param>
        /// <param name="countCap">The count cap asked for; null for no count.</param>
        /// <returns>The chunk.</returns>
        public static Chunk Empty(RowsNode node, int take, int? countCap)
        {
            return new Chunk(node, rows: null, take, dependents: null) { EmptyCount = countCap is null ? null : 0 };
        }

        /// <summary>Reads the executed chunk: its rows as the query's columns, its keys, its count, and its dependents' chunks.</summary>
        /// <returns>The rows.</returns>
        public ChunkRows Read()
        {
            if (Rows is null)
            {
                return new ChunkRows([], [], EmptyCount, Full: false, Dependents: null);
            }

            RowPage page = Rows.Value;
            QueryRowSet set = page.Rows;
            if (set.Columns.Count < Node.Width || (Node.KeyColumn is int expected && set.Columns.Count <= expected))
            {
                throw new InvalidOperationException($"The row read of {Node.Entity.Name} answered {set.Columns.Count} columns where its select holds more.");
            }

            // --- Each row's values in select order, the reader's own; the keys beside them ---
            Array[] buffers = [.. Enumerable.Range(0, Node.Width).Select(set.GetBuffer)];
            Array? keyBuffer = Node.KeyColumn is int column ? set.GetBuffer(column) : null;
            List<object?[]> rows = new(set.RowCount);
            List<long> keys = new(keyBuffer is null ? 0 : set.RowCount);
            for (int row = 0; row < set.RowCount; row++)
            {
                object?[] values = new object?[Node.Width];
                for (int index = 0; index < values.Length; index++)
                {
                    values[index] = set.IsNull(index, row) ? null : buffers[index].GetValue(row);
                }

                rows.Add(values);
                if (keyBuffer is not null)
                {
                    keys.Add(Convert.ToInt64(keyBuffer.GetValue(row), CultureInfo.InvariantCulture));
                }
            }

            return new ChunkRows(rows, keys, page.Count, Full: set.RowCount >= Take, Dependents?.Select(static dependent => dependent.Read()).ToList());
        }
    }

    /// <summary>What one executed chunk read.</summary>
    /// <param name="Rows">The rows, each the query's column values in select order.</param>
    /// <param name="Keys">The rows' keys, in row order, when the node's dependents need them; empty otherwise.</param>
    /// <param name="Count">The capped count when one was asked for; null otherwise.</param>
    /// <param name="Full">Whether the chunk reached its size, so more rows may follow.</param>
    /// <param name="Dependents">The dependents' first chunks, parallel to the node's; null when they did not ride the chunk.</param>
    internal sealed record ChunkRows(IReadOnlyList<object?[]> Rows, IReadOnlyList<long> Keys, int? Count, bool Full, IReadOnlyList<ChunkRows>? Dependents);
}
