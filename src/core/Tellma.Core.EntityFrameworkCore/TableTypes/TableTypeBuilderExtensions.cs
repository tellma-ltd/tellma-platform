// Copyright (c) 2026 Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tellma.Core.EntityFrameworkCore.TableTypes.Json;

namespace Tellma.Core.EntityFrameworkCore.TableTypes
{
    /// <summary>
    ///     Fluent configuration of table types (UDTTs) on entity types and properties. Fluent
    ///     configuration always takes precedence over the <see cref="TableTypeAttribute" /> /
    ///     <see cref="ExcludeFromTableTypeAttribute" /> attributes, including attributes inherited
    ///     from base classes.
    /// </summary>
    public static class TableTypeBuilderExtensions
    {
        /// <summary>
        ///     Opts the entity's table into having a paired table type (UDTT), derived as a row
        ///     image of the table and kept in sync by migrations.
        /// </summary>
        /// <param name="entityTypeBuilder">The entity type builder.</param>
        /// <param name="name">The type's name; defaults to <c>&lt;TableName&gt;List</c>.</param>
        /// <param name="schema">The type's schema; defaults to the table's own schema.</param>
        /// <returns>The same builder instance so that multiple calls can be chained.</returns>
        public static EntityTypeBuilder HasTableType(
            this EntityTypeBuilder entityTypeBuilder,
            string? name = null,
            string? schema = null)
        {
            ArgumentNullException.ThrowIfNull(entityTypeBuilder);

            entityTypeBuilder.HasAnnotation(TableTypeAnnotationNames.Enabled, true);
            if (name is not null)
            {
                entityTypeBuilder.HasAnnotation(TableTypeAnnotationNames.Name, name);
            }

            if (schema is not null)
            {
                entityTypeBuilder.HasAnnotation(TableTypeAnnotationNames.Schema, schema);
            }

            return entityTypeBuilder;
        }

        /// <inheritdoc cref="HasTableType(EntityTypeBuilder, string?, string?)" />
        /// <typeparam name="TEntity">The entity type being configured.</typeparam>
        public static EntityTypeBuilder<TEntity> HasTableType<TEntity>(
            this EntityTypeBuilder<TEntity> entityTypeBuilder,
            string? name = null,
            string? schema = null)
            where TEntity : class
        {
            return (EntityTypeBuilder<TEntity>)HasTableType((EntityTypeBuilder)entityTypeBuilder, name, schema);
        }

        /// <summary>
        ///     Opts the entity's table out of having a table type, overriding an inherited
        ///     <see cref="TableTypeAttribute" />.
        /// </summary>
        /// <param name="entityTypeBuilder">The entity type builder.</param>
        /// <returns>The same builder instance so that multiple calls can be chained.</returns>
        public static EntityTypeBuilder HasNoTableType(this EntityTypeBuilder entityTypeBuilder)
        {
            ArgumentNullException.ThrowIfNull(entityTypeBuilder);

            entityTypeBuilder.HasAnnotation(TableTypeAnnotationNames.Enabled, false);
            return entityTypeBuilder;
        }

        /// <inheritdoc cref="HasNoTableType(EntityTypeBuilder)" />
        /// <typeparam name="TEntity">The entity type being configured.</typeparam>
        public static EntityTypeBuilder<TEntity> HasNoTableType<TEntity>(this EntityTypeBuilder<TEntity> entityTypeBuilder)
            where TEntity : class
        {
            return (EntityTypeBuilder<TEntity>)HasNoTableType((EntityTypeBuilder)entityTypeBuilder);
        }

