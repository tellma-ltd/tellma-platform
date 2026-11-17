// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Options;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Caching;
using Tellma.Core.Tenancy;

namespace Tellma.Core.Access
{
    // The evaluator is spec 0014 §5.1; the deny re-check is §6.4, owners §5.5, the witness §4.6
    // layer 4, the step-up bar §4.3 and the decision counter §13.
    /// <summary>
    ///     Decides what the scope's caller may do with a securable, from the connected user's cached
    ///     set, the bespoke criteria registered for the securable's resource (read on every
    ///     evaluation) and, for a child securable, the decision on its owner. One per scope.
    /// </summary>
    /// <remarks>
    ///     A scope that names no caller is denied everything before any decision is composed, since a
    ///     bespoke criterion would otherwise grant on its own. The deny re-check runs at most once per
    ///     scope, and another user's load at most once per scope and user, however many calls
    ///     race for either; a re-check that fails, cancelled or refused, leaves the scope's one
    ///     re-check to the next stale denial. A load runs for the scope, not for the call that
    ///     started it: each call waits on it under its own cancellation, and disposing the
    ///     evaluator, as the scope's end does, cancels it.
    /// </remarks>
    /// <param name="accessor">The scope's request context.</param>
    /// <param name="connection">The scope's connection, which connects the caller, re-checks a denial and loads another user.</param>
    /// <param name="registry">The securable registry.</param>
    /// <param name="criteriaProviders">Every registered bespoke criteria provider.</param>
    /// <param name="witness">The scope's access witness.</param>
    /// <param name="permissions">The process's permission sets, which an invalidation drops from.</param>
    /// <param name="profiles">The process's profiles, which an invalidation drops from.</param>
    /// <param name="connections">The process's connected users, which an invalidation drops from.</param>
    /// <param name="stepUp">The assurance bar of the sensitive securables.</param>
    /// <param name="metrics">The access instruments.</param>
    /// <param name="options">The access options: the fast-deny window.</param>
    /// <param name="time">The host's clock, which every decision is taken at.</param>
    internal sealed class AccessEvaluator(
        IRequestContextAccessor accessor,
        IScopeConnection connection,
        ISecurableRegistry registry,
        IEnumerable<IAccessCriteriaProvider> criteriaProviders,
        AccessWitness witness,
        PermissionsCache permissions,
        ProfileCache profiles,
        ConnectCache connections,
        StepUpBar stepUp,
        AccessMetrics metrics,
        IOptions<AccessOptions> options,
        TimeProvider time) : IAccessEvaluator, IDisposable
    {
        /// <summary>The problem code of a refusal at every platform raise site.</summary>
        private const string ForbiddenCode = "forbidden";

        /// <summary>The set of a scope that names no caller: it holds nothing, criteria included.</summary>
        private static readonly UserAccess NoCaller = new();

        /// <summary>The criteria providers, by resource, ignoring case.</summary>
        private readonly ILookup<string, IAccessCriteriaProvider> _providers =
            (criteriaProviders ?? throw new ArgumentNullException(nameof(criteriaProviders))).ToLookup(static provider => provider.Resource, StringComparer.OrdinalIgnoreCase);

        /// <summary>Guards the loads of other users.</summary>
        private readonly Lock _gate = new();

        /// <summary>The scope's load of each other user's permissions; a null set for an unknown, inactive or system user.</summary>
        private readonly Dictionary<int, Lazy<Task<UserAccess?>>> _targets = [];

        /// <summary>Cancels the scope's loads of other users when the evaluator is disposed.</summary>
        private readonly CancellationTokenSource _lifetime = new();

        /// <summary>1 once the scope's deny re-check started; back to 0 when it failed.</summary>
        private int _rechecked;

        /// <summary>1 once the evaluator is disposed.</summary>
        private int _disposed;

        /// <inheritdoc />
        public async Task<AccessDecision> EvaluateAsync(string resource, string action, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(resource);
            ArgumentNullException.ThrowIfNull(action);
            IReadOnlyList<AccessDecision> decisions = await EvaluateCallerAsync([new SecurableRef(resource, action)], recheckFiltered: false, cancellationToken).ConfigureAwait(false);
            return decisions[0];
        }

        /// <summary>
        ///     Decides one pair for the caller as <see cref="EvaluateAsync(string, string, CancellationToken)" />
        ///     does, but re-checks a filtered decision as well as a denial when the set is older than
        ///     the fast-deny window. A refusal raised before any batch ran needs this, because no
        ///     batch's prologue compares the set's tags for it. The scope still re-checks at most
        ///     once, sharing that one re-check with the denials.
        /// </summary>
        /// <param name="resource">The resource.</param>
        /// <param name="action">The action.</param>
        /// <param name="cancellationToken">Cancels the connection or the re-check.</param>
        /// <returns>The decision, from the re-checked set when a re-check ran.</returns>
        internal async Task<AccessDecision> RecheckAsync(string resource, string action, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(resource);
            ArgumentNullException.ThrowIfNull(action);
            IReadOnlyList<AccessDecision> decisions = await EvaluateCallerAsync([new SecurableRef(resource, action)], recheckFiltered: true, cancellationToken).ConfigureAwait(false);
            return decisions[0];
        }

        /// <inheritdoc />
        public async Task<AccessDecision> RequireAsync(string resource, string action, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(resource);
            ArgumentException.ThrowIfNullOrWhiteSpace(action);
            AccessDecision decision = await EvaluateAsync(resource, action, cancellationToken).ConfigureAwait(false);

            // --- A denial is refused, naming the pair and no role, filter or other user ---
            if (decision.Outcome == AccessOutcome.Denied)
            {
                throw new ForbiddenException(ForbiddenCode, decision.Resource, decision.Action, ForbiddenReason.NoGrant);
            }

            // --- A sensitive pair asks for a recent, stronger sign-in of every caller but the platform's
            // own work: a service account cannot step up, and a scope with no assurance cannot prove one ---
            RequestContext context = accessor.Current;
            if (!context.IsSystem && registry.Find(resource, action) is { IsSensitive: true })
            {
                AssuranceCheck.Ensure(context, stepUp.Requirement, time.GetUtcNow());
            }

            return decision;
        }

        /// <inheritdoc />
        public Task<IReadOnlyList<AccessDecision>> EvaluateAsync(IReadOnlyList<SecurableRef> securables, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(securables);
            SecurableRef[] pairs = [.. securables];
            foreach (SecurableRef pair in pairs)
            {
                if (pair is null)
                {
                    throw new ArgumentException("The securables must not contain a null item.", nameof(securables));
                }

                ArgumentNullException.ThrowIfNull(pair.Resource, nameof(securables));
                ArgumentNullException.ThrowIfNull(pair.Action, nameof(securables));
            }

            return EvaluateCallerAsync(pairs, recheckFiltered: false, cancellationToken);
        }

        /// <inheritdoc />
        public async Task<AccessDecision> EvaluateForAsync(int userId, string resource, string action, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(resource);
            ArgumentNullException.ThrowIfNull(action);

            // --- A pair the registry does not hold answers alike for every user, with no load ---
            if (registry.Find(resource, action) is not SecurableDescriptor securable)
            {
                return Unknown(resource, action);
            }

            RequestContext context = accessor.Current;
            if (!context.IsSystem)
            {
                // --- A scope that names no caller learns nothing of another user: no load, no grant ---
                if (!HasCaller(context))
                {
                    return AccessDecision.Deny(securable.Resource, securable.Action);
                }

                // --- The caller's own id answers from the caller's set, with no re-check ---
                int callerId = context.UserId ?? (await connection.CurrentAsync(cancellationToken).ConfigureAwait(false)).UserId;
                if (callerId == userId)
                {
                    (UserAccess own, _) = await CallerAsync(cancellationToken).ConfigureAwait(false);
                    return DecideFrom(own, securable, time.GetUtcNow());
                }
            }

            // --- Another user: the scope's one load of them; an unknown, inactive or system user, or a
            // scope outside a tenant, is denied everything before any criterion could grant ---
            return userId <= 0 || context.Tenant is null || await TargetAsync(userId, cancellationToken).ConfigureAwait(false) is not UserAccess target
                ? AccessDecision.Deny(securable.Resource, securable.Action)
                : DecideFrom(target, securable, time.GetUtcNow());
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<AccessDecision>> EvaluateAllAsync(CancellationToken cancellationToken = default)
        {
            // --- The set as it is: the summary's own prologue has just validated it (spec 0014 §6.4) ---
            (UserAccess access, _) = await CallerAsync(cancellationToken).ConfigureAwait(false);
            DateTimeOffset now = time.GetUtcNow();
            IReadOnlyList<SecurableDescriptor> all = registry.All;
            var decisions = new AccessDecision[all.Count];
            for (int index = 0; index < decisions.Length; index++)
            {
                decisions[index] = DecideFrom(access, all[index], now);
            }

            return decisions;
        }

        /// <inheritdoc />
        public async Task<UserAccess> GetAccessAsync(CancellationToken cancellationToken = default)
        {
            (UserAccess access, _) = await CallerAsync(cancellationToken).ConfigureAwait(false);
            return access;
        }

        /// <inheritdoc />
        public void Invalidate(int tenantId, int userId)
        {
            // --- The process's entries of the user: the set, the profile and every connection composing them ---
            TenantUserCacheKey key = new(tenantId, userId);
            permissions.Remove(key);
            profiles.Remove(key);
            connections.RemoveUser(tenantId, userId);

            // --- The scope's own load of the user, when the scope is of the same tenant ---
            if (accessor.Current.Tenant?.Id == tenantId)
            {
                lock (_gate)
                {
                    _ = _targets.Remove(userId);
                }
            }
        }

        /// <summary>Cancels the scope's loads of other users that are still running. Idempotent.</summary>
        public void Dispose()
        {
            // The container may hold the evaluator under two registrations, and disposes it once per each.
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _lifetime.Cancel();
                _lifetime.Dispose();
            }
        }

        /// <summary>
        ///     Decides one registered securable from a set at an instant: the owner's decision on the
        ///     same action first, for a child securable, then the set's. A set without a user is
        ///     denied before any criterion is read. Records nothing and counts nothing.
        /// </summary>
        /// <param name="access">The set.</param>
        /// <param name="securable">The registered securable.</param>
        /// <param name="now">The instant decided.</param>
        /// <returns>The decision.</returns>
        internal AccessDecision DecideFrom(UserAccess access, SecurableDescriptor securable, DateTimeOffset now)
        {
            ArgumentNullException.ThrowIfNull(access);
            ArgumentNullException.ThrowIfNull(securable);

            // --- No user: nothing at all, whatever a criterion or an owner would add ---
            if (!access.IsSystem && access.UserId <= 0)
            {
                return AccessDecision.Deny(securable.Resource, securable.Action);
            }

            // --- The owner's decision on the same action, recursively; a chain the registry validated ends ---
            AccessDecision? owner = null;
            if (securable.Owner is SecurableOwner declared)
            {
                owner = registry.Find(declared.Resource, securable.Action) is SecurableDescriptor ownerSecurable
                    ? DecideFrom(access, ownerSecurable, now)
                    : AccessDecision.Deny(declared.Resource, securable.Action);
            }

            return access.Decide(securable, CriteriaOf(securable.Resource), owner, now);
        }

        /// <summary>The bespoke criteria of every provider registered for a resource, read now.</summary>
        /// <param name="resource">The registered resource.</param>
        /// <returns>The criteria; empty when no provider serves the resource.</returns>
        internal IReadOnlyList<AccessCriterion> CriteriaOf(string resource)
        {
            List<AccessCriterion>? criteria = null;
            foreach (IAccessCriteriaProvider provider in _providers[resource])
            {
                IReadOnlyList<AccessCriterion> provided = provider.Criteria;
                if (provided.Count > 0)
                {
                    (criteria ??= []).AddRange(provided);
                }
            }

            return criteria ?? (IReadOnlyList<AccessCriterion>)[];
        }

        /// <summary>
        ///     The denial of a pair the registry does not hold: one problem naming no stored
        ///     permission, for the resource or for the action.
        /// </summary>
        /// <param name="resource">The resource asked.</param>
        /// <param name="action">The action asked.</param>
        /// <returns>The denial.</returns>
        internal AccessDecision Unknown(string resource, string action)
        {
            string? registered = registry.FindResource(resource);
            AccessProblemCode code = registered is null ? AccessProblemCode.UnknownResource : AccessProblemCode.UnknownAction;
            string named = registered ?? resource;
            return new AccessDecision(named, action, AccessOutcome.Denied, null, [], [new AccessProblem(0, 0, named, action, code, [])]);
        }

        /// <summary>Whether a context names a caller: a user or a service account of a tenant, by subject or by id.</summary>
        /// <param name="context">The context.</param>
        /// <returns>True when it does.</returns>
        private static bool HasCaller(RequestContext context)
        {
            return context.Tenant is not null
                && context.Kind is PrincipalKind.User or PrincipalKind.ServiceAccount
                && (context.UserId is not null || context.Subject is not null);
        }

        /// <summary>
        ///     Decides pairs for the caller from one set, re-checks once when a denial is stale (or a
        ///     filter, when asked), records each pair as evaluated and counts each decision.
        /// </summary>
        /// <param name="pairs">The pairs, none null.</param>
        /// <param name="recheckFiltered">Whether a stale filtered decision is re-checked too.</param>
        /// <param name="cancellationToken">Cancels the connection or the re-check.</param>
        /// <returns>One decision per pair, in order.</returns>
        private async Task<IReadOnlyList<AccessDecision>> EvaluateCallerAsync(SecurableRef[] pairs, bool recheckFiltered, CancellationToken cancellationToken)
        {
            // --- Resolved once: a pair the registry does not hold is a denial with its problem ---
            var securables = new SecurableDescriptor?[pairs.Length];
            for (int index = 0; index < securables.Length; index++)
            {
                securables[index] = registry.Find(pairs[index].Resource, pairs[index].Action);
            }

            (UserAccess access, ConnectedUser? user) = await CallerAsync(cancellationToken).ConfigureAwait(false);
            DateTimeOffset now = time.GetUtcNow();
            AccessDecision[] decisions = Decide(access, pairs, securables, now);

            // --- A stale denial, or a stale filter when asked: one prologue-only round trip per
            // scope, then every pair decided again from what it returned (spec 0014 §6.4) ---
            if (user is not null && IsStaleRestriction(access, securables, decisions, now, recheckFiltered) && Interlocked.Exchange(ref _rechecked, 1) == 0)
            {
                ConnectedUser revalidated;
                try
                {
                    revalidated = await connection.RevalidateAsync(user, cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    // A re-check that failed verified no set: the scope's one re-check is left to the
                    // next stale denial, which would otherwise answer from this set to the scope's end.
                    Volatile.Write(ref _rechecked, 0);
                    throw;
                }

                decisions = Decide(revalidated.Access, pairs, securables, time.GetUtcNow());
            }

            // --- The witness and the counter, for the registered pair or the pair as asked ---
            for (int index = 0; index < decisions.Length; index++)
            {
                witness.Record(securables[index]?.Resource ?? pairs[index].Resource, securables[index]?.Action ?? pairs[index].Action);
                metrics.CountDecision(decisions[index].Outcome);
            }

            return decisions;
        }

        /// <summary>Decides every pair from one set at one instant.</summary>
        /// <param name="access">The set.</param>
        /// <param name="pairs">The pairs.</param>
        /// <param name="securables">The registered securable of each pair; null for one the registry does not hold.</param>
        /// <param name="now">The instant.</param>
        /// <returns>The decisions.</returns>
        private AccessDecision[] Decide(UserAccess access, SecurableRef[] pairs, SecurableDescriptor?[] securables, DateTimeOffset now)
        {
            var decisions = new AccessDecision[pairs.Length];
            for (int index = 0; index < decisions.Length; index++)
            {
                decisions[index] = securables[index] is SecurableDescriptor securable
                    ? DecideFrom(access, securable, now)
                    : Unknown(pairs[index].Resource, pairs[index].Action);
            }

            return decisions;
        }

        /// <summary>
        ///     Whether a registered pair was denied, or filtered when that counts, from a set no
        ///     prologue confirmed within the fast-deny window: a loaded set, not the system's and not
        ///     one without a user.
        /// </summary>
        /// <param name="access">The set.</param>
        /// <param name="securables">The registered securables.</param>
        /// <param name="decisions">Their decisions.</param>
        /// <param name="now">The instant decided.</param>
        /// <param name="filtered">Whether a filtered decision counts as a restriction a re-check could lift.</param>
        /// <returns>True when a re-check could change a decision.</returns>
        private bool IsStaleRestriction(UserAccess access, SecurableDescriptor?[] securables, AccessDecision[] decisions, DateTimeOffset now, bool filtered)
        {
            if (access.IsSystem || access.UserId <= 0 || now - access.ValidatedAt <= options.Value.FastDenyWindow)
            {
                return false;
            }

            for (int index = 0; index < decisions.Length; index++)
            {
                if (securables[index] is not null
                    && (decisions[index].Outcome == AccessOutcome.Denied || (filtered && decisions[index].Outcome == AccessOutcome.Filtered)))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        ///     The caller's set: the system set for the platform's own work, the empty set for a scope
        ///     that names no caller, else the connected user's, connecting the scope when it is not yet.
        /// </summary>
        /// <param name="cancellationToken">Cancels the connection.</param>
        /// <returns>The set, and the connected user it belongs to; no user for the system's or the empty set.</returns>
        private async Task<(UserAccess Access, ConnectedUser? User)> CallerAsync(CancellationToken cancellationToken)
        {
            RequestContext context = accessor.Current;
            if (context.IsSystem)
            {
                return (UserAccess.System, null);
            }

            if (!HasCaller(context))
            {
                return (NoCaller, null);
            }

            ConnectedUser user = await connection.CurrentAsync(cancellationToken).ConfigureAwait(false);
            return (user.Access, user);
        }

        /// <summary>
        ///     The scope's load of another user's permissions: the first call starts it, every later
        ///     one shares it. The load runs under the scope's lifetime, so no one call's cancellation
        ///     ends it for the others.
        /// </summary>
        /// <param name="userId">The user.</param>
        /// <param name="cancellationToken">Stops this call's wait; the load goes on for the other calls.</param>
        /// <returns>The set; null for an unknown, inactive or system user.</returns>
        /// <exception cref="ObjectDisposedException">The evaluator is disposed: its scope has ended.</exception>
        private Task<UserAccess?> TargetAsync(int userId, CancellationToken cancellationToken)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            CancellationToken lifetime = _lifetime.Token;
            Lazy<Task<UserAccess?>> load;
            lock (_gate)
            {
                // A load that failed or was cancelled is not shared: the next call loads again.
                if (_targets.TryGetValue(userId, out Lazy<Task<UserAccess?>>? known)
                    && !(known.IsValueCreated && (known.Value.IsFaulted || known.Value.IsCanceled)))
                {
                    load = known;
                }
                else
                {
                    load = new Lazy<Task<UserAccess?>>(() => connection.LoadDetachedAsync(userId, lifetime), LazyThreadSafetyMode.ExecutionAndPublication);
                    _targets[userId] = load;
                }
            }

            // Started outside the lock, once, by whichever call reaches it first; each call waits on its own token.
            return load.Value.WaitAsync(cancellationToken);
        }
    }
}
