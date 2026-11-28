// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text.Json.Serialization;

namespace Tellma.Core.Abstractions.Client
{
    /// <summary>
    ///     The full metadata of one entity, which the Entities chunk of the client metadata carries: its
    ///     summary, then its key, owner, members, actions and query facts.
    /// </summary>
    /// <param name="Name">The entity's name, such as <c>gl.Center</c>.</param>
    /// <param name="Segment">The entity's API resource segment; null for a read-only root.</param>
    /// <param name="LabelKey">The resource key of the entity's singular label.</param>
    /// <param name="PluralLabelKey">The resource key of the entity's plural label.</param>
    /// <param name="NewLabelKey">The resource key of the entity's create label.</param>
    /// <param name="LabelFallback">The humanized English a client shows when no resource declares <paramref name="LabelKey" />; null when one does.</param>
    /// <param name="Operations">The operations the entity's API offers.</param>
    /// <param name="Capabilities">The capabilities the entity's stack declares.</param>
    /// <param name="DisplayName">How a row's display name is built.</param>
    /// <param name="Kind">Whether the entity is a stack, a child of a stack's aggregate, or a read-only root.</param>
    /// <param name="Key">The key property and its type.</param>
    /// <param name="Owner">A child entity's owner, and the navigation and collection that link them; null, and omitted, for any other entity.</param>
    /// <param name="Properties">The entity's scalar properties, in the order of its metadata.</param>
    /// <param name="Navigations">The entity's navigations, in the order of the query schema.</param>
    /// <param name="Children">The entity's child collections, in the order of its metadata.</param>
    /// <param name="Actions">The entity's actions, in the order of its descriptors.</param>
    /// <param name="RelatedSelect">The paths a related row's projection selects.</param>
    /// <param name="DefaultOrderBy">The order a query takes when it names none.</param>
    /// <param name="Searchable">The properties search text matches.</param>
    /// <param name="PrefixSearchable">The properties search text matches from their start, which an index can seek; every other searchable property matches it anywhere.</param>
    /// <param name="NaturalKeys">The properties that identify a row to people, in order.</param>
    /// <param name="Extras">The names of the extras a details read may ask for.</param>
    /// <param name="Limits">The entity's query ceilings.</param>
    /// <remarks>Serialized, the summary's members come first, then these, each part in declaration order.</remarks>
    public sealed record ClientEntityMetadata(
        string Name,
        string? Segment,
        string LabelKey,
        string PluralLabelKey,
        string NewLabelKey,
        string? LabelFallback,
        IReadOnlyList<ClientEntityOperation> Operations,
        IReadOnlyList<string> Capabilities,
        ClientDisplayNameRule DisplayName,
        ClientEntityKind Kind,
        [property: JsonPropertyOrder(ClientEntityMetadata.DetailsOrder)] ClientEntityKey Key,
        [property: JsonPropertyOrder(ClientEntityMetadata.DetailsOrder)] ClientEntityOwner? Owner,
        [property: JsonPropertyOrder(ClientEntityMetadata.DetailsOrder)] IReadOnlyList<ClientPropertyMetadata> Properties,
        [property: JsonPropertyOrder(ClientEntityMetadata.DetailsOrder)] IReadOnlyList<ClientNavigationMetadata> Navigations,
        [property: JsonPropertyOrder(ClientEntityMetadata.DetailsOrder)] IReadOnlyList<ClientChildMetadata> Children,
        [property: JsonPropertyOrder(ClientEntityMetadata.DetailsOrder)] IReadOnlyList<ClientActionMetadata> Actions,
        [property: JsonPropertyOrder(ClientEntityMetadata.DetailsOrder)] IReadOnlyList<string> RelatedSelect,
        [property: JsonPropertyOrder(ClientEntityMetadata.DetailsOrder)] string DefaultOrderBy,
        [property: JsonPropertyOrder(ClientEntityMetadata.DetailsOrder)] IReadOnlyList<string> Searchable,
        [property: JsonPropertyOrder(ClientEntityMetadata.DetailsOrder)] IReadOnlyList<string> PrefixSearchable,
        [property: JsonPropertyOrder(ClientEntityMetadata.DetailsOrder)] IReadOnlyList<string> NaturalKeys,
        [property: JsonPropertyOrder(ClientEntityMetadata.DetailsOrder)] IReadOnlyList<string> Extras,
        [property: JsonPropertyOrder(ClientEntityMetadata.DetailsOrder)] ClientEntityLimits Limits)
        : ClientEntitySummary(Name, Segment, LabelKey, PluralLabelKey, NewLabelKey, LabelFallback, Operations, Capabilities, DisplayName, Kind)
    {
        /// <summary>The serialization order of the members beyond the summary's, which so follow the summary's members on the wire.</summary>
        internal const int DetailsOrder = 1;
    }
}
