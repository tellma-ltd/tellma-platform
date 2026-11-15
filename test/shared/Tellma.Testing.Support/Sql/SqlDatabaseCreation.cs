// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient;
using Tellma.Core.Abstractions.Hosting;
using Xunit;

namespace Tellma.Testing.Support.Sql
{
    /// <summary>
    ///     The one way test fixtures create a database: a bare <c>CREATE DATABASE</c> in a stated
    ///     collation, run in the server's <c>master</c> under a bound long enough for a server busy
    ///     with a whole suite's creations, and run again while it fails on the <c>model</c> lock that
    ///     parallel creations queue on (<see cref="CreateDatabaseRetry" />). Each retry is written to
    ///     the running test's output, or as a diagnostic message outside a test.
    /// </summary>
    public static class SqlDatabaseCreation
    {
        /// <summary>
        ///     How long one <c>CREATE DATABASE</c> may run. The server alone may wait about 25 seconds
        ///     for the <c>model</c> lock before the copy starts, so the 30-second default cannot hold
        ///     on a loaded host.
        /// </summary>
        public static readonly TimeSpan CommandTimeout = TimeSpan.FromMinutes(5);

        /// <summary>Creates a database, retrying a <c>model</c> lock under <see cref="CreateDatabaseRetry.Default" />.</summary>
        /// <param name="masterConnectionString">A connection string to the server's <c>master</c> database.</param>
        /// <param name="database">The new database's exact name; the caller drops it.</param>
        /// <param name="collation">The collation; the platform collation unless a test needs another.</param>
        /// <param name="cancellationToken">Cancels the creation and the waits between runs.</param>
        /// <returns>A task that completes when the database exists.</returns>
        public static Task CreateAsync(
            string masterConnectionString, string database, string collation = DatabaseCollations.Platform, CancellationToken cancellationToken = default)
        {
            return CreateAsync(masterConnectionString, database, collation, CreateDatabaseRetry.Default, WriteToTestOutput, cancellationToken);
        }

        /// <summary>Creates a database under a given policy, logging each retry to a given sink.</summary>
        /// <param name="masterConnectionString">A connection string to the server's <c>master</c> database.</param>
        /// <param name="database">The new database's exact name; the caller drops it.</param>
        /// <param name="collation">The collation.</param>
        /// <param name="retry">When a failed creation runs again.</param>
        /// <param name="log">Receives one line per retry and one on giving up.</param>
        /// <param name="cancellationToken">Cancels the creation and the waits between runs.</param>
        /// <returns>A task that completes when the database exists.</returns>
        /// <exception cref="ArgumentException">The name or the collation is blank, or the collation is not a plain word.</exception>
        public static Task CreateAsync(
            string masterConnectionString,
            string database,
            string collation,
            CreateDatabaseRetry retry,
            Action<string> log,
            CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(masterConnectionString);
            ArgumentNullException.ThrowIfNull(retry);
            ArgumentNullException.ThrowIfNull(log);

            string statement = CreateStatement(database, collation);
            return retry.RunAsync(
                token => ExecuteAsync(masterConnectionString, [statement], token),
                $"CREATE DATABASE {Quote(database)}",
                log,
                cancellationToken);
        }

        /// <summary>
        ///     Runs the statements that prepare a database a fixture just created, such as
        ///     <c>ALTER DATABASE … SET READ_COMMITTED_SNAPSHOT ON</c> or a role, each a batch of its own,
        ///     in order on one connection, so a later batch sees the session state an earlier one set,
        ///     each under <see cref="CommandTimeout" />. A busy server can hold such a statement past
        ///     the 30-second default as it holds a creation. Nothing is retried: these statements fail
        ///     only for real.
        /// </summary>
        /// <param name="connectionString">The connection string: <c>master</c> for an <c>ALTER DATABASE</c>, the database itself for what lives in it.</param>
        /// <param name="cancellationToken">Cancels the statements.</param>
        /// <param name="batches">The statements, in order.</param>
        /// <returns>A task that completes when every statement ran.</returns>
        public static Task PrepareAsync(string connectionString, CancellationToken cancellationToken, params string[] batches)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
            ArgumentNullException.ThrowIfNull(batches);
            return ExecuteAsync(connectionString, batches, cancellationToken);
        }

        /// <summary>
        ///     The default sink: the running test's output, which a failing test's report carries, or a
        ///     diagnostic message when no test is running, as in a fixture's initialization.
        /// </summary>
        /// <param name="message">The line.</param>
        public static void WriteToTestOutput(string message)
        {
            ITestContext context = TestContext.Current;
            if (context.TestOutputHelper is { } output)
            {
                output.WriteLine(message);
            }
            else
            {
                context.SendDiagnosticMessage(message);
            }
        }

        /// <summary>
        ///     The creation statement: <c>CREATE DATABASE [name] COLLATE collation;</c>, the name
        ///     bracketed and the collation checked, since it is spliced in unquoted.
        /// </summary>
        /// <param name="database">The database's exact name.</param>
        /// <param name="collation">The collation: ASCII letters, digits and underscores only.</param>
        /// <returns>The statement.</returns>
        /// <exception cref="ArgumentException">The name or the collation is blank, or the collation holds any other character.</exception>
        internal static string CreateStatement(string database, string collation)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(database);
            ArgumentException.ThrowIfNullOrWhiteSpace(collation);
            return collation.All(static character => char.IsAsciiLetterOrDigit(character) || character == '_')
                ? $"CREATE DATABASE {Quote(database)} COLLATE {collation};"
                : throw new ArgumentException("A collation name holds only ASCII letters, digits and underscores.", nameof(collation));
        }

        /// <summary>A command for one statement on a connection, under the creation bound.</summary>
        /// <param name="connection">The connection; open or not.</param>
        /// <param name="statement">The statement.</param>
        /// <returns>The command; the caller disposes it.</returns>
        internal static SqlCommand CreateCommand(SqlConnection connection, string statement)
        {
            SqlCommand command = connection.CreateCommand();
            command.CommandText = statement;
            command.CommandTimeout = (int)CommandTimeout.TotalSeconds;
            return command;
        }

        /// <summary>Runs statements, each a batch of its own, in order on one fresh connection, under the creation bound.</summary>
        /// <param name="connectionString">The connection string.</param>
        /// <param name="statements">The statements.</param>
        /// <param name="cancellationToken">Cancels the statements.</param>
        /// <returns>A task that completes when every statement ran.</returns>
        private static async Task ExecuteAsync(string connectionString, IReadOnlyList<string> statements, CancellationToken cancellationToken)
        {
            SqlConnection connection = new(connectionString);
            await using (connection.ConfigureAwait(false))
            {
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                foreach (string statement in statements)
                {
                    SqlCommand command = CreateCommand(connection, statement);
                    await using (command.ConfigureAwait(false))
                    {
                        _ = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                    }
                }
            }
        }

        /// <summary>A name in brackets, its closing brackets doubled.</summary>
        /// <param name="name">The name.</param>
        /// <returns>The quoted name.</returns>
        private static string Quote(string name)
        {
            return "[" + name.Replace("]", "]]", StringComparison.Ordinal) + "]";
        }
    }
}
