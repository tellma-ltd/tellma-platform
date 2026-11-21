// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Access
{
    /// <summary>
    ///     One user's outcome of the users' <c>invite</c> action, answered in request order: the
    ///     identity server's status, or why the user was not invited.
    /// </summary>
    /// <remarks>
    ///     Exactly one of <see cref="Status" /> and <see cref="Error" /> is set. An error is a closed
    ///     code of the platform, such as <c>Users.SubjectMismatch</c> or
    ///     <c>Users.SandboxRequiresExistingIdentity</c>, or the identity server's own text for a user
    ///     it refused.
    /// </remarks>
    /// <param name="Id">The user's id.</param>
    /// <param name="Status">The identity server's answer; null when the user was not invited.</param>
    /// <param name="Error">Why the user was not invited; null when the user was.</param>
    public sealed record InviteResult(int Id, InviteStatus? Status, string? Error);
}
