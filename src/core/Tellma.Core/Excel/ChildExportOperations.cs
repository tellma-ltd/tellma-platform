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

namespace Tellma.Core.Excel
{
    /// <summary>
    ///     The display export of a child query surface, closed over the child entity: the actions of
    ///     every exportable surface, under the child's read securable, whose decision composes the
    ///     owner's. The rows are the surface's own, read through its row source.
    /// </summary>
    /// <typeparam name="TChild">The child entity.</typeparam>
    public sealed class ChildExportOperations<TChild>
        where TChild : class
    {
        /// <summary>The scope's export runner.</summary>
        private readonly ExcelExports _exports;

        /// <summary>What the export reads: the child entity and the surface's row source.</summary>
        private readonly ExportTarget _target;

        /// <summary>Binds the operations to the child's query surface.</summary>
        /// <param name="exports">The scope's export runner.</param>
        /// <param name="children">The scope's child query service.</param>
        /// <param name="resource">The surface's resource, the child entity's name.</param>
        /// <param name="entity">The child entity's metadata.</param>
        internal ChildExportOperations(ExcelExports exports, IChildQueryService children, string resource, EntityMetadata entity)
        {
            ArgumentNullException.ThrowIfNull(children);
            ArgumentException.ThrowIfNullOrWhiteSpace(resource);
            ArgumentNullException.ThrowIfNull(entity);
            _exports = exports ?? throw new ArgumentNullException(nameof(exports));
            _target = new ExportTarget(entity, source => children.ExcelRowSource(resource, source), Child: true);
        }

        /// <summary>
        ///     Exports the rows the child list shows as a workbook of its columns: each navigation as
        ///     its display name, each multilingual name in the reader's language.
        /// </summary>
        /// <param name="request">The rows, by ids or by the list's clauses (no named filters), and the columns.</param>
        /// <param name="cancellationToken">Stops the export between pages.</param>
        /// <returns>The workbook, which the caller disposes once it is sent.</returns>
        /// <exception cref="BadRequestException">The request names its rows both ways, by no id, or by named filters, or its columns are out of bounds.</exception>
        /// <exception cref="InvalidQueryException">A column's expression, a filter or the ordering does not compile.</exception>
        /// <exception cref="LimitExceededException">The rows exceed the synchronous cap; the background route runs the same request.</exception>
        [ApiAction("export", Action = AccessActions.Read, Mutation = false, Idempotent = true, Long = true)]
        [RatePolicy(TellmaRatePolicies.Export)]
        public Task<ExportOutcome> ExportAsync(ExportRequest request, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            return _exports.DisplayAsync(_target, request, cancellationToken);
        }

        /// <summary>Creates the operations of the scope's child query surface over <typeparamref name="TChild" />.</summary>
        /// <param name="services">The scope's services.</param>
        /// <returns>The operations.</returns>
        /// <exception cref="InvalidOperationException">No child query surface is over <typeparamref name="TChild" />.</exception>
        internal static ChildExportOperations<TChild> Create(IServiceProvider services)
        {
            ArgumentNullException.ThrowIfNull(services);
            ChildQueryDescriptor surface = services.GetRequiredService<IStackRegistry>().ChildQueries.FirstOrDefault(static child => child.EntityType == typeof(TChild))
                ?? throw new InvalidOperationException($"No child query surface is over {typeof(TChild).Name}, so it has no export.");
            EntityMetadata entity = services.GetRequiredService<IEntityMetadataProvider>().Get(typeof(TChild));
            return new ChildExportOperations<TChild>(services.GetRequiredService<ExcelExports>(), services.GetRequiredService<IChildQueryService>(), surface.Resource, entity);
        }
    }
}
