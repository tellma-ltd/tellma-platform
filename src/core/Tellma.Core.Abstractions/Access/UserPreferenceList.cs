// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Tellma.Core.Abstractions.Entities;

namespace Tellma.Core.Abstractions.Access
{
    /// <summary>
    ///     A row of the table-valued parameter that writes a user's preferences: one key and its value.
    /// </summary>
    /// <remarks>
    ///     A standalone table-type shape, mapped in schema <c>dbo</c> under its class name. Preferences are
    ///     an opaque bag the server never interprets: the value is JSON by convention only.
    /// </remarks>
    [TableType]
    public class UserPreferenceList
    {
        /// <summary>The preference's key, dotted ASCII segments such as <c>nav.pinned</c>, <c>varchar(128)</c>; the row's key.</summary>
        [Key]
        [Column(TypeName = "varchar(128)")]
        public string Key { get; set; } = null!;

        /// <summary>The preference's value, opaque to the server, <c>nvarchar(max)</c>.</summary>
        public string Value { get; set; } = null!;
    }
}
