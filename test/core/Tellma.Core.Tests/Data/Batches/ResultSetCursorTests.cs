// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Data;
using System.Data.Common;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Data.Batches;

namespace Tellma.Core.Tests.Data.Batches
{
    /// <summary>
    ///     The cursor over a round trip's result sets: a statement that reads a set only when it is
    ///     its own leaves another statement's set in place, and results complete only when the
    ///     executor applies them.
    /// </summary>
    public sealed class ResultSetCursorTests
    {
        private static CancellationToken Token => TestContext.Current.CancellationToken;

        [Fact]
        public async Task A_set_that_is_not_the_statements_own_is_left_for_the_next_statement()
        {
            using DataTable first = Table("Value", 1);
            using DataTable second = Table("Other", 2);
            using DataTableReader reader = new([first, second]);
            ResultSetCursor cursor = new(reader);

            // --- The conditional statement finds the next set not its own and reads nothing ---
            Assert.Null(await cursor.NextIfAsync(static set => set.GetName(0) == "Reason", Token));
            Assert.Equal(0, cursor.SetsRead);

            // --- The owner then reads that same set, and the next one moves on ---
            DbDataReader own = await cursor.NextAsync(0, Token);
            Assert.Equal("Value", own.GetName(0));
            Assert.NotNull(await cursor.NextIfAsync(static set => set.GetName(0) == "Other", Token));
            Assert.Equal(2, cursor.SetsRead);

            // --- With no set left, a conditional read finds none and the round trip ends cleanly ---
            Assert.Null(await cursor.NextIfAsync(static _ => true, Token));
            await cursor.EndAsync(Token);
        }

        [Fact]
        public async Task A_pending_set_counts_as_left_over_when_the_round_trip_ends()
        {
            using DataTable only = Table("Value", 1);
            using DataTableReader reader = new(only);
            ResultSetCursor cursor = new(reader);

            Assert.Null(await cursor.NextIfAsync(static _ => false, Token));
            _ = await Assert.ThrowsAsync<InvalidOperationException>(async () => await cursor.EndAsync(Token));
        }

        [Fact]
        public void Staged_results_complete_only_when_applied_and_only_once()
        {
            using DataTable empty = new();
            using DataTableReader reader = new(empty);
            ResultSetCursor cursor = new(reader);
            using BatchHarness harness = new();
            DataBatch batch = harness.Batch();
            BatchResult<RawResult> first = batch.Sql($"SELECT 1 AS [One];", new SqlOptions(resultSets: 1));
            BatchResult<RawResult> second = batch.Sql($"SELECT 2 AS [Two];", new SqlOptions(resultSets: 1));
            RawResult firstValue = new([]);
            RawResult secondValue = new([]);

            // --- Staged values stay invisible until the executor applies them ---
            cursor.Complete(first, firstValue);
            cursor.Complete(second, secondValue);
            Assert.False(first.IsCompleted);
            Assert.False(second.IsCompleted);

            cursor.ApplyCompletions();
            Assert.Same(firstValue, first.Value);
            Assert.Same(secondValue, second.Value);

            // --- Applying again completes nothing twice: the staged list was cleared ---
            cursor.ApplyCompletions();
        }

        /// <summary>A one-column, one-row table.</summary>
        /// <param name="column">The column's name.</param>
        /// <param name="value">Its value.</param>
        /// <returns>The table.</returns>
        private static DataTable Table(string column, int value)
        {
            DataTable table = new();
            _ = table.Columns.Add(column, typeof(int));
            _ = table.Rows.Add(value);
            return table;
        }
    }
}
