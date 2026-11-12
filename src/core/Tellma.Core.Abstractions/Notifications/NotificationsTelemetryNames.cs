// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Notifications
{
    /// <summary>
    ///     The names of the notification and inbox instruments and their tags, on the platform's
    ///     <see cref="MeterName" /> meter. Dashboards and alerts key on these strings, so they never
    ///     change. Every tag takes a closed set of values; the tenant and the user are never tags.
    /// </summary>
    public static class NotificationsTelemetryNames
    {
        /// <summary>The meter every Core instrument is created on.</summary>
        public const string MeterName = "Tellma.Core";

        /// <summary>Counter (<c>{notification}</c>): inbox rows inserted. Tagged <see cref="TypeTag" />.</summary>
        public const string Created = "tellma.notifications.created";

        /// <summary>
        ///     Counter (<c>{notification}</c>): recipients who received no row because they were
        ///     inactive, had muted the type or held a duplicate. Tagged <see cref="TypeTag" />.
        /// </summary>
        public const string Suppressed = "tellma.notifications.suppressed";

        /// <summary>Counter (<c>{notification}</c>): unread duplicates refreshed in place. Tagged <see cref="TypeTag" />.</summary>
        public const string Replaced = "tellma.notifications.replaced";

        /// <summary>Counter (<c>{notification}</c>): rows deleted by a withdrawal. Tagged <see cref="TypeTag" />.</summary>
        public const string Withdrawn = "tellma.notifications.withdrawn";

        /// <summary>Counter (<c>{job}</c>): delivery jobs inserted, one per qualifying slice of recipients. Tagged <see cref="TypeTag" />.</summary>
        public const string DeliveryEnqueued = "tellma.notifications.delivery_enqueued";

        /// <summary>Counter (<c>{notification}</c>): rows deleted by retention. Tagged <see cref="BandTag" />.</summary>
        public const string Retained = "tellma.notifications.retained";

        /// <summary>Counter (<c>{request}</c>): inbox summaries served. Untagged.</summary>
        public const string Summaries = "tellma.notifications.inbox.summaries";

        /// <summary>Counter (<c>{notification}</c>): rows a user marked read. Tagged <see cref="VerbTag" />.</summary>
        public const string MarkedRead = "tellma.notifications.inbox.read";

        /// <summary>Counter (<c>{notification}</c>): rows a user cleared. Tagged <see cref="VerbTag" />.</summary>
        public const string Cleared = "tellma.notifications.inbox.cleared";

        /// <summary>Counter (<c>{request}</c>): notification preference saves. Untagged.</summary>
        public const string PreferencesSaved = "tellma.notifications.preferences.saved";

        /// <summary>The type tag: the notification's registered type key.</summary>
        public const string TypeTag = "type";

        /// <summary>The retention band tag: <c>read</c> or <c>unread</c>.</summary>
        public const string BandTag = "band";

        /// <summary>The inbox verb tag: <c>read</c> or <c>read-all</c> on <see cref="MarkedRead" />, <c>clear</c> or <c>clear-read</c> on <see cref="Cleared" />.</summary>
        public const string VerbTag = "verb";
    }
}
