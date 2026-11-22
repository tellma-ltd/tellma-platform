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
    ///     The export instruments on the platform meter: <c>tellma.excel.export.rows</c> and
    ///     <c>tellma.excel.export.duration</c> per written export, <c>tellma.excel.export.rejected</c>
    ///     per export refused at its row cap. Tagged by entity and shape, never by tenant or user.
    /// </summary>
    internal sealed class ExcelMetrics
    {
        /// <summary>The unit of a row count.</summary>
        private const string RowUnit = "{row}";

        /// <summary>The unit of a duration.</summary>
        private const string SecondUnit = "s";

        /// <summary>The unit of a refusal count.</summary>
        private const string ExportUnit = "{export}";

        private readonly Histogram<long> _rows;
        private readonly Histogram<double> _duration;
        private readonly Counter<long> _rejected;

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
    }
}
