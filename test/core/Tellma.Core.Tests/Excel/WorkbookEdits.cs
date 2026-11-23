// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using System.Globalization;
using Tellma.Core.Abstractions.Excel;

namespace Tellma.Core.Tests.Excel
{
    /// <summary>
    ///     Edits of a written workbook, as a user makes them in Excel: a sheet's data rows replaced by
    ///     rows of values given by each column's import path, a sheet removed, a header renamed. The
    ///     manifest, the styles and the other sheets stay as they were written.
    /// </summary>
    internal static class WorkbookEdits
    {
        /// <summary>
        ///     Replaces the data rows of each named sheet, below its header row, by rows of values
        ///     keyed by the import path of their columns as the plan reports it.
        /// </summary>
        /// <param name="workbook">The workbook's bytes.</param>
        /// <param name="plan">The workbook's import plan, which names each column's letters by its path.</param>
        /// <param name="sheets">Each sheet's name with its rows: text, a number, a boolean, or null for a blank cell.</param>
        /// <returns>The edited workbook's bytes.</returns>
        public static byte[] WithRows(byte[] workbook, ImportPlan plan, params (string Sheet, IReadOnlyDictionary<string, object?>[] Rows)[] sheets)
        {
            return Edit(workbook, document =>
            {
                foreach ((string name, IReadOnlyDictionary<string, object?>[] rows) in sheets)
                {
                    ImportPlanSheet planned = plan.Sheets.Single(sheet => sheet.Sheet == name);
                    SheetData data = Data(document, name);

                    // --- Everything below the header row goes; the rows given take its place ---
                    Row header = data.Elements<Row>().First();
                    foreach (Row old in data.Elements<Row>().Skip(1).ToList())
                    {
                        old.Remove();
                    }

                    uint index = header.RowIndex!.Value;
                    foreach (IReadOnlyDictionary<string, object?> values in rows)
                    {
                        index++;
                        Row row = new() { RowIndex = index };
                        foreach (ImportPlanColumn column in planned.Columns.OrderBy(static column => column.Column.Length).ThenBy(static column => column.Column, StringComparer.Ordinal))
                        {
                            if (column.Path is string path && values.TryGetValue(path, out object? value) && value is not null)
                            {
                                row.Append(CellOf($"{column.Column}{index.ToString(CultureInfo.InvariantCulture)}", value));
                            }
                        }

                        data.Append(row);
                    }
                }
            });
        }

        /// <summary>Removes a sheet from a workbook, as a user deletes one.</summary>
        /// <param name="workbook">The workbook's bytes.</param>
        /// <param name="name">The sheet's name.</param>
        /// <returns>The edited workbook's bytes.</returns>
        public static byte[] WithoutSheet(byte[] workbook, string name)
        {
            return Edit(workbook, document =>
            {
                WorkbookPart part = document.WorkbookPart!;
                Sheet sheet = part.Workbook!.Sheets!.Elements<Sheet>().Single(candidate => candidate.Name == name);
                OpenXmlPart worksheet = part.GetPartById(sheet.Id!);
                sheet.Remove();
                _ = part.DeletePart(worksheet);

                // The defined names that pointed into the sheet would no longer resolve.
                foreach (DefinedName defined in part.Workbook.DefinedNames?.Elements<DefinedName>().Where(defined => defined.Text.Contains(name, StringComparison.Ordinal)).ToList() ?? [])
                {
                    defined.Remove();
                }
            });
        }

        /// <summary>Opens a copy of a workbook for editing, applies an edit, and saves the copy.</summary>
        /// <param name="workbook">The workbook's bytes.</param>
        /// <param name="edit">The edit.</param>
        /// <returns>The edited bytes.</returns>
        private static byte[] Edit(byte[] workbook, Action<SpreadsheetDocument> edit)
        {
            using MemoryStream copy = new();
            copy.Write(workbook);
            using (var document = SpreadsheetDocument.Open(copy, isEditable: true))
            {
                edit(document);
            }

            return copy.ToArray();
        }

        /// <summary>A sheet's cell data.</summary>
        /// <param name="document">The open workbook.</param>
        /// <param name="name">The sheet's name.</param>
        /// <returns>The data.</returns>
        private static SheetData Data(SpreadsheetDocument document, string name)
        {
            WorkbookPart part = document.WorkbookPart!;
            Sheet sheet = part.Workbook!.Sheets!.Elements<Sheet>().Single(candidate => candidate.Name == name);
            return ((WorksheetPart)part.GetPartById(sheet.Id!)).Worksheet!.GetFirstChild<SheetData>()!;
        }

        /// <summary>A cell holding a value as Excel stores one typed in: text inline, a number, or a boolean.</summary>
        /// <param name="reference">The cell's reference.</param>
        /// <param name="value">The value.</param>
        /// <returns>The cell.</returns>
        private static Cell CellOf(string reference, object value)
        {
            return value switch
            {
                bool flag => new Cell { CellReference = reference, DataType = CellValues.Boolean, CellValue = new CellValue(flag ? "1" : "0") },
                string text => new Cell { CellReference = reference, DataType = CellValues.InlineString, InlineString = new InlineString(new Text(text)) },
                _ => new Cell { CellReference = reference, CellValue = new CellValue(Convert.ToString(value, CultureInfo.InvariantCulture)!) },
            };
        }
    }
}
