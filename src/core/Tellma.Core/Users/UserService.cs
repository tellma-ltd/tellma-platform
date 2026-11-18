// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Crud;

namespace Tellma.Core.Users
{
    // The service is spec 0018's UserService; this is its shell, which carries the stack and the
    // self-service enlistment pair until the hooks, the actions and the routes join it. The access
    // rules of spec 0014 §8.4 are a separate component, so a distribution leaf cannot drop them.
    /// <summary>
    ///     The users' stack service: the standard operations over the tenant's users and their role
    ///     memberships, closed over the distribution's user leaf. The access rules that keep the
    ///     users' writes safe run beside it as a component of the stack.
    /// </summary>
    /// <remarks>
    ///     The service declares that it enlists writes of its own stack: a user's self-service writes
    ///     run under the stack's authority through the caller's frame.
    /// </remarks>
    /// <typeparam name="TUser">The stack's user leaf.</typeparam>
    /// <param name="factory">The pipeline factory the scope resolves.</param>
    public class UserService<TUser>(IEntityPipelineFactory factory) : EntityService<TUser>(factory), IEnlists<TUser>
        where TUser : User;
}
