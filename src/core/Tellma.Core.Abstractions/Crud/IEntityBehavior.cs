// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.ComponentModel;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.Queryex;

namespace Tellma.Core.Abstractions.Crud
{
    /// <summary>
    ///     An entity service's hooks as the pipeline sees them. The service base implements it
    ///     explicitly over its protected hooks; not for distributions, which override the hooks.
    /// </summary>
    /// <typeparam name="TEntity">The stack's entity type.</typeparam>
    /// <typeparam name="TKey">The stack's key type.</typeparam>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public interface IEntityBehavior<TEntity, TKey>
    {
        /// <summary>Preprocesses a save's payload, once, before ids are assigned.</summary>
        /// <param name="context">The save's context.</param>
        /// <returns>A task that completes when the payload is preprocessed.</returns>
        Task PreprocessAsync(SaveContext<TEntity> context);

        /// <summary>Validates a save.</summary>
        /// <param name="context">The save's context.</param>
        /// <returns>A task that completes when the rules ran.</returns>
        Task ValidateAsync(SaveContext<TEntity> context);

        /// <summary>Validates a delete.</summary>
        /// <param name="context">The delete's context.</param>
        /// <returns>A task that completes when the rules ran.</returns>
        Task ValidateDeleteAsync(DeleteContext<TEntity, TKey> context);

        /// <summary>Validates an action.</summary>
        /// <param name="action">The action's name.</param>
        /// <param name="context">The action's context.</param>
        /// <returns>A task that completes when the rules ran.</returns>
        Task ValidateActionAsync(string action, ActionContext<TEntity, TKey> context);

        /// <summary>Appends the service's statements to a persist batch.</summary>
        /// <param name="context">The persist context, of the operation's kind.</param>
        /// <returns>A task that completes when the statements are appended.</returns>
        Task ContributeAsync(PersistContext<TEntity> context);

        /// <summary>Runs the service's best-effort work after a commit.</summary>
        /// <param name="outcome">The committed operation's outcome, of its kind.</param>
        /// <returns>A task that completes when the work ran.</returns>
        Task AfterCommitAsync(PersistOutcome<TEntity> outcome);

        /// <summary>Adds the service's details extras to a details plan.</summary>
        /// <param name="plan">The details plan.</param>
        void ContributeDetails(DetailsPlan<TEntity, TKey> plan);

        /// <summary>The service's search filter for a term; null keeps the platform's.</summary>
        /// <param name="search">The search term.</param>
        /// <returns>The filter, or null.</returns>
        FilterTree? SearchFilter(string search);

        /// <summary>Why a row refuses an ability the caller's grant admits; null when it does not.</summary>
        /// <param name="row">The row.</param>
        /// <param name="ability">The ability: <c>save</c>, <c>delete</c>, <c>activate</c> or <c>action:&lt;name&gt;</c>.</param>
        /// <returns>The reason's resource key, or null.</returns>
        string? RefuseOnRecord(TEntity row, string ability);
    }
}
