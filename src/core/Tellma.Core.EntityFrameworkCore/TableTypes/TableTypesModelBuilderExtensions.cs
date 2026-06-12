// Copyright (c) 2026 Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Tellma.Core.EntityFrameworkCore.TableTypes.Json;

namespace Tellma.Core.EntityFrameworkCore.TableTypes
{
    /// <summary>
    ///     Model-level fluent configuration of the table-types extension.
    /// </summary>
    public static class TableTypesModelBuilderExtensions
    {
        /// <summary>
        ///     Declares a standalone table type (spec 0001 §5) — paired with no table — for
        ///     operation-specific shapes such as bulk state updates or bulk assignments. It flows
        ///     through the same migrations pipeline and metadata API as table-derived types.
        /// </summary>
        /// <param name="modelBuilder">The model builder.</param>
        /// <param name="name">The table type's name.</param>
        /// <param name="schema">The table type's schema, or <see langword="null" /> for the database default.</param>
        /// <param name="buildAction">Configures the type's columns, key, grants and options.</param>
        /// <returns>The same builder instance so that multiple calls can be chained.</returns>
        public static ModelBuilder HasTableType(
            this ModelBuilder modelBuilder,
            string name,
            string? schema,
            Action<TableTypeBuilder> buildAction)
        {
            ArgumentNullException.ThrowIfNull(modelBuilder);
            ArgumentException.ThrowIfNullOrEmpty(name);
            ArgumentNullException.ThrowIfNull(buildAction);

            TableTypeBuilder builder = new();
            buildAction(builder);
            return AddStandalone(modelBuilder, builder.Build(name, schema));
        }

        /// <summary>
        ///     Declares a standalone table type derived from the plain CLR class
        ///     <typeparamref name="T" /> (NOT an entity type), which then doubles as the natural DTO
        ///     for the rows bound into the TVP at runtime. Columns derive from the class's public
        ///     read-write properties in declaration order, honoring <c>[Key]</c> (incl. composite),
        ///     <c>[MaxLength]</c>/<c>[StringLength]</c>, <c>[Unicode]</c>, <c>[Precision]</c>,
        ///     <c>[Column(TypeName = ...)]</c>, <c>[Required]</c>, <c>[NotMapped]</c>,
        ///     <c>[ExcludeFromTableType]</c>, and nullable reference types. <see cref="TableTypeAttribute" />
        ///     on the class overrides the name (default: the class name, verbatim) and schema.
        /// </summary>
        /// <typeparam name="T">The class describing the type's shape.</typeparam>
        /// <param name="modelBuilder">The model builder.</param>
        /// <param name="name">Overrides the type's name (default: <c>[TableType]</c>, else the class name).</param>
        /// <param name="schema">Overrides the type's schema (default: <c>[TableType]</c>, else the database default).</param>
        /// <param name="buildAction">Optionally adds grants, memory-optimization, a key override, or extra columns.</param>
        /// <returns>The same builder instance so that multiple calls can be chained.</returns>
        public static ModelBuilder HasTableType<T>(
            this ModelBuilder modelBuilder,
            string? name = null,
            string? schema = null,
            Action<TableTypeBuilder>? buildAction = null)
            where T : class
        {
            return HasTableType(modelBuilder, typeof(T), name, schema, buildAction);
        }

        /// <inheritdoc cref="HasTableType{T}(ModelBuilder, string?, string?, Action{TableTypeBuilder}?)" />
        /// <param name="modelBuilder">The model builder.</param>
        /// <param name="clrType">The class describing the type's shape.</param>
        /// <param name="name">Overrides the type's name (default: <c>[TableType]</c>, else the class name).</param>
        /// <param name="schema">Overrides the type's schema (default: <c>[TableType]</c>, else the database default).</param>
        /// <param name="buildAction">Optionally adds grants, memory-optimization, a key override, or extra columns.</param>
        public static ModelBuilder HasTableType(
            this ModelBuilder modelBuilder,
            Type clrType,
            string? name = null,
            string? schema = null,
            Action<TableTypeBuilder>? buildAction = null)
        {
            ArgumentNullException.ThrowIfNull(modelBuilder);
            ArgumentNullException.ThrowIfNull(clrType);

            TableTypeBuilder builder = new();
            AddClassColumns(builder, clrType);
            buildAction?.Invoke(builder);

            // Explicit arguments win over the class's [TableType] attribute, which wins over the
            // class-name/default-schema fallbacks — letting attribute-free classes (e.g. shapes
            // declared in EF-free assemblies such as Tellma.Core.Abstractions) pin a schema at
            // registration.
            TableTypeAttribute? attribute = clrType.GetCustomAttribute<TableTypeAttribute>(inherit: true);
            return AddStandalone(modelBuilder, builder.Build(
                name ?? attribute?.Name ?? clrType.Name,
                schema ?? attribute?.Schema));
        }

