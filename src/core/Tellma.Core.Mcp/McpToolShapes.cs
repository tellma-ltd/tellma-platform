// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text.Json;
using Tellma.Core.Abstractions.Tenancy;

namespace Tellma.Core.Mcp
{
    /// <summary>What <c>tellma_whoami</c> answers: the caller, the tenant, what the caller may do there, and the version tags.</summary>
    /// <param name="User">The caller.</param>
    /// <param name="Tenant">The tenant the server serves.</param>
    /// <param name="Access">What the caller may do, entity by entity; sensitive operations are left out.</param>
    /// <param name="Tags">The version tags the caller's view was read at, by item.</param>
    public sealed record WhoamiResult(
        WhoamiUser User,
        WhoamiTenant Tenant,
        IReadOnlyList<WhoamiEntityAccess> Access,
        IReadOnlyDictionary<string, string> Tags);

    /// <summary>The caller of a tool.</summary>
    /// <param name="Id">The user's id in the tenant.</param>
    /// <param name="Name">The user's name in their content language.</param>
    /// <param name="Email">The user's email address, if any.</param>
    /// <param name="Language">The user's content language.</param>
    /// <param name="Calendar">The user's calendar.</param>
    /// <param name="TimeZone">The user's display time zone.</param>
    public sealed record WhoamiUser(int Id, string Name, string? Email, string Language, string Calendar, string TimeZone);

    /// <summary>The tenant a server serves.</summary>
    /// <param name="Id">The tenant's id.</param>
    /// <param name="Name">The tenant's name in the caller's content language.</param>
    /// <param name="Category">Whether the tenant is live or a sandbox.</param>
    /// <param name="Languages">The tenant's content languages, the primary first.</param>
    /// <param name="Calendars">The tenant's calendars, the primary first.</param>
    /// <param name="TimeZone">The tenant's time zone.</param>
    public sealed record WhoamiTenant(
        int Id,
        string Name,
        TenantCategory Category,
        IReadOnlyList<string> Languages,
        IReadOnlyList<string> Calendars,
        string TimeZone);

    /// <summary>What the caller may do on one entity.</summary>
    /// <param name="Entity">The entity's name.</param>
    /// <param name="Operations">The standard operations granted: <c>Read</c>, <c>Save</c>, <c>Delete</c>, <c>Activate</c>.</param>
    /// <param name="Actions">The entity's other actions granted.</param>
    /// <param name="Filtered">The operations and actions granted under a row-level filter, so only some rows are reached.</param>
    public sealed record WhoamiEntityAccess(
        string Entity,
        IReadOnlyList<string> Operations,
        IReadOnlyList<string> Actions,
        IReadOnlyList<string> Filtered);

    /// <summary>The arguments of <c>tellma_describe</c>.</summary>
    /// <param name="Entity">The entity to describe; null for the catalogue.</param>
    /// <param name="Search">Words the catalogue's names, titles and descriptions are filtered by; null for the whole catalogue.</param>
    public sealed record DescribeInput(string? Entity, string? Search);

    /// <summary>The arguments of <c>tellma_query</c>.</summary>
    /// <param name="Entity">The entity, or a child entity, to query.</param>
    /// <param name="Select">The columns; null for the entity's default.</param>
    /// <param name="Filter">The filter; null for every row.</param>
    /// <param name="OrderBy">The order; null for the entity's default.</param>
    /// <param name="Skip">The rows to skip.</param>
    /// <param name="Top">The rows to return; null for the server's default.</param>
    /// <param name="Arguments">The values of the parameters the expressions declare.</param>
    /// <param name="IncludeCount">Whether to count every row the filter matches.</param>
    /// <param name="IncludeInactive">Whether an activatable entity's inactive rows are returned too.</param>
    /// <param name="Format">How the rows are rendered.</param>
    public sealed record QueryInput(
        string Entity,
        string? Select,
        string? Filter,
        string? OrderBy,
        int Skip = 0,
        int? Top = null,
        IReadOnlyDictionary<string, JsonElement>? Arguments = null,
        bool IncludeCount = false,
        bool IncludeInactive = false,
        QueryFormat Format = QueryFormat.Table);

    /// <summary>The arguments of <c>tellma_aggregate</c>.</summary>
    /// <param name="Entity">The entity, or a child entity, to aggregate.</param>
    /// <param name="Select">The grouping keys and the aggregations.</param>
    /// <param name="Filter">The filter applied before grouping; null for every row.</param>
    /// <param name="Having">The filter applied to the groups; null for every group.</param>
    /// <param name="OrderBy">The order of the groups; null for the server's.</param>
    /// <param name="Skip">The groups to skip.</param>
    /// <param name="Top">The groups to return; null for the server's default.</param>
    /// <param name="Arguments">The values of the parameters the expressions declare.</param>
    /// <param name="IncludeInactive">Whether an activatable entity's inactive rows are aggregated too.</param>
    /// <param name="Format">How the groups are rendered.</param>
    public sealed record AggregateInput(
        string Entity,
        string Select,
        string? Filter,
        string? Having,
        string? OrderBy,
        int Skip = 0,
        int? Top = null,
        IReadOnlyDictionary<string, JsonElement>? Arguments = null,
        bool IncludeInactive = false,
        QueryFormat Format = QueryFormat.Table);

    /// <summary>How <c>tellma_query</c> and <c>tellma_aggregate</c> render their rows.</summary>
    public enum QueryFormat
    {
        /// <summary>A Markdown table, one row per line.</summary>
        Table,

        /// <summary>A JSON array of objects keyed by column name.</summary>
        Objects,
    }

    /// <summary>The arguments of <c>tellma_get</c>.</summary>
    /// <param name="Entity">The entity.</param>
    /// <param name="Ids">The records' ids; exactly one when <paramref name="Collection" /> is given.</param>
    /// <param name="Include">The details extras to return beside the records.</param>
    /// <param name="Collection">A child collection to page through, by its JSON name; null for whole records.</param>
    /// <param name="Skip">The collection's rows to skip, with <paramref name="Collection" />.</param>
    /// <param name="Top">The rows of each child collection to return; null for the server's default.</param>
    public sealed record GetInput(
        string Entity,
        IReadOnlyList<long> Ids,
        IReadOnlyList<string>? Include,
        string? Collection = null,
        int Skip = 0,
        int? Top = null);
}
