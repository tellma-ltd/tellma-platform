// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Entities;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Crud.Enlistment;
using Tellma.Core.Crud.Validation;
using Tellma.Core.Data.Batches;
using Tellma.Core.Data.Queries;
using Tellma.Core.Queryex;
using Tellma.Core.Tenancy;

namespace Tellma.Core.Crud.Save
{
    // What every write of a stack shares (spec 0015 §6, §10, §11): its access decisions taken for
    // the connected user each batch is composed for (§9.1, §9.2), the read of a reference target or
    // of a visible load (§7.4), the entity tags its validation rounds were served under and the
    // persist re-declares (§6.7), the authority each appender composes under (§13.3), and the
    // post-commit phase whose failures never change the response (§13.2).
    /// <summary>
    ///     The state and the steps every write of one stack shares: a save, a delete or an action.
    /// </summary>
    /// <param name="operation">The scope's write services.</param>
    /// <param name="resource">The stack's resource.</param>
    /// <param name="operationName">The <c>crud.operation</c> the write runs as.</param>
    internal abstract class WriteRun(SaveOperation operation, string resource, string operationName)
    {
        /// <summary>The code of every refusal for a missing grant or a row outside it.</summary>
        protected const string ForbiddenCode = "forbidden";

        /// <summary>The code the post-check throws with, which the executor raises as a row-security failure.</summary>
        protected const string RowSecurityCode = "RowSecurity";

        /// <summary>The dependency a data-access failure names.</summary>
        protected const string DatabaseDependency = "database";

        /// <summary>The wait a caller is told before reloading after a commit the database could not confirm.</summary>
        protected static readonly TimeSpan DatabaseRetryAfter = TimeSpan.FromSeconds(1);

        /// <summary>The <c>entity:*</c> dependencies the validation rounds were served under, by name, each as first served.</summary>
        private readonly Dictionary<string, VersionTagDependency> _served = new(StringComparer.Ordinal);

        /// <summary>The permission set the persist batch was last composed for; null before its first composition.</summary>
        private UserAccess? _persistSet;

        /// <summary>The scope's write services.</summary>
        protected SaveOperation Services { get; } = operation;

        /// <summary>The stack's resource.</summary>
        protected string Resource { get; } = resource;

        /// <summary>The <c>crud.operation</c> the write runs as.</summary>
        protected string OperationName { get; } = operationName;

        /// <summary>The <c>entity:*</c> dependencies the validation rounds were served under, each as first served.</summary>
        protected IReadOnlyCollection<VersionTagDependency> ServedTags => _served.Values;

        /// <summary>The connected user the current batch is composed for.</summary>
        /// <exception cref="InvalidOperationException">Read before any batch was composed.</exception>
        protected ConnectedUser Composer
        {
            get => field ?? throw new InvalidOperationException("A write's access decisions are taken while a batch is composed, for the user it is composed for.");
            set;
        }

        /// <summary>Sets the authority of the appender the batch is composed for next, on a batch that takes one.</summary>
        /// <param name="batch">The batch.</param>
        /// <param name="tables">The stack-owned tables the appender may write.</param>
        protected static void Authorize(IDataBatch batch, IReadOnlySet<TableName> tables)
        {
            if (batch is IBatchAuthority authority)
            {
                authority.SetCallerAuthority(tables);
            }
        }

        /// <summary>The connected user's decision on a securable.</summary>
        /// <param name="user">The connected user.</param>
        /// <param name="resource">The resource.</param>
        /// <param name="action">The action.</param>
        /// <returns>The decision; a denial when the securable is not registered.</returns>
        protected AccessDecision Decide(ConnectedUser user, string resource, string action)
        {
            return Services.Securables.Find(resource, action) is SecurableDescriptor securable
                ? Services.Evaluator.DecideFrom(user.Access, securable, Services.Time.GetUtcNow())
                : Services.Evaluator.Unknown(resource, action);
        }

