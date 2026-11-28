// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Internal;

namespace Tellma.Core.Abstractions.Excel
{
    /// <summary>
    ///     What an import plan made of one column: its header, the path it maps to and why. Echoing
    ///     the column back as an <see cref="ImportColumnMapping" /> keeps its meaning.
    /// </summary>
    /// <param name="Column">The column's letters (<c>C</c>), which an override addresses.</param>
    /// <param name="Header">The header cell's text; empty when the cell is blank.</param>
    /// <param name="Path">
    ///     The resolved column path: a multilingual column's slot in the target tenant, a reference's
    ///     key. Null when the column maps to nothing.
    /// </param>
    /// <param name="Status">What the plan made of the column.</param>
    /// <param name="Language">The language of a multilingual column, a BCP 47 code; null otherwise.</param>
    /// <param name="Suggestions">For an unmapped column, up to three paths whose headers are nearest its own; empty otherwise.</param>
    /// <exception cref="ArgumentException"><paramref name="Column" /> is null, empty or white space, or a suggestion is null.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="Header" /> or <paramref name="Suggestions" /> is null.</exception>
    public sealed record ImportPlanColumn(
        string Column,
        string Header,
        string? Path,
        ImportColumnStatus Status,
        string? Language,
        IReadOnlyList<string> Suggestions)
    {
        /// <inheritdoc cref="ImportPlanColumn(string, string, string?, ImportColumnStatus, string?, IReadOnlyList{string})" />
        public string Column { get; init => field = NotBlank(value); } = NotBlank(Column);

        /// <inheritdoc cref="ImportPlanColumn(string, string, string?, ImportColumnStatus, string?, IReadOnlyList{string})" />
        public string Header { get; init => field = value ?? throw new ArgumentNullException(nameof(Header)); } =
            Header ?? throw new ArgumentNullException(nameof(Header));

        /// <inheritdoc cref="ImportPlanColumn(string, string, string?, ImportColumnStatus, string?, IReadOnlyList{string})" />
        public IReadOnlyList<string> Suggestions { get; init => field = ReadOnlyCopies.OfEntries(value, requireOne: false, nameof(Suggestions)); } =
            ReadOnlyCopies.OfEntries(Suggestions, requireOne: false, nameof(Suggestions));

        /// <summary>Refuses blank letters.</summary>
        /// <param name="value">The letters.</param>
        /// <returns>The letters.</returns>
        private static string NotBlank(string value)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value, nameof(Column));
            return value;
        }
    }
}
