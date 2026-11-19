// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text.Json.Serialization;

namespace Tellma.Core.Abstractions.Client
{
    /// <summary>
    ///     An operation an entity's API offers, as the client gates its pages and buttons on it.
    ///     Each member travels as the name its <see cref="JsonStringEnumMemberNameAttribute" /> declares.
    /// </summary>
    public enum ClientEntityOperation
    {
        /// <summary>Lists rows: the search page and every picker.</summary>
        [JsonStringEnumMemberName("query")]
        Query,

        /// <summary>Reads one row in details shape: the details page.</summary>
        [JsonStringEnumMemberName("details")]
        Details,

        /// <summary>Creates and updates rows.</summary>
        [JsonStringEnumMemberName("save")]
        Save,

        /// <summary>Deletes rows by id.</summary>
        [JsonStringEnumMemberName("delete")]
        Delete,

        /// <summary>Imports rows from a spreadsheet.</summary>
        [JsonStringEnumMemberName("import")]
        Import,

        /// <summary>Exports rows to a spreadsheet.</summary>
        [JsonStringEnumMemberName("export")]
        Export,
    }
}
