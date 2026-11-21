// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Composition;
using Tellma.Testing.Entities;

namespace Tellma.Core.IntegrationTests.Crud.Conformance
{
    /// <summary>
    ///     The databases of <see cref="ConformanceDatabase" />, served by an instance whose lookup
    ///     stack is registered again as a read-only gadget stack: cacheable reference data that
    ///     serves its reads alone, its rows written by the database's owner.
    /// </summary>
    /// <param name="sql">The SQL Server the databases are created on.</param>
    public sealed class GadgetDatabase(SqlServerFixture sql) : ConformanceDatabase(sql)
    {
        /// <inheritdoc />
        protected override void Compose(TellmaBuilder tellma)
        {
            base.Compose(tellma);
            _ = tellma.AddFeature(new GadgetFeature());
        }

        /// <summary>The feature that narrows the fixture's lookup stack to its reads.</summary>
        private sealed class GadgetFeature : ITellmaFeature
        {
            /// <inheritdoc />
            public string Name => "gadgets";

            /// <inheritdoc />
            public void Declare(FeatureDeclaration declaration)
            {
                ArgumentNullException.ThrowIfNull(declaration);

                // Depending on the fixture's feature lets this registration replace its operations.
                _ = declaration.Requires(FixtureFeature.FeatureName);
            }

            /// <inheritdoc />
            public void Contribute(FeatureContribution contribution)
            {
                ArgumentNullException.ThrowIfNull(contribution);
                _ = contribution.Add(new StackContributionItem(typeof(Lookup), null, StackOperations.Read));
            }
        }
    }
}
