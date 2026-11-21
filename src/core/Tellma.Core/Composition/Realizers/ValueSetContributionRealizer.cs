// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Tellma.Core.Abstractions.Composition;

namespace Tellma.Core.Composition.Realizers
{
    /// <summary>
    ///     Registers <see cref="ValueSetContributionItem" />s: each descriptor as a singleton, in
    ///     realisation order, which is the order the value-set registry lists the sets in after the
    ///     platform's own. Each is recorded with its feature, so the startup gate can say who
    ///     contributed a set it refuses.
    /// </summary>
    internal sealed class ValueSetContributionRealizer : IContributionRealizer<ValueSetContributionItem>
    {
        /// <inheritdoc />
        public void Realize(ValueSetContributionItem item, ITellmaFeature owner, RealizationContext context)
        {
            // The sugar refuses a null descriptor; an item built by hand can still carry one.
            if (item.Descriptor is null)
            {
                context.Report(new CompositionProblem(
                    CompositionNames.Source,
                    $"Feature '{owner.Name}' contributes a value set without a descriptor.",
                    "Contribute a value set with its descriptor: contribution.ValueSet(new ValueSetDescriptor(name, values))."));
                return;
            }

            // Everything else about a set (its name, its values, its aliases) is the startup gate's
            // to judge, against every other set and the model's enums.
            context.Record.ValueSets.Add(new ContributedValueSet(item.Descriptor, owner));
            _ = context.Services.AddSingleton(item.Descriptor);
        }
    }
}