        /// <summary>
        ///     Configures the entity's table type to be created with <c>MEMORY_OPTIMIZED = ON</c>
        ///     (In-Memory OLTP). The generated SQL pre-flights support and fails with an actionable
        ///     error on unsupported tiers; there is deliberately no silent on-disk fallback.
        /// </summary>
        /// <param name="entityTypeBuilder">The entity type builder.</param>
        /// <param name="memoryOptimized">Whether the type is memory-optimized.</param>
        /// <returns>The same builder instance so that multiple calls can be chained.</returns>
        public static EntityTypeBuilder IsMemoryOptimizedTableType(
            this EntityTypeBuilder entityTypeBuilder,
            bool memoryOptimized = true)
        {
            ArgumentNullException.ThrowIfNull(entityTypeBuilder);

            entityTypeBuilder.HasAnnotation(TableTypeAnnotationNames.MemoryOptimized, memoryOptimized);
            return entityTypeBuilder;
        }

        /// <inheritdoc cref="IsMemoryOptimizedTableType(EntityTypeBuilder, bool)" />
        /// <typeparam name="TEntity">The entity type being configured.</typeparam>
        public static EntityTypeBuilder<TEntity> IsMemoryOptimizedTableType<TEntity>(
            this EntityTypeBuilder<TEntity> entityTypeBuilder,
            bool memoryOptimized = true)
            where TEntity : class
        {
            return (EntityTypeBuilder<TEntity>)IsMemoryOptimizedTableType((EntityTypeBuilder)entityTypeBuilder, memoryOptimized);
        }

        /// <summary>
        ///     Configures the database principals that receive <c>GRANT EXECUTE ON TYPE</c> after
        ///     every create/recreate of the entity's table type (grants do not survive a drop).
        /// </summary>
        /// <param name="entityTypeBuilder">The entity type builder.</param>
        /// <param name="principals">The database principals.</param>
        /// <returns>The same builder instance so that multiple calls can be chained.</returns>
        public static EntityTypeBuilder HasTableTypeGrants(
            this EntityTypeBuilder entityTypeBuilder,
            params string[] principals)
        {
            ArgumentNullException.ThrowIfNull(entityTypeBuilder);
            ArgumentNullException.ThrowIfNull(principals);

            entityTypeBuilder.HasAnnotation(TableTypeAnnotationNames.Grants, TableTypeJson.SerializeStringList(principals));
            return entityTypeBuilder;
        }

        /// <inheritdoc cref="HasTableTypeGrants(EntityTypeBuilder, string[])" />
        /// <typeparam name="TEntity">The entity type being configured.</typeparam>
        public static EntityTypeBuilder<TEntity> HasTableTypeGrants<TEntity>(
            this EntityTypeBuilder<TEntity> entityTypeBuilder,
            params string[] principals)
            where TEntity : class
        {
            return (EntityTypeBuilder<TEntity>)HasTableTypeGrants((EntityTypeBuilder)entityTypeBuilder, principals);
        }

        /// <summary>
        ///     Declares the entity's table type for runtime binding without this context owning it:
        ///     another context creates and sweeps the physical type (spec 0001 §3 → scoping). Use when
        ///     two contexts share one database and both map the same table. The type stays in the
        ///     metadata API; the differ emits no create for it and the sweep ignores it.
        /// </summary>
        /// <param name="entityTypeBuilder">The entity type builder.</param>
        /// <param name="excluded">Whether the table type is excluded from this context's migrations.</param>
        /// <returns>The same builder instance so that multiple calls can be chained.</returns>
        public static EntityTypeBuilder ExcludeTableTypeFromMigrations(
            this EntityTypeBuilder entityTypeBuilder,
            bool excluded = true)
        {
            ArgumentNullException.ThrowIfNull(entityTypeBuilder);

            entityTypeBuilder.HasAnnotation(TableTypeAnnotationNames.ExcludeFromMigrations, excluded);
            return entityTypeBuilder;
        }

        /// <inheritdoc cref="ExcludeTableTypeFromMigrations(EntityTypeBuilder, bool)" />
        /// <typeparam name="TEntity">The entity type being configured.</typeparam>
        public static EntityTypeBuilder<TEntity> ExcludeTableTypeFromMigrations<TEntity>(
            this EntityTypeBuilder<TEntity> entityTypeBuilder,
            bool excluded = true)
            where TEntity : class
        {
            return (EntityTypeBuilder<TEntity>)ExcludeTableTypeFromMigrations((EntityTypeBuilder)entityTypeBuilder, excluded);
        }

