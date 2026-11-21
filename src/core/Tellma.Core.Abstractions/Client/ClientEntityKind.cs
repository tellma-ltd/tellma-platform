// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text.Json.Serialization;

namespace Tellma.Core.Abstractions.Client
{
    /// <summary>
    ///     What kind of root an entity is in the client metadata.
    ///     Each member travels as the name its <see cref="JsonStringEnumMemberNameAttribute" /> declares.
    /// </summary>
    public enum ClientEntityKind
    {
        /// <summary>An entity with a stack of its own: its API resource answers queries and, by its operations, writes.</summary>
        [JsonStringEnumMemberName("stack")]
        Stack,

        /// <summary>A child entity of a stack's aggregate, queried through its own child query surface.</summary>
        [JsonStringEnumMemberName("child")]
        Child,

        /// <summary>A root the query language reads but no API resource serves, reached only through paths.</summary>
        [JsonStringEnumMemberName("readonly")]
        ReadOnly,
    }
}
