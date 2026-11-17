// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.ObjectModel;

namespace Tellma.Core.Abstractions.Access
{
    // One holder is spec 0014 §5.1's AccessHolder.
    /// <summary>One user who holds a securable through the roles they belong to, and until when.</summary>
    /// <remarks>Construction copies the permission ids and refuses an undefined or a denied outcome.</remarks>
    /// <param name="UserId">The user.</param>
    /// <param name="Outcome">What the user's own roles allow: <see cref="AccessOutcome.Filtered" /> or <see cref="AccessOutcome.Unrestricted" />.</param>
    /// <param name="PermissionIds">The stored permissions behind the outcome, ascending.</param>
    /// <param name="EndsAt">
    ///     The latest end among the user's current memberships of the roles behind
    ///     <see cref="PermissionIds" />; null when one of them is permanent.
    /// </param>
    public sealed record AccessHolder(int UserId, AccessOutcome Outcome, IReadOnlyList<int> PermissionIds, DateTimeOffset? EndsAt)
    {
        /// <summary>What the user's own roles allow.</summary>
        public AccessOutcome Outcome { get; } = Outcome is AccessOutcome.Filtered or AccessOutcome.Unrestricted
            ? Outcome
            : throw new ArgumentOutOfRangeException(nameof(Outcome), Outcome, "A holder holds the securable, filtered or unrestricted.");

        /// <summary>The stored permissions behind the outcome, ascending.</summary>
        public IReadOnlyList<int> PermissionIds { get; } = new ReadOnlyCollection<int>([.. PermissionIds ?? throw new ArgumentNullException(nameof(PermissionIds))]);
    }
}
