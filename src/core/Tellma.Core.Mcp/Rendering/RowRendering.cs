// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Tellma.Core.Abstractions.Data;

namespace Tellma.Core.Mcp.Rendering
{
    /// <summary>A rendered tool result: one text block, and whether rows or entities were left out to stay under the cap.</summary>
    /// <param name="Text">The text.</param>
    /// <param name="Truncated">Whether the character cap left anything out.</param>
    internal sealed record RenderedText(string Text, bool Truncated);

    /// <summary>How a page of rows was asked for, which its footer reports.</summary>
    /// <param name="Skip">The rows skipped.</param>
    /// <param name="Top">The rows asked for.</param>
    /// <param name="Counted">Whether the count was asked for.</param>
    internal sealed record RowPaging(int Skip, int Top, bool Counted);

    /// <summary>
    ///     Renders a page of rows as one text block: a Markdown table, or a JSON array of objects keyed
    ///     by column name, then a footer of what was returned. Rows are added one by one under the
    ///     character cap; the first that would pass it, and every row after, is left out and the
    ///     footer says so, with the skip that pages on.
    /// </summary>
    internal static class RowRendering
    {
        /// <summary>The characters held back for the footer, so it always fits under the cap.</summary>
        public const int FooterReserve = 600;

        /// <summary>The guidance of a truncated page.</summary>
        public const string TruncatedGuidance = "narrow the select or filter, or page with skip";

        /// <summary>Renders a page.</summary>
        /// <param name="page">The page the service answered.</param>
        /// <param name="format">The rendering.</param>
        /// <param name="paging">How the page was asked for.</param>
        /// <param name="maxChars">The cap on the text's length.</param>
        /// <returns>The text.</returns>
        public static RenderedText Render(RowPage page, QueryFormat format, RowPaging paging, int maxChars)
        {
            ArgumentNullException.ThrowIfNull(page);
            ArgumentNullException.ThrowIfNull(paging);
            using RowCells cells = new(page.Rows);
            StringBuilder text = new();
            int budget = Math.Max(0, maxChars - FooterReserve);

            // --- The rows, one at a time under the budget ---
            int returned = format == QueryFormat.Objects ? Objects(cells, text, budget) : Table(cells, text, budget);
            bool truncated = returned < cells.RowCount;

            // --- The footer: what came back, the count when asked, and how to go on ---
            _ = text.Append('\n').Append('\n');
            Footer(text, page, paging, returned, truncated);
            return new RenderedText(text.ToString(), truncated);
        }

        /// <summary>Writes the rows as a Markdown table.</summary>
        /// <param name="cells">The rows' cells.</param>
        /// <param name="text">The text so far.</param>
        /// <param name="budget">The length the rows may reach.</param>
        /// <returns>The rows written.</returns>
        private static int Table(RowCells cells, StringBuilder text, int budget)
        {
            // --- The header and its rule ---
            IReadOnlyList<QueryColumn> columns = cells.Columns;
            _ = text.Append('|');
            foreach (QueryColumn column in columns)
            {
                _ = text.Append(' ').Append(Escape(column.Name)).Append(" |");
            }

            _ = text.Append('\n').Append('|');
            for (int column = 0; column < columns.Count; column++)
            {
                _ = text.Append(" --- |");
            }

            // --- The rows: an absent value is an empty cell ---
            StringBuilder line = new();
            for (int row = 0; row < cells.RowCount; row++)
            {
                _ = line.Clear().Append('\n').Append('|');
                for (int column = 0; column < columns.Count; column++)
                {
                    string? cell = cells.Text(column, row);
                    _ = line.Append(' ').Append(cell is null ? string.Empty : Escape(cell)).Append(" |");
                }

                if (text.Length + line.Length > budget)
                {
                    return row;
                }

                _ = text.Append(line);
            }

            return cells.RowCount;
        }

        /// <summary>Writes the rows as a JSON array of objects, one per line.</summary>
        /// <param name="cells">The rows' cells.</param>
        /// <param name="text">The text so far.</param>
        /// <param name="budget">The length the rows may reach.</param>
        /// <returns>The rows written.</returns>
        private static int Objects(RowCells cells, StringBuilder text, int budget)
        {
            ArrayBufferWriter<byte> buffer = new(1024);
            using Utf8JsonWriter writer = new(buffer, RowCells.WriterOptions);
            _ = text.Append('[');
            int written = cells.RowCount;
            for (int row = 0; row < cells.RowCount; row++)
            {
                // --- One object, measured before it is kept ---
                buffer.ResetWrittenCount();
                writer.Reset(buffer);
                cells.WriteObject(writer, row);
                writer.Flush();
                string item = Encoding.UTF8.GetString(buffer.WrittenSpan);

                // The separator, the item and the closing bracket must all fit.
                if (text.Length + item.Length + 3 > budget)
                {
                    written = row;
                    break;
                }

                _ = text.Append(row == 0 ? "\n" : ",\n").Append(item);
            }

            _ = text.Append(written == 0 ? "]" : "\n]");
            return written;
        }

        /// <summary>Writes a page's footer.</summary>
        /// <param name="text">The text so far.</param>
        /// <param name="page">The page.</param>
        /// <param name="paging">How it was asked for.</param>
        /// <param name="returned">The rows written.</param>
        /// <param name="truncated">Whether the cap left rows out.</param>
        private static void Footer(StringBuilder text, RowPage page, RowPaging paging, int returned, bool truncated)
        {
            CultureInfo invariant = CultureInfo.InvariantCulture;
            int next = paging.Skip + returned;
            _ = text.Append(invariant, $"returned: {returned} rows from skip {paging.Skip}");

            // --- The count, held to its cap as the web surface holds it ---
            if (paging.Counted && page.Count is int count)
            {
                int cap = page.CountCap ?? count;
                _ = text.Append(invariant, $"\ncount: {Math.Min(count, cap)}, countCapped: {(count > cap ? "true" : "false")}");
            }

            // --- How to go on: past the cap, or past a full page ---
            if (truncated)
            {
                _ = text.Append(invariant, $"\ntruncated: true — the result reached its size limit after {returned} of {page.Rows.RowCount} rows; {TruncatedGuidance}={next}.");
            }
            else if (page.Rows.RowCount >= paging.Top)
            {
                _ = text.Append(invariant, $"\nThe page is full, so more rows may follow: page with skip={next}.");
            }
        }

        /// <summary>A cell's text made safe for a Markdown table cell: pipes escaped, line breaks as spaces.</summary>
        /// <param name="value">The text.</param>
        /// <returns>The escaped text.</returns>
        internal static string Escape(string value)
        {
            ArgumentNullException.ThrowIfNull(value);
            if (value.AsSpan().IndexOfAny("|\\\r\n") < 0)
            {
                return value;
            }

            StringBuilder escaped = new(value.Length + 8);
            foreach (char c in value)
            {
                _ = c switch
                {
                    '|' => escaped.Append("\\|"),
                    '\\' => escaped.Append("\\\\"),
                    '\r' or '\n' => escaped.Append(' '),
                    _ => escaped.Append(c),
                };
            }

            return escaped.ToString();
        }
    }
}
