// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Data;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Blobs;
using Tellma.Core.Data.Batches;
using Tellma.Core.Tests.Data.Batches;

namespace Tellma.Core.Tests.Blobs
{
    /// <summary>
    ///     The stage statement as it composes: the quota then the insert, or the insert alone in the
    ///     platform's own scope; its parameters typed and in order; its writes and its reader.
    /// </summary>
    public sealed class BlobStatementsTests : IDisposable
    {
        /// <summary>The row every case stages.</summary>
        private static readonly StagedRow Row = new(
            42, "bm-scan", "bm-scan/ab/ab0123456789abcdef0123456789abcd", "application/pdf", "Report.pdf", 1234, new byte[32], null, null, "thumb", 86400);

        /// <summary>The batches, over the default composition with a connection that refuses.</summary>
        private readonly BatchHarness _harness = new();

        /// <inheritdoc />
        public void Dispose()
        {
            _harness.Dispose();
        }

        [Fact]
        public void The_statement_checks_the_quota_then_inserts_the_staged_row()
        {
            DataBatch batch = _harness.Batch(BatchPurpose.Persist);
            _ = BlobStatements.Stage(batch, Row, new StagingQuota(200, 536870912));

            BatchStatement statement = Assert.Single(batch.Statements);
            Assert.Equal(
                """
                DECLARE @tb0_now datetimeoffset(3) = SYSUTCDATETIME();
                DECLARE @tb0_count int, @tb0_bytes bigint;
                SELECT @tb0_count = COUNT(*), @tb0_bytes = ISNULL(SUM([Size]), 0)
                FROM [core].[Blobs] WHERE [CreatedById] = @tb0_p0 AND [State] = 'Staged' AND [ExpiresAt] > @tb0_now;
                IF @tb0_count >= @tb0_p11 OR @tb0_bytes + @tb0_p5 > @tb0_p12
                    THROW 50422, N'Blob.StagingQuotaExceeded', 1;
                DECLARE @tb0_exp datetimeoffset(3) = DATEADD(second, @tb0_p10, @tb0_now);
                DECLARE @tb0_id int = NEXT VALUE FOR [core].[sq_Blobs];
                INSERT INTO [core].[Blobs] ([Id], [Kind], [StorageKey], [State], [ContentType], [FileName], [Size], [Sha256],
                    [Width], [Height], [Variants], [CreatedAt], [CreatedById], [CommittedAt], [ExpiresAt])
                VALUES (@tb0_id, @tb0_p1, @tb0_p2, 'Staged', @tb0_p3, @tb0_p4, @tb0_p5, @tb0_p6,
                    @tb0_p7, @tb0_p8, @tb0_p9, @tb0_now, @tb0_p0, NULL, @tb0_exp);
                SELECT @tb0_id AS [Id], @tb0_exp AS [ExpiresAt];
                """.ReplaceLineEndings("\n"),
                statement.Text);

            Assert.Equal(
                [
                    "@tb0_p0:Int:42", "@tb0_p1:NVarChar:bm-scan", "@tb0_p2:NVarChar:bm-scan/ab/ab0123456789abcdef0123456789abcd",
                    "@tb0_p3:NVarChar:application/pdf", "@tb0_p4:NVarChar:Report.pdf", "@tb0_p5:BigInt:1234", "@tb0_p6:Binary:System.Byte[]",
                    "@tb0_p7:Int:", "@tb0_p8:Int:", "@tb0_p9:NVarChar:thumb", "@tb0_p10:Int:86400", "@tb0_p11:Int:200", "@tb0_p12:BigInt:536870912",
                ],
                statement.Parameters.Cast<ScalarParameter>().Select(static parameter => $"{parameter.Name}:{parameter.Type}:{parameter.Value}"));
            Assert.Equal([40, 120, 100, 255, 32, 100], statement.Parameters.Cast<ScalarParameter>().Where(static parameter => parameter.Size is not null).Select(static parameter => parameter.Size!.Value));
            Assert.Equal([new TableName("core", "Blobs")], statement.Writes);
            Assert.False(statement.Idempotent);
        }

        [Fact]
        public void The_platforms_own_scope_stages_without_the_quota_or_its_parameters()
        {
            DataBatch batch = _harness.Batch(BatchPurpose.Persist);
            _ = BlobStatements.Stage(batch, Row, quota: null);

            BatchStatement statement = Assert.Single(batch.Statements);
            Assert.DoesNotContain("THROW", statement.Text, StringComparison.Ordinal);
            Assert.DoesNotContain("@tb0_count", statement.Text, StringComparison.Ordinal);
            Assert.StartsWith("DECLARE @tb0_now datetimeoffset(3) = SYSUTCDATETIME();\nDECLARE @tb0_exp", statement.Text, StringComparison.Ordinal);
            Assert.Equal(11, statement.Parameters.Count);
        }

        [Fact]
        public async Task The_reader_completes_the_id_and_the_deadline_and_refuses_an_empty_set()
        {
            DateTimeOffset expires = new(2026, 11, 19, 9, 0, 0, TimeSpan.Zero);
            DataBatch batch = _harness.Batch(BatchPurpose.Persist);
            BatchResult<StagedBlob> result = BlobStatements.Stage(batch, Row, quota: null);

            await ReplayAsync(batch, Set((7, expires)));
            Assert.Equal(new StagedBlob(7, expires), result.Value);

            DataBatch empty = _harness.Batch(BatchPurpose.Persist);
            _ = BlobStatements.Stage(empty, Row, quota: null);
            _ = await Assert.ThrowsAsync<InvalidOperationException>(() => ReplayAsync(empty, Set()));
        }

        /// <summary>Hands a set to the batch's one statement and completes its result.</summary>
        /// <param name="batch">The batch.</param>
        /// <param name="set">The set.</param>
        /// <returns>A task that completes when the set is read.</returns>
        internal static async Task ReplayAsync(DataBatch batch, DataTable set)
        {
            using DataTableReader reader = new(set);
            ResultSetCursor cursor = new(reader);
            await batch.Statements[0].Reader!(cursor, TestContext.Current.CancellationToken);
            cursor.ApplyCompletions();
        }

        /// <summary>The stage statement's result set.</summary>
        /// <param name="rows">The rows.</param>
        /// <returns>The set.</returns>
        internal static DataTable Set(params (int Id, DateTimeOffset ExpiresAt)[] rows)
        {
            DataTable table = new();
            _ = table.Columns.Add("Id", typeof(int));
            _ = table.Columns.Add("ExpiresAt", typeof(DateTimeOffset));
            foreach ((int id, DateTimeOffset expiresAt) in rows)
            {
                _ = table.Rows.Add(id, expiresAt);
            }

            return table;
        }
    }
}
