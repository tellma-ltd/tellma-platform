// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Excel;
using Tellma.Core.Abstractions.Settings;
using Tellma.Core.Blobs;
using Tellma.Core.Queryex;

namespace Tellma.Core.Excel
{
    /// <summary>
    ///     Runs the exports of a stack or a child query surface: maps the request to its source, plans
    ///     the workbook, writes it to a scratch file opened only once the first page passes the cap,
    ///     and answers the rewound file. A synchronous export runs under the request's context and the
    ///     synchronous cap, and its refusal at the cap names the background route that runs the same
    ///     request; a background export runs under its job's context and the background cap, and
    ///     reports its progress after each page. Scoped.
    /// </summary>
    internal sealed class ExcelExports
    {
        /// <summary>The background route of a display export.</summary>
        public const string DisplayRemedy = "export/start";

        /// <summary>The background route of an editable export.</summary>
        public const string EditableRemedy = "export-for-import/start";

        private readonly IExcelExporter _exporter;
        private readonly ExcelContextFactory _contexts;
        private readonly ExcelOptions _options;
        private readonly ScratchDirectory _scratch;
        private readonly QueryexEngine _engine;
        private readonly IQueryexSchemaProvider _schemas;
        private readonly IDisplayNames _displayNames;
        private readonly ExcelMetrics _metrics;
        private readonly ILogger _logger;

        /// <summary>Creates the runner.</summary>
        /// <param name="exporter">The exporter.</param>
        /// <param name="contexts">The scope's context builder.</param>
        /// <param name="options">The Excel limits.</param>
        /// <param name="scratch">The scratch directory the workbook spools to.</param>
        /// <param name="engine">The Queryex engine, which types a display column.</param>
        /// <param name="schemas">The query schemas, by content-language shape.</param>
        /// <param name="displayNames">The display-name renderer.</param>
        /// <param name="metrics">The export instruments.</param>
        /// <param name="loggers">The logger factory.</param>
        public ExcelExports(
            IExcelExporter exporter,
            ExcelContextFactory contexts,
            IOptions<ExcelOptions> options,
            ScratchDirectory scratch,
            QueryexEngine engine,
            IQueryexSchemaProvider schemas,
            IDisplayNames displayNames,
            ExcelMetrics metrics,
            ILoggerFactory loggers)
        {
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(loggers);
            _exporter = exporter ?? throw new ArgumentNullException(nameof(exporter));
            _contexts = contexts ?? throw new ArgumentNullException(nameof(contexts));
            _options = options.Value;
            _scratch = scratch ?? throw new ArgumentNullException(nameof(scratch));
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _schemas = schemas ?? throw new ArgumentNullException(nameof(schemas));
            _displayNames = displayNames ?? throw new ArgumentNullException(nameof(displayNames));
            _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
            _logger = loggers.CreateLogger(ExcelLog.Category);
        }

        /// <summary>
        ///     Exports the display shape: one sheet of the request's columns, each navigation as its
        ///     display name and each group as the content slot's value.
        /// </summary>
        /// <param name="target">What is exported.</param>
        /// <param name="request">The request.</param>
        /// <param name="cancellationToken">Stops the export between pages.</param>
        /// <returns>The workbook over its scratch file, rewound.</returns>
        /// <exception cref="BadRequestException">The request maps to no source, or its columns are out of bounds.</exception>
        /// <exception cref="InvalidQueryException">A column's expression does not compile.</exception>
        /// <exception cref="LimitExceededException">The rows exceed the synchronous cap.</exception>
        public Task<ExportOutcome> DisplayAsync(ExportTarget target, ExportRequest request, CancellationToken cancellationToken)
        {
            return DisplayAsync(target, request, background: null, cancellationToken);
        }

