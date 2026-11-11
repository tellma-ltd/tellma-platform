// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.ExceptionServices;
using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Data.TableTypes;

namespace Tellma.Core.Data.Batches
{
    // Execution follows spec 0012 §6.1 and §6.5: one command per attempt, the retry rules, the commit
    // probe, and the instruments of §12.1.
    /// <summary>
    ///     Runs batches: one command per attempt, its result sets handed to the statements, failures
    ///     classified and re-run, translated or probed, every attempt traced, timed and metered, and
    ///     the outcome reported. A singleton shared by every batch.
    /// </summary>
    internal sealed class DataExecutor
    {
        /// <summary>The activity source round-trip spans are started from.</summary>
        internal static readonly ActivitySource ActivitySource = new(DataTelemetryNames.ActivitySourceName);

        /// <summary>The execution settings.</summary>
        private readonly IOptions<DataOptions> _options;

        /// <summary>The running model's metadata, whose storage fingerprint the schema guard binds.</summary>
        private readonly IEntityMetadataProvider _metadata;

        /// <summary>The clock, which also times the waits between attempts.</summary>
        private readonly TimeProvider _time;

        /// <summary>The logger.</summary>
        private readonly ILogger _logger;

        /// <summary>The executor's instruments.</summary>
        private readonly DataMetrics _metrics;

        /// <summary>The process's tenant snapshots, when the caching feature is composed.</summary>
        private readonly IVersionTagSnapshots? _snapshots;

        /// <summary>The process's user snapshots, when the caching feature is composed.</summary>
        private readonly IUserVersionTagSnapshots? _userSnapshots;

        /// <summary>Creates the executor.</summary>
        /// <param name="options">The execution settings.</param>
        /// <param name="metadata">The running model's metadata.</param>
        /// <param name="binder">The table-type binder.</param>
        /// <param name="owners">Which stack owns each table.</param>
        /// <param name="time">The clock.</param>
        /// <param name="logger">The logger.</param>
        /// <param name="metrics">The executor's instruments.</param>
        /// <param name="services">The root provider, for the optional caching services.</param>
        public DataExecutor(
            IOptions<DataOptions> options,
            IEntityMetadataProvider metadata,
            TableTypeBinder binder,
            IStackTableOwners owners,
            TimeProvider time,
            ILogger<DataBatch> logger,
            DataMetrics metrics,
            IServiceProvider services)
        {
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(metadata);
            ArgumentNullException.ThrowIfNull(binder);
            ArgumentNullException.ThrowIfNull(owners);
            ArgumentNullException.ThrowIfNull(time);
            ArgumentNullException.ThrowIfNull(logger);
            ArgumentNullException.ThrowIfNull(metrics);
            ArgumentNullException.ThrowIfNull(services);

            _options = options;
            _metadata = metadata;
            Binder = binder;
            Owners = owners;
            _time = time;
            _logger = logger;
            _metrics = metrics;
            _snapshots = services.GetService<IVersionTagSnapshots>();
            _userSnapshots = services.GetService<IUserVersionTagSnapshots>();
            VersionTags = services.GetService<IVersionTagRegistry>();
        }

        /// <summary>Where a failed attempt was when it failed.</summary>
        private enum AttemptPhase
        {
            /// <summary>Opening the connection or beginning an explicit transaction: nothing of the batch ran.</summary>
            Connecting,

            /// <summary>The command was sent: the batch may have run in part or in whole.</summary>
            Sent,

            /// <summary>Committing an explicit transaction, after the body ran.</summary>
            Committing,
        }

        /// <summary>The table-type binder.</summary>
        public TableTypeBinder Binder { get; }

        /// <summary>Which stack owns each table.</summary>
        public IStackTableOwners Owners { get; }

        /// <summary>Which version tags a batch's writes bump, when the caching feature is composed.</summary>
        public IVersionTagRegistry? VersionTags { get; }

        /// <summary>The running model's storage fingerprint.</summary>
        public string Fingerprint => _metadata.StorageFingerprint;

