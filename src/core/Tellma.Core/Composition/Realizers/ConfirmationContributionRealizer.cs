// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Composition;

namespace Tellma.Core.Composition.Realizers
{
    /// <summary>
    ///     Records <see cref="ConfirmationContributionItem" />s, in realisation order, each with its
    ///     feature, so the startup gate can name who declared a key it refuses.
    /// </summary>
    /// <remarks>
    ///     A key declared twice is recorded twice: the grammar, the duplicate and the string resource
    ///     are the startup gate's to report.
    /// </remarks>
    internal sealed class ConfirmationContributionRealizer : IContributionRealizer<ConfirmationContributionItem>
    {
        /// <inheritdoc />
        public void Realize(ConfirmationContributionItem item, ITellmaFeature owner, RealizationContext context)
        {
            // The sugar refuses a null key; an item built by hand can still carry one.
            if (item.Key is null)
            {
                context.Report(new CompositionProblem(
                    CompositionNames.Source,
                    $"Feature '{owner.Name}' declares a confirmation without a key.",
                    "Declare a confirmation with its key: contribution.Confirmation(\"Inventory.BackdatedRecost\")."));
                return;
            }

            context.Record.Confirmations.Add(new ContributedConfirmation(item.Key, owner));
        }
    }
}
