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
using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Data.TableTypes;

namespace Tellma.Core.Data.Batches
{
    /// <summary>
    ///     Runs batches: one command per round trip, its result sets handed to the statements, the
    ///     round trip traced and timed, and the outcome reported. A singleton shared by every batch.
    /// </summary>
    internal sealed class DataExecutor
    {
        /// <summary>The activity source round-trip spans are started from.</summary>
        internal static readonly ActivitySource ActivitySource = new(DataTelemetryNames.ActivitySourceName);

        /// <summary>The execution settings.</summary>
        private readonly IOptions<DataOptions> _options;

        /// <summary>The running model's metadata, whose storage fingerprint the schema guard binds.</summary>
        private readonly IEntityMetadataProvider _metadata;

        /// <summary>The clock.</summary>
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
        /// <param name="services">The root provider, for the optional snapshot stores.</param>
        public DataExecutor(
            IOptions<DataOptions> options,
            IEntityMetadataProvider metadata,
            TableTypeBinder binder,
            IStackTableOwners owners,
            TimeProvider time,
            ILogger<DataBatch> logger,
            IServiceProvider services)
        {
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(metadata);
            ArgumentNullException.ThrowIfNull(binder);
            ArgumentNullException.ThrowIfNull(owners);
            ArgumentNullException.ThrowIfNull(time);
            ArgumentNullException.ThrowIfNull(logger);
            ArgumentNullException.ThrowIfNull(services);

            _options = options;
            _metadata = metadata;
            Binder = binder;
            Owners = owners;
            _time = time;
            _logger = logger;
            _snapshots = services.GetService<IVersionTagSnapshots>();
            _userSnapshots = services.GetService<IUserVersionTagSnapshots>();
        }

        /// <summary>The table-type binder.</summary>
        public TableTypeBinder Binder { get; }

        /// <summary>Which stack owns each table.</summary>
        public IStackTableOwners Owners { get; }

        /// <summary>The running model's storage fingerprint.</summary>
        public string Fingerprint => _metadata.StorageFingerprint;

