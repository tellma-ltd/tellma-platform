// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Data;
using Tellma.Core.Data.Batches;
using Tellma.Core.Data.Ids;
using Tellma.Core.Tests.Data.Batches;
using Tellma.Core.Tests.Data.Saves;

namespace Tellma.Core.Tests.Data.Ids
{
    /// <summary>
    ///     The id allocator with no database: the exact deficit of a payload, children included; the
    ///     buffer it is served from and the reservation statement for the rest; the ids a reservation
    ///     gives back before a persist and keeps after one; the temporary ids it rewrites; the refills
    ///     that ride a batch; and the buffer a duplicate primary key drops.
    /// </summary>
    public sealed class IdAllocatorTests : IDisposable
    {
        /// <summary>The fixture tenant the harness's connection reports.</summary>
        private const int Tenant = 1;

        /// <summary>The composition over the save model.</summary>
        private readonly BatchHarness _harness = new(compose: SaveModel.Compose);

        private IdAllocator Allocator => _harness.Executor.Allocator;

        [Fact]
        public void A_warm_buffer_serves_the_exact_deficit_of_every_table_and_appends_nothing()
        {
            Allocator.Replenish(Tenant, typeof(Crate), 100, 109);
            Allocator.Replenish(Tenant, typeof(Compartment), 300, 309);
            Allocator.Replenish(Tenant, typeof(Item), 500, 509);
            Crate[] crates = [Packed(compartments: 2, items: 1), Packed(compartments: 1, items: 2), new Crate { Id = 42, Name = "existing" }];
            DataBatch batch = _harness.Batch(BatchPurpose.Validate);

            using IdReservation reservation = Allocator.Reserve(batch, typeof(Crate), crates);

            // Two new crates, three compartments, four items: the existing crate counts for nothing.
            Assert.True(reservation.IsImmediate);
            Assert.Empty(batch.Statements);
            Assert.Equal(8, Allocator.Buffered(Tenant, typeof(Crate)));
            Assert.Equal(7, Allocator.Buffered(Tenant, typeof(Compartment)));
            Assert.Equal(6, Allocator.Buffered(Tenant, typeof(Item)));
        }

