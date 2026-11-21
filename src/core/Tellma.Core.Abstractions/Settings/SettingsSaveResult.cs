// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Settings
{
    /// <summary>What a settings save answers: the editor's view and the client's, as committed, so neither reloads.</summary>
    /// <param name="Details">
    ///     The editor's view of every category the caller may read, with a stamp for every stored row,
    ///     listed in the save or not.
    /// </param>
    /// <param name="Client">The client's view, as every member now reads it.</param>
    /// <param name="SettingsTag">The client view's new wire tag, which the response's version-tags header carries too.</param>
    public sealed record SettingsSaveResult(TenantSettingsDetails Details, TenantSettingsForClient Client, string SettingsTag);
}