        /// <summary>
        ///     Excludes the table's rowversion/concurrency-token column from the table type. By
        ///     default it is included as a nullable <c>binary(8)</c> column (nullable because insert
        ///     rows carry no value; present so bulk UPDATEs can perform optimistic-concurrency checks).
        /// </summary>
        /// <param name="entityTypeBuilder">The entity type builder.</param>
        /// <param name="excluded">Whether the rowversion column is excluded.</param>
        /// <returns>The same builder instance so that multiple calls can be chained.</returns>
        public static EntityTypeBuilder ExcludesRowVersionFromTableType(
            this EntityTypeBuilder entityTypeBuilder,
            bool excluded = true)
        {
            ArgumentNullException.ThrowIfNull(entityTypeBuilder);

            entityTypeBuilder.HasAnnotation(TableTypeAnnotationNames.ExcludeRowVersion, excluded);
            return entityTypeBuilder;
        }

        /// <inheritdoc cref="ExcludesRowVersionFromTableType(EntityTypeBuilder, bool)" />
        /// <typeparam name="TEntity">The entity type being configured.</typeparam>
        public static EntityTypeBuilder<TEntity> ExcludesRowVersionFromTableType<TEntity>(
            this EntityTypeBuilder<TEntity> entityTypeBuilder,
            bool excluded = true)
            where TEntity : class
        {
            return (EntityTypeBuilder<TEntity>)ExcludesRowVersionFromTableType((EntityTypeBuilder)entityTypeBuilder, excluded);
        }

        /// <summary>
        ///     Excludes this property's column from the entity's table type. Primary-key columns
        ///     cannot be excluded.
        /// </summary>
        /// <param name="propertyBuilder">The property builder.</param>
        /// <param name="excluded">Whether the column is excluded.</param>
        /// <returns>The same builder instance so that multiple calls can be chained.</returns>
        public static PropertyBuilder ExcludeFromTableType(
            this PropertyBuilder propertyBuilder,
            bool excluded = true)
        {
            ArgumentNullException.ThrowIfNull(propertyBuilder);

            propertyBuilder.HasAnnotation(TableTypeAnnotationNames.Excluded, excluded);
            return propertyBuilder;
        }

        /// <inheritdoc cref="ExcludeFromTableType(PropertyBuilder, bool)" />
        /// <typeparam name="TProperty">The type of the property being configured.</typeparam>
        public static PropertyBuilder<TProperty> ExcludeFromTableType<TProperty>(
            this PropertyBuilder<TProperty> propertyBuilder,
            bool excluded = true)
        {
            return (PropertyBuilder<TProperty>)ExcludeFromTableType((PropertyBuilder)propertyBuilder, excluded);
        }

        /// <summary>
        ///     Re-includes this property's column in the entity's table type, overriding an
        ///     inherited <see cref="ExcludeFromTableTypeAttribute" />.
        /// </summary>
        /// <param name="propertyBuilder">The property builder.</param>
        /// <returns>The same builder instance so that multiple calls can be chained.</returns>
        public static PropertyBuilder IncludeInTableType(this PropertyBuilder propertyBuilder)
        {
            return ExcludeFromTableType(propertyBuilder, excluded: false);
        }

        /// <inheritdoc cref="IncludeInTableType(PropertyBuilder)" />
        /// <typeparam name="TProperty">The type of the property being configured.</typeparam>
        public static PropertyBuilder<TProperty> IncludeInTableType<TProperty>(this PropertyBuilder<TProperty> propertyBuilder)
        {
            return (PropertyBuilder<TProperty>)IncludeInTableType((PropertyBuilder)propertyBuilder);
        }
    }
}
