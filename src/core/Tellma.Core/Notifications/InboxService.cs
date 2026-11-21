// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Reflection;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Notifications;
using Tellma.Core.Abstractions.Realtime;
using Tellma.Core.Abstractions.Settings;
using Tellma.Core.Abstractions.TableTypes;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Crud;
using Tellma.Core.Data.Batches;
using Tellma.Core.Queryex;

namespace Tellma.Core.Notifications
{
    // The service is spec 0021 §5.2: the summary in one Read batch through the guarded runner (its
    // statement, then the actors' entity query), and each verb in one Persist batch, its statement
    // incrementing the caller's inbox version and the commit announcing inbox.changed to the
    // caller's other tabs and devices. The verbs are bookkeeping a read-only tenant still admits; a
    // frozen tenant refuses them. Every action is member-only: the statements' own predicates on the
    // caller are the authorization, so an id of another user's row is ignored, never refused.
    /// <summary>The calling user's inbox: the bell's summary, and the verbs that mark it seen, mark rows read and clear them.</summary>
    /// <param name="runner">Runs every batch behind the connect prologue.</param>
    /// <param name="accessor">The scope's request context.</param>
    /// <param name="frames">The write frames the scope's persists are nested in.</param>
    /// <param name="publisher">Announces the changed inbox.</param>
    /// <param name="schemas">The query schemas the actors' projection is held to.</param>
    /// <param name="stacks">The stack registry: the actors' related select and the ids ceiling of the notifications.</param>
    /// <param name="metadata">The entity metadata, which resolves the users' leaf.</param>
    /// <param name="options">The summary's caps.</param>
    /// <param name="metrics">The inbox's instruments.</param>
    [ApiRoute("inbox")]
    internal sealed class InboxService(
        IGuardedBatchRunner runner,
        IRequestContextAccessor accessor,
        WriteFrames frames,
        IClientEventPublisher publisher,
        IQueryexSchemaProvider schemas,
        IStackRegistry stacks,
        IEntityMetadataProvider metadata,
        IOptions<NotificationsOptions> options,
        NotificationsMetrics metrics)
    {
        /// <summary>The generic actors' read, closed over the users' leaf on first use.</summary>
        private static readonly MethodInfo ActorsOfMethod = typeof(InboxService).GetMethod(nameof(ActorsOf), BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("The inbox service's generic actors' read is missing.");

        /// <summary>The closed actors' reads, by the users' leaf.</summary>
        private static readonly ConcurrentDictionary<Type, ActorsReader> Readers = new();

        /// <summary>One leaf's actors' read: appends the query and returns what fills the related entities.</summary>
        /// <param name="batch">The batch.</param>
        /// <param name="entity">The users' leaf.</param>
        /// <param name="projection">The projected columns, key first.</param>
        /// <param name="actors">The batch-local table of the actors' ids.</param>
        /// <returns>What fills the related entities after execution.</returns>
        private delegate Action<RelatedEntities> ActorsReader(IDataBatch batch, EntityMetadata entity, IReadOnlySet<string> projection, string actors);

        /// <summary>
        ///     The bell's summary for the caller: the capped unseen and unread counts, the most severe
        ///     unseen row, the newest rows, the caller's own jobs that have not ended, the users who
        ///     acted in the newest rows, and the inbox's version and seen stamp.
        /// </summary>
        /// <param name="cancellationToken">Cancels the read.</param>
        /// <returns>The summary.</returns>
        [ApiAction("summary", MemberOnly = true, Idempotent = true, Mutation = false, Mcp = McpExposure.Hidden)]
        public async Task<InboxSummary> SummaryAsync(CancellationToken cancellationToken = default)
        {
            NotificationsOptions limits = options.Value;
            BatchResult<InboxSummaryRows>? rows = null;
            Action<RelatedEntities>? actors = null;
            InboxSummary summary = await runner.RunAsync(
                BatchPurpose.Read,
                (_, batch) =>
                {
                    // --- The statement, then the actors' entity query over the table it declared ---
                    rows = InboxStatements.Summary(Internal(batch), limits.SummaryCountCap, limits.SummaryLatestCount, out string table);
                    actors = ComposeActors(batch, table);
                    return ValueTask.CompletedTask;
                },
                (_, _) =>
                {
                    InboxSummaryRows read = rows!.Value;
                    RelatedEntities related = new();
                    actors!(related);
                    return new InboxSummary(read.Unseen, read.MaxUnseenSeverity, read.Unread, read.Latest, read.Running, related, read.Version, read.SeenAt);
                },
                cancellationToken).ConfigureAwait(false);

            metrics.RecordSummary();
            return summary;
        }

        /// <summary>Marks the caller's inbox seen: every row so far stops counting as unseen.</summary>
        /// <param name="cancellationToken">Cancels the persist before it is sent.</param>
        /// <returns>A count of 1 and the inbox's version after the verb.</returns>
        /// <exception cref="TenantUnavailableException">The tenant is frozen.</exception>
        [ApiAction("seen", MemberOnly = true, Idempotent = true, AllowWhenReadOnly = true, Mcp = McpExposure.Hidden)]
        public Task<InboxVerbResult> SeenAsync(CancellationToken cancellationToken = default)
        {
            return RunVerbAsync(InboxVerb.Seen, [], cancellationToken);
        }

        /// <summary>Marks the caller's named rows read; ids of rows already read or of another user's are ignored.</summary>
        /// <param name="request">The rows' ids.</param>
        /// <param name="cancellationToken">Cancels the persist before it is sent.</param>
        /// <returns>The rows newly marked read and the inbox's version after the verb.</returns>
        /// <exception cref="LimitExceededException">The request names more ids than the notifications' ceiling.</exception>
        /// <exception cref="TenantUnavailableException">The tenant is frozen.</exception>
        [ApiAction("read", MemberOnly = true, Idempotent = true, AllowWhenReadOnly = true, Mcp = McpExposure.Hidden)]
        public async Task<InboxVerbResult> ReadAsync(IdsRequest request, CancellationToken cancellationToken = default)
        {
            InboxVerbResult result = await RunVerbAsync(InboxVerb.Read, Ids(request), cancellationToken).ConfigureAwait(false);
            metrics.RecordMarkedRead(NotificationsMetrics.ReadVerb, result.Count);
            return result;
        }

        /// <summary>Marks every unread row of the caller's read.</summary>
        /// <param name="cancellationToken">Cancels the persist before it is sent.</param>
        /// <returns>The rows newly marked read and the inbox's version after the verb.</returns>
        /// <exception cref="TenantUnavailableException">The tenant is frozen.</exception>
        [ApiAction("read-all", MemberOnly = true, Idempotent = true, AllowWhenReadOnly = true, Mcp = McpExposure.Hidden)]
        public async Task<InboxVerbResult> ReadAllAsync(CancellationToken cancellationToken = default)
        {
            InboxVerbResult result = await RunVerbAsync(InboxVerb.ReadAll, [], cancellationToken).ConfigureAwait(false);
            metrics.RecordMarkedRead(NotificationsMetrics.ReadAllVerb, result.Count);
            return result;
        }

        /// <summary>Deletes the caller's named rows, read or not; ids of another user's rows are ignored.</summary>
        /// <param name="request">The rows' ids.</param>
        /// <param name="cancellationToken">Cancels the persist before it is sent.</param>
        /// <returns>The rows deleted and the inbox's version after the verb.</returns>
        /// <exception cref="LimitExceededException">The request names more ids than the notifications' ceiling.</exception>
        /// <exception cref="TenantUnavailableException">The tenant is frozen.</exception>
        [ApiAction("clear", MemberOnly = true, Idempotent = true, Destructive = true, AllowWhenReadOnly = true, Mcp = McpExposure.Hidden)]
        public async Task<InboxVerbResult> ClearAsync(IdsRequest request, CancellationToken cancellationToken = default)
        {
            InboxVerbResult result = await RunVerbAsync(InboxVerb.Clear, Ids(request), cancellationToken).ConfigureAwait(false);
            metrics.RecordCleared(NotificationsMetrics.ClearVerb, result.Count);
            return result;
        }

        /// <summary>Deletes every read row of the caller's.</summary>
        /// <param name="cancellationToken">Cancels the persist before it is sent.</param>
        /// <returns>The rows deleted and the inbox's version after the verb.</returns>
        /// <exception cref="TenantUnavailableException">The tenant is frozen.</exception>
        [ApiAction("clear-read", MemberOnly = true, Idempotent = true, Destructive = true, AllowWhenReadOnly = true, Mcp = McpExposure.Hidden)]
        public async Task<InboxVerbResult> ClearReadAsync(CancellationToken cancellationToken = default)
        {
            InboxVerbResult result = await RunVerbAsync(InboxVerb.ClearRead, [], cancellationToken).ConfigureAwait(false);
            metrics.RecordCleared(NotificationsMetrics.ClearReadVerb, result.Count);
            return result;
        }

        /// <summary>
        ///     Runs one verb's Persist batch, after the tenant-state verdict, with the caller's
        ///     <c>inbox.changed</c> published once it commits.
        /// </summary>
        /// <param name="verb">The verb.</param>
        /// <param name="ids">The distinct ids a verb over named rows binds; empty otherwise.</param>
        /// <param name="cancellationToken">Cancels the persist before it is sent.</param>
        /// <returns>The count and the version.</returns>
        private async Task<InboxVerbResult> RunVerbAsync(InboxVerb verb, IReadOnlyList<IdList> ids, CancellationToken cancellationToken)
        {
            // A verb is bookkeeping on the caller's own rows: a read-only tenant admits it, a frozen one does not.
            PersistVerdict.Ensure(accessor.Current, mutation: true, allowWhenReadOnly: true, frames.Open);

            BatchResult<InboxVerbResult>? result = null;
            return await runner.RunAsync(
                BatchPurpose.Persist,
                (user, batch) =>
                {
                    result = InboxStatements.Verb(Internal(batch), verb, ids);
                    publisher.Publish(batch, new ClientEvent(Notifier.InboxChangedEvent, [user.UserId], null));
                    return ValueTask.CompletedTask;
                },
                (_, _) => result!.Value,
                cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        ///     The distinct ids of a request that fit a notification's key, in the order sent: an id
        ///     outside its range names no row, so it is dropped as another user's would be ignored.
        /// </summary>
        /// <param name="request">The request.</param>
        /// <returns>The ids, as an id list.</returns>
        /// <exception cref="BadRequestException">The request has no body.</exception>
        /// <exception cref="LimitExceededException">The request names more ids than the notifications' ceiling.</exception>
        private List<IdList> Ids(IdsRequest request)
        {
            if (request is null)
            {
                throw new BadRequestException("The request has no body.");
            }

            // --- The ceiling the notifications' stack declares, as its own reads by ids are held to ---
            int ceiling = stacks.Find(CoreResources.Notification)?.Limits.MaxIds ?? StackLimits.Default.MaxIds;
            if (request.Ids.Count > ceiling)
            {
                throw new LimitExceededException(LimitNames.MaxIds, request.Ids.Count, ceiling);
            }

            HashSet<int> seen = [];
            List<IdList> ids = new(request.Ids.Count);
            foreach (long id in request.Ids)
            {
                if (id is >= int.MinValue and <= int.MaxValue && seen.Add((int)id))
                {
                    ids.Add(new IdList { Id = (int)id });
                }
            }

            return ids;
        }

        /// <summary>The batch as one a tenant database created, whose internal channel the inbox's statements ride.</summary>
        /// <param name="batch">The runner's batch.</param>
        /// <returns>The batch.</returns>
        /// <exception cref="InvalidOperationException">The batch is not a tenant database's.</exception>
        private static DataBatch Internal(IDataBatch batch)
        {
            return batch as DataBatch
                ?? throw new InvalidOperationException("The guarded runner's batch is not one a tenant database created, so the inbox's statements cannot ride it.");
        }

        /// <summary>
        ///     Appends the actors' entity query: the users whose ids the summary's actors table holds,
        ///     projected to their related select, with no row filter.
        /// </summary>
        /// <param name="batch">The batch.</param>
        /// <param name="actors">The actors table the summary declared.</param>
        /// <returns>What fills the related entities after execution.</returns>
        /// <exception cref="InvalidOperationException">The scope's context carries no tenant settings.</exception>
        private Action<RelatedEntities> ComposeActors(IDataBatch batch, string actors)
        {
            TenantSettings settings = accessor.Current.TenantSettings
                ?? throw new InvalidOperationException("The scope's context carries no tenant settings, so the inbox's actors cannot be read.");
            EntityMetadata users = metadata.Get(typeof(User));
            IReadOnlySet<string> projection = RelatedProjections.Of(stacks, users, schemas.GetSchema(settings.Shape));
            ActorsReader reader = Readers.GetOrAdd(users.ClrType, static leaf => ActorsOfMethod.MakeGenericMethod(leaf).CreateDelegate<ActorsReader>());
            return reader(batch, users, projection, actors);
        }

        /// <summary>One leaf's actors' read: the query restricted to the actors table, then added to the related entities.</summary>
        /// <typeparam name="TUser">The users' leaf.</typeparam>
        /// <param name="batch">The batch.</param>
        /// <param name="entity">The leaf's metadata.</param>
        /// <param name="projection">The projected columns, key first.</param>
        /// <param name="actors">The batch-local table of the actors' ids.</param>
        /// <returns>What fills the related entities after execution.</returns>
        private static Action<RelatedEntities> ActorsOf<TUser>(IDataBatch batch, EntityMetadata entity, IReadOnlySet<string> projection, string actors)
            where TUser : class
        {
            BatchResult<EntityQueryResult<TUser>> rows = batch.Query(new EntityQuery<TUser>
            {
                Select = string.Join(", ", projection),
                Restrictions = [new KeySetRestriction(entity.Key.Name, actors)],
                OrderBy = entity.Key.Name,
            });

            return related =>
            {
                IReadOnlyList<TUser> found = rows.Value.Entities;
                if (found.Count > 0)
                {
                    related.Add(entity.Name, found, projection);
                }
            };
        }
    }
}
