// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text.Json.Serialization;

namespace Tellma.Core.Abstractions.Client
{
    /// <summary>
    ///     Whether an action runs on records of its entity or is an API call of its own.
    ///     Each member travels as the name its <see cref="JsonStringEnumMemberNameAttribute" /> declares.
    /// </summary>
    public enum ClientActionKind
    {
        /// <summary>An entity action: it runs on the records a request names by id or filter.</summary>
        [JsonStringEnumMemberName("entity")]
        Entity,

        /// <summary>An API action: a call with a body of its own, reached through the entity's resource.</summary>
        [JsonStringEnumMemberName("api")]
        Api,
    }
}
