// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Composition;

namespace Tellma.Core.Abstractions.Access
{
    /// <summary>
    ///     A service that registers securables when the registry is composed. A feature registers one
    ///     as a singleton when its securables derive from other services; a fixed list is simpler to
    ///     contribute through <see cref="FeatureContribution.Securables" />.
    /// </summary>
    /// <remarks>
    ///     Every registered contributor runs once, in registration order, when the registry is first
    ///     resolved, which the startup gate does. A securable a contributor adds without naming a
    ///     feature belongs to the feature that registered the contributor's type; a contributor
    ///     registered outside every feature, or through a factory, names the feature of each securable
    ///     it adds, or they belong to <c>core</c>.
    /// </remarks>
    [CollectionService]
    public interface ISecurableContributor
    {
        /// <summary>Registers this contributor's securables, aliases and sensitivity marks.</summary>
        /// <param name="builder">The registry's builder, open only for the duration of the composition.</param>
        void Contribute(SecurableRegistryBuilder builder);
    }
}
