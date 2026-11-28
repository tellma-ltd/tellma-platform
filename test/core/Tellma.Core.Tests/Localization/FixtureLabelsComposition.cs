// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Tellma.Core.Tests.Infrastructure;
using Tellma.Testing.Entities;

namespace Tellma.Core.Tests.Localization
{
    /// <summary>
    ///     The fixture model composed once per test class with this suite's own feature, whose
    ///     assembly ships the labels of the fixture's entities, and three offered languages.
    /// </summary>
    public sealed class FixtureLabelsComposition : IDisposable
    {
        /// <summary>The offered languages, the tenant's content languages in the tests that need three.</summary>
        public static readonly string[] Languages = ["en", "ar", "am"];

        /// <summary>Composes the model.</summary>
        public FixtureLabelsComposition()
        {
            Services = FixtureComposition.Compose(
                new ServiceCollection(),
                compose: static tellma => tellma
                    .Languages(Languages)
                    .AddFeature(new TestFeature<Tag.A>("test.labels")))
                .BuildServiceProvider();
        }

        /// <summary>The realised composition's services.</summary>
        public ServiceProvider Services { get; }

        /// <inheritdoc />
        public void Dispose()
        {
            Services.Dispose();
        }
    }
}
