// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using System.ComponentModel.DataAnnotations.Schema;
using System.Reflection;
using System.Text.RegularExpressions;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Blobs;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Entities;
using Tellma.Core.Composition;
using Tellma.Core.Data.Conventions;
using Tellma.Core.EntityFrameworkCore.TableTypes;
using ExcludeFromTableTypeAttribute = Tellma.Core.Abstractions.Entities.ExcludeFromTableTypeAttribute;

namespace Tellma.Core.Data.Metadata
{
    /// <summary>Phase 2 of the build: each leaf's own parts, validated as they are read.</summary>
    internal sealed partial class EntityMetadataBuilder
    {
        /// <summary>The schema a table without one is in.</summary>
        private const string DefaultSchema = "dbo";

        /// <summary>The multilingual group the default display name renders.</summary>
        private const string NameGroup = "Name";

        /// <summary>The conventional code column, which the default related projection carries and key inference prefers.</summary>
        private const string CodeProperty = "Code";

        /// <summary>The CLR types a declared natural key may have: those with a key-list table type.</summary>
        private static readonly HashSet<Type> NaturalKeyTypes = [typeof(string), typeof(int), typeof(long), typeof(Guid), typeof(DateOnly)];

        /// <summary>The integer CLR types a display name may render.</summary>
        private static readonly HashSet<Type> IntegerTypes = [typeof(int), typeof(long), typeof(short), typeof(byte)];

        /// <summary>The column annotations, each of which a property it decorates must be mapped for.</summary>
        private static readonly Type[] ColumnAnnotations =
        [
            typeof(ParentKeyAttribute), typeof(ReferencesAttribute), typeof(NaturalKeyAttribute), typeof(UniqueAttribute),
            typeof(BlobReferenceAttribute), typeof(MultilingualAttribute), typeof(ServerOwnedAttribute), typeof(WriteOnceAttribute),
            typeof(DerivedAttribute), typeof(DefaultedAttribute), typeof(SearchableAttribute), typeof(JsonColumnAttribute),
            typeof(CollationAttribute), typeof(ExcludeFromTableTypeAttribute), typeof(PreserveWhitespaceAttribute),
            typeof(ExcludeFromExcelAttribute),
        ];

        /// <summary>The column annotations that only a string property may carry.</summary>
        private static readonly Type[] StringAnnotations =
        [
            typeof(SearchableAttribute), typeof(PreserveWhitespaceAttribute), typeof(JsonColumnAttribute), typeof(CollationAttribute),
            typeof(MultilingualAttribute),
        ];

