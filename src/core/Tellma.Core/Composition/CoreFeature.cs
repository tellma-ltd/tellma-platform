// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Composition;

namespace Tellma.Core.Composition
{
    /// <summary>
    ///     The platform's own feature, named <c>core</c>. The composition root adds it first and
    ///     unconditionally, so every other feature is realised after it without declaring it.
    /// </summary>
    /// <remarks>
    ///     It carries the users, roles, permissions, settings and cache content of the platform as
    ///     those areas land; each adds its items to <see cref="Contribute" />. A distribution never
    ///     adds it: <c>AddFeature&lt;CoreFeature&gt;()</c> is accepted as the idempotent no-op it is.
    /// </remarks>
    public sealed class CoreFeature : ITellmaFeature
    {
        /// <summary>The feature's name.</summary>
        public const string FeatureName = "core";

        /// <inheritdoc />
        public string Name => FeatureName;

        /// <inheritdoc />
        public void Declare(FeatureDeclaration declaration)
        {
            // Core requires nothing and binds its options as platform options, not through here.
            ArgumentNullException.ThrowIfNull(declaration);
        }

        /// <inheritdoc />
        public void Contribute(FeatureContribution contribution)
        {
            // Empty until the platform's own entities, services and model configurations land.
            ArgumentNullException.ThrowIfNull(contribution);
        }
    }
}
