// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Caching;
using Tellma.Core.IntegrationTests.Data;
using Tellma.Core.Testing.Crud;
using Tellma.Testing.Entities;
using static System.FormattableString;

namespace Tellma.Core.IntegrationTests.Crud.Conformance
{
    // The gadget of spec 0015 §18.4 is the fixture's lookup, registered read-only: cacheable,
    // activatable and searchable reference data whose rows no save writes.
    /// <summary>
    ///     The stack conformance suite closed over a read-only stack: the fixture's lookups,
    ///     registered to serve their reads alone, whose rows the suite stores by SQL.
    /// </summary>
    /// <param name="fixture">The tenant databases.</param>
    [Trait("Category", "Integration")]
    public sealed class GadgetConformanceTests(GadgetDatabase fixture) : StackConformanceTests<Lookup, int>, IClassFixture<GadgetDatabase>
    {
        /// <summary>The first id the suite stores its rows under; the stack allocates none, as it saves none.</summary>
        private const int FirstId = 100;

        /// <summary>The lookups' version tag, which a stored row moves so the cached list refills.</summary>
        private static readonly string LookupTag = VersionTagNames.ForEntity("fixture.Lookup");

        /// <inheritdoc />
        protected override IStackConformanceHarness Harness => fixture;

        /// <inheritdoc />
        protected override Lookup NewValidEntity(int ordinal)
        {
            return new Lookup { Code = Invariant($"GC-{ordinal}"), Name = Invariant($"Gadget {ordinal}") };
        }

        /// <inheritdoc />
        protected override async Task<Lookup> CreateAsync(Lookup entity)
        {
            ArgumentNullException.ThrowIfNull(entity);

            // --- The row, under an id of its own and with the flag the entity holds, and the lookups'
            // tag moved as a save would move it ---
            entity.Id = FirstId + NextOrdinal();
            int active = entity.IsActive ? 1 : 0;
            await fixture.ExecuteAsync(
                DataQueryDatabase.Stamped("[fixture].[Lookups]", "[Id], [Code], [Name], [IsActive]", Invariant($"({entity.Id}, '{entity.Code}', N'{entity.Name}', {active})")),
                Invariant($"UPDATE [core].[VersionTags] SET [Tag] = NEWID() WHERE [Name] = N'{LookupTag}';"));
            return entity;
        }
    }
}
