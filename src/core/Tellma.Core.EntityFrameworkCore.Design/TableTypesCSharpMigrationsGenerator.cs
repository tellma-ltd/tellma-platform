// Copyright (c) 2026 Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore.Migrations.Design;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
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

            List<MigrationOperation> materialized = [.. operations];
            IEnumerable<string> namespaces = base.GetNamespaces(materialized);
            return materialized.Any(o => o is CreateTableTypeOperation or DropTableTypeOperation)
                ? namespaces.Concat(TableTypeNamespaces)
                : namespaces;
        }
    }
}