        /// <summary>
        ///     Declares a complete, already-resolved table-type definition — the vocabulary model
        ///     snapshots are written in. The snapshot generator renders every derived definition as
        ///     one of these calls, and replaying it rebuilds the definition annotation byte-for-byte
        ///     so the differ's verbatim comparison works. Application code normally never calls
        ///     this; use <c>HasTableType()</c> on the entity (table-derived) or
        ///     <see cref="HasTableType(ModelBuilder, string, string?, Action{TableTypeBuilder})" />
        ///     (standalone) instead.
        /// </summary>
        /// <param name="modelBuilder">The model builder.</param>
        /// <param name="name">The table type's name.</param>
        /// <param name="schema">The table type's schema, or <see langword="null" /> for the database default.</param>
        /// <param name="buildAction">Supplies the resolved columns, key, grants and options.</param>
        /// <returns>The same builder instance so that multiple calls can be chained.</returns>
        public static ModelBuilder HasTableTypeDefinition(
            this ModelBuilder modelBuilder,
            string name,
            string? schema,
            Action<TableTypeDefinitionBuilder> buildAction)
        {
            ArgumentNullException.ThrowIfNull(modelBuilder);
            ArgumentException.ThrowIfNullOrEmpty(name);
            ArgumentNullException.ThrowIfNull(buildAction);

            TableTypeDefinitionBuilder builder = new();
            buildAction(builder);
            TableTypeDefinition definition = builder.Build(name, schema);
            string key = TableTypeAnnotationNames.DefinitionPrefix + (schema ?? string.Empty) + "." + name;
            modelBuilder.HasAnnotation(key, TableTypeJson.Serialize(definition));
            return modelBuilder;
        }

        /// <summary>Writes the standalone configuration as a model annotation for the finalizing convention.</summary>
        private static ModelBuilder AddStandalone(ModelBuilder modelBuilder, StandaloneTableTypeConfiguration configuration)
        {
            string key = TableTypeAnnotationNames.StandalonePrefix
                + (configuration.Schema ?? string.Empty) + "." + configuration.Name;
            modelBuilder.HasAnnotation(key, TableTypeJson.Serialize(configuration));
            return modelBuilder;
        }

        /// <summary>
        ///     Derives columns (in declaration order, base classes first) and the key from a plain
        ///     CLR class's public read-write instance properties and their annotations.
        /// </summary>
        private static void AddClassColumns(TableTypeBuilder builder, Type clrType)
        {
            NullabilityInfoContext nullabilityContext = new();

            // Base-most first, so inherited members read naturally before the class's own.
            List<Type> chain = [];
            for (Type? type = clrType; type is not null && type != typeof(object); type = type.BaseType)
            {
                chain.Add(type);
            }

            chain.Reverse();

            foreach (Type type in chain)
            {
                foreach (PropertyInfo property in type.GetTypeInfo().DeclaredProperties)
                {
                    if (!property.CanRead
                        || !property.CanWrite
                        || property.GetMethod!.IsStatic
                        || property.GetIndexParameters().Length > 0
                        || property.GetCustomAttribute<NotMappedAttribute>(inherit: true) is not null
                        || property.GetCustomAttribute<ExcludeFromTableTypeAttribute>(inherit: true) is not null)
                    {
                        continue;
                    }

                    Type propertyType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;

                    // Nullability: Nullable<T>, or an NRT-nullable reference type; [Required] forces non-null.
                    bool nullable = Nullable.GetUnderlyingType(property.PropertyType) is not null
                        || (!property.PropertyType.IsValueType
                            && nullabilityContext.Create(property).WriteState != NullabilityState.NotNull);
                    if (property.GetCustomAttribute<RequiredAttribute>(inherit: true) is not null)
                    {
                        nullable = false;
                    }

                    int? maxLength = property.GetCustomAttribute<MaxLengthAttribute>(inherit: true)?.Length
                        ?? property.GetCustomAttribute<StringLengthAttribute>(inherit: true)?.MaximumLength;
                    PrecisionAttribute? precision = property.GetCustomAttribute<PrecisionAttribute>(inherit: true);

                    builder.AddColumn(new StandaloneColumnConfiguration
                    {
                        Name = property.Name,
                        ClrTypeName = propertyType.AssemblyQualifiedName,
                        StoreType = property.GetCustomAttribute<ColumnAttribute>(inherit: true)?.TypeName,
                        IsNullable = nullable,
                        MaxLength = maxLength,
                        Precision = precision?.Precision,
                        Scale = precision?.Scale,
                        IsUnicode = property.GetCustomAttribute<UnicodeAttribute>(inherit: true)?.IsUnicode,
                    });

                    if (property.GetCustomAttribute<KeyAttribute>(inherit: true) is not null)
                    {
                        builder.AddDerivedKeyColumn(property.Name);
                    }
                }
            }
        }
    }
}
