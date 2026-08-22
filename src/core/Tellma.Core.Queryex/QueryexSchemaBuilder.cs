// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using Tellma.Core.Queryex.Binding;

namespace Tellma.Core.Queryex
{
    /// <summary>
    ///     Builds a <see cref="QueryexSchema" /> in two phases: declare entities, properties, and
    ///     navigations by name, then link and validate.
    /// </summary>
    /// <remarks>
    ///     Two phases rather than one because descriptors reference each other directly and cycles
    ///     are ordinary — a self-referencing hierarchy is the common case, not an edge one.
    ///     Everything this type rejects is a host bug rather than user input, so it throws; the
    ///     engine's diagnostics stay purely about the expressions people write.
    /// </remarks>
    public sealed class QueryexSchemaBuilder
    {
        /// <summary>The entity builders, in registration order.</summary>
        private readonly List<EntityBuilder> _entities = [];

        /// <summary>Lookup by logical entity name, for the uniqueness check and for linking.</summary>
        private readonly Dictionary<string, EntityBuilder> _entitiesByName =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>The model version discriminator.</summary>
        private readonly string _version;

        /// <summary>Whether <see cref="Build" /> has already run.</summary>
        private bool _built;

        /// <summary>Starts a new schema.</summary>
        /// <param name="version">
        ///     An opaque discriminator that must change whenever anything about the model changes.
        ///     It participates in every cache key, so a stale one keeps stale compilations alive.
        /// </param>
        /// <exception cref="ArgumentException"><paramref name="version" /> is null or whitespace.</exception>
        public QueryexSchemaBuilder(string version)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(version);

            _version = version;
        }

        /// <summary>Declares an entity.</summary>
        /// <param name="name">The logical entity name, unique across the schema case-insensitively.</param>
        /// <param name="source">
        ///     The physical source: a bracket-quoted table or view name, optionally schema- and
        ///     database-qualified, such as <c>[gl].[Invoices]</c>.
        /// </param>
        /// <returns>A builder for the entity's members.</returns>
        /// <exception cref="ArgumentException">A name is empty, malformed, or already taken.</exception>
        /// <exception cref="InvalidOperationException">The schema has already been built.</exception>
        public EntityBuilder Entity(string name, string source)
        {
            ThrowIfBuilt();
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            ArgumentException.ThrowIfNullOrWhiteSpace(source);
            ValidateLogicalName(name, nameof(name));
            ValidateSource(source);

            if (_entitiesByName.ContainsKey(name))
            {
                // Case-insensitively, because resolution is: two entities differing only in case
                // would make every path through them ambiguous.
                throw new ArgumentException(
                    Invariant($"An entity named '{name}' is already declared."),
                    nameof(name));
            }

            EntityBuilder builder = new(name, source);
            _entities.Add(builder);
            _entitiesByName.Add(name, builder);
            return builder;
        }

        /// <summary>Links every declared name to its descriptor, validates, and freezes the schema.</summary>
        /// <returns>The immutable schema.</returns>
        /// <exception cref="InvalidOperationException">
        ///     The declared model is not coherent: an entity has no key, a navigation names an
        ///     entity or a foreign key that does not exist, a foreign key does not agree in type
        ///     with the key it references, or a hierarchy node is not a hierarchy-node column.
        /// </exception>
        public QueryexSchema Build()
        {
            ThrowIfBuilt();
            _built = true;

            foreach (EntityBuilder entity in _entities)
            {
                entity.Link(_entitiesByName);
            }

            // Foreign keys are checked only after every entity has a key, so that the message names
            // the real problem rather than a missing key on the far side of a navigation.
            foreach (EntityBuilder entity in _entities)
            {
                entity.LinkNavigations(_entitiesByName);
            }

            List<EntityDescriptor> descriptors = new(_entities.Count);
            foreach (EntityBuilder entity in _entities)
            {
                entity.Descriptor.BuildLookups();
                descriptors.Add(entity.Descriptor);
            }

            return new QueryexSchema(_version, descriptors);
        }

