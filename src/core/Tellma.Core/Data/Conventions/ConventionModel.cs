// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tellma.Core.Composition;

namespace Tellma.Core.Data.Conventions
{
    /// <summary>
    ///     What the conventions share: which entity types they apply to, how a declared target type
    ///     resolves to the leaf in the model, and how a convention adds the foreign keys and indexes it
    ///     names.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Every configuration is applied as if declared by an attribute: an explicit model
    ///         configuration (Core's own) wins over it, and it wins over the provider's own conventions.
    ///         Foreign keys are the exception: they are added explicitly (see
    ///         <see cref="AddForeignKey" />), and one a model configuration declared is left alone.
    ///     </para>
    ///     <para>
    ///         A declaration the model cannot be built with (a reference to an entity absent from the
    ///         model, an index over a property the class lacks) throws an
    ///         <see cref="InvalidOperationException" /> that names the class and the declaration.
    ///         Everything the model can be built with but the platform refuses is left to the
    ///         metadata validation at startup, which reports every problem at once.
    ///     </para>
    /// </remarks>
    internal static class ConventionModel
    {
        /// <summary>
        ///     The entity types the conventions shape: each mapped to a table of its own, neither owned
        ///     nor shared, and not derived from another entity type of the model (the platform maps
        ///     leaves only).
        /// </summary>
        /// <param name="model">The model.</param>
        /// <returns>The entity types, ordered by name so the conventions run deterministically.</returns>
        public static List<IConventionEntityType> Tables(IConventionModel model)
        {
            return [.. model.GetEntityTypes()
                .Where(static entityType => entityType.BaseType is null
                    && !entityType.IsOwned()
                    && !entityType.HasSharedClrType
                    && entityType.GetTableName() is not null)
                .OrderBy(static entityType => entityType.Name, StringComparer.Ordinal)];
        }

        /// <summary>The table an entity type maps to.</summary>
        /// <param name="entityType">The entity type.</param>
        /// <returns>The table name.</returns>
        public static string Table(IReadOnlyEntityType entityType)
        {
            return entityType.GetTableName()!;
        }

        /// <summary>The column a property maps to in its entity type's table.</summary>
        /// <param name="property">The property.</param>
        /// <returns>The column name.</returns>
        public static string Column(IReadOnlyProperty property)
        {
            return property.GetColumnName();
        }

        /// <summary>
        ///     The entity type of the leaf registered for <paramref name="target" />: the one mapped type
        ///     whose class is <paramref name="target" /> or derives from it.
        /// </summary>
        /// <param name="model">The model.</param>
        /// <param name="target">The declared target: a pack's default or any entity.</param>
        /// <param name="declaration">The declaration that names the target, for the message.</param>
        /// <returns>The leaf's entity type.</returns>
        /// <exception cref="InvalidOperationException">No mapped type, or more than one, is the target or derives from it.</exception>
        public static IConventionEntityType Leaf(IConventionModel model, Type target, string declaration)
        {
            List<IConventionEntityType> candidates = [.. model.GetEntityTypes()
                .Where(entityType => entityType.BaseType is null
                    && !entityType.IsOwned()
                    && !entityType.HasSharedClrType
                    && target.IsAssignableFrom(entityType.ClrType))];

            return candidates.Count switch
            {
                1 => candidates[0],
                0 => throw new InvalidOperationException(
                    $"{declaration} names the entity {TypeDisplay.Qualified(target)}, which is not in the model. Contribute it with contribution.Entity<{TypeDisplay.Short(target)}>() in the feature that owns it."),
                _ => throw new InvalidOperationException(
                    $"{declaration} names the entity {TypeDisplay.Qualified(target)}, but the model maps {string.Join(" and ", candidates.Select(static candidate => TypeDisplay.Qualified(candidate.ClrType)))} for it. Register one leaf for it with UseEntity<{TypeDisplay.Short(target)}, TLeaf>()."),
            };
        }

