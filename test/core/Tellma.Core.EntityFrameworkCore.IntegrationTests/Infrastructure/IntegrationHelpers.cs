// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Tellma.Core.EntityFrameworkCore.MigrationsHost;
using Tellma.Core.EntityFrameworkCore.TableTypes;

namespace Tellma.Core.EntityFrameworkCore.IntegrationTests.Infrastructure
{
    /// <summary>Shared helpers for the integration suite.</summary>
    public static class IntegrationHelpers
    {
        /// <summary>Creates the migrator-shaped host's context against a real database.</summary>
        public static MigrationsHostContext CreateMigrationsHostContext(string connectionString)
        {
            DbContextOptionsBuilder<MigrationsHostContext> optionsBuilder = new();
            optionsBuilder
                .UseSqlServer(connectionString)
                .UseTableTypes(sweepScope: MigrationsHostContext.SweepScope);
            return new MigrationsHostContext(optionsBuilder.Options);
        }

        /// <summary>Runs a scalar SQL query and returns the result.</summary>
        public static async Task<T?> ScalarAsync<T>(string connectionString, string sql)
        {
            await using SqlConnection connection = new(connectionString);
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await using SqlCommand command = connection.CreateCommand();
            command.CommandText = sql;
            object? result = await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);
            return result is null or DBNull ? default : (T)result;
        }

        /// <summary>Runs a single-column SQL query and returns all values as strings.</summary>
        public static async Task<List<string>> ColumnAsync(string connectionString, string sql)
        {
            await using SqlConnection connection = new(connectionString);
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await using SqlCommand command = connection.CreateCommand();
            command.CommandText = sql;
            await using SqlDataReader reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
            List<string> values = [];
            while (await reader.ReadAsync(TestContext.Current.CancellationToken))
            {
                values.Add(reader.GetString(0));
            }

            return values;
        }

        /// <summary>Executes one or more SQL batches (no GO splitting).</summary>
        public static async Task ExecuteAsync(string connectionString, params string[] batches)
        {
            await using SqlConnection connection = new(connectionString);
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            foreach (string batch in batches)
            {
                await using SqlCommand command = connection.CreateCommand();
                command.CommandText = batch;
                await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
            }
        }

        /// <summary>Splits a migration script on <c>GO</c> batch separators and executes it.</summary>
        public static async Task ExecuteScriptAsync(string connectionString, string script)
        {
            string[] batches = script.Split(["GO\r\n", "GO\n"], StringSplitOptions.RemoveEmptyEntries);
            await ExecuteAsync(connectionString, [.. batches.Where(b => !string.IsNullOrWhiteSpace(b))]);
        }

        /// <summary>
        ///     The ordered column names of a table type, resolved by its <b>logical</b> name (the
        ///     deployed physical name carries a content-hash suffix; the logical name is read from the
        ///     <c>Tellma:TableType:LogicalName</c> extended-property stamp).
        /// </summary>
        public static Task<List<string>> GetTypeColumnsAsync(string connectionString, string schema, string logicalName)
        {
            return ColumnAsync(
                connectionString,
                $"""
                SELECT c.[name]
                FROM [sys].[table_types] tt
                INNER JOIN [sys].[extended_properties] ep ON ep.[class] = 6 AND ep.[major_id] = tt.[user_type_id]
                    AND ep.[name] = N'Tellma:TableType:LogicalName' AND CONVERT(nvarchar(max), ep.[value]) = N'{logicalName}'
                INNER JOIN [sys].[columns] c ON c.[object_id] = tt.[type_table_object_id]
                WHERE SCHEMA_NAME(tt.[schema_id]) = N'{schema}'
                ORDER BY c.[column_id]
                """);
        }

        /// <summary>The physical (content-hash-suffixed) name of a table type, by its logical name.</summary>
        public static Task<string?> GetPhysicalNameAsync(string connectionString, string schema, string logicalName)
        {
            return ScalarAsync<string>(
                connectionString,
                $"""
                SELECT tt.[name]
                FROM [sys].[table_types] tt
                INNER JOIN [sys].[extended_properties] ep ON ep.[class] = 6 AND ep.[major_id] = tt.[user_type_id]
                    AND ep.[name] = N'Tellma:TableType:LogicalName' AND CONVERT(nvarchar(max), ep.[value]) = N'{logicalName}'
                WHERE SCHEMA_NAME(tt.[schema_id]) = N'{schema}'
                """);
        }

        /// <summary>The ordered column names of a table, from the catalog views.</summary>
        public static Task<List<string>> GetTableColumnsAsync(string connectionString, string schema, string name)
        {
            return ColumnAsync(
                connectionString,
                $"""
                SELECT c.[name]
                FROM [sys].[tables] t
                INNER JOIN [sys].[columns] c ON c.[object_id] = t.[object_id]
                WHERE t.[name] = N'{name}' AND SCHEMA_NAME(t.[schema_id]) = N'{schema}'
                ORDER BY c.[column_id]
                """);
        }
    }
}
