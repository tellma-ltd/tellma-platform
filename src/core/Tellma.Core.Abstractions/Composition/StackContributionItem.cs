// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Crud;

namespace Tellma.Core.Abstractions.Composition
{
    /// <summary>
    ///     An entity a feature contributes, with the service its stack runs through. The entity joins
    ///     the tenant model, and its table, its table type and every convention-derived constraint
    ///     follow from its class; a distribution's leaf replaces it when one is registered for it.
    /// </summary>
    /// <param name="EntityType">The entity: the pack's default, which a distribution's leaf may replace.</param>
    /// <param name="ServiceType">
    ///     The service class of the entity's stack, or <see langword="null" /> for the platform's default
    ///     service.
    /// </param>
    /// <param name="Operations">
    ///     The operations the stack serves, overriding the entity's own declaration; <see langword="null" />
    ///     to keep it.
    /// </param>
    public sealed record StackContributionItem(Type EntityType, Type? ServiceType, StackOperations? Operations) : FeatureContributionItem;
}
