// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Validation;

namespace Tellma.Core.Users
{
    // The service is spec 0018's RoleService; this is its shell, which carries the stack and spec
    // 0014 §3.7's duplicate-permission collapse until the details extras (members, problems) join
    // it. The access rules of spec 0014 §8.4 are a separate component, so a distribution leaf
    // cannot drop them.
    /// <summary>
    ///     The roles' stack service: the standard operations over the tenant's roles and their
    ///     permissions, closed over the distribution's role leaf. The access rules that keep the
    ///     roles' writes safe run beside it as a component of the stack.
    /// </summary>
    /// <remarks>
    ///     A save collapses each role's exact duplicate permissions (the same resource, action and
    ///     filter) to one row before they are validated, counted and stored. A leaf that overrides
    ///     <see cref="PreprocessAsync" /> calls this one to keep it.
    /// </remarks>
    /// <typeparam name="TRole">The stack's role leaf.</typeparam>
    /// <param name="factory">The pipeline factory the scope resolves.</param>
    public class RoleService<TRole>(IEntityPipelineFactory factory) : EntityService<TRole>(factory)
        where TRole : Role
    {
        /// <summary>Preprocesses a save: the base's preprocessing, then each role's exact duplicate permissions collapsed to one row.</summary>
        /// <param name="context">The save's context; its rows have no final ids yet.</param>
        /// <returns>A task that completes when the payload is preprocessed.</returns>
        protected override async Task PreprocessAsync(SaveContext<TRole> context)
        {
            ArgumentNullException.ThrowIfNull(context);
            await base.PreprocessAsync(context).ConfigureAwait(false);
            foreach (TRole role in context.Entities)
            {
                _ = PermissionCollapse.Collapse(role);
            }
        }
    }
}