        [Fact]
        public void A_cold_reservation_is_one_statement_per_short_table_sized_to_the_shortfall_and_the_low_water_mark()
        {
            Allocator.Replenish(Tenant, typeof(Crate), 100, 101);
            DataBatch batch = _harness.Batch(BatchPurpose.Validate);

            using IdReservation reservation = Allocator.Reserve(batch, typeof(Crate), [Packed(compartments: 0, items: 0), Packed(compartments: 0, items: 0), Packed(compartments: 0, items: 0)]);

            // Two from the buffer, one from the statement, which tops the buffer up to 64 as well.
            Assert.False(reservation.IsImmediate);
            BatchStatement statement = Assert.Single(batch.Statements);
            Assert.Equal(
                """
                DECLARE @tb0_first sql_variant, @tb0_last sql_variant;
                EXEC sys.sp_sequence_get_range @sequence_name = N'[sv].[sq_Crates]', @range_size = @tb0_p0, @range_first_value = @tb0_first OUTPUT, @range_last_value = @tb0_last OUTPUT;
                SELECT CAST(@tb0_first AS int) AS [First], CAST(@tb0_last AS int) AS [Last], (SELECT MAX([Id]) FROM [sv].[Crates]) AS [MaxId];
                """,
                statement.Text,
                ignoreLineEndingDifferences: true);
            Assert.Equal(65L, ((ScalarParameter)Assert.Single(statement.Parameters)).Value);
            Assert.Empty(statement.Writes);
            Assert.Equal(0, Allocator.Buffered(Tenant, typeof(Crate)));

            // The ids are not in hand until the batch ran.
            InvalidOperationException early = Assert.Throws<InvalidOperationException>(() => reservation.Assign([]));
            Assert.Contains("not reserved yet", early.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void A_long_sequence_reads_its_range_as_bigint()
        {
            DataBatch batch = _harness.Batch(BatchPurpose.Validate);

            using IdReservation reservation = Allocator.Reserve(batch, typeof(Ledger), [new Ledger()]);

            Assert.Contains("CAST(@tb0_first AS bigint) AS [First]", Assert.Single(batch.Statements).Text, StringComparison.Ordinal);
        }

        [Fact]
        public void A_reservation_disposed_before_any_persist_returns_its_ids_to_the_front_of_the_buffer()
        {
            Allocator.Replenish(Tenant, typeof(Crate), 100, 104);
            Allocator.Replenish(Tenant, typeof(Crate), 200, 204);
            Crate[] first = [new Crate(), new Crate(), new Crate(), new Crate(), new Crate(), new Crate()];
            IdReservation reservation = Allocator.Reserve(_harness.Batch(BatchPurpose.Validate), typeof(Crate), first);
            reservation.Assign(first);
            Assert.Equal([100, 101, 102, 103, 104, 200], first.Select(static crate => crate.Id));
            Assert.Equal(4, Allocator.Buffered(Tenant, typeof(Crate)));

            // A validation failure: the ids go back, in order, ahead of the rest.
            reservation.Dispose();
            reservation.Dispose();
            Assert.Equal(10, Allocator.Buffered(Tenant, typeof(Crate)));

            Crate[] again = [new Crate(), new Crate(), new Crate(), new Crate(), new Crate(), new Crate(), new Crate()];
            using IdReservation next = Allocator.Reserve(_harness.Batch(BatchPurpose.Validate), typeof(Crate), again);
            next.Assign(again);
            Assert.Equal([100, 101, 102, 103, 104, 200, 201], again.Select(static crate => crate.Id));
        }

        [Fact]
        public async Task A_reservation_whose_rows_a_persist_attempted_to_save_returns_nothing()
        {
            Allocator.Replenish(Tenant, typeof(Crate), 100, 109);
            Crate crate = new() { Name = "new" };
            IdReservation reservation = Allocator.Reserve(_harness.Batch(BatchPurpose.Validate), typeof(Crate), [crate]);
            reservation.Assign([crate]);
            DataBatch persist = _harness.Batch(BatchPurpose.Persist);
            _ = persist.Save([crate]);

            // The send fails, but it may have reached the server: the id may be in the table.
            _ = await BatchHarness.SealAsync(persist);
            reservation.Dispose();

            Assert.Equal(9, Allocator.Buffered(Tenant, typeof(Crate)));
            Assert.False(Allocator.HoldOf(crate)!.IsReturned);
        }

        [Fact]
        public void Temporary_ids_are_rewritten_in_a_self_reference_a_child_parent_key_and_a_grandchild_parent_key()
        {
            Allocator.Replenish(Tenant, typeof(Crate), 100, 109);
            Allocator.Replenish(Tenant, typeof(Compartment), 300, 309);
            Allocator.Replenish(Tenant, typeof(Item), 500, 509);
            Item item = new() { Id = -1, CompartmentId = -1, Label = "x" };
            Compartment compartment = new() { Id = -1, CrateId = -1, Items = [item] };
            Crate outer = new() { Id = -1, Name = "outer", Compartments = [compartment] };
            Crate inner = new() { Id = -2, Name = "inner", OuterId = -1 };
            Crate stray = new() { Name = "stray", OuterId = -9 };
            Crate existing = new() { Id = 42, Name = "existing", OuterId = -2 };
            Crate[] rows = [inner, outer, stray, existing];

            using IdReservation reservation = Allocator.Reserve(_harness.Batch(BatchPurpose.Validate), typeof(Crate), rows);
            reservation.Assign(rows);

            // Ids in payload order; references to a temporary id follow it, whichever row defines it.
            Assert.Equal((100, 101, 102), (inner.Id, outer.Id, stray.Id));
            Assert.Equal(101, inner.OuterId);
            Assert.Equal(100, existing.OuterId);
            Assert.Equal((300, 101), (compartment.Id, compartment.CrateId));
            Assert.Equal((500, 300), (item.Id, item.CompartmentId));

            // A temporary id no row defines is left for the pipeline to report.
            Assert.Equal(-9, stray.OuterId);
        }

        [Fact]
        public void Two_new_rows_of_one_table_sharing_a_temporary_id_are_refused_and_nothing_is_taken()
        {
            Allocator.Replenish(Tenant, typeof(Crate), 100, 109);

            _ = Assert.Throws<ArgumentException>(
                () => Allocator.Reserve(_harness.Batch(BatchPurpose.Validate), typeof(Crate), [new Crate { Id = -1 }, new Crate { Id = -1 }]));

            Assert.Equal(10, Allocator.Buffered(Tenant, typeof(Crate)));
        }

        [Fact]
        public void A_payload_changed_between_reserve_and_assign_is_refused()
        {
            Allocator.Replenish(Tenant, typeof(Crate), 100, 109);
            Crate[] rows = [new Crate(), new Crate()];
            using IdReservation reservation = Allocator.Reserve(_harness.Batch(BatchPurpose.Validate), typeof(Crate), rows);

            _ = Assert.Throws<InvalidOperationException>(() => reservation.Assign([rows[0]]));
            _ = Assert.Throws<InvalidOperationException>(() => reservation.Assign([.. rows, new Crate()]));

            reservation.Assign(rows);
            _ = Assert.Throws<InvalidOperationException>(() => reservation.Assign(rows));
        }

        [Fact]
        public void A_sibling_has_no_sequence()
        {
            InvalidOperationException refusal = Assert.Throws<InvalidOperationException>(
                () => Allocator.Reserve(_harness.Batch(BatchPurpose.Validate), typeof(CrateExtra), [new CrateExtra()]));

            Assert.Contains("no sequence", refusal.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task A_batch_writing_a_buffered_table_below_the_low_water_mark_carries_its_refill()
        {
            Allocator.Replenish(Tenant, typeof(Crate), 100, 109);
            Allocator.Replenish(Tenant, typeof(Compartment), 300, 399);
            Allocator.Replenish(Tenant, typeof(Item), 500, 563);
            DataBatch batch = _harness.Batch(BatchPurpose.Persist);
            _ = batch.Save([new Crate { Id = 42, Name = "existing" }]);

            _ = await BatchHarness.SealAsync(batch);

            // The crates' buffer holds 10 of 64; the compartments' and items' are at the mark; the ledgers' was never used.
            Assert.Equal(2, batch.Statements.Count);
            BatchStatement refill = batch.Statements[1];
            Assert.Contains("@sequence_name = N'[sv].[sq_Crates]'", refill.Text, StringComparison.Ordinal);
            Assert.Equal(54L, ((ScalarParameter)Assert.Single(refill.Parameters)).Value);
            Assert.Single(batch.ExecutedSteps.Skip(1));
        }

        [Fact]
        public async Task A_batch_writing_a_table_whose_buffer_was_never_used_carries_no_refill()
        {
            DataBatch batch = _harness.Batch(BatchPurpose.Persist);
            _ = batch.Save([new Crate { Id = 42, Name = "existing" }]);

            _ = await BatchHarness.SealAsync(batch);

            _ = Assert.Single(batch.Statements);
        }

        [Fact]
        public void A_duplicate_primary_key_drops_that_sequences_buffer_and_no_other()
        {
            Allocator.Replenish(Tenant, typeof(Crate), 100, 109);
            Allocator.Replenish(Tenant, typeof(Compartment), 300, 309);

            Allocator.OnDuplicateKey(Tenant, "IX_Crates_Name");
            Assert.Equal(10, Allocator.Buffered(Tenant, typeof(Crate)));

            Allocator.OnDuplicateKey(Tenant + 1, "PK_Crates");
            Assert.Equal(10, Allocator.Buffered(Tenant, typeof(Crate)));

            Allocator.OnDuplicateKey(Tenant, "PK_Crates");
            Assert.Equal(0, Allocator.Buffered(Tenant, typeof(Crate)));
            Assert.Equal(10, Allocator.Buffered(Tenant, typeof(Compartment)));
        }

        public void Dispose()
        {
            _harness.Dispose();
        }

        /// <summary>A new crate with new compartments, each with new items.</summary>
        /// <param name="compartments">The compartments.</param>
        /// <param name="items">The items of each compartment.</param>
        /// <returns>The crate.</returns>
        private static Crate Packed(int compartments, int items)
        {
            return new Crate
            {
                Name = "packed",
                Compartments = [.. Enumerable.Range(0, compartments).Select(_ => new Compartment { Items = [.. Enumerable.Range(0, items).Select(static _ => new Item { Label = "item" })] })],
            };
        }
    }
}
