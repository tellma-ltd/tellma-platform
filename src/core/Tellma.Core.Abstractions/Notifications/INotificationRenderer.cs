// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Localization;

namespace Tellma.Core.Abstractions.Notifications
{
    /// <summary>
    ///     Renders a notification's text on the server, in a given reader's language, calendar and
    ///     display zone. A scoped service.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A notification row stores its type's key and its arguments, never text, so a reader who
    ///         changes language reads every notification in the new one. The client renders rows
    ///         itself from its string pack; the server renders for the callers that have no client,
    ///         such as a channel delivering to each recipient in that recipient's context.
    ///     </para>
    ///     <para>
    ///         The text is the type's resource <c>Notification_&lt;Key&gt;</c>, with every <c>.</c> of
    ///         the key replaced by <c>_</c>, formatted as an ICU message with the arguments' top-level
    ///         members; when the type also declares <c>Notification_&lt;Key&gt;_Detail</c>, that line
    ///         follows on a new line. A <see cref="LocalizedText" /> argument renders as the reader's
    ///         content slot. A missing resource renders the key itself, and malformed arguments render
    ///         the message without them.
    ///     </para>
    /// </remarks>
    public interface INotificationRenderer
    {
        /// <summary>Renders one notification for one reader.</summary>
        /// <param name="type">The notification's type key.</param>
        /// <param name="argumentsJson">The notification's arguments as a JSON object, or <see langword="null" /> for none.</param>
        /// <param name="targetResource">The resource the notification points at, or <see langword="null" />.</param>
        /// <param name="targetId">The id of the row the notification points at, or <see langword="null" />.</param>
        /// <param name="localization">The reader's language, calendar, display zone and content slot.</param>
        /// <returns>The title, then the detail line on a new line when the type has one.</returns>
        string Render(string type, string? argumentsJson, string? targetResource, long? targetId, LocalizationContext localization);
    }
}
