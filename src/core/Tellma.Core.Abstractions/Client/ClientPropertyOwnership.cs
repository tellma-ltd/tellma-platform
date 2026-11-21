// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text.Json.Serialization;

namespace Tellma.Core.Abstractions.Client
{
    /// <summary>
    ///     Who writes a property's value, which decides whether the client edits it.
    ///     Each member travels as the name its <see cref="JsonStringEnumMemberNameAttribute" /> declares.
    /// </summary>
    public enum ClientPropertyOwnership
    {
        /// <summary>The client writes it on create and on update.</summary>
        [JsonStringEnumMemberName("editable")]
        Editable,

        /// <summary>The client writes it on create only.</summary>
        [JsonStringEnumMemberName("write-once")]
        WriteOnce,

        /// <summary>The server writes it; the client shows it read-only.</summary>
        [JsonStringEnumMemberName("server-owned")]
        ServerOwned,

        /// <summary>The server computes it from other values; the client shows it read-only.</summary>
        [JsonStringEnumMemberName("derived")]
        Derived,

        /// <summary>The database writes it, as it does a key or a stamp; the client shows it read-only.</summary>
        [JsonStringEnumMemberName("database-owned")]
        DatabaseOwned,
    }
}
