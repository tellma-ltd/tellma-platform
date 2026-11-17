// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient;
using System.Data;
using System.Globalization;

namespace Tellma.Testing.Support.Sql
{
    /// <summary>
    ///     The one way a test fixture drops the databases it created on an existing server. One
    ///     connection to <c>master</c> is opened, and then the process's pooled connections are
    ///     closed, so no drop waits to roll back a session the suite left idle in the pool. Each
    ///     database is then set to single-user, which rolls back any session still in it, and
    ///     dropped, in one statement run under <see cref="SqlDatabaseCreation.CommandTimeout" />: a
    ///     loaded server can hold a drop past the 30-second default, as it holds a creation. A drop
    ///     that meets a lock or a session it could not take over
    ///     (<see cref="IsTransient(IReadOnlyCollection{int})" />) runs again after the waits of
    ///     <see cref="CreateDatabaseRetry.Default" />, and each rerun closes the pooled connections
    ///     again first. A database that cannot be dropped does not stop the others: every database
    ///     left behind is reported together at the end.
    /// </summary>
    public static class SqlDatabaseDrop
    {
        /// <summary>The error that follows a failed <c>ALTER DATABASE</c>, held or not; it decides nothing alone.</summary>
        private const int AlterDatabaseFailedErrorNumber = 5069;

        /// <summary>
        ///     The errors that mean the database was held for a moment, by a session or a lock the
        ///     statement could not take over, so a rerun may succeed.
        /// </summary>
        /// <remarks>
        ///     <list type="bullet">
        ///         <item><description>3702: the database is still in use.</description></item>
        ///         <item><description>5061: a lock could not be placed on the database.</description></item>
        ///         <item><description>5064: the database is single-user and another session holds it.</description></item>
        ///         <item><description>924: the database is open and admits one user at a time.</description></item>
        ///         <item><description>952: the database is in transition.</description></item>
        ///         <item><description>1205: the statement was chosen as a deadlock victim.</description></item>
        ///     </list>
        /// </remarks>
        private static readonly int[] HeldErrorNumbers = [3702, 5061, 5064, 924, 952, 1205];

        /// <summary>
        ///     Drops databases, each on its own, over one connection to the server's <c>master</c>.
        ///     A database that does not exist is passed over. Retries go to the running test's output,
        ///     or to a diagnostic message outside a test.
        /// </summary>
        /// <param name="masterConnectionString">A connection string to the server's <c>master</c> database.</param>
        /// <param name="databases">The databases' exact names.</param>
        /// <param name="cancellationToken">Cancels the drops and the waits between runs.</param>
        /// <returns>A task that completes when every database is gone.</returns>
        /// <exception cref="AggregateException">
        ///     A database could not be dropped, the connection could not be opened, or it was lost
        ///     before every database was tried. The message names each database left behind, and each
        ///     failure is an inner exception.
        /// </exception>
        public static Task DropAllAsync(string masterConnectionString, IReadOnlyList<string> databases, CancellationToken cancellationToken = default)
        {
            return DropAllAsync(
                masterConnectionString,
                databases,
                CreateDatabaseRetry.Default,
                SqlDatabaseCreation.WriteToTestOutput,
                static (command, token) => command.ExecuteNonQueryAsync(token),
                SqlConnection.ClearAllPools,
                cancellationToken);
        }

        /// <summary>Whether a failed drop may succeed when run again.</summary>
        /// <param name="exception">The failure.</param>
        /// <returns>True for a <see cref="SqlException" /> whose errors <see cref="IsTransient(IReadOnlyCollection{int})" /> accepts.</returns>
        public static bool IsTransient(Exception exception)
        {
            ArgumentNullException.ThrowIfNull(exception);
            return exception is SqlException sql && IsTransient([.. sql.Errors.Cast<SqlError>().Select(static error => error.Number)]);
        }

        /// <summary>
        ///     Whether the errors one failed drop reported mean only that the database was held: at
        ///     least one of 3702, 5061, 5064, 924, 952 or 1205, and nothing besides them and the 5069
        ///     that follows a failed <c>ALTER DATABASE</c>.
        /// </summary>
        /// <param name="errorNumbers">The numbers of every error the statement reported.</param>
        /// <returns>True when a rerun may succeed.</returns>
        public static bool IsTransient(IReadOnlyCollection<int> errorNumbers)
        {
            ArgumentNullException.ThrowIfNull(errorNumbers);
            return errorNumbers.Any(static number => HeldErrorNumbers.Contains(number))
                && errorNumbers.All(static number => number == AlterDatabaseFailedErrorNumber || HeldErrorNumbers.Contains(number));
        }

