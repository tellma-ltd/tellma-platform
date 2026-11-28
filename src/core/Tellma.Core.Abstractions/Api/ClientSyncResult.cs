// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Settings;

namespace Tellma.Core.Abstractions.Api
{
    /// <summary>
    ///     The answer of <c>client/sync</c>: the tenant's facts and current tags, and only the cached
    ///     documents whose held tags differ.
    /// </summary>
    /// <param name="Tenant">The tenant's facts; always present.</param>
    /// <param name="Tags">
    ///     The current wire tags by name: always <c>settings</c>, <c>preferences</c>,
    ///     <c>permissions</c> and <c>entities</c>, plus one <c>entity:&lt;Name&gt;</c> per cacheable
    ///     entity when the held <c>entities</c> tag differs or is absent.
    /// </param>
    /// <param name="Settings">
    ///     The tenant's settings as the client sees them, written from the bytes the settings cache
    ///     serialized when it loaded them; null when the held <c>settings</c> tag is current.
    /// </param>
    /// <param name="Me">
    ///     The caller's own envelope, as <c>users/me</c> answers it; null when the held
    ///     <c>preferences</c> and <c>permissions</c> tags are both current.
    /// </param>
    /// <param name="StaleEntities">The cacheable entities whose held <c>entity:&lt;Name&gt;</c> tag differs, in name order.</param>
    public sealed record ClientSyncResult(
        TenantFacts Tenant,
        IReadOnlyDictionary<string, string> Tags,
        PreSerializedJson<TenantSettingsForClient>? Settings,
        MeResult? Me,
        IReadOnlyList<string> StaleEntities);
}
