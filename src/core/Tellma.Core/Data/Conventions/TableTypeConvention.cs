// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using System.ComponentModel.DataAnnotations.Schema;
using System.Reflection;
using Tellma.Core.Abstractions.Hosting;
using Tellma.Core.EntityFrameworkCore.TableTypes;
using Tellma.Core.EntityFrameworkCore.TableTypes.Json;
using CollationAttribute = Tellma.Core.Abstractions.Entities.CollationAttribute;
using ExcludeFromTableTypeAttribute = Tellma.Core.Abstractions.Entities.ExcludeFromTableTypeAttribute;
using ExtensionExcludeFromTableTypeAttribute = Tellma.Core.EntityFrameworkCore.TableTypes.ExcludeFromTableTypeAttribute;
using TableTypeAttribute = Tellma.Core.Abstractions.Entities.TableTypeAttribute;

namespace Tellma.Core.Data.Conventions
{
    /// <summary>
    ///     Maps the platform's <see cref="TableTypeAttribute" /> and
    ///     <see cref="ExcludeFromTableTypeAttribute" /> onto the table-type extension's configuration, so
    ///     the attributes live in the contract assembly without an EF dependency. Every type, entity
    ///     derived or standalone, grants <c>EXECUTE</c> to the application role.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         An entity's opt-in becomes the extension's table-type annotations on its entity type, and
    ///         each excluded property the exclusion annotation; both are inherited by a leaf, since the
    ///         attributes are. The extension derives the type itself when the model is finalized, after
    ///         every platform convention, so the shadow columns the tree and temporal conventions exclude
    ///         are already marked.
    ///     </para>
    ///     <para>
    ///         A standalone class is registered by <see cref="AddStandalone" /> while the model is
    ///         built: in schema <c>dbo</c> under its class name unless its attribute says otherwise,
    ///         with its <see cref="CollationAttribute" /> columns collated, its excluded properties left
    ///         out and its enum properties stored by name as sized <c>varchar</c> columns, exactly as an
    ///         entity's would be.
    ///     </para>
    /// </remarks>
    internal sealed class TableTypeConvention : IModelFinalizingConvention
    {
        /// <summary>The schema of a standalone type whose attribute names none.</summary>
        public const string StandaloneSchema = "dbo";

        /// <inheritdoc />
        public void ProcessModelFinalizing(IConventionModelBuilder modelBuilder, IConventionContext<IConventionModelBuilder> context)
        {
            string grants = TableTypeJson.SerializeStringList([DatabaseRoles.Application]);
            foreach (IConventionEntityType entityType in ConventionModel.Tables(modelBuilder.Metadata))
            {
                TableTypeAttribute? attribute = EntityShapes.GetAttribute<TableTypeAttribute>(entityType.ClrType);
                if (attribute is null)
                {
                    continue;
                }

                // --- The opt-in, as the extension's own annotations; a fluent opt-out still wins ---
                IConventionEntityTypeBuilder builder = entityType.Builder;
                _ = builder.HasAnnotation(TableTypeAnnotationNames.Enabled, true, fromDataAnnotation: true);
                if (attribute.Name is not null)
                {
                    _ = builder.HasAnnotation(TableTypeAnnotationNames.Name, attribute.Name, fromDataAnnotation: true);
                }

                if (attribute.Schema is not null)
                {
                    _ = builder.HasAnnotation(TableTypeAnnotationNames.Schema, attribute.Schema, fromDataAnnotation: true);
                }

                _ = builder.HasAnnotation(TableTypeAnnotationNames.Grants, grants, fromDataAnnotation: true);

                // --- Excluded properties ---
                foreach (IConventionProperty property in entityType.GetProperties())
                {
                    if (property.PropertyInfo is not null
                        && EntityShapes.GetAttribute<ExcludeFromTableTypeAttribute>(property.PropertyInfo) is not null)
                    {
                        Exclude(property);
                    }
                }
            }
        }

