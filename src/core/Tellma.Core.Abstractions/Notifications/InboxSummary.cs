// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Data;

namespace Tellma.Core.Abstractions.Notifications
{
    /// <summary>
    ///     What the bell shows for the calling user: the capped unseen and unread counts, the most
    ///     severe unseen row, the newest rows, the caller's own jobs still running, the users who
    ///     acted in the newest rows, and the inbox's version.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The counts are capped rather than maintained: a client shows <c>99+</c> from 100. The
    ///         version is read before the rest, so a copy whose <see cref="Version" /> is at or above
    ///         the version an inbox verb returned already contains that verb's effect.
    ///     </para>
    ///     <para>
    ///         <see cref="Latest" /> and <see cref="Running" /> are raw rows the client renders in the
    ///         reader's language.
    ///     </para>
    /// </remarks>
    /// <param name="Unseen">
    ///     How many rows arrived after the user last opened the bell, up to
    ///     <see cref="NotificationsOptions.SummaryCountCap" />.
    /// </param>
    /// <param name="MaxUnseenSeverity">
    ///     The most severe of those unseen rows, the newest ones within the cap; <see langword="null" />
    ///     when none is unseen.
    /// </param>
    /// <param name="Unread">How many rows are unread, up to <see cref="NotificationsOptions.SummaryCountCap" />.</param>
    /// <param name="Latest">
    ///     The newest rows, read or not, at most <see cref="NotificationsOptions.SummaryLatestCount" />,
    ///     newest first.
    /// </param>
    /// <param name="Running">
    ///     The caller's own jobs that have not ended, cron occurrences aside, at most
    ///     <see cref="NotificationsOptions.SummaryLatestCount" />, newest first.
    /// </param>
    /// <param name="Related">The users who acted in <see cref="Latest" />, as their related projection shows them.</param>
    /// <param name="Version">The caller's inbox version, which every inbox verb increments.</param>
    /// <param name="SeenAt">When the caller last opened the bell; <see langword="null" /> when never.</param>
    public sealed record InboxSummary(
        int Unseen,
        NotificationSeverity? MaxUnseenSeverity,
        int Unread,
        IReadOnlyList<Notification> Latest,
        IReadOnlyList<InboxRunningJob> Running,
        RelatedEntities Related,
        long Version,
        DateTimeOffset? SeenAt);
}
