// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Composition;

namespace Tellma.Distro.Acme
{
    /// <summary>
    ///     Acme's own feature, named <c>acme</c>: the feature its client app's declarations belong to,
    ///     so the client's <c>acmeFeature</c> names a feature the server composes. It declares and
    ///     contributes nothing yet; Acme's entities, services and settings join it.
    /// </summary>
    public sealed class AcmeFeature : ITellmaFeature
    {
        /// <summary>The feature's name, which the client's feature key repeats.</summary>
        public const string FeatureName = "acme";

        /// <inheritdoc />
        public string Name => FeatureName;

        /// <inheritdoc />
        public void Declare(FeatureDeclaration declaration)
        {
            ArgumentNullException.ThrowIfNull(declaration);
        }

        /// <inheritdoc />
        public void Contribute(FeatureContribution contribution)
        {
            ArgumentNullException.ThrowIfNull(contribution);
        }
    }
}
