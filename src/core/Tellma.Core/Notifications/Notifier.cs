// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Notifications;
using Tellma.Core.Abstractions.Realtime;
using Tellma.Core.Abstractions.TableTypes;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Data.Batches;
using Tellma.Core.Jobs;

namespace Tellma.Core.Notifications
{
    /// <summary>
    ///     The scoped notifier: checks and shapes the requests, appends one fixed statement per
    ///     request to the caller's batch, and, once the batch commits, announces the changed inboxes
    ///     in one <c>inbox.changed</c> event and wakes this instance's poller for the delivery jobs.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Every refusal here is a programming error, thrown before anything is appended, so a
    ///         refused call leaves the batch as it found it. The parameter cap of the batch is measured
    ///         for the whole call first, for the same reason.
    ///     </para>
    ///     <para>
    ///         The first call on a batch registers the batch's one commit callback; every later
    ///         request on the batch, raised or withdrawn, joins it, so a user whose inbox several
    ///         requests changed receives one event. The callback reads each statement's outcome only
    ///         when the statement completed in the attempt that committed.
    ///     </para>
    /// </remarks>
    internal sealed class Notifier : INotifier
    {
        /// <summary>The client event announcing that a user's inbox changed.</summary>
        public const string InboxChangedEvent = "inbox.changed";

        /// <summary>The scope's tenant database, which the asynchronous calls create their batch from.</summary>
        private readonly ITenantDatabase _database;

        /// <summary>The scope's request context, which defaults the actor.</summary>
        private readonly IRequestContextAccessor _context;

        /// <summary>The registered notification types.</summary>
        private readonly INotificationTypeRegistry _types;

        /// <summary>The limits of a request.</summary>
        private readonly NotificationsOptions _options;

        /// <summary>The channels that deliver on this instance, and their condition.</summary>
        private readonly NotificationDeliveryChannels _channels;

        /// <summary>Announces the changed inboxes.</summary>
        private readonly IClientEventPublisher _publisher;

        /// <summary>The seam that wakes this instance's poller.</summary>
        private readonly TenantPollerWake _wake;

        /// <summary>The notifier's instruments.</summary>
        private readonly NotificationsMetrics _metrics;

        /// <summary>The notifier's logger.</summary>
        private readonly ILogger _logger;

        /// <summary>The requests of each batch this notifier appended to, which the batch's one commit callback reads.</summary>
        private readonly ConditionalWeakTable<DataBatch, BatchRequests> _batches = [];

        /// <summary>Creates the notifier for one scope.</summary>
        /// <param name="database">The scope's tenant database.</param>
        /// <param name="context">The scope's request context.</param>
        /// <param name="types">The registered notification types.</param>
        /// <param name="options">The limits of a request.</param>
        /// <param name="channels">The channels that deliver on this instance.</param>
        /// <param name="publisher">The scope's client event publisher.</param>
        /// <param name="wake">The seam that wakes this instance's poller.</param>
        /// <param name="metrics">The notifier's instruments.</param>
        /// <param name="loggers">Creates the notifier's logger.</param>
        public Notifier(
            ITenantDatabase database,
            IRequestContextAccessor context,
            INotificationTypeRegistry types,
            IOptions<NotificationsOptions> options,
            NotificationDeliveryChannels channels,
            IClientEventPublisher publisher,
            TenantPollerWake wake,
            NotificationsMetrics metrics,
            ILoggerFactory loggers)
        {
            ArgumentNullException.ThrowIfNull(database);
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(types);
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(channels);
            ArgumentNullException.ThrowIfNull(publisher);
            ArgumentNullException.ThrowIfNull(wake);
            ArgumentNullException.ThrowIfNull(metrics);
            ArgumentNullException.ThrowIfNull(loggers);
            _database = database;
            _context = context;
            _types = types;
            _options = options.Value;
            _channels = channels;
            _publisher = publisher;
            _wake = wake;
            _metrics = metrics;
            _logger = loggers.CreateLogger(NotificationsLog.Category);
        }

