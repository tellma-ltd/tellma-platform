// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Logging;

namespace Tellma.Core.Excel
{
    /// <summary>Source-generated log messages of the Excel exports and imports, in the <c>Tellma.Core.Excel</c> category.</summary>
    /// <remarks>
    ///     The events carry the entity, row counts and durations, never a cell's value or a file's
    ///     name; the tenant and the user ride the log scope of the request or job.
    /// </remarks>
    internal static partial class ExcelLog
    {
        /// <summary>The logger category of the Excel codec's events.</summary>
        public const string Category = "Tellma.Core.Excel";

        /// <summary>An export wrote its workbook.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="entity">The exported entity's name.</param>
        /// <param name="rows">The data rows written to the parent sheet.</param>
        /// <param name="durationMs">How long the export took, in milliseconds.</param>
        [LoggerMessage(
            EventName = "ExportCompleted",
            Level = LogLevel.Information,
            Message = "An export of {Entity} wrote {Rows} rows in {DurationMs} ms.")]
        public static partial void ExportCompleted(ILogger logger, string entity, int rows, long durationMs);

        /// <summary>An export was refused because a sheet's rows exceed its cap.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="entity">The exported entity's name.</param>
        /// <param name="rows">The rows counted, or the row the cap stopped at.</param>
        /// <param name="cap">The cap.</param>
        [LoggerMessage(
            EventName = "ExportRejected",
            Level = LogLevel.Information,
            Message = "An export of {Entity} was refused: {Rows} rows against a cap of {Cap}.")]
        public static partial void ExportRejected(ILogger logger, string entity, int rows, int cap);

        /// <summary>An import committed its rows.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="entity">The imported entity's name.</param>
        /// <param name="mode">The import's mode.</param>
        /// <param name="inserted">The parent rows inserted.</param>
        /// <param name="updated">The parent rows updated.</param>
        /// <param name="durationMs">How long the import took, in milliseconds.</param>
        [LoggerMessage(
            EventName = "ImportCompleted",
            Level = LogLevel.Information,
            Message = "An import of {Entity} in the {Mode} mode inserted {Inserted} and updated {Updated} rows in {DurationMs} ms.")]
        public static partial void ImportCompleted(ILogger logger, string entity, string mode, int inserted, int updated, long durationMs);

        /// <summary>An import was refused with errors, and nothing it read was committed.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="entity">The imported entity's name.</param>
        /// <param name="mode">The import's mode.</param>
        /// <param name="totalErrors">The errors found, before the report's cap.</param>
        /// <param name="firstCode">The code of the first error reported.</param>
        [LoggerMessage(
            EventName = "ImportFailed",
            Level = LogLevel.Warning,
            Message = "An import of {Entity} in the {Mode} mode was refused with {TotalErrors} errors, the first {FirstCode}.")]
        public static partial void ImportFailed(ILogger logger, string entity, string mode, int totalErrors, string firstCode);

        /// <summary>A chunk of an import is saved again after rows it read changed before its save.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="importId">The background import's id; null for a synchronous import.</param>
        /// <param name="firstRow">The Excel row of the chunk's first parent row.</param>
        /// <param name="attempt">The retry's number, from 1.</param>
        /// <param name="conflicts">The rows that changed.</param>
        [LoggerMessage(
            EventName = "ImportChunkRetried",
            Level = LogLevel.Information,
            Message = "The import {ImportId} saves its chunk from row {FirstRow} again, attempt {Attempt}, after {Conflicts} rows changed since they were read.")]
        public static partial void ImportChunkRetried(ILogger logger, int? importId, int firstRow, int attempt, int conflicts);

        /// <summary>A chunk of a background import committed with its checkpoint.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="importId">The import's id.</param>
        /// <param name="nextRow">The position the next chunk starts at.</param>
        /// <param name="rows">The parent rows the chunk committed.</param>
        [LoggerMessage(
            EventName = "ImportChunkCommitted",
            Level = LogLevel.Information,
            Message = "The import {ImportId} committed a chunk of {Rows} rows; the next chunk starts at position {NextRow}.")]
        public static partial void ImportChunkCommitted(ILogger logger, int importId, int nextRow, int rows);

        /// <summary>A background import resumed from its committed checkpoint.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="importId">The import's id.</param>
        /// <param name="nextRow">The position it resumed at.</param>
        [LoggerMessage(
            EventName = "ImportResumed",
            Level = LogLevel.Information,
            Message = "The import {ImportId} resumed at position {NextRow} from its committed checkpoint.")]
        public static partial void ImportResumed(ILogger logger, int importId, int nextRow);

        /// <summary>An import's shared strings outgrew their memory budget and spilled to scratch files.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="bytes">The bytes the shared strings were charged, text and overhead together.</param>
        [LoggerMessage(
            EventName = "SharedStringsSpilled",
            Level = LogLevel.Information,
            Message = "An import's shared strings were charged {Bytes} bytes and spilled to scratch files.")]
        public static partial void SharedStringsSpilled(ILogger logger, long bytes);

        /// <summary>
        ///     A property the server owns is marked as excluded from Excel, which it already is: the
        ///     editable shape never carries it, and the display shape carries it only when selected.
        /// </summary>
        /// <param name="logger">The logger.</param>
        /// <param name="entity">The entity's name.</param>
        /// <param name="property">The property's name.</param>
        /// <param name="ownership">The property's ownership.</param>
        [LoggerMessage(
            EventName = "RedundantExcelExclusion",
            Level = LogLevel.Warning,
            Message = "The property {Entity}.{Property} is {Ownership}, so it is already left out of the editable export and its exclusion from Excel is redundant.")]
        public static partial void RedundantExclusion(ILogger logger, string entity, string property, string ownership);
    }
}
