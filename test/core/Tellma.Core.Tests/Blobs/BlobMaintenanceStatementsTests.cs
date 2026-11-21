// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Data;
using Tellma.Core.Abstractions.Blobs;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Blobs;
using Tellma.Core.Data.Batches;
using Tellma.Core.Tests.Data.Batches;

namespace Tellma.Core.Tests.Blobs
{
    /// <summary>
    ///     The sweep's claim and row delete, the reconcile and the verification's segment read as
    ///     they compose: their text, parameters, writes and idempotence, and what their readers make
    ///     of a result set.
    /// </summary>
    public sealed class BlobMaintenanceStatementsTests : IDisposable
    {
        /// <summary>The batches, over the default composition with a connection that refuses.</summary>
        private readonly BatchHarness _harness = new();

        /// <inheritdoc />
        public void Dispose()
        {
            _harness.Dispose();
        }

        // --- The sweep's claim ---

        [Fact]
        public void The_claim_takes_the_earliest_due_rows_on_the_database_clock_and_returns_their_previous_state()
        {
            DataBatch batch = _harness.Batch(BatchPurpose.Maintenance);
            _ = BlobStatements.Claim(batch, 500, TimeSpan.FromMinutes(60));

            BatchStatement statement = Assert.Single(batch.Statements);
            Assert.Equal(
                """
                DECLARE @tb0_now datetimeoffset(3) = SYSUTCDATETIME();
                WITH due AS (
                    SELECT TOP (@tb0_p0) [Id], [Kind], [StorageKey], [Variants], [State], [ExpiresAt]
                    FROM [core].[Blobs] WITH (ROWLOCK)
                    WHERE [State] IN ('Staged', 'Released', 'Deleting') AND [ExpiresAt] < @tb0_now
                    ORDER BY [ExpiresAt])
                UPDATE due SET [State] = 'Deleting', [ExpiresAt] = DATEADD(second, @tb0_p1, @tb0_now)
                OUTPUT inserted.[Id], inserted.[Kind], inserted.[StorageKey], inserted.[Variants], deleted.[State];
                """.ReplaceLineEndings("\n"),
                statement.Text);
            Assert.Equal(
                ["@tb0_p0:Int:500", "@tb0_p1:Int:3600"],
                statement.Parameters.Cast<ScalarParameter>().Select(static parameter => $"{parameter.Name}:{parameter.Type}:{parameter.Value}"));
            Assert.Equal([BlobStatements.Blobs], statement.Writes);
            Assert.False(statement.Idempotent);
        }

        [Fact]
        public async Task The_claims_reader_returns_each_row_with_the_state_it_left()
        {
            DataBatch batch = _harness.Batch(BatchPurpose.Maintenance);
            BatchResult<IReadOnlyList<ClaimedBlob>> result = BlobStatements.Claim(batch, 500, TimeSpan.FromMinutes(60));

            DataTable set = Table(("Id", typeof(int)), ("Kind", typeof(string)), ("StorageKey", typeof(string)), ("Variants", typeof(string)), ("State", typeof(string)));
            _ = set.Rows.Add(7, "bm-scan", "bm-scan/ab/ab0123456789abcdef0123456789abcd", "thumb", "Released");
            _ = set.Rows.Add(9, "bm-scan", "bm-scan/cd/cd0123456789abcdef0123456789abcd", "", "Deleting");
            await BlobStatementsTests.ReplayAsync(batch, set);

            Assert.Equal(
                [
                    new ClaimedBlob(7, "bm-scan", "bm-scan/ab/ab0123456789abcdef0123456789abcd", "thumb", BlobState.Released),
                    new ClaimedBlob(9, "bm-scan", "bm-scan/cd/cd0123456789abcdef0123456789abcd", "", BlobState.Deleting),
                ],
                result.Value);
        }

        [Fact]
        public void The_claim_refuses_an_empty_batch_and_a_reclaim_span_no_second_count_carries()
        {
            DataBatch batch = _harness.Batch(BatchPurpose.Maintenance);

            _ = Assert.Throws<ArgumentOutOfRangeException>(() => BlobStatements.Claim(batch, 0, TimeSpan.FromMinutes(60)));
            _ = Assert.Throws<OverflowException>(() => BlobStatements.Claim(batch, 500, TimeSpan.FromDays(30_000)));
            Assert.Empty(batch.Statements);
        }

        // --- The sweep's row delete ---