        /// <inheritdoc />
        public void Notify(IDataBatch batch, IReadOnlyList<NotificationRequest> requests)
        {
            ArgumentNullException.ThrowIfNull(batch);
            ArgumentNullException.ThrowIfNull(requests);
            DataBatch data = Writable(batch);

            // The jobs a request enqueues commit with its rows and the write they describe, so a
            // batch whose statements autocommit is refused, as every enqueue refuses it; the
            // statements that enqueue also refuse to run outside a transaction.
            if (data.TransactionMode == TransactionMode.None)
            {
                throw new ArgumentException(
                    $"The batch runs in {nameof(TransactionMode.None)} mode, where each statement commits on its own, so notifications could not commit with the write they describe. Notify on a batch in {nameof(TransactionMode.Auto)} or {nameof(TransactionMode.Explicit)} mode.",
                    nameof(batch));
            }

            // --- Every request checked and shaped before anything is appended ---
            RequestContext context = _context.Current;
            string? condition = _channels.Condition;
            string? traceParent = JobQueue.TraceParent();
            List<ShapedNotification> shaped = new(requests.Count);
            for (int index = 0; index < requests.Count; index++)
            {
                NotificationRequest request = requests[index]
                    ?? throw new ArgumentNullException(nameof(requests), string.Create(CultureInfo.InvariantCulture, $"The notification request at {index} is null."));
                if (Shape(request, context, condition, traceParent) is ShapedNotification statement)
                {
                    shaped.Add(statement);
                }
            }

            // --- The batch's parameter cap, for the whole call, then the statements ---
            EnsureCapacity(data, shaped.Sum(static statement => statement.ParameterCount));
            foreach (ShapedNotification statement in shaped)
            {
                BatchResult<NotifyOutcome> result = NotificationStatements.Notify(data, statement);
                Requests(data).Notified.Add(new NotifiedRequest(statement.Type, statement.Inbox, result));
            }
        }

