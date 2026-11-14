// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Entities;
using Tellma.Core.Queryex;

namespace Tellma.Core.Abstractions.Crud
{
    /// <summary>
    ///     Everything one stack declares, derived once at startup from the entity's shape, its
    ///     annotations and its service's actions. The web and MCP projections, the client metadata,
    ///     securable registration, the Excel codec and the conformance tests all read this record, so
    ///     none of them derives the stack a second time.
    /// </summary>
    /// <remarks>
    ///     The registry builds one descriptor per stack and serves the same instance for the life of
    ///     the host, so two descriptors are compared by identity in practice: the record's synthesised
    ///     equality compares the collection members by reference.
    /// </remarks>
    /// <param name="Resource">
    ///     The entity name (<c>gl.Center</c>): the query root, the securable resource and the filter
    ///     root of the stack's securables. A distribution's leaf keeps the name of the pack entity it
    ///     derives from.
    /// </param>
    /// <param name="ResourceSegment">The kebab-case plural of the table (<c>centers</c>), or the segment the entity's API annotation names; routing only, never a securable.</param>
    /// <param name="EntityType">The entity's leaf type.</param>
    /// <param name="ServiceType">The stack's service type, closed over the leaf.</param>
    /// <param name="KeyType">The key type: <see cref="int" /> or <see cref="long" />.</param>
    /// <param name="Description">The stack's description for tool text; null when it has none.</param>
    /// <param name="Operations">The operations the stack serves.</param>
    /// <param name="Capabilities">
    ///     The capabilities the entity declares, from a closed vocabulary: <c>activatable</c>,
    ///     <c>tree</c>, <c>temporal</c>, <c>multilingual</c>, <c>cacheable</c>, <c>blobs</c>,
    ///     <c>job-entity</c>, <c>searchable</c>, <c>excel</c>, <c>assignable</c>.
    /// </param>
    /// <param name="Actions">
    ///     Every action of the stack, each as its kind: the built-in actions, then the service's
    ///     entity and API actions in declaration order, then the API actions of each stack companion
    ///     in registration order.
    /// </param>
    /// <param name="Properties">Every mapped property of the leaf, with its ownership.</param>
    /// <param name="Children">Every child collection of the leaf, with its row cap.</param>
    /// <param name="SearchableProperties">The searchable string properties and how each is matched, in declaration order.</param>
    /// <param name="NaturalKeys">The natural-key properties, in order.</param>
    /// <param name="DefaultSelect">The projection a caller that names none receives.</param>
    /// <param name="RelatedSelect">The projection visible through navigations; always holds <c>Id</c>, the display-name columns and <c>Code</c>.</param>
    /// <param name="DetailsExpand">The navigations the details projection follows.</param>
    /// <param name="DefaultOrderBy">The ordering an operation uses when the request names none; null leaves each operation's own default.</param>
    /// <param name="DisplayName">How a row of the leaf is named.</param>
    /// <param name="Extras">Every details extra the stack serves, by name.</param>
    /// <param name="NamedFilters">The stack's named filters, each a constant tree built once at startup, keyed by a kebab-case name.</param>
    /// <param name="Limits">The stack's ceilings.</param>
    /// <param name="Mcp">How the stack's operations are exposed as agent tools.</param>
    /// <param name="ContractRevision">Bumped by hand when the stack's wire meaning changes with no change of shape; enters the contract fingerprint of every endpoint of the stack.</param>
    /// <param name="Feature">The name of the feature that registered the stack; every securable registered for it carries it.</param>
    public sealed record StackDescriptor(
        string Resource,
        string ResourceSegment,
        Type EntityType,
        Type ServiceType,
        Type KeyType,
        string? Description,
        StackOperations Operations,
        IReadOnlySet<string> Capabilities,
        IReadOnlyList<ActionDescriptor> Actions,
        IReadOnlyList<PropertyMetadata> Properties,
        IReadOnlyList<ChildCollectionDescriptor> Children,
        IReadOnlyList<(string Property, SearchKind Kind)> SearchableProperties,
        IReadOnlyList<string> NaturalKeys,
        string DefaultSelect,
        IReadOnlySet<string> RelatedSelect,
        IReadOnlyList<string> DetailsExpand,
        string? DefaultOrderBy,
        DisplayNameMetadata DisplayName,
        IReadOnlyList<string> Extras,
        IReadOnlyDictionary<string, FilterTree> NamedFilters,
        StackLimits Limits,
        McpExposure Mcp,
        int ContractRevision,
        string Feature);
}
