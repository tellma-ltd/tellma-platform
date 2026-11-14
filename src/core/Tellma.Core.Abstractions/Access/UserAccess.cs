// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace Tellma.Core.Abstractions.Access
{
    // The cached set is spec 0014 §5.1 and §6.2; Decide is §5.2 and §5.5.
    /// <summary>
    ///     One caller's resolved permissions in one tenant: every grant of a membership that has not
    ///     ended, future ones included, each with its window; every grant of an active public role;
    ///     and the stored permissions that grant nothing. Immutable but for <see cref="ValidatedAt" />.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <see cref="Decide" /> is a pure function of the set and the time it is given: no I/O,
    ///         no clock of its own, no ambient state. A membership window opening or closing therefore
    ///         needs no reload: a decision at 15:00 drops a membership that ends at 15:00.
    ///     </para>
    ///     <para>
    ///         The set memoises the direct side of each securable's decision until its next window
    ///         boundary; the memo is never shared with a copy. Two sets are equal only when they are
    ///         the same instance: a set is a cache entry, not a value.
    ///     </para>
    ///     <para>
    ///         A set without a user (<see cref="UserId" /> not positive) denies everything, bespoke
    ///         criteria and owners included, unless it is the system set.
    ///     </para>
    /// </remarks>
    public sealed record UserAccess
    {
        /// <summary>The most securables whose direct side one set memoises; beyond it, decisions are composed afresh.</summary>
        private const int MaxMemoEntries = 4096;

        /// <summary>The memoised direct sides, by securable, each with the interval of time it holds for.</summary>
        private readonly ConcurrentDictionary<SecurableDescriptor, MemoEntry> _memo = new();

        /// <summary>Every window boundary of the grants, as sorted distinct UTC ticks; computed on first use.</summary>
        private long[]? _boundaries;

        /// <summary>The latest instant a passing prologue confirmed the set, as UTC ticks; 0 until the first.</summary>
        private long _validatedTicks;

        /// <summary>Creates an empty set; the initializers fill it.</summary>
        public UserAccess()
        {
        }

        /// <summary>Copies a set for a <c>with</c> expression: the same content and validation, a memo of its own.</summary>
        /// <param name="original">The set copied.</param>
        private UserAccess(UserAccess original)
        {
            TenantId = original.TenantId;
            UserId = original.UserId;
            Tag = original.Tag;
            UserTag = original.UserTag;
            ComputedAt = original.ComputedAt;
            IsSystem = original.IsSystem;
            Grants = original.Grants;
            Problems = original.Problems;
            ActionResources = original.ActionResources;
            _memo = new();
            _validatedTicks = Volatile.Read(ref original._validatedTicks);
        }

        /// <summary>The system set: unrestricted on every securable, never loaded, never denied.</summary>
        public static UserAccess System { get; } = new()
        {
            UserId = WellKnownIds.SystemUserId,
            IsSystem = true,
            ComputedAt = DateTimeOffset.UnixEpoch,
        };

        /// <summary>The tenant the set belongs to.</summary>
        public int TenantId { get; init; }

        /// <summary>The user the set belongs to; not positive for a set without a user, which denies everything.</summary>
        public int UserId { get; init; }

        /// <summary>The tenant's <c>permissions</c> version tag the set was built under; the set is valid only while it matches.</summary>
        public Guid Tag { get; init; }

        /// <summary>The user's permissions version tag the set was built under; the set is valid only while it matches.</summary>
        public Guid UserTag { get; init; }

        /// <summary>When the set was built.</summary>
        public DateTimeOffset ComputedAt { get; init; }

        /// <summary>
        ///     The last time a passing prologue confirmed both tags, in UTC; <see cref="ComputedAt" />
        ///     until the first. It bounds how long a denial is trusted without asking the database
        ///     again.
        /// </summary>
        public DateTimeOffset ValidatedAt => new(Math.Max(Volatile.Read(ref _validatedTicks), ComputedAt.UtcTicks), TimeSpan.Zero);

        /// <summary>
        ///     Whether this is the system set, unrestricted on every securable. Only
        ///     <see cref="System" /> and its copies are: no other code can mint a set that holds
        ///     everything.
        /// </summary>
        public bool IsSystem { get; private init; }

        /// <summary>The grants: every membership that has not ended, future ones included, and every active public role's.</summary>
        public IReadOnlyList<RoleAccessGrant> Grants
        {
            get;
            init
            {
                field = AccessLists.Copy(value, nameof(Grants));
                _boundaries = null;
            }
        } = [];

        /// <summary>The stored permissions that grant nothing, one per permission and problem.</summary>
        public IReadOnlyList<AccessProblem> Problems
        {
            get;
            init => field = AccessLists.Copy(value, nameof(Problems));
        } = [];

        /// <summary>
        ///     The resources that register each action, from the securable registry the set was
        ///     built against: a grant on the wildcard resource and another action than <c>Read</c>
        ///     implies reading only those. Empty by default, which implies no such read.
        /// </summary>
        public ActionResourceMap ActionResources
        {
            get;
            init => field = value ?? throw new ArgumentNullException(nameof(ActionResources));
        } = ActionResourceMap.Empty;

        /// <summary>Decides what the caller may do with one securable at one instant.</summary>
        /// <param name="securable">The registered securable; never a wildcard.</param>
        /// <param name="criteria">The bespoke criteria of every provider registered for the securable's resource.</param>
        /// <param name="owner">
        ///     For a securable with an owner, this caller's decision on the owner's resource and the
        ///     same action, taken first; null for any other securable. The evaluator passes a denial
        ///     for an owner pair the registry does not hold.
        /// </param>
        /// <param name="now">The instant decided, the host's clock; every window is judged against it.</param>
        /// <returns>The decision.</returns>
        /// <exception cref="ArgumentNullException">The securable or the criteria are null.</exception>
        /// <exception cref="ArgumentException">
        ///     The securable names a wildcard, the criteria hold a null item, or the owner decision is
        ///     missing, superfluous, or for another pair than the securable's owner and action.
        /// </exception>
        public AccessDecision Decide(SecurableDescriptor securable, IReadOnlyList<AccessCriterion> criteria, AccessDecision? owner, DateTimeOffset now)
        {
            AccessComposition.RequireArguments(securable, criteria, owner);

            // --- Step 1: the system set holds everything; a set without a user holds nothing ---
            if (IsSystem)
            {
                return AccessComposition.SystemDecision(securable);
            }

            if (UserId <= 0)
            {
                return AccessDecision.Deny(securable.Resource, securable.Action);
            }

            // --- Steps 2 to 5 over the grants current at now, memoised until the next boundary ---
            return AccessComposition.Finish(securable, DirectAt(securable, now), criteria, owner);
        }

        /// <summary>The next instant after <paramref name="now" /> at which a grant's window opens or closes.</summary>
        /// <param name="now">The instant.</param>
        /// <returns>The earliest start or end among the grants after now, in UTC; null when none is.</returns>
        public DateTimeOffset? NextBoundary(DateTimeOffset now)
        {
            long next = IntervalAt(now.UtcTicks).Until;
            return next == long.MaxValue ? null : new DateTimeOffset(next, TimeSpan.Zero);
        }

        /// <summary>
        ///     Records that a passing prologue confirmed the set at an instant. Monotonic and
        ///     thread-safe: an earlier instant than the one recorded changes nothing. The system set,
        ///     which is never loaded, ignores it.
        /// </summary>
        /// <param name="at">The instant the prologue passed.</param>
        public void MarkValidated(DateTimeOffset at)
        {
            if (IsSystem)
            {
                return;
            }

            // --- Compare and swap until the recorded instant is at least the new one ---
            long ticks = at.UtcTicks;
            long seen = Volatile.Read(ref _validatedTicks);
            while (ticks > seen)
            {
                long previous = Interlocked.CompareExchange(ref _validatedTicks, ticks, seen);
                if (previous == seen)
                {
                    return;
                }

                seen = previous;
            }
        }

        /// <summary>Whether another set is this one: sets compare by identity.</summary>
        /// <param name="other">The other set.</param>
        /// <returns>True when both are the same instance.</returns>
        public bool Equals(UserAccess? other)
        {
            return ReferenceEquals(this, other);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return RuntimeHelpers.GetHashCode(this);
        }

        /// <summary>The direct side of a securable's decision at an instant: memoised, else composed and memoised.</summary>
        /// <param name="securable">The securable.</param>
        /// <param name="now">The instant.</param>
        /// <returns>The direct side.</returns>
        private AccessComposition.DirectSide DirectAt(SecurableDescriptor securable, DateTimeOffset now)
        {
            // --- A memoised side holds for the whole interval between two boundaries ---
            long ticks = now.UtcTicks;
            if (_memo.TryGetValue(securable, out MemoEntry? memo) && memo.From <= ticks && ticks < memo.Until)
            {
                return memo.Side;
            }

            // --- The grants whose window contains now; the window is the only use of the time ---
            List<RoleAccessGrant> current = [];
            foreach (RoleAccessGrant grant in Grants)
            {
                if (grant.IsCurrentAt(now))
                {
                    current.Add(grant);
                }
            }

            AccessComposition.DirectSide side = AccessComposition.Direct(securable, current, Problems, ActionResources);

            // --- Memoised for the interval; a full memo composes afresh rather than growing ---
            (long from, long until) = IntervalAt(ticks);
            if (_memo.Count < MaxMemoEntries || _memo.ContainsKey(securable))
            {
                _memo[securable] = new MemoEntry(from, until, side);
            }

            return side;
        }

        /// <summary>The interval between two window boundaries that contains an instant.</summary>
        /// <param name="ticks">The instant, as UTC ticks.</param>
        /// <returns>
        ///     The latest boundary at or before the instant (<see cref="long.MinValue" /> when none
        ///     is) and the earliest after it (<see cref="long.MaxValue" /> when none is). Which grants
        ///     are current never changes inside it.
        /// </returns>
        private (long From, long Until) IntervalAt(long ticks)
        {
            long[] boundaries = Boundaries();
            int index = Array.BinarySearch(boundaries, ticks);
            int after = index >= 0 ? index + 1 : ~index;
            int atOrBefore = after - 1;
            return (
                atOrBefore >= 0 ? boundaries[atOrBefore] : long.MinValue,
                after < boundaries.Length ? boundaries[after] : long.MaxValue);
        }

        /// <summary>Every start and end of the grants' windows, as sorted distinct UTC ticks.</summary>
        /// <returns>The boundaries.</returns>
        private long[] Boundaries()
        {
            // Computed once; two threads racing here compute the same array, so either may win.
            if (Volatile.Read(ref _boundaries) is long[] known)
            {
                return known;
            }

            SortedSet<long> boundaries = [];
            foreach (RoleAccessGrant grant in Grants)
            {
                if (grant.StartsAt is DateTimeOffset start)
                {
                    _ = boundaries.Add(start.UtcTicks);
                }

                if (grant.EndsAt is DateTimeOffset end)
                {
                    _ = boundaries.Add(end.UtcTicks);
                }
            }

            long[] computed = [.. boundaries];
            Volatile.Write(ref _boundaries, computed);
            return computed;
        }

        /// <summary>A memoised direct side and the interval of time it holds for.</summary>
        /// <param name="From">The interval's start, inclusive, as UTC ticks.</param>
        /// <param name="Until">The interval's end, exclusive, as UTC ticks.</param>
        /// <param name="Side">The direct side.</param>
        private sealed record MemoEntry(long From, long Until, AccessComposition.DirectSide Side);
    }
}
