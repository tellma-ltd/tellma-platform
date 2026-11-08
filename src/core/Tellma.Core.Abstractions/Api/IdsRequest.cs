// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Api
{
    /// <summary>
    ///     A body that names rows by id: <c>delete</c>, <c>delete-with-descendants</c>, an entity
    ///     action with a result of its own that targets many rows, and an operation whose body is ids
    ///     alone.
    /// </summary>
    /// <remarks>
    ///     <see cref="Ids" /> is required, with between one and the stack's id ceiling entries, else a
    ///     limit-exceeded error. It binds as <c>ids</c>, so a validation error on a target row reads
    ///     <c>ids[i]</c>.
    /// </remarks>
    /// <param name="Ids">The rows' ids; numbers or strings on the wire, narrowed to the stack's key type.</param>
    public sealed record IdsRequest(IReadOnlyList<long> Ids);

    /// <summary>
    ///     The body of an entity action with a result of its own that targets many rows and takes
    ///     arguments.
    /// </summary>
    /// <remarks>
    ///     <see cref="Ids" /> follows the rules of <see cref="IdsRequest.Ids" />. An error in the
    ///     arguments is rooted at <c>arguments</c> (<c>arguments.creditLimit</c>).
    /// </remarks>
    /// <typeparam name="TArguments">The action's arguments record.</typeparam>
    /// <param name="Ids">The rows' ids; numbers or strings on the wire, narrowed to the stack's key type.</param>
    /// <param name="Arguments">The action's arguments.</param>
    public sealed record IdsRequest<TArguments>(IReadOnlyList<long> Ids, TArguments Arguments);
}
