// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Internal;
using Tellma.Core.Queryex;

namespace Tellma.Core.Excel
{
    /// <summary>
    ///     One sheet column: its header, the type its cells encode by, and what the manifest records of
    ///     it. A column encodes by its type, never by the values it meets, so it stays homogeneous and
    ///     sortable.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         With <see cref="Property" /> set, the property's CLR type decides the encoding (an
    ///         <c>int</c> or a <c>long</c> key, an enum, a value set, a <c>TimeOnly</c>); without it,
    ///         <see cref="Type" /> and <see cref="Scale" /> do, read tolerantly: a numeric column whose
    ///         value is an integer is written as one.
    ///     </para>
    ///     <para>
    ///         The init-only members beyond the four positional ones describe an editable column for
    ///         the manifest and the validation lists; a display or report column leaves them at their
    ///         defaults.
    ///     </para>
    /// </remarks>
    /// <param name="Header">The header cell's text.</param>
    /// <param name="Type">The Queryex result type of the column's values.</param>
    /// <param name="Scale">The scale of a decimal column read from a bare path; null for an expression, which takes the general format.</param>
    /// <param name="Path">The column path's segments (<c>Parent</c>, <c>Code</c>); null for an expression.</param>
    /// <exception cref="ArgumentNullException"><paramref name="Header" /> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="Scale" /> is negative or above 28.</exception>
    /// <exception cref="ArgumentException"><paramref name="Path" /> is empty or holds a blank segment.</exception>
    public sealed record ExcelColumnSpec(string Header, QueryexType Type, int? Scale, IReadOnlyList<string>? Path)
    {
        /// <summary>The largest scale a decimal carries.</summary>
        private const int MaxScale = 28;

        /// <inheritdoc cref="ExcelColumnSpec(string, QueryexType, int?, IReadOnlyList{string}?)" />
        public string Header { get; init => field = value ?? throw new ArgumentNullException(nameof(Header)); } =
            Header ?? throw new ArgumentNullException(nameof(Header));

        /// <inheritdoc cref="ExcelColumnSpec(string, QueryexType, int?, IReadOnlyList{string}?)" />
        public int? Scale { get; init => field = ValidScale(value); } = ValidScale(Scale);

        /// <inheritdoc cref="ExcelColumnSpec(string, QueryexType, int?, IReadOnlyList{string}?)" />
        public IReadOnlyList<string>? Path { get; init => field = ValidPath(value); } = ValidPath(Path);

        /// <summary>
        ///     The property behind a bare path, whose CLR type, length, enum members and value set
        ///     decide the encoding, the width and the validation list; null for any other column.
        /// </summary>
        public PropertyMetadata? Property { get; init; }

        /// <summary>What the column holds, as the manifest records it; <see cref="ExcelColumnRole.Property" /> by default.</summary>
        public ExcelColumnRole Role { get; init; } = ExcelColumnRole.Property;

        /// <summary>The BCP 47 code of a multilingual column's language; null otherwise.</summary>
        public string? Language { get; init; }

        /// <summary>The target entity's name (<c>gl.Center</c>) of a reference or parent reference column; null otherwise.</summary>
        public string? ReferenceEntity { get; init; }

        /// <summary>Which key of its target a reference column is expressed in; null for any other column.</summary>
        public ExcelKeyKind? KeyKind { get; init; }

        /// <summary>
        ///     The stored values a value-set column offers in its validation list, in order; null for
        ///     none. An enum's come from <see cref="Property" />.
        /// </summary>
        public IReadOnlyList<string>? Choices
        {
            get;
            init => field = value is null ? null : ReadOnlyCopies.OfEntries(value, requireOne: false, nameof(Choices));
        }

        /// <summary>The column path joined with dots (<c>Parent.Code</c>); null for an expression.</summary>
        public string? PathText => Path is null ? null : string.Join('.', Path);

        /// <summary>Refuses a scale a decimal cannot carry.</summary>
        /// <param name="value">The scale.</param>
        /// <returns>The scale.</returns>
        private static int? ValidScale(int? value)
        {
            if (value is int scale)
            {
                ArgumentOutOfRangeException.ThrowIfNegative(scale, nameof(Scale));
                ArgumentOutOfRangeException.ThrowIfGreaterThan(scale, MaxScale, nameof(Scale));
            }

            return value;
        }

        /// <summary>Refuses an empty path or a blank segment, and copies the rest.</summary>
        /// <param name="value">The segments.</param>
        /// <returns>A read-only copy; null for null.</returns>
        private static IReadOnlyList<string>? ValidPath(IReadOnlyList<string>? value)
        {
            if (value is null)
            {
                return null;
            }

            IReadOnlyList<string> copy = ReadOnlyCopies.OfEntries(value, requireOne: true, nameof(Path));
            foreach (string segment in copy)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(segment, nameof(Path));
            }

            return copy;
        }
    }
}
