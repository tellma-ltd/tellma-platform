// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Tellma.Core.Composition;
using Tellma.Core.Tests.Infrastructure;

namespace Tellma.Core.Tests.Composition
{
    /// <summary>The options seam the platform's own options bind through, beside the features' declared ones.</summary>
    public class OptionsRegistrarTests
    {
        private readonly RealizationContext _context = new(
            new ServiceCollection(),
            new TellmaCompositionRecord(TestComposition.Slug, TestComposition.Configuration(), new TestEnvironment()));

        [Fact]
        public void One_type_bound_again_from_the_same_path_binds_once()
        {
            OptionsRegistrar registrar = new(_context, TestComposition.Configuration());

            registrar.Bind<SampleOptions>("Tellma:Sample", "gl");
            registrar.Bind<SampleOptions>("tellma:sample", CoreFeature.FeatureName);

            Assert.Empty(_context.Problems);
            Assert.Single(_context.Record.BoundOptions);
        }

        [Fact]
        public void One_type_bound_from_a_second_path_is_reported_naming_both()
        {
            OptionsRegistrar registrar = new(_context, TestComposition.Configuration());

            registrar.Bind<SampleOptions>("Tellma:Data", "gl");
            registrar.Bind<SampleOptions>("Tellma:Sql", CoreFeature.FeatureName);

            Assert.Contains(
                "bound from 'Tellma:Data' (by 'gl') and 'Tellma:Sql' (by 'core')",
                Assert.Single(_context.Problems).Problem,
                StringComparison.Ordinal);
            Assert.Equal("Tellma:Data", Assert.Single(_context.Record.BoundOptions).ConfigurationPath);
        }
    }
}
