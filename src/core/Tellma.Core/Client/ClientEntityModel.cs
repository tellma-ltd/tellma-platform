// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Frozen;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Blobs;
using Tellma.Core.Abstractions.Client;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Entities;
using Tellma.Core.Abstractions.Localization;
using Tellma.Core.Abstractions.Settings;
using Tellma.Core.Crud;
using Tellma.Core.Crud.Query;
using Tellma.Core.Data;
using Tellma.Core.Data.Queries;
using Tellma.Core.Localization;
using Tellma.Core.Queryex;
using Tellma.Core.Sharing;

namespace Tellma.Core.Client
{
    // The model is spec 0029 §2: every root of the All-shape query schema, sorted by name, each with
    // its members in metadata order and every label key resolved (§2.4), so one composition always
    // emits the same bytes. The display-name rule is §5.1's and the blob policies §16's.
    /// <summary>
    ///     The client entity model of the realised composition: every entity the query language
    ///     knows, described as the client metadata carries it, with the named enums. Built once, on
    ///     first use, from the composition's registries; it reads no tenant and no store.
    /// </summary>
    /// <param name="schemas">The query schemas, whose widest shape names every root and member.</param>
    /// <param name="metadata">The model's entity metadata.</param>
    /// <param name="stacks">The stacks and child query surfaces.</param>
    /// <param name="strings">The keys the neutral resources declare, which resolve every label key.</param>
    /// <param name="valueSets">The composed value sets.</param>
    /// <param name="enums">The composition's C# enums.</param>
    /// <param name="blobKinds">The blob kinds, whose policies a blob reference carries.</param>
    /// <param name="shares">The share entities, which the client metadata leaves out.</param>
    internal sealed class ClientEntityModel(
        QueryexSchemaProvider schemas,
        IEntityMetadataProvider metadata,
        IStackRegistry stacks,
        INeutralStringKeys strings,
        IValueSetRegistry valueSets,
        NamedEnums enums,
        IBlobKindRegistry blobKinds,
        ShareEntityTypes shares) : IClientEntityModel
    {
        /// <summary>The operations an API can offer, in the order the client metadata lists them.</summary>
        private static readonly (StackOperations Flag, ClientEntityOperation Operation)[] OperationOrder =
        [
            (StackOperations.Query, ClientEntityOperation.Query),
            (StackOperations.Details, ClientEntityOperation.Details),
            (StackOperations.Save, ClientEntityOperation.Save),
            (StackOperations.Delete, ClientEntityOperation.Delete),
            (StackOperations.Import, ClientEntityOperation.Import),
            (StackOperations.Export, ClientEntityOperation.Export),
        ];

        /// <summary>The model, built on first use.</summary>
        private readonly Lazy<Contents> _contents = new(
            () => Build(schemas, metadata, stacks, strings, valueSets, enums, blobKinds, shares),
            LazyThreadSafetyMode.ExecutionAndPublication);

        /// <inheritdoc />
        public IReadOnlyList<ClientEntityMetadata> Entities => _contents.Value.Entities;

        /// <inheritdoc />
        public IReadOnlyList<ClientEnumMetadata> Enums => _contents.Value.Enums;

        /// <inheritdoc />
        public ClientEntityMetadata? Find(string entity)
        {
            ArgumentNullException.ThrowIfNull(entity);
            return _contents.Value.ByName.GetValueOrDefault(entity);
        }

        /// <summary>Builds the model.</summary>
        /// <param name="schemas">The query schemas.</param>
        /// <param name="metadata">The model's entity metadata.</param>
        /// <param name="stacks">The stacks and child query surfaces.</param>
        /// <param name="strings">The keys the neutral resources declare.</param>
        /// <param name="valueSets">The composed value sets.</param>
        /// <param name="enums">The composition's C# enums.</param>
        /// <param name="blobKinds">The blob kinds.</param>
        /// <param name="shares">The share entities, left out.</param>
        /// <returns>The model.</returns>
        private static Contents Build(
            QueryexSchemaProvider schemas,
            IEntityMetadataProvider metadata,
            IStackRegistry stacks,
            INeutralStringKeys strings,
            IValueSetRegistry valueSets,
            NamedEnums enums,
            IBlobKindRegistry blobKinds,
            ShareEntityTypes shares)
        {
            // --- Every root of the widest shape but the share entities: an entity of the model by its metadata, a read-only root by its descriptor ---
            QueryexSchemaMap map = schemas.Map(MultilingualShape.All);
            Builder builder = new(map, metadata, stacks, strings, enums, blobKinds);
            ClientEntityMetadata[] entities = [.. map.Schema.Entities
                .Where(descriptor => map.ClrType(descriptor) is not Type type || !shares.Types.Contains(type))
                .Select(descriptor => map.ClrType(descriptor) is null ? builder.ReadOnlyRoot(descriptor) : builder.Entity(map.Metadata(descriptor)))
                .OrderBy(static entity => entity.Name, StringComparer.Ordinal)];

            // --- Every named enum once: the C# enums, then the value sets, by name ---
            ClientEnumMetadata[] named = [.. enums.All.Select(builder.Enum)
                .Concat(valueSets.All.Select(builder.ValueSet))
                .OrderBy(static item => item.Name, StringComparer.Ordinal)];

            return new Contents(
                entities.AsReadOnly(),
                named.AsReadOnly(),
                entities.ToFrozenDictionary(static entity => entity.Name, StringComparer.OrdinalIgnoreCase));
        }

        /// <summary>The built model.</summary>
        /// <param name="Entities">Every entity, by name.</param>
        /// <param name="Enums">Every named enum, by name.</param>
        /// <param name="ByName">Every entity, by its name ignoring case.</param>
        private sealed record Contents(
            IReadOnlyList<ClientEntityMetadata> Entities,
            IReadOnlyList<ClientEnumMetadata> Enums,
            FrozenDictionary<string, ClientEntityMetadata> ByName);

        /// <summary>Describes the entities and enums of one composition.</summary>
        /// <param name="map">The widest shape's schema and its map to the model.</param>
        /// <param name="metadata">The model's entity metadata.</param>
        /// <param name="stacks">The stacks and child query surfaces.</param>
        /// <param name="strings">The keys the neutral resources declare.</param>
        /// <param name="enums">The composition's C# enums.</param>
        /// <param name="blobKinds">The blob kinds.</param>
        private sealed class Builder(
            QueryexSchemaMap map,
            IEntityMetadataProvider metadata,
            IStackRegistry stacks,
            INeutralStringKeys strings,
            NamedEnums enums,
            IBlobKindRegistry blobKinds)
        {
            /// <summary>Each child entity's owner and the collection that holds it, by the child's metadata.</summary>
            private readonly Dictionary<EntityMetadata, (EntityMetadata Owner, ChildCollectionMetadata Collection)> _owners = Owners(metadata);

            /// <summary>Describes an entity of the model: a stack, a child, or an entity no stack serves.</summary>
            /// <param name="entity">The entity's metadata.</param>
            /// <returns>The description.</returns>
            public ClientEntityMetadata Entity(EntityMetadata entity)
            {
                StackDescriptor? stack = stacks.Find(entity.Name);
                ChildQueryDescriptor? child = stack is null ? stacks.FindChildQuery(entity.Name) : null;
                EntityDescriptor descriptor = map.Descriptor(entity);
                IReadOnlyDictionary<string, string> wire = WireNames(entity.ClrType);

                // --- What kind of entity, and what its API offers ---
                (ClientEntityKind kind, string? segment) = stack is not null ? (ClientEntityKind.Stack, stack.ResourceSegment)
                    : child is not null ? (ClientEntityKind.Child, child.ResourceSegment)
                    : (ClientEntityKind.ReadOnly, null);
                ClientEntityOperation[] operations = stack is not null
                    ? [.. OperationOrder.Where(item => stack.Operations.HasFlag(item.Flag)).Select(static item => item.Operation)]
                    : child is not null
                        ? child.Exportable ? [ClientEntityOperation.Query, ClientEntityOperation.Export] : [ClientEntityOperation.Query]
                        : [];

                // --- The labels ---
                (string labelKey, string? labelFallback) = LabelChains.Resolve(strings, LabelChains.Entity(entity.Name), LabelChains.EntityFallback(entity.Name));
                string pluralLabelKey = LabelChains.Resolve(strings, LabelChains.Plural(entity.Name), string.Empty).Key;
                string newLabelKey = LabelChains.Resolve(strings, LabelChains.New(entity.Name), string.Empty).Key;

                // --- The owner of a child entity, and the navigation and collection that link them ---
                ClientEntityOwner? owner = null;
                string? ownerKey = null;
                if (_owners.TryGetValue(entity, out (EntityMetadata Owner, ChildCollectionMetadata Collection) parent))
                {
                    // The parent key is always a foreign key with a navigation: the metadata builder
                    // refuses one that does not end in Id, so no model that builds lacks it.
                    ownerKey = parent.Collection.ParentKey.Name;
                    string navigation = entity.References.First(reference => reference.ForeignKey.Name == ownerKey).Navigation;
                    owner = new ClientEntityOwner(parent.Owner.Name, navigation, parent.Collection.Property);
                }

                // --- The members ---
                ClientPropertyMetadata[] properties = [.. Properties(entity, wire)];
                ClientNavigationMetadata[] navigations = [.. descriptor.Navigations.Select(navigation => Navigation(entity, navigation, ownerKey, wire))];
                ClientChildMetadata[] children = [.. entity.Children.Select(collection => Child(entity, collection, wire))];
                IEnumerable<(string Resource, ActionDescriptor Action)> actions = stack is not null ? stack.Actions.Select(action => (stack.Resource, action))
                    : child is not null ? child.Actions.Select(action => (child.Resource, (ActionDescriptor)action))
                    : [];

                // --- The query facts ---
                IReadOnlyList<(string Property, SearchKind Kind)> searchable = stack is { SearchableProperties.Count: > 0 } ? stack.SearchableProperties
                    : child?.SearchableProperties ?? QuerySurfaces.Convention(entity);
                IEnumerable<string> relatedSelect = stack?.RelatedSelect ?? child?.RelatedSelect ?? StackDescriptorBuilder.RelatedSelect(entity);
                StackLimits limits = stack?.Limits ?? child?.Limits ?? StackLimits.Default;
                Type keyType = Nullable.GetUnderlyingType(entity.Key.ClrType) ?? entity.Key.ClrType;
                DisplayNameMetadata display = entity.DisplayName;

                return new ClientEntityMetadata(
                    entity.Name,
                    segment,
                    labelKey,
                    pluralLabelKey,
                    newLabelKey,
                    labelFallback,
                    operations.AsReadOnly(),
                    stack is null ? [] : [.. stack.Capabilities.Order(StringComparer.Ordinal)],
                    new ClientDisplayNameRule(display.Source.Name, display.Group is not null, display.Prefix?.Name),
                    kind,
                    new ClientEntityKey(entity.Key.Name, keyType == typeof(long) ? ClientKeyType.Long : ClientKeyType.Int),
                    owner,
                    properties.AsReadOnly(),
                    navigations.AsReadOnly(),
                    children.AsReadOnly(),
                    [.. actions.Select(item => Action(entity, item.Resource, item.Action))],
                    [.. relatedSelect],
                    stack is not null ? stack.DefaultOrderBy ?? $"{entity.Key.Name} desc" : child?.DefaultOrderBy ?? $"{entity.Key.Name} desc",
                    [.. searchable.Select(static item => item.Property)],
                    [.. entity.NaturalKeys.Select(static natural => natural.Property.Name)],
                    stack is null ? [] : [.. stack.Extras],
                    Limits(limits));
            }

            /// <summary>
            ///     Describes a read-only root: a table the query language reads that is no entity, so
            ///     no API serves it and every column is the server's.
            /// </summary>
            /// <param name="descriptor">The root's descriptor.</param>
            /// <returns>The description.</returns>
            public ClientEntityMetadata ReadOnlyRoot(EntityDescriptor descriptor)
            {
                string name = descriptor.Name;
                (string labelKey, string? labelFallback) = LabelChains.Resolve(strings, LabelChains.Entity(name), LabelChains.EntityFallback(name));
                PropertyDescriptor key = descriptor.Key;
                return new ClientEntityMetadata(
                    name,
                    Segment: null,
                    labelKey,
                    LabelChains.Resolve(strings, LabelChains.Plural(name), string.Empty).Key,
                    LabelChains.Resolve(strings, LabelChains.New(name), string.Empty).Key,
                    labelFallback,
                    Operations: [],
                    Capabilities: [],
                    new ClientDisplayNameRule(key.Name, Multilingual: false, Prefix: null),
                    ClientEntityKind.ReadOnly,
                    new ClientEntityKey(key.Name, Store(key).Family == QueryexStoreFamily.QxBigInt ? ClientKeyType.Long : ClientKeyType.Int),
                    Owner: null,
                    [.. descriptor.Properties.Select(property => ReadOnlyProperty(name, property))],
                    [.. descriptor.Navigations.Select(navigation => Navigation(name, navigation, ClientNavigationKind.Reference, navigation.IsOptional || !navigation.ForeignKey.IsNotNull, navigation.ForeignKey.Name))],
                    Children: [],
                    Actions: [],
                    RelatedSelect: [key.Name],
                    DefaultOrderBy: $"{key.Name} desc",
                    Searchable: [],
                    NaturalKeys: [],
                    Extras: [],
                    Limits(StackLimits.Default));
            }

            /// <summary>Describes a C# enum: each member's wire name with its label, in value order.</summary>
            /// <param name="named">The enum.</param>
            /// <returns>The description.</returns>
            public ClientEnumMetadata Enum(NamedEnum named)
            {
                ClientEnumValue[] values = [.. named.Members().Select(member =>
                {
                    (string key, string? fallback) = LabelChains.Resolve(
                        strings,
                        LabelChains.EnumValue(named.KeyPrefixes, named.Type.Name, member.Name),
                        NameHumanizer.Humanize(member.Name));
                    return new ClientEnumValue(member.Wire, key, Label: null, fallback);
                })];

                return new ClientEnumMetadata(named.Name, values.AsReadOnly(), Aliases: null);
            }

            /// <summary>
            ///     Describes a value set: each value with its key, or its literal label, which reads the
            ///     same in every language; a key no resource declares falls back to the value itself.
            /// </summary>
            /// <param name="set">The set.</param>
            /// <returns>The description.</returns>
            public ClientEnumMetadata ValueSet(ValueSetDescriptor set)
            {
                ClientEnumValue[] values = [.. set.Values.Select(entry => entry.Label is string label
                    ? new ClientEnumValue(entry.Value, LabelKey: null, label, LabelFallback: null)
                    : new ClientEnumValue(entry.Value, entry.LabelKey, Label: null, entry.LabelKey is string key && strings.Declares(key) ? null : entry.Value))];

                // The aliases by name, so the emitted bytes never depend on the set's dictionary.
                SortedDictionary<string, string>? aliases = set.Aliases is { Count: > 0 } declared
                    ? new SortedDictionary<string, string>(declared.ToDictionary(StringComparer.Ordinal), StringComparer.Ordinal)
                    : null;
                return new ClientEnumMetadata(set.Name, values.AsReadOnly(), aliases);
            }

            /// <summary>The query ceilings of an entity, with the longest filter the query language reads.</summary>
            /// <param name="limits">The stack's ceilings, or the defaults.</param>
            /// <returns>The ceilings.</returns>
            private static ClientEntityLimits Limits(StackLimits limits)
            {
                return new ClientEntityLimits(
                    limits.MaxTake,
                    limits.CountCap,
                    limits.MaxSkipWindow,
                    limits.MaxSearchLength,
                    limits.MaxIds,
                    limits.MaxExtras,
                    QueryexLimits.Default.MaxInputLength);
            }

            /// <summary>Each child entity's owner and collection, from every entity's child collections.</summary>
            /// <param name="metadata">The model's entity metadata.</param>
            /// <returns>The owners, by child.</returns>
            private static Dictionary<EntityMetadata, (EntityMetadata Owner, ChildCollectionMetadata Collection)> Owners(IEntityMetadataProvider metadata)
            {
                Dictionary<EntityMetadata, (EntityMetadata, ChildCollectionMetadata)> owners = new(ReferenceEqualityComparer.Instance);
                foreach (EntityMetadata entity in metadata.All)
                {
                    foreach (ChildCollectionMetadata collection in entity.Children)
                    {
                        _ = owners.TryAdd(collection.Child, (entity, collection));
                    }
                }

                return owners;
            }

            /// <summary>The wire names of a type's members, as the platform's JSON rules spell them, by property name.</summary>
            /// <param name="type">The type.</param>
            /// <returns>The wire names.</returns>
            private static Dictionary<string, string> WireNames(Type type)
            {
                JsonTypeInfo info = TellmaJson.Options.GetTypeInfo(type);
                Dictionary<string, string> names = new(StringComparer.Ordinal);
                foreach (JsonPropertyInfo property in info.Properties)
                {
                    if (property.AttributeProvider is PropertyInfo member)
                    {
                        _ = names.TryAdd(member.Name, property.Name);
                    }
                }

                return names;
            }

            /// <summary>A member's wire name: the platform's JSON rules', else the naming policy's for a member the wire leaves out.</summary>
            /// <param name="wire">The type's wire names.</param>
            /// <param name="name">The member's name.</param>
            /// <returns>The wire name.</returns>
            private static string Json(IReadOnlyDictionary<string, string> wire, string name)
            {
                return wire.TryGetValue(name, out string? json) ? json : JsonNamingPolicy.CamelCase.ConvertName(name);
            }

            /// <summary>The store type of a read-only root's column, which the schema always records.</summary>
            /// <param name="property">The column.</param>
            /// <returns>The store type.</returns>
            private static QueryexStoreType Store(PropertyDescriptor property)
            {
                return property.StoreType ?? throw new InvalidOperationException($"The column {property.Name} of a read-only root has no store type.");
            }

            /// <summary>Who writes a property, as the client reads it.</summary>
            /// <param name="ownership">The property's ownership.</param>
            /// <returns>The client's ownership.</returns>
            private static ClientPropertyOwnership Ownership(PropertyOwnership ownership)
            {
                return ownership switch
                {
                    PropertyOwnership.Editable => ClientPropertyOwnership.Editable,
                    PropertyOwnership.WriteOnce => ClientPropertyOwnership.WriteOnce,
                    PropertyOwnership.ServerOwned => ClientPropertyOwnership.ServerOwned,
                    PropertyOwnership.Derived => ClientPropertyOwnership.Derived,
                    PropertyOwnership.DatabaseOwned => ClientPropertyOwnership.DatabaseOwned,
                    _ => throw new ArgumentOutOfRangeException(nameof(ownership), ownership, "Not a defined property ownership."),
                };
            }

            /// <summary>
            ///     An entity's properties: those the query language reads, then interleaved in metadata
            ///     order the JSON columns, which it does not but a details form shows.
            /// </summary>
            /// <param name="entity">The entity.</param>
            /// <param name="wire">The entity's wire names.</param>
            /// <returns>The properties, in metadata order.</returns>
            private IEnumerable<ClientPropertyMetadata> Properties(EntityMetadata entity, IReadOnlyDictionary<string, string> wire)
            {
                HashSet<PropertyMetadata> queryable = new(map.Declared(entity), ReferenceEqualityComparer.Instance);
                HashSet<PropertyMetadata> json = new(map.JsonColumns(entity), ReferenceEqualityComparer.Instance);
                foreach (PropertyMetadata property in entity.Properties.Where(property => queryable.Contains(property) || json.Contains(property)))
                {
                    yield return Property(entity, property, queryable.Contains(property), json.Contains(property), wire);
                }
            }

            /// <summary>Describes one property of an entity.</summary>
            /// <param name="entity">The entity.</param>
            /// <param name="property">The property.</param>
            /// <param name="queryable">Whether the query language reads it.</param>
            /// <param name="json">Whether it is a JSON column.</param>
            /// <param name="wire">The entity's wire names.</param>
            /// <returns>The description.</returns>
            private ClientPropertyMetadata Property(EntityMetadata entity, PropertyMetadata property, bool queryable, bool json, IReadOnlyDictionary<string, string> wire)
            {
                // --- The type: a JSON column, a value set, a C# enum, else the CLR type's ---
                string? enumName = property.ValueSet ?? (NamedEnums.Unwrap(property.ClrType) is Type enumType ? enums.Find(enumType)?.Name : null);
                ClientDataType type = json ? ClientDataType.Json
                    : property.ValueSet is not null ? ClientDataType.Enum
                    : ClientDataTypes.Of(property.ClrType)
                        ?? throw new InvalidOperationException($"The property {property.Name} of {entity.Name} has the type {property.ClrType.Name}, which the client has no data type for.");

                // --- A multilingual column: its group and slot; a twin takes its anchor's label ---
                ClientMultilingualSlot? slot = null;
                string labelSource = property.Name;
                if (property.MultilingualGroup is string groupName && entity.MultilingualGroups.FirstOrDefault(group => group.Name == groupName) is MultilingualGroup group)
                {
                    slot = new ClientMultilingualSlot(group.Name, ReferenceEquals(group.Secondary, property) ? 2 : ReferenceEquals(group.Tertiary, property) ? 3 : 1);
                    labelSource = group.Primary.Name;
                }

                (string labelKey, string? labelFallback) = LabelChains.Resolve(strings, LabelChains.Property(entity.Name, labelSource), NameHumanizer.Humanize(labelSource));

                // --- A blob reference: its kind's policy, as registered ---
                ClientBlobKindPolicy? blob = null;
                if (entity.BlobReferences.FirstOrDefault(reference => ReferenceEquals(reference.Property, property)) is BlobReferenceMetadata reference)
                {
                    blob = Blob(reference.Kind, blobKinds.Find(reference.Kind)?.Policy ?? reference.Policy);
                }

                return new ClientPropertyMetadata(
                    property.Name,
                    Json(wire, property.Name),
                    type,
                    queryable,
                    property.IsNullable,
                    property.IsUnique,
                    type == ClientDataType.Integer && ClientDataTypes.IsLong(property.ClrType),
                    labelKey,
                    labelFallback,
                    !property.ExcludedFromExcel,
                    Ownership(property.Ownership),
                    property.IsDefaulted,
                    type == ClientDataType.String ? property.MaxLength : null,
                    type == ClientDataType.Decimal ? property.Precision : null,
                    type == ClientDataType.Decimal ? property.Scale : null,
                    type == ClientDataType.Enum ? enumName : null,
                    slot,
                    entity.References.FirstOrDefault(candidate => ReferenceEquals(candidate.ForeignKey, property))?.Navigation,
                    blob);
            }

            /// <summary>Describes one column of a read-only root, which the server alone writes.</summary>
            /// <param name="entity">The root's name.</param>
            /// <param name="property">The column.</param>
            /// <returns>The description.</returns>
            private ClientPropertyMetadata ReadOnlyProperty(string entity, PropertyDescriptor property)
            {
                QueryexStoreType store = Store(property);
                ClientDataType type = ClientDataTypes.Of(store.Family);
                (string labelKey, string? labelFallback) = LabelChains.Resolve(strings, LabelChains.Property(entity, property.Name), NameHumanizer.Humanize(property.Name));
                return new ClientPropertyMetadata(
                    property.Name,
                    JsonNamingPolicy.CamelCase.ConvertName(property.Name),
                    type,
                    Queryable: true,
                    Nullable: !property.IsNotNull,
                    property.IsUnique,
                    Long: store.Family == QueryexStoreFamily.QxBigInt,
                    labelKey,
                    labelFallback,
                    Excel: true,
                    ClientPropertyOwnership.ServerOwned,
                    Defaulted: false,
                    MaxLength: type == ClientDataType.String ? store.Size : null,
                    Precision: type == ClientDataType.Decimal ? store.Size : null,
                    Scale: type == ClientDataType.Decimal ? store.Scale : null,
                    Enum: null,
                    Multilingual: null,
                    ForeignKeyOf: null,
                    Blob: null);
            }

            /// <summary>
            ///     Describes one navigation of an entity: a child's link to its owner, a sibling's
            ///     one-to-one row, or a plain reference.
            /// </summary>
            /// <param name="entity">The entity.</param>
            /// <param name="navigation">The navigation.</param>
            /// <param name="ownerKey">The child's parent key, for a child entity; null otherwise.</param>
            /// <param name="wire">The entity's wire names.</param>
            /// <returns>The description.</returns>
            private ClientNavigationMetadata Navigation(EntityMetadata entity, NavigationDescriptor navigation, string? ownerKey, IReadOnlyDictionary<string, string> wire)
            {
                string foreignKey = navigation.ForeignKey.Name;
                bool sibling = foreignKey == entity.Key.Name
                    && metadata.All.FirstOrDefault(candidate => candidate.Name == navigation.Target.Name) is EntityMetadata target
                    && EntityShapes.GetAttribute<SiblingAttribute>(target.ClrType) is { } declared
                    && declared.Owner.IsAssignableFrom(entity.ClrType)
                    && declared.Navigation == navigation.Name;
                (ClientNavigationKind kind, bool nullable) = foreignKey == ownerKey ? (ClientNavigationKind.Owner, false)
                    : sibling ? (ClientNavigationKind.Sibling, true)
                    : (ClientNavigationKind.Reference, !navigation.ForeignKey.IsNotNull);
                ClientNavigationMetadata described = Navigation(entity.Name, navigation, kind, nullable, sibling ? null : foreignKey);
                return described with { Json = Json(wire, navigation.Name) };
            }

            /// <summary>Describes one navigation, with its label resolved.</summary>
            /// <param name="entity">The entity's name.</param>
            /// <param name="navigation">The navigation.</param>
            /// <param name="kind">The navigation's kind.</param>
            /// <param name="nullable">Whether it may be empty.</param>
            /// <param name="labelForeignKey">The foreign key whose labels the navigation's label falls back to; null for a sibling, which holds none of its own.</param>
            /// <returns>The description.</returns>
            private ClientNavigationMetadata Navigation(string entity, NavigationDescriptor navigation, ClientNavigationKind kind, bool nullable, string? labelForeignKey)
            {
                (string labelKey, string? labelFallback) = LabelChains.Resolve(
                    strings,
                    LabelChains.Navigation(entity, navigation.Name, labelForeignKey),
                    NameHumanizer.Humanize(navigation.Name));
                return new ClientNavigationMetadata(
                    navigation.Name,
                    JsonNamingPolicy.CamelCase.ConvertName(navigation.Name),
                    navigation.ForeignKey.Name,
                    navigation.Target.Name,
                    kind,
                    nullable,
                    labelKey,
                    labelFallback);
            }

            /// <summary>Describes one child collection of an entity.</summary>
            /// <param name="entity">The entity.</param>
            /// <param name="collection">The collection.</param>
            /// <param name="wire">The entity's wire names.</param>
            /// <returns>The description.</returns>
            private ClientChildMetadata Child(EntityMetadata entity, ChildCollectionMetadata collection, IReadOnlyDictionary<string, string> wire)
            {
                (string labelKey, string? labelFallback) = LabelChains.Resolve(
                    strings,
                    LabelChains.Collection(entity.Name, collection.Property, collection.Child.Name),
                    LabelChains.EntityFallback(collection.Child.Name));
                return new ClientChildMetadata(
                    collection.Property,
                    Json(wire, collection.Property),
                    collection.Child.Name,
                    collection.ParentKey.Name,
                    collection.MaxCount,
                    labelKey,
                    labelFallback);
            }

            /// <summary>Describes one action of an entity's API, with its arguments.</summary>
            /// <param name="entity">The entity.</param>
            /// <param name="resource">The resource the action belongs to, whose keys label it.</param>
            /// <param name="action">The action.</param>
            /// <returns>The description.</returns>
            private ClientActionMetadata Action(EntityMetadata entity, string resource, ActionDescriptor action)
            {
                (string labelKey, string? labelFallback) = LabelChains.Resolve(strings, LabelChains.Action(resource, action.Name), LabelChains.ActionFallback(action.Name));
                Type? arguments = ActionArguments.TypeOf(action);
                ClientActionField[]? fields = arguments is null ? null : [.. ActionArguments.Members(arguments).Select(member => Field(resource, action, member))];

                // --- What each kind declares: an entity action's securable action and targets, an API action's securable ---
                (ClientActionKind kind, bool builtIn, string? securableAction, bool singleTarget, bool supportsFilter, ClientActionResult result) = action switch
                {
                    EntityActionDescriptor entityAction => (
                        ClientActionKind.Entity,
                        entityAction.IsBuiltIn,
                        entityAction.Action,
                        entityAction.SingleTarget,
                        entityAction.SupportsFilter,
                        entityAction.ResultType == typeof(EntitiesResult<>).MakeGenericType(entity.ClrType) ? ClientActionResult.Entities : ClientActionResult.Own),
                    ApiActionDescriptor apiAction => (
                        ClientActionKind.Api,
                        false,
                        apiAction.Securable?.Action,
                        false,
                        false,
                        apiAction.ResultType == typeof(void) ? ClientActionResult.None : ClientActionResult.Own),
                    _ => throw new InvalidOperationException($"The action {action.Name} of {resource} is of no kind the client metadata knows."),
                };

                return new ClientActionMetadata(
                    action.Name,
                    kind,
                    builtIn,
                    securableAction,
                    labelKey,
                    labelFallback,
                    action.Mutation,
                    action.Idempotent,
                    action.Destructive,
                    action.AllowWhenReadOnly,
                    singleTarget,
                    supportsFilter,
                    result,
                    arguments is null ? null : ActionArguments.Schema(arguments),
                    fields?.AsReadOnly());
            }

            /// <summary>Describes one argument of an action, as its generated dialog renders it.</summary>
            /// <param name="resource">The resource the action belongs to.</param>
            /// <param name="action">The action.</param>
            /// <param name="member">The argument.</param>
            /// <returns>The description.</returns>
            private ClientActionField Field(string resource, ActionDescriptor action, ActionArguments.Member member)
            {
                string field = LabelChains.FieldName(member.Property);
                (string labelKey, string? labelFallback) = LabelChains.Resolve(strings, LabelChains.ActionField(resource, action.Name, field), NameHumanizer.Humanize(field));
                return new ClientActionField(
                    member.Json.Name,
                    labelKey,
                    labelFallback,
                    ActionArguments.IsRequired(member),
                    ActionArguments.Value(member, enums));
            }

            /// <summary>A blob kind's policy as the client checks a file against it.</summary>
            /// <param name="kind">The kind.</param>
            /// <param name="policy">The kind's policy.</param>
            /// <returns>The client's policy.</returns>
            private static ClientBlobKindPolicy Blob(string kind, BlobKindPolicy policy)
            {
                ClientImagePolicy? image = policy.Image is ImagePolicy declared
                    ? new ClientImagePolicy(
                        declared.MaxDimension,
                        declared.Fit switch
                        {
                            ImageFit.Contain => ClientImageFit.Contain,
                            ImageFit.CoverSquare => ClientImageFit.CoverSquare,
                            _ => throw new InvalidOperationException($"The blob kind '{kind}' declares an image fit the client does not know."),
                        },
                        declared.Format switch
                        {
                            ImageFormat.WebP => ClientImageFormat.WebP,
                            ImageFormat.Jpeg => ClientImageFormat.Jpeg,
                            ImageFormat.Png => ClientImageFormat.Png,
                            _ => throw new InvalidOperationException($"The blob kind '{kind}' declares an image format the client does not know."),
                        })
                    : null;
                return new ClientBlobKindPolicy(
                    kind,
                    [.. policy.AllowedContentTypes],
                    policy.MaxSize,
                    image,
                    policy.Thumbnail is not null,
                    policy.ReadAccess == BlobReadAccess.AnyMember ? ClientBlobReadAccess.AnyMember : ClientBlobReadAccess.OwnerRead);
            }
        }
    }
}
