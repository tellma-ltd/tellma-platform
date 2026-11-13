// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Queryex;

namespace Tellma.Core.Access
{
    // The build is spec 0014 §6.2; the drift policy and its reporting are §9 and §13.
    /// <summary>
    ///     Builds a caller's <see cref="UserAccess" /> from the stored permissions the connect prologue
    ///     loaded: each row resolved against the securable registry, each distinct filter validated
    ///     once against the tenant's current schema under its own stamp, and every row that resolves
    ///     to nothing kept as a problem that grants nothing.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A grant carries its pair as registered (an alias resolved, the registered spelling),
    ///         its filter text interned across sets, its stamp, and the window of the membership that
    ///         brings it; a public role's grant has no window, and an ended membership grants nothing.
    ///         Exact duplicates are one grant, and grants are ordered by role, permission and window,
    ///         whatever order the rows arrived in.
    ///     </para>
    ///     <para>
    ///         Each problem is logged and counted once per permission and problem for each version
    ///         of the tenant's permissions, however many users' sets meet it.
    ///     </para>
    /// </remarks>
    /// <param name="classifier">Classifies each stored permission.</param>
    /// <param name="metrics">The set's instruments.</param>
    /// <param name="loggerFactory">Creates the access logger.</param>
    /// <param name="timeProvider">The host's clock, for the build time and its duration.</param>
    internal sealed class UserAccessBuilder(
        PermissionClassifier classifier,
        AccessMetrics metrics,
        ILoggerFactory loggerFactory,
        TimeProvider timeProvider)
    {
        /// <summary>The most drift reports remembered before starting over, which reports each again at most once more.</summary>
        internal const int MaxReportedDrift = 16384;

        /// <summary>The access logger.</summary>
        private readonly ILogger _logger = loggerFactory.CreateLogger(AccessEvents.Category);

        /// <summary>The problems already reported, by tenant, permissions version, permission and problem.</summary>
        private readonly ConcurrentDictionary<(int TenantId, Guid Tag, int PermissionId, AccessProblemCode Code), byte> _reported = new();

        /// <summary>The shared filter texts and role names.</summary>
        internal AccessInterner Interner { get; } = new();

        /// <summary>Builds one caller's set.</summary>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="userId">The user; positive.</param>
        /// <param name="tag">The tenant's <c>permissions</c> version tag the rows were read under.</param>
        /// <param name="userTag">The user's permissions version tag the rows were read under.</param>
        /// <param name="rows">The stored permissions of the user's active roles, one per membership window, and of every active public role.</param>
        /// <param name="schema">The tenant's current query schema.</param>
        /// <returns>The set, computed now.</returns>
        /// <exception cref="ArgumentOutOfRangeException">The user id is not positive.</exception>
        /// <exception cref="ArgumentNullException">The rows or the schema are null.</exception>
        /// <exception cref="ArgumentException">A row is null.</exception>
        public UserAccess Build(int tenantId, int userId, Guid tag, Guid userTag, IReadOnlyList<PermissionRow> rows, QueryexSchema schema)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(userId);
            ArgumentNullException.ThrowIfNull(rows);
            ArgumentNullException.ThrowIfNull(schema);

            long started = timeProvider.GetTimestamp();
            DateTimeOffset now = timeProvider.GetUtcNow();

            // --- Each row once: its classification shared by its windows, each filter validated once ---
            Dictionary<(string? Resource, string? Action, string? Filter, int? Version, bool IsPublic), PermissionClassification> classified = [];
            Dictionary<(string Root, string Text, int Version), PermissionClassifier.Validation> validations = [];
            HashSet<RoleAccessGrant> grants = [];
            Dictionary<(int PermissionId, AccessProblemCode Code), (AccessProblem Problem, Exception? Failure)> problems = [];

            foreach (PermissionRow row in rows)
            {
                if (row is null)
                {
                    throw new ArgumentException("The permission rows must not contain a null row.", nameof(rows));
                }

                // --- A public role has no membership and no window; an ended membership grants nothing again ---
                DateTimeOffset? startsAt = row.IsPublic ? null : row.StartsAt;
                DateTimeOffset? endsAt = row.IsPublic ? null : row.EndsAt;
                if (endsAt is DateTimeOffset end && end <= now)
                {
                    continue;
                }

                (string? Resource, string? Action, string? Filter, int? Version, bool IsPublic) key = (row.Resource, row.Action, row.Filter, row.FilterQueryexVersion, row.IsPublic);
                if (!classified.TryGetValue(key, out PermissionClassification? classification))
                {
                    classification = classifier.Classify(row.Resource, row.Action, row.Filter, row.FilterQueryexVersion, row.IsPublic, schema, validations);
                    classified.Add(key, classification);
                }

                // --- A row that resolves to nothing is one problem, however many windows bring it ---
                if (classification.Problem is AccessProblemCode code)
                {
                    _ = problems.TryAdd(
                        (row.PermissionId, code),
                        (new AccessProblem(row.PermissionId, row.RoleId, classification.Resource, classification.Action, code, classification.Diagnostics), classification.Failure));
                    continue;
                }

                _ = grants.Add(new RoleAccessGrant(
                    classification.Resource,
                    classification.Action,
                    row.PermissionId,
                    row.RoleId,
                    Interner.Intern(row.RoleName ?? string.Empty),
                    row.IsPublic,
                    classification.Filter is string filter ? Interner.Intern(filter) : null,
                    classification.FilterQueryexVersion,
                    startsAt,
                    endsAt));
            }

            // --- The set, in an order that never depends on the rows' ---
            List<RoleAccessGrant> ordered = [.. grants];
            ordered.Sort(CompareGrants);
            List<AccessProblem> found = [.. problems.Values.Select(static problem => problem.Problem)];
            found.Sort(static (left, right) => left.PermissionId != right.PermissionId
                ? left.PermissionId.CompareTo(right.PermissionId)
                : left.Code.CompareTo(right.Code));

            UserAccess access = new()
            {
                TenantId = tenantId,
                UserId = userId,
                Tag = tag,
                UserTag = userTag,
                ComputedAt = now,
                Grants = ordered,
                Problems = found,
            };

            // --- Reported once per permission and problem for each version of the tenant's permissions ---
            foreach ((AccessProblem problem, Exception? failure) in problems.Values)
            {
                Report(tenantId, userId, tag, problem, failure);
            }

            TimeSpan elapsed = timeProvider.GetElapsedTime(started);
            metrics.RecordBuild(elapsed, ordered.Count);
            AccessLog.AccessSetBuilt(_logger, tenantId, userId, ordered.Count, found.Count, elapsed.TotalMilliseconds);
            return access;
        }

