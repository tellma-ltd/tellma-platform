// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Logging;
using System.Globalization;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Settings;
using Tellma.Core.Abstractions.TableTypes;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Data.Queries;
using Tellma.Core.Queryex;

namespace Tellma.Core.Access
{
    // The endpoints are spec 0018 §3.7 over spec 0014 §5.1's evaluator and holders resolver; naming
    // another user is spec 0014 §10's rule: the caller reads that user's row and the roles, and no
    // decision about the target is recorded or counted. Budgets are spec 0018 §8.1.
    /// <summary>
    ///     The access endpoints: what a user may do with a list of securables and why, and who holds
    ///     one securable. The caller's own access is answered from the caller's set with no round
    ///     trip; another user's, and the holders, are role-editor information, so they ask for the
    ///     roles' read grant, and list only users the caller may read.
    /// </summary>
    /// <param name="evaluator">The scope's evaluator.</param>
    /// <param name="holders">Resolves who holds a securable.</param>
    /// <param name="runner">Runs the read that finds another user visible to the caller.</param>
    /// <param name="securables">The securable registry.</param>
    /// <param name="connection">The scope's connection, which names the caller.</param>
    /// <param name="accessor">The scope's request context.</param>
    /// <param name="schemas">The query schemas, whose users' entity the visibility read is rooted at.</param>
    /// <param name="metadata">The entity metadata of the user leaf.</param>
    /// <param name="time">The host's clock, which the caller's visibility is decided at.</param>
    /// <param name="loggerFactory">Creates the access logger.</param>
    [ApiRoute("access")]
    internal sealed class AccessService(
        AccessEvaluator evaluator,
        IAccessHoldersResolver holders,
        IGuardedBatchRunner runner,
        ISecurableRegistry securables,
        IScopeConnection connection,
        IRequestContextAccessor accessor,
        QueryexSchemaProvider schemas,
        IEntityMetadataProvider metadata,
        TimeProvider time,
        ILoggerFactory loggerFactory)
    {
        /// <summary>The most securables one check decides.</summary>
        public const int MaxSecurables = 200;

        /// <summary>The access logger.</summary>
        private readonly ILogger _logger = (loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory))).CreateLogger(AccessEvents.Category);

        /// <summary>
        ///     Decides a list of securables for a user, in order: the caller's own from the caller's
        ///     set, with no round trip; another user's for a caller who may read the roles and that
        ///     user, in two. A pair the registry does not hold is denied with its problem.
        /// </summary>
        /// <param name="request">The user, null for the caller, and the securables.</param>
        /// <param name="cancellationToken">Cancels the reads.</param>
        /// <returns>One decision per securable, in request order.</returns>
        /// <exception cref="BadRequestException">No body, or a securable that is null or names no resource or no action.</exception>
        /// <exception cref="LimitExceededException">More securables than one check decides.</exception>
        /// <exception cref="ForbiddenException">Another user is named and the caller may not read the roles.</exception>
        /// <exception cref="NotFoundException">Another user is named whom the caller may not read, or who does not exist.</exception>
        [ApiAction("check", MemberOnly = true, Idempotent = true, Mutation = false, Mcp = McpExposure.Hidden)]
        public async Task<IReadOnlyList<AccessDecision>> CheckAsync(AccessCheckRequest request, CancellationToken cancellationToken = default)
        {
            // --- Admission, before any round trip: the ceiling, then each pair whole ---
            if (request is null)
            {
                throw new BadRequestException("The request has no body.");
            }

            IReadOnlyList<SecurableRef> pairs = request.Securables;
            if (pairs.Count > MaxSecurables)
            {
                throw new LimitExceededException(LimitNames.MaxSecurables, pairs.Count, MaxSecurables);
            }

            for (int index = 0; index < pairs.Count; index++)
            {
                if (pairs[index] is not { Resource: not null, Action: not null })
                {
                    throw new BadRequestException(
                        "Each securable names a resource and an action.",
                        new Dictionary<string, object?>(StringComparer.Ordinal) { ["index"] = index });
                }
            }

            // --- The caller, by omission or by their own id: decided, recorded and counted from their set ---
            if (request.UserId is not int userId || userId == await CallerIdAsync(cancellationToken).ConfigureAwait(false))
            {
                return await evaluator.EvaluateAsync(pairs, cancellationToken).ConfigureAwait(false);
            }

            // --- Another user: the roles' read grant, then the target visible to the caller ---
            _ = await evaluator.RequireAsync(CoreResources.Role, AccessActions.Read, cancellationToken).ConfigureAwait(false);
            await EnsureVisibleAsync(userId, cancellationToken).ConfigureAwait(false);

            // --- Each pair from the scope's one load of the target, in request order ---
            var decisions = new AccessDecision[pairs.Count];
            for (int index = 0; index < decisions.Length; index++)
            {
                decisions[index] = await evaluator.EvaluateForAsync(userId, pairs[index].Resource, pairs[index].Action, cancellationToken).ConfigureAwait(false);
            }

            return decisions;
        }

        /// <summary>
        ///     Lists who holds one securable and through which grants, for a caller who may read the
        ///     roles: the holders the caller may read, the others counted only. A pair the registry
        ///     does not hold answers with its problem and no holder.
        /// </summary>
        /// <param name="request">The securable.</param>
        /// <param name="cancellationToken">Cancels the read.</param>
        /// <returns>The holders.</returns>
        /// <exception cref="BadRequestException">No body.</exception>
        /// <exception cref="ForbiddenException">The caller may not read the roles.</exception>
        [ApiAction("holders", MemberOnly = true, Idempotent = true, Mutation = false, Mcp = McpExposure.Hidden)]
        public async Task<AccessHolders> HoldersAsync(AccessHoldersRequest request, CancellationToken cancellationToken = default)
        {
            if (request is null)
            {
                throw new BadRequestException("The request has no body.");
            }

            _ = await evaluator.RequireAsync(CoreResources.Role, AccessActions.Read, cancellationToken).ConfigureAwait(false);
            return await holders.ResolveAsync(request.Resource, request.Action, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>The caller's user id: the context's, else the connection's.</summary>
        /// <param name="cancellationToken">Cancels the connection, when the scope is not connected yet.</param>
        /// <returns>The id.</returns>
        private async Task<int> CallerIdAsync(CancellationToken cancellationToken)
        {
            return accessor.Current.UserId ?? (await connection.CurrentAsync(cancellationToken).ConfigureAwait(false)).UserId;
        }

        /// <summary>
        ///     Refuses a user the caller may not read: one read of the user's id under the caller's
        ///     own read of the users, a hidden user and a missing one alike not found.
        /// </summary>
        /// <param name="userId">The user's id.</param>
        /// <param name="cancellationToken">Cancels the read.</param>
        /// <returns>A task that completes when the user is found visible.</returns>
        /// <exception cref="NotFoundException">The caller may not read the user, or no such user exists.</exception>
        /// <exception cref="InvalidOperationException">The caller's filter on the users exceeds the compile ceilings.</exception>
        private async Task EnsureVisibleAsync(int userId, CancellationToken cancellationToken)
        {
            string[] missing = [userId.ToString(CultureInfo.InvariantCulture)];
            if (userId <= 0)
            {
                throw new NotFoundException(CoreResources.User, missing);
            }

            // --- One guarded read; a recompose decides afresh under the refreshed set ---
            BatchResult<RowPage>? visible = null;
            bool found = await runner.RunAsync(
                BatchPurpose.Read,
                (user, batch) =>
                {
                    visible = ComposeVisible(user, batch, userId);
                    return ValueTask.CompletedTask;
                },
                (_, _) => visible is { Value.Rows.RowCount: 1 },
                cancellationToken).ConfigureAwait(false);
            if (!found)
            {
                throw new NotFoundException(CoreResources.User, missing);
            }
        }

        /// <summary>Composes the read of one user's id under the caller's read of the users; nothing when it denies.</summary>
        /// <param name="user">The connected caller.</param>
        /// <param name="batch">The batch.</param>
        /// <param name="userId">The user's id.</param>
        /// <returns>The read; null when the caller may read no user.</returns>
        /// <exception cref="InvalidOperationException">The caller's filter on the users exceeds the compile ceilings.</exception>
        private BatchResult<RowPage>? ComposeVisible(ConnectedUser user, IDataBatch batch, int userId)
        {
            // --- The caller's decision, from the set the batch is composed under ---
            AccessDecision decision = securables.Find(CoreResources.User, AccessActions.Read) is SecurableDescriptor read
                ? evaluator.DecideFrom(user.Access, read, time.GetUtcNow())
                : evaluator.Unknown(CoreResources.User, AccessActions.Read);
            if (!decision.IsAllowed)
            {
                return null;
            }

            // --- The users' entity of the tenant's shape; the filter compiles under the pipeline's ceilings ---
            TenantSettings settings = accessor.Current.TenantSettings
                ?? throw new InvalidOperationException("The scope's context carries no tenant settings, so the user's visibility cannot be read.");
            EntityMetadata users = metadata.Get(typeof(User));
            batch.Limits = QueryexProfiles.PipelineLimits;
            SqlIdentifier keys = batch.Tvp([new IdList { Id = userId }]);
            try
            {
                return batch.Rows(
                    new QuerySpec
                    {
                        Root = schemas.Map(settings.Shape).Descriptor(users),
                        Select = users.Key.Name,
                        Filter = decision.Filter,
                        Restrictions = [new KeySetRestriction(users.Key.Name, keys.Name)],
                    },
                    arguments: null,
                    options: null);
            }
            catch (ArgumentException tooComplex) when (decision.Outcome == AccessOutcome.Filtered)
            {
                // An administrator's configuration problem: a plain error, never a silent denial.
                string involved = string.Join(", ", decision.Grants.OfType<RoleAccessGrant>().Select(static grant => grant.RoleId).Distinct().Order());
                AccessLog.FilterTooComplex(_logger, tooComplex, user.Access.TenantId, user.UserId, decision.Resource, decision.Action, involved);
                throw new InvalidOperationException("The caller's filter on the users exceeded the compile ceilings, so another user's access cannot be checked.", tooComplex);
            }
        }
    }
}