        /// <summary>The entity type mapped to a platform table, such as <c>core.Users</c>.</summary>
        /// <param name="model">The model.</param>
        /// <param name="table">The platform table.</param>
        /// <param name="declaration">What needs the table, for the message.</param>
        /// <returns>The entity type.</returns>
        /// <exception cref="InvalidOperationException">No entity type maps the table.</exception>
        public static IConventionEntityType FindCoreTable(IConventionModel model, CoreTable table, string declaration)
        {
            return model.GetEntityTypes().FirstOrDefault(entityType => entityType.BaseType is null
                    && entityType.GetTableName() == table.Name
                    && entityType.GetSchema() == table.Schema)
                ?? throw new InvalidOperationException(
                    $"{declaration} references the platform table [{table.Schema}].[{table.Name}], which the model does not map. Compose the feature that contributes it.");
        }

        /// <summary>
        ///     Adds a many-to-one or one-to-one foreign key from <paramref name="property" /> to the key
        ///     of <paramref name="principal" />, with no navigation, named
        ///     <c>FK_&lt;Table&gt;_&lt;Column&gt;</c>.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         The key is added as an explicit configuration. The provider's model cleanup removes
        ///         every navigation-less foreign key a convention or an attribute declared, and the
        ///         platform maps no navigations at all: its query navigations derive from the column
        ///         names instead.
        ///     </para>
        ///     <para>
        ///         A foreign key a model configuration already declared over the same column to the same
        ///         principal is left as that configuration made it.
        ///     </para>
        /// </remarks>
        /// <param name="dependent">The dependent entity type.</param>
        /// <param name="property">The foreign-key property.</param>
        /// <param name="principal">The principal entity type.</param>
        /// <param name="onDelete">What deleting the principal does.</param>
        /// <param name="unique">Whether at most one dependent row references each principal row.</param>
        /// <param name="declaration">The declaration that asks for the key, for the message.</param>
        /// <returns>The foreign key.</returns>
        /// <exception cref="InvalidOperationException">The principal has no primary key.</exception>
        public static IConventionForeignKey AddForeignKey(
            IConventionEntityType dependent,
            IConventionProperty property,
            IConventionEntityType principal,
            DeleteBehavior onDelete,
            bool unique,
            string declaration)
        {
            IConventionKey key = principal.FindPrimaryKey()
                ?? throw new InvalidOperationException(
                    $"{declaration} references {principal.DisplayName()}, which has no primary key.");

            if (dependent.FindForeignKey(property, key, principal) is IConventionForeignKey existing
                && existing.GetConfigurationSource() == ConfigurationSource.Explicit)
            {
                return existing;
            }

            var mutable = (IMutableEntityType)dependent;
            IMutableForeignKey foreignKey = mutable.FindForeignKey((IMutableProperty)property, (IMutableKey)key, (IMutableEntityType)principal)
                ?? mutable.AddForeignKey((IMutableProperty)property, (IMutableKey)key, (IMutableEntityType)principal);

            foreignKey.IsUnique = unique;
            foreignKey.DeleteBehavior = onDelete;
            foreignKey.SetConstraintName(ConstraintNames.ForeignKey(Table(dependent), Column(property)));
            return (IConventionForeignKey)foreignKey;
        }

        /// <summary>
        ///     Adds or reconfigures the index over <paramref name="properties" />, in order, under the
        ///     given name. An index a declaration already added over the same columns becomes this one.
        /// </summary>
        /// <param name="entityType">The entity type.</param>
        /// <param name="properties">The indexed properties, in key order.</param>
        /// <param name="name">The index's database name.</param>
        /// <param name="unique">Whether the index is unique.</param>
        /// <param name="filter">The index's filter, or <see langword="null" /> for none.</param>
        /// <returns>The index's builder.</returns>
        /// <exception cref="InvalidOperationException">An explicit configuration refuses the index.</exception>
        public static IConventionIndexBuilder AddIndex(
            IConventionEntityType entityType,
            IReadOnlyList<IConventionProperty> properties,
            string name,
            bool unique,
            string? filter)
        {
            IConventionIndexBuilder builder = entityType.Builder.HasIndex(properties, fromDataAnnotation: true)
                ?? throw new InvalidOperationException(
                    $"The index {name} on {entityType.DisplayName()} conflicts with a model configuration of the entity.");

            _ = builder.IsUnique(unique, fromDataAnnotation: true);
            _ = builder.HasDatabaseName(name, fromDataAnnotation: true);
            _ = builder.HasFilter(filter, fromDataAnnotation: true);
            return builder;
        }

