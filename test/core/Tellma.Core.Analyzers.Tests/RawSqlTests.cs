// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Analyzers.Tests.Infrastructure;

namespace Tellma.Core.Analyzers.Tests
{
    /// <summary>TELLMA0003: raw statement text is parsed and held to the batch's rules.</summary>
    public class RawSqlTests
    {
        /// <summary>The usings and the table-type row every test body may use.</summary>
        private const string Prelude = """
            using System;
            using System.Collections.Generic;
            using Tellma.Core.Abstractions.Data;

            public sealed class IdRow
            {
                public int Id { get; set; }
            }

            """;

        // ------------------------------------------------------------------
        // The text
        // ------------------------------------------------------------------

        /// <summary>Text not written at the call cannot be checked, so it is refused.</summary>
        /// <returns>A task that completes when the check has passed.</returns>
        [Fact]
        public Task Text_not_written_at_the_call_is_refused()
        {
            return Verify("""
                FormattableString text = $"SELECT 1";
                batch.Sql({|TELLMA0003:text|}, SqlOptions.None);
                batch.Sql({|TELLMA0003:System.Runtime.CompilerServices.FormattableStringFactory.Create("SELECT " + "1")|}, SqlOptions.None);
                """);
        }

        /// <summary>The raw statement taken as a delegate is refused: what it is later called with cannot be checked.</summary>
        /// <returns>A task that completes when the check has passed.</returns>
        [Fact]
        public Task The_raw_statement_as_a_delegate_is_refused()
        {
            return Verify("""
                Func<FormattableString, SqlOptions?, BatchResult<RawResult>> run = {|TELLMA0003:batch.Sql|};
                _ = nameof(IDataBatch.Sql);
                """);
        }

        /// <summary>Text that does not parse is refused where the parser stopped.</summary>
        /// <returns>A task that completes when the check has passed.</returns>
        [Fact]
        public Task Text_that_does_not_parse_is_refused()
        {
            return Verify("""
                batch.Sql($"SELECT [Id] {|TELLMA0003:F|}ROM WHERE [Id] = 1", SqlOptions.None);
                batch.Sql({|TELLMA0003:$"SELECT [Id] FROM"|}, SqlOptions.None);
                """);
        }

        /// <summary>Text holding two batches is refused.</summary>
        /// <returns>A task that completes when the check has passed.</returns>
        [Fact]
        public Task Text_holding_two_batches_is_refused()
        {
            return Verify("""
                batch.Sql({|TELLMA0003:$"SELECT 1\nGO\nSELECT 2"|}, SqlOptions.None);
                """);
        }

