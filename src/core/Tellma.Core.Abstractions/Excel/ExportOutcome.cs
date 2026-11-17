// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Excel
{
    /// <summary>A synchronous export's workbook, ready to send.</summary>
    /// <remarks>
    ///     The workbook is spooled to a temporary file, which disposing the outcome (or its stream)
    ///     deletes. The web layer sends <see cref="Workbook" /> as the response body and disposes it.
    /// </remarks>
    /// <param name="Workbook">A readable, seekable stream over the spooled workbook, positioned at its start.</param>
    /// <param name="FileName">The download name: the entity's plural label and the export's time, ASCII only.</param>
    /// <param name="Rows">The data rows written to the parent sheet.</param>
    /// <exception cref="ArgumentNullException"><paramref name="Workbook" /> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="FileName" /> is null, empty or white space.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="Rows" /> is negative.</exception>
    public sealed record ExportOutcome(Stream Workbook, string FileName, int Rows) : IDisposable, IAsyncDisposable
    {
        /// <inheritdoc cref="ExportOutcome(Stream, string, int)" />
        public Stream Workbook { get; init => field = value ?? throw new ArgumentNullException(nameof(Workbook)); } =
            Workbook ?? throw new ArgumentNullException(nameof(Workbook));

        /// <inheritdoc cref="ExportOutcome(Stream, string, int)" />
        public string FileName { get; init => field = NotBlank(value); } = NotBlank(FileName);

        /// <inheritdoc cref="ExportOutcome(Stream, string, int)" />
        public int Rows { get; init => field = NotNegative(value); } = NotNegative(Rows);

        /// <inheritdoc />
        public void Dispose()
        {
            Workbook.Dispose();
        }

        /// <inheritdoc />
        public ValueTask DisposeAsync()
        {
            return Workbook.DisposeAsync();
        }

        /// <summary>Refuses a blank file name.</summary>
        /// <param name="value">The name.</param>
        /// <returns>The name.</returns>
        private static string NotBlank(string value)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value, nameof(FileName));
            return value;
        }

        /// <summary>Refuses a negative row count.</summary>
        /// <param name="value">The count.</param>
        /// <returns>The count.</returns>
        private static int NotNegative(int value)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value, nameof(Rows));
            return value;
        }
    }
}
