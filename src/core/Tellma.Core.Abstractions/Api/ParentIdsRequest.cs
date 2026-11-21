// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text.Json;

namespace Tellma.Core.Abstractions.Api
{
    /// <summary>
    ///     The body of a tree stack's <c>get-by-parent-ids</c>: the children of the given parents, and
    ///     the roots on request, as one row set with no count and no ancestors.
    /// </summary>
    /// <param name="ParentIds">The parents' ids; null or empty for the roots. Numbers or strings on the wire, narrowed to the stack's key type.</param>
    /// <param name="Select">The Queryex select; null for the stack's default select.</param>
    /// <param name="Filters">Queryex predicates, each its own leaf, conjoined; null or empty for none.</param>
    /// <param name="Arguments">The values of the clauses' parameters, as JSON scalars keyed by parameter name.</param>
    /// <param name="IncludeInactive">On an activatable tree: whether inactive rows are included.</param>
    /// <param name="IncludeRoots">With parent ids: whether the roots come too, in the same row set and the same statement.</param>
    /// <param name="OrderBy">The Queryex ordering; null for the stack's default ordering, else <c>Id</c>.</param>
    /// <param name="NamedFilters">The stack's named filters to apply, by name, conjoined; an unknown name is a query-invalid error.</param>
    public sealed record ParentIdsRequest(
        IReadOnlyList<long>? ParentIds,
        string? Select,
        IReadOnlyList<string>? Filters,
        IReadOnlyDictionary<string, JsonElement>? Arguments,
        bool IncludeInactive = false,
        bool IncludeRoots = false,
        string? OrderBy = null,
        IReadOnlyList<string>? NamedFilters = null);
}