        /// <summary>Plain reads, raw strings and escaped braces pass.</summary>
        /// <returns>A task that completes when the check has passed.</returns>
        [Fact]
        public Task Plain_reads_pass()
        {
            return Verify(""""
                int id = 1;
                batch.Sql($"SELECT [Id], N'{{\"a\":1}}' AS [Json] FROM [core].[Jobs] WHERE [Id] = {id}", new SqlOptions(resultSets: 1));
                batch.Sql($"""
                    SELECT [Id]
                    FROM [core].[Jobs]
                    WHERE [Id] = {id};
                    """, new SqlOptions(resultSets: 1));
                """");
        }

        // ------------------------------------------------------------------
        // Holes
        // ------------------------------------------------------------------

        /// <summary>A hole with an alignment or a format string is refused.</summary>
        /// <returns>A task that completes when the check has passed.</returns>
        [Fact]
        public Task A_formatted_hole_is_refused()
        {
            return Verify("""
                decimal rate = 1.5m;
                batch.Sql($"SELECT {|TELLMA0003:{rate:N2}|}, {|TELLMA0003:{rate,10}|}", SqlOptions.None);
                """);
        }

        /// <summary>A bare nameof binds a string where a name was meant, so it is refused; an identifier over it passes.</summary>
        /// <returns>A task that completes when the check has passed.</returns>
        [Fact]
        public Task A_bare_nameof_is_refused()
        {
            return Verify("""
                batch.Sql($"SELECT {|TELLMA0003:{nameof(IdRow.Id)}|} FROM [core].[Jobs]", SqlOptions.None);
                batch.Sql($"SELECT {new SqlIdentifier(nameof(IdRow.Id))} FROM [core].[Jobs]", SqlOptions.None);
                """);
        }

        /// <summary>A hole of a type the batch cannot bind is refused.</summary>
        /// <returns>A task that completes when the check has passed.</returns>
        [Fact]
        public Task A_hole_the_batch_cannot_bind_is_refused()
        {
            return Verify("""
                List<int> numbers = [1];
                object boxed = 1;
                batch.Sql($"SELECT {|TELLMA0003:{boxed}|}, {|TELLMA0003:{numbers}|}", SqlOptions.None);
                """);
        }

        /// <summary>Scalars, identifiers and row lists bind.</summary>
        /// <returns>A task that completes when the check has passed.</returns>
        [Fact]
        public Task Scalars_identifiers_and_row_lists_bind()
        {
            return Verify(""""
                IReadOnlyList<IdRow> rows = [new IdRow { Id = 1 }];
                SqlIdentifier ids = batch.Tvp(rows);
                int? maybe = null;
                batch.Sql($"""
                    SELECT {1}, {"text"}, {'c'}, {true}, {2L}, {3.5m}, {4.5}, {Guid.Empty}, {DateTimeOffset.UnixEpoch}, {DateOnly.MinValue},
                        {TimeSpan.Zero}, {new byte[] { 1 }}, {maybe}, {null}, {DayOfWeek.Monday}
                    FROM {rows} AS r JOIN {ids} AS i ON i.[Id] = r.[Id];
                    """, new SqlOptions(resultSets: 1));
                """");
        }

        /// <summary>A hole inside a string literal, a comment or a quoted name is refused.</summary>
        /// <returns>A task that completes when the check has passed.</returns>
        [Fact]
        public Task A_hole_inside_quotes_or_a_comment_is_refused()
        {
            return Verify("""
                string name = "x";
                batch.Sql($"SELECT N'{|TELLMA0003:{name}|}' /* {|TELLMA0003:{name}|} */, [{|TELLMA0003:{name}|}]", SqlOptions.None);
                """);
        }

        // ------------------------------------------------------------------
        // Refused statements
        // ------------------------------------------------------------------

        /// <summary>MERGE is refused.</summary>
        /// <returns>A task that completes when the check has passed.</returns>
        [Fact]
        public Task Merge_is_refused()
        {
            return Verify("""
                batch.Sql($"{|TELLMA0003:MERGE INTO [dist].[Ledger] AS t USING (SELECT 1 AS [Id]) AS s ON t.[Id] = s.[Id] WHEN MATCHED THEN DELETE;|}", Writes);
                """);
        }

        /// <summary>EXEC is refused, procedures and dynamic SQL alike, except the two allow-listed system procedures.</summary>
        /// <returns>A task that completes when the check has passed.</returns>
        [Fact]
        public Task Exec_is_refused_outside_the_allow_list()
        {
            return Verify("""
                string text = "SELECT 1";
                batch.Sql($"{|TELLMA0003:EXEC [dist].[Recalculate]|}", SqlOptions.None);
                batch.Sql($"{|TELLMA0003:EXEC sp_executesql N'SELECT 1'|}", SqlOptions.None);
                batch.Sql($"{|TELLMA0003:EXEC ({text})|}", SqlOptions.None);
                batch.Sql($"DECLARE @p sysname = N'x'; {|TELLMA0003:EXEC @p|}", SqlOptions.None);
                batch.Sql($"{|TELLMA0003:EXEC [other].[sys].[sp_getapplock] @Resource = N'x', @LockMode = 'Exclusive'|}", SqlOptions.None);
                batch.Sql($"EXEC sys.sp_getapplock @Resource = N'x', @LockMode = 'Exclusive', @LockOwner = 'Transaction'", SqlOptions.None);
                batch.Sql($"DECLARE @first sql_variant; EXEC sp_sequence_get_range @sequence_name = N'[gl].[sq_Centers]', @range_size = 10, @range_first_value = @first OUTPUT", SqlOptions.None);
                """);
        }

        /// <summary>EXECUTE AS is refused.</summary>
        /// <returns>A task that completes when the check has passed.</returns>
        [Fact]
        public Task Execute_as_is_refused()
        {
            return Verify("""
                batch.Sql($"{|TELLMA0003:EXECUTE AS USER = 'someone';|} SELECT 1", SqlOptions.None);
                """);
        }

        /// <summary>The isolation level and the transaction belong to the executor.</summary>
        /// <returns>A task that completes when the check has passed.</returns>
        [Fact]
        public Task The_isolation_level_and_the_transaction_are_refused()
        {
            return Verify("""
                batch.Sql($"{|TELLMA0003:SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;|}", SqlOptions.None);
                batch.Sql($"{|TELLMA0003:BEGIN TRANSACTION;|} {|TELLMA0003:COMMIT;|}", SqlOptions.None);
                batch.Sql($"{|TELLMA0003:SAVE TRANSACTION point;|} {|TELLMA0003:ROLLBACK TRANSACTION point;|}", SqlOptions.None);
                batch.Sql($"{|TELLMA0003:SET XACT_ABORT OFF;|} {|TELLMA0003:SET IMPLICIT_TRANSACTIONS ON;|}", SqlOptions.None);
                batch.Sql($"SET NOCOUNT ON; SELECT 1;", new SqlOptions(resultSets: 1));
                """);
        }

        // ------------------------------------------------------------------
        // Reserved names
        // ------------------------------------------------------------------

        /// <summary>Declaring or assigning a reserved name is refused outside the runtime; reading one, and look-alikes, pass.</summary>
        /// <returns>A task that completes when the check has passed.</returns>
        [Fact]
        public Task Reserved_names_are_refused_outside_the_runtime()
        {
            return Verify("""
                batch.Sql($"DECLARE {|TELLMA0003:@tm_Mine int|}; DECLARE {|TELLMA0003:@TB1_x int|}; DECLARE {|TELLMA0003:@qx12_y TABLE ([Id] int)|};", SqlOptions.None);
                batch.Sql($"{|TELLMA0003:SET @tm_CallerId = 1;|} SELECT {|TELLMA0003:@tb2_p0 = 1|};", SqlOptions.None);
                batch.Sql($"INSERT INTO {|TELLMA0003:@tb3_keys|} ([Id]) VALUES (1);", SqlOptions.None);
                batch.Sql($"DECLARE @tmp int, @tbl int, @qxRate int, @tb_x int; SELECT [Id] FROM [core].[Jobs] WHERE [CreatedById] = @tm_CallerId;", new SqlOptions(resultSets: 1));
                """);
        }

        /// <summary>The runtime declares its own reserved names.</summary>
        /// <returns>A task that completes when the check has passed.</returns>
        [Fact]
        public Task The_runtime_declares_reserved_names()
        {
            return Verify("""
                batch.Sql($"DECLARE @tm_Mine int; SET @tm_Mine = 1;", SqlOptions.None);
                """, "Tellma.Core");
        }

        /// <summary>Declaring or assigning a value hole is refused: it is the batch's own parameter.</summary>
        /// <returns>A task that completes when the check has passed.</returns>
        [Fact]
        public Task Assigning_a_value_hole_is_refused()
        {
            return Verify("""
                int id = 1;
                SqlIdentifier keys = batch.DeclareIdTable();
                batch.Sql($"SET {|TELLMA0003:{id}|} = 2; INSERT INTO {keys} ([Id]) VALUES (1);", SqlOptions.None);
                """);
        }

        // ------------------------------------------------------------------
        // Raised errors
        // ------------------------------------------------------------------

        /// <summary>THROW outside the platform's and the distribution's bands is refused.</summary>
        /// <returns>A task that completes when the check has passed.</returns>
        [Fact]
        public Task Throw_outside_the_bands_is_refused()
        {
            return Verify("""
                const int Mine = 51000;
                int computed = Environment.TickCount;
                batch.Sql($"{|TELLMA0003:THROW 50000, N'x', 1;|}", SqlOptions.None);
                batch.Sql($"{|TELLMA0003:THROW 50700, N'x', 1;|}", SqlOptions.None);
                batch.Sql($"{|TELLMA0003:THROW {Mine}, N'x', 1;|}", SqlOptions.None);
                batch.Sql($"THROW 50400, N'x', 1; THROW 50699, N'y', 1; THROW {computed}, N'z', 1;", SqlOptions.None);
                batch.Sql($"BEGIN TRY SELECT 1; END TRY BEGIN CATCH THROW; END CATCH", SqlOptions.None);
                """);
        }

        /// <summary>RAISERROR above the informational severities, or the legacy form, is refused.</summary>
        /// <returns>A task that completes when the check has passed.</returns>
        [Fact]
        public Task Raiserror_above_informational_severity_is_refused()
        {
            return Verify("""
                int severity = 16;
                batch.Sql($"{|TELLMA0003:RAISERROR(N'x', 16, 1);|}", SqlOptions.None);
                batch.Sql($"{|TELLMA0003:RAISERROR(N'x', {severity}, 1);|}", SqlOptions.None);
                batch.Sql($"RAISERROR(N'progress', 10, 1) WITH NOWAIT; RAISERROR(N'note', 0, 1);", SqlOptions.None);
                batch.Sql($"RAISERROR {|TELLMA0003:5|}0001 'the legacy form does not parse';", SqlOptions.None);
                """);
        }

        // ------------------------------------------------------------------
        // Declared writes
        // ------------------------------------------------------------------

        /// <summary>A permanent table written without declared writes is refused, statement by statement.</summary>
        /// <returns>A task that completes when the check has passed.</returns>
        [Fact]
        public Task Writes_without_declared_writes_are_refused()
        {
            return Verify("""
                batch.Sql($"INSERT INTO {|TELLMA0003:[dist].[Ledger]|} ([Id]) VALUES (1);", SqlOptions.None);
                batch.Sql($"UPDATE {|TELLMA0003:[dist].[Ledger]|} SET [Amount] = 0;", null);
                batch.Sql($"DELETE FROM {|TELLMA0003:dist.Ledger|};", new SqlOptions(resultSets: 1));
                batch.Sql($"UPDATE {|TELLMA0003:l|} SET [Amount] = 0 FROM [dist].[Ledger] AS l JOIN [dist].[Accounts] AS a ON a.[Id] = l.[AccountId];", new SqlOptions(writes: null));
                batch.Sql($"DELETE {|TELLMA0003:Ledger|} FROM [dist].[Ledger] WHERE [Id] = 1;", new SqlOptions(writes: new HashSet<TableName>()));
                batch.Sql($"WITH c AS (SELECT * FROM [dist].[Ledger] WHERE [Id] > 1) DELETE FROM {|TELLMA0003:c|};", SqlOptions.None);
                batch.Sql($"SELECT 1 AS [Id] INTO {|TELLMA0003:[dist].[Copy]|};", SqlOptions.None);
                batch.Sql($"TRUNCATE TABLE {|TELLMA0003:[dist].[Ledger]|};", SqlOptions.None);
                batch.Sql($"DECLARE @k TABLE ([Id] int); DELETE FROM @k OUTPUT deleted.[Id] INTO {|TELLMA0003:[dist].[Log]|} ([Id]);", SqlOptions.None);
                batch.Sql($"INSERT INTO {|TELLMA0003:Ledger|} ([Id]) VALUES (1);", SqlOptions.None);
                batch.Sql($"UPDATE {|TELLMA0003:{new SqlIdentifier("dist.Ledger")}|} SET [Amount] = 0;", SqlOptions.None);
                batch.Sql($"WITH c AS (SELECT l.* FROM [dist].[Ledger] AS l JOIN [dist].[Accounts] AS a ON a.[Id] = l.[AccountId]) DELETE FROM {|TELLMA0003:c|};", SqlOptions.None);
                """);
        }

        /// <summary>Declared writes cover the statement; table variables, temporary tables and batch tables need none.</summary>
        /// <returns>A task that completes when the check has passed.</returns>
        [Fact]
        public Task Declared_and_local_writes_pass()
        {
            return Verify("""
                SqlIdentifier keys = batch.DeclareIdTable();
                IReadOnlyList<IdRow> rows = [];
                batch.Sql($"UPDATE l SET [Amount] = 0 FROM [dist].[Ledger] AS l;", Writes);
                batch.Sql($"UPDATE [dist].[Ledger] SET [Amount] = 0;", new SqlOptions(writes: new HashSet<TableName> { new("dist", "Ledger") }));
                batch.Sql($"UPDATE [dist].[Ledger] SET [Amount] = 0;", new SqlOptions(writes: Opaque()));
                batch.Sql($"UPDATE [dist].[Ledger] SET [Amount] = 0;", SqlOptions.ForCaller(Opaque()));
                batch.Sql($"DECLARE @k TABLE ([Id] int); INSERT INTO @k ([Id]) SELECT [Id] FROM {rows}; DELETE FROM @k;", SqlOptions.None);
                batch.Sql($"CREATE TABLE #t ([Id] int); INSERT INTO #t ([Id]) VALUES (1); UPDATE t SET [Id] = 2 FROM #t AS t;", SqlOptions.None);
                batch.Sql($"INSERT INTO {keys} ([Id]) SELECT [Id] FROM [dist].[Ledger];", SqlOptions.None);
                batch.Sql($"WITH c AS (SELECT * FROM {keys}) DELETE FROM c;", SqlOptions.None);
                batch.Sql($"DELETE FROM {new SqlIdentifier("@mine")}; DECLARE @mine TABLE ([Id] int);", SqlOptions.None);

                // A name the code does not show is left to the batch's run-time checks.
                batch.Sql($"UPDATE {new SqlIdentifier(Environment.MachineName)} SET [Amount] = 0;", SqlOptions.None);
                """);
        }

        /// <summary>A listed set must hold every permanent table the text writes.</summary>
        /// <returns>A task that completes when the check has passed.</returns>
        [Fact]
        public Task A_write_missing_from_the_listed_set_is_refused()
        {
            return Verify("""
                batch.Sql($"UPDATE [dist].[Ledger] SET [Amount] = 0; DELETE FROM {|TELLMA0003:[dist].[Accounts]|};", new SqlOptions(writes: new HashSet<TableName> { new("dist", "Ledger") }));
                batch.Sql($"UPDATE [DIST].[ledger] SET [Amount] = 0;", new SqlOptions(writes: new HashSet<TableName> { new TableName("dist", "Ledger") }, idempotent: true));
                """);
        }

        /// <summary>Writing a table whose rows bump a user-level tag names the affected users.</summary>
        /// <returns>A task that completes when the check has passed.</returns>
        [Fact]
        public Task A_user_tagged_write_names_its_users()
        {
            return Verify("""
                SqlIdentifier users = batch.DeclareIdTable();
                batch.Sql({|TELLMA0003:$"UPDATE [core].[Users] SET [Name] = N'x' WHERE [Id] = 1;"|}, new SqlOptions(writes: new HashSet<TableName> { new("core", "Users") }));
                batch.Sql($"UPDATE [core].[Users] SET [Name] = N'x' WHERE [Id] = 1;", new SqlOptions(writes: new HashSet<TableName> { new("core", "Users") }, userIds: users));
                batch.Sql($"UPDATE [core].[Users] SET [Name] = N'x' WHERE [Id] = 1;", SqlOptions.ForCaller(new HashSet<TableName> { new("core", "Users") }));
                """, "Tellma.Core");
        }

        /// <summary>Wraps statements in a method over a batch, with the usings and helpers every test uses.</summary>
        /// <param name="body">The method body.</param>
        /// <param name="assemblyName">The compilation's assembly name.</param>
        /// <returns>A task that completes when the check has passed.</returns>
        private static Task Verify(string body, string assemblyName = AnalyzerHarness.DistributionAssembly)
        {
            string indented = string.Join("\n", body.Split('\n').Select(static line => "        " + line));
            string source = Prelude + $$"""
                public class Statements
                {
                    private static readonly SqlOptions Writes = new(writes: new HashSet<TableName> { new("dist", "Ledger") });

                    private static IReadOnlySet<TableName> Opaque() => new HashSet<TableName>();

                    public void Run(IDataBatch batch)
                    {
                {{indented}}
                    }
                }
                """;
            return AnalyzerHarness.VerifyAsync<DataBatchAnalyzer>(source, assemblyName);
        }
    }
}
