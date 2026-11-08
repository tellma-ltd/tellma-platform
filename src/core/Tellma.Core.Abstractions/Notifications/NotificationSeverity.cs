// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Notifications
{
    /// <summary>
    ///     How serious a notification is, in ascending order. It drives the row's icon and the
    ///     colour of the inbox badge, which shows the most severe unseen notification.
    /// </summary>
    public enum NotificationSeverity
    {
        /// <summary>Information; the default.</summary>
        Info,

        /// <summary>Something the user asked for completed.</summary>
        Success,

        /// <summary>Something needs the user's attention.</summary>
        Warning,

        /// <summary>Something failed.</summary>
        Error,
    }
}
