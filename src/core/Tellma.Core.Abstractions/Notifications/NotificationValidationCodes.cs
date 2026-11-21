// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Notifications
{
    /// <summary>
    ///     The codes a save of a user's notification preferences refuses a list with, each at the row
    ///     and member it names: <c>[i].type</c>, <c>[i].channel</c> or <c>[i].enabled</c>.
    /// </summary>
    public static class NotificationValidationCodes
    {
        /// <summary>A row names a type no composed feature registers. Argument: <c>type</c>.</summary>
        public const string UnknownType = "Notifications.UnknownType";

        /// <summary>A row names a channel no composed feature registers. Argument: <c>channel</c>.</summary>
        public const string UnknownChannel = "Notifications.UnknownChannel";

        /// <summary>A row repeats the type and channel of an earlier row. Arguments: <c>type</c> and <c>channel</c>.</summary>
        public const string DuplicatePreference = "Notifications.DuplicatePreference";

        /// <summary>A row turns the inbox off for a type that cannot be muted. Argument: <c>type</c>.</summary>
        public const string CannotMute = "Notifications.CannotMute";
    }
}
