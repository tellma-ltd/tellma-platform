// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Excel;
using Tellma.Core.Crud;

namespace Tellma.Core.Excel
{
    /// <summary>What a background export reads, as the operations of a stack or a child query surface bind it.</summary>
    internal interface IExcelExportTarget
    {
        /// <summary>The entity, the row source of a source, and whether the target is a child query surface.</summary>
        ExportTarget ExportTarget { get; }
    }

    /// <summary>What a background import writes, as a stack's operations bind it.</summary>
    internal interface IExcelImportTarget : IExcelExportTarget
    {
        /// <summary>The stack's service, whose row source reads the lookups and which hydrates and saves the rows.</summary>
        IStackExcelRows Service { get; }

        /// <summary>The stack's limits.</summary>
        StackLimits Limits { get; }

        /// <summary>Parses a workbook into the session its chunks resolve from, under the background limits.</summary>
        /// <param name="importer">The importer.</param>
        /// <param name="workbook">The workbook's local copy.</param>
        /// <param name="request">The import's request.</param>
        /// <param name="context">The job's context.</param>
        /// <param name="cancellationToken">Stops the parse.</param>
        /// <returns>The session.</returns>
        /// <exception cref="ImportException">The workbook is refused, or holds an error.</exception>
        Task<ImportSession> ParseAsync(IExcelImporter importer, Stream workbook, ImportRequest request, ExcelContext context, CancellationToken cancellationToken);
    }

    // Spec 0019 §12.3-§12.4: a job's arguments name the resource it exports or imports, and the
    // handler reaches the resource through the operations the platform closed over its type, the
    // same scoped instance an action of the resource is invoked on.
    /// <summary>Resolves the operations of a resource a background export or import names.</summary>
    internal static class ExcelJobTargets
    {
        /// <summary>The export target of a stack or a child query surface.</summary>
        /// <param name="services">The job scope's services.</param>
        /// <param name="resource">The resource.</param>
        /// <returns>The target; null when no stack with workbooks and no exportable child surface has the resource.</returns>
        public static IExcelExportTarget? Export(IServiceProvider services, string resource)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(resource);
            StackRegistry registry = services.GetRequiredService<StackRegistry>();
            // A stack exports when it serves workbooks either way; else an exportable child surface does.
            return registry.Find(resource) is StackDescriptor stack
                ? stack.Operations.HasFlag(StackOperations.Export) || stack.Operations.HasFlag(StackOperations.Import) ? Stack(services, registry, resource) : null
                : registry.FindChildQuery(resource) is { Exportable: true } child
                    ? (IExcelExportTarget)services.GetRequiredService(typeof(ChildExportOperations<>).MakeGenericType(child.EntityType))
                    : null;
        }

        /// <summary>The import target of a stack.</summary>
        /// <param name="services">The job scope's services.</param>
        /// <param name="resource">The stack's resource.</param>
        /// <returns>The target; null when no stack that imports has the resource.</returns>
        public static IExcelImportTarget? Import(IServiceProvider services, string resource)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(resource);
            StackRegistry registry = services.GetRequiredService<StackRegistry>();
            return registry.Find(resource) is { } stack && stack.Operations.HasFlag(StackOperations.Import) ? Stack(services, registry, resource) : null;
        }

        /// <summary>The operations of the stack of a resource.</summary>
        /// <param name="services">The job scope's services.</param>
        /// <param name="registry">The stacks.</param>
        /// <param name="resource">The stack's resource.</param>
        /// <returns>The operations.</returns>
        private static IExcelImportTarget Stack(IServiceProvider services, StackRegistry registry, string resource)
        {
            Type leaf = registry.LeafOf(resource) ?? throw new InvalidOperationException($"The stack of {resource} has no leaf.");
            return (IExcelImportTarget)services.GetRequiredService(typeof(ExcelOperations<>).MakeGenericType(leaf));
        }
    }
}
