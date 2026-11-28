// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient;
using System.Globalization;

namespace Tellma.Testing.Support.Sql
{
    /// <summary>
    ///     When a test fixture's <c>CREATE DATABASE</c> is tried again: only when SQL Server reports
    ///     that it could not lock the <c>model</c> database (error 1807), a bounded number of times,
    ///     after waits that double from a base up to a ceiling and vary by a quarter either way. Every
    ///     other failure surfaces at once.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <c>CREATE DATABASE</c> copies <c>model</c> under an exclusive lock on it, so the creations
    ///         of a suite's parallel tests queue on that lock. A creation that cannot take it within the
    ///         server's own wait (about 20 to 30 seconds) fails with 1807, "Retry the operation later",
    ///         followed by 1802, "CREATE DATABASE failed". Nothing was created, so running the same
    ///         statement again is safe. Any other error, a 1802 without a 1807 among them, is a real
    ///         failure.
    ///     </para>
    ///     <para>
    ///         A command timeout is not retried: the client gives up on a statement the server may still
    ///         finish, so a second run could meet the database the first one created. The creation's
    ///         command bound is long enough instead.
    ///     </para>
    ///     <para>
    ///         The same waits pace the drops at the end of a suite (<see cref="SqlDatabaseDrop" />),
    ///         which run this loop under their own classifier: a database held for a moment by a
    ///         session or a lock.
    ///     </para>
    /// </remarks>
    public sealed class CreateDatabaseRetry
    {
        /// <summary>The error SQL Server raises when <c>CREATE DATABASE</c> could not lock <c>model</c>.</summary>
        public const int ModelLockedErrorNumber = 1807;

        /// <summary>The error that follows any failed <c>CREATE DATABASE</c>, 1807 included.</summary>
        public const int CreateDatabaseFailedErrorNumber = 1802;

        /// <summary>
        ///     Creates a policy.
        /// </summary>
        /// <param name="maxAttempts">How many times the operation runs at most, the first run included; at least 1.</param>
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

        /// <summary>
        ///     The fixtures' policy: six runs, waits of about 1, 2, 4, 8 and 10 seconds between them.
        ///     With the server's own wait on each run, a creation keeps trying for about three minutes.
        /// </summary>
        public static CreateDatabaseRetry Default { get; } = new(6, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(10));

        /// <summary>How many times the operation runs at most, the first run included.</summary>
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
            return exception is SqlException sql && IsTransient(ErrorNumbers(sql));
        }

        /// <summary>
        ///     Whether the errors one failed statement reported are the <c>model</c> lock alone: at
        ///     least one 1807, and nothing besides 1807 and the 1802 that follows it.
        /// </summary>
        /// <param name="errorNumbers">The numbers of every error the statement reported.</param>
        /// <returns>True when a retry may succeed.</returns>
        public static bool IsTransient(IReadOnlyCollection<int> errorNumbers)
        {
            ArgumentNullException.ThrowIfNull(errorNumbers);
            return errorNumbers.Contains(ModelLockedErrorNumber)
                && errorNumbers.All(static number => number is ModelLockedErrorNumber or CreateDatabaseFailedErrorNumber);
        }

        /// <summary>
        ///     The wait before a retry: the base doubled once per earlier retry, held at the ceiling,
        ///     then scaled by between three quarters and five quarters.
        /// </summary>
        /// <param name="retry">The retry, from 1.</param>
        /// <param name="jitter">A value in [0, 1) that places the wait in its range; 0.5 is the centre.</param>
        /// <returns>The wait.</returns>
        /// <exception cref="ArgumentOutOfRangeException">The retry is below 1, or the jitter is outside [0, 1).</exception>
        public TimeSpan Delay(int retry, double jitter)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(retry, 1);
            if (jitter is < 0 or >= 1 || double.IsNaN(jitter))
            {
                throw new ArgumentOutOfRangeException(nameof(jitter), jitter, "The jitter must be in [0, 1).");
            }