        [Fact]
        public void The_row_delete_removes_only_deleting_rows_of_its_list_and_returns_their_ids()
        {
            DataBatch batch = _harness.Batch(BatchPurpose.Maintenance);
            _ = BlobStatements.DeleteSwept(batch, [7, 9, 7]);

            BatchStatement statement = Assert.Single(batch.Statements);
            Assert.Equal(
                """
                DELETE b
                OUTPUT deleted.[Id]
                FROM [core].[Blobs] AS b INNER JOIN @tb0_t0 AS i ON i.[Id] = b.[Id] WHERE b.[State] = 'Deleting';
                """.ReplaceLineEndings("\n"),
                statement.Text);
            TableParameter ids = Assert.IsType<TableParameter>(Assert.Single(statement.Parameters));
            Assert.Equal(("@tb0_t0", 2), (ids.Name, ids.Rows));
            Assert.Equal([BlobStatements.Blobs], statement.Writes);
            Assert.True(statement.Idempotent);
        }

        [Fact]
        public async Task The_row_deletes_reader_returns_the_ids_deleted()
        {
            DataBatch batch = _harness.Batch(BatchPurpose.Maintenance);
            BatchResult<IReadOnlyList<int>> result = BlobStatements.DeleteSwept(batch, [7, 9]);

            DataTable set = Table(("Id", typeof(int)));
            _ = set.Rows.Add(9);
            await BlobStatementsTests.ReplayAsync(batch, set);

            Assert.Equal([9], result.Value);
        }

        [Fact]
        public void The_row_delete_refuses_an_empty_list()
        {
            DataBatch batch = _harness.Batch(BatchPurpose.Maintenance);

            _ = Assert.Throws<ArgumentException>(() => BlobStatements.DeleteSwept(batch, []));
            Assert.Empty(batch.Statements);
        }

        // --- The verification's segment read ---

        [Fact]
        public void The_segment_read_matches_the_kinds_rows_under_the_segment_and_writes_nothing()
        {
            DataBatch batch = _harness.Batch(BatchPurpose.Maintenance);
            _ = BlobStatements.Segment(batch, "bm-scan", "bm-scan/0f");

            BatchStatement statement = Assert.Single(batch.Statements);
            Assert.Equal(
                """
                SELECT [Id], [StorageKey], [Variants], [State], [CreatedAt]
                FROM [core].[Blobs]
                WHERE [Kind] = @tb0_p0 AND [StorageKey] LIKE @tb0_p1;
                """.ReplaceLineEndings("\n"),
                statement.Text);
            Assert.Equal(
                ["@tb0_p0:NVarChar:bm-scan:40", "@tb0_p1:NVarChar:bm-scan/0f/%:120"],
                statement.Parameters.Cast<ScalarParameter>().Select(static parameter => $"{parameter.Name}:{parameter.Type}:{parameter.Value}:{parameter.Size}"));
            Assert.Empty(statement.Writes);
            Assert.True(statement.Idempotent);
        }

        [Theory]
        [InlineData("bm-scan")]
        [InlineData("other/0f")]
        [InlineData("bm-scan/0F")]
        [InlineData("bm-scan/0f/")]
        public void The_segment_read_refuses_anything_but_a_fan_out_segment_of_its_kind(string segment)
        {
            DataBatch batch = _harness.Batch(BatchPurpose.Maintenance);

            _ = Assert.Throws<ArgumentException>(() => BlobStatements.Segment(batch, "bm-scan", segment));
            Assert.Empty(batch.Statements);
        }

        [Fact]
        public async Task The_segment_reads_reader_returns_every_row_with_its_state_and_staging_instant()
        {
            DateTimeOffset created = new(2026, 11, 19, 9, 0, 0, TimeSpan.Zero);
            DataBatch batch = _harness.Batch(BatchPurpose.Maintenance);
            BatchResult<IReadOnlyList<SegmentBlob>> result = BlobStatements.Segment(batch, "bm-scan", "bm-scan/ab");

            DataTable set = Table(("Id", typeof(int)), ("StorageKey", typeof(string)), ("Variants", typeof(string)), ("State", typeof(string)), ("CreatedAt", typeof(DateTimeOffset)));
            _ = set.Rows.Add(7, "bm-scan/ab/ab0123456789abcdef0123456789abcd", "thumb", "Committed", created);
            await BlobStatementsTests.ReplayAsync(batch, set);

            Assert.Equal([new SegmentBlob(7, "bm-scan/ab/ab0123456789abcdef0123456789abcd", "thumb", BlobState.Committed, created)], result.Value);
        }

        /// <summary>An empty result set with the given columns.</summary>
        /// <param name="columns">The columns' names and types, in order.</param>
        /// <returns>The set.</returns>
        private static DataTable Table(params (string Name, Type Type)[] columns)
        {
            DataTable table = new();
            foreach ((string name, Type type) in columns)
            {
                _ = table.Columns.Add(name, type);
            }

            return table;
        }
    }
}
