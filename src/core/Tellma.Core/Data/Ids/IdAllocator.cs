// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Data.Common;
using System.Globalization;
using System.Runtime.CompilerServices;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Data.Batches;
using Tellma.Core.Data.Conventions;

namespace Tellma.Core.Data.Ids
{
    // Allocation follows spec 0012 §7: exact-deficit reservation riding the batch it is given, a
    // per (tenant, sequence) buffer refilled to the low-water mark by batches that write its table,
    // assignment with temporary-id rewriting, the return of unused ids before any persist, and the
    // heal of a sequence found behind its table at reservation.
    /// <summary>
    ///     Allocates new rows' ids from each table's sequence through a buffer of ranges per tenant and
    ///     sequence. A singleton, shared by every batch of the process.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The buffer serves a sequence once a reservation or a take used it; batches that write the
    ///         sequence's table then keep it at the low-water mark with a refill statement that rides
    ///         them. A table whose rows fixed-text statements insert with their own sequence calls
    ///         never enters the buffer.
    ///     </para>
    ///     <para>
    ///         A range read back from the database is kept only above the table's largest id, so ids an
    ///         out-of-band insert wrote are never handed out; a reservation that finds its range at or
    ///         below that id consumes the gap with one more round trip and logs the heal. A duplicate
    ///         primary key surfacing from a persist is never healed: the sequence's buffered ranges are
    ///         dropped, so the next reservation reads the table afresh.
    ///     </para>
    /// </remarks>
    internal sealed class IdAllocator : IIdAllocator
    {
        /// <summary>The execution settings: the low-water mark.</summary>
        private readonly IOptions<DataOptions> _options;

        /// <summary>The running model's metadata.</summary>
        private readonly IEntityMetadataProvider _metadata;

        /// <summary>The tenant model, which declares the sequences.</summary>
        private readonly IModel _model;

        /// <summary>The logger.</summary>
        private readonly ILogger _logger;

        /// <summary>The id instruments.</summary>
        private readonly DataMetrics _metrics;

        /// <summary>The buffers, by tenant and sequence, created when a reservation or a take first uses one.</summary>
        private readonly ConcurrentDictionary<(int TenantId, TableName Sequence), SequenceBuffer> _buffers = new();

        /// <summary>The rows a reservation assigned an id to, until a batch that inserted them commits.</summary>
        private readonly ConditionalWeakTable<object, ReservationHold> _assigned = [];

        /// <summary>The sequences of the model's tables, built on first use.</summary>
        private readonly Lazy<SequenceMap> _sequences;

        /// <summary>Creates the allocator.</summary>
        /// <param name="options">The execution settings.</param>
        /// <param name="metadata">The running model's metadata.</param>
        /// <param name="model">The tenant model.</param>
        /// <param name="logger">The logger.</param>
        /// <param name="metrics">The data layer's instruments.</param>
        public IdAllocator(IOptions<DataOptions> options, IEntityMetadataProvider metadata, IModel model, ILogger<IdAllocator> logger, DataMetrics metrics)
        {
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(metadata);
            ArgumentNullException.ThrowIfNull(model);
            ArgumentNullException.ThrowIfNull(logger);
            ArgumentNullException.ThrowIfNull(metrics);

            _options = options;
            _metadata = metadata;
            _model = model;
            _logger = logger;
            _metrics = metrics;
            _sequences = new(BuildSequences, LazyThreadSafetyMode.ExecutionAndPublication);
        }

        /// <summary>The low-water mark: the ids a buffer is refilled to and a reservation tops it up to.</summary>
        private int LowWater => _options.Value.IdBufferLowWater;

