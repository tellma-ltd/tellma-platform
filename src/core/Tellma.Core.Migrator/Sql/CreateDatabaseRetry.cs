// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Tellma.Core.Migrator.Sql
{
    /// <summary>
    ///     When the migrator runs a <c>CREATE DATABASE</c> again: only when SQL Server reports that it
    ///     could not lock the <c>model</c> database (error 1807), a bounded number of times, after
    ///     waits that double from a base up to a ceiling and vary by a quarter either way. Each retry,
    ///     and giving up, is logged. Every other failure surfaces at once.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <c>CREATE DATABASE</c> copies <c>model</c> under an exclusive lock on it, so creations on
    ///         one server queue on that lock: a run's parallel provisionings, other runs, another
    ///         distribution's migrator, or a session someone left open in <c>model</c>. A creation that
    ///         cannot take the lock within the server's own wait (about 25 seconds) fails with 1807,
    ///         "Retry the operation later", followed by 1802, "CREATE DATABASE failed", and creates
    ///         nothing, so running the same statement again is safe. Any other error, a 1802 without a
    ///         1807 among them included, is a real failure.
    ///     </para>
    ///     <para>
    ///         A command timeout is not retried: the client gives up on a statement the server may
    ///         still finish, so another run could meet the database the first one created. The
    ///         creation's command bound is long enough instead, and the next migrator run converges.
    ///     </para>
    /// </remarks>
    internal sealed class CreateDatabaseRetry
    {
        /// <summary>The error SQL Server raises when <c>CREATE DATABASE</c> could not lock <c>model</c>.</summary>
        public const int ModelLockedErrorNumber = 1807;

        /// <summary>The error that follows any failed <c>CREATE DATABASE</c>, 1807 included.</summary>
        public const int CreateDatabaseFailedErrorNumber = 1802;

        /// <summary>How many times a creation runs when the configuration does not say, the first run included.</summary>
        public const int DefaultMaxAttempts = 5;

        /// <summary>The wait before the first retry, before jitter: two seconds.</summary>
        public static readonly TimeSpan DefaultBaseDelay = TimeSpan.FromSeconds(2);

        /// <summary>The ceiling the doubling waits stop at, before jitter: fifteen seconds.</summary>
        public static readonly TimeSpan DefaultMaxDelay = TimeSpan.FromSeconds(15);

        /// <summary>Creates a policy with the default waits.</summary>
        /// <param name="maxAttempts">How many times the creation runs at most, the first run included; at least 1.</param>
        /// <exception cref="ArgumentOutOfRangeException">The bound is below 1.</exception>
        public CreateDatabaseRetry(int maxAttempts)
            : this(maxAttempts, DefaultBaseDelay, DefaultMaxDelay)
        {
        }

        /// <summary>Creates a policy.</summary>
        /// <param name="maxAttempts">How many times the creation runs at most, the first run included; at least 1.</param>
        /// <param name="baseDelay">The wait before the first retry, before jitter; positive.</param>
        /// <param name="maxDelay">The ceiling the doubling waits stop at, before jitter; at least <paramref name="baseDelay" />.</param>
        /// <exception cref="ArgumentOutOfRangeException">A bound is outside its domain.</exception>
        public CreateDatabaseRetry(int maxAttempts, TimeSpan baseDelay, TimeSpan maxDelay)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(maxAttempts, 1);
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(baseDelay, TimeSpan.Zero);
            ArgumentOutOfRangeException.ThrowIfLessThan(maxDelay, baseDelay);
            MaxAttempts = maxAttempts;
            BaseDelay = baseDelay;
            MaxDelay = maxDelay;
        }

        /// <summary>How many times the creation runs at most, the first run included.</summary>
        public int MaxAttempts { get; }

        /// <summary>The wait before the first retry, before jitter.</summary>
        public TimeSpan BaseDelay { get; }

        /// <summary>The ceiling the doubling waits stop at, before jitter.</summary>
        public TimeSpan MaxDelay { get; }

        /// <summary>Whether a failure is the <c>model</c> lock the policy retries.</summary>
        /// <param name="exception">The failure.</param>
        /// <returns>True for a <see cref="SqlException" /> whose errors <see cref="IsTransient(IReadOnlyCollection{int})" /> accepts.</returns>
        public static bool IsTransient(Exception exception)
        {
            ArgumentNullException.ThrowIfNull(exception);
            return exception is SqlException sql && IsTransient([.. sql.Errors.Cast<SqlError>().Select(static error => error.Number)]);
        }

        /// <summary>
        ///     Whether the errors one failed statement reported are the <c>model</c> lock alone: at
        ///     least one 1807, and nothing besides 1807 and the 1802 that follows it.
        /// </summary>
        /// <param name="errorNumbers">The numbers of every error the statement reported.</param>
        /// <returns>True when running the statement again may succeed.</returns>
        public static bool IsTransient(IReadOnlyCollection<int> errorNumbers)
        {
            ArgumentNullException.ThrowIfNull(errorNumbers);
            return errorNumbers.Contains(ModelLockedErrorNumber)
                && errorNumbers.All(static number => number is ModelLockedErrorNumber or CreateDatabaseFailedErrorNumber);
        }

        /// <summary>
        ///     The wait before a retry: the base doubled once per earlier retry, held at the ceiling,
        ///     then scaled by between three quarters and five quarters, so creations that failed
        ///     together do not all run again at the same moment.
        /// </summary>
        /// <param name="retry">The retry, from 1.</param>
        /// <param name="jitter">A value in [0, 1) that places the wait in its range; 0.5 is the centre.</param>
        /// <returns>The wait.</returns>
        /// <exception cref="ArgumentOutOfRangeException">The retry is below 1, or the jitter is outside [0, 1).</exception>
        public TimeSpan Delay(int retry, double jitter)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(retry, 1);
            if (double.IsNaN(jitter) || jitter is < 0 or >= 1)
            {
                throw new ArgumentOutOfRangeException(nameof(jitter), jitter, "The jitter must be in [0, 1).");
            }

            // Doubling overflows long before the retry count matters, so the ceiling is applied first.
            double ticks = Math.Min(MaxDelay.Ticks, BaseDelay.Ticks * Math.Pow(2, Math.Min(retry - 1, 62)));
            return TimeSpan.FromTicks((long)(ticks * (0.75 + (0.5 * jitter))));
        }

        /// <summary>
        ///     Runs a database's creation, and runs it again after a wait while it fails with the
        ///     <c>model</c> lock and attempts remain. Each retry, and giving up, is logged.
        /// </summary>
        /// <param name="creation">The creation; it gets the cancellation token.</param>
        /// <param name="logger">The migrator's logger.</param>
        /// <param name="database">The database being created, for the log.</param>
        /// <param name="server">The server, for the log.</param>
        /// <param name="cancellationToken">Cancels the creation and the waits.</param>
        /// <returns>A task that completes when a run succeeded; it faults with the last run's own exception otherwise.</returns>
        public Task RunAsync(Func<CancellationToken, Task> creation, ILogger logger, string database, string server, CancellationToken cancellationToken)
        {
            return RunAsync(creation, logger, database, server, IsTransient, Task.Delay, Random.Shared.NextDouble, cancellationToken);
        }

        /// <summary>
        ///     <see cref="RunAsync(Func{CancellationToken, Task}, ILogger, string, string, CancellationToken)" />
        ///     over its seams: the classifier, the wait and the jitter source, so the loop is proven
        ///     without a server or a clock.
        /// </summary>
        /// <param name="creation">The creation.</param>
        /// <param name="logger">The migrator's logger.</param>
        /// <param name="database">The database being created, for the log.</param>
        /// <param name="server">The server, for the log.</param>
        /// <param name="isTransient">Whether a failure may be retried.</param>
        /// <param name="wait">Waits for a delay; honours the token.</param>
        /// <param name="jitter">Returns a value in [0, 1).</param>
        /// <param name="cancellationToken">Cancels the creation and the waits.</param>
        /// <returns>A task that completes when a run succeeded; it faults with the last run's own exception otherwise.</returns>
        public async Task RunAsync(
            Func<CancellationToken, Task> creation,
            ILogger logger,
            string database,
            string server,
            Func<Exception, bool> isTransient,
            Func<TimeSpan, CancellationToken, Task> wait,
            Func<double> jitter,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(creation);
            ArgumentNullException.ThrowIfNull(logger);
            ArgumentNullException.ThrowIfNull(database);
            ArgumentNullException.ThrowIfNull(server);
            ArgumentNullException.ThrowIfNull(isTransient);
            ArgumentNullException.ThrowIfNull(wait);
            ArgumentNullException.ThrowIfNull(jitter);

            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    await creation(cancellationToken).ConfigureAwait(false);
                    return;
                }
                catch (Exception exception) when (!cancellationToken.IsCancellationRequested && isTransient(exception))
                {
                    // --- Out of attempts: say so, and surface the server's own error unchanged ---
                    if (attempt >= MaxAttempts)
                    {
                        MigratorLog.DatabaseCreationGaveUp(logger, database, server, attempt);
                        throw;
                    }

                    // --- Another attempt remains: wait, then run again ---
                    TimeSpan delay = Delay(attempt, jitter());
                    MigratorLog.DatabaseCreationRetrying(logger, database, server, attempt, MaxAttempts, delay);
                    await wait(delay, cancellationToken).ConfigureAwait(false);
                }
            }
        }
    }
}
