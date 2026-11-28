// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Composition;

namespace Tellma.Core.Composition.Realizers
{
    /// <summary>
    ///     Records <see cref="ModelContributionItem" />s, in feature order, for the platform's data
    ///     context to apply. Whether each implements an entity type configuration is the
    ///     <c>core.model-contributors</c> startup check's to decide, beside the model it configures.
    /// </summary>
    internal sealed class ModelContributionRealizer : IContributionRealizer<ModelContributionItem>
    {
        /// <inheritdoc />
        public void Realize(ModelContributionItem item, ITellmaFeature owner, RealizationContext context)
        {
            if (item.ConfigurationType is null)
            {
                context.Report(new CompositionProblem(
                    CompositionNames.Source,
                    $"Feature '{owner.Name}' contributes a model configuration with no configuration type.",
                    "Contribute a model configuration with contribution.Model<TConfiguration>()."));
                return;
            }

            if (!context.Record.ModelConfigurations.Exists(existing => existing.Type == item.ConfigurationType))
            {
                context.Record.ModelConfigurations.Add(new ContributedType(item.ConfigurationType, owner));
            }
        }
    }
}
