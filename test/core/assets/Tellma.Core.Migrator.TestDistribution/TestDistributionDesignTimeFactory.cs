// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Composition;
using Tellma.Core.Data;

namespace Tellma.Core.Migrator.TestDistribution
{
    /// <summary>The test distribution's design-time factory: <c>dotnet ef</c> builds its tenant context from its composition.</summary>
    public sealed class TestDistributionDesignTimeFactory : TellmaDesignTimeDbContextFactory<TellmaDbContext>
    {
        /// <inheritdoc />
        protected override string Slug => TestDistribution.Slug;

        /// <inheritdoc />
        public override void Compose(TellmaBuilder builder)
        {
            TestDistribution.Compose(builder);
        }
    }
}
