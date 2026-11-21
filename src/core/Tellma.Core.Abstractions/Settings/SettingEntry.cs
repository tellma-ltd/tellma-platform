// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Entities;

namespace Tellma.Core.Abstractions.Settings
{
    /// <summary>
    ///     One stored value of a declared <see cref="SettingKey{T}" />: a row exists only while the
    ///     value departs from the key's default.
    /// </summary>
    /// <remarks>
    ///     A secret key's row holds the protected text of its value, never the value itself. The
    ///     class is a mapped entity of its own, never a stack, and no securable reads it through a
    ///     query.
    /// </remarks>
    [Table("SettingEntries", Schema = "core")]
    [TableType]
    [Temporal]
    [BumpsVersionTag(VersionTagNames.Settings)]
    public sealed class SettingEntry : TopLevelEntity
    {
        /// <summary>The key's name, <c>nvarchar(128)</c>, unique: <c>gl.posting.autoNumberOnPost</c>.</summary>
        [MaxLength(SettingKey.MaxNameLength)]
        [Unique]
        public string Key { get; set; } = null!;

        /// <summary>
        ///     The value as one JSON value, <c>nvarchar(max)</c>: the key's own encoding, or for a
        ///     secret key a JSON string holding the protected text of that encoding.
        /// </summary>
        [JsonColumn]
        public string Value { get; set; } = null!;
    }
}
