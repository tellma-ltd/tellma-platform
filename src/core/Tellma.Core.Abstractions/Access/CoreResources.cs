// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Access
{
    /// <summary>
    ///     The securable resources the platform itself registers. An entity's resource is its entity
    ///     name; every other resource follows the same dotted grammar: two or more segments of
    ///     <c>[A-Za-z][A-Za-z0-9]*</c> joined by dots, at most 128 characters.
    /// </summary>
    public static class CoreResources
    {
        /// <summary>The users.</summary>
        public const string User = "core.User";

        /// <summary>The roles and their permissions.</summary>
        public const string Role = "core.Role";

        /// <summary>The general settings.</summary>
        public const string SettingsGeneral = "core.Settings.General";

        /// <summary>The prefix of every settings category's resource, which the category's name completes.</summary>
        public const string SettingsPrefix = "core.Settings.";

        /// <summary>The background jobs.</summary>
        public const string Job = "core.Job";

        /// <summary>The job schedules.</summary>
        public const string Schedule = "core.Schedule";

        /// <summary>The notifications.</summary>
        public const string Notification = "core.Notification";

        /// <summary>The outgoing emails.</summary>
        public const string OutgoingEmail = "core.OutgoingEmail";

        /// <summary>The exports.</summary>
        public const string Export = "core.Export";

        /// <summary>The imports.</summary>
        public const string Import = "core.Import";

        /// <summary>The assignments.</summary>
        public const string Assignment = "core.Assignment";

        /// <summary>The stored reports.</summary>
        public const string Report = "core.Report";

        /// <summary>The dashboards.</summary>
        public const string Dashboard = "core.Dashboard";

        /// <summary>The support requests, which are read and deleted only.</summary>
        public const string SupportRequest = "core.SupportRequest";

        /// <summary>
        ///     The wildcard, valid only as a stored permission's resource, where it matches every
        ///     resource. No securable may be named with it, and a permission over it takes no filter.
        /// </summary>
        public const string Wildcard = "*";
    }
}
