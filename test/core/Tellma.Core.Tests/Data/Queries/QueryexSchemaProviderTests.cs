// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Settings;
using Tellma.Core.Data.Queries;
using Tellma.Core.Queryex;
using Tellma.Core.Tests.Data.Batches;

namespace Tellma.Core.Tests.Data.Queries
{
    /// <summary>
    ///     The query schema of a composed model: one schema per multilingual shape, the mapped scalars
    ///     with their store types, the navigations (a sibling's optional), the tree node, the
    ///     withheld columns and the fingerprint.
    /// </summary>
    public sealed class QueryexSchemaProviderTests : IDisposable
    {
        /// <summary>The composition over the query model.</summary>
        private readonly BatchHarness _harness = new(compose: QueryModel.Compose);

        /// <summary>The provider.</summary>
        private QueryexSchemaProvider Provider => _harness.Services.GetRequiredService<QueryexSchemaProvider>();

        [Fact]
        public void The_interface_resolves_to_the_one_provider_and_each_shape_has_one_schema()
        {
            Assert.Same(Provider, _harness.Services.GetRequiredService<IQueryexSchemaProvider>());
            Assert.Same(Provider.GetSchema(MultilingualShape.All), Provider.GetSchema(MultilingualShape.All));
            Assert.Same(Provider.GetSchema(MultilingualShape.Primary), Provider.Map(MultilingualShape.Primary).Schema);
            Assert.NotSame(Provider.GetSchema(MultilingualShape.Primary), Provider.GetSchema(MultilingualShape.All));
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => Provider.GetSchema(0));
        }

        [Fact]
        public void The_three_shapes_differ_only_in_the_twins_they_admit()
        {
            string[] primary = Names(MultilingualShape.Primary, "qx.Gadget");
            string[] secondary = Names(MultilingualShape.PrimaryAndSecondary, "qx.Gadget");
            string[] all = Names(MultilingualShape.All, "qx.Gadget");

            Assert.DoesNotContain("Name2", primary);
            Assert.DoesNotContain("Name3", primary);
            string[] withSecond = [.. primary, "Name2"];
            string[] withBoth = [.. primary, "Name2", "Name3"];
            Assert.Equal(withSecond.Order(StringComparer.Ordinal), secondary.Order(StringComparer.Ordinal));
            Assert.Equal(withBoth.Order(StringComparer.Ordinal), all.Order(StringComparer.Ordinal));

            // Every entity is in every shape, with the same navigations.
            foreach (MultilingualShape shape in Enum.GetValues<MultilingualShape>())
            {
                QueryexSchema schema = Provider.GetSchema(shape);
                Assert.Equal(
                    Provider.GetSchema(MultilingualShape.Primary).Entities.Select(static entity => entity.Name),
                    schema.Entities.Select(static entity => entity.Name));
            }
        }

        [Fact]
        public void An_entity_is_named_and_sourced_by_its_metadata_and_keyed_by_its_id()
        {
            EntityDescriptor gadget = Entity(MultilingualShape.Primary, "qx.Gadget");

            Assert.Equal("[qx].[Gadgets]", gadget.Source);
            Assert.Equal("Id", gadget.Key.Name);
            Assert.True(gadget.Key.IsNotNull);
            Assert.Equal(QueryexStoreFamily.QxInt, gadget.Key.StoreType?.Family);
            Assert.Null(gadget.TreeNode);
        }

        [Fact]
        public void Mapped_scalars_carry_their_store_types_and_nullability()
        {
            EntityDescriptor gadget = Entity(MultilingualShape.Primary, "qx.Gadget");

            PropertyDescriptor price = Property(gadget, "Price");
            Assert.Equal(QueryexType.QxNumeric, price.Type);
            Assert.Equal(QueryexStoreType.QxDecimal(19, 4), price.StoreType);
            Assert.False(price.IsNotNull);

            PropertyDescriptor kind = Property(gadget, "Kind");
            Assert.Equal(QueryexType.QxString, kind.Type);
            Assert.Equal(QueryexStoreFamily.QxVarChar, kind.StoreType?.Family);
            Assert.True(kind.IsNotNull);
            Assert.False(Property(gadget, "OptionalKind").IsNotNull);

            Assert.Equal(QueryexStoreType.QxDate, Property(gadget, "DueOn").StoreType);
            Assert.Equal(QueryexStoreType.QxNVarChar(50), Property(gadget, "Code").StoreType);
            Assert.True(Property(gadget, "Code").IsUnique);
            Assert.Equal(QueryexStoreType.QxDateTimeOffset(7), Property(gadget, "CreatedAt").StoreType);
        }

        [Fact]
        public void Json_and_period_columns_are_withheld_and_the_map_keeps_the_json_columns()
        {
            QueryexSchemaMap map = Provider.Map(MultilingualShape.All);
            EntityMetadata gadgetMetadata = _harness.Services.GetRequiredService<IEntityMetadataProvider>().Get(typeof(Gadget));
            EntityDescriptor gadget = map.Descriptor(gadgetMetadata);

            Assert.DoesNotContain(gadget.Properties, static property => property.Name is "Settings" or "ValidFrom" or "ValidTo");
            Assert.Equal(["Settings"], map.JsonColumns(gadgetMetadata).Select(static property => property.Column));
            Assert.Same(gadgetMetadata, map.Metadata(gadget));
            Assert.Contains(map.Declared(gadgetMetadata), static property => property.Name == "Name3");
            Assert.DoesNotContain(map.Declared(gadgetMetadata), static property => property.Name == "Settings");
            Assert.DoesNotContain(Provider.Map(MultilingualShape.Primary).Declared(gadgetMetadata), static property => property.Name == "Name2");

            EntityMetadata part = _harness.Services.GetRequiredService<IEntityMetadataProvider>().Get(typeof(GadgetPart));
            Assert.Equal(["Settings"], map.JsonColumns(part).Select(static property => property.Column));
        }