        /// <summary>
        ///     Exports the display shape, synchronously under the request's context or in the
        ///     background under a job's.
        /// </summary>
        /// <param name="target">What is exported.</param>
        /// <param name="request">The request.</param>
        /// <param name="background">The background run's context and progress; null for a synchronous export.</param>
        /// <param name="cancellationToken">Stops the export between pages.</param>
        /// <returns>The workbook over its scratch file, rewound.</returns>
        /// <exception cref="BadRequestException">The request maps to no source, or its columns are out of bounds.</exception>
        /// <exception cref="InvalidQueryException">A column's expression does not compile.</exception>
        /// <exception cref="LimitExceededException">The rows exceed the cap of the run.</exception>
        public async Task<ExportOutcome> DisplayAsync(ExportTarget target, ExportRequest request, ExportRun? background, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(target);
            ArgumentNullException.ThrowIfNull(request);

            // --- The request: its columns, then its one source ---
            ExportSources.CheckColumns(request);
            ExportSource source = ExportSources.Of(request, template: false, target.Child);

            // --- The plan: the columns expanded into the select terms they read ---
            ExcelContext context = background?.Context ?? _contexts.Create();
            EntityMetadata entity = target.Entity;
            var shape = (MultilingualShape)Math.Clamp(context.TenantLanguages.Count, 1, 3);
            var columns = DisplayColumns.Expand(entity, request.Columns, context, _engine, _schemas.GetSchema(shape), _displayNames);

            // A by-query export counts on its first page; ids are capped by their own limit.
            int cap = Cap(background);
            ExcelQuery query = new(
                entity.Name,
                columns.SelectPaths,
                request.OrderBy,
                Restriction: null,
                RestrictionValues: null,
                Take: cap + 1,
                CountCap: source is ExportSource.ByQuery ? cap : null,
                Dependents: null);
            ExcelWorkbookPlan plan = _exporter.PlanDisplay(entity, columns.Specs, query, context);

            IExcelRowSource rows = Reporting(new FoldingRowSource(target.Rows(source), columns), plan, source, background);
            return await WriteAsync(plan, rows, context, cap, background is null ? DisplayRemedy : null, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        ///     Exports the editable shape: the parent sheet and a sheet per selected collection at
        ///     every depth, each row the import reads back; an empty id list is the template.
        /// </summary>
        /// <param name="target">What is exported: a stack.</param>
        /// <param name="request">The request.</param>
        /// <param name="cancellationToken">Stops the export between pages.</param>
        /// <returns>The workbook over its scratch file, rewound.</returns>
        /// <exception cref="BadRequestException">The request maps to no source, or names a column, key or collection the entity lacks.</exception>
        /// <exception cref="LimitExceededException">The parent rows exceed the synchronous cap, or a child sheet's rows its own.</exception>
        public Task<ExportOutcome> EditableAsync(ExportTarget target, ExportForImportRequest request, CancellationToken cancellationToken)
        {
            return EditableAsync(target, request, background: null, cancellationToken);
        }

        /// <summary>
        ///     Exports the editable shape, synchronously under the request's context or in the
        ///     background under a job's.
        /// </summary>
        /// <param name="target">What is exported: a stack.</param>
        /// <param name="request">The request.</param>
        /// <param name="background">The background run's context and progress; null for a synchronous export.</param>
        /// <param name="cancellationToken">Stops the export between pages.</param>
        /// <returns>The workbook over its scratch file, rewound.</returns>
        /// <exception cref="BadRequestException">The request maps to no source, or names a column, key or collection the entity lacks.</exception>
        /// <exception cref="LimitExceededException">The parent rows exceed the cap of the run, or a child sheet's rows its own.</exception>
        public async Task<ExportOutcome> EditableAsync(ExportTarget target, ExportForImportRequest request, ExportRun? background, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(target);
            ArgumentNullException.ThrowIfNull(request);
            if (target.Child)
            {
                throw new InvalidOperationException("A child query surface has no editable export.");
            }

            ExportSource source = ExportSources.Of(request, template: true, child: false);
            ExcelContext context = background?.Context ?? _contexts.Create();
            int cap = Cap(background);
            ExcelWorkbookPlan plan = _exporter.PlanEditable(target.Entity, request, cap, context);
            IExcelRowSource rows = Reporting(target.Rows(source), plan, source, background);
            return await WriteAsync(plan, rows, context, cap, background is null ? EditableRemedy : null, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>The parent sheet's cap of a run: the synchronous cap in a request, the export cap in the background.</summary>
        /// <param name="background">The background run; null for a synchronous export.</param>
        /// <returns>The cap.</returns>
        private int Cap(ExportRun? background)
        {
            return background is null ? _options.MaxSynchronousExportRows : Math.Min(_options.MaxExportRows, ExcelOptions.MaxWorksheetDataRows);
        }

        /// <summary>A background run's row source, which reports after each page of the parent sheet; a synchronous export's own.</summary>
        /// <param name="rows">The export's row source.</param>
        /// <param name="plan">The plan, whose first sheet is the parent sheet.</param>
        /// <param name="source">The export's source, whose ids are the total of a by-ids export.</param>
        /// <param name="background">The background run; null for a synchronous export.</param>
        /// <returns>The row source.</returns>
        private static IExcelRowSource Reporting(IExcelRowSource rows, ExcelWorkbookPlan plan, ExportSource source, ExportRun? background)
        {
            return background is null
                ? rows
                : new ProgressRowSource(rows, plan.Sheets[0].Query, source is ExportSource.ByIds byIds ? byIds.Ids.Count : null, background.Progress);
        }

        /// <summary>
        ///     Writes a plan to a scratch file opened once the first page passes the cap, and hands the
        ///     rewound file over; on any failure the file goes with its handle.
        /// </summary>
        /// <param name="plan">The plan.</param>
        /// <param name="rows">The row source the plan's queries run through.</param>
        /// <param name="context">The request's context.</param>
        /// <param name="cap">The cap the parent sheet's query was planned with.</param>
        /// <param name="remedy">The background route that runs the same request; null for a background export, which has none.</param>
        /// <param name="cancellationToken">Stops the write between pages.</param>
        /// <returns>The workbook.</returns>
        /// <exception cref="LimitExceededException">A sheet's rows exceed its cap; a synchronous parent sheet's refusal names the remedy.</exception>
        private async Task<ExportOutcome> WriteAsync(ExcelWorkbookPlan plan, IExcelRowSource rows, ExcelContext context, int cap, string? remedy, CancellationToken cancellationToken)
        {
            long started = Stopwatch.GetTimestamp();
            FileStream? file = null;
            try
            {
                int written = await _exporter.WriteAsync(plan, rows, context, () => file = ScratchFiles.Create(_scratch.Path), cancellationToken).ConfigureAwait(false);
                FileStream workbook = file ?? throw new InvalidOperationException("The workbook writer opened no destination.");
                workbook.Position = 0;

                TimeSpan elapsed = Stopwatch.GetElapsedTime(started);
                _metrics.Exported(plan.Entity.Name, plan.Shape, background: remedy is null, written, elapsed.TotalSeconds);
                ExcelLog.ExportCompleted(_logger, plan.Entity.Name, written, (long)elapsed.TotalMilliseconds);

                // The outcome owns the file from here; the caller disposes it once it is sent.
                ExportOutcome outcome = new(workbook, plan.FileName, written);
                file = null;
                return outcome;
            }
            catch (LimitExceededException exceeded) when (exceeded.Limit == ExcelErrorCodes.ExportRowLimitExceeded)
            {
                _metrics.Rejected(plan.Entity.Name, plan.Shape);
                ExcelLog.ExportRejected(_logger, plan.Entity.Name, exceeded.Actual, exceeded.Maximum);

                // The parent sheet's synchronous cap is below the background one, so the same
                // request runs in the background; a child sheet's cap is the same on both routes.
                int background = Math.Min(_options.MaxExportRows, ExcelOptions.MaxWorksheetDataRows);
                if (remedy is not null && exceeded.Remedy is null && exceeded.Maximum == cap && cap < background)
                {
                    throw new LimitExceededException(exceeded.Limit, exceeded.Actual, exceeded.Maximum, remedy);
                }

                throw;
            }
            finally
            {
                file?.Dispose();
            }
        }
    }

    /// <summary>A background export's run: the job's context, and where it reports the rows written after each page of the parent sheet.</summary>
    /// <param name="Context">The context built from the job's culture.</param>
    /// <param name="Progress">Receives the rows written so far and the total, null when none is known.</param>
    internal sealed record ExportRun(ExcelContext Context, Action<int, int?> Progress);

    /// <summary>What an export reads: the entity, the row source of a source, and whether it is a child query surface.</summary>
    /// <param name="Entity">The exported entity: the stack's, or the child surface's.</param>
    /// <param name="Rows">The row source of one source, under the caller's read filter.</param>
    /// <param name="Child">Whether the target is a child query surface, which takes no named filters and has no editable export.</param>
    internal sealed record ExportTarget(EntityMetadata Entity, Func<ExportSource, IExcelRowSource> Rows, bool Child);
}
