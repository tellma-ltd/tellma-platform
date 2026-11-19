// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Client
{
    /// <summary>One top-level property of an action's arguments, as the generated action dialog renders it.</summary>
    /// <param name="Name">The property's wire name, such as <c>creditLimit</c>.</param>
    /// <param name="LabelKey">The resource key of the field's label.</param>
    /// <param name="LabelFallback">The humanized English a client shows when no resource declares <paramref name="LabelKey" />; null, and omitted, when one does.</param>
    /// <param name="Required">Whether the argument must be given: its type is not nullable, or the property is marked required.</param>
    /// <param name="Value">The argument's type facts.</param>
    public sealed record ClientActionField(
        string Name,
        string LabelKey,
        string? LabelFallback,
        bool Required,
        ClientValueDescriptor Value);
}
