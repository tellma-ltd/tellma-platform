// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Composition;

namespace Tellma.Testing.Entities
{
    /// <summary>
    ///     The feature that contributes the fixture schema and the stand-in core tables to a
    ///     composition, so a test host's tenant model is the fixture database's.
    /// </summary>
    public sealed class FixtureFeature : ITellmaFeature
    {
        /// <summary>The feature's name.</summary>
        public const string FeatureName = "fixture";

        /// <inheritdoc />
        public string Name => FeatureName;

        /// <inheritdoc />
        public void Declare(FeatureDeclaration declaration)
        {
        }

        /// <inheritdoc />
        public void Contribute(FeatureContribution contribution)
        {
            ArgumentNullException.ThrowIfNull(contribution);

            // --- The stand-in core tables the fixture's foreign keys and the platform's statements name ---
            _ = contribution
                .Entity<FixtureUser>()
                .Entity<FixtureJob>()
                .Entity<FixtureBlob>()
                .Model<FixtureVersionTagConfiguration>()
                .Model<FixtureUserStampConfiguration>();

            // --- The fixture schema; children come with their parents ---
            _ = contribution
                .Entity<Widget>()
                .Entity<WidgetExtra>()
                .Entity<Node>()
                .Entity<Shipment>();
        }
    }
}
