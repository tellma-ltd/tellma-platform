// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Composition;

namespace Tellma.Core.Access
{
    /// <summary>
    ///     The contributor a feature's <see cref="SecurablesContributionItem" /> becomes: it runs the
    ///     feature's configuration on the registry's builder, on behalf of the feature.
    /// </summary>
    /// <param name="feature">The name of the feature that contributed the item.</param>
    /// <param name="configure">The item's configuration.</param>
    internal sealed class FeatureSecurableContributor(string feature, Action<SecurableRegistryBuilder> configure) : ISecurableContributor
    {
        /// <summary>The name of the feature that contributed the item, which owns what it registers.</summary>
        public string Feature { get; } = feature;

        /// <inheritdoc />
        public void Contribute(SecurableRegistryBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);
            configure(builder);
        }
    }
}
