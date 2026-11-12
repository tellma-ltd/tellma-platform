// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Notifications
{
    /// <summary>
    ///     One message fanned out to many recipients: at most one inbox row per recipient, and the
    ///     delivery to every other channel the recipient enabled for the type.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Platform or distribution code composes a request, never a client, so a request that
    ///         breaks a rule below is a programming error: <see cref="INotifier" /> refuses it with
    ///         <see cref="InvalidOperationException" /> before it appends anything.
    ///     </para>
    ///     <para>
    ///         A recipient who is inactive or unknown when the request is persisted receives nothing,
    ///         and neither does one who turned the inbox off for a mutable type; a request never fails
    ///         the write it rides on for such a reason.
    ///     </para>
    /// </remarks>
    /// <param name="Type">The key of a type registered on this instance.</param>
    /// <param name="RecipientIds">
    ///     The recipients' user ids. Duplicates collapse, the empty list writes nothing, and the list
    ///     holds at most <see cref="NotificationsOptions.MaxRecipientsPerRequest" /> ids.
    /// </param>
    /// <param name="Arguments">
    ///     The message's arguments, serialised with the platform's JSON options into the row: a JSON
    ///     object whose top-level members are the message's ICU arguments and the client's click data.
    ///     At most <see cref="NotificationsOptions.MaxArgumentsChars" /> characters once serialised;
    ///     <see langword="null" /> for none.
    /// </param>
    /// <param name="TargetResource">
    ///     The resource the notification points at, equal to the type's
    ///     <see cref="NotificationTypeDescriptor.TargetResource" />; required with
    ///     <paramref name="TargetId" /> when the type declares a target, and forbidden when it does not.
    /// </param>
    /// <param name="TargetId">The id of the row the notification points at; set exactly when <paramref name="TargetResource" /> is.</param>
    /// <param name="ActorUserId">
    ///     The user who caused the notification, an existing user id; <see langword="null" /> for the
    ///     system. The caller passes the request's user where one acted.
    /// </param>
    /// <param name="DedupKey">
    ///     At most 128 characters: the key under which a recipient's earlier row counts as a duplicate,
    ///     matched without regard to type. Activity about a record keys by
    ///     <c>&lt;resource&gt;:&lt;id&gt;</c> (<c>acme.Document:42</c>); everything else keys by its type
    ///     key, with an anchor where it needs one (<c>core.schedule.paused:42</c>).
    ///     <see langword="null" /> for no duplicate detection.
    /// </param>
    /// <param name="OnDuplicate">What a duplicate does to this request; <see cref="DuplicatePolicy.Replace" /> and <see cref="DuplicatePolicy.Ongoing" /> require <paramref name="DedupKey" />.</param>
    /// <param name="InboxOnly">
    ///     True to write inbox rows only and deliver to no other channel. A notice about a failing
    ///     delivery path is raised this way, so that it never feeds the path it reports on. Refused on
    ///     a channel-only type, which has no rows.
    /// </param>
    /// <param name="Severity">The severity of this request's rows; <see langword="null" /> for the type's <see cref="NotificationTypeDescriptor.Severity" />.</param>
    public sealed record NotificationRequest(
        string Type,
        IReadOnlyList<int> RecipientIds,
        object? Arguments,
        string? TargetResource,
        long? TargetId,
        int? ActorUserId,
        string? DedupKey,
        DuplicatePolicy OnDuplicate = DuplicatePolicy.Suppress,
        bool InboxOnly = false,
        NotificationSeverity? Severity = null);
}
