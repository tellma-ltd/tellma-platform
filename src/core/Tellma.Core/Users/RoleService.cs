// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Crud;

namespace Tellma.Core.Users
{
    // The service is spec 0018's RoleService; this is its shell, which carries the stack until the
    // details extras (members, problems) and the duplicate collapse join it. The access rules of
    // spec 0014 §8.4 are a separate component, so a distribution leaf cannot drop them.
    /// <summary>
    ///     The roles' stack service: the standard operations over the tenant's roles and their
    ///     permissions, closed over the distribution's role leaf. The access rules that keep the
    ///     roles' writes safe run beside it as a component of the stack.
    /// </summary>
    /// <typeparam name="TRole">The stack's role leaf.</typeparam>
    /// <param name="factory">The pipeline factory the scope resolves.</param>
    public class RoleService<TRole>(IEntityPipelineFactory factory) : EntityService<TRole>(factory)
        where TRole : Role;
}
