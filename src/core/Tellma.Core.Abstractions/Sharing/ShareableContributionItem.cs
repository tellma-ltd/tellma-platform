// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Composition;

namespace Tellma.Core.Abstractions.Sharing
{
    /// <summary>
    ///     Makes an entity shareable: its rows have an owner and grants beside them in a share entity,
    ///     and the platform registers the access criteria, the sharing commands, the <c>shares</c>
    ///     details extra, the <c>shared-with-me</c> named filter and the notification type a new user
    ///     grant raises.
    /// </summary>
    /// <remarks>
    ///     Contributed through <see cref="ShareableContributionExtensions.Shareable{TEntity, TShare}" />.
    ///     The startup gate refuses a share entity that is a stack or a child collection, or that has
    ///     other than one foreign key to the entity, and an entity whose service does not derive from
    ///     <see cref="ShareableService{TEntity, TShare}" />.
    /// </remarks>
    /// <param name="EntityType">The shareable entity, a top-level entity implementing <see cref="IShareable" />.</param>
    /// <param name="ShareType">The share entity beside it, an <c>Entity&lt;int&gt;</c> implementing <see cref="IShareRow" />.</param>
    /// <param name="NotificationType">The key of the notification type a newly added user grant raises, such as <c>core.report.shared</c>.</param>
    public sealed record ShareableContributionItem(Type EntityType, Type ShareType, string NotificationType) : FeatureContributionItem;
}
