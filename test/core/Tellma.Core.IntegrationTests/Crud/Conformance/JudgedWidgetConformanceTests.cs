// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Testing.Crud;
using Tellma.Testing.Entities;
using static System.FormattableString;

namespace Tellma.Core.IntegrationTests.Crud.Conformance
{
    /// <summary>
    ///     The stack conformance suite closed over the widget stack as a distribution's own service
    ///     shapes it: a custom entity action and an API action, judged deletes and actions, and a
    ///     validator that loads across three dependent rounds on every save.
    /// </summary>
    /// <param name="fixture">The tenant databases.</param>
    [Trait("Category", "Integration")]
    public sealed class JudgedWidgetConformanceTests(JudgedWidgetDatabase fixture) : StackConformanceTests<Widget, int>, IClassFixture<JudgedWidgetDatabase>
    {
        /// <inheritdoc />
        protected override IStackConformanceHarness Harness => fixture;

        /// <inheritdoc />
        protected override bool ValidatorsLoad => true;

        /// <inheritdoc />
        protected override int DependentValidationRounds => DependentLoadsValidator.Rounds - 1;

        /// <inheritdoc />
        protected override Widget NewValidEntity(int ordinal)
        {
            return new Widget
            {
                Code = Invariant($"WJ-{ordinal}"),
                Kind = WidgetKind.Plain,
                Name = Invariant($"Judged {ordinal}"),
                Subject = Invariant($"wj-{ordinal}"),
                Price = ordinal,
                Parts = [Part(ordinal, 1), Part(ordinal, 2)],
            };
        }

        /// <summary>A part of a new widget, holding two notes.</summary>
        /// <param name="ordinal">The widget's ordinal.</param>
        /// <param name="number">The part's ordinal within the widget.</param>
        /// <returns>The part.</returns>
        private static WidgetPart Part(int ordinal, int number)
        {
            return new WidgetPart
            {
                Ordinal = number,
                Notes = Invariant($"Part {number} of {ordinal}"),
                PartNotes = [new WidgetPartNote { Text = Invariant($"Note 1 of part {number} of {ordinal}") }, new WidgetPartNote { Text = Invariant($"Note 2 of part {number} of {ordinal}") }],
            };
        }

        /// <inheritdoc />
        protected override object? ActionArguments(EntityActionDescriptor action)
        {
            ArgumentNullException.ThrowIfNull(action);
            return action.Name == JudgedWidgetService.Rename ? new WidgetRename { Name = "Renamed" } : null;
        }
    }
}