        /// <summary>Runs a sealed batch: one round trip, and more only when a failure is re-run.</summary>
        /// <param name="batch">The batch, its contributors' stages run.</param>
        /// <param name="cancellationToken">The caller's token, linked with the scope's.</param>
        /// <returns>The outcome.</returns>
        public async Task<BatchOutcome> ExecuteAsync(DataBatch batch, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(batch);

            BatchAssembly assembly = batch.Assemble();
            DataOptions options = _options.Value;

            // --- Cancellation: reads, validation and the waits between attempts are cancelled; a sent persist completes ---
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(batch.ScopeCancellation, cancellationToken);
            CancellationToken beforeSend = linked.Token;
            CancellationToken afterSend = batch.Purpose == BatchPurpose.Persist ? CancellationToken.None : linked.Token;

            Run run = new(batch, assembly, options, batch.Connection.TenantId, batch.Access.Operation, PurposeName(batch.Purpose));
            int roundTripsBefore = batch.Access.RoundTrips;
            _metrics.RecordStatements(run.Purpose, assembly.StatementCount);

            object? auditState = batch.Audit is IDataBatchAudit audit
                ? await audit.BeforeAsync(beforeSend).ConfigureAwait(false)
                : null;

            bool bodyRan = await RunAsync(run, beforeSend, afterSend).ConfigureAwait(false);

            if (batch.Audit is IDataBatchAudit after)
            {
                await after.AfterAsync(auditState, batch.WrittenTables, afterSend).ConfigureAwait(false);
            }

            // --- The outcome: the snapshots the prologue read replace the process's ---
            VersionTagSnapshot tags = batch.ReadVersionTags
                ?? _snapshots?.Current(run.TenantId)
                ?? new VersionTagSnapshot(run.TenantId, new Dictionary<string, VersionTag>(StringComparer.Ordinal), _time.GetUtcNow());
            if (batch.ReadVersionTags is not null)
            {
                _snapshots?.Replace(tags);
            }

            if (batch.ReadUserVersionTags is UserVersionTagSnapshot user)
            {
                _userSnapshots?.Replace(user);
            }

            // Every attempt, a probe, an explicit transaction's begin and commit count.
            BatchOutcome outcome = new(tags, batch.ReadUserVersionTags, RoundTrips: batch.Access.RoundTrips - roundTripsBefore);

            // --- The commit callbacks: outside the transaction, never thrown ---
            if (bodyRan)
            {
                foreach (Action<BatchOutcome> callback in batch.CommittedCallbacks)
                {
                    try
                    {
                        callback(outcome);
                    }
                    catch (Exception exception) when (exception is not OutOfMemoryException)
                    {
                        DataBatchLog.OnCommittedFailed(_logger, exception, run.Operation, run.TenantId);
                    }
                }
            }

            return outcome;
        }

        /// <summary>The command timeout of a purpose: the maintenance one for a maintenance batch, the ordinary one otherwise.</summary>
        /// <param name="purpose">The batch's purpose.</param>
        /// <param name="options">The execution settings.</param>
        /// <returns>The timeout in whole seconds.</returns>
        internal static int CommandTimeoutSeconds(BatchPurpose purpose, DataOptions options)
        {
            return TimeoutSeconds(purpose == BatchPurpose.Maintenance ? options.MaintenanceCommandTimeout : options.CommandTimeout);
        }

