// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient;
using System.Diagnostics;
using System.Globalization;

namespace Tellma.Core.AspNetCore.IntegrationTests.Infrastructure
{
    /// <summary>
    ///     A lock another session holds on a tenant's database, in an open transaction, until it is
    ///     released, so a request's statement waits on it where the server shows it: a real lock wait,
    ///     never a delay the suite injects.
    /// </summary>
    internal sealed class SqlLock : IAsyncDisposable
    {
        /// <summary>How long a statement may take to reach the lock.</summary>
        private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

        private readonly string _connectionString;
        private readonly SqlConnection _connection;
        private readonly SqlTransaction _transaction;
        private bool _released;

        /// <summary>Initializes a lock over its session's open transaction.</summary>
        /// <param name="connectionString">The connection string of the session and of the monitor that watches it.</param>
        /// <param name="connection">The session.</param>
        /// <param name="transaction">The transaction holding the lock.</param>
        /// <param name="sessionId">The session's id on the server.</param>
        private SqlLock(string connectionString, SqlConnection connection, SqlTransaction transaction, short sessionId)
        {
            _connectionString = connectionString;
            _connection = connection;
            _transaction = transaction;
            SessionId = sessionId;
        }

        /// <summary>The holding session's id on the server.</summary>
        public short SessionId { get; }

        /// <summary>The test's cancellation.</summary>
        private static CancellationToken Token => TestContext.Current.CancellationToken;

        /// <summary>Opens a session, begins a transaction and runs a statement that takes the lock.</summary>
        /// <param name="connectionString">The tenant's database.</param>
        /// <param name="statement">The statement that takes the lock and keeps it until the transaction ends.</param>
        /// <returns>The held lock.</returns>
        public static async Task<SqlLock> HoldAsync(string connectionString, string statement)
        {
            // A pool of its own, so releasing the lock never hands its session to the host.
            string unpooled = new SqlConnectionStringBuilder(connectionString) { Pooling = false }.ConnectionString;
            SqlConnection connection = new(unpooled);
            await connection.OpenAsync(Token);
            var transaction = (SqlTransaction)await connection.BeginTransactionAsync(Token);
            await using (SqlCommand command = new(statement, connection, transaction))
            {
                _ = await command.ExecuteNonQueryAsync(Token);
            }

            await using SqlCommand spid = new("SELECT @@SPID;", connection, transaction);
            short id = Convert.ToInt16(await spid.ExecuteScalarAsync(Token), CultureInfo.InvariantCulture);
            return new SqlLock(unpooled, connection, transaction, id);
        }

        /// <summary>The statements other sessions are running while waiting on the lock.</summary>
        /// <returns>The waiting statements' text.</returns>
        public async Task<List<string>> WaitersAsync()
        {
            await using SqlConnection monitor = new(_connectionString);
            await monitor.OpenAsync(Token);
            await using SqlCommand command = new(
                """
                SELECT T.[text]
                  FROM [sys].[dm_exec_requests] AS R
                 CROSS APPLY [sys].[dm_exec_sql_text](R.[sql_handle]) AS T
                 WHERE R.[blocking_session_id] = @session;
                """,
                monitor);
            _ = command.Parameters.AddWithValue("@session", SessionId);
            await using SqlDataReader reader = await command.ExecuteReaderAsync(Token);
            List<string> waiting = [];
            while (await reader.ReadAsync(Token))
            {
                waiting.Add(reader.GetString(0));
            }

            return waiting;
        }

        /// <summary>Waits until some session waits on the lock.</summary>
        /// <returns>The first waiting statement's text.</returns>
        public async Task<string> AwaitWaiterAsync()
        {
            var elapsed = Stopwatch.StartNew();
            while (true)
            {
                List<string> waiting = await WaitersAsync();
                if (waiting.Count > 0)
                {
                    return waiting[0];
                }

                Assert.True(elapsed.Elapsed < Patience, "No statement reached the held lock.");
                await Task.Delay(TimeSpan.FromMilliseconds(50), Token);
            }
        }

        /// <summary>Waits until no session waits on the lock any more, while it is still held.</summary>
        /// <returns>A task that completes when the last waiter is gone.</returns>
        public async Task AwaitNoWaiterAsync()
        {
            var elapsed = Stopwatch.StartNew();
            while ((await WaitersAsync()).Count > 0)
            {
                Assert.True(elapsed.Elapsed < Patience, "A statement still waits on the held lock.");
                await Task.Delay(TimeSpan.FromMilliseconds(50), Token);
            }
        }

        /// <summary>Releases the lock: the transaction is rolled back, undoing whatever took it.</summary>
        /// <returns>A task that completes when the lock is released.</returns>
        public async Task ReleaseAsync()
        {
            if (!_released)
            {
                _released = true;
                await _transaction.RollbackAsync(CancellationToken.None);
            }
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            await ReleaseAsync();
            await _transaction.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
