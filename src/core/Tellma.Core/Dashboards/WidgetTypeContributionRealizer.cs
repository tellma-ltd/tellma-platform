// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Composition;

namespace Tellma.Core.Dashboards
{
    /// <summary>
    ///     Records <see cref="WidgetTypeContributionItem" />s with their feature, in realisation order,
    ///     which is the order the widget type registry lists them in, so the startup gate can say who
    ///     contributed a key it refuses.
    /// </summary>
    internal sealed class WidgetTypeContributionRealizer : IContributionRealizer<WidgetTypeContributionItem>
    {
        /// <inheritdoc />
        public void Realize(WidgetTypeContributionItem item, ITellmaFeature owner, RealizationContext context)
        {
            // The sugar refuses a null key; an item built by hand can still carry one.
            if (item.Key is null)
            {
                context.Report(new CompositionProblem(
                    CompositionNames.Source,
                    $"Feature '{owner.Name}' contributes a widget type without a key.",
                    $"Contribute a widget type with its key: contribution.WidgetType(\"{owner.Name}.<name>\")."));
                return;
            }

            // The key's grammar, its owner and its uniqueness, and the settings class, are the startup gate's to judge.
            context.Record.WidgetTypes.Add(new ContributedWidgetType(item.Key, item.SettingsType, owner));
        }
    }
}
