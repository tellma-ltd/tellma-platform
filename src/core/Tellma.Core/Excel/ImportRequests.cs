// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Excel;

namespace Tellma.Core.Excel
{
    // Spec 0019 §8.1: the request faults judged against the entity's metadata alone are refused before
    // the blob is read, so a malformed override costs no round trip and never reaches the workbook.
    /// <summary>The checks of an import request's file and overrides, made before its workbook is read.</summary>
    internal static class ImportRequests
    {
        /// <summary>Refuses a request whose file id or overrides are malformed, or name a collection the entity lacks.</summary>
        /// <param name="entity">The stack's entity.</param>
        /// <param name="fileId">The staged upload's id.</param>
        /// <param name="sheetMappings">The sheet overrides.</param>
        /// <param name="columnMappings">The column overrides.</param>
        /// <exception cref="BadRequestException">A fault of the request.</exception>
        public static void Check(
            EntityMetadata entity,
            int fileId,
            IReadOnlyList<ImportSheetMapping>? sheetMappings,
            IReadOnlyList<ImportColumnMapping>? columnMappings)
        {
            ArgumentNullException.ThrowIfNull(entity);

            // --- The file: a staged upload's id ---
            if (fileId <= 0)
            {
                throw Fault("The file id must be the id of an uploaded file.", "fileId");
            }

            // --- The sheet overrides: each sheet once, each target once, each a collection path of the entity ---
            if (sheetMappings is null)
            {
                throw Fault("The sheet mappings must be a list, empty for none.", "sheetMappings");
            }

            HashSet<string> collections = new(ImportPaths.Collections(entity).Select(static collection => collection.Path), StringComparer.Ordinal);
            HashSet<string> sheets = new(StringComparer.OrdinalIgnoreCase);
            HashSet<string> targets = new(StringComparer.Ordinal);
            for (int index = 0; index < sheetMappings.Count; index++)
            {
                string at = string.Create(CultureInfo.InvariantCulture, $"sheetMappings[{index}]");
                ImportSheetMapping mapping = sheetMappings[index] ?? throw Fault($"The sheet mapping at {at} is missing.", at);
                if (string.IsNullOrWhiteSpace(mapping.Sheet))
                {
                    throw Fault($"The sheet mapping at {at} names no sheet.", $"{at}.sheet");
                }

                if (!sheets.Add(mapping.Sheet))
                {
                    throw Fault($"The sheet '{mapping.Sheet}' at {at} is mapped more than once.", $"{at}.sheet", mapping.Sheet);
                }

                if (mapping.Collection is not string collection)
                {
                    continue;
                }

                if (collection.Length > 0 && !collections.Contains(collection))
                {
                    throw Fault($"The collection '{collection}' at {at} is not a collection path of {entity.Name}.", $"{at}.collection", collection);
                }

                if (!targets.Add(collection))
                {
                    string target = collection.Length == 0 ? "the parent sheet" : $"the collection '{collection}'";
                    throw Fault($"The sheet mapping at {at} maps a second sheet to {target}.", $"{at}.collection", collection);
                }
            }

            // --- The column overrides: each a column letter, each (sheet, column) once ---
            if (columnMappings is null)
            {
                throw Fault("The column mappings must be a list, empty for none.", "columnMappings");
            }

            HashSet<(string Sheet, int Column)> columns = new(SheetColumnComparer.Instance);
            for (int index = 0; index < columnMappings.Count; index++)
            {
                string at = string.Create(CultureInfo.InvariantCulture, $"columnMappings[{index}]");
                ImportColumnMapping mapping = columnMappings[index] ?? throw Fault($"The column mapping at {at} is missing.", at);
                if (string.IsNullOrWhiteSpace(mapping.Sheet))
                {
                    throw Fault($"The column mapping at {at} names no sheet.", $"{at}.sheet");
                }

                if (mapping.Column is null || !ColumnLetters.TryParse(mapping.Column, out int column))
                {
                    throw Fault($"The column '{mapping.Column}' at {at} is not a column letter from A to XFD.", $"{at}.column", mapping.Column);
                }

                if (!columns.Add((mapping.Sheet, column)))
                {
                    throw Fault($"The column {mapping.Column} of the sheet '{mapping.Sheet}' at {at} is mapped more than once.", $"{at}.column", mapping.Column);
                }
            }
        }

        /// <summary>A request fault, with the path at fault and the name it carried.</summary>
        /// <param name="detail">The English sentence.</param>
        /// <param name="path">The path in the request.</param>
        /// <param name="name">The name at fault; null when there is none.</param>
        /// <returns>The exception.</returns>
        private static BadRequestException Fault(string detail, string path, string? name = null)
        {
            Dictionary<string, object?> arguments = new(StringComparer.Ordinal) { ["path"] = path };
            if (name is not null)
            {
                arguments["name"] = name;
            }

            return new BadRequestException(detail, arguments);
        }

        /// <summary>Compares a sheet and a column: the sheet ignoring case, as sheet names are.</summary>
        internal sealed class SheetColumnComparer : IEqualityComparer<(string Sheet, int Column)>
        {
            /// <summary>The comparer.</summary>
            public static readonly SheetColumnComparer Instance = new();

            /// <inheritdoc />
            public bool Equals((string Sheet, int Column) x, (string Sheet, int Column) y)
            {
                return x.Column == y.Column && string.Equals(x.Sheet, y.Sheet, StringComparison.OrdinalIgnoreCase);
            }

            /// <inheritdoc />
            public int GetHashCode((string Sheet, int Column) obj)
            {
                return HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Sheet), obj.Column);
            }
        }
    }
}
