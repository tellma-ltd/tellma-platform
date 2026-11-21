// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Access;

namespace Tellma.Core.Caching
{
    // The bump set is spec 0013 §2.5: the written tables' names, the explicit bumps, and `entities`
    // whenever any `entity:*` name is present.
    /// <summary>
    ///     What one batch bumps, resolved once when its epilogue stage starts: the distinct tenant-level
    ///     names its written tables and explicit bumps resolve to, and the user-level rules its written
    ///     tables fire. The in-transaction guard and the bump epilogue both read it, so they agree.
    /// </summary>
    internal sealed class VersionTagBumpPlan
    {
        /// <summary>The plan of a batch that bumps nothing.</summary>
        public static readonly VersionTagBumpPlan None = new([], [], writesUserStamps: false);

        /// <summary>Creates the plan.</summary>
        /// <param name="tenantNames">The distinct tenant-level names, in ordinal order.</param>
        /// <param name="userRules">The user-level rules that fire.</param>
        /// <param name="writesUserStamps">Whether the batch writes <c>core.UserStamps</c>, itself or through a user-level bump.</param>
        private VersionTagBumpPlan(IReadOnlyList<string> tenantNames, IReadOnlyList<UserVersionTagRule> userRules, bool writesUserStamps)
        {
            TenantNames = tenantNames;
            UserRules = userRules;
            WritesUserStamps = writesUserStamps;
        }

        /// <summary>The distinct tenant-level names the batch bumps, in ordinal order.</summary>
        public IReadOnlyList<string> TenantNames { get; }

        /// <summary>The user-level rules the batch's written tables fire.</summary>
        public IReadOnlyList<UserVersionTagRule> UserRules { get; }

        /// <summary>
        ///     The user-level columns the rules bump, each once, in <see cref="UserVersionTagNames" />
        ///     order.
        /// </summary>
        public IReadOnlyList<UserVersionTagNames> UserColumns => [.. UserRules.Select(static rule => rule.Column).Distinct().Order()];

        /// <summary>
        ///     Whether the batch writes <c>core.UserStamps</c>: a statement declares it, or a user-level
        ///     rule fires, whose bump updates it. The guard then reads the caller's stamp row under an
        ///     update lock, so two writes of one stamp row serialise instead of deadlocking.
        /// </summary>
        public bool WritesUserStamps { get; }

        /// <summary>Whether the batch bumps nothing.</summary>
        public bool IsEmpty => TenantNames.Count == 0 && UserRules.Count == 0;

        /// <summary>Resolves a batch's bumps.</summary>
        /// <param name="registry">Which tags the written tables bump, or null when the caching feature is not composed.</param>
        /// <param name="written">The tables the batch's statements declare they write.</param>
        /// <param name="explicitBumps">The tenant-level names the batch bumps explicitly.</param>
        /// <returns>The plan.</returns>
        public static VersionTagBumpPlan Resolve(IVersionTagRegistry? registry, IReadOnlySet<TableName> written, IReadOnlySet<string> explicitBumps)
        {
            ArgumentNullException.ThrowIfNull(written);
            ArgumentNullException.ThrowIfNull(explicitBumps);

            // --- The written tables' names and rules, and the explicit names ---
            VersionTagEffects effects = registry?.Resolve(written) ?? VersionTagEffects.None;
            SortedSet<string> names = new(effects.TenantNames, StringComparer.Ordinal);
            names.UnionWith(explicitBumps);

            // --- Any list's tag implies the composite every client watches ---
            if (names.Any(static name => name.StartsWith(VersionTagNames.EntityPrefix, StringComparison.Ordinal)))
            {
                _ = names.Add(VersionTagNames.Entities);
            }

            bool writesUserStamps = written.Contains(UserStampConfiguration.Table) || effects.UserRules.Count > 0;
            return names.Count == 0 && effects.UserRules.Count == 0 && !writesUserStamps
                ? None
                : new VersionTagBumpPlan([.. names], effects.UserRules, writesUserStamps);
        }
    }
}