        /// <summary>A sized <c>varchar</c> store type, the one an enum is stored in.</summary>
        private static readonly Regex SizedVarchar = new(@"^varchar\(\d+\)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

        /// <summary>The blob kinds declared so far, with the property that declared each first.</summary>
        private readonly Dictionary<string, string> _blobKinds = new(StringComparer.Ordinal);

        /// <summary>Describes one leaf: everything about it that names no other entity.</summary>
        /// <param name="leaf">The leaf.</param>
        private void DescribeLeaf(Leaf leaf)
        {
            IEntityType entityType = leaf.EntityType;

            // --- The table, the capability columns and the members the model maps ---
            TableName table = DescribeTable(leaf);
            CollectCapabilities(leaf);
            leaf.ParentKey = FindParentKey(leaf);
            List<(PropertyInfo Member, IProperty Property)> mapped = MappedMembers(leaf);

            // --- What each property needs to know before it is described ---
            HashSet<string> unique = UniquelyBacked(leaf);
            Dictionary<string, string> groupOf = MultilingualMembership(mapped);

            // --- The properties ---
            List<PropertyMetadata> properties = [];
            foreach ((PropertyInfo member, IProperty property) in mapped)
            {
                if (DescribeProperty(leaf, member, property, unique, groupOf) is PropertyMetadata described)
                {
                    properties.Add(described);
                    leaf.Properties[member.Name] = described;
                    leaf.Members[member.Name] = member;
                }
            }

            // --- The parts built from the properties ---
            List<MultilingualGroup> groups = DescribeGroups(leaf, mapped);
            List<UniqueIndexMetadata> uniqueIndexes = DescribeUniqueIndexes(leaf);
            List<NaturalKeyMetadata> naturalKeys = DescribeNaturalKeys(leaf, mapped, unique);
            List<BlobReferenceMetadata> blobReferences = DescribeBlobReferences(leaf, mapped);
            TreeMetadata? tree = DescribeTree(leaf, table);
            string? tableType = DescribeTableType(leaf);

            PropertyMetadata? key = entityType.FindPrimaryKey() is { Properties: [IProperty keyProperty] }
                && leaf.Properties.TryGetValue(keyProperty.Name, out PropertyMetadata? keyMetadata)
                    ? keyMetadata
                    : null;
            if (key is null)
            {
                Report(
                    $"The entity {Display(leaf)} has no single-column primary key the platform reads.",
                    "Keep the Id the entity's base declares as its only key; a model configuration must not change the key.");
                return;
            }

            DisplayNameMetadata displayName = DescribeDisplayName(leaf, groups, naturalKeys, key);
            CheckRelatedSelect(leaf, displayName);

            Func<object>? factory = PropertyAccessors.Factory(leaf.ClrType);
            if (factory is null)
            {
                Report(
                    $"The entity {Display(leaf)} has no parameterless constructor, which resetting its server-owned and derived properties creates fresh instances with.",
                    "Give the class a parameterless constructor; it may be non-public.");
                return;
            }

            // --- The metadata; the parts naming other entities are filled in phase 4 and read lazily ---
            Type clrType = leaf.ClrType;
            leaf.Metadata = new EntityMetadata(
                clrType,
                $"{table.Schema}.{leaf.DefaultType.Name}",
                table,
                tableType,
                key,
                properties,
                naturalKeys,
                groups,
                uniqueIndexes,
                isTopLevel: EntityShapes.IsTopLevel(clrType),
                isTemporal: entityType.IsTemporal(),
                isActivatable: typeof(IActivatable).IsAssignableFrom(clrType),
                isJobEntity: typeof(IJobEntity).IsAssignableFrom(clrType),
                isCacheable: EntityShapes.GetAttribute<CacheableAttribute>(clrType) is not null,
                tree,
                blobReferences,
                displayName,
                factory,
                children: _ => leaf.Collections,
                references: _ => leaf.References,
                schemaFingerprint: metadata => Fingerprints.Schema(metadata, leaf.StoreTypes));
        }

        /// <summary>
        ///     The leaf's table, and the table rules: the class declares its table with
        ///     <see cref="TableAttribute" /> in a schema of its own (so a leaf's substitution never renames
        ///     it), and a leaf that re-declares the attribute names its default's table.
        /// </summary>
        /// <param name="leaf">The leaf.</param>
        /// <returns>The table.</returns>
        private TableName DescribeTable(Leaf leaf)
        {
            string? schema = leaf.EntityType.GetSchema();
            string name = leaf.EntityType.GetTableName()!;
            TableName table = new(string.IsNullOrEmpty(schema) ? DefaultSchema : schema, name);

            if (EntityShapes.GetAttribute<TableAttribute>(leaf.ClrType) is not TableAttribute declared)
            {
                Report(
                    $"The entity {Display(leaf)} declares no [Table], so its table would take its leaf's class name and change with a leaf substitution.",
                    $"Declare the table and its schema on the class: [Table(\"{name}\", Schema = \"<schema>\")].");
                return table;
            }

            if (string.IsNullOrEmpty(schema) || string.Equals(schema, DefaultSchema, StringComparison.OrdinalIgnoreCase))
            {
                Report(
                    $"The entity {Display(leaf)} maps to [{table.Schema}].[{table.Name}]; the dbo schema holds only the platform's own tables and the standalone table types.",
                    $"Name the entity's schema in its [Table]: [Table(\"{name}\", Schema = \"<schema>\")], such as the pack's own lower-case schema.");
            }

            if (leaf.DefaultType != leaf.ClrType
                && EntityShapes.GetAttribute<TableAttribute>(leaf.DefaultType) is TableAttribute inherited
                && (declared.Name != inherited.Name || declared.Schema != inherited.Schema))
            {
                Report(
                    $"The leaf {Display(leaf)} re-declares [Table(\"{declared.Name}\", Schema = \"{declared.Schema}\")], but its default {TypeDisplay.Qualified(leaf.DefaultType)} maps to [{inherited.Schema}].[{inherited.Name}].",
                    $"Remove the leaf's [Table], or name the default's table: [Table(\"{inherited.Name}\", Schema = \"{inherited.Schema}\")].");
            }

            return table;
        }

        /// <summary>
        ///     The capability columns: every property a platform base or a capability interface declares
        ///     is mapped (a leaf cannot un-inherit, so ignoring or hiding one is refused), and the ones the
        ///     server owns by derivation are recorded.
        /// </summary>
        /// <param name="leaf">The leaf.</param>
        private void CollectCapabilities(Leaf leaf)
        {
            Type clrType = leaf.ClrType;
            List<string> required = [nameof(Entity<>.Id)];
            HashSet<string> serverOwned = leaf.ServerOwnedByDerivation;
            _ = serverOwned.Add(nameof(Entity<>.Id));

            // --- The bases ---
            if (EntityShapes.IsTopLevel(clrType))
            {
                string[] audit = [nameof(TopLevelEntity.CreatedAt), nameof(TopLevelEntity.CreatedById), nameof(TopLevelEntity.ModifiedAt), nameof(TopLevelEntity.ModifiedById)];
                required.AddRange(audit);
                serverOwned.UnionWith(audit);
            }

            if (EntityShapes.IsTree(clrType))
            {
                required.AddRange([nameof(TreeEntity.ParentId), nameof(TreeEntity.SubtreeCount)]);
                _ = serverOwned.Add(nameof(TreeEntity.SubtreeCount));
            }

            if (EntityShapes.IsActivatableTree(clrType))
            {
                required.Add(nameof(ActivatableTreeEntity.ActiveSubtreeCount));
                _ = serverOwned.Add(nameof(ActivatableTreeEntity.ActiveSubtreeCount));
            }

            // --- The capability interfaces, whose members' attributes never reach the class ---
            foreach ((Type capability, string member) in (ReadOnlySpan<(Type, string)>)[(typeof(IActivatable), nameof(IActivatable.IsActive)), (typeof(IJobEntity), nameof(IJobEntity.JobId))])
            {
                if (!capability.IsAssignableFrom(clrType))
                {
                    continue;
                }

                PropertyInfo? implementation = Implementation(clrType, capability, member);
                if (implementation is null || implementation.GetMethod is not { IsPublic: true })
                {
                    Report(
                        $"The entity {Display(leaf)} implements {capability.Name}.{member} explicitly, so the model cannot map the column the capability needs.",
                        $"Implement {member} as a public property of the class.");
                    continue;
                }

                required.Add(implementation.Name);
                _ = serverOwned.Add(implementation.Name);
                if (capability == typeof(IJobEntity))
                {
                    leaf.JobIdMember = implementation;
                }
            }

            foreach (string name in required)
            {
                if (leaf.EntityType.FindProperty(name) is not IProperty property || property.IsShadowProperty())
                {
                    Report(
                        $"The entity {Display(leaf)} does not map {name}, a column its base or capability interface declares and the platform relies on.",
                        $"Remove the [NotMapped] or Ignore() that hides {name}: a leaf cannot un-inherit a capability column.");
                }
            }
        }

        /// <summary>
        ///     The property of a class that implements an interface's property, through the class's
        ///     interface map. The map reports its methods as reflected from the class, so they are matched
        ///     to the declaring class's properties by method handle.
        /// </summary>
        /// <param name="clrType">The class.</param>
        /// <param name="capability">The interface.</param>
        /// <param name="member">The interface property's name.</param>
        /// <returns>The implementing property, or <see langword="null" />.</returns>
        private static PropertyInfo? Implementation(Type clrType, Type capability, string member)
        {
            MethodInfo declared = capability.GetProperty(member)!.GetMethod!;
            InterfaceMapping map = clrType.GetInterfaceMap(capability);
            int index = Array.IndexOf(map.InterfaceMethods, declared);
            MethodInfo target = map.TargetMethods[index];
            return target.DeclaringType!
                .GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .FirstOrDefault(property => property.GetMethod is MethodInfo getter && getter.MethodHandle == target.MethodHandle);
        }

        /// <summary>
        ///     The child's one <see cref="ParentKeyAttribute" /> property. Reports a child that declares
        ///     none or several, and a parent key on a class that is not a child.
        /// </summary>
        /// <param name="leaf">The leaf.</param>
        /// <returns>The parent-key property, or <see langword="null" />.</returns>
        private PropertyInfo? FindParentKey(Leaf leaf)
        {
            List<PropertyInfo> declared = [.. EntityShapes.DeclaredProperties(leaf.ClrType)
                .Where(static property => EntityShapes.GetAttribute<ParentKeyAttribute>(property) is not null)];

            if (!EntityShapes.IsChild(leaf.ClrType))
            {
                if (declared.Count > 0)
                {
                    Report(
                        $"The entity {Display(leaf)} declares [ParentKey] on {string.Join(" and ", declared.Select(static property => property.Name))}, but it is not a child entity.",
                        "Derive the class from ChildEntity to make it a child, or declare the foreign key with [References].");
                }

                return null;
            }

            if (declared is [PropertyInfo single])
            {
                return single;
            }

            Report(
                declared.Count == 0
                    ? $"The child entity {Display(leaf)} declares no [ParentKey], so no collection can carry it."
                    : $"The child entity {Display(leaf)} declares [ParentKey] on {string.Join(" and ", declared.Select(static property => property.Name))}; a child has exactly one parent key.",
                "Mark the one foreign key to the parent with [ParentKey]; a second foreign key to the parent is a [References].");
            return null;
        }

        /// <summary>
        ///     The leaf's mapped scalar properties in declaration order, base classes first, each with its
        ///     model property. Reports a mapped member that is not a public property, and a column
        ///     annotation on a property the model does not map.
        /// </summary>
        /// <param name="leaf">The leaf.</param>
        /// <returns>The mapped members.</returns>
        private List<(PropertyInfo Member, IProperty Property)> MappedMembers(Leaf leaf)
        {
            List<(PropertyInfo, IProperty)> mapped = [];
            HashSet<string> seen = new(StringComparer.Ordinal);
            foreach (PropertyInfo member in EntityShapes.DeclaredProperties(leaf.ClrType))
            {
                if (leaf.EntityType.FindProperty(member.Name) is IProperty property && !property.IsShadowProperty())
                {
                    mapped.Add((member, property));
                    _ = seen.Add(member.Name);
                    continue;
                }

                // An annotation on a member the model never sees would silently do nothing.
                foreach (Type annotation in ColumnAnnotations)
                {
                    if (Attribute.IsDefined(member, annotation, inherit: true))
                    {
                        Report(
                            $"The entity {Display(leaf)} declares [{Annotation(annotation)}] on {member.Name}, which the model does not map.",
                            $"Remove the [{Annotation(annotation)}], or map {member.Name}: remove its [NotMapped], or give it a getter and a setter.");
                    }
                }
            }

            // A member the model maps but the platform cannot read or write as a public property.
            foreach (IProperty property in leaf.EntityType.GetProperties())
            {
                if (!property.IsShadowProperty() && !seen.Contains(property.Name))
                {
                    Report(
                        $"The entity {Display(leaf)} maps {property.Name}, which is not a public property of the class.",
                        $"Make {property.Name} a public property, or stop mapping it.");
                }
            }

            return mapped;
        }

        /// <summary>
        ///     The properties whose value identifies one row: the whole primary key, the whole of a unique
        ///     index, or on a child a unique index over the parent key and the property.
        /// </summary>
        /// <param name="leaf">The leaf.</param>
        /// <returns>The property names.</returns>
        private static HashSet<string> UniquelyBacked(Leaf leaf)
        {
            HashSet<string> unique = new(StringComparer.Ordinal);
            if (leaf.EntityType.FindPrimaryKey() is { Properties: [IProperty key] })
            {
                _ = unique.Add(key.Name);
            }

            foreach (IIndex index in leaf.EntityType.GetIndexes().Where(static index => index.IsUnique))
            {
                if (index.Properties is [IProperty single])
                {
                    _ = unique.Add(single.Name);
                }
                else if (leaf.ParentKey is not null && index.Properties is [IProperty parentKey, IProperty scoped] && parentKey.Name == leaf.ParentKey.Name)
                {
                    _ = unique.Add(scoped.Name);
                }
            }

            return unique;
        }

        /// <summary>The multilingual group each anchor and twin belongs to, by property name.</summary>
        /// <param name="mapped">The mapped members.</param>
        /// <returns>The group name of every member of a group.</returns>
        private static Dictionary<string, string> MultilingualMembership(List<(PropertyInfo Member, IProperty Property)> mapped)
        {
            Dictionary<string, string> groupOf = new(StringComparer.Ordinal);
            foreach ((PropertyInfo member, IProperty _) in mapped)
            {
                if (member.PropertyType == typeof(string) && EntityShapes.GetAttribute<MultilingualAttribute>(member) is not null)
                {
                    groupOf[member.Name] = member.Name;
                    _ = groupOf.TryAdd(member.Name + MultilingualAttribute.SecondarySuffix, member.Name);
                    _ = groupOf.TryAdd(member.Name + MultilingualAttribute.TertiarySuffix, member.Name);
                }
            }

            return groupOf;
        }

        /// <summary>
        ///     Describes one property, and checks its own rules: ownership, a name that stays the CLR name
        ///     where the platform names the column literally, annotations on the right types, and enums
        ///     stored by name in a sized <c>varchar</c>.
        /// </summary>
        /// <param name="leaf">The leaf.</param>
        /// <param name="member">The CLR property.</param>
        /// <param name="property">The model's property.</param>
        /// <param name="unique">The uniquely backed property names.</param>
        /// <param name="groupOf">The multilingual group of each group member.</param>
        /// <returns>The property's metadata, or <see langword="null" /> when it has no usable accessor.</returns>
        private PropertyMetadata? DescribeProperty(
            Leaf leaf,
            PropertyInfo member,
            IProperty property,
            HashSet<string> unique,
            Dictionary<string, string> groupOf)
        {
            string column = property.GetColumnName();
            string storeType = property.GetColumnType();
            RelationalTypeMapping mapping = property.GetRelationalTypeMapping();
            Type clrType = member.PropertyType;
            Type underlying = Nullable.GetUnderlyingType(clrType) ?? clrType;
            leaf.StoreTypes[member.Name] = storeType;

            // --- Ownership ---
            PropertyOwnership ownership = Ownership(leaf, member, property);
            if (EntityShapes.GetAttribute<DefaultedAttribute>(member) is not null && ownership != PropertyOwnership.Editable)
            {
                Report(
                    $"The entity {Display(leaf)} declares [Defaulted] on {member.Name}, which is {ownership}; only an editable property arrives empty for the pipeline to fill.",
                    $"Remove the [Defaulted] from {member.Name}.");
            }

            // --- The column name, where the platform's own statements spell it ---
            bool fixedName = IsPlatformBase(member.DeclaringType!)
                || leaf.ServerOwnedByDerivation.Contains(member.Name)
                || member.DeclaringType!.IsAssignableFrom(leaf.DefaultType);
            if (fixedName && column != member.Name)
            {
                Report(
                    $"The entity {Display(leaf)} maps {member.Name} to the column {column}; a column its base, a capability interface or its default leaf declares keeps its property's name.",
                    $"Remove the [Column] name or HasColumnName from {member.Name}; [Column(TypeName = ...)] alone is allowed.");
            }

            // --- Annotations on the right types ---
            if (clrType != typeof(string))
            {
                foreach (Type annotation in StringAnnotations)
                {
                    if (Attribute.IsDefined(member, annotation, inherit: true))
                    {
                        Report(
                            $"The entity {Display(leaf)} declares [{Annotation(annotation)}] on {member.Name}, a {TypeDisplay.Short(clrType)}; it applies to a string property only.",
                            $"Remove the [{Annotation(annotation)}] from {member.Name}.");
                    }
                }
            }

            // --- Enums: by member name, in a sized varchar ---
            if (underlying.IsEnum
                && (mapping.Converter?.ProviderClrType != typeof(string) || !SizedVarchar.IsMatch(storeType)))
            {
                Report(
                    $"The entity {Display(leaf)} stores the enum {member.Name} as {storeType}; every enum is stored by member name in a sized varchar.",
                    $"Remove the conversion or column type a model configuration gives {member.Name}; the conventions store it as varchar(n).");
            }

            // --- The accessors ---
            Action<object, object?>? setter = PropertyAccessors.Setter(leaf.ClrType, member, property.FieldInfo);
            if (member.GetMethod is null || setter is null)
            {
                Report(
                    $"The entity {Display(leaf)} maps {member.Name}, which the platform cannot both read and write.",
                    $"Give {member.Name} a getter and a setter; the setter may be non-public.");
                return null;
            }

            SearchableAttribute? searchable = EntityShapes.GetAttribute<SearchableAttribute>(member);
            int? size = mapping.Size is int length && length >= 0 ? length : property.GetMaxLength();
            return new PropertyMetadata(
                member.Name,
                column,
                clrType,
                ownership,
                IsNullable: property.IsColumnNullable(),
                IsUnique: unique.Contains(member.Name),
                MaxLength: size,
                Precision: mapping.Precision ?? property.GetPrecision(),
                Scale: mapping.Scale ?? property.GetScale(),
                EnumValues: underlying.IsEnum ? Array.AsReadOnly(Enum.GetNames(underlying)) : null,
                ValueSet: null,
                MultilingualGroup: groupOf.GetValueOrDefault(member.Name),
                IsSearchable: searchable is not null,
                SearchKind: searchable?.Kind,
                ExcludedFromExcel: EntityShapes.GetAttribute<ExcludeFromExcelAttribute>(member) is not null,
                IsDefaulted: EntityShapes.GetAttribute<DefaultedAttribute>(member) is not null,
                Getter: PropertyAccessors.Getter(leaf.ClrType, member),
                Setter: setter);
        }

        /// <summary>
        ///     A property's ownership: database-owned when the model computes it; server-owned by
        ///     derivation for the key, the audit columns, the subtree counts and the capability members;
        ///     else as its one ownership annotation declares, else editable. Reports more than one
        ///     ownership annotation, one on a parent key or a computed column, and one that contradicts the
        ///     derivation.
        /// </summary>
        /// <param name="leaf">The leaf.</param>
        /// <param name="member">The CLR property.</param>
        /// <param name="property">The model's property.</param>
        /// <returns>The ownership.</returns>
        private PropertyOwnership Ownership(Leaf leaf, PropertyInfo member, IProperty property)
        {
            List<(string Name, PropertyOwnership Ownership)> declared = [];
            if (EntityShapes.GetAttribute<ServerOwnedAttribute>(member) is not null)
            {
                declared.Add(("ServerOwned", PropertyOwnership.ServerOwned));
            }

            if (EntityShapes.GetAttribute<WriteOnceAttribute>(member) is not null)
            {
                declared.Add(("WriteOnce", PropertyOwnership.WriteOnce));
            }

            if (EntityShapes.GetAttribute<DerivedAttribute>(member) is not null)
            {
                declared.Add(("Derived", PropertyOwnership.Derived));
            }

            string annotations = string.Join(", ", declared.Select(static item => $"[{item.Name}]"));
            if (declared.Count > 1)
            {
                Report(
                    $"The entity {Display(leaf)} declares {annotations} on {member.Name}; a property has at most one ownership.",
                    $"Keep one of {annotations} on {member.Name}.");
            }

            // --- What the model or the contract decides, whatever the annotations say ---
            bool computed = property.GetComputedColumnSql() is not null;
            bool parentKey = leaf.ParentKey?.Name == member.Name;
            if (declared.Count > 0 && (computed || parentKey))
            {
                Report(
                    $"The entity {Display(leaf)} declares {annotations} on {member.Name}, which is {(computed ? "a database-computed column" : "the child's parent key")}.",
                    $"Remove {annotations} from {member.Name}: {(computed ? "the database owns a computed column" : "the parent key is set by the save that carries the child")}.");
            }

            if (computed)
            {
                return PropertyOwnership.DatabaseOwned;
            }

            if (leaf.ServerOwnedByDerivation.Contains(member.Name))
            {
                if (declared.Any(static item => item.Ownership != PropertyOwnership.ServerOwned))
                {
                    Report(
                        $"The entity {Display(leaf)} declares {annotations} on {member.Name}, which the server owns by its base or capability interface.",
                        $"Remove {annotations} from {member.Name}.");
                }

                return PropertyOwnership.ServerOwned;
            }

            return parentKey || declared.Count == 0 ? PropertyOwnership.Editable : declared[0].Ownership;
        }

        /// <summary>
        ///     The multilingual groups, in anchor declaration order. Reports a twin that is missing, not a
        ///     nullable string, or of another length or unicode than its anchor.
        /// </summary>
        /// <param name="leaf">The leaf.</param>
        /// <param name="mapped">The mapped members.</param>
        /// <returns>The groups.</returns>
        private List<MultilingualGroup> DescribeGroups(Leaf leaf, List<(PropertyInfo Member, IProperty Property)> mapped)
        {
            List<MultilingualGroup> groups = [];
            foreach ((PropertyInfo member, IProperty anchor) in mapped)
            {
                if (member.PropertyType != typeof(string)
                    || EntityShapes.GetAttribute<MultilingualAttribute>(member) is null
                    || !leaf.Properties.TryGetValue(member.Name, out PropertyMetadata? primary))
                {
                    continue;
                }

                RelationalTypeMapping anchorMapping = anchor.GetRelationalTypeMapping();
                var twins = new PropertyMetadata?[2];
                string[] suffixes = [MultilingualAttribute.SecondarySuffix, MultilingualAttribute.TertiarySuffix];
                for (int i = 0; i < suffixes.Length; i++)
                {
                    string twinName = member.Name + suffixes[i];
                    if (!leaf.Properties.TryGetValue(twinName, out PropertyMetadata? twin) || leaf.EntityType.FindProperty(twinName) is not IProperty twinProperty)
                    {
                        Report(
                            $"The entity {Display(leaf)} declares the multilingual group {member.Name}, but maps no twin {twinName}.",
                            $"Add the twin: public string? {twinName} {{ get; set; }}.");
                        continue;
                    }

                    RelationalTypeMapping twinMapping = twinProperty.GetRelationalTypeMapping();
                    if (twin.ClrType != typeof(string) || !twin.IsNullable || twinMapping.Size != anchorMapping.Size || twinMapping.IsUnicode != anchorMapping.IsUnicode)
                    {
                        Report(
                            $"The entity {Display(leaf)} maps the twin {twinName} as {leaf.StoreTypes[twinName]}{(twin.IsNullable ? string.Empty : " NOT NULL")}, but its anchor {member.Name} is {leaf.StoreTypes[member.Name]}; a twin is a nullable string of its anchor's length and unicode.",
                            $"Declare {twinName} as string? with no facets of its own; the conventions give it {member.Name}'s.");
                    }

                    twins[i] = twin;
                }

                groups.Add(new MultilingualGroup(member.Name, primary, twins[0], twins[1]));
            }

            return groups;
        }

        /// <summary>The table's unique indexes over the leaf's properties, by name; an index over a shadow column (a tree's node) is the server's alone and left out.</summary>
        /// <param name="leaf">The leaf.</param>
        /// <returns>The unique indexes.</returns>
        private static List<UniqueIndexMetadata> DescribeUniqueIndexes(Leaf leaf)
        {
            List<UniqueIndexMetadata> indexes = [];
            foreach (IIndex index in leaf.EntityType.GetIndexes()
                .Where(static index => index.IsUnique)
                .OrderBy(static index => index.GetDatabaseName(), StringComparer.Ordinal))
            {
                List<PropertyMetadata> properties = [];
                foreach (IProperty property in index.Properties)
                {
                    if (leaf.Properties.TryGetValue(property.Name, out PropertyMetadata? described))
                    {
                        properties.Add(described);
                    }
                }

                if (properties.Count == index.Properties.Count && index.GetDatabaseName() is string name)
                {
                    indexes.Add(new UniqueIndexMetadata(name, properties));
                }
            }

            return indexes;
        }

        /// <summary>
        ///     The natural keys: the declared ones by their order, then the inferred ones (the multilingual
        ///     <c>Name</c> anchor, <c>Code</c>, the required unique strings, the nullable unique strings, in
        ///     declaration order). Reports a declared key or unique property that is a JSON or binary column,
        ///     has no unique index behind it, or is a key of a type with no key-list table type.
        /// </summary>
        /// <param name="leaf">The leaf.</param>
        /// <param name="mapped">The mapped members.</param>
        /// <param name="unique">The uniquely backed property names.</param>
        /// <returns>The natural keys, ranked.</returns>
        private List<NaturalKeyMetadata> DescribeNaturalKeys(Leaf leaf, List<(PropertyInfo Member, IProperty Property)> mapped, HashSet<string> unique)
        {
            List<(PropertyMetadata Property, int Order, int Position)> declared = [];
            for (int position = 0; position < mapped.Count; position++)
            {
                PropertyInfo member = mapped[position].Member;
                NaturalKeyAttribute? naturalKey = EntityShapes.GetAttribute<NaturalKeyAttribute>(member);
                bool isUnique = naturalKey is not null || EntityShapes.GetAttribute<UniqueAttribute>(member) is not null;
                if (!isUnique || !leaf.Properties.TryGetValue(member.Name, out PropertyMetadata? property))
                {
                    continue;
                }

                string annotation = naturalKey is not null ? "NaturalKey" : "Unique";
                string storeType = leaf.StoreTypes[member.Name];
                Type underlying = Nullable.GetUnderlyingType(member.PropertyType) ?? member.PropertyType;
                bool binary = member.PropertyType == typeof(byte[]) || storeType.Contains("binary", StringComparison.OrdinalIgnoreCase);
                bool valid = true;

                if (EntityShapes.GetAttribute<JsonColumnAttribute>(member) is not null || binary)
                {
                    Report(
                        $"The entity {Display(leaf)} declares [{annotation}] on {member.Name}, a {(binary ? "binary" : "JSON")} column, which no unique index can key.",
                        $"Remove the [{annotation}] from {member.Name}.");
                    valid = false;
                }
                else if (!unique.Contains(member.Name))
                {
                    Report(
                        $"The entity {Display(leaf)} declares [{annotation}] on {member.Name}, but no unique index backs it{(leaf.ParentKey is null ? string.Empty : " within its parent")}.",
                        $"Remove the model configuration that replaces or drops the index the conventions give {member.Name}.");
                    valid = false;
                }

                if (naturalKey is not null && !NaturalKeyTypes.Contains(underlying))
                {
                    Report(
                        $"The entity {Display(leaf)} declares [NaturalKey] on {member.Name}, a {TypeDisplay.Short(member.PropertyType)}; a natural key is a string, int, long, Guid or DateOnly, the types a key list binds.",
                        $"Declare {member.Name} [Unique] instead, or key the entity on a property of one of those types.");
                    valid = false;
                }

                if (naturalKey is not null && valid)
                {
                    declared.Add((property, naturalKey.Order, position));
                }
            }

            // --- The declared keys, by their order and then where they are declared ---
            List<NaturalKeyMetadata> keys = [];
            HashSet<string> taken = new(StringComparer.Ordinal);
            foreach ((PropertyMetadata property, int _, int _) in declared.OrderBy(static key => key.Order).ThenBy(static key => key.Position))
            {
                keys.Add(new NaturalKeyMetadata(property, IsDeclared: true, keys.Count));
                _ = taken.Add(property.Name);
            }

            // --- The inferred keys: unique strings, in the inference order ---
            List<PropertyMetadata> candidates = [.. mapped
                .Select(item => leaf.Properties.GetValueOrDefault(item.Member.Name))
                .OfType<PropertyMetadata>()
                .Where(property => property.ClrType == typeof(string)
                    && unique.Contains(property.Name)
                    && !taken.Contains(property.Name)
                    && EntityShapes.GetAttribute<JsonColumnAttribute>(leaf.Members[property.Name]) is null)];

            IEnumerable<PropertyMetadata> inferred = candidates
                .Where(static property => property.Name == NameGroup && property.MultilingualGroup == NameGroup)
                .Concat(candidates.Where(static property => property.Name == CodeProperty))
                .Concat(candidates.Where(static property => !property.IsNullable))
                .Concat(candidates.Where(static property => property.IsNullable))
                .Distinct();
            foreach (PropertyMetadata property in inferred)
            {
                keys.Add(new NaturalKeyMetadata(property, IsDeclared: false, keys.Count));
            }

            return keys;
        }

        /// <summary>
        ///     The blob-reference columns. Reports a reference on a property other than <c>int?</c>, a kind
        ///     outside the kind grammar or declared by two properties, and a declaration whose policy cannot
        ///     be made.
        /// </summary>
        /// <param name="leaf">The leaf.</param>
        /// <param name="mapped">The mapped members.</param>
        /// <returns>The blob references.</returns>
        private List<BlobReferenceMetadata> DescribeBlobReferences(Leaf leaf, List<(PropertyInfo Member, IProperty Property)> mapped)
        {
            List<BlobReferenceMetadata> references = [];
            foreach ((PropertyInfo member, IProperty _) in mapped)
            {
                if (EntityShapes.GetAttribute<BlobReferenceAttribute>(member) is not BlobReferenceAttribute blob
                    || !leaf.Properties.TryGetValue(member.Name, out PropertyMetadata? property))
                {
                    continue;
                }

                string declaration = $"{Display(leaf)}.{member.Name}";
                bool valid = true;
                if (member.PropertyType != typeof(int?))
                {
                    Report(
                        $"The entity {Display(leaf)} declares [BlobReference] on {member.Name}, a {TypeDisplay.Short(member.PropertyType)}; a blob reference is an int? column.",
                        $"Declare {member.Name} as int?.");
                    valid = false;
                }

                if (!BlobName.IsValidKind(blob.Kind))
                {
                    Report(
                        $"The entity {Display(leaf)} declares the blob kind '{blob.Kind}' on {member.Name}, which is not a valid kind.",
                        "Name the kind in lower-case letters, digits and hyphens, starting with a letter, such as 'user-image'.");
                    valid = false;
                }
                else if (!_blobKinds.TryAdd(blob.Kind, declaration))
                {
                    Report(
                        $"The blob kind '{blob.Kind}' is declared by both {_blobKinds[blob.Kind]} and {declaration}; one property declares a kind.",
                        $"Give {declaration} a kind of its own.");
                    valid = false;
                }

                BlobKindPolicy? policy = null;
                try
                {
                    policy = blob.CreatePolicy();
                }
                catch (Exception exception) when (exception is InvalidOperationException or ArgumentOutOfRangeException)
                {
                    Report(
                        $"The blob reference {declaration} cannot make its policy: {exception.Message}",
                        $"Correct the [BlobReference] on {member.Name}.");
                    valid = false;
                }

                if (valid && policy is not null)
                {
                    references.Add(new BlobReferenceMetadata(property, blob.Kind, policy));
                }
            }

            return references;
        }

        /// <summary>The tree columns of a tree entity. Reports a depth outside 2 to 32 and a missing unique node index.</summary>
        /// <param name="leaf">The leaf.</param>
        /// <param name="table">The leaf's table.</param>
        /// <returns>The tree's metadata, or <see langword="null" /> for a non-tree or a tree whose columns are missing.</returns>
        private TreeMetadata? DescribeTree(Leaf leaf, TableName table)
        {
            if (!EntityShapes.IsTree(leaf.ClrType))
            {
                return null;
            }

            int maxDepth = leaf.EntityType.FindAnnotation(TellmaAnnotationNames.TreeMaxDepth)?.Value as int?
                ?? EntityShapes.GetAttribute<TreeAttribute>(leaf.ClrType)?.MaxDepth
                ?? TreeAttribute.DefaultMaxDepth;
            if (maxDepth is < 2 or > TreeAttribute.DefaultMaxDepth)
            {
                Report(
                    $"The tree entity {Display(leaf)} declares [Tree(MaxDepth = {maxDepth})]; a tree's depth is between 2 and {TreeAttribute.DefaultMaxDepth}.",
                    $"Declare a depth between 2 and {TreeAttribute.DefaultMaxDepth}.");
            }

            string nodeIndex = ConstraintNames.UniqueIndex(table.Name, [TreeConvention.NodeColumn]);
            bool indexed = leaf.EntityType.GetIndexes().Any(index => index.IsUnique
                && index.Properties is [IProperty node]
                && node.Name == TreeConvention.NodeColumn
                && index.GetDatabaseName() == nodeIndex);
            if (!indexed)
            {
                Report(
                    $"The tree entity {Display(leaf)} has no unique index {nodeIndex} over its node column.",
                    "Remove the model configuration that replaces or drops the index the conventions give the node.");
            }

            return leaf.Properties.TryGetValue(nameof(TreeEntity.ParentId), out PropertyMetadata? parentId)
                && leaf.Properties.TryGetValue(nameof(TreeEntity.SubtreeCount), out PropertyMetadata? subtreeCount)
                    ? new TreeMetadata(
                        parentId,
                        TreeConvention.NodeColumn,
                        subtreeCount,
                        leaf.Properties.GetValueOrDefault(nameof(ActivatableTreeEntity.ActiveSubtreeCount)),
                        maxDepth)
                    : null;
        }

        /// <summary>
        ///     The physical name of the leaf's row-image type. Reports a top-level or child entity without
        ///     one, a shadow node or period column inside it, and a capability column left out of it.
        /// </summary>
        /// <param name="leaf">The leaf.</param>
        /// <returns>The physical name, <c>&lt;schema&gt;.&lt;name&gt;_&lt;hash8&gt;</c>, or <see langword="null" /> when the entity has no table type.</returns>
        private string? DescribeTableType(Leaf leaf)
        {
            TableTypeDefinition? tableType = leaf.EntityType.GetTableType();
            bool saved = EntityShapes.IsTopLevel(leaf.ClrType) || EntityShapes.IsChild(leaf.ClrType);
            if (tableType is null)
            {
                if (saved)
                {
                    Report(
                        $"The entity {Display(leaf)} has no table type, so its rows cannot be saved.",
                        "Declare [TableType] on the class, and remove any model configuration's HasNoTableType().");
                }

                return null;
            }

            HashSet<string> columns = [.. tableType.Columns.Select(static column => column.Name)];

            // --- The shadow columns the server alone writes stay out of the row image ---
            List<string> shadows = [];
            if (EntityShapes.IsTree(leaf.ClrType))
            {
                shadows.Add(TreeConvention.NodeColumn);
            }

            if (leaf.EntityType.IsTemporal())
            {
                shadows.AddRange([TemporalConvention.PeriodStart, TemporalConvention.PeriodEnd]);
            }

            foreach (string shadow in shadows.Where(columns.Contains))
            {
                Report(
                    $"The table type of {Display(leaf)} carries the column {shadow}, which the server alone writes.",
                    $"Remove the model configuration that re-includes {shadow} in the table type.");
            }

            // --- The capability columns stay in it ---
            if (saved)
            {
                foreach (string capability in leaf.ServerOwnedByDerivation.Append(nameof(TreeEntity.ParentId)))
                {
                    if (leaf.EntityType.FindProperty(capability) is IProperty property
                        && !property.IsShadowProperty()
                        && !columns.Contains(property.GetColumnName()))
                    {
                        Report(
                            $"The table type of {Display(leaf)} leaves out {capability}, a column its base or capability interface declares.",
                            $"Remove the [ExcludeFromTableType] or exclusion that drops {capability}.");
                    }
                }
            }

            return $"{tableType.Schema}.{tableType.PhysicalName}";
        }

        /// <summary>
        ///     The display-name rule: the declared source and prefix, else the multilingual <c>Name</c>
        ///     group, else the first natural key, else the key. Reports a declared source that is not a
        ///     group anchor or a string, integer or date property of the leaf, and a prefix that is not a
        ///     string or integer property of the leaf.
        /// </summary>
        /// <param name="leaf">The leaf.</param>
        /// <param name="groups">The leaf's multilingual groups.</param>
        /// <param name="naturalKeys">The leaf's natural keys.</param>
        /// <param name="key">The leaf's key.</param>
        /// <returns>The rule.</returns>
        private DisplayNameMetadata DescribeDisplayName(Leaf leaf, List<MultilingualGroup> groups, List<NaturalKeyMetadata> naturalKeys, PropertyMetadata key)
        {
            if (EntityShapes.GetAttribute<DisplayNameSourceAttribute>(leaf.ClrType) is not DisplayNameSourceAttribute declared)
            {
                return groups.FirstOrDefault(static group => group.Name == NameGroup) is MultilingualGroup name
                    ? new DisplayNameMetadata(name.Primary, name, null)
                    : new DisplayNameMetadata(naturalKeys.Count > 0 ? naturalKeys[0].Property : key, null, null);
            }

            // --- The declared source ---
            PropertyMetadata source = key;
            MultilingualGroup? group = null;
            if (leaf.Properties.TryGetValue(declared.Property ?? string.Empty, out PropertyMetadata? named))
            {
                group = groups.FirstOrDefault(candidate => candidate.Name == named.Name);
                if (group is null && !IsDisplayable(named.ClrType, dates: true))
                {
                    Report(
                        $"The entity {Display(leaf)} declares [DisplayNameSource(\"{declared.Property}\")], a {TypeDisplay.Short(named.ClrType)}; a display name renders a multilingual anchor or a string, integer or date property.",
                        "Name a multilingual anchor or a string, integer or DateOnly property of the entity.");
                }

                source = named;
            }
            else
            {
                Report(
                    $"The entity {Display(leaf)} declares [DisplayNameSource(\"{declared.Property}\")], which names no mapped property of the entity.",
                    "Name a mapped property of the entity, with nameof.");
            }

            // --- The declared prefix ---
            PropertyMetadata? prefix = null;
            if (declared.Prefix is string prefixName)
            {
                if (!leaf.Properties.TryGetValue(prefixName, out prefix))
                {
                    Report(
                        $"The entity {Display(leaf)} declares the display-name prefix \"{prefixName}\", which names no mapped property of the entity.",
                        "Name a mapped string or integer property of the entity, with nameof.");
                }
                else if (!IsDisplayable(prefix.ClrType, dates: false))
                {
                    Report(
                        $"The entity {Display(leaf)} declares the display-name prefix {prefixName}, a {TypeDisplay.Short(prefix.ClrType)}; a prefix is a string or integer property.",
                        "Name a string or integer property as the prefix.");
                }
            }

            return new DisplayNameMetadata(source, group, prefix);
        }

        /// <summary>Whether a display name can render a property of a CLR type: a string, an integer, and optionally a date.</summary>
        /// <param name="clrType">The type.</param>
        /// <param name="dates">Whether a <see cref="DateOnly" /> renders.</param>
        /// <returns>True when it renders.</returns>
        private static bool IsDisplayable(Type clrType, bool dates)
        {
            Type underlying = Nullable.GetUnderlyingType(clrType) ?? clrType;
            return underlying == typeof(string) || IntegerTypes.Contains(underlying) || (dates && underlying == typeof(DateOnly));
        }

        /// <summary>
        ///     The related projection a <see cref="RelatedSelectAttribute" /> declares keeps <c>Id</c>, the
        ///     display-name columns and <c>Code</c>, which every navigation to the entity renders by.
        /// </summary>
        /// <param name="leaf">The leaf.</param>
        /// <param name="displayName">The leaf's display-name rule.</param>
        private void CheckRelatedSelect(Leaf leaf, DisplayNameMetadata displayName)
        {
            if (EntityShapes.GetAttribute<RelatedSelectAttribute>(leaf.ClrType) is not RelatedSelectAttribute related)
            {
                return;
            }

            HashSet<string> selected = [.. (related.Select ?? string.Empty)
                .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)];

            List<string> required = [nameof(Entity<>.Id), .. DisplayNameColumns(displayName)];
            if (leaf.Properties.ContainsKey(CodeProperty))
            {
                required.Add(CodeProperty);
            }

            List<string> missing = [.. required.Distinct(StringComparer.Ordinal).Where(column => !selected.Contains(column))];
            if (missing.Count > 0)
            {
                Report(
                    $"The entity {Display(leaf)} declares [RelatedSelect(\"{related.Select}\")], which leaves out {string.Join(", ", missing)}; a related projection keeps Id, the display-name columns and Code.",
                    $"Add {string.Join(", ", missing)} to the [RelatedSelect].");
            }
        }

