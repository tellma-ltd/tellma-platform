// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Excel;
using Tellma.Core.Crud;

namespace Tellma.Core.Excel
{
    /// <summary>
    ///     The workbook operations of a stack, attached by the platform to every stack that exports
    ///     or imports, closed over the stack's leaf. Each method is an API action under the stack's
    ///     resource, whose securable the invoker evaluates before the method runs; the read
    ///     decision's filter is applied by the stack's row source.
    /// </summary>
    /// <remarks>
    ///     A stack projects the display export when it serves <see cref="StackOperations.Export" />
    ///     and the editable export when it serves <see cref="StackOperations.Import" />.
    /// </remarks>
    /// <typeparam name="TEntity">The stack's leaf.</typeparam>
    public sealed class ExcelOperations<TEntity>
        where TEntity : class
    {
        /// <summary>The scope's export runner.</summary>
        private readonly ExcelExports _exports;

        /// <summary>What the exports read: the stack's entity and its service's row source.</summary>
        private readonly ExportTarget _target;

        /// <summary>
        ///     The scope's import runner, resolved when an inspection first needs it: a composition
        ///     without a blob store still exports.
        /// </summary>
        private readonly Func<ExcelImports> _imports;

        /// <summary>The stack's limits, whose save sizes bound a synchronous and an atomic import.</summary>
        private readonly StackLimits _limits;

        /// <summary>Binds the operations to the stack's service.</summary>
        /// <param name="exports">The scope's export runner.</param>
        /// <param name="imports">Resolves the scope's import runner, called only when an inspection runs.</param>
        /// <param name="service">The stack's service, as the Excel operations reach it.</param>
        /// <param name="entity">The stack's entity metadata.</param>
        /// <param name="limits">The stack's limits.</param>
        internal ExcelOperations(ExcelExports exports, Func<ExcelImports> imports, IStackExcelRows service, EntityMetadata entity, StackLimits limits)
        {
            ArgumentNullException.ThrowIfNull(service);
            ArgumentNullException.ThrowIfNull(entity);
            _exports = exports ?? throw new ArgumentNullException(nameof(exports));
            _imports = imports ?? throw new ArgumentNullException(nameof(imports));
            _limits = limits ?? throw new ArgumentNullException(nameof(limits));
            _target = new ExportTarget(entity, service.ExcelRowSource, Child: false);
        }

        /// <summary>
        ///     Exports the rows the grid shows as a workbook of the grid's columns: each navigation
        ///     as its display name, each multilingual name in the reader's language.
        /// </summary>
        /// <param name="request">The rows, by ids or by the grid's clauses, and the columns.</param>
        /// <param name="cancellationToken">Stops the export between pages.</param>
        /// <returns>The workbook, which the caller disposes once it is sent.</returns>
        /// <exception cref="BadRequestException">The request names its rows both ways or by no id, or its columns are out of bounds.</exception>
        /// <exception cref="InvalidQueryException">A column's expression, a filter or the ordering does not compile.</exception>
        /// <exception cref="LimitExceededException">The rows exceed the synchronous cap; the background route runs the same request.</exception>
        [ApiAction("export", Action = AccessActions.Read, Mutation = false, Idempotent = true, Long = true)]
        [RatePolicy(TellmaRatePolicies.Export)]
        public Task<ExportOutcome> ExportAsync(ExportRequest request, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            return _exports.DisplayAsync(_target, request, cancellationToken);
        }

        /// <summary>
        ///     Exports rows as a workbook the import reads back: identities, stamps, editable columns
        ///     and references in their keys, with a sheet per selected collection at every depth. An
        ///     empty id list exports the template: the headers, the validation lists and the manifest.
        /// </summary>
        /// <param name="request">The rows, by ids or by the grid's clauses, and the columns, keys and collections.</param>
        /// <param name="cancellationToken">Stops the export between pages.</param>
        /// <returns>The workbook, which the caller disposes once it is sent.</returns>
        /// <exception cref="BadRequestException">The request names its rows both ways, or a column, key or collection the entity lacks.</exception>
        /// <exception cref="InvalidQueryException">A filter or the ordering does not compile.</exception>
        /// <exception cref="LimitExceededException">The parent rows exceed the synchronous cap, or a child sheet's rows its own.</exception>
        [ApiAction("export-for-import", Action = AccessActions.Read, Mutation = false, Idempotent = true, Long = true)]
        [RatePolicy(TellmaRatePolicies.Export)]
        public Task<ExportOutcome> ExportForImportAsync(ExportForImportRequest request, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            return _exports.EditableAsync(_target, request, cancellationToken);
        }

        /// <summary>
        ///     Reads the caller's uploaded workbook and reports how an import would map it: each
        ///     sheet's binding and data rows, each header's path and status, the row keys it offers,
        ///     and whether it must run in the background. Nothing is written, and the upload stays
        ///     staged for the import that follows.
        /// </summary>
        /// <param name="request">The upload, and the sheet and column overrides to apply.</param>
        /// <param name="cancellationToken">Cancels the read.</param>
        /// <returns>The plan.</returns>
        /// <exception cref="BadRequestException">The file id or an override is malformed, or an override names a collection the entity lacks.</exception>
        /// <exception cref="NotFoundException">The file is not the caller's own staged upload of an import file.</exception>
        /// <exception cref="ImportException">
        ///     The workbook is above a size limit or unreadable, its manifest names another entity, or
        ///     an override names a sheet, column or path the workbook or entity lacks.
        /// </exception>
        [ApiAction("inspect-import", Action = AccessActions.Save, Mutation = false, Idempotent = true, Long = true)]
        [RatePolicy(TellmaRatePolicies.Import)]
        public Task<ImportPlan> InspectImportAsync(InspectImportRequest request, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            return _imports().InspectAsync(_target.Entity, _limits, request, cancellationToken);
        }

        /// <summary>Creates the operations of the scope's stack over <typeparamref name="TEntity" />.</summary>
        /// <param name="services">The scope's services.</param>
        /// <returns>The operations.</returns>
        /// <exception cref="InvalidOperationException">No stack has <typeparamref name="TEntity" /> as its leaf.</exception>
        internal static ExcelOperations<TEntity> Create(IServiceProvider services)
        {
            ArgumentNullException.ThrowIfNull(services);
            StackDescriptor stack = services.GetRequiredService<StackRegistry>().FindByLeaf(typeof(TEntity))
                ?? throw new InvalidOperationException($"No stack has {typeof(TEntity).Name} as its leaf, so its Excel operations have no service.");
            var service = (IStackExcelRows)services.GetRequiredService(stack.ServiceType);
            EntityMetadata entity = services.GetRequiredService<IEntityMetadataProvider>().Get(typeof(TEntity));

            // The import runner needs the host's blob store, so it is resolved only when an inspection runs.
            return new ExcelOperations<TEntity>(services.GetRequiredService<ExcelExports>(), services.GetRequiredService<ExcelImports>, service, entity, stack.Limits);
        }
    }
}
