// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Notifications
{
    /// <summary>
    ///     The withdrawal of a condition that no longer holds: every row carrying the dedup key is
    ///     deleted, read or not, whatever its type. A key with no rows deletes nothing.
    /// </summary>
    /// <param name="DedupKey">The key the condition was raised under; required, at most 128 characters.</param>
    /// <param name="RecipientIds">
    ///     The users whose rows are deleted; <see langword="null" /> for every user's. Duplicates
    ///     collapse, the empty list deletes nothing, and the list holds at most
    ///     <see cref="NotificationsOptions.MaxRecipientsPerRequest" /> ids.
    /// </param>
    public sealed record WithdrawRequest(string DedupKey, IReadOnlyList<int>? RecipientIds = null);
}
