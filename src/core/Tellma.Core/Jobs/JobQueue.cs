// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Entities;
using Tellma.Core.Abstractions.Jobs;
using Tellma.Core.Abstractions.TableTypes;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.Data.Batches;

namespace Tellma.Core.Jobs
{
    /// <summary>
    ///     The scoped job queue: validates the requests, appends the enqueue statement to the batch,
    ///     and, once the batch commits, sets each owning row's in-memory <see cref="IJobEntity.JobId" />
    ///     and wakes this instance's poller for the tenant.
    /// </summary>
    /// <remarks>
    ///     Every refusal here is a programming error, thrown before anything is appended, so a refused
    ///     call leaves the batch as it found it. Whether a named row exists and has no active job is
    ///     the statement's check, made on the stored row under the update's lock, never on the
    ///     in-memory member (<see cref="JobEnqueueCodes.OwnerUnavailable" />).
    /// </remarks>
    internal sealed class JobQueue : IJobQueue
    {
        /// <summary>The scope's tenant database, which <see cref="EnqueueAsync" /> creates its batch from.</summary>
        private readonly ITenantDatabase _database;

        /// <summary>The scope's request context, which defaults the run-as user.</summary>
        private readonly IRequestContextAccessor _context;

        /// <summary>The handlers this instance registers.</summary>
        private readonly JobHandlerRegistry _registry;

        /// <summary>The model's entity metadata, which names an owner's table.</summary>
        private readonly IEntityMetadataProvider _metadata;

        /// <summary>The seam that wakes this instance's poller.</summary>
        private readonly TenantPollerWake _wake;

        /// <summary>The warnings the scope confirmed; null when the scope composes none.</summary>
        private readonly IConfirmations? _confirmations;

        /// <summary>Creates the queue for one scope.</summary>
        /// <param name="database">The scope's tenant database.</param>
        /// <param name="context">The scope's request context.</param>
        /// <param name="registry">The handlers this instance registers.</param>
        /// <param name="metadata">The model's entity metadata.</param>
        /// <param name="wake">The seam that wakes this instance's poller.</param>
        /// <param name="confirmations">The warnings the scope confirmed; null when the scope composes none.</param>
        public JobQueue(
            ITenantDatabase database,
            IRequestContextAccessor context,
            JobHandlerRegistry registry,
            IEntityMetadataProvider metadata,
            TenantPollerWake wake,
            IConfirmations? confirmations = null)
        {
            ArgumentNullException.ThrowIfNull(database);
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(registry);
            ArgumentNullException.ThrowIfNull(metadata);
            ArgumentNullException.ThrowIfNull(wake);
            _database = database;
            _context = context;
            _registry = registry;
            _metadata = metadata;
            _wake = wake;
            _confirmations = confirmations;
        }

