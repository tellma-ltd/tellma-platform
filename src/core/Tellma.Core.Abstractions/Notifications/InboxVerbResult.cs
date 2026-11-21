// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Notifications
{
    /// <summary>What an inbox verb did: the rows it changed and the inbox's version after it.</summary>
    /// <remarks>
    ///     Every verb increments the version, even one that changed no row, so a client that holds it
    ///     knows that an <see cref="InboxSummary" /> whose <see cref="InboxSummary.Version" /> is at or
    ///     above it contains the verb.
    /// </remarks>
    /// <param name="Count">
    ///     The rows the verb changed: 1 for marking the inbox seen, the rows newly marked read, or the
    ///     rows deleted.
    /// </param>
    /// <param name="Version">The caller's inbox version after the verb.</param>
    public sealed record InboxVerbResult(int Count, long Version);
}
