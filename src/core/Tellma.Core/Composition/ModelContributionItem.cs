// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Composition;

namespace Tellma.Core.Composition
{
    /// <summary>
    ///     An EF model configuration Core contributes to the platform's data context: a closed
    ///     <c>IEntityTypeConfiguration&lt;T&gt;</c>, or an open one over a replaceable default's leaf
    ///     that the context closes over the registered leaf.
    /// </summary>
    /// <remarks>
    ///     Internal, so no module or distribution can contribute model configuration: they declare
    ///     schema through entity annotations and conventions alone.
    /// </remarks>
    /// <param name="ConfigurationType">The configuration type, closed or an open generic definition of one type parameter.</param>
    internal sealed record ModelContributionItem(Type ConfigurationType) : FeatureContributionItem;

    /// <summary>The sugar for <see cref="ModelContributionItem" />, available to Core's own features only.</summary>
    internal static class ModelContributionExtensions
    {
        /// <summary>Contributes a closed model configuration.</summary>
        /// <typeparam name="TConfiguration">The configuration type.</typeparam>
        /// <param name="contribution">The contribution.</param>
        /// <returns>The contribution, for chaining.</returns>
        public static FeatureContribution Model<TConfiguration>(this FeatureContribution contribution)
            where TConfiguration : class
        {
            ArgumentNullException.ThrowIfNull(contribution);
            return contribution.Add(new ModelContributionItem(typeof(TConfiguration)));
        }

        /// <summary>Contributes an open model configuration, such as <c>typeof(UserConfiguration&lt;&gt;)</c>, closed over the registered leaf.</summary>
        /// <param name="contribution">The contribution.</param>
        /// <param name="openConfiguration">The open generic configuration definition.</param>
        /// <returns>The contribution, for chaining.</returns>
        public static FeatureContribution Model(this FeatureContribution contribution, Type openConfiguration)
        {
            ArgumentNullException.ThrowIfNull(contribution);
            ArgumentNullException.ThrowIfNull(openConfiguration);
            return contribution.Add(new ModelContributionItem(openConfiguration));
        }
    }
}
