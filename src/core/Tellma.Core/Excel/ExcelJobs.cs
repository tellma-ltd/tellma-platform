// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text.Json;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Jobs;
using Tellma.Core.Abstractions.Notifications;

namespace Tellma.Core.Excel
{
    /// <summary>
    ///     The names the background exports and imports share: their handler keys, their labels and
    ///     progress messages, the notices they raise, and the arguments of each notice.
    /// </summary>
    internal static class ExcelJobs
    {
        /// <summary>The handler key of a background export.</summary>
        public const string ExportHandlerKey = "core.export";

        /// <summary>The handler key of a background import.</summary>
        public const string ImportHandlerKey = "core.import";

        /// <summary>The label of an export's job: "Exporting {entity}".</summary>
        public const string ExportLabel = "Excel.Export.Label";

        /// <summary>The label of an import's job: "Importing {entity}".</summary>
        public const string ImportLabel = "Excel.Import.Label";

        /// <summary>An export's progress message, with the rows written so far.</summary>
        public const string ExportProgress = "Excel.Export.Progress";

        /// <summary>An import's progress message while it reads its workbook.</summary>
        public const string ImportParsing = "Excel.Import.Parsing";

        /// <summary>An import's progress message while it looks up a chunk's references and reads its stored rows.</summary>
        public const string ImportResolving = "Excel.Import.Resolving";

        /// <summary>An import's progress message while a chunk is validated.</summary>
        public const string ImportValidating = "Excel.Import.Validating";

        /// <summary>An import's progress message as a chunk commits, which its checkpoint carries.</summary>
        public const string ImportSaving = "Excel.Import.Saving";

        /// <summary>The notice that a background export's file is ready.</summary>
        public const string ExportReadyType = "core.export.ready";

        /// <summary>The notice that a background import completed.</summary>
        public const string ImportCompletedType = "core.import.completed";

        /// <summary>The notification category of both notices.</summary>
        public const string FilesCategory = "files";

        /// <summary>The label and progress argument naming the exported or imported entity.</summary>
        public const string EntityArgument = "entity";

        /// <summary>The progress argument counting the rows written.</summary>
        public const string RowsArgument = "rows";

        /// <summary>The descriptor of <see cref="ExportReadyType" />: a success, toasted, on the export's row, never muted.</summary>
        public static NotificationTypeDescriptor ExportReadyDescriptor { get; } = new(
            ExportReadyType, FilesCategory, Mutable: false, TargetResource: CoreResources.Export, Severity: NotificationSeverity.Success, Toast: true);

        /// <summary>The descriptor of <see cref="ImportCompletedType" />: a success, toasted, on the import's row, never muted.</summary>
        public static NotificationTypeDescriptor ImportCompletedDescriptor { get; } = new(
            ImportCompletedType, FilesCategory, Mutable: false, TargetResource: CoreResources.Import, Severity: NotificationSeverity.Success, Toast: true);

        /// <summary>The label of a job over a resource, which the job pages render with the entity's plural label.</summary>
        /// <param name="key">The label's key.</param>
        /// <param name="resource">The exported or imported resource.</param>
        /// <returns>The label.</returns>
        public static JobMessage Label(string key, string resource)
        {
            return new JobMessage(key, new Dictionary<string, object?>(StringComparer.Ordinal) { [EntityArgument] = resource });
        }

        /// <summary>
        ///     The stored form of an export's arguments: serialized as the case's base, so its
        ///     <c>kind</c> member is written and the handler reads the case back.
        /// </summary>
        /// <param name="arguments">The arguments.</param>
        /// <returns>The JSON the job stores.</returns>
        public static JsonElement Stored(ExportJobArguments arguments)
        {
            ArgumentNullException.ThrowIfNull(arguments);
            return JsonSerializer.SerializeToElement(arguments, TellmaJson.Options);
        }

        /// <summary>A progress percent short of the completion's 100: the share done, rounded down, at most 99.</summary>
        /// <param name="done">The part done.</param>
        /// <param name="total">The whole; null or not positive for none.</param>
        /// <returns>The percent; null when there is no whole.</returns>
        public static byte? Percent(long done, long? total)
        {
            if (total is not long whole || whole <= 0)
            {
                return null;
            }

            long percent = Math.Clamp(100 * Math.Max(done, 0) / whole, 0, JobLimits.MaxProgressPercent - 1);
            return (byte)percent;
        }
    }

    /// <summary>The arguments of the <c>core.export.ready</c> notice.</summary>
    /// <param name="FileName">The exported workbook's file name.</param>
    /// <param name="RowCount">The data rows the parent sheet holds.</param>
    /// <param name="FileId">The exported workbook's blob, which the notice downloads.</param>
    internal sealed record ExportReadyArguments(string FileName, int RowCount, int FileId);

    /// <summary>The arguments of the <c>core.import.completed</c> notice.</summary>
    /// <param name="FileName">The uploaded workbook's file name; null when the upload named none.</param>
    /// <param name="RowCount">The parent rows the workbook holds.</param>
    /// <param name="ErrorCount">The errors the import reported; zero for a completed import.</param>
    internal sealed record ImportCompletedArguments(string? FileName, int RowCount, int ErrorCount);
}
