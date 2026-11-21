// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Localization;

namespace Tellma.Core.Abstractions.Notifications
{
    /// <summary>
    ///     One recipient a channel delivers a notification to: an active user who enabled the channel
    ///     for the type, read when the delivery job runs, so a change made since the raise is honoured.
    /// </summary>
    /// <param name="UserId">The user's id.</param>
    /// <param name="Name">The user's name.</param>
    /// <param name="Email">The user's sign-in email address, or <see langword="null" />.</param>
    /// <param name="ContactEmail">The address the user asked to be contacted at, or <see langword="null" />; an email channel prefers it to <paramref name="Email" />.</param>
    /// <param name="ContactMobile">The mobile number the user asked to be contacted at, or <see langword="null" />.</param>
    /// <param name="Localization">
    ///     The language, calendar, display zone and content slot the recipient's text renders in: the
    ///     user's own preferences, else the tenant's.
    /// </param>
    public sealed record NotificationRecipient(
        int UserId,
        string Name,
        string? Email,
        string? ContactEmail,
        string? ContactMobile,
        LocalizationContext Localization);
}
