// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Data;
using Tellma.Core.Testing.Crud;
using Tellma.Module.Gl;
using static System.FormattableString;

namespace Tellma.Core.IntegrationTests.Crud.Conformance
{
    /// <summary>
    ///     The stack conformance suite closed over the GL module's centers: a module-owned activatable
    ///     tree with a unique code, whose service judges saves against the parent's type and judges
    ///     deletes, and which serves no delete by query.
    /// </summary>
    /// <param name="fixture">The tenant databases.</param>
    [Trait("Category", "Integration")]
    public sealed class CenterConformanceTests(ConformanceDatabase fixture) : StackConformanceTests<Center, int>, IClassFixture<ConformanceDatabase>
    {
        /// <inheritdoc />
        protected override IStackConformanceHarness Harness => fixture;

        /// <inheritdoc />
        protected override Center NewValidEntity(int ordinal)
        {
            // Every center the suite creates groups, so the suite's children are accepted under it;
            // two ordinals still differ in their type, so an update changes one.
            return new Center
            {
                Code = Invariant($"CC-{ordinal}"),
                Name = Invariant($"Center {ordinal}"),
                CenterType = ordinal % 2 == 0 ? CenterType.Abstract : CenterType.BusinessUnit,
            };
        }

        /// <inheritdoc />
        protected override bool ExercisesReference(EntityMetadata referencing, ReferenceMetadata reference)
        {
            ArgumentNullException.ThrowIfNull(referencing);
            ArgumentNullException.ThrowIfNull(reference);

            // A center's children refuse its delete under the service's own code before the
            // platform's in-use check runs (spec 0018 §5.4), which the centers' own tests hold.
            bool parentKey = referencing.ClrType == typeof(Center) && ReferenceEquals(referencing.Tree?.ParentKey, reference.ForeignKey);
            return !parentKey && base.ExercisesReference(referencing, reference);
        }
    }
}
