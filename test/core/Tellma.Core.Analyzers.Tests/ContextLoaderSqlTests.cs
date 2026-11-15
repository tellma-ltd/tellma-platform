// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Analyzers.Tests.Infrastructure;

namespace Tellma.Core.Analyzers.Tests
{
    /// <summary>
    ///     TELLMA0003 over the context loader's raw read: the batch's rules, and a read only. It writes
    ///     nothing but table variables it declares, declares no writes, and names no reserved name.
    /// </summary>
    public class ContextLoaderSqlTests
    {
        /// <summary>The usings and the table-type row every test body may use.</summary>
        private const string Prelude = """
            using System;
            using System.Collections.Generic;
            using Tellma.Core.Abstractions.Data;
            using Tellma.Core.Abstractions.Entities;
            using Tellma.Core.Abstractions.Validation;

            [TableType]
            public sealed class IdRow
            {
                public int Id { get; set; }
            }

            """;

        /// <summary>A read, with or without options, and a table variable the text declares and fills, pass.</summary>
        /// <returns>A task that completes when the check has passed.</returns>
        [Fact]
        public Task A_read_passes()
        {
            return Verify("""
                int id = 1;
                List<IdRow> rows = [];
                loader.Sql($"SELECT [Id] FROM [dist].[Ledger] WHERE [Id] = {id};", new SqlOptions(resultSets: 1));
                loader.Sql($"SELECT [Id] FROM [dist].[Ledger] AS l JOIN {rows} AS r ON r.[Id] = l.[Id];");
                loader.Sql($"DECLARE @seen TABLE ([Id] int); INSERT INTO @seen ([Id]) SELECT [Id] FROM [dist].[Ledger]; DELETE FROM @seen WHERE [Id] < 0; SELECT [Id] FROM @seen;", null);
                loader.Sql($"SELECT 1;", new SqlOptions(writes: new HashSet<TableName>()));
                """);
        }

        /// <summary>A write into a permanent table is refused, whatever the options declare.</summary>
        /// <returns>A task that completes when the check has passed.</returns>
        [Fact]
        public Task A_permanent_write_is_refused()
        {
            return Verify("""
                loader.Sql($"INSERT INTO {|TELLMA0003:[dist].[Ledger]|} ([Id]) VALUES (1);", SqlOptions.None);
                loader.Sql($"UPDATE {|TELLMA0003:[dist].[Ledger]|} SET [Amount] = 0;", Opaque);
                loader.Sql($"SELECT 1 AS [Id] INTO {|TELLMA0003:[dist].[Copy]|};");
                """);
        }

        /// <summary>A write into a temporary table, an undeclared table variable or a hole's table is refused.</summary>
        /// <returns>A task that completes when the check has passed.</returns>
        [Fact]
        public Task A_write_into_anything_but_a_declared_table_variable_is_refused()
        {
            return Verify("""
                List<IdRow> rows = [];
                loader.Sql($"INSERT INTO {|TELLMA0003:#t|} ([Id]) VALUES (1);");
                loader.Sql($"DELETE FROM {|TELLMA0003:@elsewhere|};");
                loader.Sql($"DELETE FROM {|TELLMA0003:{rows}|};");
                """);
        }

        /// <summary>Writes the options spell out are refused at the options; a set the call does not show is left to the run-time check.</summary>
        /// <returns>A task that completes when the check has passed.</returns>
        [Fact]
        public Task Declared_writes_are_refused()
        {
            return Verify("""
                loader.Sql($"SELECT 1;", {|TELLMA0003:new SqlOptions(writes: new HashSet<TableName> { new("dist", "Ledger") })|});
                loader.Sql($"SELECT 1;", Opaque);
                """);
        }

        /// <summary>Every reserved name is refused, read or written, once per name and in the runtime too; look-alikes pass.</summary>
        /// <returns>A task that completes when the check has passed.</returns>
        [Fact]
        public async Task Reserved_names_are_refused_even_when_read()
        {
            const string Body = """
                loader.Sql($"SELECT [Id] FROM [dist].[Ledger] WHERE [CreatedById] = {|TELLMA0003:@tm_CallerId|} OR [ModifiedById] = @tm_CallerId;");
                loader.Sql($"SELECT [Id] FROM {|TELLMA0003:@TB1_keys|};");
                loader.Sql($"DECLARE {|TELLMA0003:@qx0_p0|} int = 1; SELECT {|TELLMA0003:@tm_Guard|};");
                loader.Sql($"DECLARE @tmp int = 1, @tbl int = 2, @qxRate int = 3; SELECT @tmp, @tbl, @qxRate;");
                """;
            await Verify(Body);
            await Verify(Body, "Tellma.Core");
        }

        /// <summary>The two procedures a persist batch may execute are refused: a sequence range is never given back, and a session applock outlives the read.</summary>
        /// <returns>A task that completes when the check has passed.</returns>
        [Fact]
        public Task The_batch_procedures_are_refused()
        {
            return Verify("""
                loader.Sql($"DECLARE @f sql_variant; {|TELLMA0003:EXEC sys.sp_sequence_get_range @sequence_name = N'dbo.Ids', @range_size = 1000000, @range_first_value = @f OUTPUT|}; SELECT 1;");
                loader.Sql($"{|TELLMA0003:EXEC sys.sp_getapplock @Resource = N'x', @LockMode = 'Shared', @LockOwner = 'Session'|}; SELECT 1;");
                """);
        }

        /// <summary>The batch's rules hold for the loader's text, under the loader's name.</summary>
        /// <returns>A task that completes when the check has passed.</returns>
        [Fact]
        public Task The_batch_rules_apply()
        {
            return Verify("""
                FormattableString text = $"SELECT 1";
                loader.Sql({|TELLMA0003:text|});
                loader.Sql($"{|TELLMA0003:EXEC [dist].[Mine]|}");
                loader.Sql($"{|TELLMA0003:THROW 50000, N'x', 1;|}");
                Func<FormattableString, SqlOptions?, ContextRef<RawResult>> run = {|TELLMA0003:loader.Sql|};
                _ = nameof(IContextLoader.Sql);
                """);
        }

        /// <summary>Wraps a body in a method over a loader and runs the analyzer over it.</summary>
        /// <param name="body">The method body, with the expected diagnostics marked.</param>
        /// <param name="assemblyName">The compilation's assembly name.</param>
        /// <returns>A task that completes when the check has passed.</returns>
        private static Task Verify(string body, string assemblyName = AnalyzerHarness.DistributionAssembly)
        {
            string indented = string.Join("\n", body.Split('\n').Select(static line => "        " + line));
            string source = Prelude + $$"""
                public class Statements
                {
                    private static readonly SqlOptions Opaque = new(writes: new HashSet<TableName> { new("dist", "Ledger") });

                    public void Run(IContextLoader loader)
                    {
                {{indented}}
                    }
                }
                """;
            return AnalyzerHarness.VerifyAsync<DataBatchAnalyzer>(source, assemblyName);
        }
    }
}