        /// <summary>The connected user's decision on the stack's own securable, refused when it grants nothing.</summary>
        /// <param name="user">The connected user.</param>
        /// <param name="action">The action.</param>
        /// <returns>The decision: unrestricted or filtered.</returns>
        /// <exception cref="ForbiddenException">The connected user's set no longer grants the action.</exception>
        protected AccessDecision Granted(ConnectedUser user, string action)
        {
            AccessDecision decision = Decide(user, Resource, action);
            return decision.Outcome == AccessOutcome.Denied
                ? throw new ForbiddenException(ForbiddenCode, decision.Resource, decision.Action, ForbiddenReason.NoGrant)
                : decision;
        }

        /// <summary>
        ///     The connected user's read of an entity, as a reference to it or a visible load of it is
        ///     judged: its own read decision when a securable guards it, its owner's seen through the
        ///     navigation to the owner's row for a child entity without one, and no restriction for an
        ///     entity no grant can reach.
        /// </summary>
        /// <param name="user">The connected user.</param>
        /// <param name="target">The entity.</param>
        /// <returns>The read.</returns>
        /// <exception cref="InvalidOperationException">A child entity on the way declares no navigation through its parent key.</exception>
        protected TargetRead ReadOf(ConnectedUser user, EntityMetadata target)
        {
            // --- A securable of its own: a stack's, or a child query surface's, whose owner the evaluator composes ---
            if (Services.Securables.ForResource(target.Name).Count > 0)
            {
                return new TargetRead(Decide(user, target.Name, AccessActions.Read), Services.Securables.Find(target.Name, AccessActions.Read));
            }

            // --- A child without one: its owner's read, the owner's filter rebased onto the child's navigation ---
            if (Services.OwnerOf(target) is EntityOwner owner)
            {
                string navigation = owner.Navigation ?? throw new InvalidOperationException(
                    $"The child entity {target.Name} has no navigation through its parent key {owner.ParentKey.Name}, so a read of it cannot be judged under the read of {owner.Entity.Name}.");
                TargetRead above = ReadOf(user, owner.Entity);
                AccessDecision decision = above.Decision;
                FilterTree? filter = decision.Filter is FilterTree held ? FilterTree.Via(navigation, held) : null;
                return above with { Decision = new AccessDecision(target.Name, AccessActions.Read, decision.Outcome, filter, decision.Grants, decision.Problems) };
            }

            // --- No grant reaches it ---
            return new TargetRead(new AccessDecision(target.Name, AccessActions.Read, AccessOutcome.Unrestricted, null, [], []), Securable: null);
        }

        // Spec 0015 §7.4 loads the references through the visible load, so a validator's visible load
        // is decided as a reference target's read is: a child without a securable of its own under its
        // owner's decision, and a sensitive read asking for the step-up a direct read asks for (spec
        // 0014 §4.3) before its rows reach the validator. A denied read loads nothing and asks nothing.
        /// <summary>The caller's read decision on an entity, for the user the current round is composed for.</summary>
        /// <param name="entityType">The entity's class.</param>
        /// <returns>The decision.</returns>
        /// <exception cref="StepUpRequiredException">The entity is read under a sensitive securable, below the step-up bar.</exception>
        /// <exception cref="IndividualRequiredException">The same, for a service account, which cannot step up.</exception>
        protected AccessDecision ReadDecision(Type entityType)
        {
            TargetRead read = ReadOf(Composer, Services.Metadata.Get(entityType));
            if (read.IsSensitive)
            {
                EnsureStepUp();
            }

            return read.Decision;
        }

        /// <summary>Asks for the step-up a direct read of a sensitive securable asks for, of every caller but the platform's own work.</summary>
        /// <exception cref="StepUpRequiredException">The caller's sign-in is below the step-up bar.</exception>
        /// <exception cref="IndividualRequiredException">The caller is a service account, which cannot step up.</exception>
        protected void EnsureStepUp()
        {
            RequestContext context = Services.Accessor.Current;
            if (!context.IsSystem)
            {
                AssuranceCheck.Ensure(context, Services.StepUp.Requirement, Services.Time.GetUtcNow());
            }
        }