        /// <summary>Orders grants by role, permission, then window: an open start first, an open end last.</summary>
        /// <param name="left">One grant.</param>
        /// <param name="right">The other.</param>
        /// <returns>The order.</returns>
        private static int CompareGrants(RoleAccessGrant left, RoleAccessGrant right)
        {
            int order = left.RoleId.CompareTo(right.RoleId);
            if (order == 0)
            {
                order = left.PermissionId.CompareTo(right.PermissionId);
            }

            if (order == 0)
            {
                order = (left.StartsAt?.UtcTicks ?? long.MinValue).CompareTo(right.StartsAt?.UtcTicks ?? long.MinValue);
            }

            return order != 0 ? order : (left.EndsAt?.UtcTicks ?? long.MaxValue).CompareTo(right.EndsAt?.UtcTicks ?? long.MaxValue);
        }

        /// <summary>Logs and counts a problem the first time this process meets it under a version of the tenant's permissions.</summary>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="userId">The user whose set met it.</param>
        /// <param name="tag">The tenant's permissions version tag.</param>
        /// <param name="problem">The problem.</param>
        /// <param name="failure">The engine's exception behind it, if any.</param>
        private void Report(int tenantId, int userId, Guid tag, AccessProblem problem, Exception? failure)
        {
            // --- Bounded: a full memory starts over, reporting each problem again at most once more ---
            if (_reported.Count >= MaxReportedDrift)
            {
                _reported.Clear();
            }

            if (!_reported.TryAdd((tenantId, tag, problem.PermissionId, problem.Code), 0))
            {
                return;
            }

            metrics.CountUnresolved(problem.Code);
            AccessLog.PermissionDrift(
                _logger,
                failure,
                tenantId,
                userId,
                problem.PermissionId,
                problem.RoleId,
                problem.Resource,
                problem.Action,
                AccessMetrics.ProblemValue(problem.Code),
                string.Join(", ", problem.Diagnostics.Select(static diagnostic => diagnostic.Code)));
        }
    }
}
