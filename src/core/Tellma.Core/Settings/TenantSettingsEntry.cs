// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Settings;

namespace Tellma.Core.Settings
{
    /// <summary>
    ///     One tenant's entry in the settings cache: the server's view and the client's, serialized
    ///     once when the settings were loaded.
    /// </summary>
    /// <param name="Settings">The server's view.</param>
    /// <param name="ClientJson">The client's view, <see cref="TenantSettingsForClient" />, as UTF-8 JSON; shared, never to be changed.</param>
    /// <param name="IsPlaceholder">
    ///     Whether the database holds the unprovisioned placeholder: such an entry is served only to
    ///     the platform's own work, and never stored.
    /// </param>
    internal sealed record TenantSettingsEntry(TenantSettings Settings, byte[] ClientJson, bool IsPlaceholder);
}