        /// <summary>Records the <c>entity:*</c> dependencies a validation round's batch was served under, keeping each first served tag.</summary>
        /// <param name="batch">The round's batch, once composed.</param>
        protected void Served(IDataBatch batch)
        {
            if (batch is not IBatchDependencies declared)
            {
                return;
            }

            foreach (VersionTagDependency dependency in declared.Dependencies)
            {
                if (dependency.Name.StartsWith(VersionTagNames.EntityPrefix, StringComparison.Ordinal))
                {
                    _ = _served.TryAdd(dependency.Name, dependency with { OnMismatch = VersionTagMismatchPolicy.Rerun });
                }
            }
        }

        /// <summary>Declares on the persist batch every tag the validation rounds were served under, so a list that moved since fails its guard.</summary>
        /// <param name="batch">The persist batch.</param>
        protected void DependOnServed(IDataBatch batch)
        {
            if (_served.Count > 0)
            {
                batch.DependsOn([.. _served.Values]);
            }
        }

        /// <summary>Refuses a re-entered persist whose fresh snapshot moved a tag the validation rounds were served under.</summary>
        /// <param name="user">The fresh connected user.</param>
        /// <exception cref="StaleContextException">A served tag moved: what the validators read is stale.</exception>
        protected void EnsureServedTagsHold(ConnectedUser user)
        {
            VersionTagDependency[] moved = [.. _served.Values.Where(served => user.TenantTags.TryGetValue(served.Name, out Guid fresh) && fresh != served.ExpectedTag.Value)];
            if (moved.Length > 0)
            {
                throw new StaleContextException(moved);
            }
        }

        // Spec 0015 §5.1 re-runs a host's own validators on a re-entry and replays its enlisted groups,
        // whose validators never run again. A re-entry composed for another permission set than the
        // composition before it (the runner reconnected past a moved permissions tag) would commit
        // rows those validators judged under grants the caller may no longer hold, such as a role
        // membership whose escalation test read the caller's own set. So it fails closed: stale,
        // whenever a group's target validated its rows; a group no validator judged replays as it is.
        /// <summary>
        ///     Notes the permission set a persist composition is for; on a re-entry composed for
        ///     another set, refuses it when a group the batch carries was validated under the one before.
        /// </summary>
        /// <param name="user">The connected user the composition is for.</param>
        /// <param name="enlistments">The rounds whose groups the batch carries, at any depth; null when the write hosts none.</param>
        /// <exception cref="StaleContextException">The set moved, and a group's target validated its rows under the one before.</exception>
        protected void EnsureGroupsJudgedUnder(ConnectedUser user, EnlistmentRounds? enlistments)
        {
            ArgumentNullException.ThrowIfNull(user);
            UserAccess? before = _persistSet;
            _persistSet = user.Access;
            if (before is null || ReferenceEquals(before, user.Access) || enlistments is null || !enlistments.Groups.Any(static group => group.Validates))
            {
                return;
            }

            throw new StaleContextException([new VersionTagDependency(VersionTagNames.Permissions, new VersionTag(before.Tag), VersionTagMismatchPolicy.Rerun)]);
        }

        /// <summary>Runs one post-commit participant, logging and counting its failure instead of raising it.</summary>
        /// <param name="component">The participant's type.</param>
        /// <param name="step">The participant's step.</param>
        /// <returns>A task that completes when the step returned or failed.</returns>
        protected async Task ContainedAsync(Type component, Func<Task> step)
        {
            try
            {
                await step().ConfigureAwait(false);
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                // A post-commit failure never changes the response: the write committed.
                Failed(component, failure);
            }
        }

        /// <summary>Logs and counts a commit callback a participant registered that failed.</summary>
        /// <param name="callback">The callback.</param>
        /// <param name="failure">Its failure.</param>
        protected void CallbackFailed(Action<BatchOutcome> callback, Exception failure)
        {
            // A lambda's method lives on a closure class nested in the component that wrote it.
            Type? component = callback.Method.DeclaringType;
            while (component is { IsNested: true } && component.Name.StartsWith('<'))
            {
                component = component.DeclaringType;
            }

            Failed(component ?? callback.GetType(), failure);
        }

