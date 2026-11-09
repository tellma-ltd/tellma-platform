// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Diagnostics.CodeAnalysis;

namespace Tellma.Core.Abstractions.Settings
{
    /// <summary>
    ///     The kind of value a setting holds, as the settings editor renders it. A key's nullable and
    ///     array forms are separate flags of its descriptor.
    /// </summary>
    [SuppressMessage(
        "Naming",
        "CA1720:Identifier contains type name",
        Justification = "The members name the value kinds of the settings wire contract, which the client switches on by these names.")]
    public enum SettingValueType
    {
        /// <summary>A <see cref="bool" />.</summary>
        Boolean,

        /// <summary>An <see cref="int" />.</summary>
        Int32,

        /// <summary>A <see cref="long" />, written as a JSON string.</summary>
        Int64,

        /// <summary>A <see cref="decimal" />, written as a JSON string.</summary>
        Decimal,

        /// <summary>A <see cref="string" />.</summary>
        String,

        /// <summary>A <see cref="DateOnly" />, <c>yyyy-MM-dd</c>.</summary>
        Date,

        /// <summary>A <see cref="TimeOnly" />.</summary>
        Time,

        /// <summary>A <see cref="System.Guid" />.</summary>
        Guid,

        /// <summary>An enum, written as its member's name; the descriptor lists the members.</summary>
        Enum,

        /// <summary>Any other shape, serialized through the key's own source-generated contract.</summary>
        Json,
    }
}
