// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Runtime.CompilerServices;
using Tellma.Core.Abstractions.Blobs;
using Tellma.Core.Abstractions.Calendars;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Excel;
using Tellma.Core.Abstractions.Localization;
using Tellma.Core.Excel;
using Tellma.Testing.Entities;

namespace Tellma.Core.Tests.Excel
{
    /// <summary>
    ///     What the import suites share: editable workbooks written by the real planner and writer
    ///     over generated rows, and the importer, its inspection and its parse, over a fixture
    ///     composition with the options and limits a test chooses.
    /// </summary>
    /// <param name="services">The fixture composition's services.</param>
    internal sealed class ImportKit(IServiceProvider services) : IDisposable
    {
        /// <summary>The fixture model's metadata.</summary>
        public IEntityMetadataProvider Metadata { get; } = services.GetRequiredService<IEntityMetadataProvider>();

        /// <summary>The scratch directory workbooks spool and spill to, one per kit.</summary>
        public string Scratch { get; } = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "tellma-import-tests", Guid.NewGuid().ToString("N"))).FullName;

        /// <summary>An entity's metadata.</summary>
        /// <typeparam name="TEntity">The entity.</typeparam>
        /// <returns>The metadata.</returns>
        public EntityMetadata Entity<TEntity>()
        {
            return Metadata.Get(typeof(TEntity));
        }

        /// <summary>Writes an editable export of generated rows, as the export-for-import operation would.</summary>
        /// <typeparam name="TEntity">The exported entity.</typeparam>
        /// <param name="context">The exporting request's context.</param>
        /// <param name="rows">The rows of every sheet, under each owner row once.</param>
        /// <param name="request">The request; every column and collection when null.</param>
        /// <returns>The workbook's bytes.</returns>
        public async Task<byte[]> ExportAsync<TEntity>(ExcelContext context, int rows = 2, ExportForImportRequest? request = null)
        {
            ExportPlanner planner = new(services.GetRequiredService<IValueSetRegistry>(), "1.2.3");
            ExcelWorkbookPlan plan = planner.PlanEditable(Entity<TEntity>(), request ?? new ExportForImportRequest(), 100, context);
            using MemoryStream destination = new();
            _ = await new WorkbookWriter(new ExcelOptions(), Scratch).WriteAsync(plan, new GeneratedRows(plan.Sheets[0].Query, rows), context, () => destination, TestContext.Current.CancellationToken);
            return destination.ToArray();
        }

        /// <summary>Inspects a workbook against an entity.</summary>
        /// <typeparam name="TEntity">The stack's entity.</typeparam>
        /// <param name="workbook">The workbook's bytes.</param>
        /// <param name="context">The importing request's context.</param>
        /// <param name="request">The overrides; none when null.</param>
        /// <param name="options">The codec's options; the defaults when null.</param>
        /// <param name="limits">The stack's limits; the defaults when null.</param>
        /// <param name="loggers">The importer's loggers; none when null.</param>
        /// <returns>The plan.</returns>
        public Task<ImportPlan> InspectAsync<TEntity>(byte[] workbook, ExcelContext context, InspectImportRequest? request = null, ExcelOptions? options = null, StackLimits? limits = null, ILoggerFactory? loggers = null)
        {
            return InspectAsync(Entity<TEntity>(), workbook, context, request, options, limits, loggers);
        }

        /// <summary>Inspects a workbook against an entity.</summary>
        /// <param name="entity">The stack's entity.</param>
        /// <param name="workbook">The workbook's bytes.</param>
        /// <param name="context">The importing request's context.</param>
        /// <param name="request">The overrides; none when null.</param>
        /// <param name="options">The codec's options; the defaults when null.</param>
        /// <param name="limits">The stack's limits; the defaults when null.</param>
        /// <param name="loggers">The importer's loggers; none when null.</param>
        /// <returns>The plan.</returns>
        public async Task<ImportPlan> InspectAsync(EntityMetadata entity, byte[] workbook, ExcelContext context, InspectImportRequest? request = null, ExcelOptions? options = null, StackLimits? limits = null, ILoggerFactory? loggers = null)
        {
            request ??= new InspectImportRequest { FileId = 1 };
            ImportRequests.Check(entity, request.FileId, request.SheetMappings, request.ColumnMappings);
            using MemoryStream stream = new(workbook, writable: false);
            return await Importer(options, loggers).InspectAsync(stream, entity, limits ?? StackLimits.Default, request, context, TestContext.Current.CancellationToken);
        }

        /// <summary>Parses a workbook for an import into an entity.</summary>
        /// <typeparam name="TEntity">The stack's entity.</typeparam>
        /// <param name="workbook">The workbook's bytes.</param>
        /// <param name="context">The importing request's context.</param>
        /// <param name="request">The mode, the row key and the overrides; an insert when null.</param>
        /// <param name="options">The codec's options; the defaults when null.</param>
        /// <param name="limits">The stack's limits; the defaults when null.</param>
        /// <param name="synchronous">Whether the synchronous limits apply.</param>
        /// <returns>The session.</returns>
        public async Task<ExcelImportSession<TEntity>> ParseAsync<TEntity>(byte[] workbook, ExcelContext context, ImportRequest? request = null, ExcelOptions? options = null, StackLimits? limits = null, bool synchronous = true)
            where TEntity : class
        {
            request ??= new ImportRequest { FileId = 1 };
            EntityMetadata entity = Entity<TEntity>();
            ImportRequests.Check(entity, request);
            using MemoryStream stream = new(workbook, writable: false);
            return await Importer(options).ParseAsync<TEntity>(stream, entity, limits ?? StackLimits.Default, request, context, synchronous, TestContext.Current.CancellationToken);
        }

        /// <summary>An importer over the composition's blob kinds and metrics, with its own header cache.</summary>
        /// <param name="options">The codec's options; the defaults when null.</param>
        /// <param name="loggers">The importer's loggers; none when null.</param>
        /// <returns>The importer.</returns>
        public ExcelImporter Importer(ExcelOptions? options = null, ILoggerFactory? loggers = null)
        {
            return new ExcelImporter(
                options ?? new ExcelOptions(),
                Scratch,
                services.GetRequiredService<IBlobKindRegistry>(),
                new HeaderIndexCache(),
                services.GetRequiredService<ExcelMetrics>(),
                loggers ?? NullLoggerFactory.Instance,
                services.GetRequiredService<IValueSetRegistry>(),
                services.GetRequiredService<ICalendarRegistry>());
        }

        /// <summary>A context for a tenant: the exporting scope's, with another tenant, deployment, languages or culture.</summary>
        /// <param name="scope">The scope whose labels, catalogue and calendar the context keeps.</param>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="deployment">The deployment.</param>
        /// <param name="languages">The tenant's languages; the scope's when null.</param>
        /// <returns>The context.</returns>
        public static ExcelContext Tenant(ExportScope scope, int tenantId = 7, string deployment = "deployment-a", params string[]? languages)
        {
            return scope.Context with
            {
                TenantId = tenantId,
                DeploymentId = deployment,
                TenantLanguages = languages is { Length: > 0 } ? languages : scope.Context.TenantLanguages,
            };
        }

        /// <summary>The plan's sheet of a name.</summary>
        /// <param name="plan">The plan.</param>
        /// <param name="name">The sheet's name.</param>
        /// <returns>The sheet.</returns>
        public static ImportPlanSheet Sheet(ImportPlan plan, string name)
        {
            return Assert.Single(plan.Sheets, sheet => sheet.Sheet == name);
        }

        /// <summary>A plan sheet's column of a header.</summary>
        /// <param name="sheet">The sheet.</param>
        /// <param name="header">The header.</param>
        /// <returns>The column.</returns>
        public static ImportPlanColumn Column(ImportPlanSheet sheet, string header)
        {
            return Assert.Single(sheet.Columns, column => column.Header == header);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            try
            {
                Directory.Delete(Scratch, recursive: true);
            }
            catch (IOException)
            {
                // A file still open on a slow host is left for the system's temp cleanup.
            }
        }

        /// <summary>
        ///     A row source that makes one page of every query: the rows numbered from 1, each value
        ///     made from its select path, and under each page the same number of rows of each dependent.
        /// </summary>
        /// <param name="query">The parent sheet's query.</param>
        /// <param name="rows">The rows of each sheet.</param>
        private sealed class GeneratedRows(ExcelQuery query, int rows) : IExcelRowSource
        {
            /// <inheritdoc />
            public async IAsyncEnumerable<ExcelPage> Pages(ExcelQuery requested, [EnumeratorCancellation] CancellationToken cancellationToken = default)
            {
                Assert.Same(query, requested);
                await Task.Yield();
                yield return Page(requested);
            }

            /// <inheritdoc />
            public Task<IReadOnlyList<IReadOnlyList<IReadOnlyList<object?>>>> ReadAsync(IReadOnlyList<ExcelQuery> queries, CancellationToken cancellationToken = default)
            {
                throw new NotSupportedException();
            }

            /// <summary>One page of a query and its dependents.</summary>
            /// <param name="sheet">The query.</param>
            /// <returns>The page.</returns>
            private ExcelPage Page(ExcelQuery sheet)
            {
                return new ExcelPage(
                    [.. Enumerable.Range(1, rows).Select(row => (IReadOnlyList<object?>)[.. sheet.SelectPaths.Select(path => Value(path, row))])],
                    [.. (sheet.Dependents ?? []).Select(Page)],
                    Count: null);
            }

            /// <summary>A generated value of a select path.</summary>
            /// <param name="path">The path.</param>
            /// <param name="row">The row's number.</param>
            /// <returns>The value.</returns>
            private static object? Value(string path, int row)
            {
                return path switch
                {
                    "ModifiedAt" => ExcelTestKit.Now,
                    "Kind" => WidgetKind.Plain,
                    "OptionalKind" => null,
                    "Price" => 1.25m * row,
                    "DueOn" => new DateOnly(2026, 1, row),
                    "Ordinal" => row,
                    "StartsAt" or "EndsAt" => null,
                    _ when path == "Id" || path.EndsWith("Id", StringComparison.Ordinal) => row,
                    _ => $"{path}-{row}",
                };
            }
        }
    }
}