        /// <summary>Logs and counts one post-commit failure.</summary>
        /// <param name="component">The failing component's type.</param>
        /// <param name="failure">The failure.</param>
        protected void Failed(Type component, Exception failure)
        {
            Services.Metrics.CountEffectFailure(component.Name);
            CrudLog.EffectFailed(Services.Logger, component.Name, Resource, OperationName, Activity.Current?.TraceId.ToString(), failure);
        }

        /// <summary>Logs the failure of the read that follows a commit the probe proved; the write answers its ids alone.</summary>
        /// <param name="failure">The read's failure.</param>
        protected void ProvenCommitReadFailed(Exception failure)
        {
            CrudLog.ProvenCommitReadFailed(Services.Logger, Resource, OperationName, Activity.Current?.TraceId.ToString(), failure);
        }

        /// <summary>Dispatches the loads of one validation round on one guarded batch; a round with none sends nothing.</summary>
        /// <param name="loader">The operation's loader.</param>
        /// <param name="cancellationToken">Cancels the round.</param>
        /// <returns>A task that completes when the round's refs are filled.</returns>
        protected async Task DispatchRoundAsync(ContextLoader loader, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(loader);
            ContextLoader.ContextRound loads = loader.BeginRound();
            if (loads.IsEmpty)
            {
                return;
            }

            // The batch that executed, whose entity tags the persist re-declares (spec 0015 §6.7).
            IDataBatch? served = null;
            _ = await Services.Runner.RunAsync(
                BatchPurpose.Validate,
                (user, batch) =>
                {
                    // A recomposition composes the round afresh, its decisions taken again.
                    Composer = user;
                    batch.Limits = QueryexProfiles.PipelineLimits;
                    loads.Compose(batch);
                    served = batch;
                    return ValueTask.CompletedTask;
                },
                static (_, _) => true,
                cancellationToken).ConfigureAwait(false);

            // The runner may have refreshed the context in its own frame: the validators resume in
            // the cultures of the context the batch was composed under.
            Services.Accessor.Current.ApplyCultures();
            Served(served!);
            loads.Complete();
        }

        // Spec 0015 §9.2, for a hooked delete and the general action path: the targets load under the
        // caller's Read filter, and the same ids are counted under the operation's grant, compared in
        // C# once the round ran. A grant without a filter needs no count, and a caller who reads no
        // row of the stack loads nothing, so every id is not found.
        /// <summary>Composes the load of an operation's target rows and the grant's count over the same ids.</summary>
        /// <typeparam name="TEntity">The stack's entity type.</typeparam>
        /// <typeparam name="TKey">The stack's key type.</typeparam>
        /// <param name="batch">The round's batch.</param>
        /// <param name="user">The connected user.</param>
        /// <param name="map">The tenant's schema map.</param>
        /// <param name="root">The stack's root entity.</param>
        /// <param name="ids">The target ids, each once.</param>
        /// <param name="action">The operation's access action.</param>
        /// <param name="children">The child collections to load with the rows, dotted.</param>
        /// <param name="read">Receives the visible rows and the grant's count, null when the grant admits every row.</param>
        /// <returns>Reads the results into <paramref name="read" /> once the batch ran.</returns>
        /// <exception cref="ForbiddenException">The connected user's set no longer grants the action.</exception>
        protected Action ComposeTargets<TEntity, TKey>(
            IDataBatch batch, ConnectedUser user, QueryexSchemaMap map, EntityMetadata root, TKey[] ids, string action, IReadOnlyList<string> children, Action<IReadOnlyList<TEntity>, int?> read)
            where TEntity : Entity<TKey>
            where TKey : struct, IBinaryInteger<TKey>
        {
            ArgumentNullException.ThrowIfNull(map);
            ArgumentNullException.ThrowIfNull(read);

            // --- The decisions, for the user the batch is composed for ---
            AccessDecision granted = Granted(user, action);
            AccessDecision visible = Decide(user, Resource, AccessActions.Read);
            EntityDescriptor descriptor = map.Descriptor(root);
            string key = descriptor.Key.Name;
            IReadOnlyList<KeySetRestriction> restrictions = [new KeySetRestriction(key, KeyTables.Bind(batch, ids).Name)];

            // --- The rows, every mapped property, unless the caller reads none ---
            BatchResult<EntityQueryResult<TEntity>>? rows = visible.Outcome == AccessOutcome.Denied
                ? null
                : batch.Query(new EntityQuery<TEntity>
                {
                    Select = string.Join(", ", map.Declared(root).Select(static property => property.Name)),
                    AllProperties = true,
                    Filter = visible.Filter,
                    Restrictions = restrictions,
                    Children = children,
                    OrderBy = key,
                });

            // --- The count under the grant's filter ---
            BatchResult<int>? count = granted.Outcome == AccessOutcome.Filtered
                ? batch.Count(new QuerySpec { Root = descriptor, Select = key, Filter = granted.Filter, Restrictions = restrictions }, arguments: null, cap: ids.Length)
                : null;
            return () => read(rows?.Value.Entities ?? [], count?.Value);
        }

