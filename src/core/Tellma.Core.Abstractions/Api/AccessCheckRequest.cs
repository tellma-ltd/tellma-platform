// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Access;

namespace Tellma.Core.Abstractions.Api
{
    /// <summary>The body of <c>access/check</c>: the access decisions of one user over a list of securables, answered in order.</summary>
    /// <remarks>
    ///     <see cref="Securables" /> is required: a body that omits it or sends null is refused as a
    ///     binding error.
    /// </remarks>
    /// <param name="UserId">The user whose access is checked; null for the caller.</param>
    /// <param name="Securables">The securables to decide, in the order the answer lists them; never null.</param>
    /// <exception cref="System.Text.Json.JsonException"><paramref name="Securables" /> is null.</exception>
    public sealed record AccessCheckRequest(int? UserId, IReadOnlyList<SecurableRef> Securables)
    {
        /// <inheritdoc cref="AccessCheckRequest(int?, IReadOnlyList{SecurableRef})" />
        public IReadOnlyList<SecurableRef> Securables { get; } = WireMembers.Required(Securables, "securables");
    }
}