        /// <summary>
        ///     Sends the round trip until it succeeds or a failure surfaces: a reported transient failure
        ///     is re-run, a lost connection is re-run or probed, everything else is final.
        /// </summary>
        /// <param name="run">The batch being run.</param>
        /// <param name="beforeSend">The token until a command is sent, which also cancels the opens and the waits until then.</param>
        /// <param name="afterSend">The token once it is sent.</param>
        /// <returns>Whether the body ran.</returns>
        private async Task<bool> RunAsync(Run run, CancellationToken beforeSend, CancellationToken afterSend)
        {
            DataBatch batch = run.Batch;
            BatchAssembly assembly = run.Assembly;
            bool probeable = batch.Purpose == BatchPurpose.Persist && assembly.InTextTransaction && batch.Probe is not null;

            // The failure a not-committed verdict answered, once one did: a re-run that then collides
            // with the probed rows' primary key proves the first attempt committed after the probe.
            Exception? notCommitted = null;

            // Once a command was sent, a persist completes: the later opens and waits are no longer cancelled.
            CancellationToken current = beforeSend;

            for (int attempt = 1; ; attempt++)
            {
                Attempt result = await AttemptAsync(run, attempt, current, afterSend).ConfigureAwait(false);
                current = result.Sent ? afterSend : current;
                if (result.Failure is not Exception failure)
                {
                    result.Cursor?.ApplyCompletions();
                    _metrics.RecordRoundTrip(run.Operation, run.Purpose, DataMetrics.Ok, result.Elapsed);
                    return result.BodyRan;
                }

                // --- Cancellation surfaces as itself ---
                if (current.IsCancellationRequested)
                {
                    Settle(run, result, DataMetrics.Failed);
                    if (failure is OperationCanceledException)
                    {
                        ExceptionDispatchInfo.Throw(failure);
                    }

                    throw new OperationCanceledException("The batch was cancelled while it ran.", failure, current);
                }

                // --- Classification ---
                IReadOnlyList<SqlErrorInfo>? errors = failure is SqlException sql ? SqlErrorInfo.Of(sql) : null;
                FailureKind kind = Classify(result, errors);

                // A re-run after a not-committed verdict that collides with the probed rows: the first
                // attempt reached its insert after the probe and committed.
                if (notCommitted is not null && errors is not null && batch.Probe!.IsOwnPrimaryKeyViolation(SqlErrorInfo.Primary(errors)))
                {
                    Settle(run, result, DataMetrics.Failed);
                    _metrics.RecordProbe(CommitVerdict.Committed);
                    throw new DataAccessAmbiguousException(notCommitted, CommitVerdict.Committed);
                }

                RetryAction action = RetryRules.Decide(
                    kind, result.Sent, batch.TransactionMode, result.Phase == AttemptPhase.Committing, assembly.MayRunTwice, probeable, batch.WrittenTables.Count > 0);
                string retryClass = kind == FailureKind.Reported ? DataMetrics.ReportedClass : DataMetrics.AmbiguousClass;
                switch (action)
                {
                    case RetryAction.Surface:
                        Settle(run, result, DataMetrics.Failed);
                        if (errors is not null && kind == FailureKind.Final && result.Sent)
                        {
                            throw DataErrors.Translate(errors, failure, assembly, result.Cursor);
                        }

                        ExceptionDispatchInfo.Throw(failure);
                        break;

                    case RetryAction.GiveUp:
                        Settle(run, result, DataMetrics.Failed);
                        throw new DataAccessRetryExhaustedException(failure, attempt);

                    case RetryAction.Unknown:
                        Settle(run, result, DataMetrics.Failed);
                        _metrics.RecordProbe(CommitVerdict.Unknown);
                        throw new DataAccessAmbiguousException(failure, CommitVerdict.Unknown);

                    case RetryAction.Probe:
                        CommitVerdict verdict = await ProbeAsync(run, batch.Probe!).ConfigureAwait(false);
                        if (verdict != CommitVerdict.NotCommitted)
                        {
                            Settle(run, result, DataMetrics.Failed);
                            throw new DataAccessAmbiguousException(failure, verdict);
                        }

                        // Nothing was written. A defect would fail again, so it surfaces as itself;
                        // a lost connection is re-run.
                        if (kind == FailureKind.Defect)
                        {
                            Settle(run, result, DataMetrics.Failed);
                            ExceptionDispatchInfo.Throw(failure);
                        }

                        notCommitted = failure;
                        retryClass = DataMetrics.AmbiguousClass;
                        break;

                    case RetryAction.Rerun:
                        break;

                    default:
                        throw new InvalidOperationException($"The retry action {action} is unknown.");
                }

                // --- The re-run: within the attempts allowed, after a jittered wait ---
                if (attempt > run.Options.RetryAttempts)
                {
                    Settle(run, result, DataMetrics.Failed);
                    throw new DataAccessRetryExhaustedException(failure, attempt);
                }

                Settle(run, result, DataMetrics.Retried, applyCompletions: false);
                batch.Access.Retries++;
                _metrics.RecordRetry(retryClass, errors is null ? DataMetrics.ConnectionLost : SqlErrorInfo.Primary(errors).Number.ToString(CultureInfo.InvariantCulture));
                TimeSpan delay = RetryRules.Delay(attempt, run.Options.RetryBaseDelay, Random.Shared.NextDouble());
                DataBatchLog.Retrying(_logger, failure, run.Operation, run.TenantId, attempt, retryClass, delay.TotalMilliseconds);
                await Task.Delay(delay, _time, current).ConfigureAwait(false);
            }
        }

