// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Api
{
    /// <summary>The body of a single-target entity action, which names exactly one row and answers a result of its own.</summary>
    /// <remarks>
    ///     <see cref="Id" /> is required and positive. It binds as <c>id</c>, so a validation error
    ///     on the target row reads <c>id</c>.
    /// </remarks>
    /// <param name="Id">The row's id; a number or a string on the wire, narrowed to the stack's key type.</param>
    public sealed record IdRequest(long Id);

    /// <summary>The body of a single-target entity action that takes arguments.</summary>
    /// <remarks>
    ///     <see cref="Id" /> follows the rules of <see cref="IdRequest.Id" />. An error in the
    ///     arguments is rooted at <c>arguments</c>.
    /// </remarks>
    /// <typeparam name="TArguments">The action's arguments record.</typeparam>
    /// <param name="Id">The row's id; a number or a string on the wire, narrowed to the stack's key type.</param>
    /// <param name="Arguments">The action's arguments.</param>
    public sealed record IdRequest<TArguments>(long Id, TArguments Arguments);
}