        /// <summary>
        ///     Settles the targets' pre-check: an id the load missed is not found, hidden and absent
        ///     alike, before a grant's count below the loaded rows refuses the operation.
        /// </summary>
        /// <typeparam name="TEntity">The stack's entity type.</typeparam>
        /// <typeparam name="TKey">The stack's key type.</typeparam>
        /// <param name="ids">The target ids, each once, in request order.</param>
        /// <param name="loaded">The visible rows.</param>
        /// <param name="granted">The grant's count; null when the grant admits every row.</param>
        /// <param name="action">The operation's access action.</param>
        /// <returns>The rows, in the ids' order.</returns>
        /// <exception cref="NotFoundException">An id is absent or hidden.</exception>
        /// <exception cref="ForbiddenException">A visible row is outside the grant's filter.</exception>
        protected IReadOnlyList<TEntity> SettleTargets<TEntity, TKey>(TKey[] ids, IReadOnlyList<TEntity> loaded, int? granted, string action)
            where TEntity : Entity<TKey>
            where TKey : struct, IBinaryInteger<TKey>
        {
            ArgumentNullException.ThrowIfNull(ids);
            ArgumentNullException.ThrowIfNull(loaded);
            var byId = loaded.ToDictionary(static row => row.Id);
            string[] missing = [.. ids.Where(id => !byId.ContainsKey(id)).Select(static id => id.ToString(null, CultureInfo.InvariantCulture))];
            return missing.Length > 0
                ? throw new NotFoundException(Resource, missing)
                : granted < loaded.Count
                ? throw new ForbiddenException(ForbiddenCode, Resource, action, ForbiddenReason.OutsideFilter)
                : [.. ids.Select(id => byId[id])];
        }

        /// <summary>Calls the service hook, then every effect in registration order, on a persist context.</summary>
        /// <typeparam name="TEntity">The stack's entity type.</typeparam>
        /// <typeparam name="TKey">The stack's key type.</typeparam>
        /// <param name="behavior">The service's hooks.</param>
        /// <param name="context">The persist context, whose batch is the participants' recording view.</param>
        /// <returns>A task that completes when every participant returned.</returns>
        protected async Task ContributeAsync<TEntity, TKey>(IEntityBehavior<TEntity, TKey> behavior, PersistContext<TEntity> context)
        {
            ArgumentNullException.ThrowIfNull(behavior);

            // A participant only appends: a round trip of its own is refused before it is sent.
            using (ComposeFence.Close(static () => new InvalidOperationException(
                "A persist participant appends to the operation's batch and performs no I/O of its own. Load what it needs in the validation rounds, or append a read whose result it reads after the commit.")))
            {
                await behavior.ContributeAsync(context).ConfigureAwait(false);
                foreach (IPersistEffect<TEntity> effect in Services.Services.GetServices<IPersistEffect<TEntity>>())
                {
                    await effect.ContributeAsync(context).ConfigureAwait(false);
                }
            }
        }

