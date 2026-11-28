// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Composition
{
    /// <summary>
    ///     A service registration a feature contributes, by implementation type or by factory.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Exactly one of <see cref="ImplementationType" /> and <see cref="Factory" /> is set; the
    ///         platform reports any other combination, and an implementation type that is not a
    ///         concrete class assignable to <see cref="ServiceType" />, as a composition problem.
    ///     </para>
    ///     <para>
    ///         When two features register the same service type, the registration of the feature
    ///         realised later wins and replaces the earlier one. Features are realised after every
    ///         feature they require, so a distribution's feature replaces a pack's service by
    ///         requiring the pack and registering the same service type. A service type marked
    ///         <see cref="CollectionServiceAttribute" /> is never replaced: each registration adds one
    ///         implementation.
    ///     </para>
    /// </remarks>
    /// <param name="ServiceType">The service type resolved from the container.</param>
    /// <param name="ImplementationType">The concrete type constructed for it, or null when <paramref name="Factory" /> is set.</param>
    /// <param name="Factory">The factory that creates it, or null when <paramref name="ImplementationType" /> is set.</param>
    /// <param name="Lifetime">The registration's lifetime.</param>
    public sealed record ServiceContributionItem(
        Type ServiceType,
        Type? ImplementationType,
        Func<IServiceProvider, object>? Factory,
        ServiceLifetimeKind Lifetime) : FeatureContributionItem;
}
