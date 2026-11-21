// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Entities;
using Tellma.Core.Abstractions.Internal;

namespace Tellma.Core.Abstractions.Data
{
    /// <summary>
    ///     The runtime description of one mapped entity leaf, built once at startup from the tenant model
    ///     and the entity class and validated by the startup gate: its table, its properties and who owns
    ///     each, its child collections, references, natural keys, multilingual groups, unique indexes,
    ///     tree columns, blob references and display-name rule.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The emitter, the materializer, the id allocator, the pipeline, the Excel codec and the
    ///         capability recipes all read an entity through its metadata, which
    ///         <see cref="IEntityMetadataProvider" /> serves; nothing reflects over entity classes a second
    ///         time.
    ///     </para>
    ///     <para>
    ///         Immutable once built. <see cref="Children" />, <see cref="References" /> and
    ///         <see cref="SchemaFingerprint" /> name other entities' metadata, which may name this one in
    ///         turn (a tree's parent is its own entity), so they come from factories the constructor
    ///         takes and run once, on first read; the platform reads each before the gate passes.
    ///     </para>
    /// </remarks>
    public sealed class EntityMetadata
    {
        private readonly Func<object> _createInstance;
        private readonly PropertyMetadata[] _serverOwned;
        private readonly PropertyMetadata[] _derived;
        private readonly Lazy<IReadOnlyList<ChildCollectionMetadata>> _children;
        private readonly Lazy<IReadOnlyList<ReferenceMetadata>> _references;
        private readonly Lazy<string> _schemaFingerprint;