        // Spec 0015 §13.2: the hook, then every effect, after the commit is acknowledged and outside
        // any transaction. A failure is logged with the operation's trace id and counted by the
        // component's type name; it never changes the response and is never retried.
        /// <summary>Runs the post-commit phase: the service hook, then every effect, each failure contained.</summary>
        /// <typeparam name="TEntity">The stack's entity type.</typeparam>
        /// <typeparam name="TKey">The stack's key type.</typeparam>
        /// <param name="behavior">The service's hooks.</param>
        /// <param name="outcome">What committed.</param>
        /// <returns>A task that completes when every participant returned.</returns>
        protected async Task AfterCommitAsync<TEntity, TKey>(IEntityBehavior<TEntity, TKey> behavior, PersistOutcome<TEntity> outcome)
        {
            ArgumentNullException.ThrowIfNull(behavior);
            await ContainedAsync(behavior.GetType(), () => behavior.AfterCommitAsync(outcome)).ConfigureAwait(false);
            foreach (IPersistEffect<TEntity> effect in Services.Services.GetServices<IPersistEffect<TEntity>>())
            {
                await ContainedAsync(effect.GetType(), () => effect.AfterCommitAsync(outcome)).ConfigureAwait(false);
            }
        }

        /// <summary>The tenant's version tags after a commit: the batch's, else this instance's last snapshot when no batch answered.</summary>
        /// <param name="committed">The outcome of the batch that committed; null when none answered.</param>
        /// <returns>The tags.</returns>
        protected VersionTagSnapshot TagsAfter(BatchOutcome? committed)
        {
            return committed?.VersionTags
                ?? Services.Services.GetRequiredService<IVersionTagSnapshots>().Current(Services.Accessor.Current.RequireTenant().Id);
        }

        /// <summary>The ids of a request, each once, in the order first named.</summary>
        /// <typeparam name="TKey">The key type.</typeparam>
        /// <param name="ids">The ids.</param>
        /// <returns>The distinct ids.</returns>
        protected static TKey[] DistinctIds<TKey>(IReadOnlyList<TKey> ids)
            where TKey : struct
        {
            return [.. ids.Distinct()];
        }

        /// <summary>A stack's key read back from a receipt, which boxes an int or a long.</summary>
        /// <typeparam name="TKey">The stack's key type.</typeparam>
        /// <param name="boxed">The boxed key.</param>
        /// <returns>The key.</returns>
        /// <exception cref="InvalidOperationException">The value is neither an int nor a long.</exception>
        protected static TKey KeyOf<TKey>(object boxed)
            where TKey : struct, IBinaryInteger<TKey>
        {
            return boxed switch
            {
                int narrow => TKey.CreateChecked(narrow),
                long wide => TKey.CreateChecked(wide),
                _ => throw new InvalidOperationException($"A receipt holds int or long keys, not {boxed.GetType().Name}."),
            };
        }

        /// <summary>The caller's read of a reference target or of a visible load's entity.</summary>
        /// <param name="Decision">
        ///     The decision on the entity: its own, its owner's with the filter rebased onto it, or
        ///     unrestricted for an entity no grant can reach.
        /// </param>
        /// <param name="Securable">The read securable the decision was taken on, whose sensitivity asks for a step-up; null when no grant reaches the entity.</param>
        protected readonly record struct TargetRead(AccessDecision Decision, SecurableDescriptor? Securable)
        {
            /// <summary>Whether the caller reads no row of the entity, so every reference to it is not found.</summary>
            public bool IsDenied => Decision.Outcome == AccessOutcome.Denied;

            /// <summary>Whether the read is allowed under a sensitive securable, so it asks for the step-up a direct read asks for.</summary>
            public bool IsSensitive => !IsDenied && Securable is { IsSensitive: true };
        }

        /// <summary>The pipeline's own loads of a round, composed beside the validators'.</summary>
        /// <param name="compose">Composes the loads and returns what reads their results into the operation.</param>
        protected sealed class PipelineLoad(Func<IDataBatch, Action> compose) : ContextLoader.LoadGroup
        {
            /// <inheritdoc />
            public override Action Compose(IDataBatch batch)
            {
                return compose(batch);
            }

            /// <inheritdoc />
            public override void Finish()
            {
                // The readers fill the operation's own state: no ref waits on this group.
            }
        }
    }
}