        /// <summary>Formats a message with the invariant culture.</summary>
        /// <param name="message">The interpolated message.</param>
        /// <returns>The formatted message.</returns>
        internal static string Invariant(FormattableString message)
        {
            return message.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        ///     Rejects a logical name that could never be written in an expression, or that carries
        ///     characters no diagnostic could render.
        /// </summary>
        /// <param name="name">The name to check.</param>
        /// <param name="parameterName">The caller's parameter name, for the exception.</param>
        internal static void ValidateLogicalName(string name, string parameterName)
        {
            foreach (char character in name)
            {
                // A bracketed identifier escapes every reserved word and every punctuation
                // character, but it cannot contain its own closing bracket, and a control
                // character would corrupt any diagnostic that quoted it back.
                if (character == ']' || IsForbiddenInName(character))
                {
                    throw new ArgumentException(
                        Invariant($"The logical name '{name}' contains a character that cannot be written in an expression."),
                        parameterName);
                }
            }
        }

        /// <summary>
        ///     Rejects a physical source that is not a bracket-quoted, optionally qualified name.
        /// </summary>
        /// <param name="source">The source to check.</param>
        /// <remarks>
        ///     The source is emitted verbatim, so it is one of only two pieces of host-supplied text
        ///     that ever reach SQL. Validating its shape here means the emitter never has to reason
        ///     about what a host might have put in it.
        /// </remarks>
        internal static void ValidateSource(string source)
        {
            int index = 0;
            int parts = 0;
            while (index < source.Length)
            {
                if (parts > 0)
                {
                    if (source[index] != '.')
                    {
                        throw SourceMalformed(source);
                    }

                    index++;
                }

                if (index >= source.Length || source[index] != '[')
                {
                    throw SourceMalformed(source);
                }

                // A doubled bracket is the backend's own escape for a bracket inside a name, so
                // scanning steps over it rather than treating the first half as the close. A part
                // still cannot end its own quoting: the close is the first bracket not doubled.
                int close = FindClose(source, index);
                if (close < 0 || close == index + 1)
                {
                    throw SourceMalformed(source);
                }

                index = close + 1;
                parts++;
            }

            if (parts is < 1 or > 3)
            {
                throw SourceMalformed(source);
            }
        }

        /// <summary>Finds where one bracketed part of a source ends.</summary>
        /// <param name="source">The whole source.</param>
        /// <param name="index">Where the part's opening bracket is.</param>
        /// <returns>The closing bracket's index, or -1 when the part never closes.</returns>
        /// <exception cref="ArgumentException">The part carries a character a name may not.</exception>
        private static int FindClose(string source, int index)
        {
            int scan = index + 1;
            while (scan < source.Length)
            {
                if (source[scan] != ']')
                {
                    if (IsForbiddenInName(source[scan]))
                    {
                        throw SourceMalformed(source);
                    }

                    scan++;
                    continue;
                }

                if (scan + 1 < source.Length && source[scan + 1] == ']')
                {
                    scan += 2;
                    continue;
                }

                return scan;
            }

            return -1;
        }

        /// <summary>Whether a character has no place in a name that reaches emitted SQL.</summary>
        /// <param name="character">The character.</param>
        /// <returns>True when it does not.</returns>
        /// <remarks>
        ///     The two separator characters are not control characters as the platform classifies
        ///     them, but a reader and a diff treat them as line breaks — and the writer promises that
        ///     emitted SQL uses one line ending and only one.
        /// </remarks>
        internal static bool IsForbiddenInName(char character)
        {
            return char.IsControl(character) || character is '\u2028' or '\u2029';
        }

        /// <summary>Builds the exception for a malformed physical source.</summary>
        /// <param name="source">The offending source.</param>
        /// <returns>The exception to throw.</returns>
        private static ArgumentException SourceMalformed(string source)
        {
            return new ArgumentException(
                Invariant($"The source '{source}' is not a bracket-quoted name of one to three parts, such as [gl].[Invoices]."),
                nameof(source));
        }

        /// <summary>Rejects further changes once the schema has been built.</summary>
        private void ThrowIfBuilt()
        {
            if (_built)
            {
                throw new InvalidOperationException(
                    "The schema has already been built. Build a new schema rather than changing a live one.");
            }
        }

        /// <summary>Declares one entity's properties and navigations.</summary>
        public sealed class EntityBuilder
        {
            /// <summary>The declared navigations, paired with the names they still have to resolve.</summary>
            private readonly List<PendingNavigation> _navigations = [];

            /// <summary>The declared properties, in declaration order.</summary>
            private readonly List<PropertyDescriptor> _properties = [];

            /// <summary>Every logical member name declared so far, properties and navigations alike.</summary>
            private readonly HashSet<string> _memberNames = new(StringComparer.OrdinalIgnoreCase);

            /// <summary>Every physical column name declared so far.</summary>
            private readonly HashSet<string> _columns = new(StringComparer.OrdinalIgnoreCase);

            /// <summary>The logical name of the key property, once declared.</summary>
            private string? _keyName;

            /// <summary>The logical name of the hierarchy-node property, when the entity is hierarchical.</summary>
            private string? _treeNodeName;

            /// <summary>Initializes the builder.</summary>
            /// <param name="name">The logical entity name.</param>
            /// <param name="source">The physical source.</param>
            internal EntityBuilder(string name, string source)
            {
                Descriptor = new EntityDescriptor(name, source);
            }

            /// <summary>The descriptor this builder fills in.</summary>
            internal EntityDescriptor Descriptor { get; }

            /// <summary>Declares the entity's single-column surrogate key.</summary>
            /// <param name="name">The logical property name.</param>
            /// <param name="type">The key's type, which must be equatable.</param>
            /// <param name="column">The physical column name, unquoted.</param>
            /// <param name="storeType">The physical column type, or null for the default mapping.</param>
            /// <returns>This builder, for chaining.</returns>
            /// <exception cref="ArgumentException">A name is empty, malformed, or already taken.</exception>
            /// <exception cref="InvalidOperationException">A key is already declared.</exception>
            public EntityBuilder Key(
                string name,
                QueryexType type,
                string column,
                QueryexStoreType? storeType = null)
            {
                if (_keyName is not null)
                {
                    throw new InvalidOperationException(
                        Invariant($"Entity '{Descriptor.Name}' already declares a key, '{_keyName}'."));
                }

                if (!QueryexTypeFacts.IsEquatable(type))
                {
                    // A key anchors every join condition and the tiebreaker that makes a page
                    // reproducible. A type the language cannot compare can do neither.
                    throw new ArgumentException(
                        Invariant($"Entity '{Descriptor.Name}' cannot key on a {type} value, which the language cannot compare."),
                        nameof(type));
                }

                // Declared unique whatever the host passed: a key is unique by definition, and the
                // hierarchy predicates read that flag to decide whether a lookup can match twice.
                Property(name, type, column, isNotNull: true, isUnique: true, storeType);
                _keyName = name;
                return this;
            }

            /// <summary>Declares a scalar property.</summary>
            /// <param name="name">The logical property name, unique among the entity's members.</param>
            /// <param name="type">The property's type in the language.</param>
            /// <param name="column">The physical column name, unquoted and unique within the entity.</param>
            /// <param name="isNotNull">Whether the column cannot hold an absent value.</param>
            /// <param name="isUnique">Whether the column is backed by a unique constraint or index.</param>
            /// <param name="storeType">The physical column type, or null for the default mapping.</param>
            /// <returns>This builder, for chaining.</returns>
            /// <exception cref="ArgumentException">
            ///     A name is empty or malformed, a name or column is already taken, or the store type
            ///     does not belong to <paramref name="type" />.
            /// </exception>
            public EntityBuilder Property(
                string name,
                QueryexType type,
                string column,
                bool isNotNull = false,
                bool isUnique = false,
                QueryexStoreType? storeType = null)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(name);
                ArgumentException.ThrowIfNullOrWhiteSpace(column);
                ValidateLogicalName(name, nameof(name));
                AddMemberName(name, nameof(name));
                AddColumn(column);

                if (storeType is QueryexStoreType declared
                    && !QueryexTypeFacts.Admits(type, declared.Family))
                {
                    throw new ArgumentException(
                        Invariant($"Property '{Descriptor.Name}.{name}' declares a store type of family {declared.Family}, which cannot hold a {type} value."),
                        nameof(storeType));
                }

                _properties.Add(new PropertyDescriptor(name, column, type, isNotNull, isUnique, storeType));
                return this;
            }

            /// <summary>Marks an already-declared property as the entity's hierarchy node.</summary>
            /// <param name="name">The logical name of a hierarchy-node property.</param>
            /// <returns>This builder, for chaining.</returns>
            /// <exception cref="ArgumentException"><paramref name="name" /> is empty.</exception>
            /// <exception cref="InvalidOperationException">A hierarchy node is already declared.</exception>
            public EntityBuilder TreeNode(string name)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(name);

                if (_treeNodeName is not null)
                {
                    throw new InvalidOperationException(
                        Invariant($"Entity '{Descriptor.Name}' already declares a hierarchy node, '{_treeNodeName}'."));
                }

                _treeNodeName = name;
                return this;
            }

