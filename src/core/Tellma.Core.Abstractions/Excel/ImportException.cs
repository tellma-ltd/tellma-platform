// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Internal;

namespace Tellma.Core.Abstractions.Excel
{
    /// <summary>
    ///     An import failed: the errors found, each at its workbook cell. Maps to 422 with the problem
    ///     code <c>validation</c>, one <c>errors[]</c> item per error with its coordinates, and
    ///     <c>totalErrors</c> and <c>committedRowRanges</c> beside them.
    /// </summary>
    /// <remarks>
    ///     <see cref="Errors" /> may be capped, in sheet, row and column order; <see cref="TotalErrors" />
    ///     says how many exist. <see cref="CommittedRowRanges" /> is empty for a synchronous import,
    ///     which commits nothing when it fails.
    /// </remarks>
    /// <param name="errors">The errors reported, in sheet, row and column order; at least one, none null.</param>
    /// <param name="totalErrors">How many errors exist; at least as many as reported.</param>
    /// <param name="committedRowRanges">The runs of rows already committed by earlier chunks, in ascending order; none null.</param>
    /// <exception cref="ArgumentNullException"><paramref name="errors" /> or <paramref name="committedRowRanges" /> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="errors" /> is empty, or a list holds a null entry.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="totalErrors" /> is below the number of errors reported.</exception>
    public sealed class ImportException(IReadOnlyList<ImportError> errors, int totalErrors, IReadOnlyList<RowRange> committedRowRanges)
        : TellmaException(ProblemCode, Describe(errors, totalErrors))
    {
        /// <summary>The problem code of this outcome.</summary>
        private const string ProblemCode = "validation";

        // Member initializers run before the base constructor, so the lists are validated here,
        // before the log message is rendered from them.

        /// <summary>The errors reported, in sheet, row and column order.</summary>
        public IReadOnlyList<ImportError> Errors { get; } = ReadOnlyCopies.OfEntries(errors, requireOne: true, nameof(errors));

        /// <summary>The runs of rows already committed, in ascending order; empty for a synchronous import.</summary>
        public IReadOnlyList<RowRange> CommittedRowRanges { get; } =
            ReadOnlyCopies.OfEntries(committedRowRanges, requireOne: false, nameof(committedRowRanges));

        /// <summary>How many errors exist, reported or not.</summary>
        public int TotalErrors { get; } = totalErrors >= errors.Count
            ? totalErrors
            : throw new ArgumentOutOfRangeException(nameof(totalErrors), totalErrors, "The total counts at least the errors reported.");

        /// <summary>Renders the log message.</summary>
        /// <param name="errors">The errors.</param>
        /// <param name="totalErrors">The total.</param>
        /// <returns>The message.</returns>
        private static string Describe(IReadOnlyList<ImportError> errors, int totalErrors)
        {
            // The message is built before the members validate, so the list is checked here first.
            errors = ReadOnlyCopies.OfEntries(errors, requireOne: true, nameof(errors));

            // Each item names its cell when the workbook can place it, else its code alone.
            string list = LogText.List(errors, static error => error.Sheet is null
                ? error.Code
                : string.Create(CultureInfo.InvariantCulture, $"{error.Code} at {error.Sheet}!{error.Column}{error.Row}"));

            return string.Create(CultureInfo.InvariantCulture, $"The import failed with {totalErrors} error(s): {list}.");
        }
    }
}
