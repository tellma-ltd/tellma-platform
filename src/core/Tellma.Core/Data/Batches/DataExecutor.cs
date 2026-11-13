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
using Tellma.Core.Data.Ids;
using Tellma.Core.Data.Queries;
using Tellma.Core.Data.Saves;
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
        /// <param name="queries">What query statements compile and materialise with.</param>
        /// <param name="allocator">The id allocator, whose refills ride the batches that write its tables.</param>
        /// <param name="saves">The save emitter.</param>
        /// <param name="services">The root provider, for the optional caching services.</param>
        public DataExecutor(
            IOptions<DataOptions> options,
            IEntityMetadataProvider metadata,
            TableTypeBinder binder,
            IStackTableOwners owners,
            TimeProvider time,
            ILogger<DataBatch> logger,
            DataMetrics metrics,
            QueryServices queries,
            IdAllocator allocator,
            SaveEmitter saves,
            IServiceProvider services)
        {
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(metadata);
            ArgumentNullException.ThrowIfNull(binder);
            ArgumentNullException.ThrowIfNull(owners);
            ArgumentNullException.ThrowIfNull(time);
            ArgumentNullException.ThrowIfNull(logger);
            ArgumentNullException.ThrowIfNull(metrics);
            ArgumentNullException.ThrowIfNull(queries);
            ArgumentNullException.ThrowIfNull(allocator);
            ArgumentNullException.ThrowIfNull(saves);
            ArgumentNullException.ThrowIfNull(services);

            _options = options;
            _metadata = metadata;
            Binder = binder;
            Owners = owners;
            _time = time;
            _logger = logger;
            Metrics = metrics;
            Queries = queries;
            Allocator = allocator;
            Saves = saves;
            _snapshots = services.GetService<IVersionTagSnapshots>();
            _userSnapshots = services.GetService<IUserVersionTagSnapshots>();
            VersionTags = services.GetService<IVersionTagRegistry>();
        }

        /// <summary>Where a failed attempt was when it failed.</summary>
        private enum AttemptPhase
        {
            /// <summary>
            ///     Nothing of the batch reached the server: opening the connection, beginning an explicit
            ///     transaction, or writing a request the client failed to encode.
            /// </summary>
            Unsent,

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

        /// <summary>What query statements compile and materialise with.</summary>
        public QueryServices Queries { get; }

        /// <summary>The id allocator.</summary>
        public IdAllocator Allocator { get; }

        /// <summary>The save emitter.</summary>
        public SaveEmitter Saves { get; }

        /// <summary>The data layer's instruments.</summary>
        public DataMetrics Metrics { get; }

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
            Metrics.RecordStatements(run.Purpose, assembly.StatementCount);

            object? auditState = batch.Audit is IDataBatchAudit audit
                ? await audit.BeforeAsync(beforeSend).ConfigureAwait(false)
                : null;

            bool bodyRan;
            try
            {
                bodyRan = await RunAsync(run, beforeSend, afterSend).ConfigureAwait(false);
            }
            catch (UniqueConstraintViolationException violation)
            {
                // A duplicate primary key is never healed: the buffer that handed the id out is
                // dropped, so the next reservation reads the table afresh.
                Allocator.OnDuplicateKey(run.TenantId, violation.IndexName);
                throw;
            }

            if (batch.Audit is IDataBatchAudit after)
            {
                await after.AfterAsync(auditState, batch.WrittenTables, afterSend).ConfigureAwait(false);
            }

            // --- The platform's completion steps: before the outcome, so their round trips count ---
            if (bodyRan)
            {
                foreach (Func<CancellationToken, Task> step in batch.ExecutedSteps)
                {
                    await step(beforeSend).ConfigureAwait(false);
                }
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
        /// <param name="beforeSend">The caller's token, which cancels the opens, the waits between attempts and any command until it is sent.</param>
        /// <param name="afterSend">The token of a sent command: never cancelled for a persist.</param>
        /// <returns>Whether the body ran.</returns>
        private async Task<bool> RunAsync(Run run, CancellationToken beforeSend, CancellationToken afterSend)
        {
            DataBatch batch = run.Batch;
            BatchAssembly assembly = run.Assembly;
            bool probeable = batch.Purpose == BatchPurpose.Persist && assembly.InTextTransaction && batch.Probe is not null;
            bool writes = batch.WrittenTables.Count > 0;

            // --- What earlier attempts leave open ---
            // The failure a not-committed verdict answered: the server may still have been running that
            // attempt, short of its insert, so before any error reaches the caller the probe asks again.
            Exception? notCommitted = null;

            // The failure of a sent writing attempt that was re-run without a verdict: whether it
            // committed stays unknown, so a cancellation then is no failure the caller may act on.
            Exception? unresolved = null;

            // How many re-runs followed a command timeout, which a read or validation batch limits.
            int timeoutReruns = 0;

            for (int attempt = 1; ; attempt++)
            {
                Attempt result = await AttemptAsync(run, attempt, beforeSend, afterSend).ConfigureAwait(false);
                if (result.Failure is not Exception failure)
                {
                    result.Cursor?.ApplyCompletions();
                    Metrics.RecordRoundTrip(run.Operation, run.Purpose, DataMetrics.Ok, result.Elapsed);
                    return result.BodyRan;
                }

                // --- Cancellation of the attempt: the open, or a sent command other than a persist ---
                if ((result.Sent ? afterSend : beforeSend).IsCancellationRequested)
                {
                    // A writing command cancelled after its send may have committed before it stopped,
                    // unless an explicit transaction holds it short of its commit: the executor never
                    // commits that transaction, and rolls it back.
                    bool mayHaveCommitted = result.Sent && writes
                        && (batch.TransactionMode != TransactionMode.Explicit || result.Phase == AttemptPhase.Committing);
                    Settle(run, result, DataMetrics.Failed);
                    ExceptionDispatchInfo.Throw(
                        await CancelledAsync(run, failure, mayHaveCommitted ? failure : unresolved, notCommitted, beforeSend).ConfigureAwait(false));
                }

                // --- Classification ---
                IReadOnlyList<SqlErrorInfo>? errors = failure is SqlException sql ? SqlErrorInfo.Of(sql) : null;
                FailureKind kind = Classify(result.Sent, result.ConnectionLost, assembly.InTextTransaction, errors);
                bool timedOut = errors is not null && errors.Any(static error => error.Number == DataErrors.CommandTimeout);

                // A re-run after a not-committed verdict that collides with the probed rows: the first
                // attempt reached its insert after the probe and committed.
                if (notCommitted is not null && errors is not null && batch.Probe!.IsOwnPrimaryKeyViolation(SqlErrorInfo.Primary(errors)))
                {
                    Settle(run, result, DataMetrics.Failed);
                    Metrics.RecordProbe(CommitVerdict.Committed);
                    throw new DataAccessAmbiguousException(notCommitted, CommitVerdict.Committed);
                }

                RetryAction action = RetryRules.Decide(
                    kind, result.Sent, batch.TransactionMode, result.Phase == AttemptPhase.Committing, assembly.MayRunTwice, probeable, writes);
                string retryClass = kind == FailureKind.Reported ? DataMetrics.ReportedClass : DataMetrics.AmbiguousClass;
                bool probedNow = false;
                switch (action)
                {
                    case RetryAction.Surface:
                        // After a not-committed verdict the re-run's error may be the first attempt's
                        // commit seen from behind (a moved tag, a conflict), so the probe asks again.
                        if (notCommitted is not null && await ReprobeAsync(run, notCommitted, failure, beforeSend).ConfigureAwait(false) is Exception answered)
                        {
                            Settle(run, result, DataMetrics.Failed);
                            throw answered;
                        }

                        // After the re-run of a write whose outcome stayed unknown, no error of a later
                        // attempt shows that nothing committed.
                        Settle(run, result, DataMetrics.Failed);
                        if (unresolved is not null)
                        {
                            throw UnknownOutcome(failure);
                        }

                        if (errors is not null && kind is FailureKind.Final or FailureKind.Defect && result.Sent)
                        {
                            throw DataErrors.Translate(errors, failure, assembly, result.Cursor);
                        }

                        ExceptionDispatchInfo.Throw(failure);
                        break;

                    case RetryAction.GiveUp:
                        Settle(run, result, DataMetrics.Failed);
                        throw unresolved is null ? new DataAccessRetryExhaustedException(failure, attempt) : UnknownOutcome(failure);

                    case RetryAction.Unknown:
                        Settle(run, result, DataMetrics.Failed);
                        throw UnknownOutcome(failure);

                    case RetryAction.Probe:
                        CommitVerdict verdict = await ProbeAsync(run, batch.Probe!, beforeSend).ConfigureAwait(false);
                        if (verdict != CommitVerdict.NotCommitted)
                        {
                            Settle(run, result, DataMetrics.Failed);
                            throw new DataAccessAmbiguousException(failure, verdict);
                        }

                        // Nothing was written. A defect would fail again, so it surfaces as itself,
                        // translated when the server raised it; a lost connection is re-run.
                        if (kind == FailureKind.Defect)
                        {
                            Settle(run, result, DataMetrics.Failed);
                            if (errors is not null)
                            {
                                throw DataErrors.Translate(errors, failure, assembly, result.Cursor);
                            }

                            ExceptionDispatchInfo.Throw(failure);
                        }

                        notCommitted = failure;
                        probedNow = true;
                        retryClass = DataMetrics.AmbiguousClass;
                        break;

                    case RetryAction.Rerun:
                        // A sent writing batch re-run because it may run twice: its outcome stays unknown.
                        unresolved = kind == FailureKind.Ambiguous && writes ? failure : unresolved;
                        break;

                    default:
                        throw new InvalidOperationException($"The retry action {action} is unknown.");
                }

                // --- The re-run: within the attempts allowed, and for a timed-out read within its own budget ---
                if (attempt > run.Options.RetryAttempts || (timedOut && !RetryRules.MayRerunAfterTimeout(batch.Purpose, timeoutReruns)))
                {
                    if (notCommitted is not null && !probedNow && await ReprobeAsync(run, notCommitted, failure, beforeSend).ConfigureAwait(false) is Exception reprobed)
                    {
                        Settle(run, result, DataMetrics.Failed);
                        throw reprobed;
                    }

                    // Exhaustion says nothing committed, which an earlier attempt of unknown outcome belies.
                    Settle(run, result, DataMetrics.Failed);
                    throw unresolved is null ? new DataAccessRetryExhaustedException(failure, attempt) : UnknownOutcome(failure);
                }

                TimeSpan delay = RetryRules.Delay(attempt, run.Options.RetryBaseDelay, Random.Shared.NextDouble());
                DataBatchLog.Retrying(_logger, failure, run.Operation, run.TenantId, attempt, retryClass, delay.TotalMilliseconds);

                // --- The wait: the caller's token cancels it, as it does the next attempt's open ---
                try
                {
                    await Task.Delay(delay, _time, beforeSend).ConfigureAwait(false);
                }
                catch (OperationCanceledException cancelled) when (beforeSend.IsCancellationRequested)
                {
                    Settle(run, result, DataMetrics.Failed);
                    ExceptionDispatchInfo.Throw(await CancelledAsync(run, cancelled, unresolved, notCommitted, beforeSend).ConfigureAwait(false));
                }

                // --- The re-run starts: it is counted and metered ---
                timeoutReruns += timedOut ? 1 : 0;
                Settle(run, result, DataMetrics.Retried, applyCompletions: false);
                batch.Access.Retries++;
                Metrics.RecordRetry(retryClass, errors is null ? DataMetrics.ConnectionLost : SqlErrorInfo.Primary(errors).Number.ToString(CultureInfo.InvariantCulture));
            }
        }

        /// <summary>
        ///     What a cancelled batch surfaces. Cancellation never turns a write whose outcome is unknown
        ///     into a failure: after a not-committed verdict the probe asks again, and a writing attempt
        ///     that may have committed is ambiguous. Otherwise the cancellation itself.
        /// </summary>
        /// <param name="run">The batch being run.</param>
        /// <param name="cancellation">The failure the cancellation caused: an <see cref="OperationCanceledException" />, or the provider's error for a cancelled command.</param>
        /// <param name="unresolved">The failure of a writing attempt that may have committed, if any.</param>
        /// <param name="notCommitted">The failure a not-committed verdict answered, if any.</param>
        /// <param name="token">The token that was cancelled.</param>
        /// <returns>The exception to throw.</returns>
        private async Task<Exception> CancelledAsync(Run run, Exception cancellation, Exception? unresolved, Exception? notCommitted, CancellationToken token)
        {
            OperationCanceledException cancelled = cancellation as OperationCanceledException
                ?? new OperationCanceledException("The batch was cancelled while it ran.", cancellation, token);
            return notCommitted is not null && await ReprobeAsync(run, notCommitted, cancelled, token).ConfigureAwait(false) is Exception answered ? answered
                : unresolved is null ? cancelled
                : UnknownOutcome(unresolved);
        }

        /// <summary>The ambiguity of a write whose outcome nothing can tell, metered as an unknown verdict.</summary>
        /// <param name="failure">The failure the ambiguity carries.</param>
        /// <returns>The exception to throw.</returns>
        private DataAccessAmbiguousException UnknownOutcome(Exception failure)
        {
            Metrics.RecordProbe(CommitVerdict.Unknown);
            return new DataAccessAmbiguousException(failure, CommitVerdict.Unknown);
        }

        /// <summary>
        ///     Probes again, before an error reaches the caller, after a not-committed verdict: the first
        ///     attempt may have committed since, when the server was still running it short of its insert.
        /// </summary>
        /// <param name="run">The batch being run.</param>
        /// <param name="notCommitted">The failure the not-committed verdict answered.</param>
        /// <param name="failure">The error that would otherwise reach the caller.</param>
        /// <param name="cancellationToken">The caller's token, which cancels the waits between the probe's attempts.</param>
        /// <returns>The ambiguity to surface instead, or null when the batch still did not commit.</returns>
        private async Task<Exception?> ReprobeAsync(Run run, Exception notCommitted, Exception failure, CancellationToken cancellationToken)
        {
            return await ProbeAsync(run, run.Batch.Probe!, cancellationToken).ConfigureAwait(false) switch
            {
                CommitVerdict.Committed => new DataAccessAmbiguousException(notCommitted, CommitVerdict.Committed),
                CommitVerdict.Unknown => new DataAccessAmbiguousException(failure, CommitVerdict.Unknown),
                CommitVerdict.NotCommitted => null,
                _ => throw new InvalidOperationException("The commit verdict is unknown to the executor."),
            };
        }

        /// <summary>
        ///     Classifies a failed round trip. Before the send, a server error that is transient or a lost
        ///     connection is safe to repeat, and any other failure is final. After it, a final error on
        ///     a connection that is no longer open is a lost connection; a number outside the bands
        ///     platform and distribution SQL throw is a defect when the text commits its own transaction;
        ///     and a client failure is a lost connection when the connection closed, else a reader's defect.
        /// </summary>
        /// <param name="sent">Whether the command was sent.</param>
        /// <param name="connectionLost">Whether the connection was no longer open after the failure.</param>
        /// <param name="inTextTransaction">Whether the text begins and commits its own transaction.</param>
        /// <param name="errors">The server errors, when the provider raised the failure.</param>
        /// <returns>The kind.</returns>
        private static FailureKind Classify(bool sent, bool connectionLost, bool inTextTransaction, IReadOnlyList<SqlErrorInfo>? errors)
        {
            if (!sent)
            {
                return errors is null || DataErrors.Classify(errors) == FailureKind.Final ? FailureKind.Final : FailureKind.Reported;
            }

            if (errors is null)
            {
                return connectionLost ? FailureKind.Ambiguous : FailureKind.Defect;
            }

            // An out-of-band number may come from a RAISERROR, which XACT_ABORT does not abort: the
            // server may have run on to the text's COMMIT, so the failure proves nothing about it.
            FailureKind kind = DataErrors.Classify(errors);
            return kind != FailureKind.Final ? kind
                : connectionLost ? FailureKind.Ambiguous
                : inTextTransaction && DataErrors.IsOutOfBand(SqlErrorInfo.Primary(errors).Number) ? FailureKind.Defect
                : FailureKind.Final;
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

            Metrics.RecordRoundTrip(run.Operation, run.Purpose, outcome, attempt.Elapsed);
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
            AttemptPhase phase = AttemptPhase.Unsent;
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
                SqlDataReader reader;
                try
                {
                    reader = await command.ExecuteReaderAsync(afterSend).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is not (SqlException or OperationCanceledException) && connection.State == ConnectionState.Open)
                {
                    // The client failed to write the request, such as a table-valued parameter's row
                    // it could not encode. The server runs no request it has not received whole, and
                    // the driver abandons a partly written one with an attention on a connection it
                    // keeps open, so nothing of the batch ran.
                    phase = AttemptPhase.Unsent;
                    throw;
                }

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
        ///     Runs the commit probe until it answers. A probe whose own round trip failed transiently or
        ///     lost its connection runs again, within the attempts and with the waits of a batch's
        ///     re-runs, and after a timeout once more at most, as a read does. Any other failure, the
        ///     attempts running out, or the caller cancelling a wait leaves the verdict unknown.
        /// </summary>
        /// <param name="probeOnce">Runs one attempt, given the attempt from 1; a failure is returned, never thrown.</param>
        /// <param name="options">The execution settings: the attempts allowed and the first wait.</param>
        /// <param name="time">The clock that times the waits.</param>
        /// <param name="unit">Picks each wait's jitter: a uniform value in [0, 1).</param>
        /// <param name="retrying">Told of each failure that runs again: the failure, its attempt and the wait.</param>
        /// <param name="cancellationToken">The caller's token, which cancels the waits.</param>
        /// <returns>The attempt that answered, or the last that failed, whose verdict is <see cref="CommitVerdict.Unknown" />.</returns>
        internal static async Task<ProbeAttempt> ProbeUntilAnsweredAsync(
            Func<int, Task<ProbeAttempt>> probeOnce,
            DataOptions options,
            TimeProvider time,
            Func<double> unit,
            Action<Exception, int, TimeSpan> retrying,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(probeOnce);
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(time);
            ArgumentNullException.ThrowIfNull(unit);
            ArgumentNullException.ThrowIfNull(retrying);

            int timeoutReruns = 0;
            for (int attempt = 1; ; attempt++)
            {
                ProbeAttempt result = await probeOnce(attempt).ConfigureAwait(false);
                if (result.Failure is not Exception failure)
                {
                    return result;
                }

                // --- The probe is a read: it runs again when a re-run may cure its failure ---
                if (result.Kind is not (FailureKind.Reported or FailureKind.Ambiguous)
                    || attempt > options.RetryAttempts
                    || (result.TimedOut && !RetryRules.MayRerunAfterTimeout(BatchPurpose.Read, timeoutReruns)))
                {
                    return result;
                }

                TimeSpan delay = RetryRules.Delay(attempt, options.RetryBaseDelay, unit());
                retrying(failure, attempt, delay);
                try
                {
                    await Task.Delay(delay, time, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return result;
                }

                timeoutReruns += result.TimedOut ? 1 : 0;
            }
        }

        /// <summary>
        ///     Runs the commit probe on a fresh connection, each attempt one autocommit round trip, until
        ///     it answers or its failures leave the verdict <see cref="CommitVerdict.Unknown" />.
        /// </summary>
        /// <param name="run">The batch being run.</param>
        /// <param name="probe">The probe.</param>
        /// <param name="cancellationToken">The caller's token, which cancels the waits between attempts.</param>
        /// <returns>The verdict, also metered.</returns>
        private async Task<CommitVerdict> ProbeAsync(Run run, CommitProbe probe, CancellationToken cancellationToken)
        {
            ProbeAttempt answer = await ProbeUntilAnsweredAsync(
                attempt => ProbeOnceAsync(run, probe, attempt),
                run.Options,
                _time,
                Random.Shared.NextDouble,
                (failure, attempt, delay) => DataBatchLog.CommitProbeRetrying(_logger, failure, run.Operation, run.TenantId, attempt, delay.TotalMilliseconds),
                cancellationToken).ConfigureAwait(false);
            if (answer.Failure is Exception failure)
            {
                DataBatchLog.CommitProbeFailed(_logger, failure, run.Operation, run.TenantId);
            }

            Metrics.RecordProbe(answer.Verdict);
            return answer.Verdict;
        }

        /// <summary>Runs one attempt of the commit probe on a fresh connection.</summary>
        /// <param name="run">The batch being run.</param>
        /// <param name="probe">The probe.</param>
        /// <param name="attempt">The attempt, from 1.</param>
        /// <returns>The attempt's verdict, or its failure classified; a failure is returned, never thrown.</returns>
        private static async Task<ProbeAttempt> ProbeOnceAsync(Run run, CommitProbe probe, int attempt)
        {
            DataBatch batch = run.Batch;
            using Activity? activity = ActivitySource.StartActivity(
                run.Operation.Length == 0 ? "probe" : $"{run.Operation} probe", ActivityKind.Client);
            _ = activity?
                .SetTag(DataTelemetryNames.RoleTag, DataTelemetryNames.TenantRole)
                .SetTag(DataTelemetryNames.PurposeAttribute, run.Purpose)
                .SetTag(DataTelemetryNames.OperationAttribute, run.Operation)
                .SetTag(DataTelemetryNames.AttemptAttribute, attempt)
                .SetTag(DataTelemetryNames.TenantIdAttribute, run.TenantId);

            SqlConnection? connection = null;
            try
            {
                // Never cancelled: the answer decides whether a write that may have committed is reported as one.
                connection = await batch.Connection.AcquireAsync(CancellationToken.None).ConfigureAwait(false);
                batch.Access.RoundTrips++;
                CommitVerdict verdict = await probe.RunAsync(connection, CommandTimeoutSeconds(batch.Purpose, run.Options)).ConfigureAwait(false);
                return new ProbeAttempt(verdict, Failure: null, FailureKind.Final, TimedOut: false);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // Classified as any round trip is: sent once the connection opened, reading nothing.
                _ = activity?.SetStatus(ActivityStatusCode.Error, exception.GetType().Name);
                IReadOnlyList<SqlErrorInfo>? errors = exception is SqlException sql ? SqlErrorInfo.Of(sql) : null;
                bool connectionLost = connection is not null && connection.State != ConnectionState.Open;
                FailureKind kind = Classify(sent: connection is not null, connectionLost, inTextTransaction: false, errors);
                bool timedOut = errors is not null && errors.Any(static error => error.Number == DataErrors.CommandTimeout);
                return new ProbeAttempt(CommitVerdict.Unknown, exception, kind, timedOut);
            }
            finally
            {
                if (connection is not null)
                {
                    await batch.Connection.ReleaseAsync(connection).ConfigureAwait(false);
                }
            }
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
            public bool Sent => Phase != AttemptPhase.Unsent;
        }

        /// <summary>One attempt of the commit probe.</summary>
        /// <param name="Verdict">The verdict; <see cref="CommitVerdict.Unknown" /> when the attempt failed.</param>
        /// <param name="Failure">The failure, or null when the probe answered.</param>
        /// <param name="Kind">The failure's kind; not read when the probe answered.</param>
        /// <param name="TimedOut">Whether the failure was the command timeout.</param>
        internal readonly record struct ProbeAttempt(CommitVerdict Verdict, Exception? Failure, FailureKind Kind, bool TimedOut);
    }
}