        /// <inheritdoc />
        public async Task NotifyAsync(IReadOnlyList<NotificationRequest> requests, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(requests);

            IDataBatch batch = _database.CreateBatch(BatchPurpose.Persist);
            Notify(batch, requests);

            // Nothing appended, nothing to send: executing would still run the prologue's round trip,
            // which does no work and could refuse the no-op on a frozen tenant or a deactivated caller.
            if (((DataBatch)batch).Statements.Count == 0)
            {
                return;
            }

            _ = await batch.ExecuteAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public void Withdraw(IDataBatch batch, IReadOnlyList<WithdrawRequest> requests)
        {
            ArgumentNullException.ThrowIfNull(batch);
            ArgumentNullException.ThrowIfNull(requests);
            DataBatch data = Writable(batch);

            // --- Every request checked and shaped before anything is appended ---
            List<(string Key, IReadOnlyList<IdList>? Recipients)> shaped = new(requests.Count);
            for (int index = 0; index < requests.Count; index++)
            {
                WithdrawRequest request = requests[index]
                    ?? throw new ArgumentNullException(nameof(requests), string.Create(CultureInfo.InvariantCulture, $"The withdrawal at {index} is null."));
                DedupKey(request.DedupKey, required: true, "The withdrawal");

                // Null withdraws every recipient's rows; an empty list withdraws nothing, so appends nothing.
                IReadOnlyList<IdList>? recipients = null;
                if (request.RecipientIds is not null)
                {
                    List<int> distinct = DistinctRecipients(request.RecipientIds, $"The withdrawal of '{request.DedupKey}'");
                    if (distinct.Count == 0)
                    {
                        continue;
                    }

                    recipients = [.. distinct.Select(static id => new IdList { Id = id })];
                }

                shaped.Add((request.DedupKey, recipients));
            }

            // --- The batch's parameter cap, for the whole call, then the statements ---
            // A withdrawal binds its key, and its recipients when it names them.
            EnsureCapacity(data, shaped.Sum(static statement => statement.Recipients is null ? 1 : 2));
            foreach ((string key, IReadOnlyList<IdList>? recipients) in shaped)
            {
                BatchResult<WithdrawOutcome> result = NotificationStatements.Withdraw(data, key, recipients);
                Requests(data).Withdrawn.Add(new WithdrawnRequest(key, result));
            }
        }

        /// <inheritdoc />
        public async Task WithdrawAsync(IReadOnlyList<WithdrawRequest> requests, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(requests);

            IDataBatch batch = _database.CreateBatch(BatchPurpose.Persist);
            Withdraw(batch, requests);

            // Nothing appended, nothing to send: executing would still run the prologue's round trip,
            // which does no work and could refuse the no-op on a frozen tenant or a deactivated caller.
            if (((DataBatch)batch).Statements.Count == 0)
            {
                return;
            }

            _ = await batch.ExecuteAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>The batch as one a tenant database created, still composing, and one that writes.</summary>
        /// <param name="batch">The caller's batch.</param>
        /// <returns>The batch.</returns>
        /// <exception cref="ArgumentException">The batch is not a tenant database's, or does not write.</exception>
        /// <exception cref="InvalidOperationException">The batch already executed.</exception>
        private static DataBatch Writable(IDataBatch batch)
        {
            DataBatch data = batch as DataBatch
                ?? throw new ArgumentException("The batch is not one a tenant database created, so the notification statements cannot ride it.", nameof(batch));
            return !data.IsComposing
                ? throw new InvalidOperationException("The batch already executed, so the notification statements cannot ride it. Notify or withdraw on a batch that is still composing.")
                : data.Purpose is BatchPurpose.Persist or BatchPurpose.Maintenance
                    ? data
                    : throw new ArgumentException(
                        $"A {data.Purpose} batch does not write; notify or withdraw on a Persist batch, or a Maintenance batch outside any caller.",
                        nameof(batch));
        }

        /// <summary>Refuses a call whose statements would take the batch past its parameter cap.</summary>
        /// <param name="data">The batch.</param>
        /// <param name="parameters">The parameters the call's statements bind.</param>
        /// <exception cref="InvalidOperationException">The batch would declare more parameters than allowed.</exception>
        private static void EnsureCapacity(DataBatch data, int parameters)
        {
            // Every statement's parameters are named after its own ordinal, so none is shared with
            // what the batch already declares.
            int total = data.ParameterCount + parameters;
            if (total > BatchAssembly.MaxParameters)
            {
                throw BatchAssembly.TooManyParameters(total);
            }
        }

        /// <summary>
        ///     Checks one request against its type and the limits, and shapes it for its statement;
        ///     null when it appends nothing: no recipients, or a channel-only type with no channel to
        ///     deliver it.
        /// </summary>
        /// <param name="request">The request.</param>
        /// <param name="context">The scope's request context.</param>
        /// <param name="condition">The delivery condition; null when no channel delivers.</param>
        /// <param name="traceParent">The raising activity's trace parent.</param>
        /// <returns>The shaped request, or null.</returns>
        /// <exception cref="InvalidOperationException">The request breaks a rule.</exception>
        private ShapedNotification? Shape(NotificationRequest request, RequestContext context, string? condition, string? traceParent)
        {
            // --- The type: registered on this instance ---
            NotificationTypeDescriptor descriptor = (request.Type is null ? null : _types.Find(request.Type))
                ?? throw new InvalidOperationException(
                    $"The notification type '{request.Type}' is not registered. Register it with contribution.NotificationType(descriptor) in the feature that raises it.");
            string type = descriptor.Key;
            string described = $"The notification request of type '{type}'";

            // --- The enums: defined members only ---
            if (!Enum.IsDefined(request.OnDuplicate))
            {
                throw new InvalidOperationException($"{described} has the duplicate policy {request.OnDuplicate}, which is not defined.");
            }

            if (request.Severity is NotificationSeverity requested && !Enum.IsDefined(requested))
            {
                throw new InvalidOperationException($"{described} has the severity {requested}, which is not defined.");
            }

            // --- The duplicate key: bounded, and required by Replace and Ongoing ---
            DedupKey(request.DedupKey, required: false, described);
            if (request.OnDuplicate != DuplicatePolicy.Suppress && request.DedupKey is null)
            {
                throw new InvalidOperationException($"{described} is {request.OnDuplicate} without a DedupKey; {request.OnDuplicate} finds the duplicate by its key, so give the request one.");
            }

            // --- A channel-only type has no row: no inbox-only request and no duplicate key ---
            if (!descriptor.Inbox)
            {
                if (request.InboxOnly)
                {
                    throw new InvalidOperationException($"{described} is InboxOnly, but the type is channel-only and writes no inbox row, so the request would deliver nothing.");
                }

                if (request.DedupKey is not null)
                {
                    throw new InvalidOperationException($"{described} carries a DedupKey, but the type is channel-only and writes no inbox row for the key to collapse.");
                }
            }

            // --- The target: exactly the type's, or none when the type declares none ---
            if (descriptor.TargetResource is string target)
            {
                if (!string.Equals(request.TargetResource, target, StringComparison.Ordinal) || request.TargetId is null)
                {
                    throw new InvalidOperationException(
                        $"{described} must target the type's resource '{target}' with a TargetId, but it targets '{request.TargetResource}' with the id {(request.TargetId is long id ? id.ToString(CultureInfo.InvariantCulture) : "null")}.");
                }
            }
            else if (request.TargetResource is not null || request.TargetId is not null)
            {
                throw new InvalidOperationException($"{described} carries a target, but the type declares none; leave TargetResource and TargetId null.");
            }

            // --- The arguments and the recipients ---
            string? arguments = Arguments(request.Arguments, described);
            List<int> distinct = DistinctRecipients(request.RecipientIds, described);
            if (distinct.Count == 0)
            {
                return null;
            }

            // --- The resolved values ---
            NotificationSeverity severity = request.Severity ?? descriptor.Severity;
            int? actor = request.ActorUserId ?? (context.IsSystem ? null : context.UserId);
            List<NotificationRowList> recipients = [.. distinct.Select(static (userId, ordinal) => new NotificationRowList { Ordinal = ordinal, UserId = userId })];

            // --- The delivery slices, unless the request is inbox-only or no channel delivers ---
            List<NotificationSliceList>? slices = null;
            if (!request.InboxOnly && condition is not null)
            {
                slices = Slices(type, severity, arguments, request.TargetResource, request.TargetId, actor, distinct);
            }
            else if (!descriptor.Inbox)
            {
                // A channel-only request with no channel to deliver it appends nothing.
                return null;
            }

            return new ShapedNotification(
                type,
                severity,
                arguments,
                request.TargetResource,
                request.TargetId,
                actor,
                request.DedupKey,
                request.OnDuplicate,
                descriptor.Mutable,
                descriptor.Inbox,
                recipients,
                slices,
                slices is null ? null : condition,
                slices is null ? null : traceParent);
        }

        /// <summary>
        ///     Cuts the distinct recipients into slices of at most the configured size, each with the
        ///     arguments of the delivery job that would carry it.
        /// </summary>
        /// <param name="type">The type key.</param>
        /// <param name="severity">The resolved severity.</param>
        /// <param name="arguments">The serialised arguments.</param>
        /// <param name="targetResource">The target resource.</param>
        /// <param name="targetId">The target row's id.</param>
        /// <param name="actor">The resolved actor.</param>
        /// <param name="recipients">The distinct recipients, in first-seen order.</param>
        /// <returns>The slices, numbered from 0.</returns>
        private List<NotificationSliceList> Slices(
            string type, NotificationSeverity severity, string? arguments, string? targetResource, long? targetId, int? actor, List<int> recipients)
        {
            int size = _options.DeliverySliceSize;
            List<NotificationSliceList> slices = new((recipients.Count + size - 1) / size);
            for (int first = 0; first < recipients.Count; first += size)
            {
                int count = Math.Min(size, recipients.Count - first);
                NotificationDeliveryArguments delivery = new(type, severity, arguments, targetResource, targetId, actor, recipients.GetRange(first, count));
                slices.Add(new NotificationSliceList
                {
                    SliceOrdinal = slices.Count,
                    FirstOrdinal = first,
                    LastOrdinal = first + count - 1,
                    ArgumentsJson = JsonSerializer.Serialize(delivery, TellmaJson.Options),
                });
            }

            return slices;
        }

        /// <summary>Serialises a request's arguments under the platform's JSON rules, held to the configured limit.</summary>
        /// <param name="arguments">The arguments; null for none.</param>
        /// <param name="described">The request, as a message's opening words name it.</param>
        /// <returns>The arguments as a JSON object; null for none.</returns>
        /// <exception cref="InvalidOperationException">The arguments do not serialise, are not an object, or are too large.</exception>
        private string? Arguments(object? arguments, string described)
        {
            if (arguments is null)
            {
                return null;
            }

            string json;
            try
            {
                json = JsonSerializer.Serialize(arguments, TellmaJson.Options);
            }
            catch (Exception exception) when (exception is JsonException or NotSupportedException or InvalidOperationException)
            {
                throw new InvalidOperationException($"{described} has arguments that cannot be serialised as JSON: {exception.Message}", exception);
            }

            // The message reads the arguments' top-level members by name, so they are one object.
            return !json.StartsWith('{')
                ? throw new InvalidOperationException($"{described} has arguments that serialise to JSON that is not an object; pass an object whose members name the message's arguments.")
                : json.Length <= _options.MaxArgumentsChars
                    ? json
                    : throw new InvalidOperationException(string.Create(
                        CultureInfo.InvariantCulture,
                        $"{described} has arguments of {json.Length} characters once serialised; a notification carries at most {_options.MaxArgumentsChars}. Pass ids and short values; the client fetches the rest."));
        }

        /// <summary>The distinct recipients of a request, in first-seen order, held to the configured limit.</summary>
        /// <param name="recipientIds">The request's recipients.</param>
        /// <param name="described">The request, as a message's opening words name it.</param>
        /// <returns>The distinct ids.</returns>
        /// <exception cref="InvalidOperationException">The list is missing or too long.</exception>
        private List<int> DistinctRecipients(IReadOnlyList<int>? recipientIds, string described)
        {
            if (recipientIds is null)
            {
                throw new InvalidOperationException($"{described} has no recipient list; pass an empty list for none.");
            }

            if (recipientIds.Count > _options.MaxRecipientsPerRequest)
            {
                throw new InvalidOperationException(string.Create(
                    CultureInfo.InvariantCulture,
                    $"{described} names {recipientIds.Count} recipients; a request names at most {_options.MaxRecipientsPerRequest}. Raise the notification in several requests."));
            }

            HashSet<int> seen = [];
            List<int> distinct = new(recipientIds.Count);
            foreach (int id in recipientIds)
            {
                if (seen.Add(id))
                {
                    distinct.Add(id);
                }
            }

            return distinct;
        }

        /// <summary>Checks a duplicate key: non-empty and within the column's width.</summary>
        /// <param name="key">The key.</param>
        /// <param name="required">Whether the key is required.</param>
        /// <param name="described">The request, as a message's opening words name it.</param>
        /// <exception cref="InvalidOperationException">The key is missing when required, empty, or too long.</exception>
        private static void DedupKey(string? key, bool required, string described)
        {
            if (key is null)
            {
                if (required)
                {
                    throw new InvalidOperationException($"{described} has no DedupKey; a withdrawal names the key whose rows it deletes.");
                }

                return;
            }

            if (key.Length is 0 or > Notification.DedupKeyLength)
            {
                throw new InvalidOperationException(string.Create(
                    CultureInfo.InvariantCulture,
                    $"{described} has a DedupKey of {key.Length} characters; a key takes 1 to {Notification.DedupKeyLength}."));
            }
        }

        /// <summary>The requests appended to a batch, registering the batch's one commit callback on the first.</summary>
        /// <param name="data">The batch.</param>
        /// <returns>The batch's requests.</returns>
        private BatchRequests Requests(DataBatch data)
        {
            if (!_batches.TryGetValue(data, out BatchRequests? requests))
            {
                // The callback is registered before the batch is remembered, so a registration the
                // batch refuses leaves nothing waiting on it.
                BatchRequests created = new();
                data.OnCommitted(_ => Committed(data, created));
                _batches.Add(data, created);
                requests = created;
            }

            return requests;
        }

        /// <summary>
        ///     The batch's commit callback: counts and logs what each completed statement did, wakes
        ///     the poller when a delivery job was enqueued, and announces the changed inboxes. Never
        ///     throws: the rows are committed whatever happens here.
        /// </summary>
        /// <param name="data">The committed batch.</param>
        /// <param name="requests">Its requests.</param>
        private void Committed(DataBatch data, BatchRequests requests)
        {
            try
            {
                // --- What each statement did, in append order ---
                SortedSet<int> users = [];
                bool enqueued = false;
                foreach (NotifiedRequest notified in requests.Notified.Where(static notified => notified.Result.IsCompleted))
                {
                    NotifyOutcome outcome = notified.Result.Value;
                    _metrics.RecordNotify(notified.Type, notified.Inbox, outcome);
                    NotificationsLog.NotificationsInserted(
                        _logger, notified.Type, outcome.Requested, outcome.InsertedUserIds.Count + outcome.RefreshedUserIds.Count, outcome.JobIds.Count);
                    users.UnionWith(outcome.InsertedUserIds);
                    users.UnionWith(outcome.RefreshedUserIds);
                    enqueued |= outcome.JobIds.Count > 0;
                }

                foreach (WithdrawnRequest withdrawn in requests.Withdrawn.Where(static withdrawn => withdrawn.Result.IsCompleted))
                {
                    WithdrawOutcome outcome = withdrawn.Result.Value;
                    _metrics.RecordWithdraw(outcome);
                    NotificationsLog.NotificationsWithdrawn(_logger, withdrawn.Key, outcome.Deleted.Count);
                    users.UnionWith(outcome.Deleted.Select(static row => row.UserId));
                }

                // --- The nudge: this instance's poller claims the delivery jobs now ---
                if (enqueued)
                {
                    _wake.Wake(data.Connection.TenantId);
                }

                // --- One event per changed inbox, however many requests changed it ---
                // No event when no inbox changed: an event with no users would reach the whole tenant.
                if (users.Count > 0)
                {
                    _ = AnnounceAsync([.. users]);
                }
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                NotificationsLog.InboxEventFailed(_logger, exception);
            }
        }

        /// <summary>Publishes <c>inbox.changed</c> to some users, logging rather than throwing a failure.</summary>
        /// <param name="users">The users whose inbox changed.</param>
        /// <returns>A task that completes once the event was handed over or failed.</returns>
        private async Task AnnounceAsync(IReadOnlyList<int> users)
        {
            try
            {
                await _publisher.PublishAsync(new ClientEvent(InboxChangedEvent, users, null)).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                NotificationsLog.InboxEventFailed(_logger, exception);
            }
        }

        /// <summary>The requests one batch carries, which its commit callback reads.</summary>
        private sealed class BatchRequests
        {
            /// <summary>The raised requests, in append order.</summary>
            public List<NotifiedRequest> Notified { get; } = [];

            /// <summary>The withdrawals, in append order.</summary>
            public List<WithdrawnRequest> Withdrawn { get; } = [];
        }

        /// <summary>One raised request on a batch.</summary>
        /// <param name="Type">Its type key.</param>
        /// <param name="Inbox">Whether its type writes inbox rows.</param>
        /// <param name="Result">What its statement did, once the batch executed.</param>
        private sealed record NotifiedRequest(string Type, bool Inbox, BatchResult<NotifyOutcome> Result);

        /// <summary>One withdrawal on a batch.</summary>
        /// <param name="Key">The withdrawn key.</param>
        /// <param name="Result">What its statement deleted, once the batch executed.</param>
        private sealed record WithdrawnRequest(string Key, BatchResult<WithdrawOutcome> Result);
    }
}
