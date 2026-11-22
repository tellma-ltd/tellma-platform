// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Queryex
{
    /// <summary>The backend's precision and scale of one decimal number.</summary>
    /// <param name="Precision">The significant digits, 1 to 38.</param>
    /// <param name="Scale">The digits after the point, at most the precision.</param>
    public readonly record struct QueryexNumericType(byte Precision, byte Scale);

    /// <summary>
    ///     What one run of a statement read, which the measures need before they can be evaluated
    ///     over its partials.
    /// </summary>
    /// <param name="DecimalTypes">
    ///     Parallel to <see cref="CompiledMeasures.Partials" />: the result column's precision and
    ///     scale as the reader reported them, for every sum and for a least or greatest value that is
    ///     a decimal; null for every other partial.
    /// </param>
    /// <param name="Arguments">
    ///     The declared parameters' values, by name ignoring case; an omitted parameter is absent. A
    ///     number is a <see cref="decimal" />, an <see cref="int" /> or a <see cref="long" />.
    /// </param>
    /// <param name="Today">The value the statement's date slot was bound with.</param>
    /// <param name="Now">The value the statement's instant slot was bound with.</param>
    /// <param name="UserId">The value the statement's user slot was bound with, or null for none.</param>
    public sealed record MeasureContext(
        IReadOnlyList<QueryexNumericType?> DecimalTypes,
        IReadOnlyDictionary<string, object?> Arguments,
        DateOnly Today,
        DateTimeOffset Now,
        object? UserId);
}