        /// <inheritdoc />
        public IdReservation Reserve(IDataBatch batch, Type entityType, IReadOnlyList<object> rows)
        {
            ArgumentNullException.ThrowIfNull(batch);
            ArgumentNullException.ThrowIfNull(entityType);
            ArgumentNullException.ThrowIfNull(rows);
            DataBatch data = batch as DataBatch
                ?? throw new ArgumentException("The batch is not one a tenant database created, so no reservation statement can ride it.", nameof(batch));
            if (!data.IsComposing)
            {
                throw new InvalidOperationException("The batch already executed, so no reservation statement can ride it. Reserve on a batch that is still composing.");
            }

            // --- The exact deficit per table: every row with no id yet, children included ---
            EntityMetadata root = _metadata.Get(entityType);
            List<PayloadRow> payload = PayloadRows.Enumerate(root, rows);
            List<(EntityMetadata Entity, long Deficit)> deficits = [];
            HashSet<(Type Type, long Id)> temporaries = [];
            foreach (PayloadRow row in payload)
            {
                long id = row.Id;
                if (id > 0)
                {
                    continue;
                }

                if (id < 0 && !temporaries.Add((row.Entity.ClrType, id)))
                {
                    throw new ArgumentException(
                        string.Create(CultureInfo.InvariantCulture, $"Two new {row.Entity.Name} rows of the payload share the temporary id {id}; a temporary id is unique within its payload."),
                        nameof(rows));
                }

                int index = deficits.FindIndex(entry => ReferenceEquals(entry.Entity, row.Entity));
                if (index < 0)
                {
                    deficits.Add((row.Entity, 1));
                }
                else
                {
                    deficits[index] = (row.Entity, deficits[index].Deficit + 1);
                }
            }

            // --- Each table's deficit: from the buffer, else from a statement riding the batch ---
            int tenantId = data.Connection.TenantId;
            ReservationHold hold = new(this, root);
            try
            {
                foreach ((EntityMetadata entity, long deficit) in deficits)
                {
                    SequenceInfo sequence = SequenceOf(entity);
                    SequenceBuffer buffer = BufferOf(tenantId, sequence);
                    List<IdRange> taken = buffer.Take(deficit);
                    long held = taken.Sum(static range => range.Count);
                    if (held > 0)
                    {
                        _metrics.RecordIdsReserved(sequence.Tag, held);
                    }

                    ReservationHold.Need need = hold.Add(buffer, taken, pending: deficit - held);
                    if (need.Pending > 0)
                    {
                        // The range tops the buffer up to the low-water mark, which the take emptied.
                        BatchResult<SequenceRead> read = AppendRange(data, sequence, need.Pending + LowWater);
                        data.OnExecuted(cancellationToken => CompleteAsync(data, hold, need, read, cancellationToken));
                    }
                }
            }
            catch
            {
                // Nothing saw the ids taken so far.
                hold.Dispose();
                throw;
            }

            hold.Seal();
            return new IdReservation(hold);
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<object>> TakeAsync(ITenantDatabase database, Type entityType, int count, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(database);
            ArgumentNullException.ThrowIfNull(entityType);
            ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
            IBatchConnection connection = database as IBatchConnection
                ?? throw new ArgumentException("The database is not one the platform composed.", nameof(database));

            EntityMetadata entity = _metadata.Get(entityType);
            SequenceInfo sequence = SequenceOf(entity);
            SequenceBuffer buffer = BufferOf(connection.TenantId, sequence);

            // --- The buffer first ---
            List<IdRange> ranges = buffer.Take(count);
            long held = ranges.Sum(static range => range.Count);
            if (held > 0)
            {
                _metrics.RecordIdsReserved(sequence.Tag, held);
            }

            // --- The shortfall: one autocommit round trip, which also tops the buffer up ---
            if (held < count)
            {
                try
                {
                    var batch = (DataBatch)database.CreateBatch(BatchPurpose.Maintenance);
                    batch.TransactionMode = TransactionMode.None;
                    BatchResult<SequenceRead> read = AppendRange(batch, sequence, count - held + LowWater);
                    _ = await batch.ExecuteAsync(cancellationToken).ConfigureAwait(false);
                    Settled settled = await SettleAsync(batch, buffer, read.Value, count - held, ranges, cancellationToken).ConfigureAwait(false);

                    // After a heal, the ids taken from the buffer at or below the table's largest id are
                    // discarded: the settled range already carries their replacements.
                    ranges = settled.Floor is long floor ? IdRange.Above(ranges, floor) : ranges;
                    ranges.AddRange(settled.Ids);
                }
                catch
                {
                    // The ids taken from the buffer were never handed out; the buffer's floor discards
                    // any a heal found the table holding.
                    Return(buffer, ranges);
                    throw;
                }
            }

            return [.. ranges.SelectMany(static range => range.Ids()).Select(id => sequence.IsLong ? (object)id : checked((int)id))];
        }

        /// <summary>The ids a tenant's buffer holds for an entity's sequence; zero when the buffer never served it.</summary>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="entityType">The entity.</param>
        /// <returns>The ids held.</returns>
        internal long Buffered(int tenantId, Type entityType)
        {
            SequenceInfo sequence = SequenceOf(_metadata.Get(entityType));
            return _buffers.TryGetValue((tenantId, sequence.Name), out SequenceBuffer? buffer) ? buffer.Count : 0;
        }

        /// <summary>Adds a range the sequence issued to a tenant's buffer, at the back, as a refill does.</summary>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="entityType">The entity whose sequence issued the range.</param>
        /// <param name="first">The range's first id.</param>
        /// <param name="last">The range's last id.</param>
        internal void Replenish(int tenantId, Type entityType, long first, long last)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(last, first);
            _ = BufferOf(tenantId, SequenceOf(_metadata.Get(entityType))).Append(new IdRange(first, last));
        }

