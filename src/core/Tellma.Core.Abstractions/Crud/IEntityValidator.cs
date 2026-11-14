// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Validation;

namespace Tellma.Core.Abstractions.Crud
{
    /// <summary>
    ///     A validation component: rules a pack or library adds to a stack it cannot subclass the
    ///     service of. The service's own hook runs first, then every component in registration order.
    /// </summary>
    /// <remarks>
    ///     A component written against a base entity is generic over the leaf, with one type parameter
    ///     constrained to that base, so it follows a distribution's substituted leaf. A component may
    ///     enlist writes into another stack through the context's host when it declares the pair.
    /// </remarks>
    /// <typeparam name="TEntity">The stack's entity type.</typeparam>
    [CollectionService]
    public interface IEntityValidator<TEntity>
    {
        /// <summary>Validates a save.</summary>
        /// <param name="context">The save's context.</param>
        /// <returns>A task that completes when the rules ran.</returns>
        Task ValidateAsync(SaveContext<TEntity> context);

        /// <summary>Validates a delete. Does nothing by default.</summary>
        /// <param name="context">The delete's context.</param>
        /// <returns>A task that completes when the rules ran.</returns>
        Task ValidateDeleteAsync(DeleteContext<TEntity> context)
        {
            return Task.CompletedTask;
        }

        /// <summary>Validates an action. Does nothing by default.</summary>
        /// <param name="action">The action's name.</param>
        /// <param name="context">The action's context.</param>
        /// <returns>A task that completes when the rules ran.</returns>
        Task ValidateActionAsync(string action, ActionContext<TEntity> context)
        {
            return Task.CompletedTask;
        }
    }
}
