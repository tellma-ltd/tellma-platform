// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Entities;
using Tellma.Core.Abstractions.Validation;

namespace Tellma.Core.Abstractions.Sharing
{
    /// <summary>
    ///     The sharing actions of a shareable entity, which its service's <c>share</c>, <c>publish</c>
    ///     and <c>transfer</c> actions delegate to. Registered for the entity by
    ///     <see cref="ShareableContributionExtensions.Shareable{TEntity, TShare}" />; scoped.
    /// </summary>
    /// <remarks>
    ///     Each command validates its arguments against each target's current grants and composes its
    ///     writes onto the action's persist batch, so it runs inside the action's validation rounds:
    ///     call it from the action's method with the context the pipeline passed. <c>share</c> and
    ///     <c>publish</c> merge their grants and never stamp the row; <c>transfer</c> writes the owner
    ///     and stamps it.
    /// </remarks>
    /// <typeparam name="TEntity">The shareable entity.</typeparam>
    public interface IShareCommands<TEntity>
        where TEntity : TopLevelEntity, IShareable
    {
        /// <summary>Merges user grants into each target's grants, and notifies each newly granted user.</summary>
        /// <param name="context">The action's context.</param>
        /// <param name="arguments">The grants to set and the users whose grants to remove.</param>
        /// <returns>A task that completes when the writes are composed.</returns>
        Task ShareAsync(ActionContext<TEntity, int> context, ShareArguments arguments);

        /// <summary>Merges role grants and the Everyone grant into each target's grants.</summary>
        /// <param name="context">The action's context.</param>
        /// <param name="arguments">The grants to set, the roles whose grants to remove, and the Everyone grant's fate.</param>
        /// <returns>A task that completes when the writes are composed.</returns>
        Task PublishAsync(ActionContext<TEntity, int> context, PublishArguments arguments);

        /// <summary>Makes another user the owner of each target, removing that user's own grant.</summary>
        /// <param name="context">The action's context.</param>
        /// <param name="arguments">The new owner.</param>
        /// <returns>A task that completes when the writes are composed.</returns>
        Task TransferAsync(ActionContext<TEntity, int> context, TransferArguments arguments);

        /// <summary>
        ///     Withdraws every recipient's share notice of each deleted row, in the delete's own batch,
        ///     since the delete removes every grant the notices announced.
        /// </summary>
        /// <param name="context">The delete's persist context, whose affected ids are the deleted rows.</param>
        void WithdrawAll(DeletePersistContext<TEntity> context);
    }
}