        /// <summary>Leaves a property's column out of its table's row-image type, unless a model configuration includes it explicitly.</summary>
        /// <param name="property">The property.</param>
        public static void Exclude(IConventionProperty property)
        {
            _ = property.Builder.HasAnnotation(TableTypeAnnotationNames.Excluded, true, fromDataAnnotation: true);
        }

        /// <summary>
        ///     Registers a standalone table-type class: <c>[&lt;Schema&gt;].[&lt;Name&gt;]</c> from its
        ///     <see cref="TableTypeAttribute" />, else <c>[dbo].[&lt;class name&gt;]</c>, granted to the
        ///     application role.
        /// </summary>
        /// <param name="modelBuilder">The model builder.</param>
        /// <param name="rowType">The row class.</param>
        public static void AddStandalone(ModelBuilder modelBuilder, Type rowType)
        {
            ArgumentNullException.ThrowIfNull(modelBuilder);
            ArgumentNullException.ThrowIfNull(rowType);

            TableTypeAttribute? attribute = EntityShapes.GetAttribute<TableTypeAttribute>(rowType);
            _ = modelBuilder.HasTableType(
                rowType,
                StandaloneName(rowType),
                StandaloneSchemaOf(rowType),
                table =>
                {
                    _ = table.HasGrants(DatabaseRoles.Application);
                    foreach (PropertyInfo property in ColumnProperties(rowType))
                    {
                        // Enums are stored by name, sized like an entity's enum column.
                        if (StandaloneEnumType(property) is Type enumType
                            && EntityShapes.GetAttribute<ColumnAttribute>(property)?.TypeName is null)
                        {
                            int length = EnumStringLengthConvention.LengthOf(enumType);
                            _ = table.HasColumnType(property.Name, $"varchar({length})", maxLength: length);
                        }

                        if (EntityShapes.GetAttribute<CollationAttribute>(property) is { } collation)
                        {
                            _ = table.UseCollation(property.Name, collation.Name);
                        }

                        if (EntityShapes.GetAttribute<ExcludeFromTableTypeAttribute>(property) is not null)
                        {
                            _ = table.ExcludeColumn(property.Name);
                        }
                    }
                });
        }

        /// <summary>The logical name of a standalone type: its attribute's, else its class name.</summary>
        /// <param name="rowType">The row class.</param>
        /// <returns>The name.</returns>
        public static string StandaloneName(Type rowType)
        {
            return EntityShapes.GetAttribute<TableTypeAttribute>(rowType)?.Name ?? rowType.Name;
        }

        /// <summary>The schema of a standalone type: its attribute's, else <c>dbo</c>.</summary>
        /// <param name="rowType">The row class.</param>
        /// <returns>The schema.</returns>
        public static string StandaloneSchemaOf(Type rowType)
        {
            return EntityShapes.GetAttribute<TableTypeAttribute>(rowType)?.Schema ?? StandaloneSchema;
        }

        /// <summary>The enum type of a standalone property of an enum or nullable enum type, or <see langword="null" />.</summary>
        /// <param name="property">The property.</param>
        /// <returns>The enum type, or <see langword="null" />.</returns>
        public static Type? StandaloneEnumType(PropertyInfo property)
        {
            Type type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            return type.IsEnum ? type : null;
        }

        /// <summary>
        ///     The properties of a standalone class that derive a column: the public read-write instance
        ///     properties the extension's class derivation reads, before the exclusions of this
        ///     convention.
        /// </summary>
        /// <param name="rowType">The row class.</param>
        /// <returns>The properties, in declaration order, base classes first.</returns>
        public static IEnumerable<PropertyInfo> ColumnProperties(Type rowType)
        {
            return EntityShapes.DeclaredProperties(rowType).Where(static property => property.CanRead
                && property.CanWrite
                && property.GetIndexParameters().Length == 0
                && EntityShapes.GetAttribute<NotMappedAttribute>(property) is null
                && EntityShapes.GetAttribute<ExtensionExcludeFromTableTypeAttribute>(property) is null);
        }
    }
}
