// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Excel
{
    /// <summary>
    ///     The names of the Excel export's and import's instruments and their tags. Dashboards and
    ///     alerts key on these strings, so they never change.
    /// </summary>
    /// <remarks>
    ///     Every tag takes a bounded set of values: <see cref="EntityTag" /> an entity name,
    ///     <see cref="ShapeTag" /> one of <see cref="DisplayShape" />, <see cref="ForImportShape" /> and
    ///     <see cref="ReportShape" />, <see cref="ModeTag" /> an import mode's name,
    ///     <see cref="BackgroundTag" /> a boolean and <see cref="CodeTag" /> an error code. The tenant
    ///     and the user are never tags; they go to the log scope.
    /// </remarks>
    public static class ExcelTelemetryNames
    {
        /// <summary>The meter every Core instrument is created on.</summary>
        public const string MeterName = "Tellma.Core";

        /// <summary>Histogram (<c>{row}</c>): the rows one export wrote. Tagged <see cref="EntityTag" />, <see cref="ShapeTag" /> and <see cref="BackgroundTag" />.</summary>
        public const string ExportRows = "tellma.excel.export.rows";

        /// <summary>Histogram (<c>s</c>): how long one export ran. Tagged <see cref="EntityTag" />, <see cref="ShapeTag" /> and <see cref="BackgroundTag" />.</summary>
        public const string ExportDuration = "tellma.excel.export.duration";

        /// <summary>Counter (<c>{export}</c>): exports refused for exceeding a row cap. Tagged <see cref="EntityTag" /> and <see cref="ShapeTag" />.</summary>
        public const string ExportRejected = "tellma.excel.export.rejected";

        /// <summary>Histogram (<c>{row}</c>): the rows one import read. Tagged <see cref="EntityTag" />, <see cref="ModeTag" /> and <see cref="BackgroundTag" />.</summary>
        public const string ImportRows = "tellma.excel.import.rows";

        /// <summary>Histogram (<c>s</c>): how long one import ran. Tagged <see cref="EntityTag" />, <see cref="ModeTag" /> and <see cref="BackgroundTag" />.</summary>
        public const string ImportDuration = "tellma.excel.import.duration";

        /// <summary>Histogram (<c>{query}</c>): the reference resolution queries one import ran. Tagged <see cref="EntityTag" />.</summary>
        public const string ImportLookups = "tellma.excel.import.lookups";

        /// <summary>Counter (<c>{error}</c>): import errors, by code. Tagged <see cref="EntityTag" /> and <see cref="CodeTag" />.</summary>
        public const string ImportErrors = "tellma.excel.import.errors";

        /// <summary>Histogram (<c>{chunk}</c>): the chunks one background import committed. Tagged <see cref="EntityTag" />.</summary>
        public const string ImportChunks = "tellma.excel.import.chunks";

        /// <summary>Counter (<c>{import}</c>): background imports resumed from a checkpoint. Tagged <see cref="EntityTag" />.</summary>
        public const string ImportResumed = "tellma.excel.import.resumed";

        /// <summary>Counter (<c>{save}</c>): chunks re-saved after rows changed since they were read. Tagged <see cref="EntityTag" />.</summary>
        public const string ImportRetries = "tellma.excel.import.retries";

        /// <summary>Counter (<c>{import}</c>): imports whose shared strings spilled from memory to a scratch file. Untagged.</summary>
        public const string SharedStringsSpilled = "tellma.excel.sst.spilled";

        /// <summary>The entity tag: the exported or imported entity's name.</summary>
        public const string EntityTag = "entity";

        /// <summary>The shape tag: which export wrote the file.</summary>
        public const string ShapeTag = "shape";

        /// <summary>The mode tag: the import's mode.</summary>
        public const string ModeTag = "mode";

        /// <summary>The background tag: whether a job ran the work.</summary>
        public const string BackgroundTag = "background";

        /// <summary>The code tag: an import error's code.</summary>
        public const string CodeTag = "code";

        /// <summary>The <see cref="ShapeTag" /> value of a display export.</summary>
        public const string DisplayShape = "display";

        /// <summary>The <see cref="ShapeTag" /> value of an editable export.</summary>
        public const string ForImportShape = "for-import";

        /// <summary>The <see cref="ShapeTag" /> value of a report export.</summary>
        public const string ReportShape = "report";
    }
}
