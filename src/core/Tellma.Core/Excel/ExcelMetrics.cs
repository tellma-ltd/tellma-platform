// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Diagnostics;
using System.Diagnostics.Metrics;
using Tellma.Core.Abstractions.Excel;

namespace Tellma.Core.Excel
{
    /// <summary>
    ///     The Excel instruments on the platform meter: <c>tellma.excel.export.rows</c> and
    ///     <c>tellma.excel.export.duration</c> per written export, <c>tellma.excel.export.rejected</c>
    ///     per export refused at its row cap, tagged by entity and shape;
    ///     <c>tellma.excel.import.rows</c> and <c>tellma.excel.import.duration</c> per committed import,
    ///     tagged by entity, mode and whether a job ran it; <c>tellma.excel.import.lookups</c> per
    ///     committed import and <c>tellma.excel.import.retries</c> per chunk saved again after a stamp
    ///     conflict, tagged by entity; <c>tellma.excel.import.errors</c> per reported import error,
    ///     tagged by entity and code; and <c>tellma.excel.sst.spilled</c> per imported workbook whose
    ///     shared strings spilled, untagged. Never tagged by tenant or user.
    /// </summary>
    internal sealed class ExcelMetrics
    {
        /// <summary>The unit of a row count.</summary>
        private const string RowUnit = "{row}";

        /// <summary>The unit of a duration.</summary>
        private const string SecondUnit = "s";

        /// <summary>The unit of a refusal count.</summary>
        private const string ExportUnit = "{export}";

        /// <summary>The unit of a count of imported workbooks.</summary>
        private const string WorkbookUnit = "{workbook}";

        /// <summary>The unit of a count of lookup queries.</summary>
        private const string QueryUnit = "{query}";

        /// <summary>The unit of a count of import errors.</summary>
        private const string ErrorUnit = "{error}";

        /// <summary>The unit of a count of chunks saved again.</summary>
        private const string SaveUnit = "{save}";

        /// <summary>The rows each written export wrote to its parent sheet.</summary>
        private readonly Histogram<long> _rows;

        /// <summary>The duration of each written export.</summary>
        private readonly Histogram<double> _duration;

        /// <summary>The exports refused at their row cap.</summary>
        private readonly Counter<long> _rejected;

        /// <summary>The imported workbooks whose shared strings spilled to scratch files.</summary>
        private readonly Counter<long> _spilled;

        /// <summary>The parent rows each committed import read.</summary>
        private readonly Histogram<long> _importRows;

        /// <summary>The duration of each committed import.</summary>
        private readonly Histogram<double> _importDuration;

        /// <summary>The reference resolution queries each committed import ran.</summary>
        private readonly Histogram<long> _importLookups;

        /// <summary>The import errors reported, by code.</summary>
        private readonly Counter<long> _importErrors;

        /// <summary>The chunks saved again after rows changed since they were read.</summary>
        private readonly Counter<long> _importRetries;

        /// <summary>Creates the instruments on the platform meter.</summary>
        /// <param name="meterFactory">The host's meter factory, which owns the meter's lifetime.</param>
        public ExcelMetrics(IMeterFactory meterFactory)
        {
            ArgumentNullException.ThrowIfNull(meterFactory);

            // The meter stays a local: the factory owns and disposes it.
            Meter meter = meterFactory.Create(ExcelTelemetryNames.MeterName);
            _rows = meter.CreateHistogram<long>(ExcelTelemetryNames.ExportRows, RowUnit, "Data rows an export wrote to its parent sheet.");
            _duration = meter.CreateHistogram<double>(ExcelTelemetryNames.ExportDuration, SecondUnit, "Duration of an export, from its first read to its closed package.");
            _rejected = meter.CreateCounter<long>(ExcelTelemetryNames.ExportRejected, ExportUnit, "Exports refused because a sheet's rows exceed its cap.");
            _spilled = meter.CreateCounter<long>(ExcelTelemetryNames.SharedStringsSpilled, WorkbookUnit, "Imported workbooks whose shared strings spilled to scratch files.");
            _importRows = meter.CreateHistogram<long>(ExcelTelemetryNames.ImportRows, RowUnit, "Parent rows an import read and committed.");
            _importDuration = meter.CreateHistogram<double>(ExcelTelemetryNames.ImportDuration, SecondUnit, "Duration of an import, from the read of its upload to its commit.");
            _importLookups = meter.CreateHistogram<long>(ExcelTelemetryNames.ImportLookups, QueryUnit, "Reference resolution queries an import ran.");
            _importErrors = meter.CreateCounter<long>(ExcelTelemetryNames.ImportErrors, ErrorUnit, "Import errors reported, by code.");
            _importRetries = meter.CreateCounter<long>(ExcelTelemetryNames.ImportRetries, SaveUnit, "Import chunks saved again after rows changed since they were read.");
        }

