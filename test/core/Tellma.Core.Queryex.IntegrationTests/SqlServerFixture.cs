// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient;
using System.Globalization;
using System.Text;
using Tellma.Queryex.Testing.Schema;
using Tellma.Queryex.Testing.Semantics;
using Testcontainers.MsSql;

namespace Tellma.Core.Queryex.IntegrationTests
{
    /// <summary>
    ///     A real server with the fixture's tables and rows on it.
    /// </summary>
    /// <remarks>
    ///     Uses whatever server the environment names, and starts one in a container when it names
    ///     none. Both paths matter: a developer wants the local instance they already have, and
    ///     continuous integration wants something it can create and throw away.
    /// </remarks>
    public sealed class SqlServerFixture : IAsyncLifetime
    {
        /// <summary>The variable naming a server to use instead of starting one.</summary>
        private const string ConnectionVariable = "TELLMA_TEST_SQL";

        /// <summary>The database the fixture creates.</summary>
        private const string DatabaseName = "TellmaQueryexTests";

        /// <summary>
        ///     The image a container run uses.
        /// </summary>
        /// <remarks>
        ///     Pinned rather than left floating, so that what a differential run compares against is
        ///     the same backend on every machine and in every build.
        /// </remarks>
        private const string Image = "mcr.microsoft.com/mssql/server:2022-latest";

        /// <summary>The container, when one was started.</summary>
        private MsSqlContainer? _container;

        /// <summary>How to reach the database the fixture created.</summary>
        public string ConnectionString { get; private set; } = string.Empty;

        /// <summary>Starts the server if needed, then creates the tables and puts the rows on them.</summary>
        /// <returns>A task that completes when the fixture is ready.</returns>
        public async ValueTask InitializeAsync()
        {
            string? named = Environment.GetEnvironmentVariable(ConnectionVariable);
            string master;

            if (string.IsNullOrWhiteSpace(named))
            {
                _container = new MsSqlBuilder(Image).Build();
                await _container.StartAsync();
                master = _container.GetConnectionString();
            }
            else
            {
                master = named;
            }

            SqlConnectionStringBuilder builder = new(master) { InitialCatalog = "master" };
            await using (SqlConnection connection = new(builder.ConnectionString))
            {
                await connection.OpenAsync();
                await Execute(connection, "IF DB_ID('" + DatabaseName + "') IS NULL CREATE DATABASE ["
                    + DatabaseName + "];");
            }

            builder.InitialCatalog = DatabaseName;
            ConnectionString = builder.ConnectionString;

            await using SqlConnection database = new(ConnectionString);
            await database.OpenAsync();
            await Execute(database, LedgerDdl.Drop());
            await Execute(database, LedgerDdl.Create());
            await Execute(database, LedgerData.Insert());
        }

        /// <summary>Stops the container, when one was started.</summary>
        /// <returns>A task that completes when it has stopped.</returns>
        public async ValueTask DisposeAsync()
        {
            if (_container is not null)
            {
                await _container.DisposeAsync();
            }
        }

        /// <summary>Opens a connection to the fixture's database.</summary>
        /// <returns>The open connection.</returns>
        public async Task<SqlConnection> OpenAsync()
        {
            SqlConnection connection = new(ConnectionString);
            await connection.OpenAsync();
            return connection;
        }

        /// <summary>Runs one script, statement by statement.</summary>
        /// <param name="connection">The open connection.</param>
        /// <param name="script">The script.</param>
        /// <returns>A task that completes when the script has run.</returns>
        /// <remarks>
        ///     Split on the separator a command-line client understands, because a driver sends one
        ///     batch at a time and the scripts are written the way a person would write them.
        /// </remarks>
        private static async Task Execute(SqlConnection connection, string script)
        {
            foreach (string batch in Batches(script))
            {
                await using SqlCommand command = connection.CreateCommand();
                command.CommandText = batch;
                await command.ExecuteNonQueryAsync();
            }
        }

        /// <summary>Splits a script into the batches a driver can send.</summary>
        /// <param name="script">The script.</param>
        /// <returns>The batches.</returns>
        private static IEnumerable<string> Batches(string script)
        {
            string[] lines = script.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
            StringBuilder batch = new();

            foreach (string line in lines)
            {
                if (string.Equals(line.Trim(), "GO", StringComparison.OrdinalIgnoreCase))
                {
                    if (batch.Length > 0)
                    {
                        yield return batch.ToString();
                        batch.Clear();
                    }

                    continue;
                }

                batch.Append(line).Append('\n');
            }

            if (batch.ToString().Trim().Length > 0)
            {
                yield return batch.ToString();
            }
        }

        /// <summary>The whole numbers a scalar query returns, for the checks that need one.</summary>
        /// <param name="sql">The query.</param>
        /// <returns>The values.</returns>
        public async Task<IReadOnlyList<string>> ReadColumnAsync(string sql)
        {
            await using SqlConnection connection = await OpenAsync();
            await using SqlCommand command = connection.CreateCommand();
            command.CommandText = sql;

            List<string> values = [];
            await using SqlDataReader reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                values.Add(reader.IsDBNull(0)
                    ? string.Empty
                    : Convert.ToString(reader.GetValue(0), CultureInfo.InvariantCulture) ?? string.Empty);
            }

            return values;
        }
    }

    /// <summary>The group of tests that share one server.</summary>
    [CollectionDefinition(Name)]
    public sealed class SqlServerGroup : ICollectionFixture<SqlServerFixture>
    {
        /// <summary>The group's name.</summary>
        public const string Name = "SqlServer";
    }
}
