// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text.Json.Serialization;

namespace Tellma.Core.Abstractions.Client
{
    /// <summary>
    ///     The eager facts about one entity, which the Core chunk of the client metadata carries: enough
    ///     for route matching, the launcher and display names before the full metadata loads.
    /// </summary>
    /// <remarks>
    ///     Serialized as this type, a <see cref="ClientEntityMetadata" /> writes these members alone.
    /// </remarks>
    /// <param name="Name">The entity's name, such as <c>gl.Center</c>.</param>
    /// <param name="Segment">The entity's API resource segment, such as <c>centers</c>, a child's query segment; null, written as such, for a read-only root.</param>
    /// <param name="LabelKey">The resource key of the entity's singular label.</param>
    /// <param name="PluralLabelKey">The resource key of the entity's plural label.</param>
    /// <param name="NewLabelKey">The resource key of the entity's create label, such as "New customer".</param>
    /// <param name="LabelFallback">The humanized English a client shows when no resource declares <paramref name="LabelKey" />; null, and omitted, when one does.</param>
    /// <param name="Operations">The operations the entity's API offers, in the vocabulary's order.</param>
    /// <param name="Capabilities">The capabilities the entity's stack declares, in ordinal order.</param>
    /// <param name="DisplayName">How a row's display name is built.</param>
    /// <param name="Kind">Whether the entity is a stack, a child of a stack's aggregate, or a read-only root.</param>
    public record ClientEntitySummary(
        string Name,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? Segment,
        string LabelKey,
        string PluralLabelKey,
        string NewLabelKey,
        string? LabelFallback,
        IReadOnlyList<ClientEntityOperation> Operations,
        IReadOnlyList<string> Capabilities,
        ClientDisplayNameRule DisplayName,
        ClientEntityKind Kind);
}
