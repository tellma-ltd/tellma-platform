// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Composition
{
    /// <summary>
    ///     A component a feature attaches to the stacks of an entity: a validator, a persist effect, a
    ///     details contributor or a named filter contributor. The platform closes it over each stack's
    ///     leaf, so a component written against a pack's entity follows a distribution's substitution.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The component applies to the stack whose leaf is <paramref name="EntityType" /> or derives
    ///         from it. A component generic over the entity is registered closed over
    ///         <paramref name="EntityType" /> (<c>CenterRules&lt;Center&gt;</c>), or by its open definition
    ///         of one type parameter (<c>typeof(EInvoiceRules&lt;&gt;)</c>) when only the leaf meets its
    ///         constraints; either way it runs closed over the leaf.
    ///     </para>
    ///     <para>
    ///         A component that is not generic runs only on the stack whose leaf is exactly
    ///         <paramref name="EntityType" />; registered for an entity a distribution substitutes, it fails
    ///         startup.
    ///     </para>
    /// </remarks>
    /// <param name="EntityType">The entity the component is written against.</param>
    /// <param name="ComponentType">The component class: closed over <paramref name="EntityType" />, or an open generic definition of one type parameter.</param>
    /// <param name="Kind">The contract the component implements.</param>
    public sealed record ValidatorContributionItem(Type EntityType, Type ComponentType, ComponentKind Kind) : FeatureContributionItem;
}
