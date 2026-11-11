// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Data;
using System.Runtime.CompilerServices;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.TableTypes;
using Tellma.Core.Data.Batches;

namespace Tellma.Core.Tests.Data.Batches
{
    /// <summary>
    ///     How a batch assembles with no database: the frame and the contributors' stages, ordinals and
    ///     statement-local names, the line map, the hole renderer, deduplication, the caller's write
    ///     authority and the parameter budget.
    /// </summary>
    public sealed class DataBatchAssemblyTests : IDisposable
    {
        private static readonly TableName Widgets = new("fixture", "Widgets");

        /// <summary>A format whose hole names a second argument, kept out of a literal so the analyzers let it through.</summary>
        private static readonly string HoleWithoutArgument = "SELECT {" + 1.ToString(System.Globalization.CultureInfo.InvariantCulture) + "};";

        private readonly BatchHarness _harness = new();

        [Fact]
        public async Task A_batch_assembles_the_frame_with_prologues_in_order_and_epilogues_in_reverse()
        {
            TextContributor first = new(10, "PRINT 'a prologue';", "PRINT 'a epilogue';");
            TextContributor second = new(20, "PRINT 'b prologue';", "PRINT 'b epilogue';");

            // Registered out of order: the batch runs them by Order.
            DataBatch batch = _harness.Batch(BatchPurpose.Persist, second, first);
            _ = batch.Sql($"UPDATE [fixture].[Widgets] SET [Price] = {5m}", new SqlOptions(writes: new HashSet<TableName> { Widgets }));

            BatchAssembly assembly = await BatchHarness.SealAsync(batch);

            string expected = string.Join(
                "\n",
                BatchAssembly.SessionSettings,
                BatchAssembly.SchemaGuardText,
                "PRINT 'a prologue';",
                "PRINT 'b prologue';",
                BatchAssembly.TransactionOpen,
                "UPDATE [fixture].[Widgets] SET [Price] = @tb2_p0",
                ";",
                "PRINT 'b epilogue';",
                "PRINT 'a epilogue';",
                BatchAssembly.TransactionCommit,
                string.Empty);
            Assert.Equal(expected, assembly.Text);
            Assert.Equal([0, 1, 2, 3, 4], assembly.Statements.Select(static statement => statement.Ordinal));
            Assert.Equal([DataBatchStage.Prologue, DataBatchStage.Epilogue], first.Ran);
            Assert.Equal([DataBatchStage.Prologue, DataBatchStage.Epilogue], second.Ran);

            // --- The fingerprint is the first parameter, fixed-length text; the hole is the statement's own ---
            ScalarParameter fingerprint = Assert.IsType<ScalarParameter>(assembly.Parameters[0]);
            Assert.Equal((ReservedSqlNames.SchemaFingerprint, SqlDbType.Char, (object?)_harness.Executor.Fingerprint), (fingerprint.Name, fingerprint.Type, fingerprint.Value));
            Assert.Equal("@tb2_p0", assembly.Parameters[1].Name);
        }

        [Fact]
        public async Task The_same_batch_assembles_to_the_same_text_every_time()
        {
            async Task<string> AssembleAsync()
            {
                DataBatch batch = _harness.Batch(BatchPurpose.Read, new TextContributor(1, "PRINT 'p';", "PRINT 'e';"));
                IdList[] rows = [new() { Id = 1 }];
                SqlIdentifier ids = batch.Tvp(rows);
                _ = batch.Sql($"SELECT [Id] FROM {ids} WHERE [Id] > {0} AND {"x"} <> N'';", new SqlOptions(resultSets: 1));
                return (await BatchHarness.SealAsync(batch)).Text;
            }

            Assert.Equal(await AssembleAsync(), await AssembleAsync());
        }

        [Fact]
        public async Task The_prologue_stage_runs_once_at_the_first_statement_and_a_batch_with_none_still_runs_both_stages()
        {
            TextContributor contributor = new(1, "PRINT 'p';", null);
            DataBatch batch = _harness.Batch(BatchPurpose.Read, contributor);
            Assert.Empty(contributor.Ran);

            _ = batch.Sql($"SELECT 1 AS [One];", new SqlOptions(resultSets: 1));
            _ = batch.Sql($"SELECT 2 AS [Two];", new SqlOptions(resultSets: 1));
            Assert.Equal([DataBatchStage.Prologue], contributor.Ran);

            TextContributor idle = new(1, "PRINT 'p';", "PRINT 'e';");
            BatchAssembly empty = await BatchHarness.SealAsync(_harness.Batch(BatchPurpose.Read, idle));
            Assert.Equal([DataBatchStage.Prologue, DataBatchStage.Epilogue], idle.Ran);
            Assert.Equal(2, empty.StatementCount);
            Assert.False(empty.InTextTransaction);
        }