            /// <summary>Declares a many-to-one navigation to another entity.</summary>
            /// <param name="name">The logical navigation name, unique among the entity's members.</param>
            /// <param name="target">The logical name of the target entity.</param>
            /// <param name="foreignKey">
            ///     The logical name of the foreign-key property on this entity. It must be declared
            ///     separately, like any other property, and must agree in type with the target's key.
            /// </param>
            /// <returns>This builder, for chaining.</returns>
            /// <exception cref="ArgumentException">A name is empty, malformed, or already taken.</exception>
            public EntityBuilder Navigation(string name, string target, string foreignKey)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(name);
                ArgumentException.ThrowIfNullOrWhiteSpace(target);
                ArgumentException.ThrowIfNullOrWhiteSpace(foreignKey);
                ValidateLogicalName(name, nameof(name));
                AddMemberName(name, nameof(name));

                _navigations.Add(new PendingNavigation(new NavigationDescriptor(name), target, foreignKey));
                return this;
            }

            /// <summary>Resolves the entity's own members and validates them.</summary>
            /// <param name="entities">Every entity builder, by logical name.</param>
            internal void Link(IReadOnlyDictionary<string, EntityBuilder> entities)
            {
                _ = entities;

                if (_keyName is null)
                {
                    throw new InvalidOperationException(
                        Invariant($"Entity '{Descriptor.Name}' declares no key. Every entity needs one: it anchors join conditions and the paging tiebreaker."));
                }

                Descriptor.Properties = _properties;
                Descriptor.Navigations = [.. _navigations.Select(static pending => pending.Descriptor)];
                Descriptor.BuildLookups();

                PropertyDescriptor key = Descriptor.FindProperty(_keyName)!;
                if (!QueryexTypeFacts.IsEquatable(key.Type))
                {
                    // A key that cannot be equated could anchor neither a join condition nor the
                    // paging tiebreaker, so the failure would surface far from its cause.
                    throw new InvalidOperationException(
                        Invariant($"The key of entity '{Descriptor.Name}' has type {key.Type}, which cannot be compared for equality."));
                }

                Descriptor.Key = key;

                if (_treeNodeName is not null)
                {
                    PropertyDescriptor? node = Descriptor.FindProperty(_treeNodeName)
                        ?? throw new InvalidOperationException(
                            Invariant($"Entity '{Descriptor.Name}' names '{_treeNodeName}' as its hierarchy node, but declares no such property."));

                    if (node.Type != QueryexType.QxHierarchyId)
                    {
                        throw new InvalidOperationException(
                            Invariant($"The hierarchy node '{Descriptor.Name}.{_treeNodeName}' has type {node.Type} rather than a hierarchy-node type."));
                    }

                    Descriptor.TreeNode = node;
                }
            }

            /// <summary>Resolves the entity's navigations against the linked entities.</summary>
            /// <param name="entities">Every entity builder, by logical name.</param>
            internal void LinkNavigations(IReadOnlyDictionary<string, EntityBuilder> entities)
            {
                foreach (PendingNavigation pending in _navigations)
                {
                    if (!entities.TryGetValue(pending.TargetName, out EntityBuilder? target))
                    {
                        throw new InvalidOperationException(
                            Invariant($"Navigation '{Descriptor.Name}.{pending.Descriptor.Name}' targets entity '{pending.TargetName}', which the schema does not declare."));
                    }

                    PropertyDescriptor? foreignKey = Descriptor.FindProperty(pending.ForeignKeyName)
                        ?? throw new InvalidOperationException(
                            Invariant($"Navigation '{Descriptor.Name}.{pending.Descriptor.Name}' names foreign key '{pending.ForeignKeyName}', which entity '{Descriptor.Name}' does not declare as a property."));

                    if (foreignKey.Type != target.Descriptor.Key.Type)
                    {
                        throw new InvalidOperationException(
                            Invariant($"Foreign key '{Descriptor.Name}.{pending.ForeignKeyName}' has type {foreignKey.Type}, but the key of '{target.Descriptor.Name}' has type {target.Descriptor.Key.Type}."));
                    }

                    pending.Descriptor.Target = target.Descriptor;
                    pending.Descriptor.ForeignKey = foreignKey;
                }
            }

            /// <summary>Records a logical member name, rejecting a collision.</summary>
            /// <param name="name">The name to record.</param>
            /// <param name="parameterName">The caller's parameter name, for the exception.</param>
            private void AddMemberName(string name, string parameterName)
            {
                if (!_memberNames.Add(name))
                {
                    // Properties and navigations share one namespace because a path segment is
                    // resolved against both, so a collision would make the path ambiguous.
                    throw new ArgumentException(
                        Invariant($"Entity '{Descriptor.Name}' already declares a member named '{name}'."),
                        parameterName);
                }
            }

            /// <summary>Records a physical column name, rejecting a collision.</summary>
            /// <param name="column">The column to record.</param>
            private void AddColumn(string column)
            {
                foreach (char character in column)
                {
                    if (char.IsControl(character))
                    {
                        throw new ArgumentException(
                            Invariant($"The column name '{column}' on entity '{Descriptor.Name}' contains a control character."),
                            nameof(column));
                    }
                }

                if (!_columns.Add(column))
                {
                    throw new ArgumentException(
                        Invariant($"Entity '{Descriptor.Name}' already maps a member to column '{column}'."),
                        nameof(column));
                }
            }

            /// <summary>A navigation whose target and foreign key are still names.</summary>
            /// <param name="Descriptor">The descriptor to fill in.</param>
            /// <param name="TargetName">The logical name of the target entity.</param>
            /// <param name="ForeignKeyName">The logical name of the foreign-key property.</param>
            private sealed record PendingNavigation(
                NavigationDescriptor Descriptor,
                string TargetName,
                string ForeignKeyName);
        }
    }
}
