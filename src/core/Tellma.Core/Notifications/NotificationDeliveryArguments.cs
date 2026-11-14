// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Notifications;

namespace Tellma.Core.Notifications
{
    /// <summary>
    ///     The arguments of one <c>core.notification-delivery</c> job: one slice of one notification
    ///     request's recipients, carried to the channels other than the inbox. Serialised with the
    ///     platform's JSON options into the job's arguments.
    /// </summary>
    /// <remarks>
    ///     The request is rebuilt from it when the job runs, so it carries everything a channel reads
    ///     but the duplicate key, which governs inbox rows only.
    /// </remarks>
    /// <param name="Type">The notification's type key.</param>
    /// <param name="Severity">The resolved severity: the request's, else its type's.</param>
    /// <param name="ArgumentsJson">The request's serialised arguments; null for none.</param>
    /// <param name="TargetResource">The resource the notification points at; null for none.</param>
    /// <param name="TargetId">The id of the row the notification points at; null for none.</param>
    /// <param name="ActorUserId">The user who caused the notification; null for the system.</param>
    /// <param name="RecipientIds">The slice's distinct recipients, in the request's order.</param>
    public sealed record NotificationDeliveryArguments(
        string Type,
        NotificationSeverity Severity,
        string? ArgumentsJson,
        string? TargetResource,
        long? TargetId,
        int? ActorUserId,
        IReadOnlyList<int> RecipientIds);
}
