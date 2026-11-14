// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Notifications;
using Tellma.Core.Abstractions.TableTypes;

namespace Tellma.Core.Notifications
{
    /// <summary>
    ///     A notification request checked and shaped for its statement: the resolved severity and
    ///     actor, the serialised arguments, the distinct recipients numbered from 0, and the delivery
    ///     slices when the request enqueues delivery.
    /// </summary>
    /// <param name="Type">The registered type key.</param>
    /// <param name="Severity">The resolved severity: the request's, else the type's.</param>
    /// <param name="ArgumentsJson">The serialised arguments; null for none.</param>
    /// <param name="TargetResource">The target resource; null for none.</param>
    /// <param name="TargetId">The target row's id; null for none.</param>
    /// <param name="ActorUserId">The resolved actor; null for the system.</param>
    /// <param name="DedupKey">The duplicate key; null for none.</param>
    /// <param name="Policy">What a duplicate does.</param>
    /// <param name="Mutable">Whether the type's inbox channel may be muted, so the insert reads the preferences.</param>
    /// <param name="Inbox">Whether the type writes inbox rows; false for a channel-only type.</param>
    /// <param name="Recipients">The distinct recipients, in first-seen order, each numbered by its position.</param>
    /// <param name="Slices">The delivery slices; null when the request enqueues no delivery.</param>
    /// <param name="DeliveryCondition">
    ///     The delivery condition, a composite format whose one placeholder is the type's parameter;
    ///     null when the request enqueues no delivery.
    /// </param>
    /// <param name="TraceParent">The raising activity's W3C trace parent; null for none.</param>
    internal sealed record ShapedNotification(
        string Type,
        NotificationSeverity Severity,
        string? ArgumentsJson,
        string? TargetResource,
        long? TargetId,
        int? ActorUserId,
        string? DedupKey,
        DuplicatePolicy Policy,
        bool Mutable,
        bool Inbox,
        IReadOnlyList<NotificationRowList> Recipients,
        IReadOnlyList<NotificationSliceList>? Slices,
        string? DeliveryCondition,
        string? TraceParent)
    {
        /// <summary>The parameters the request's statement binds: the eight scalars and the recipients, the slices and the trace parent with an enqueue; four for a channel-only one.</summary>
        public int ParameterCount => (Inbox, Slices is not null) switch
        {
            (true, true) => 11,
            (true, false) => 9,
            (false, true) => 4,
            (false, false) => 0,
        };
    }

    /// <summary>What one request's statement did, read from its result sets.</summary>
    /// <param name="Requested">How many distinct recipients the request named.</param>
    /// <param name="InsertedUserIds">The users who received a new row.</param>
    /// <param name="RefreshedUserIds">The users whose unread duplicate was refreshed in place.</param>
    /// <param name="JobIds">The delivery jobs enqueued, one per qualifying slice.</param>
    internal sealed record NotifyOutcome(
        int Requested,
        IReadOnlyList<int> InsertedUserIds,
        IReadOnlyList<int> RefreshedUserIds,
        IReadOnlyList<int> JobIds);

    /// <summary>What one withdrawal deleted, read from its result set.</summary>
    /// <param name="Deleted">The deleted rows' users and types.</param>
    internal sealed record WithdrawOutcome(IReadOnlyList<WithdrawnRow> Deleted);

    /// <summary>One row a withdrawal deleted.</summary>
    /// <param name="UserId">The row's recipient.</param>
    /// <param name="Type">The row's type key.</param>
    internal sealed record WithdrawnRow(int UserId, string Type);
}
