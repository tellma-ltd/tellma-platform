// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Design;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Tellma.Core.EntityFrameworkCore.TableTypes;
using Tellma.Core.EntityFrameworkCore.TableTypes.Operations;

namespace Tellma.Core.EntityFrameworkCore.Design
{
    /// <summary>
    ///     A <see cref="CSharpMigrationsGenerator" /> that adds the Tellma table-type namespaces to
    ///     a migration file's <c>using</c> directives whenever the migration contains table-type
    ///     operations. EF's namespace collection only covers column CLR types, data values and
    ///     annotations — never the namespaces of custom operations or the extension methods that
    ///     scaffold them — so without this the scaffolded
    ///     <c>migrationBuilder.CreateTableType(...)</c> calls would not compile.
    /// </summary>
    /// <param name="dependencies">The base generator dependencies; pass through to EF's generator.</param>
    /// <param name="csharpDependencies">The C#-specific dependencies; pass through to EF's generator.</param>
    public class TableTypesCSharpMigrationsGenerator(
        MigrationsCodeGeneratorDependencies dependencies,
        CSharpMigrationsGeneratorDependencies csharpDependencies)
        : CSharpMigrationsGenerator(dependencies, csharpDependencies)
    {
        /// <summary>
        ///     The namespaces required by scaffolded table-type operations:
        ///     <c>TableTypeColumnDefinition</c> and the <c>CreateTableType</c>/<c>DropTableType</c>
        ///     extension methods.
        /// </summary>
        private static readonly string[] TableTypeNamespaces =
        [
            "Tellma.Core.EntityFrameworkCore.TableTypes",
            "Tellma.Core.EntityFrameworkCore.TableTypes.Operations",
        ];

        /// <inheritdoc />
        protected override IEnumerable<string> GetNamespaces(IEnumerable<MigrationOperation> operations)
        {
            ArgumentNullException.ThrowIfNull(operations);

            // Materialize once: base.GetNamespaces enumerates the sequence and we enumerate it again
            // with Any(); a lazy/one-shot operations enumerable would otherwise be re-evaluated or
            // already consumed on the second pass.
            List<MigrationOperation> materialized = [.. operations];
            IEnumerable<string> namespaces = base.GetNamespaces(materialized);
            return materialized.Any(o => o is CreateTableTypeOperation or DropTableTypeOperation)
                ? namespaces.Concat(TableTypeNamespaces)
                : namespaces;
        }

        /// <summary>
        ///     Adds the namespace of <c>HasTableTypeDefinition(...)</c> and its builder to snapshot
        ///     (and migration-metadata) files when the model carries table-type definitions, which
        ///     the snapshot generator renders as fluent calls.
        /// </summary>
        /// <param name="model">The model the snapshot is generated for.</param>
        /// <returns>The namespaces the generated code requires.</returns>
        protected override IEnumerable<string> GetNamespaces(IModel model)
        {
            ArgumentNullException.ThrowIfNull(model);

            IEnumerable<string> namespaces = base.GetNamespaces(model);
            return model.GetAnnotations().Any(a => a.Name.StartsWith(TableTypeAnnotationNames.DefinitionPrefix, StringComparison.Ordinal))
                ? namespaces.Concat([TableTypeNamespaces[0]])
                : namespaces;
        }
    }
}