        /// <summary>
        ///     Classifies a failed attempt. Before the send, a server error that is transient or a lost
        ///     connection is safe to repeat, and any other failure is final. After it, a final error on
        ///     a connection that is no longer open is a lost connection, and a client failure is one
        ///     too when the connection closed, else a reader's defect.
        /// </summary>
        /// <param name="attempt">The attempt.</param>
        /// <param name="errors">The server errors, when the provider raised the failure.</param>
        /// <returns>The kind.</returns>
        private static FailureKind Classify(Attempt attempt, IReadOnlyList<SqlErrorInfo>? errors)
        {
            if (!attempt.Sent)
            {
                return errors is null || DataErrors.Classify(errors) == FailureKind.Final ? FailureKind.Final : FailureKind.Reported;
            }

            if (errors is not null)
            {
                FailureKind kind = DataErrors.Classify(errors);
                return kind == FailureKind.Final && attempt.ConnectionLost ? FailureKind.Ambiguous : kind;
            }

            return attempt.ConnectionLost ? FailureKind.Ambiguous : FailureKind.Defect;
        }

        /// <summary>Records an attempt whose outcome is known and, when it is the one that counts, completes what it read.</summary>
        /// <param name="run">The batch being run.</param>
        /// <param name="attempt">The attempt.</param>
        /// <param name="outcome">The duration's outcome tag.</param>
        /// <param name="applyCompletions">Whether the attempt counts, so its readers' results complete.</param>
        private void Settle(Run run, Attempt attempt, string outcome, bool applyCompletions = true)
        {
            // A failed batch still hands its callers the results read before the failure, such as the
            // connect prologue's row; an attempt that is re-run hands out nothing.
            if (applyCompletions)
            {
                attempt.Cursor?.ApplyCompletions();
            }

            _metrics.RecordRoundTrip(run.Operation, run.Purpose, outcome, attempt.Elapsed);
        }