        [Fact]
        public async Task Only_an_auto_batch_that_declares_a_write_opens_its_own_transaction()
        {
            foreach ((TransactionMode mode, bool writes, bool expected) in new[]
            {
                (TransactionMode.Auto, true, true),
                (TransactionMode.Auto, false, false),
                (TransactionMode.None, true, false),
                (TransactionMode.Explicit, true, false),
            })
            {
                DataBatch batch = _harness.Batch(BatchPurpose.Persist);
                batch.TransactionMode = mode;
                _ = batch.Sql($"DELETE FROM [fixture].[Widgets] WHERE 1 = 0;", writes ? new SqlOptions(writes: new HashSet<TableName> { Widgets }) : null);
                BatchAssembly assembly = await BatchHarness.SealAsync(batch);

                Assert.Equal(expected, assembly.InTextTransaction);
                Assert.Equal(expected, assembly.Text.Contains(BatchAssembly.TransactionOpen, StringComparison.Ordinal));
            }
        }

        [Fact]
        public async Task A_marked_batch_wraps_everything_after_its_prologue_in_the_guard()
        {
            DataBatch batch = _harness.Batch(BatchPurpose.Persist, new TextContributor(1, "DECLARE @tm_Guard bit = 1;", null));
            batch.MarkConnectPrologue();
            _ = batch.Sql($"DELETE FROM [fixture].[Widgets] WHERE 1 = 0;", new SqlOptions(writes: new HashSet<TableName> { Widgets }));

            BatchAssembly assembly = await BatchHarness.SealAsync(batch);

            Assert.True(assembly.Guarded);
            Assert.EndsWith(
                string.Join("\n", "DECLARE @tm_Guard bit = 1;", BatchAssembly.GuardOpen, BatchAssembly.TransactionOpen, "DELETE FROM [fixture].[Widgets] WHERE 1 = 0;", BatchAssembly.TransactionCommit, BatchAssembly.GuardClose, string.Empty),
                assembly.Text,
                StringComparison.Ordinal);
        }

        [Fact]
        public void The_connect_prologue_mark_is_refused_on_a_maintenance_batch_and_after_the_first_statement()
        {
            _ = Assert.Throws<InvalidOperationException>(() => _harness.Batch(BatchPurpose.Maintenance).MarkConnectPrologue());

            DataBatch batch = _harness.Batch();
            _ = batch.Sql($"SELECT 1 AS [One];", new SqlOptions(resultSets: 1));
            _ = Assert.Throws<InvalidOperationException>(batch.MarkConnectPrologue);
            _ = Assert.Throws<InvalidOperationException>(() => batch.Begin(BatchSection.Prologue));
        }

