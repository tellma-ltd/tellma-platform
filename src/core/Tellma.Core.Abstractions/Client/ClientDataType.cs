// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Tellma.Core.Abstractions.Client
{
    /// <summary>
    ///     A value's type as the client formats, edits and filters it.
    ///     Each member travels as the name its <see cref="JsonStringEnumMemberNameAttribute" /> declares.
    /// </summary>
    /// <remarks>
    ///     Every integer width is <see cref="Integer" />; a C# <see cref="long" /> also carries its
    ///     own flag, since its values travel as decimal text. <see cref="Float" />, <see cref="Time" />,
    ///     <see cref="Json" /> and <see cref="Binary" /> values are never queryable.
    /// </remarks>
    [SuppressMessage(
        "Naming",
        "CA1720:Identifier contains type name",
        Justification = "The members name the value types of the client metadata's wire contract, which the client switches on by these names.")]
    public enum ClientDataType
    {
        /// <summary>Text.</summary>
        [JsonStringEnumMemberName("string")]
        String,

        /// <summary>A whole number of any width: an <see cref="int" />, a <see cref="short" />, a <see cref="byte" /> or a <see cref="long" />.</summary>
        [JsonStringEnumMemberName("integer")]
        Integer,

        /// <summary>An exact decimal number, which travels as decimal text.</summary>
        [JsonStringEnumMemberName("decimal")]
        Decimal,

        /// <summary>A binary floating-point number.</summary>
        [JsonStringEnumMemberName("float")]
        Float,

        /// <summary>True or false.</summary>
        [JsonStringEnumMemberName("bool")]
        Bool,

        /// <summary>A calendar date: a <see cref="DateOnly" />.</summary>
        [JsonStringEnumMemberName("date")]
        Date,

        /// <summary>A wall-clock date and time without an offset: a <see cref="System.DateTime" />.</summary>
        [JsonStringEnumMemberName("datetime")]
        DateTime,

        /// <summary>A point in time with its offset: a <see cref="DateTimeOffset" />.</summary>
        [JsonStringEnumMemberName("instant")]
        Instant,

        /// <summary>A time of day: a <see cref="TimeOnly" />.</summary>
        [JsonStringEnumMemberName("time")]
        Time,

        /// <summary>A globally unique identifier.</summary>
        [JsonStringEnumMemberName("guid")]
        Guid,

        /// <summary>One value of a named enum: a C# enum or a value set.</summary>
        [JsonStringEnumMemberName("enum")]
        Enum,

        /// <summary>A JSON document.</summary>
        [JsonStringEnumMemberName("json")]
        Json,

        /// <summary>Bytes.</summary>
        [JsonStringEnumMemberName("binary")]
        Binary,
    }
}
