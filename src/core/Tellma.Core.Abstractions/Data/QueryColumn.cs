// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Queryex;

namespace Tellma.Core.Abstractions.Data
{
    /// <summary>
    ///     One column of a <see cref="QueryRowSet" />: the projection of a compiled query's column, with
    ///     the numeric facets the result set itself reported.
    /// </summary>
    /// <param name="Name">The column's name: the select item's text.</param>
    /// <param name="Type">The column's query-language type.</param>
    /// <param name="Scale">
    ///     On a <see cref="QueryexType.QxNumeric" /> column only: 0 for an integer result, the decimal's
    ///     scale for a decimal result, null for a floating-point result. Null on every other type.
    /// </param>
    /// <param name="Precision">A decimal result's precision; null on every other column.</param>
    /// <param name="Nullable">Whether the column can hold null.</param>
    /// <param name="Path">The navigation path of a column that reads a property, root first; null for a computed column.</param>
    /// <param name="GroupingKey">Whether the column is a grouping key of an aggregate query.</param>
    public sealed record QueryColumn(
        string Name, QueryexType Type, int? Scale, int? Precision, bool Nullable, IReadOnlyList<string>? Path,
        bool GroupingKey);
}
