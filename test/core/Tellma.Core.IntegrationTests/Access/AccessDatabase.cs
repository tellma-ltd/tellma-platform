// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient;
using Tellma.Testing.Entities;

namespace Tellma.Core.IntegrationTests.Access
{
    /// <summary>
    ///     One fixture database for a test class, created once by the fixture's migrations. A test that
    ///     writes either rolls its writes back or removes what it committed, so the seeded rows stay
    ///     the only rows every other test sees.
    /// </summary>
    /// <param name="sql">The suite's server.</param>
    public sealed class AccessDatabase(SqlServerFixture sql) : IAsyncLifetime
    {
        /// <summary>The host, once created.</summary>
        private FixtureTenant? _tenant;

        /// <summary>The database's connection string.</summary>
        public string ConnectionString => (_tenant ?? throw new InvalidOperationException("The database is not created yet.")).ConnectionString;

        /// <inheritdoc />
        public async ValueTask InitializeAsync()
        {
            // The tests write with raw statements, never through a batch, so no audit is wired.
            _tenant = await FixtureTenant.CreateAsync(
                sql.MasterConnectionString,
                sql.NewDatabaseName("fx_access"),
                audit: false,
                cancellationToken: TestContext.Current.CancellationToken);
        }

        /// <inheritdoc />
        public ValueTask DisposeAsync()
        {
            return _tenant?.DisposeAsync() ?? ValueTask.CompletedTask;
        }

        /// <summary>Runs a query and returns its first column, row by row, as text.</summary>
        /// <param name="query">The query.</param>
        /// <returns>The values, a null shown as <c>NULL</c>.</returns>
        public async Task<List<string>> ColumnAsync(string query)
        {
            SqlConnection connection = new(ConnectionString);
            await using (connection.ConfigureAwait(false))
            {
                await connection.OpenAsync(TestContext.Current.CancellationToken).ConfigureAwait(false);
                SqlCommand command = connection.CreateCommand();
                await using (command.ConfigureAwait(false))
                {
                    command.CommandText = query;
                    SqlDataReader reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken).ConfigureAwait(false);
                    await using (reader.ConfigureAwait(false))
                    {
                        List<string> values = [];
                        while (await reader.ReadAsync(TestContext.Current.CancellationToken).ConfigureAwait(false))
                        {
                            values.Add(reader.IsDBNull(0) ? "NULL" : Convert.ToString(reader.GetValue(0), System.Globalization.CultureInfo.InvariantCulture)!);
                        }

                        return values;
                    }
                }
            }
        }
    }
}
