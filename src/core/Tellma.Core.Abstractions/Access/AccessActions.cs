// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Access
{
    /// <summary>
    ///     The platform's securable actions: PascalCase identifiers of at most 32 characters. A
    ///     capability or a service may declare further actions of its own.
    /// </summary>
    public static class AccessActions
    {
        /// <summary>Query, details, and export in any format.</summary>
        public const string Read = "Read";

        /// <summary>Create and update, import included.</summary>
        public const string Save = "Save";

        /// <summary>Delete by ids, by query, and with descendants.</summary>
        public const string Delete = "Delete";

        /// <summary>Activate and deactivate.</summary>
        public const string Activate = "Activate";

        /// <summary>Invite users through the identity server.</summary>
        public const string Invite = "Invite";

        /// <summary>Read and write another user's preferences.</summary>
        public const string Preferences = "Preferences";

        /// <summary>Manage a service account's client credentials.</summary>
        public const string Credentials = "Credentials";

        /// <summary>Retry a failed job.</summary>
        public const string Retry = "Retry";

        /// <summary>Cancel a job.</summary>
        public const string Cancel = "Cancel";

        /// <summary>Resume a paused job.</summary>
        public const string Resume = "Resume";

        /// <summary>Share a stored report or dashboard.</summary>
        public const string Share = "Share";

        /// <summary>Publish a stored report or dashboard.</summary>
        public const string Publish = "Publish";

        /// <summary>Transfer a stored report or dashboard to another owner.</summary>
        public const string Transfer = "Transfer";

        /// <summary>
        ///     The wildcard, valid only as a stored permission's action, where it matches every action of
        ///     the resource. No securable may be named with it.
        /// </summary>
        public const string Wildcard = "*";
    }
}
