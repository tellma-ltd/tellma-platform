// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Data;

namespace Tellma.Core.Abstractions.Caching
{
    /// <summary>
    ///     Every version tag the tenant database carries and which of them a batch's writes bump: built
    ///     at composition from the model's attributes plus the platform's fixed rules for the
    ///     preference tables that are not entities, and validated at startup.
    /// </summary>
    public interface IVersionTagRegistry
    {
        /// <summary>Every tenant-level tag name, which the migrator seeds.</summary>
        IReadOnlyList<string> Names { get; }

        /// <summary>
        ///     Maps the union of a batch's declared written tables to the tenant-level names it bumps
        ///     and the user-level rules that fire. A cacheable entity's table yields its
        ///     <c>entity:&lt;Name&gt;</c> name and <see cref="VersionTagNames.Entities" />.
        /// </summary>
        /// <param name="writtenTables">The tables the batch's statements declare they write.</param>
        /// <returns>The effects; empty when no written table carries a tag.</returns>
        VersionTagEffects Resolve(IReadOnlySet<TableName> writtenTables);
    }

    /// <summary>What a batch's writes bump: tenant-level names and user-level rules.</summary>
    /// <param name="TenantNames">The distinct tenant-level names the written tables bump.</param>
    /// <param name="UserRules">The user-level rules the written tables fire.</param>
    public sealed record VersionTagEffects(IReadOnlyList<string> TenantNames, IReadOnlyList<UserVersionTagRule> UserRules)
    {
        /// <summary>No effects: the written tables carry no tag.</summary>
        public static VersionTagEffects None { get; } = new([], []);
    }

    /// <summary>
    ///     One user-level rule: rows written to <paramref name="Table" /> bump the
    ///     <paramref name="Column" /> tag of every distinct user in <paramref name="UserIdColumn" />.
    /// </summary>
    /// <param name="Table">The table whose writes fire the rule.</param>
    /// <param name="Column">The user-level tag the rule bumps.</param>
    /// <param name="UserIdColumn">The table's column holding the affected user's id.</param>
    public sealed record UserVersionTagRule(TableName Table, UserVersionTagNames Column, string UserIdColumn);
}
