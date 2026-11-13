// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Access;

namespace Tellma.Core.Composition.Realizers
{
    /// <summary>
    ///     Registers <see cref="SecurablesContributionItem" />s: each configuration as a singleton
    ///     <see cref="ISecurableContributor" /> on behalf of its feature, which the securable registry
    ///     runs, in realisation order, when it is composed.
    /// </summary>
    internal sealed class SecurablesContributionRealizer : IContributionRealizer<SecurablesContributionItem>
    {
        /// <inheritdoc />
        public void Realize(SecurablesContributionItem item, ITellmaFeature owner, RealizationContext context)
        {
            // The sugar refuses a null configuration; an item built by hand can still carry one.
            if (item.Configure is null)
            {
                context.Report(new CompositionProblem(
                    CompositionNames.Source,
                    $"Feature '{owner.Name}' contributes securables without a configuration.",
                    "Contribute securables with the method that registers them: contribution.Securables(builder => builder.Add(resource, action, filterRoot))."));
                return;
            }

            // What the configuration registers is the securables check's to judge, against every
            // other registration and the model.
            _ = context.Services.AddSingleton<ISecurableContributor>(new FeatureSecurableContributor(owner.Name, item.Configure));
        }
    }
}
