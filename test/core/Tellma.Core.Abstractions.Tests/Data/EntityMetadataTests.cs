// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Data;

namespace Tellma.Core.Abstractions.Tests.Data
{
    /// <summary>The entity metadata value: what its constructor refuses, and its lazily built parts.</summary>
    public class EntityMetadataTests
    {
        [Fact]
        public void The_parts_naming_other_entities_are_built_once_on_first_read_given_the_metadata()
        {
            int built = 0;
            EntityMetadata? seen = null;
            EntityMetadata metadata = Create(
                Property("Id"),
                [Property("Id")],
                references: self =>
                {
                    built++;
                    seen = self;
                    return [];
                });

            Assert.Equal(0, built);
            Assert.Empty(metadata.References);
            Assert.Empty(metadata.References);
            Assert.Equal(1, built);
            Assert.Same(metadata, seen);
            Assert.Equal("fingerprint", metadata.SchemaFingerprint);
            Assert.Equal("fixture.Row", metadata.ToString());
        }

        [Fact]
        public void A_key_outside_the_properties_is_refused()
        {
            ArgumentException exception = Assert.Throws<ArgumentException>(() => Create(Property("Id"), [Property("Name")]));
            Assert.Equal("key", exception.ParamName);
        }

        [Fact]
        public void An_empty_property_list_is_refused()
        {
            _ = Assert.Throws<ArgumentException>(() => Create(Property("Id"), []));
        }

        [Fact]
        public void A_reset_with_nothing_to_restore_creates_no_instance()
        {
            PropertyMetadata id = Property("Id");
            EntityMetadata metadata = Create(id, [id], createInstance: static () => throw new InvalidOperationException("Not expected."));

            metadata.ResetServerOwned(new Row());
            metadata.ResetDerived(new Row());
        }

        /// <summary>An editable property of <see cref="Row" /> that reads and writes nothing.</summary>
        /// <param name="name">The property's name.</param>
        /// <returns>The property.</returns>
        private static PropertyMetadata Property(string name)
        {
            return new PropertyMetadata(
                name, name, typeof(int), PropertyOwnership.Editable, false, false, null, null, null, null, null, null, false, null, false, false,
                static _ => null, static (_, _) => { });
        }

        /// <summary>Metadata of <see cref="Row" /> with the given key and properties.</summary>
        /// <param name="key">The key.</param>
        /// <param name="properties">The properties.</param>
        /// <param name="createInstance">The factory; a new <see cref="Row" /> when null.</param>
        /// <param name="references">The references' factory; none when null.</param>
        /// <returns>The metadata.</returns>
        private static EntityMetadata Create(
            PropertyMetadata key,
            IReadOnlyList<PropertyMetadata> properties,
            Func<object>? createInstance = null,
            Func<EntityMetadata, IReadOnlyList<ReferenceMetadata>>? references = null)
        {
            return new EntityMetadata(
                typeof(Row), "fixture.Row", new TableName("fixture", "Rows"), null, key, properties, [], [], [],
                isTopLevel: false, isTemporal: false, isActivatable: false, isJobEntity: false, isCacheable: false, tree: null, blobReferences: [],
                displayName: new DisplayNameMetadata(key, null, null),
                createInstance ?? (static () => new Row()),
                children: static _ => [],
                references ?? (static _ => []),
                schemaFingerprint: static _ => "fingerprint");
        }

        /// <summary>A row class the metadata describes.</summary>
        private sealed class Row;
    }
}
