// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Data;
using Tellma.Core.Testing.Crud;
using Tellma.Testing.Entities;
using static System.FormattableString;

namespace Tellma.Core.IntegrationTests.Crud.Conformance
{
    /// <summary>
    ///     The stack conformance suite closed over the fixture's node stack: an activatable tree with
    ///     a unique code, whose links reference other nodes.
    /// </summary>
    /// <param name="fixture">The tenant databases.</param>
    [Trait("Category", "Integration")]
    public sealed class NodeConformanceTests(ConformanceDatabase fixture) : StackConformanceTests<Node, int>, IClassFixture<ConformanceDatabase>
    {
        /// <inheritdoc />
        protected override IStackConformanceHarness Harness => fixture;

        /// <inheritdoc />
        protected override Node NewValidEntity(int ordinal)
        {
            return new Node
            {
                Code = Invariant($"NC-{ordinal}"),
                Name = Invariant($"Node {ordinal}"),
                Links = [new Link { OtherNodeId = ConformanceDatabase.AnchorNode }, new Link { OtherNodeId = ConformanceDatabase.AnchorNode }],
            };
        }

        /// <inheritdoc />
        protected override async Task ReferToAsync(ReferenceMetadata reference, EntityMetadata referencing, Node target)
        {
            ArgumentNullException.ThrowIfNull(reference);
            ArgumentNullException.ThrowIfNull(target);

            // --- A link of another node names the target ---
            if (referencing.ClrType == typeof(Link) && reference.ForeignKey.Name == nameof(Link.OtherNodeId))
            {
                Node referrer = NewValidEntity(NextOrdinal());
                referrer.Links = [new Link { OtherNodeId = target.Id }];
                _ = await CreateAsync(referrer);
                return;
            }

            await base.ReferToAsync(reference, referencing, target);
        }
    }
}