        /// <summary>
        ///     Appends a refill statement to a batch about to execute for every table it writes whose
        ///     buffer serves saves and holds less than the low-water mark. Each tops its buffer up to the
        ///     mark once the batch ran.
        /// </summary>
        /// <param name="batch">The batch, after its caller's statements and before its epilogue.</param>
        internal void AppendRefills(DataBatch batch)
        {
            ArgumentNullException.ThrowIfNull(batch);
            if (batch.WrittenTables.Count == 0)
            {
                return;
            }

            int tenantId = batch.Connection.TenantId;
            SequenceMap map = _sequences.Value;

            // In table order, so a batch's text does not depend on the set's enumeration order.
            foreach (TableName table in batch.WrittenTables.OrderBy(static table => table.ToString(), StringComparer.Ordinal).ToArray())
            {
                if (!map.ByTable.TryGetValue(table, out SequenceInfo? sequence)
                    || !_buffers.TryGetValue((tenantId, sequence.Name), out SequenceBuffer? buffer))
                {
                    continue;
                }

                // A buffer whose last refill found the sequence behind refills no more: every refill
                // would find it behind again until a reservation reads a range and heals it.
                long held = buffer.Count;
                if (held >= LowWater || buffer.IsBehind)
                {
                    continue;
                }

                BatchResult<SequenceRead> read = AppendRange(batch, sequence, LowWater - held);
                batch.OnExecuted(_ =>
                {
                    if (read.IsCompleted)
                    {
                        Refilled(buffer, read.Value);
                    }

                    return Task.CompletedTask;
                });
            }
        }

        /// <summary>
        ///     Drops the buffered ranges of the sequences whose table's primary key a duplicate-key error
        ///     named, so the next reservation reads the table afresh and heals when it is behind.
        /// </summary>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="indexName">The constraint or index the error named.</param>
        internal void OnDuplicateKey(int tenantId, string indexName)
        {
            ArgumentNullException.ThrowIfNull(indexName);
            if (!_sequences.Value.ByPrimaryKey.TryGetValue(indexName, out SequenceInfo[]? sequences))
            {
                return;
            }

            foreach (SequenceInfo sequence in sequences)
            {
                if (_buffers.TryGetValue((tenantId, sequence.Name), out SequenceBuffer? buffer))
                {
                    IdAllocatorLog.BufferDropped(_logger, sequence.Tag, tenantId, buffer.Clear());
                }
            }
        }