        /// <summary>Sends the command once, reads every result set and commits an explicit transaction.</summary>
        /// <param name="run">The batch being run.</param>
        /// <param name="attempt">The attempt, from 1.</param>
        /// <param name="beforeSend">The token until the command is sent.</param>
        /// <param name="afterSend">The token once it is sent.</param>
        /// <returns>The attempt's outcome; a failure is returned, never thrown.</returns>
        private async Task<Attempt> AttemptAsync(Run run, int attempt, CancellationToken beforeSend, CancellationToken afterSend)
        {
            DataBatch batch = run.Batch;
            BatchAssembly assembly = run.Assembly;

            // --- The span: named for the operation and purpose, so a trace names the round trip without its text ---
            using Activity? activity = ActivitySource.StartActivity(
                run.Operation.Length == 0 ? run.Purpose : $"{run.Operation} {run.Purpose}", ActivityKind.Client);
            _ = activity?
                .SetTag(DataTelemetryNames.RoleTag, DataTelemetryNames.TenantRole)
                .SetTag(DataTelemetryNames.PurposeAttribute, run.Purpose)
                .SetTag(DataTelemetryNames.OperationAttribute, run.Operation)
                .SetTag(DataTelemetryNames.AttemptAttribute, attempt)
                .SetTag(DataTelemetryNames.TenantIdAttribute, run.TenantId);

            long started = _time.GetTimestamp();
            AttemptPhase phase = AttemptPhase.Connecting;
            SqlConnection? connection = null;
            SqlTransaction? transaction = null;
            ResultSetCursor? cursor = null;
            Exception? failure = null;
            bool connectionLost = false;
            bool bodyRan = false;
            TimeSpan elapsed;
            try
            {
                connection = await batch.Connection.AcquireAsync(beforeSend).ConfigureAwait(false);

                // An explicit transaction costs a round trip to open and one to commit.
                if (batch.TransactionMode == TransactionMode.Explicit)
                {
                    transaction = (SqlTransaction)await connection.BeginTransactionAsync(beforeSend).ConfigureAwait(false);
                    batch.Access.RoundTrips++;
                }

                await using SqlCommand command = connection.CreateCommand();
                command.CommandType = CommandType.Text;
                command.CommandText = assembly.Text;
                command.CommandTimeout = CommandTimeoutSeconds(batch.Purpose, run.Options);
                command.Transaction = transaction;
                foreach (BatchParameter parameter in assembly.Parameters)
                {
                    _ = command.Parameters.Add(parameter.Create());
                }

                phase = AttemptPhase.Sent;
                batch.Access.RoundTrips++;
                SqlDataReader reader = await command.ExecuteReaderAsync(afterSend).ConfigureAwait(false);
                await using (reader.ConfigureAwait(false))
                {
                    cursor = new ResultSetCursor(reader);
                    try
                    {
                        bodyRan = await assembly.ReadAsync(cursor, () => batch.GuardPassed, afterSend).ConfigureAwait(false);
                    }
                    catch (Exception exception) when (exception is not SqlException)
                    {
                        // A failure on this side (a reader's defect, a count mismatch, cancellation)
                        // would otherwise let disposing the reader drain the stream, and so run the
                        // batch to its COMMIT behind a reported failure. The attention aborts the
                        // batch, and XACT_ABORT rolls back what it has not committed.
                        if (connection.State == ConnectionState.Open)
                        {
                            command.Cancel();
                        }

                        throw;
                    }
                }

                if (transaction is not null)
                {
                    phase = AttemptPhase.Committing;
                    await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
                    batch.Access.RoundTrips++;
                }
            }
            catch (Exception exception)
            {
                // The failure is returned for the retry rules; whether the connection survived it
                // tells a lost connection from an error the server reported on a live one.
                failure = exception;
                connectionLost = connection is not null && connection.State != ConnectionState.Open;
                _ = activity?.SetStatus(ActivityStatusCode.Error, exception.GetType().Name);
                if (transaction is not null && phase != AttemptPhase.Committing)
                {
                    await RollBackAsync(transaction, run.Operation, run.TenantId).ConfigureAwait(false);
                }
            }
            finally
            {
                if (transaction is not null)
                {
                    await transaction.DisposeAsync().ConfigureAwait(false);
                }

                if (connection is not null)
                {
                    await batch.Connection.ReleaseAsync(connection).ConfigureAwait(false);
                }

                // --- The slow round trip carries its full text to the log, and only there ---
                elapsed = _time.GetElapsedTime(started);
                if (elapsed > run.Options.SlowRoundTripThreshold)
                {
                    DataBatchLog.SlowRoundTrip(
                        _logger,
                        run.TenantId,
                        run.Operation,
                        run.Purpose,
                        attempt,
                        elapsed.TotalMilliseconds,
                        assembly.StatementCount,
                        assembly.TableRows,
                        cursor?.RowsRead ?? 0,
                        assembly.Text);
                }
            }

            return new Attempt(bodyRan, failure, phase, connectionLost, cursor, elapsed);
        }

