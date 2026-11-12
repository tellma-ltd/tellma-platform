// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Notifications
{
    /// <summary>
    ///     What a <see cref="NotificationRequest" /> does for a recipient who already holds a
    ///     duplicate: a row with the same <see cref="NotificationRequest.DedupKey" />, whatever its
    ///     type. Duplicates are detected on a best-effort basis, so two raises racing each other can
    ///     both write a row.
    /// </summary>
    public enum DuplicatePolicy
    {
        /// <summary>
        ///     The recipient receives nothing while an unread duplicate exists. The default; a read
        ///     duplicate does not count, so the next raise after it was read writes a new row.
        /// </summary>
        Suppress,

        /// <summary>
        ///     The unread duplicate becomes this request's row, keeping its id: its type, severity,
        ///     arguments, target and actor are replaced, its creation time is refreshed, and it stays
        ///     unread, so it resurfaces unseen and on top. A recipient without an unread duplicate
        ///     receives a new row. Requires a dedup key.
        /// </summary>
        Replace,

        /// <summary>
        ///     As <see cref="Suppress" />, but a read duplicate counts too: a condition raised again
        ///     while it still holds writes nothing, and the raiser withdraws it when it clears.
        ///     Requires a dedup key.
        /// </summary>
        Ongoing,
    }
}