        /// <summary>The shape tag of an export kind.</summary>
        /// <param name="shape">The kind.</param>
        /// <returns>The tag's value.</returns>
        public static string ShapeOf(ExportKind shape)
        {
            return shape switch
            {
                ExportKind.Display => ExcelTelemetryNames.DisplayShape,
                ExportKind.ForImport => ExcelTelemetryNames.ForImportShape,
                ExportKind.Report => ExcelTelemetryNames.ReportShape,
                _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, "The export kind is not one the instruments tag."),
            };
        }

        /// <summary>The mode tag of an import mode.</summary>
        /// <param name="mode">The mode.</param>
        /// <returns>The tag's value.</returns>
        public static string ModeOf(ImportMode mode)
        {
            return mode switch
            {
                ImportMode.Insert => "insert",
                ImportMode.Update => "update",
                ImportMode.Upsert => "upsert",
                _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "The import mode is not one the instruments tag."),
            };
        }

        /// <summary>Records a written export.</summary>
        /// <param name="entity">The exported entity's name.</param>
        /// <param name="shape">The export's shape.</param>
        /// <param name="background">Whether a job wrote it.</param>
        /// <param name="rows">The data rows written to the parent sheet.</param>
        /// <param name="seconds">How long it took.</param>
        public void Exported(string entity, ExportKind shape, bool background, int rows, double seconds)
        {
            TagList tags = new()
            {
                { ExcelTelemetryNames.EntityTag, entity },
                { ExcelTelemetryNames.ShapeTag, ShapeOf(shape) },
                { ExcelTelemetryNames.BackgroundTag, background },
            };
            _rows.Record(rows, tags);
            _duration.Record(seconds, tags);
        }

        /// <summary>Records an export refused at its row cap.</summary>
        /// <param name="entity">The exported entity's name.</param>
        /// <param name="shape">The export's shape.</param>
        public void Rejected(string entity, ExportKind shape)
        {
            _rejected.Add(
                1,
                new KeyValuePair<string, object?>(ExcelTelemetryNames.EntityTag, entity),
                new KeyValuePair<string, object?>(ExcelTelemetryNames.ShapeTag, ShapeOf(shape)));
        }

        /// <summary>Records a committed import.</summary>
        /// <param name="entity">The imported entity's name.</param>
        /// <param name="mode">The import's mode.</param>
        /// <param name="background">Whether a job ran it.</param>
        /// <param name="rows">The parent rows it read.</param>
        /// <param name="seconds">How long it took.</param>
        /// <param name="lookups">The reference resolution queries it ran.</param>
        public void Imported(string entity, ImportMode mode, bool background, int rows, double seconds, int lookups)
        {
            TagList tags = new()
            {
                { ExcelTelemetryNames.EntityTag, entity },
                { ExcelTelemetryNames.ModeTag, ModeOf(mode) },
                { ExcelTelemetryNames.BackgroundTag, background },
            };
            _importRows.Record(rows, tags);
            _importDuration.Record(seconds, tags);
            _importLookups.Record(lookups, new KeyValuePair<string, object?>(ExcelTelemetryNames.EntityTag, entity));
        }

        /// <summary>Records the errors an import reported, each under its code.</summary>
        /// <param name="entity">The imported entity's name.</param>
        /// <param name="errors">The errors reported.</param>
        public void ImportRefused(string entity, IEnumerable<ImportError> errors)
        {
            ArgumentNullException.ThrowIfNull(errors);
            foreach (ImportError error in errors)
            {
                _importErrors.Add(
                    1,
                    new KeyValuePair<string, object?>(ExcelTelemetryNames.EntityTag, entity),
                    new KeyValuePair<string, object?>(ExcelTelemetryNames.CodeTag, error.Code));
            }
        }

        /// <summary>Records a chunk saved again after rows changed since it read them.</summary>
        /// <param name="entity">The imported entity's name.</param>
        public void ImportRetried(string entity)
        {
            _importRetries.Add(1, new KeyValuePair<string, object?>(ExcelTelemetryNames.EntityTag, entity));
        }

        /// <summary>Records an imported workbook whose shared strings spilled to scratch files.</summary>
        public void SharedStringsSpilled()
        {
            _spilled.Add(1);
        }
    }
}