        /// <summary>Runs a sealed batch in one round trip.</summary>
        /// <param name="batch">The batch, its contributors' stages run.</param>
        /// <param name="cancellationToken">The caller's token, linked with the scope's.</param>
        /// <returns>The outcome.</returns>
        public async Task<BatchOutcome> ExecuteAsync(DataBatch batch, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(batch);

            BatchAssembly assembly = batch.Assemble();
            DataOptions options = _options.Value;

            // --- Cancellation: reads and validation are cancelled; a sent persist completes ---
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(batch.ScopeCancellation, cancellationToken);
            CancellationToken beforeSend = linked.Token;
            CancellationToken afterSend = batch.Purpose == BatchPurpose.Persist ? CancellationToken.None : linked.Token;

            int tenantId = batch.Connection.TenantId;
            string operation = batch.Access.Operation;
            bool bodyRan;
            SqlConnection connection = await batch.Connection.AcquireAsync(beforeSend).ConfigureAwait(false);
            try
            {
                object? auditState = batch.Audit is IDataBatchAudit audit
                    ? await audit.BeforeAsync(beforeSend).ConfigureAwait(false)
                    : null;

                bodyRan = await RoundTripAsync(batch, assembly, connection, tenantId, operation, options, beforeSend, afterSend).ConfigureAwait(false);

                if (batch.Audit is IDataBatchAudit after)
                {
                    await after.AfterAsync(auditState, batch.WrittenTables, afterSend).ConfigureAwait(false);
                }
            }
            finally
            {
                await batch.Connection.ReleaseAsync(connection).ConfigureAwait(false);
            }

            // --- The outcome: the snapshots the prologue read replace the process's ---
            VersionTagSnapshot tags = batch.ReadVersionTags
                ?? _snapshots?.Current(tenantId)
                ?? new VersionTagSnapshot(tenantId, new Dictionary<string, VersionTag>(StringComparer.Ordinal), _time.GetUtcNow());
            if (batch.ReadVersionTags is not null)
            {
                _snapshots?.Replace(tags);
            }

            if (batch.ReadUserVersionTags is UserVersionTagSnapshot user)
            {
                _userSnapshots?.Replace(user);
            }

            BatchOutcome outcome = new(tags, batch.ReadUserVersionTags, RoundTrips: 1);

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
                        DataBatchLog.OnCommittedFailed(_logger, exception, operation, tenantId);
                    }
                }
            }

            return outcome;
        }

        /// <summary>Sends the command, reads every result set and commits an explicit transaction.</summary>
        /// <param name="batch">The batch.</param>
        /// <param name="assembly">Its assembly.</param>
        /// <param name="connection">The open connection.</param>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="operation">The scope's operation.</param>
        /// <param name="options">The execution settings.</param>
        /// <param name="beforeSend">The token until the command is sent.</param>
        /// <param name="afterSend">The token once it is sent.</param>
        /// <returns>Whether the body ran.</returns>
        private async Task<bool> RoundTripAsync(
            DataBatch batch,
            BatchAssembly assembly,
            SqlConnection connection,
            int tenantId,
            string operation,
            DataOptions options,
            CancellationToken beforeSend,
            CancellationToken afterSend)
        {
            const int attempt = 1;
            string purpose = PurposeName(batch.Purpose);

            // --- The span: named for the operation and purpose, so a trace names the round trip without its text ---
            using Activity? activity = ActivitySource.StartActivity(
                operation.Length == 0 ? purpose : $"{operation} {purpose}", ActivityKind.Client);
            _ = activity?
                .SetTag(DataTelemetryNames.RoleTag, DataTelemetryNames.TenantRole)
                .SetTag(DataTelemetryNames.PurposeAttribute, purpose)
                .SetTag(DataTelemetryNames.OperationAttribute, operation)
                .SetTag(DataTelemetryNames.AttemptAttribute, attempt)
                .SetTag(DataTelemetryNames.TenantIdAttribute, tenantId);

            long started = _time.GetTimestamp();
            ResultSetCursor? cursor = null;
            SqlTransaction? transaction = null;
            try
            {
                // An explicit transaction costs a round trip to open and one to commit.
                if (batch.TransactionMode == TransactionMode.Explicit)
                {
                    transaction = (SqlTransaction)await connection.BeginTransactionAsync(beforeSend).ConfigureAwait(false);
                }

                await using SqlCommand command = connection.CreateCommand();
                command.CommandType = CommandType.Text;
                command.CommandText = assembly.Text;
                command.CommandTimeout = TimeoutSeconds(batch.Purpose == BatchPurpose.Maintenance ? options.MaintenanceCommandTimeout : options.CommandTimeout);
                command.Transaction = transaction;
                foreach (BatchParameter parameter in assembly.Parameters)
                {
                    _ = command.Parameters.Add(parameter.Create());
                }

                batch.Access.RoundTrips++;
                bool bodyRan;
                SqlDataReader reader = await command.ExecuteReaderAsync(afterSend).ConfigureAwait(false);
                await using (reader.ConfigureAwait(false))
                {
                    cursor = new ResultSetCursor(reader);
                    bodyRan = await assembly.ReadAsync(cursor, () => batch.GuardPassed, afterSend).ConfigureAwait(false);
                }

                if (transaction is not null)
                {
                    await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
                }

                return bodyRan;
            }
            catch (Exception exception)
            {
                _ = activity?.SetStatus(ActivityStatusCode.Error, exception.GetType().Name);
                if (transaction is not null)
                {
                    await RollBackAsync(transaction, operation, tenantId).ConfigureAwait(false);
                }

                throw;
            }
            finally
            {
                if (transaction is not null)
                {
                    await transaction.DisposeAsync().ConfigureAwait(false);
                }

                // --- The slow round trip carries its full text to the log, and only there ---
                TimeSpan elapsed = _time.GetElapsedTime(started);
                if (elapsed > options.SlowRoundTripThreshold)
                {
                    DataBatchLog.SlowRoundTrip(
                        _logger,
                        tenantId,
                        operation,
                        purpose,
                        attempt,
                        elapsed.TotalMilliseconds,
                        assembly.StatementCount,
                        assembly.TableRows,
                        cursor?.RowsRead ?? 0,
                        assembly.Text);
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

        /// <summary>A purpose's name in lower case, as spans and logs carry it.</summary>
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
    }
}