        /// <summary>
        ///     Runs the commit probe on a fresh connection as one autocommit round trip. A probe that
        ///     fails answers <see cref="CommitVerdict.Unknown" />.
        /// </summary>
        /// <param name="run">The batch being run.</param>
        /// <param name="probe">The probe.</param>
        /// <returns>The verdict, also metered.</returns>
        private async Task<CommitVerdict> ProbeAsync(Run run, CommitProbe probe)
        {
            DataBatch batch = run.Batch;
            using Activity? activity = ActivitySource.StartActivity(
                run.Operation.Length == 0 ? "probe" : $"{run.Operation} probe", ActivityKind.Client);
            _ = activity?
                .SetTag(DataTelemetryNames.RoleTag, DataTelemetryNames.TenantRole)
                .SetTag(DataTelemetryNames.PurposeAttribute, run.Purpose)
                .SetTag(DataTelemetryNames.OperationAttribute, run.Operation)
                .SetTag(DataTelemetryNames.TenantIdAttribute, run.TenantId);

            CommitVerdict verdict;
            SqlConnection? connection = null;
            try
            {
                // Never cancelled: the answer decides whether a write that may have committed is reported as one.
                connection = await batch.Connection.AcquireAsync(CancellationToken.None).ConfigureAwait(false);
                batch.Access.RoundTrips++;
                verdict = await probe.RunAsync(connection, CommandTimeoutSeconds(batch.Purpose, run.Options)).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                _ = activity?.SetStatus(ActivityStatusCode.Error, exception.GetType().Name);
                DataBatchLog.CommitProbeFailed(_logger, exception, run.Operation, run.TenantId);
                verdict = CommitVerdict.Unknown;
            }
            finally
            {
                if (connection is not null)
                {
                    await batch.Connection.ReleaseAsync(connection).ConfigureAwait(false);
                }
            }

            _metrics.RecordProbe(verdict);
            return verdict;
        }

        /// <summary>Rolls an explicit transaction back after a failure, logging a rollback that fails too.</summary>
        /// <param name="transaction">The transaction.</param>
        /// <param name="operation">The scope's operation.</param>
        /// <param name="tenantId">The tenant.</param>
        /// <returns>A task that completes when the rollback was attempted.</returns>
        private async Task RollBackAsync(SqlTransaction transaction, string operation, int tenantId)
        {
            try
            {
                // Under XACT_ABORT the server may have rolled back already, which leaves nothing to do.
                if (transaction.Connection is not null)
                {
                    await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                }
            }
            catch (Exception exception) when (exception is SqlException or InvalidOperationException)
            {
                // The original failure is the one that surfaces.
                DataBatchLog.RollbackFailed(_logger, exception, operation, tenantId);
            }
        }

        /// <summary>A purpose's name in lower case, as spans, logs and instruments carry it.</summary>
        /// <param name="purpose">The purpose.</param>
        /// <returns>The name.</returns>
        private static string PurposeName(BatchPurpose purpose)
        {
            return purpose switch
            {
                BatchPurpose.Read => "read",
                BatchPurpose.Validate => "validate",
                BatchPurpose.Persist => "persist",
                BatchPurpose.Maintenance => "maintenance",
                _ => throw new ArgumentOutOfRangeException(nameof(purpose), purpose, "Not a batch purpose."),
            };
        }

        /// <summary>A timeout in whole seconds, rounded up, at least one.</summary>
        /// <param name="timeout">The timeout.</param>
        /// <returns>The seconds.</returns>
        private static int TimeoutSeconds(TimeSpan timeout)
        {
            return (int)Math.Clamp(Math.Ceiling(timeout.TotalSeconds), 1, int.MaxValue);
        }

        /// <summary>One batch being run: what every attempt of it shares.</summary>
        /// <param name="Batch">The batch.</param>
        /// <param name="Assembly">Its assembly.</param>
        /// <param name="Options">The execution settings.</param>
        /// <param name="TenantId">The tenant.</param>
        /// <param name="Operation">The scope's operation.</param>
        /// <param name="Purpose">The purpose's name.</param>
        private sealed record Run(DataBatch Batch, BatchAssembly Assembly, DataOptions Options, int TenantId, string Operation, string Purpose);

        /// <summary>One attempt's outcome.</summary>
        /// <param name="BodyRan">Whether the body ran, on success.</param>
        /// <param name="Failure">The failure, or null on success.</param>
        /// <param name="Phase">Where the attempt was when it ended.</param>
        /// <param name="ConnectionLost">Whether the connection was no longer open after the failure.</param>
        /// <param name="Cursor">The attempt's cursor, once the command returned a reader.</param>
        /// <param name="Elapsed">How long the attempt took.</param>
        private sealed record Attempt(bool BodyRan, Exception? Failure, AttemptPhase Phase, bool ConnectionLost, ResultSetCursor? Cursor, TimeSpan Elapsed)
        {
            /// <summary>Whether the batch's command was sent.</summary>
            public bool Sent => Phase != AttemptPhase.Connecting;
        }
    }
}
