// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text.Json;
using Tellma.Core.Abstractions.Data;

namespace Tellma.Core.Abstractions.Settings
{
    /// <summary>
    ///     The settings editor's view of the tenant's settings: only the categories the caller may
    ///     read, each value with the stamp a save expects back for it.
    /// </summary>
    /// <remarks>
    ///     A secret's value never appears; <see cref="SetKeys" /> tells only whether one is stored.
    ///     The keys' descriptions travel in the client metadata, not here.
    /// </remarks>
    /// <param name="General">
    ///     The typed row with its audit columns, when the caller may read the general settings; null
    ///     otherwise. Its <c>ModifiedAt</c> is the stamp a save of a typed member expects back.
    /// </param>
    /// <param name="Values">
    ///     The current value of every non-secret key of the categories the caller may read, by key:
    ///     the stored value, else the key's default.
    /// </param>
    /// <param name="SetKeys">The keys of those categories a row is stored for, secrets included.</param>
    /// <param name="EntryStamps">
    ///     The modification stamp of each stored row of those categories, by key: what a save of that
    ///     entry expects back.
    /// </param>
    /// <param name="Related">
    ///     The display projection of every entity a reference key's value points at, as the cached
    ///     settings held the ids; an id whose row no longer exists is absent.
    /// </param>
    public sealed record TenantSettingsDetails(
        Settings? General,
        IReadOnlyDictionary<string, JsonElement> Values,
        IReadOnlyList<string> SetKeys,
        IReadOnlyDictionary<string, DateTimeOffset> EntryStamps,
        RelatedEntities Related);
}
