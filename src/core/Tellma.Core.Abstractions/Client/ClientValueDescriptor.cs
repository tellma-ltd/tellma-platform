// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Tellma.Core.Abstractions.Client
{
    /// <summary>
    ///     The type facts that choose a value's display format and control, for a value that is no
    ///     entity property: an action's argument field.
    /// </summary>
    /// <param name="Type">The value's type.</param>
    /// <param name="Nullable">Whether the value may be empty.</param>
    /// <param name="Enum">The named enum the value takes its values from; null, and omitted, for none.</param>
    /// <param name="MaxLength">The longest text accepted; null, and omitted, for no limit.</param>
    /// <param name="Scale">The digits after the point of a decimal; null, and omitted, when unstated.</param>
    /// <param name="Long">Whether the value is an integer that is a C# <see cref="long" />, whose values travel as decimal text; omitted when false.</param>
    /// <param name="Target">The entity whose id the value holds, such as <c>core.User</c>; null, and omitted, for none.</param>
    /// <param name="Array">Whether the value is a list of enum values or of ids; omitted when false.</param>
    [SuppressMessage(
        "Naming",
        "CA1720:Identifier contains type name",
        Justification = "The members are the client metadata's wire names, which the client reads by these names.")]
    public sealed record ClientValueDescriptor(
        ClientDataType Type,
        bool Nullable,
        string? Enum,
        int? MaxLength,
        int? Scale,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Long,
        string? Target,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Array);
}