        /// <summary>
        ///     <see cref="DropAllAsync(string, IReadOnlyList{string}, CancellationToken)" /> over its
        ///     seams: the retry policy, the log, what runs a drop's command and what closes the pooled
        ///     connections, so the path is proven without a server or a wait.
        /// </summary>
        /// <param name="masterConnectionString">A connection string to the server's <c>master</c> database.</param>
        /// <param name="databases">The databases' exact names.</param>
        /// <param name="retry">When a held drop runs again.</param>
        /// <param name="log">Receives one line per retry and one on giving up.</param>
        /// <param name="execute">Runs a drop's command, which carries the statement and the bound; it gets the cancellation token.</param>
        /// <param name="clearPools">Closes the process's idle pooled connections.</param>
        /// <param name="cancellationToken">Cancels the drops and the waits between runs.</param>
        /// <returns>A task that completes when every database is gone.</returns>
        /// <exception cref="AggregateException">A database was left behind; see the public overload.</exception>
        internal static async Task DropAllAsync(
            string masterConnectionString,
            IReadOnlyList<string> databases,
            CreateDatabaseRetry retry,
            Action<string> log,
            Func<SqlCommand, CancellationToken, Task> execute,
            Action clearPools,
            CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(masterConnectionString);
            ArgumentNullException.ThrowIfNull(databases);
            ArgumentNullException.ThrowIfNull(retry);
            ArgumentNullException.ThrowIfNull(log);
            ArgumentNullException.ThrowIfNull(execute);
            ArgumentNullException.ThrowIfNull(clearPools);
            if (databases.Count == 0)
            {
                return;
            }

            SqlConnection connection = new(masterConnectionString);
            await using (connection.ConfigureAwait(false))
            {
                // --- The connection opens before the pools are cleared, so it can reuse a warm pooled
                // login rather than a fresh one. A connection that cannot be opened leaves every
                // database untried, and they are all named ---
                try
                {
                    await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
                {
                    throw Report(databases.Count, [], [exception], [.. databases], "the connection to the server could not be opened, its failure the inner exception");
                }

                // --- The process's idle pooled connections close next, so the drops find none of its
                // sessions to roll back. On LocalDB, rolling back the sessions in a database costs its
                // drop about three seconds however few there are, so a suite's cleanup ran for minutes,
                // against a fraction of a second per drop once the pools are clear ---
                clearPools();

                await DropEachAsync(
                    databases,
                    (database, token) => DropAsync(connection, database, retry, log, execute, clearPools, token),
                    () => connection.State == ConnectionState.Open,
                    cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        ///     The drop statement: when the database exists, set it to single-user with its sessions
        ///     rolled back, then drop it. The name is bracketed, and quoted as a literal for
        ///     <c>DB_ID</c>, so a rerun after a partial run is safe.
        /// </summary>
        /// <param name="database">The database's exact name.</param>
        /// <returns>The statement.</returns>
        /// <exception cref="ArgumentException">The name is blank.</exception>
        internal static string DropStatement(string database)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(database);
            string literal = database.Replace("'", "''", StringComparison.Ordinal);
            string quoted = SqlDatabaseCreation.Quote(database);
            return $"IF DB_ID(N'{literal}') IS NOT NULL BEGIN ALTER DATABASE {quoted} SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE {quoted}; END";
        }

        /// <summary>
        ///     Runs a drop for each database in order. A failed drop is recorded and the next one runs,
        ///     until the connection is lost; then the rest are left untried. The failures are thrown
        ///     together at the end. A cancellation surfaces at once.
        /// </summary>
        /// <param name="databases">The databases' exact names.</param>
        /// <param name="drop">Drops one database; it gets the cancellation token.</param>
        /// <param name="connected">Whether the connection the drops run on is still open.</param>
        /// <param name="cancellationToken">Cancels the drops.</param>
        /// <returns>A task that completes when every drop succeeded.</returns>
        /// <exception cref="AggregateException">A drop failed, or databases were left untried.</exception>
        internal static async Task DropEachAsync(
            IReadOnlyList<string> databases,
            Func<string, CancellationToken, Task> drop,
            Func<bool> connected,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(databases);
            ArgumentNullException.ThrowIfNull(drop);
            ArgumentNullException.ThrowIfNull(connected);

            List<string> failed = [];
            List<Exception> failures = [];
            int tried = 0;
            foreach (string database in databases)
            {
                // --- A lost connection fails every later drop the same way: stop, and report the rest untried ---
                if (!connected())
                {
                    break;
                }

                tried++;
                try
                {
                    await drop(database, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
                {
                    failed.Add(database);
                    failures.Add(exception);
                }
            }

            string[] untried = [.. databases.Skip(tried)];
            if (failed.Count > 0 || untried.Length > 0)
            {
                throw Report(databases.Count, [.. failed], [.. failures], untried, "the connection to the server was lost");
            }
        }

        /// <summary>
        ///     Drops one database under a retry policy, on an open connection to <c>master</c>, its
        ///     command carrying the statement and <see cref="SqlDatabaseCreation.CommandTimeout" />.
        ///     A held drop runs again under <see cref="IsTransient(Exception)" />, and each rerun
        ///     first closes the process's idle pooled connections, since one of them may be the
        ///     session that held the database. Any other failure surfaces at once.
        /// </summary>
        /// <param name="master">A connection to the server's <c>master</c> database; open, unless <paramref name="execute" /> never uses it.</param>
        /// <param name="database">The database's exact name.</param>
        /// <param name="retry">When a held drop runs again.</param>
        /// <param name="log">Receives one line per retry and one on giving up.</param>
        /// <param name="execute">Runs the command; it gets the cancellation token.</param>
        /// <param name="clearPools">Closes the process's idle pooled connections.</param>
        /// <param name="cancellationToken">Cancels the drop and the waits between runs.</param>
        /// <returns>A task that completes when the database is gone.</returns>
        internal static Task DropAsync(
            SqlConnection master,
            string database,
            CreateDatabaseRetry retry,
            Action<string> log,
            Func<SqlCommand, CancellationToken, Task> execute,
            Action clearPools,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(master);
            ArgumentNullException.ThrowIfNull(retry);
            ArgumentNullException.ThrowIfNull(execute);
            ArgumentNullException.ThrowIfNull(clearPools);

            string statement = DropStatement(database);
            int runs = 0;
            return retry.RunAsync(
                async token =>
                {
                    // --- A rerun first closes the pooled connections that may hold the database ---
                    if (runs++ > 0)
                    {
                        clearPools();
                    }

                    SqlCommand command = SqlDatabaseCreation.CreateCommand(master, statement);
                    await using (command.ConfigureAwait(false))
                    {
                        await execute(command, token).ConfigureAwait(false);
                    }
                },
                $"DROP DATABASE {SqlDatabaseCreation.Quote(database)}",
                log,
                IsTransient,
                Task.Delay,
                Random.Shared.NextDouble,
                cancellationToken);
        }

        /// <summary>The report of a cleanup that left databases behind: each one named, each failure an inner exception.</summary>
        /// <param name="total">How many databases the cleanup was given.</param>
        /// <param name="failed">The databases whose drop failed.</param>
        /// <param name="failures">The failures: one per failed database, or the connection's own.</param>
        /// <param name="untried">The databases never tried.</param>
        /// <param name="untriedReason">Why the untried ones were not tried.</param>
        /// <returns>The exception.</returns>
        private static AggregateException Report(
            int total, string[] failed, Exception[] failures, string[] untried, string untriedReason)
        {
            string message = string.Create(
                CultureInfo.InvariantCulture,
                $"{failed.Length + untried.Length} of the {total} test databases were not dropped and must be dropped by hand.");
            if (failed.Length > 0)
            {
                message += $" Failed, each with its failure as an inner exception: {Names(failed)}.";
            }

            if (untried.Length > 0)
            {
                message += $" Not tried, as {untriedReason}: {Names(untried)}.";
            }

            return new AggregateException(message, failures);
        }

        /// <summary>Names, each bracketed, joined by commas.</summary>
        /// <param name="names">The names.</param>
        /// <returns>The list.</returns>
        private static string Names(IEnumerable<string> names)
        {
            return string.Join(", ", names.Select(SqlDatabaseCreation.Quote));
        }
    }
}
