// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Testing.Crud;
using Tellma.Testing.Entities;
using static System.FormattableString;

namespace Tellma.Core.IntegrationTests.Crud.Conformance
{
    /// <summary>
    ///     The stack conformance suite closed over the fixture's lookup stack: cacheable, activatable
    ///     and searchable, with a unique code.
    /// </summary>
    /// <param name="fixture">The tenant databases.</param>
    [Trait("Category", "Integration")]
    public sealed class LookupConformanceTests(ConformanceDatabase fixture) : StackConformanceTests<Lookup, int>, IClassFixture<ConformanceDatabase>
    {
        /// <inheritdoc />
        protected override IStackConformanceHarness Harness => fixture;

        /// <inheritdoc />
        protected override Lookup NewValidEntity(int ordinal)
        {
            return new Lookup { Code = Invariant($"LC-{ordinal}"), Name = Invariant($"Lookup {ordinal}") };
        }
    }
}
