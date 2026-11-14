// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Entities;

namespace Tellma.Core.Abstractions.Crud
{
    /// <summary>
    ///     The read-only query surface over one child collection of a stack, at any depth, with a
    ///     securable of its own whose grants compose with the owner's. The registry derives one per
    ///     child collection of a stack that serves queries, unless the child type opts out.
    /// </summary>
    /// <remarks>
    ///     A grandchild's owner is the child surface above it. The surface inherits the root stack's
    ///     limits and agent exposure. It serves <c>query</c>, and, when <see cref="Exportable" />, the
    ///     display-shape export under the same <c>Read</c>; never an import.
    /// </remarks>
    /// <param name="Resource">The child's entity name (<c>acme.SalesInvoiceLine</c>), its securable resource.</param>
    /// <param name="ResourceSegment">The kebab-case plural of the child's table (<c>sales-invoice-lines</c>), or the segment the child's API annotation names.</param>
    /// <param name="EntityType">The child entity's type.</param>
    /// <param name="OwnerResource">The owner's entity name (<c>acme.SalesInvoice</c>).</param>
    /// <param name="OwnerNavigation">The child's navigation to its owner row (<c>Invoice</c>).</param>
    /// <param name="Collection">The owner's collection property (<c>Lines</c>).</param>
    /// <param name="SearchableProperties">The searchable string properties of the child and how each is matched.</param>
    /// <param name="DefaultSelect">The projection a caller that names none receives: the id, the parent key, the natural keys, the name group and the code.</param>
    /// <param name="RelatedSelect">The projection visible through navigations.</param>
    /// <param name="DefaultOrderBy">The ordering when the request names none: <c>Id desc</c>.</param>
    /// <param name="Exportable">Whether the root stack serves export, so this surface serves the display-shape export too.</param>
    /// <param name="Limits">The root stack's ceilings.</param>
    /// <param name="Mcp">The root stack's agent exposure.</param>
    /// <param name="Actions">The surface's two export actions when <see cref="Exportable" />, else empty.</param>
    public sealed record ChildQueryDescriptor(
        string Resource,
        string ResourceSegment,
        Type EntityType,
        string OwnerResource,
        string OwnerNavigation,
        string Collection,
        IReadOnlyList<(string Property, SearchKind Kind)> SearchableProperties,
        string DefaultSelect,
        IReadOnlySet<string> RelatedSelect,
        string DefaultOrderBy,
        bool Exportable,
        StackLimits Limits,
        McpExposure Mcp,
        IReadOnlyList<ApiActionDescriptor> Actions);
}
