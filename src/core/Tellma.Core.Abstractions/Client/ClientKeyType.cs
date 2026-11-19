// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Tellma.Core.Abstractions.Client
{
    /// <summary>
    ///     The type of an entity's key, which decides how the client holds an id.
    ///     Each member travels as the name its <see cref="JsonStringEnumMemberNameAttribute" /> declares.
    /// </summary>
    [SuppressMessage(
        "Naming",
        "CA1720:Identifier contains type name",
        Justification = "The members name the key types of the client metadata's wire contract, which the client switches on by these names.")]
    public enum ClientKeyType
    {
        /// <summary>A 32-bit key: ids travel as JSON numbers.</summary>
        [JsonStringEnumMemberName("int")]
        Int,

        /// <summary>A 64-bit key: ids travel as decimal text.</summary>
        [JsonStringEnumMemberName("long")]
        Long,
    }
}