        /// <summary>The reservation that assigned a row its id, while no batch that inserted it committed.</summary>
        /// <param name="row">The row.</param>
        /// <returns>The reservation's hold, or null for a row whose id the allocator did not assign, or whose insert committed.</returns>
        internal ReservationHold? HoldOf(object row)
        {
            ArgumentNullException.ThrowIfNull(row);
            return _assigned.TryGetValue(row, out ReservationHold? hold) ? hold : null;
        }

        /// <summary>Records that a reservation assigned a row its id.</summary>
        /// <param name="row">The row.</param>
        /// <param name="hold">The reservation's hold.</param>
        internal void Register(object row, ReservationHold hold)
        {
            _assigned.AddOrUpdate(row, hold);
        }

        /// <summary>Forgets rows whose insert committed: they are existing rows from now on.</summary>
        /// <param name="rows">The rows.</param>
        internal void Committed(IEnumerable<object> rows)
        {
            ArgumentNullException.ThrowIfNull(rows);
            foreach (object row in rows)
            {
                _ = _assigned.Remove(row);
            }
        }

        /// <summary>
        ///     Puts ranges a reservation or a take held back at the front of the buffer they came from,
        ///     and meters them. Ids at or below the buffer's floor are discarded and logged.
        /// </summary>
        /// <param name="buffer">The buffer the ranges were taken from.</param>
        /// <param name="ranges">The ranges.</param>
        internal void Return(SequenceBuffer buffer, IReadOnlyList<IdRange> ranges)
        {
            long count = ranges.Sum(static range => range.Count);
            if (count == 0)
            {
                return;
            }

            long discarded = buffer.Return(ranges);
            _metrics.RecordIdsReturned(buffer.Sequence.Tag, count - discarded);
            if (discarded > 0)
            {
                IdAllocatorLog.StaleDiscarded(_logger, buffer.Sequence.Tag, buffer.TenantId, discarded);
            }
        }

        /// <summary>
        ///     Forgets every buffer of a tenant: the database behind the tenant may change (a relocation,
        ///     a point-in-time restore), and a buffered id says nothing about the next database. Ids
        ///     still held by reservations return to the forgotten buffers, never to the new ones.
        /// </summary>
        /// <param name="tenantId">The tenant.</param>
        internal void ForgetTenant(int tenantId)
        {
            long dropped = 0;
            foreach ((int TenantId, TableName Sequence) key in _buffers.Keys)
            {
                if (key.TenantId == tenantId && _buffers.TryRemove(key, out SequenceBuffer? buffer))
                {
                    dropped += buffer.Clear();
                }
            }

            IdAllocatorLog.TenantForgotten(_logger, tenantId, dropped);
        }

        /// <summary>Drops a tenant's buffered ids of a sequence at or below a table's largest id.</summary>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="sequence">The sequence.</param>
        /// <param name="floor">The table's largest id.</param>
        internal void DropAtOrBelow(int tenantId, SequenceInfo sequence, long floor)
        {
            _ = BufferOf(tenantId, sequence).DropAtOrBelow(floor);
        }

        /// <summary>The sequence that serves an entity's table.</summary>
        /// <param name="entity">The entity.</param>
        /// <returns>The sequence.</returns>
        /// <exception cref="InvalidOperationException">The table has none.</exception>
        internal SequenceInfo SequenceOf(EntityMetadata entity)
        {
            return _sequences.Value.ByType.TryGetValue(entity.ClrType, out SequenceInfo? sequence)
                ? sequence
                : throw new InvalidOperationException(
                    $"The table of {entity.Name}, {entity.Table}, has no sequence, so the allocator cannot give its rows ids. A sibling table takes each row's id from the row it extends.");
        }

