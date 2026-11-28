// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Settings
{
    /// <summary>Who may see a setting's value.</summary>
    public enum SettingVisibility
    {
        /// <summary>
        ///     Read by server code. Its value leaves the server only through the settings editor's
        ///     view, to a member holding read on its category; its code default is public.
        /// </summary>
        Server,

        /// <summary>Also sent to every member's browser with the tenant's client settings.</summary>
        Client,

        /// <summary>
        ///     Server-only and protected at rest: stored encrypted under a tenant-scoped purpose,
        ///     decrypted on first read in server code, and never returned once stored. Its default is
        ///     always null.
        /// </summary>
        Secret,
    }
}
