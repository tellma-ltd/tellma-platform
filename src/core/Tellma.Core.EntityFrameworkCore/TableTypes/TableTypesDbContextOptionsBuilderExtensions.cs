// Copyright (c) 2026 Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Tellma.Core.EntityFrameworkCore.TableTypes
{
    /// <summary>
    ///     The activation entry point of the table-types extension.
    /// </summary>
    public static class TableTypesDbContextOptionsBuilderExtensions
    {
        /// <summary>
        ///     Activates SQL Server table-type (UDTT) generation for this context:
        ///     <c>optionsBuilder.UseSqlServer(...).UseTableTypes()</c>. Tables opt in individually
        ///     via <c>HasTableType()</c> or <see cref="TableTypeAttribute" />; opted-in tables get a
        ///     derived table type created and kept in sync by the same migrations pipeline that
        ///     manages the tables.
        /// </summary>
        /// <param name="optionsBuilder">The options builder; <c>UseSqlServer(...)</c> must also be configured.</param>
        /// <returns>The same builder instance so that multiple calls can be chained.</returns>
        public static DbContextOptionsBuilder UseTableTypes(this DbContextOptionsBuilder optionsBuilder)
        {
            ArgumentNullException.ThrowIfNull(optionsBuilder);

            TableTypesOptionsExtension extension =
                optionsBuilder.Options.FindExtension<TableTypesOptionsExtension>() ?? new TableTypesOptionsExtension();
            ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(extension);
            return optionsBuilder;
        }

        /// <inheritdoc cref="UseTableTypes(DbContextOptionsBuilder)" />
        /// <typeparam name="TContext">The type of the context being configured.</typeparam>
        public static DbContextOptionsBuilder<TContext> UseTableTypes<TContext>(this DbContextOptionsBuilder<TContext> optionsBuilder)
            where TContext : DbContext
        {
            return (DbContextOptionsBuilder<TContext>)UseTableTypes((DbContextOptionsBuilder)optionsBuilder);
        }
    }
}
