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
        ///     <c>optionsBuilder.UseSqlServer(...).UseTableTypes(sweepScope)</c>. Tables opt in
        ///     individually via <c>HasTableType()</c> or <see cref="TableTypeAttribute" />; opted-in
        ///     tables get a derived table type created and kept in sync by the same migrations
        ///     pipeline that manages the tables.
        /// </summary>
        /// <param name="optionsBuilder">The options builder; <c>UseSqlServer(...)</c> must also be configured.</param>
        /// <param name="sweepScope">
        ///     A <b>required</b>, stable string naming which types this context owns (spec 0001 §3 →
        ///     scoping). It is stamped on every created type; the cleanup sweep touches only types
        ///     carrying it, and the create rejects a same-named type owned by a different scope. It
        ///     has no default — deriving it from the context type name would silently change ownership
        ///     on a class rename. When two contexts share one database, give each a distinct scope and
        ///     have non-owners declare shared types with <c>ExcludeFromMigrations()</c>.
        /// </param>
        /// <returns>The same builder instance so that multiple calls can be chained.</returns>
        public static DbContextOptionsBuilder UseTableTypes(this DbContextOptionsBuilder optionsBuilder, string sweepScope)
        {
            ArgumentNullException.ThrowIfNull(optionsBuilder);
            ArgumentException.ThrowIfNullOrWhiteSpace(sweepScope);

            TableTypesOptionsExtension extension = new(sweepScope);
            ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(extension);
            return optionsBuilder;
        }

        /// <inheritdoc cref="UseTableTypes(DbContextOptionsBuilder, string)" />
        /// <typeparam name="TContext">The type of the context being configured.</typeparam>
        /// <param name="optionsBuilder">The options builder; <c>UseSqlServer(...)</c> must also be configured.</param>
        /// <param name="sweepScope">The required sweep scope (see the non-generic overload).</param>
        public static DbContextOptionsBuilder<TContext> UseTableTypes<TContext>(
            this DbContextOptionsBuilder<TContext> optionsBuilder, string sweepScope)
            where TContext : DbContext
        {
            return (DbContextOptionsBuilder<TContext>)UseTableTypes((DbContextOptionsBuilder)optionsBuilder, sweepScope);
        }
    }
}