        [Fact]
        public void Platform_secrets_are_withheld()
        {
            EntityDescriptor blob = Entity(MultilingualShape.Primary, "core.Blob");

            Assert.DoesNotContain(blob.Properties, static property => property.Name is "StorageKey" or "Sha256");
            Assert.Contains(blob.Properties, static property => property.Name == "ContentType");
        }

        [Fact]
        public void A_reference_is_a_navigation_by_its_foreign_key_and_a_sibling_an_optional_one_on_the_owner_id()
        {
            EntityDescriptor gadget = Entity(MultilingualShape.Primary, "qx.Gadget");

            NavigationDescriptor zone = Navigation(gadget, "Zone");
            Assert.Equal("qx.Zone", zone.Target.Name);
            Assert.Equal("ZoneId", zone.ForeignKey.Name);
            Assert.False(zone.IsOptional);

            NavigationDescriptor createdBy = Navigation(gadget, "CreatedBy");
            Assert.Equal("core.User", createdBy.Target.Name);

            NavigationDescriptor extra = Navigation(gadget, "Extra");
            Assert.Equal("qx.GadgetExtra", extra.Target.Name);
            Assert.Equal("Id", extra.ForeignKey.Name);
            Assert.True(extra.IsOptional);

            // A child names its parent through its parent key.
            Assert.Equal("GadgetId", Navigation(Entity(MultilingualShape.Primary, "qx.GadgetPart"), "Gadget").ForeignKey.Name);
        }

        [Fact]
        public void A_tree_declares_its_node_as_a_hierarchy_position()
        {
            EntityDescriptor zone = Entity(MultilingualShape.Primary, "qx.Zone");

            PropertyDescriptor node = Assert.IsType<PropertyDescriptor>(zone.TreeNode);
            Assert.Equal("Node", node.Column);
            Assert.Equal(QueryexType.QxHierarchyId, node.Type);
            Assert.Equal("qx.Zone", Navigation(zone, "Parent").Target.Name);
        }

        [Fact]
        public void The_um_al_qura_calendar_reads_the_month_table()
        {
            QueryexEngine engine = _harness.Services.GetRequiredService<QueryexEngine>();
            EntityDescriptor gadget = Entity(MultilingualShape.Primary, "qx.Gadget");

            QueryexResult<CompiledQuery> compiled = engine.CompileQuery(
                new QuerySpec { Root = gadget, Select = "year(DueOn, 'uq')" },
                new QueryCompilationOptions { Schema = Provider.GetSchema(MultilingualShape.Primary), QueryexVersion = QueryexLanguage.Version });

            Assert.True(compiled.Succeeded, string.Join(", ", compiled.Diagnostics.Select(static diagnostic => diagnostic.Code)));
            Assert.Contains("[core].[CalendarMonths]", compiled.Value.Sql, StringComparison.Ordinal);
        }

        [Fact]
        public void The_fingerprint_is_a_stable_digest_of_the_model()
        {
            string fingerprint = Provider.Fingerprint;

            Assert.Matches("^[0-9a-f]{64}$", fingerprint);
            QueryexSchemaProvider again = new(
                _harness.Services.GetRequiredService<IModel>(), _harness.Services.GetRequiredService<IEntityMetadataProvider>());
            Assert.Equal(fingerprint, again.Fingerprint);

            using BatchHarness other = new();
            Assert.NotEqual(fingerprint, other.Services.GetRequiredService<IQueryexSchemaProvider>().Fingerprint);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _harness.Dispose();
        }

        /// <summary>An entity of a shape's schema.</summary>
        /// <param name="shape">The shape.</param>
        /// <param name="name">The entity's name.</param>
        /// <returns>The entity.</returns>
        private EntityDescriptor Entity(MultilingualShape shape, string name)
        {
            return Provider.GetSchema(shape).FindEntity(name) ?? throw new InvalidOperationException($"No entity {name}.");
        }

        /// <summary>The property names of an entity under a shape.</summary>
        /// <param name="shape">The shape.</param>
        /// <param name="name">The entity's name.</param>
        /// <returns>The names, in declaration order.</returns>
        private string[] Names(MultilingualShape shape, string name)
        {
            return [.. Entity(shape, name).Properties.Select(static property => property.Name)];
        }

        /// <summary>A property of an entity.</summary>
        /// <param name="entity">The entity.</param>
        /// <param name="name">The property's name.</param>
        /// <returns>The property.</returns>
        private static PropertyDescriptor Property(EntityDescriptor entity, string name)
        {
            return entity.Properties.Single(property => property.Name == name);
        }

        /// <summary>A navigation of an entity.</summary>
        /// <param name="entity">The entity.</param>
        /// <param name="name">The navigation's name.</param>
        /// <returns>The navigation.</returns>
        private static NavigationDescriptor Navigation(EntityDescriptor entity, string name)
        {
            return entity.Navigations.Single(navigation => navigation.Name == name);
        }
    }
}