        /// <inheritdoc />
        public BatchResult<IReadOnlyList<int>> Enqueue(IDataBatch batch, IReadOnlyList<JobRequest> requests)
        {
            ArgumentNullException.ThrowIfNull(batch);
            ArgumentNullException.ThrowIfNull(requests);

            // --- The batch: one a tenant database created, still composing, and one that writes ---
            DataBatch data = batch as DataBatch
                ?? throw new ArgumentException("The batch is not one a tenant database created, so the enqueue statement cannot ride it.", nameof(batch));
            if (!data.IsComposing)
            {
                throw new InvalidOperationException("The batch already executed, so the enqueue statement cannot ride it. Enqueue on a batch that is still composing.");
            }

            if (data.Purpose is not (BatchPurpose.Persist or BatchPurpose.Maintenance))
            {
                throw new ArgumentException(
                    $"A {data.Purpose} batch does not write; enqueue on a Persist batch, or a Maintenance batch outside any caller.",
                    nameof(batch));
            }

            // The jobs and their rows' stamps commit together, so a batch whose statements autocommit
            // is refused (spec 0020 §1.4); the statement itself also refuses to run outside a
            // transaction, in case the mode changes after this call.
            if (data.TransactionMode == TransactionMode.None)
            {
                throw new ArgumentException(
                    $"The batch runs in {nameof(TransactionMode.None)} mode, where each statement commits on its own, so the jobs and their rows' stamps could not commit together. Enqueue on a batch in {nameof(TransactionMode.Auto)} or {nameof(TransactionMode.Explicit)} mode.",
                    nameof(batch));
            }

            // --- Every request checked and shaped before anything is appended ---
            RequestContext context = _context.Current;
            string? traceParent = TraceParent();
            string? confirmedKeys = ConfirmedKeys(context);
            List<JobRequestList> rows = new(requests.Count);
            List<IJobEntity?> owners = new(requests.Count);
            EntityMetadata? owner = null;
            HashSet<long> ownerIds = [];
            for (int ordinal = 0; ordinal < requests.Count; ordinal++)
            {
                JobRequest request = requests[ordinal]
                    ?? throw new ArgumentException(string.Create(CultureInfo.InvariantCulture, $"The request at {ordinal} is null."), nameof(requests));
                JobHandlerRegistration handler = Handler(request.HandlerKey);
                long? ownerId = OwnerId(request, handler, ref owner, ownerIds);
                (string? labelKey, string? labelArguments) = Label(request.Label, nameof(requests));
                rows.Add(new JobRequestList
                {
                    Ordinal = ordinal,
                    HandlerKey = handler.Key,
                    LabelKey = labelKey,
                    LabelArgumentsJson = labelArguments,
                    DueAt = request.DueAt?.ToUniversalTime(),
                    ArgumentsJson = Arguments(request, nameof(requests)),
                    TraceParent = traceParent,
                    ConfirmedKeys = confirmedKeys,
                    RunAsUserId = RunAsUserId(request, context),
                    RequestedById = request.RequestedById,
                    OwnerId = ownerId,
                });
                owners.Add(request.Entity);
            }

            // --- The statement, then what happens once the batch commits ---
            BatchResult<IReadOnlyList<int>> result = JobStatements.Enqueue(data, rows, owner?.Table);
            data.OnCommitted(_ =>
            {
                // The in-memory owners take the ids the statement gave their rows (spec 0020 §4.1).
                if (result.IsCompleted)
                {
                    for (int ordinal = 0; ordinal < owners.Count; ordinal++)
                    {
                        if (owners[ordinal] is IJobEntity entity)
                        {
                            entity.JobId = result.Value[ordinal];
                        }
                    }
                }

                // This instance's poller claims the jobs now; others see them on their next poll (spec 0020 §4.4).
                _wake.Wake(data.Connection.TenantId);
            });

            return result;
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<int>> EnqueueAsync(IReadOnlyList<JobRequest> requests, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(requests);

            IDataBatch batch = _database.CreateBatch(BatchPurpose.Persist);
            BatchResult<IReadOnlyList<int>> result = Enqueue(batch, requests);
            _ = await batch.ExecuteAsync(cancellationToken).ConfigureAwait(false);
            return result.Value;
        }

        /// <summary>The handler a request names, which must be registered on this instance.</summary>
        /// <param name="key">The request's handler key.</param>
        /// <returns>The handler's registration.</returns>
        /// <exception cref="InvalidOperationException">No handler on this instance registers the key.</exception>
        private JobHandlerRegistration Handler(string key)
        {
            return key is not null && _registry.TryFind(key, out JobHandlerRegistration? handler)
                ? handler
                : throw new InvalidOperationException(
                    $"No job handler on this instance registers the key '{key}'. Register the handler with contribution.JobHandler<THandler>(), or enqueue from a host that composes the feature that registers it.");
        }

        /// <summary>
        ///     Checks the row a request names against its handler's shape and the call's other
        ///     requests, and returns its id.
        /// </summary>
        /// <param name="request">The request.</param>
        /// <param name="handler">The handler it names.</param>
        /// <param name="owner">The owners' entity, set by the first request that names a row and held by the rest.</param>
        /// <param name="ownerIds">The ids of the rows the call's earlier requests named.</param>
        /// <returns>The row's id; null for an arguments-only job.</returns>
        private long? OwnerId(JobRequest request, JobHandlerRegistration handler, ref EntityMetadata? owner, HashSet<long> ownerIds)
        {
            // --- The shape: an entity handler processes one row, an arguments-only one none ---
            if (request.Entity is not IJobEntity entity)
            {
                return handler.EntityType is null
                    ? null
                    : throw new InvalidOperationException(
                        $"The job handler '{handler.Key}' processes {handler.EntityType.Name} rows, so each of its requests names the row as its Entity.");
            }

            if (handler.EntityType is null)
            {
                throw new InvalidOperationException(
                    $"The job handler '{handler.Key}' processes no rows, so its requests name no Entity; pass what it needs in the arguments.");
            }

            // --- One entity type per call: the statement updates one owner table ---
            EntityMetadata metadata = _metadata.Get(entity.GetType());
            if (metadata.ClrType != handler.EntityType)
            {
                throw new InvalidOperationException(
                    $"The job handler '{handler.Key}' processes {handler.EntityType.Name} rows, not the {metadata.Name} row the request names.");
            }

            if (owner is not null && owner.ClrType != metadata.ClrType)
            {
                throw new InvalidOperationException(
                    $"The requests name rows of both {owner.Name} and {metadata.Name}; one call enqueues the rows of one entity type, so enqueue each type in its own call.");
            }

            owner = metadata;

            // --- A saved row, named once; whether its stored job has ended is the statement's check ---
            long id = Convert.ToInt64(metadata.Key.Getter(entity), CultureInfo.InvariantCulture);
            if (id <= 0)
            {
                throw new InvalidOperationException(string.Create(
                    CultureInfo.InvariantCulture,
                    $"The {metadata.Name} row {id} has a temporary id. Enqueue a new row's job from the save pipeline's contribution step, once the save assigned its id."));
            }

            bool first = ownerIds.Add(id);
            return first
                ? id
                : throw new InvalidOperationException(string.Create(
                    CultureInfo.InvariantCulture,
                    $"The {metadata.Name} row {id} is named by more than one request; a row has one job at a time."));
        }

        /// <summary>Checks and serialises a request's display label.</summary>
        /// <param name="label">The label; null for the handler's own.</param>
        /// <param name="parameterName">The parameter the request arrived in, named in the exception.</param>
        /// <returns>The label's key and arguments as the job stores them.</returns>
        /// <exception cref="ArgumentException">The label is too wide for the job table.</exception>
        private static (string? Key, string? ArgumentsJson) Label(JobMessage? label, string parameterName)
        {
            return label is null ? (null, null) : (label.Key, JobJson.SerializeArguments(label, parameterName));
        }

        /// <summary>Serialises a request's arguments under the platform's JSON rules, held to the handler arguments' limit.</summary>
        /// <param name="request">The request.</param>
        /// <param name="parameterName">The parameter the request arrived in, named in the exception.</param>
        /// <returns>The arguments as JSON; null for none.</returns>
        /// <exception cref="ArgumentException">The arguments do not serialise, or are too large once serialised.</exception>
        private static string? Arguments(JobRequest request, string parameterName)
        {
            if (request.Arguments is null)
            {
                return null;
            }

            string json;
            try
            {
                json = JsonSerializer.Serialize(request.Arguments, TellmaJson.Options);
            }
            catch (Exception exception) when (exception is JsonException or NotSupportedException or InvalidOperationException)
            {
                throw new ArgumentException($"The arguments of the job '{request.HandlerKey}' cannot be serialised as JSON: {exception.Message}", parameterName, exception);
            }

            int bytes = Encoding.UTF8.GetByteCount(json);
            return bytes <= JobLimits.MaxHandlerArgumentsBytes
                ? json
                : throw new ArgumentException(
                    string.Create(CultureInfo.InvariantCulture, $"The arguments of the job '{request.HandlerKey}' take {bytes} bytes once serialised; a job carries at most {JobLimits.MaxHandlerArgumentsBytes}. Pass a reference to larger data instead."),
                    parameterName);
        }

        /// <summary>The user a job runs as: the request's, else the scope's user, else the system user in a system scope.</summary>
        /// <param name="request">The request.</param>
        /// <param name="context">The scope's request context.</param>
        /// <returns>The run-as user's id.</returns>
        /// <exception cref="InvalidOperationException">The request names none and the scope has neither a user nor the system principal.</exception>
        private static int RunAsUserId(JobRequest request, RequestContext context)
        {
            return request.RunAsUserId
                ?? (context.IsSystem ? WellKnownIds.SystemUserId : (int?)null)
                ?? context.UserId
                ?? throw new InvalidOperationException(
                    $"The job '{request.HandlerKey}' names no user to run as, and the scope has no user to default to. Name the user in the request's RunAsUserId.");
        }

        /// <summary>The enqueuing activity's W3C trace parent; null when there is none.</summary>
        /// <returns>The trace parent.</returns>
        private static string? TraceParent()
        {
            // Only a W3C id is a traceparent; a hierarchical one would link nothing (spec 0020 §4.3).
            return Activity.Current is { IdFormat: ActivityIdFormat.W3C, Id: string id } && id.Length <= JobConfiguration.TraceParentLength ? id : null;
        }

        /// <summary>
        ///     The warning keys the scope confirmed, comma-separated in ordinal order; none in a system
        ///     scope, where every key counts as confirmed and the job's own scope is a system one too.
        /// </summary>
        /// <param name="context">The scope's request context.</param>
        /// <returns>The keys; null when there are none.</returns>
        /// <exception cref="InvalidOperationException">The keys do not fit a job's column.</exception>
        private string? ConfirmedKeys(RequestContext context)
        {
            if (context.IsSystem || _confirmations is null || _confirmations.Confirmed.Count == 0)
            {
                return null;
            }

            string keys = string.Join(',', _confirmations.Confirmed.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));
            return keys.Length <= JobConfiguration.ConfirmedKeysLength
                ? keys
                : throw new InvalidOperationException(string.Create(
                    CultureInfo.InvariantCulture,
                    $"The scope confirmed warnings whose keys take {keys.Length} characters; a job carries at most {JobConfiguration.ConfirmedKeysLength}."));
        }
    }
}
