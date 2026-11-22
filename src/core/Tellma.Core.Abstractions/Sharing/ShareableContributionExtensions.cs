// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Entities;

namespace Tellma.Core.Abstractions.Sharing
{
    /// <summary>The sugar that makes an entity shareable, for any feature's contribution.</summary>
    public static class ShareableContributionExtensions
    {
        /// <summary>
        ///     Makes <typeparamref name="TEntity" /> shareable through the grants of
        ///     <typeparamref name="TShare" />: registers its access criteria, its sharing commands
        ///     (<see cref="IShareCommands{TEntity}" />), the <c>shares</c> details extra, the
        ///     <c>shared-with-me</c> named filter and the notification type a new user grant raises.
        /// </summary>
        /// <remarks>
        ///     The entity's own stack is contributed separately, with a service derived from
        ///     <see cref="ShareableService{TEntity, TShare}" /> that declares the three sharing actions.
        ///     The share entity is a mapped table beside it, never a stack, with one foreign key to the
        ///     entity, which deletes a row's grants with the row.
        /// </remarks>
        /// <typeparam name="TEntity">The shareable entity.</typeparam>
        /// <typeparam name="TShare">The share entity.</typeparam>
        /// <param name="contribution">The contribution.</param>
        /// <param name="notificationType">The key of the notification type a new user grant raises, such as <c>core.report.shared</c>.</param>
        /// <returns>The contribution, for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="contribution" /> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="notificationType" /> is null, empty or white space.</exception>
        public static FeatureContribution Shareable<TEntity, TShare>(this FeatureContribution contribution, string notificationType)
            where TEntity : TopLevelEntity, IShareable
            where TShare : Entity<int>, IShareRow
        {
            ArgumentNullException.ThrowIfNull(contribution);
            ArgumentException.ThrowIfNullOrWhiteSpace(notificationType);
            return contribution.Add(new ShareableContributionItem(typeof(TEntity), typeof(TShare), notificationType));
        }
    }
}