        /// <summary>Every display-name column of a rule, whatever a tenant's content languages: the source or every twin of its group, then the prefix.</summary>
        /// <param name="displayName">The rule.</param>
        /// <returns>The property names.</returns>
        private static IEnumerable<string> DisplayNameColumns(DisplayNameMetadata displayName)
        {
            IEnumerable<PropertyMetadata?> columns = displayName.Group is MultilingualGroup group
                ? [group.Primary, group.Secondary, group.Tertiary]
                : [displayName.Source];

            return columns.Append(displayName.Prefix).OfType<PropertyMetadata>().Select(static property => property.Name);
        }

        /// <summary>A leaf for messages: its namespace-qualified class name.</summary>
        /// <param name="leaf">The leaf.</param>
        /// <returns>The description.</returns>
        private static string Display(Leaf leaf)
        {
            return TypeDisplay.Qualified(leaf.ClrType);
        }

        /// <summary>An annotation's name without its suffix, as it is written: <c>NaturalKey</c>.</summary>
        /// <param name="annotation">The attribute type.</param>
        /// <returns>The name.</returns>
        private static string Annotation(Type annotation)
        {
            const string Suffix = nameof(Attribute);
            return annotation.Name.EndsWith(Suffix, StringComparison.Ordinal) ? annotation.Name[..^Suffix.Length] : annotation.Name;
        }
    }
}