        /// <summary>A tenant's buffer of a sequence, created on first use.</summary>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="sequence">The sequence.</param>
        /// <returns>The buffer.</returns>
        private SequenceBuffer BufferOf(int tenantId, SequenceInfo sequence)
        {
            return _buffers.GetOrAdd((tenantId, sequence.Name), static (key, sequence) => new SequenceBuffer(key.TenantId, sequence), sequence);
        }

        /// <summary>
        ///     Appends a statement that reserves a range of a sequence and reads it back with the
        ///     table's largest id, which tells a sequence that fell behind its table.
        /// </summary>
        /// <param name="batch">The batch.</param>
        /// <param name="sequence">The sequence.</param>
        /// <param name="size">How many ids to reserve.</param>
        /// <returns>The range read, once the batch executed and the statement ran.</returns>
        private static BatchResult<SequenceRead> AppendRange(DataBatch batch, SequenceInfo sequence, long size)
        {
            BatchResult<SequenceRead> result = new();
            StatementDraft draft = batch.Begin();
            try
            {
                // --- The text: sp_sequence_get_range needs UPDATE on the sequence only, and issues outside any transaction ---
                string first = draft.Name(ReservedSqlNames.StatementNames.First);
                string last = draft.Name(ReservedSqlNames.StatementNames.Last);
                string rangeSize = draft.Scalar(size);
                string keyType = sequence.IsLong ? "bigint" : "int";
                string name = sequence.Name.Delimited.Replace("'", "''", StringComparison.Ordinal);
                string text = string.Create(
                    CultureInfo.InvariantCulture,
                    $"""
                    DECLARE {first} sql_variant, {last} sql_variant;
                    EXEC sys.sp_sequence_get_range @sequence_name = N'{name}', @range_size = {rangeSize}, @range_first_value = {first} OUTPUT, @range_last_value = {last} OUTPUT;
                    SELECT CAST({first} AS {keyType}) AS [First], CAST({last} AS {keyType}) AS [Last], (SELECT MAX([Id]) FROM {sequence.Table.Delimited}) AS [MaxId];
                    """);

                // --- The reader: one row, completed only for the attempt that counts ---
                int ordinal = draft.Ordinal;
                batch.Append(draft, text, reader: async (cursor, cancellationToken) =>
                {
                    DbDataReader set = await cursor.NextAsync(ordinal, cancellationToken).ConfigureAwait(false);
                    if (!await set.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture, $"The id reservation at statement {ordinal} returned no row."));
                    }

                    SequenceRead read = new(
                        Convert.ToInt64(set.GetValue(0), CultureInfo.InvariantCulture),
                        Convert.ToInt64(set.GetValue(1), CultureInfo.InvariantCulture),
                        set.IsDBNull(2) ? null : Convert.ToInt64(set.GetValue(2), CultureInfo.InvariantCulture));
                    cursor.CountRows(1);
                    cursor.Complete(result, read);
                });
                return result;
            }
            catch
            {
                batch.Discard(draft);
                throw;
            }
        }

        /// <summary>
        ///     Completes a reservation statement once its batch executed: the range is settled, healed
        ///     when the sequence is behind, and handed to the hold. A failure is kept for
        ///     <see cref="IdReservation.Assign" /> to report, never thrown, since the batch may have committed.
        /// </summary>
        /// <param name="origin">The batch the statement rode.</param>
        /// <param name="hold">The reservation.</param>
        /// <param name="need">The table's need the statement serves.</param>
        /// <param name="read">The statement's result.</param>
        /// <param name="cancellationToken">Cancels a healing round trip.</param>
        /// <returns>A task that completes when the need is filled or failed.</returns>
        private async Task CompleteAsync(DataBatch origin, ReservationHold hold, ReservationHold.Need need, BatchResult<SequenceRead> read, CancellationToken cancellationToken)
        {
            // A statement that did not run (the guard failed) leaves the need unfilled.
            if (!read.IsCompleted)
            {
                return;
            }

            try
            {
                Settled settled = await SettleAsync(origin, need.Buffer, read.Value, need.Pending, hold.HeldOf(need), cancellationToken).ConfigureAwait(false);
                hold.Fill(need, settled.Ids, settled.Floor);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                hold.Fail(need, exception);
            }
        }

        /// <summary>
        ///     Settles a range a reservation read: when the sequence is behind its table the range is
        ///     discarded and one more round trip consumes the gap, and every id at or below the table's
        ///     largest id is discarded too, from the buffer and from what the caller holds; the ids
        ///     wanted are kept for the caller and the rest of the range tops the buffer up.
        /// </summary>
        /// <param name="origin">The batch the range was read on, whose scope a healing round trip runs in.</param>
        /// <param name="buffer">The buffer of the sequence the caller took from, which the tail tops up.</param>
        /// <param name="read">The range read.</param>
        /// <param name="wanted">How many ids the caller wants.</param>
        /// <param name="held">The ids of the sequence the caller already holds, taken from the buffer.</param>
        /// <param name="cancellationToken">Cancels a healing round trip.</param>
        /// <returns>
        ///     The caller's new ids and, after a heal, the floor at or below which the caller discards
        ///     the ids it held: the new ids then replace those as well.
        /// </returns>
        /// <exception cref="InvalidOperationException">The sequence is still behind after the heal.</exception>
        private async Task<Settled> SettleAsync(DataBatch origin, SequenceBuffer buffer, SequenceRead read, long wanted, IReadOnlyList<IdRange> held, CancellationToken cancellationToken)
        {
            SequenceInfo sequence = buffer.Sequence;
            _metrics.RecordIdRefill(sequence.Tag);
            int tenantId = origin.Connection.TenantId;
            IdRange range = new(read.First, read.Last);
            long? floor = null;

            // --- The heal: an out-of-band insert left ids above the range's start ---
            if (read.MaxId is long maxId && read.First <= maxId)
            {
                // The floor rises before the round trip, so ids at or below the table never return to
                // the buffer, even when the heal fails; the caller's held ones are replaced.
                long dropped = buffer.DropAtOrBelow(maxId);
                wanted += IdRange.CountAtOrBelow(held, maxId);
                long consumed = maxId - read.First + 1 + wanted + LowWater;
                DataBatch heal = origin.CreateSibling(BatchPurpose.Maintenance);
                heal.TransactionMode = TransactionMode.None;
                BatchResult<SequenceRead> healed = AppendRange(heal, sequence, consumed);
                _ = await heal.ExecuteAsync(cancellationToken).ConfigureAwait(false);

                SequenceRead again = healed.Value;
                long start = again.MaxId is long newMax ? Math.Max(again.First, newMax + 1) : again.First;

                // The second read may have found the table further ahead still.
                floor = Math.Max(maxId, again.MaxId ?? maxId);
                dropped += buffer.DropAtOrBelow(floor.Value);
                IdAllocatorLog.Healed(_logger, sequence.Tag, tenantId, read.First, maxId, consumed, dropped);
                _metrics.RecordIdHealed(sequence.Tag);
                if (again.Last - start + 1 < wanted)
                {
                    throw new InvalidOperationException(string.Create(
                        CultureInfo.InvariantCulture,
                        $"The sequence {sequence.Tag} is still behind its table after consuming {consumed} ids: rows keep arriving out of band. Find what inserts ids the sequence did not issue."));
                }

                range = new IdRange(start, again.Last);
            }

            // --- The caller's head of the range; the tail tops the buffer up ---
            // A reservation read a range above the table: the buffer may refill again.
            IdRange head = new(range.First, range.First + wanted - 1);
            if (range.Last > head.Last)
            {
                _ = buffer.Append(new IdRange(head.Last + 1, range.Last));
            }

            buffer.ClearBehind();

            _metrics.RecordIdsReserved(sequence.Tag, wanted);
            return new Settled([head], floor);
        }

        /// <summary>
        ///     Adds what a refill read to its buffer: the part above the table's largest id. A range
        ///     that starts at or below it found the sequence behind its table: the buffered ids at or
        ///     below it are dropped, since any of them may collide, and the buffer is marked behind,
        ///     which stops its refills until a reservation heals; the condition is reported once.
        /// </summary>
        /// <param name="buffer">The buffer.</param>
        /// <param name="read">The range read.</param>
        private void Refilled(SequenceBuffer buffer, SequenceRead read)
        {
            SequenceInfo sequence = buffer.Sequence;
            _metrics.RecordIdRefill(sequence.Tag);
            if (read.MaxId is long maxId && read.First <= maxId)
            {
                // No heal here: the refill keeps what lies above the table, and the next reservation
                // that reads a range below it heals the sequence.
                long dropped = buffer.DropAtOrBelow(maxId);
                if (buffer.MarkBehind())
                {
                    IdAllocatorLog.BehindOnRefill(_logger, sequence.Tag, buffer.TenantId, read.First, maxId, dropped);
                    _metrics.RecordIdHealed(sequence.Tag);
                }
            }

            // The buffer's floor keeps only what lies above the table.
            _ = buffer.Append(new IdRange(read.First, read.Last));
        }

        /// <summary>Maps every table the model gives a sequence: by entity, by table and by primary-key name.</summary>
        /// <returns>The map.</returns>
        private SequenceMap BuildSequences()
        {
            List<SequenceInfo> sequences = [];
            foreach (EntityMetadata entity in _metadata.All)
            {
                // A sibling has no sequence: it takes its key from the row it extends.
                string name = ConstraintNames.Sequence(entity.Table.Name);
                if (_model.FindSequence(name, entity.Table.Schema) is not null)
                {
                    sequences.Add(new SequenceInfo(entity, new TableName(entity.Table.Schema, name), ConstraintNames.PrimaryKey(entity.Table.Name)));
                }
            }

            return new SequenceMap(
                sequences.ToFrozenDictionary(static sequence => sequence.Entity.ClrType),
                sequences.ToFrozenDictionary(static sequence => sequence.Table),
                sequences.GroupBy(static sequence => sequence.PrimaryKey, StringComparer.OrdinalIgnoreCase)
                    .ToFrozenDictionary(static group => group.Key, static group => group.ToArray(), StringComparer.OrdinalIgnoreCase));
        }

        /// <summary>The model's sequences, by entity, by table and by primary-key name.</summary>
        /// <param name="ByType">By entity leaf.</param>
        /// <param name="ByTable">By table.</param>
        /// <param name="ByPrimaryKey">By the table's primary-key name; two schemas may share one.</param>
        private sealed record SequenceMap(
            FrozenDictionary<Type, SequenceInfo> ByType,
            FrozenDictionary<TableName, SequenceInfo> ByTable,
            FrozenDictionary<string, SequenceInfo[]> ByPrimaryKey);
    }

    /// <summary>The ids a settled range gives its caller.</summary>
    /// <param name="Ids">The caller's new ids.</param>
    /// <param name="Floor">
    ///     After a heal, the table's largest id: the caller discards the ids it held at or below it,
    ///     which <paramref name="Ids" /> replace. Null when the sequence was not behind.
    /// </param>
    internal sealed record Settled(List<IdRange> Ids, long? Floor);

    /// <summary>A range a reservation statement read back.</summary>
    /// <param name="First">The range's first id.</param>
    /// <param name="Last">The range's last id.</param>
    /// <param name="MaxId">The table's largest id when the range was issued; null for an empty table.</param>
    internal sealed record SequenceRead(long First, long Last, long? MaxId);
}