            // Doubling in ticks overflows long before the retry count matters, so the ceiling is applied first.
            double ticks = Math.Min(MaxDelay.Ticks, BaseDelay.Ticks * Math.Pow(2, Math.Min(retry - 1, 62)));
            return TimeSpan.FromTicks((long)(ticks * (0.75 + (0.5 * jitter))));
        }

        /// <summary>
        ///     Runs an operation, and runs it again after a wait while it fails with the <c>model</c>
        ///     lock and runs remain. Each retry, and giving up, is logged.
        /// </summary>
        /// <param name="operation">The operation; it gets the cancellation token.</param>
        /// <param name="description">What the operation does, for the log, such as <c>CREATE DATABASE [name]</c>.</param>
        /// <param name="log">Receives one line per retry and one on giving up.</param>
        /// <param name="cancellationToken">Cancels the operation and the waits.</param>
        /// <returns>A task that completes when a run succeeded; it faults with the last run's exception otherwise.</returns>
        public Task RunAsync(Func<CancellationToken, Task> operation, string description, Action<string> log, CancellationToken cancellationToken)
        {
            return RunAsync(operation, description, log, IsTransient, Task.Delay, Random.Shared.NextDouble, cancellationToken);
        }

        /// <summary>
        ///     <see cref="RunAsync(Func{CancellationToken, Task}, string, Action{string}, CancellationToken)" />
        ///     over its seams: the classifier, the wait and the jitter source, so the loop is proven
        ///     without a server or a clock.
        /// </summary>
        /// <param name="operation">The operation.</param>
        /// <param name="description">What the operation does, for the log.</param>
        /// <param name="log">Receives one line per retry and one on giving up.</param>
        /// <param name="isTransient">Whether a failure may be retried.</param>
        /// <param name="wait">Waits for a delay; honours the token.</param>
        /// <param name="jitter">Returns a value in [0, 1).</param>
        /// <param name="cancellationToken">Cancels the operation and the waits.</param>
        /// <returns>A task that completes when a run succeeded; it faults with the last run's exception otherwise.</returns>
        internal async Task RunAsync(
            Func<CancellationToken, Task> operation,
            string description,
            Action<string> log,
            Func<Exception, bool> isTransient,
            Func<TimeSpan, CancellationToken, Task> wait,
            Func<double> jitter,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(operation);
            ArgumentException.ThrowIfNullOrWhiteSpace(description);
            ArgumentNullException.ThrowIfNull(log);
            ArgumentNullException.ThrowIfNull(isTransient);
            ArgumentNullException.ThrowIfNull(wait);
            ArgumentNullException.ThrowIfNull(jitter);

            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    await operation(cancellationToken).ConfigureAwait(false);
                    return;
                }
                catch (Exception exception) when (!cancellationToken.IsCancellationRequested && isTransient(exception))
                {
                    // --- Out of runs: say so, and surface the server's own error unchanged ---
                    if (attempt >= MaxAttempts)
                    {
                        log(string.Create(
                            CultureInfo.InvariantCulture,
                            $"{description} failed with a transient error on attempt {attempt} of {MaxAttempts}; giving up: {Summary(exception)}"));
                        throw;
                    }

                    // --- Another run remains: wait, then run again ---
                    TimeSpan delay = Delay(attempt, jitter());
                    log(string.Create(
                        CultureInfo.InvariantCulture,
                        $"{description} failed with a transient error on attempt {attempt} of {MaxAttempts}; retrying in {delay.TotalSeconds:0.0} s: {Summary(exception)}"));
                    await wait(delay, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        /// <summary>Every error number a SQL exception carries, in order.</summary>
        /// <param name="exception">The exception.</param>
        /// <returns>The numbers.</returns>
        private static int[] ErrorNumbers(SqlException exception)
        {
            return [.. exception.Errors.Cast<SqlError>().Select(static error => error.Number)];
        }

        /// <summary>A one-line account of a failure for the log: its error numbers when it is SQL's, and its first message line.</summary>
        /// <param name="exception">The failure.</param>
        /// <returns>The account.</returns>
        private static string Summary(Exception exception)
        {
            string message = exception.Message.Split('\n', 2)[0].TrimEnd('\r');
            return exception is SqlException sql
                ? $"SQL error {string.Join(", ", ErrorNumbers(sql))}: {message}"
                : $"{exception.GetType().Name}: {message}";
        }
    }
}