        /// <summary>Gives a column a named default constraint, <c>DF_&lt;Table&gt;_&lt;Column&gt;</c>.</summary>
        /// <param name="entityType">The entity type.</param>
        /// <param name="propertyName">The property.</param>
        /// <param name="value">The default value.</param>
        public static void SetDefault(IConventionEntityType entityType, string propertyName, object value)
        {
            IConventionProperty property = RequireProperty(entityType, propertyName, "the default of");
            string name = ConstraintNames.Default(Table(entityType), Column(property));
            _ = property.Builder.HasDefaultValue(value, name, fromDataAnnotation: true);
        }

        /// <summary>The filter that leaves the null values of a column out of an index: <c>[Column] IS NOT NULL</c>.</summary>
        /// <param name="column">The column.</param>
        /// <returns>The filter.</returns>
        public static string NotNullFilter(string column)
        {
            return $"{Delimit(column)} IS NOT NULL";
        }

        /// <summary>A SQL Server identifier delimited in brackets, any <c>]</c> in it doubled.</summary>
        /// <param name="identifier">The identifier.</param>
        /// <returns>The delimited identifier.</returns>
        public static string Delimit(string identifier)
        {
            return $"[{identifier.Replace("]", "]]", StringComparison.Ordinal)}]";
        }

        /// <summary>
        ///     A property a platform base or capability interface declares, which must be mapped: a
        ///     model configuration's <c>Ignore</c> or a <c>[NotMapped]</c> member hiding it fails the
        ///     model with the entity and the column named.
        /// </summary>
        /// <param name="entityType">The entity type.</param>
        /// <param name="propertyName">The property.</param>
        /// <param name="purpose">What needs the column, for the message: "the audit column", "the key of".</param>
        /// <returns>The property.</returns>
        /// <exception cref="InvalidOperationException">The property is not mapped.</exception>
        public static IConventionProperty RequireProperty(IConventionEntityType entityType, string propertyName, string purpose)
        {
            return entityType.FindProperty(propertyName)
                ?? throw new InvalidOperationException(
                    $"The entity {TypeDisplay.Qualified(entityType.ClrType)} does not map {propertyName}, which {purpose} its table needs. A column a platform base or capability interface declares cannot be ignored or hidden; remove the Ignore() or [NotMapped].");
        }

        /// <summary>A description of a declaration on a property, for messages: <c>[References] on Widget.OwnerId</c>.</summary>
        /// <param name="attribute">The attribute's name, without the suffix.</param>
        /// <param name="entityType">The declaring entity type.</param>
        /// <param name="member">The member.</param>
        /// <returns>The description.</returns>
        public static string Declaration(string attribute, IReadOnlyEntityType entityType, string member)
        {
            return $"[{attribute}] on {TypeDisplay.Qualified(entityType.ClrType)}.{member}";
        }

        /// <summary>A description of a declaration on a class, for messages: <c>[Sibling] on WidgetExtra</c>.</summary>
        /// <param name="attribute">The attribute's name, without the suffix.</param>
        /// <param name="entityType">The declaring entity type.</param>
        /// <returns>The description.</returns>
        public static string Declaration(string attribute, IReadOnlyEntityType entityType)
        {
            return $"[{attribute}] on {TypeDisplay.Qualified(entityType.ClrType)}";
        }
    }

    /// <summary>A platform table, by schema and name.</summary>
    /// <param name="Schema">The schema.</param>
    /// <param name="Name">The table name.</param>
    internal sealed record CoreTable(string Schema, string Name)
    {
        /// <summary><c>core.Users</c>, which every audit column references.</summary>
        public static CoreTable Users { get; } = new("core", "Users");

        /// <summary><c>core.Jobs</c>, which every job entity's <c>JobId</c> references.</summary>
        public static CoreTable Jobs { get; } = new("core", "Jobs");

        /// <summary><c>core.Blobs</c>, which every blob-reference column references.</summary>
        public static CoreTable Blobs { get; } = new("core", "Blobs");
    }
}