        /// <summary>Describes one entity leaf.</summary>
        /// <param name="clrType">The leaf's CLR type.</param>
        /// <param name="name">The entity's name, <c>&lt;schema&gt;.&lt;default leaf's class name&gt;</c>.</param>
        /// <param name="table">The leaf's table.</param>
        /// <param name="tableTypePhysicalName">The physical name of the table's row-image type, or <see langword="null" /> when the entity has none.</param>
        /// <param name="key">The primary-key property.</param>
        /// <param name="properties">Every mapped scalar property, in declaration order, base classes first.</param>
        /// <param name="naturalKeys">The natural keys, in rank order.</param>
        /// <param name="multilingualGroups">The multilingual groups, in anchor declaration order.</param>
        /// <param name="uniqueIndexes">The table's unique indexes.</param>
        /// <param name="isTopLevel">Whether the entity derives from <see cref="TopLevelEntity{TKey}" />.</param>
        /// <param name="isTemporal">Whether the table is system-versioned.</param>
        /// <param name="isActivatable">Whether the entity implements <see cref="IActivatable" />.</param>
        /// <param name="isJobEntity">Whether the entity implements <see cref="IJobEntity" />.</param>
        /// <param name="isCacheable">Whether the entity declares <see cref="CacheableAttribute" />.</param>
        /// <param name="tree">The tree columns of a tree entity; null otherwise.</param>
        /// <param name="blobReferences">The blob-reference columns.</param>
        /// <param name="displayName">The display-name rule.</param>
        /// <param name="createInstance">Creates a fresh instance of the leaf, whose values the reset methods restore.</param>
        /// <param name="children">Builds the child collections, given this metadata; run once, on first read.</param>
        /// <param name="references">Builds the references, given this metadata; run once, on first read.</param>
        /// <param name="schemaFingerprint">Computes the schema fingerprint, given this metadata; run once, on first read.</param>
        /// <exception cref="ArgumentNullException">A reference argument is null.</exception>
        /// <exception cref="ArgumentException">A list holds a null entry, or the key is not among the properties.</exception>
        public EntityMetadata(
            Type clrType,
            string name,
            TableName table,
            string? tableTypePhysicalName,
            PropertyMetadata key,
            IReadOnlyList<PropertyMetadata> properties,
            IReadOnlyList<NaturalKeyMetadata> naturalKeys,
            IReadOnlyList<MultilingualGroup> multilingualGroups,
            IReadOnlyList<UniqueIndexMetadata> uniqueIndexes,
            bool isTopLevel,
            bool isTemporal,
            bool isActivatable,
            bool isJobEntity,
            bool isCacheable,
            TreeMetadata? tree,
            IReadOnlyList<BlobReferenceMetadata> blobReferences,
            DisplayNameMetadata displayName,
            Func<object> createInstance,
            Func<EntityMetadata, IReadOnlyList<ChildCollectionMetadata>> children,
            Func<EntityMetadata, IReadOnlyList<ReferenceMetadata>> references,
            Func<EntityMetadata, string> schemaFingerprint)
        {
            ArgumentNullException.ThrowIfNull(clrType);
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            ArgumentNullException.ThrowIfNull(table);
            ArgumentNullException.ThrowIfNull(key);
            ArgumentNullException.ThrowIfNull(displayName);
            ArgumentNullException.ThrowIfNull(createInstance);
            ArgumentNullException.ThrowIfNull(children);
            ArgumentNullException.ThrowIfNull(references);
            ArgumentNullException.ThrowIfNull(schemaFingerprint);

            // --- The entity's own parts, copied so a caller's lists cannot change them later ---
            ClrType = clrType;
            Name = name;
            Table = table;
            TableTypePhysicalName = tableTypePhysicalName;
            Properties = ReadOnlyCopies.OfEntries(properties, requireOne: true, nameof(properties));
            Key = Properties.Contains(key) ? key : throw new ArgumentException("The key must be one of the properties.", nameof(key));
            NaturalKeys = ReadOnlyCopies.OfEntries(naturalKeys, requireOne: false, nameof(naturalKeys));
            MultilingualGroups = ReadOnlyCopies.OfEntries(multilingualGroups, requireOne: false, nameof(multilingualGroups));
            UniqueIndexes = ReadOnlyCopies.OfEntries(uniqueIndexes, requireOne: false, nameof(uniqueIndexes));
            IsTopLevel = isTopLevel;
            IsTemporal = isTemporal;
            IsActivatable = isActivatable;
            IsJobEntity = isJobEntity;
            IsCacheable = isCacheable;
            Tree = tree;
            BlobReferences = ReadOnlyCopies.OfEntries(blobReferences, requireOne: false, nameof(blobReferences));
            DisplayName = displayName;

            // --- What the reset methods restore, fixed once ---
            _createInstance = createInstance;
            // The key is server-owned, but the id allocator owns it: a temporary (negative) id must
            // survive the reset, since the rows that reference it are rewritten after it.
            _serverOwned = [.. Properties.Where(property => property.Ownership == PropertyOwnership.ServerOwned && !ReferenceEquals(property, Key))];
            _derived = [.. Properties.Where(static property => property.Ownership == PropertyOwnership.Derived)];

            // --- The parts that name other entities, built on first read ---
            _children = new(() => ReadOnlyCopies.OfEntries(children(this), requireOne: false, nameof(children)), LazyThreadSafetyMode.ExecutionAndPublication);
            _references = new(() => ReadOnlyCopies.OfEntries(references(this), requireOne: false, nameof(references)), LazyThreadSafetyMode.ExecutionAndPublication);
            _schemaFingerprint = new(() => schemaFingerprint(this), LazyThreadSafetyMode.ExecutionAndPublication);
        }

        /// <summary>The leaf's CLR type.</summary>
        public Type ClrType { get; }

        /// <summary>
        ///     The entity's name: <c>&lt;schema&gt;.&lt;CLR name of the pack's default leaf&gt;</c>, such as
        ///     <c>core.User</c> or <c>gl.Center</c>. A distribution's leaf keeps its default's name. It is the
        ///     query entity's name, the securable resource, the key of related-entity sets and the suffix of
        ///     the entity's version tag.
        /// </summary>
        public string Name { get; }

        /// <summary>The leaf's table.</summary>
        public TableName Table { get; }

        /// <summary>
        ///     The physical name of the table's row-image type, <c>&lt;schema&gt;.&lt;Table&gt;List_&lt;hash8&gt;</c>,
        ///     the only name table-valued parameters of the entity's rows are bound by; null for an entity
        ///     with no table type, which no save writes.
        /// </summary>
        public string? TableTypePhysicalName { get; }

        /// <summary>The primary-key property, <c>Id</c>.</summary>
        public PropertyMetadata Key { get; }

        /// <summary>
        ///     Every mapped scalar property in declaration order, base classes first. Shadow columns (a
        ///     tree's node, a temporal table's periods) are not properties.
        /// </summary>
        public IReadOnlyList<PropertyMetadata> Properties { get; }

        /// <summary>The child collections, in declaration order.</summary>
        public IReadOnlyList<ChildCollectionMetadata> Children => _children.Value;