        [Fact]
        public async Task A_guarded_batch_whose_prologue_reports_no_verdict_is_a_defect()
        {
            DataBatch batch = _harness.Batch(BatchPurpose.Read);
            batch.MarkConnectPrologue();
            _ = batch.Sql($"SELECT 1 AS [One];", new SqlOptions(resultSets: 1));
            BatchAssembly assembly = await BatchHarness.SealAsync(batch);

            using DataTableReader reader = new(Table("One", 1));
            _ = await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await assembly.ReadAsync(new ResultSetCursor(reader), static () => null, TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task Statement_local_names_carry_the_ordinal_and_ordinals_are_append_order()
        {
            DataBatch batch = _harness.Batch(BatchPurpose.Read, new TextContributor(1, "PRINT 'p';", null));
            SqlIdentifier ids = batch.DeclareIdTable();
            IdList[] rows = [new() { Id = 1 }];
            SqlIdentifier tvp = batch.Tvp(rows);
            _ = batch.Sql($"SELECT [Id] FROM {ids} UNION ALL SELECT [Id] FROM {tvp} WHERE [Id] = {7};", new SqlOptions(resultSets: 1));

            Assert.Equal("@tb1_ids", ids.Name);
            Assert.Equal("@tb2_t0", tvp.Name);

            BatchAssembly assembly = await BatchHarness.SealAsync(batch);
            Assert.Contains("SELECT [Id] FROM @tb1_ids UNION ALL SELECT [Id] FROM @tb2_t0 WHERE [Id] = @tb3_p0;", assembly.Text, StringComparison.Ordinal);
            Assert.Equal(["@tm_schema", "@tb2_t0", "@tb3_p0"], assembly.Parameters.Select(static parameter => parameter.Name));
            Assert.Equal(1, assembly.TableRows);

            // The text-less TVP statement takes an ordinal but no line.
            Assert.Equal(3, assembly.StatementCount);
        }

        [Fact]
        public async Task A_discarded_statement_gives_its_ordinal_to_the_next()
        {
            DataBatch batch = _harness.Batch();
            _ = Assert.Throws<ArgumentException>(() => batch.Sql($"SELECT {new SqlIdentifier("@undeclared")}.[Id];", null));
            _ = batch.Sql($"SELECT {1} AS [One];", new SqlOptions(resultSets: 1));

            BatchAssembly assembly = await BatchHarness.SealAsync(batch);
            Assert.Equal(0, Assert.Single(assembly.Statements).Ordinal);
            Assert.Equal("@tb0_p0", assembly.Parameters[1].Name);
        }

        [Fact]
        public async Task The_line_map_resolves_each_statements_lines_to_its_ordinal_and_the_frame_to_none()
        {
            DataBatch batch = _harness.Batch(BatchPurpose.Read, new TextContributor(1, "PRINT 'p';", null));
            _ = batch.Sql($"SELECT 1 AS [One]\nFROM (VALUES (1)) AS v([X]);", new SqlOptions(resultSets: 1));
            _ = batch.Sql($"SELECT 2 AS [Two] -- no terminator", new SqlOptions(resultSets: 1));
            BatchAssembly assembly = await BatchHarness.SealAsync(batch);

            // Lines 1-4 are the frame: the session settings and the schema guard.
            string[] lines = assembly.Text.Split('\n');
            Assert.Equal("PRINT 'p';", lines[4]);
            Assert.Equal([null, null, null, null, 0, 1, 1, 2, 2], Enumerable.Range(1, 9).Select(assembly.StatementOrdinalAt));

            // A statement without a terminator gets one on a line of its own, past any trailing comment.
            Assert.Equal(";", lines[8]);
            Assert.Null(assembly.StatementOrdinalAt(10));
        }

        [Fact]
        public void Holes_bind_scalars_typed_by_their_values_and_render_identifiers_as_text()
        {
            DataBatch batch = _harness.Batch();
            string longText = new('x', 4001);
            _ = batch.Sql(
                $"SELECT {"short"}, {longText}, {DayOfWeek.Monday}, {42}, {42L}, {1.5m}, {(string?)null}, {new byte[] { 1 }} FROM {new SqlIdentifier("fixture.Widgets")};",
                new SqlOptions(resultSets: 1));

            BatchStatement statement = Assert.Single(batch.Statements);
            Assert.Equal(
                "SELECT @tb0_p0, @tb0_p1, @tb0_p2, @tb0_p3, @tb0_p4, @tb0_p5, @tb0_p6, @tb0_p7 FROM [fixture].[Widgets];",
                statement.Text);

            (SqlDbType, object?, int?)[] expected =
            [
                (SqlDbType.NVarChar, "short", 4000),
                (SqlDbType.NVarChar, longText, -1),
                (SqlDbType.VarChar, "Monday", 4000),
                (SqlDbType.Int, 42, null),
                (SqlDbType.BigInt, 42L, null),
                (SqlDbType.Decimal, 1.5m, null),
                (SqlDbType.NVarChar, null, 4000),
            ];
            List<ScalarParameter> parameters = [.. statement.Parameters.Cast<ScalarParameter>()];
            Assert.Equal(expected, parameters.Take(7).Select(static parameter => (parameter.Type, parameter.Value, parameter.Size)));

            // Bytes bind at one size up to 8,000.
            Assert.Equal((SqlDbType.VarBinary, 8000), (parameters[7].Type, parameters[7].Size));
            Assert.Equal([1], Assert.IsType<byte[]>(parameters[7].Value));
        }

        [Fact]
        public void A_hole_with_an_alignment_or_a_format_an_undefined_enum_value_or_an_unbindable_value_is_refused()
        {
            DataBatch batch = _harness.Batch();

            _ = Assert.Throws<FormatException>(() => batch.Sql(FormattableStringFactory.Create("SELECT {0,5};", 1), null));
            _ = Assert.Throws<FormatException>(() => batch.Sql(FormattableStringFactory.Create("SELECT {0:N2};", 1m), null));
            _ = Assert.Throws<FormatException>(() => batch.Sql(FormattableStringFactory.Create(HoleWithoutArgument, 1), null));
            _ = Assert.Throws<ArgumentException>(() => batch.Sql($"SELECT {(DayOfWeek)42};", null));
            _ = Assert.Throws<ArgumentException>(() => batch.Sql($"SELECT {new object()};", null));
            _ = Assert.Throws<ArgumentException>(() => batch.Sql($"SELECT * FROM {new SqlIdentifier("@tb0_ids")};", null));

            // Nothing was appended, so the next statement still takes ordinal 0.
            Assert.Empty(batch.Statements);
        }

        [Fact]
        public void Identical_statements_collapse_unless_they_write_and_an_idempotent_write_collapses_too()
        {
            DataBatch batch = _harness.Batch(BatchPurpose.Persist);
            HashSet<TableName> writes = [Widgets];

            BatchResult<RawResult> read = batch.Sql($"SELECT {1} AS [One];", new SqlOptions(resultSets: 1));
            Assert.Same(read, batch.Sql($"SELECT {1} AS [One];", new SqlOptions(resultSets: 1)));
            Assert.NotSame(read, batch.Sql($"SELECT {2} AS [One];", new SqlOptions(resultSets: 1)));
            Assert.NotSame(read, batch.Sql($"SELECT {1} AS [One];", new SqlOptions(resultSets: 1, idempotent: true)));

            BatchResult<RawResult> write = batch.Sql($"DELETE FROM [fixture].[Widgets] WHERE [Id] = {1};", new SqlOptions(writes: writes));
            Assert.NotSame(write, batch.Sql($"DELETE FROM [fixture].[Widgets] WHERE [Id] = {1};", new SqlOptions(writes: writes)));

            BatchResult<RawResult> idempotent = batch.Sql($"DELETE FROM [fixture].[Widgets] WHERE [Id] = {2};", new SqlOptions(writes: writes, idempotent: true));
            Assert.Same(idempotent, batch.Sql($"DELETE FROM [fixture].[Widgets] WHERE [Id] = {2};", new SqlOptions(writes: new HashSet<TableName> { Widgets }, idempotent: true)));

            Assert.Equal(6, batch.Statements.Count);
        }

        [Fact]
        public void A_caller_write_to_a_stack_owned_table_is_refused_outside_the_callers_authority()
        {
            using BatchHarness owned = new(new MapOwners(new Dictionary<TableName, string> { [Widgets] = "fixture.widgets" }));
            DataBatch batch = owned.Batch(BatchPurpose.Persist);
            SqlOptions writes = new(writes: new HashSet<TableName> { Widgets });

            InvalidOperationException refused = Assert.Throws<InvalidOperationException>(() => batch.Sql($"DELETE FROM [fixture].[Widgets];", writes));
            Assert.Contains("fixture.widgets", refused.Message, StringComparison.Ordinal);

            // --- The stack's own authority admits it, and the internal channel never checks ---
            batch.SetCallerAuthority(new HashSet<TableName> { Widgets });
            _ = batch.Sql($"DELETE FROM [fixture].[Widgets];", writes);
            batch.SetCallerAuthority(new HashSet<TableName>());
            batch.Append(batch.Begin(), "DELETE FROM [fixture].[Widgets];", new HashSet<TableName> { Widgets });

            Assert.Equal(2, batch.Statements.Count);
            Assert.Equal(Widgets, Assert.Single(batch.WrittenTables));
        }

        [Fact]
        public void A_batch_over_the_parameter_budget_is_refused_when_the_statement_is_appended()
        {
            DataBatch batch = _harness.Batch();

            // The fingerprint is one of the 2,000.
            object[] fits = [.. Enumerable.Range(0, BatchAssembly.MaxParameters - 1).Cast<object>()];
            _ = batch.Sql(FormattableStringFactory.Create(Placeholders(fits.Length), fits), null);

            InvalidOperationException refused = Assert.Throws<InvalidOperationException>(
                () => batch.Sql(FormattableStringFactory.Create("SELECT {0};", -1), null));
            Assert.Contains("2001", refused.Message, StringComparison.Ordinal);
            _ = Assert.Single(batch.Statements);
        }

        [Fact]
        public void A_reserved_name_bound_to_two_values_is_refused_and_bound_to_one_value_is_bound_once()
        {
            DataBatch batch = _harness.Batch(BatchPurpose.Maintenance);
            foreach (int value in new[] { 1, 1 })
            {
                StatementDraft draft = batch.Begin();
                draft.Reserved(new ScalarParameter(ReservedSqlNames.UserId, SqlDbType.Int, value));
                batch.Append(draft, $"PRINT {ReservedSqlNames.UserId};");
            }

            Assert.Equal(2, batch.Assemble().Parameters.Count);

            StatementDraft other = batch.Begin();
            other.Reserved(new ScalarParameter(ReservedSqlNames.UserId, SqlDbType.Int, 2));
            batch.Append(other, $"PRINT {ReservedSqlNames.UserId};");
            _ = Assert.Throws<InvalidOperationException>(batch.Assemble);
        }

        [Fact]
        public async Task A_statement_that_reads_a_set_the_round_trip_did_not_return_or_leaves_one_unread_is_a_defect()
        {
            CancellationToken token = TestContext.Current.CancellationToken;

            // --- Two sets declared, one returned ---
            DataBatch reading = _harness.Batch();
            _ = reading.Sql($"SELECT 1 AS [A];", new SqlOptions(resultSets: 2));
            BatchAssembly twoDeclared = await BatchHarness.SealAsync(reading);
            using (DataTableReader one = new(Table("A", 1)))
            {
                InvalidOperationException missing = await Assert.ThrowsAsync<InvalidOperationException>(
                    async () => await twoDeclared.ReadAsync(new ResultSetCursor(one), static () => null, token));
                Assert.Contains("returned 1", missing.Message, StringComparison.Ordinal);
            }

            // --- One set declared, two returned ---
            DataBatch leaving = _harness.Batch();
            BatchResult<RawResult> result = leaving.Sql($"SELECT 1 AS [A];", new SqlOptions(resultSets: 1));
            BatchAssembly oneDeclared = await BatchHarness.SealAsync(leaving);
            using (DataTableReader two = new([Table("A", 1), Table("B", 2)]))
            {
                InvalidOperationException extra = await Assert.ThrowsAsync<InvalidOperationException>(
                    async () => await oneDeclared.ReadAsync(new ResultSetCursor(two), static () => null, token));
                Assert.Contains("more result sets", extra.Message, StringComparison.Ordinal);
            }

            // The statement read its own set before the surplus was found.
            Assert.Equal(1, ((int[])result.Value.ResultSets[0].GetBuffer(0))[0]);
        }

        [Fact]
        public async Task A_sealed_batch_takes_nothing_more()
        {
            DataBatch batch = _harness.Batch();
            _ = await BatchHarness.SealAsync(batch);

            _ = Assert.Throws<InvalidOperationException>(() => batch.Sql($"SELECT 1;", null));
            _ = Assert.Throws<InvalidOperationException>(() => batch.TransactionMode = TransactionMode.None);
            _ = Assert.Throws<InvalidOperationException>(() => batch.OnCommitted(static _ => { }));
            _ = await Assert.ThrowsAsync<InvalidOperationException>(() => batch.ExecuteAsync(TestContext.Current.CancellationToken));
        }

        [Fact]
        public void The_members_whose_compilers_are_not_composed_say_so()
        {
            DataBatch batch = _harness.Batch();
            NotSupportedException refused = Assert.Throws<NotSupportedException>(batch.FromCache<IdList>);
            Assert.Contains("FromCache", refused.Message, StringComparison.Ordinal);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _harness.Dispose();
        }

        /// <summary>A one-column, one-row table.</summary>
        /// <param name="column">The column.</param>
        /// <param name="value">The value.</param>
        /// <returns>The table.</returns>
        private static DataTable Table(string column, int value)
        {
            DataTable table = new();
            _ = table.Columns.Add(column, typeof(int));
            _ = table.Rows.Add(value);
            return table;
        }

        /// <summary>A <c>SELECT</c> of that many holes.</summary>
        /// <param name="count">The holes.</param>
        /// <returns>The format.</returns>
        private static string Placeholders(int count)
        {
            return "SELECT " + string.Join(", ", Enumerable.Range(0, count).Select(static index => "{" + index.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}")) + ";";
        }
    }
}
