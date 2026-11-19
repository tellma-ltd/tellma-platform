// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text.Json.Serialization;

namespace Tellma.Core.Abstractions.Client
{
    /// <summary>
    ///     What an action answers.
    ///     Each member travels as the name its <see cref="JsonStringEnumMemberNameAttribute" /> declares.
    /// </summary>
    public enum ClientActionResult
    {
        /// <summary>The records it ran on, read back in details shape.</summary>
        [JsonStringEnumMemberName("entities")]
        Entities,

        /// <summary>A result of its own.</summary>
        [JsonStringEnumMemberName("own")]
        Own,

        /// <summary>Nothing: an API action returning a bare task.</summary>
        [JsonStringEnumMemberName("none")]
        None,
    }
}
