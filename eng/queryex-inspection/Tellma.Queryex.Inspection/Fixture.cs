// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient;
using System.Text;
using Tellma.Queryex.Testing.Schema;
using Tellma.Queryex.Testing.Semantics;

namespace Tellma.Queryex.Inspection
{
    /// <summary>
    ///     Puts the fixture's tables and rows on whatever server the playground was pointed at.
    /// </summary>
    /// <remarks>
    ///     The same tables and the same rows the conformance suites use, so a query run here answers
    ///     what the suites pin rather than whatever happens to be in somebody's database.
    /// </remarks>
    internal static class Fixture
    {
        /// <summary>Creates the tables and inserts the rows, replacing whatever was there.</summary>
        /// <param name="connectionString">The server to deploy to.</param>
        /// <returns>What went wrong, or null when it worked.</returns>
        internal static async Task<string?> DeployAsync(string connectionString)
        {
            try
            {
                await CreateDatabaseAsync(connectionString);

                await using SqlConnection connection = new(connectionString);
                await connection.OpenAsync();

                foreach (string batch in Batches(LedgerDdl.Drop() + LedgerDdl.Create() + LedgerData.Insert()))
                {
                    await using SqlCommand command = connection.CreateCommand();
                    command.CommandText = batch;
                    await command.ExecuteNonQueryAsync();
                }

                return null;
            }
            catch (SqlException problem)
            {
                return problem.Message;
            }
        }

        /// <summary>Creates the database the connection string names, when it is not there yet.</summary>
        /// <param name="connectionString">The server and database.</param>
        /// <returns>A task that completes when the database exists.</returns>
        /// <remarks>
        ///     A developer pointing this at a local instance should not have to create a database by
        ///     hand first; the fixture is rebuilt on every start anyway.
        /// </remarks>
        private static async Task CreateDatabaseAsync(string connectionString)
        {
            SqlConnectionStringBuilder target = new(connectionString);
            if (string.IsNullOrWhiteSpace(target.InitialCatalog))
            {
                return;
            }

            SqlConnectionStringBuilder master = new(connectionString) { InitialCatalog = "master" };
            await using SqlConnection connection = new(master.ConnectionString);
            await connection.OpenAsync();
            await using SqlCommand command = connection.CreateCommand();

            // Built into a variable and then run: the backend's dynamic-execution form takes only
            // variables and literals, not an expression. The name is a developer's own command-line
            // argument, and the quoting is the backend's own, so a name with a bracket in it still
            // names one database rather than two.
            command.CommandText =
                "DECLARE @sql nvarchar(max) = N'CREATE DATABASE [' + REPLACE(@name, N']', N']]') + N'];'; "
                + "IF DB_ID(@name) IS NULL EXEC (@sql);";

            command.Parameters.AddWithValue("@name", target.InitialCatalog);
            await command.ExecuteNonQueryAsync();
        }

        /// <summary>Splits a script into the batches a driver can send.</summary>
        /// <param name="script">The script.</param>
        /// <returns>The batches.</returns>
        private static IEnumerable<string> Batches(string script)
        {
            StringBuilder batch = new();
            foreach (string line in script.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
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
    }
}
