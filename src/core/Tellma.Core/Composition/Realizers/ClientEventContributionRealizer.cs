// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Tellma.Core.Abstractions.Composition;

namespace Tellma.Core.Composition.Realizers
{
    /// <summary>
    ///     Registers <see cref="ClientEventContributionItem" />s: each item as a singleton, in
    ///     realisation order, which is the order the client event registry lists the names in. Each
    ///     is recorded with its feature, so the startup gate can say who contributed a name it
    ///     refuses.
    /// </summary>
    internal sealed class ClientEventContributionRealizer : IContributionRealizer<ClientEventContributionItem>
    {
        /// <inheritdoc />
        public void Realize(ClientEventContributionItem item, ITellmaFeature owner, RealizationContext context)
        {
            // The sugar refuses a null name; an item built by hand can still carry one.
            if (item.Name is null)
            {
                context.Report(new CompositionProblem(
                    CompositionNames.Source,
                    $"Feature '{owner.Name}' contributes a client event without a name.",
                    "Contribute a client event with its name: contribution.ClientEvent(\"<feature>.<event>\")."));
                return;
            }

            // The name's grammar and its uniqueness are the startup gate's to judge.
            context.Record.ClientEvents.Add(new ContributedClientEvent(item.Name, owner));
            _ = context.Services.AddSingleton(item);
        }
    }
}
