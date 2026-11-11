// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient;
using System.Globalization;
using System.Text;
using Tellma.Core.Abstractions.Hosting;
using Tellma.Queryex.Testing.Schema;
using Tellma.Queryex.Testing.Semantics;

namespace Tellma.Core.Queryex.IntegrationTests
{
    /// <summary>
    ///     The ledger's tables and rows on a real server: a database of its own on the shared SQL
    ///     Server fixture, created and seeded once for the collection.
    /// </summary>
    /// <remarks>
    ///     The server is whatever the environment names, or a container started for the run, so a
    ///     developer can use the local instance they already have and continuous integration gets
    ///     something it can create and throw away.
    /// </remarks>
    public sealed class LedgerDatabase : IAsyncLifetime
    {
        /// <summary>The server the database lives on, owned by this fixture.</summary>
        private readonly SqlServerFixture _server = new();

        /// <summary>How to reach the database the fixture created.</summary>
        public string ConnectionString { get; private set; } = string.Empty;

        /// <summary>Starts the server if needed, then creates the database, its tables and its rows.</summary>
        /// <returns>A task that completes when the fixture is ready.</returns>
        public async ValueTask InitializeAsync()
        {
            await _server.InitializeAsync();

            // A database of the run's own, created in the platform collation, which the server's
            // default is not. The text columns carry their own collation either way; the database's
            // is what literals and variables get.
            ConnectionString = await _server.CreateDatabaseAsync("queryex", DatabaseCollations.Platform);

            await using SqlConnection database = new(ConnectionString);
            await database.OpenAsync();
            await Execute(database, LedgerDdl.Create());
            await Execute(database, LedgerData.Insert());
        }

        /// <summary>Drops the database on an existing server, or stops the container.</summary>
        /// <returns>A task that completes when the server is released.</returns>
        public async ValueTask DisposeAsync()
        {
            await _server.DisposeAsync();
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
    public sealed class SqlServerGroup : ICollectionFixture<LedgerDatabase>
    {
        /// <summary>The group's name.</summary>
        public const string Name = "SqlServer";
    }
}
