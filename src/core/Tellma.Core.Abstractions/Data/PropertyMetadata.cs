// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Entities;

namespace Tellma.Core.Abstractions.Data
{
    /// <summary>
    ///     One mapped scalar property of an entity, as the data layer, the pipeline and the Excel codec
    ///     read it: its column, its type and facets, who writes it, and compiled accessors.
    /// </summary>
    /// <remarks>
    ///     Built by the platform at startup from the model and the entity class, never by hand. The
    ///     accessors are compiled once and are safe to call from any thread.
    /// </remarks>
    /// <param name="Name">The CLR property name, which is also its name on the wire and in queries.</param>
    /// <param name="Column">The column it maps to.</param>
    /// <param name="ClrType">The property's CLR type, nullable value types included (<c>int?</c>, an enum).</param>
    /// <param name="Ownership">Who writes the column.</param>
    /// <param name="IsNullable">Whether the column admits null.</param>
    /// <param name="IsUnique">
    ///     Whether a value identifies one row: the property is the whole primary key, or the whole of a
    ///     unique index, or, on a child, a unique index together with the child's parent key.
    /// </param>
    /// <param name="MaxLength">The column's length for a sized text or binary column; null for <c>max</c> and every other type.</param>
    /// <param name="Precision">The column's precision: a decimal's digits, or a temporal type's fractional-second digits; null otherwise.</param>
    /// <param name="Scale">A decimal column's scale; null otherwise.</param>
    /// <param name="EnumValues">An enum property's member names, in the enum's value order; null otherwise.</param>
    /// <param name="ValueSet">The name of the value set the string column draws from, when it declares one; null otherwise.</param>
    /// <param name="MultilingualGroup">The name of the multilingual group the property belongs to (its anchor's name), as anchor or twin; null otherwise.</param>
    /// <param name="IsSearchable">Whether the property joins the entity's search text (<see cref="SearchableAttribute" />).</param>
    /// <param name="SearchKind">How search text matches it when searchable; null otherwise.</param>
    /// <param name="ExcludedFromExcel">Whether the property is left out of spreadsheets (<see cref="ExcludeFromExcelAttribute" />).</param>
    /// <param name="IsDefaulted">Whether the property may arrive empty for the pipeline to fill (<see cref="DefaultedAttribute" />).</param>
    /// <param name="Getter">Reads the property of an instance of the entity.</param>
    /// <param name="Setter">
    ///     Writes the property of an instance of the entity. Null for a non-nullable value type throws
    ///     <see cref="InvalidOperationException" />; a value of another type throws
    ///     <see cref="InvalidCastException" />.
    /// </param>
    public sealed record PropertyMetadata(
        string Name,
        string Column,
        Type ClrType,
        PropertyOwnership Ownership,
        bool IsNullable,
        bool IsUnique,
        int? MaxLength,
        int? Precision,
        int? Scale,
        IReadOnlyList<string>? EnumValues,
        string? ValueSet,
        string? MultilingualGroup,
        bool IsSearchable,
        SearchKind? SearchKind,
        bool ExcludedFromExcel,
        bool IsDefaulted,
        Func<object, object?> Getter,
        Action<object, object?> Setter);
}