        /// <summary>Every many-to-one foreign key of the entity, in the order of its properties.</summary>
        public IReadOnlyList<ReferenceMetadata> References => _references.Value;

        /// <summary>
        ///     The natural keys, in rank order: the declared ones, then the ones inferred from unique string
        ///     columns. Empty when the entity has none, so its surrogate id is its export row key.
        /// </summary>
        public IReadOnlyList<NaturalKeyMetadata> NaturalKeys { get; }

        /// <summary>The first natural key's property, or <see langword="null" /> when the entity has none.</summary>
        public PropertyMetadata? NaturalKey => NaturalKeys.Count > 0 ? NaturalKeys[0].Property : null;

        /// <summary>The multilingual groups, in anchor declaration order.</summary>
        public IReadOnlyList<MultilingualGroup> MultilingualGroups { get; }

        /// <summary>The table's unique indexes, by name.</summary>
        public IReadOnlyList<UniqueIndexMetadata> UniqueIndexes { get; }

        /// <summary>Whether the entity derives from <see cref="TopLevelEntity{TKey}" /> and carries the audit columns.</summary>
        public bool IsTopLevel { get; }

        /// <summary>Whether the table is system-versioned.</summary>
        public bool IsTemporal { get; }

        /// <summary>Whether the entity implements <see cref="IActivatable" />.</summary>
        public bool IsActivatable { get; }

        /// <summary>Whether the entity implements <see cref="IJobEntity" />.</summary>
        public bool IsJobEntity { get; }

        /// <summary>Whether the entity declares <see cref="CacheableAttribute" />.</summary>
        public bool IsCacheable { get; }

        /// <summary>The tree columns of a tree entity; null for every other entity.</summary>
        public TreeMetadata? Tree { get; }

        /// <summary>The blob-reference columns, in declaration order.</summary>
        public IReadOnlyList<BlobReferenceMetadata> BlobReferences { get; }

        /// <summary>The display-name rule, with its source resolved.</summary>
        public DisplayNameMetadata DisplayName { get; }

        /// <summary>
        ///     The lower-case hex SHA-256 of the entity's editable shape: the name, column, store type,
        ///     nullability and ownership of every editable and write-once property, and of every child
        ///     collection's, recursively. It changes exactly when what an import may write changes, so an
        ///     export and an import of different shapes are told apart.
        /// </summary>
        public string SchemaFingerprint => _schemaFingerprint.Value;

        /// <summary>
        ///     Restores every server-owned property of an instance but the key to its value on a freshly
        ///     constructed instance. The pipeline calls it on new rows before the emitter reads them. The
        ///     key, which the id allocator owns, is kept, so a temporary id and the references to it
        ///     survive until the allocator rewrites them.
        /// </summary>
        /// <param name="entity">An instance of the leaf.</param>
        /// <exception cref="ArgumentNullException"><paramref name="entity" /> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="entity" /> is not an instance of the leaf.</exception>
        public void ResetServerOwned(object entity)
        {
            Reset(entity, _serverOwned);
        }

        /// <summary>
        ///     Restores every derived property of an instance to its value on a freshly constructed
        ///     instance. The pipeline calls it on every row, new and existing, before its hooks fill the
        ///     derived values, so a hook that sets nothing persists the default and never the client's value.
        /// </summary>
        /// <param name="entity">An instance of the leaf.</param>
        /// <exception cref="ArgumentNullException"><paramref name="entity" /> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="entity" /> is not an instance of the leaf.</exception>
        public void ResetDerived(object entity)
        {
            Reset(entity, _derived);
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return Name;
        }

        /// <summary>Copies the given properties of a fresh instance onto an instance of the leaf.</summary>
        /// <param name="entity">The instance.</param>
        /// <param name="properties">The properties to restore.</param>
        private void Reset(object entity, PropertyMetadata[] properties)
        {
            ArgumentNullException.ThrowIfNull(entity);
            if (!ClrType.IsInstanceOfType(entity))
            {
                throw new ArgumentException($"The entity is a {entity.GetType().Name}, not an instance of {ClrType.Name}, the leaf of {Name}.", nameof(entity));
            }

            if (properties.Length == 0)
            {
                return;
            }

            // A fresh instance per call: a default a constructor computes (or a mutable one) is never
            // shared between rows.
            object fresh = _createInstance();
            foreach (PropertyMetadata property in properties)
            {
                property.Setter(entity, property.Getter(fresh));
            }
        }
    }
}
