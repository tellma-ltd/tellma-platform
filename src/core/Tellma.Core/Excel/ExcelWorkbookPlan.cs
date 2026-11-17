// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.ObjectModel;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Excel;
using Tellma.Core.Abstractions.Internal;

namespace Tellma.Core.Excel
{
    /// <summary>
    ///     An entity export worked out before its first row is read: the file name, the data sheets in
    ///     order and the manifest's header block. The writer streams the workbook from it.
    /// </summary>
    /// <param name="Entity">The exported entity.</param>
    /// <param name="Shape">Which export: the display or the editable shape. A report is written as a table instead.</param>
    /// <param name="FileName">The download name, <c>.xlsx</c> included.</param>
    /// <param name="Sheets">
    ///     The data sheets in workbook order: the parent sheet first, then each child sheet after the
    ///     sheet of its owner.
    /// </param>
    /// <param name="Manifest">The manifest's header block, key to value, in the order it is written.</param>
    /// <exception cref="ArgumentNullException"><paramref name="Entity" /> or <paramref name="Manifest" /> is null.</exception>
    /// <exception cref="ArgumentException">
    ///     <paramref name="Shape" /> is the report shape, <paramref name="FileName" /> is blank,
    ///     <paramref name="Sheets" /> is empty, holds a null entry or does not start with the parent
    ///     sheet alone, or <paramref name="Manifest" /> holds a blank key or a null value.
    /// </exception>
    public sealed record ExcelWorkbookPlan(
        EntityMetadata Entity,
        ExportKind Shape,
        string FileName,
        IReadOnlyList<ExcelSheetPlan> Sheets,
        IReadOnlyDictionary<string, string> Manifest)
    {
        /// <inheritdoc cref="ExcelWorkbookPlan(EntityMetadata, ExportKind, string, IReadOnlyList{ExcelSheetPlan}, IReadOnlyDictionary{string, string})" />
        public EntityMetadata Entity { get; init => field = value ?? throw new ArgumentNullException(nameof(Entity)); } =
            Entity ?? throw new ArgumentNullException(nameof(Entity));

        /// <inheritdoc cref="ExcelWorkbookPlan(EntityMetadata, ExportKind, string, IReadOnlyList{ExcelSheetPlan}, IReadOnlyDictionary{string, string})" />
        public ExportKind Shape { get; init => field = EntityShape(value, nameof(Shape)); } = EntityShape(Shape, nameof(Shape));

        /// <inheritdoc cref="ExcelWorkbookPlan(EntityMetadata, ExportKind, string, IReadOnlyList{ExcelSheetPlan}, IReadOnlyDictionary{string, string})" />
        public string FileName { get; init => field = NotBlank(value); } = NotBlank(FileName);

        /// <inheritdoc cref="ExcelWorkbookPlan(EntityMetadata, ExportKind, string, IReadOnlyList{ExcelSheetPlan}, IReadOnlyDictionary{string, string})" />
        public IReadOnlyList<ExcelSheetPlan> Sheets { get; init => field = ValidSheets(value, nameof(Sheets)); } = ValidSheets(Sheets, nameof(Sheets));

        /// <inheritdoc cref="ExcelWorkbookPlan(EntityMetadata, ExportKind, string, IReadOnlyList{ExcelSheetPlan}, IReadOnlyDictionary{string, string})" />
        public IReadOnlyDictionary<string, string> Manifest { get; init => field = ValidManifest(value, nameof(Manifest)); } = ValidManifest(Manifest, nameof(Manifest));

        /// <summary>Refuses the report shape, which no entity plan writes.</summary>
        /// <param name="value">The shape.</param>
        /// <param name="name">The member it came from.</param>
        /// <returns>The shape.</returns>
        private static ExportKind EntityShape(ExportKind value, string name)
        {
            return value is ExportKind.Display or ExportKind.ForImport
                ? value
                : throw new ArgumentException("An entity workbook is a display or an editable export.", name);
        }

        /// <summary>Refuses a blank file name.</summary>
        /// <param name="value">The name.</param>
        /// <returns>The name.</returns>
        private static string NotBlank(string value)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value, nameof(FileName));
            return value;
        }

        /// <summary>Copies the sheets, refusing a list that does not start with the parent sheet alone.</summary>
        /// <param name="value">The sheets.</param>
        /// <param name="name">The member it came from.</param>
        /// <returns>A read-only copy.</returns>
        private static ReadOnlyCollection<ExcelSheetPlan> ValidSheets(IReadOnlyList<ExcelSheetPlan> value, string name)
        {
            ReadOnlyCollection<ExcelSheetPlan> copy = ReadOnlyCopies.OfEntries(value, requireOne: true, name);
            return copy[0].Collection is null && copy.Skip(1).All(static sheet => sheet.Collection is not null)
                ? copy
                : throw new ArgumentException("The first sheet, and only it, is the parent sheet.", name);
        }

        /// <summary>Copies the header block in its order, refusing a blank key or a null value.</summary>
        /// <param name="value">The block.</param>
        /// <param name="name">The member it came from.</param>
        /// <returns>A read-only copy in the same order.</returns>
        private static ReadOnlyDictionary<string, string> ValidManifest(IReadOnlyDictionary<string, string> value, string name)
        {
            ArgumentNullException.ThrowIfNull(value, name);
            OrderedDictionary<string, string> copy = new(value.Count, StringComparer.Ordinal);
            foreach ((string key, string entry) in value)
            {
                if (string.IsNullOrWhiteSpace(key) || entry is null)
                {
                    throw new ArgumentException("A manifest entry has a key and a value.", name);
                }

                copy.Add(key, entry);
            }

            return new ReadOnlyDictionary<string, string>(copy);
        }
    }
}
