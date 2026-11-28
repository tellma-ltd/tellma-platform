// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Tellma.Core.Abstractions.Client
{
    /// <summary>
    ///     One scalar property of an entity as the client formats, edits, filters and labels it. Every
    ///     property the query language reads appears, and every JSON column, which it does not.
    /// </summary>
    /// <param name="Name">The property's name, as paths, labels and declarations spell it: <c>CreditLimit</c>.</param>
    /// <param name="Json">The property's wire name: <c>creditLimit</c>.</param>
    /// <param name="Type">The property's type.</param>
    /// <param name="Queryable">Whether queries may filter, sort and select by it.</param>
    /// <param name="Nullable">Whether the value may be empty.</param>
    /// <param name="Unique">Whether a value identifies one row; omitted when false.</param>
    /// <param name="Long">Whether the property is an integer that is a C# <see cref="long" />, whose values travel as decimal text; omitted when false.</param>
    /// <param name="LabelKey">The resource key of the property's label; a multilingual twin carries its anchor's.</param>
    /// <param name="LabelFallback">The humanized English a client shows when no resource declares <paramref name="LabelKey" />; null, and omitted, when one does.</param>
    /// <param name="Excel">Whether spreadsheet import and export include the property.</param>
    /// <param name="Ownership">Who writes the value.</param>
    /// <param name="Defaulted">Whether a save may leave the value empty for the server to fill; omitted when false.</param>
    /// <param name="MaxLength">The longest text accepted; null, and omitted, for no limit or a value that is no text.</param>
    /// <param name="Precision">The total digits of a decimal; null, and omitted, otherwise.</param>
    /// <param name="Scale">The digits after the point of a decimal; null, and omitted, otherwise.</param>
    /// <param name="Enum">The named enum the value takes its values from; null, and omitted, for none.</param>
    /// <param name="Multilingual">The multilingual group and slot the property fills; null, and omitted, for none.</param>
    /// <param name="ForeignKeyOf">The navigation this property is the key of, such as <c>Parent</c>; null, and omitted, for none.</param>
    /// <param name="Blob">The upload policy of a blob reference property; null, and omitted, otherwise.</param>
    [SuppressMessage(
        "Naming",
        "CA1720:Identifier contains type name",
        Justification = "The members are the client metadata's wire names, which the client reads by these names.")]
    public sealed record ClientPropertyMetadata(
        string Name,
        string Json,
        ClientDataType Type,
        bool Queryable,
        bool Nullable,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Unique,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Long,
        string LabelKey,
        string? LabelFallback,
        bool Excel,
        ClientPropertyOwnership Ownership,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Defaulted,
        int? MaxLength,
        int? Precision,
        int? Scale,
        string? Enum,
        ClientMultilingualSlot? Multilingual,
        string? ForeignKeyOf,
        ClientBlobKindPolicy? Blob);
}
