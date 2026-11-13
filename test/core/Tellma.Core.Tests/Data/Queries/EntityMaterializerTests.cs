// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using System.Data;
using System.Data.Common;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Settings;
using Tellma.Core.Data.Queries;
using Tellma.Core.Queryex;
using Tellma.Core.Tests.Data.Batches;

namespace Tellma.Core.Tests.Data.Queries
{
    /// <summary>
    ///     The materializer's routing: a depth-1 column onto the statement's own entity, a deeper one
    ///     onto a deduplicated related instance with its projection, a JSON column by its name, and
    ///     children onto their parents' collections.
    /// </summary>
    public sealed class EntityMaterializerTests : IDisposable
    {
        /// <summary>The composition over the query model.</summary>
        private readonly BatchHarness _harness = new(compose: QueryModel.Compose);

        /// <summary>The materializer.</summary>
        private EntityMaterializer Materializer => _harness.Services.GetRequiredService<EntityMaterializer>();

        /// <summary>The schema map of the widest shape.</summary>
        private QueryexSchemaMap Map => _harness.Services.GetRequiredService<QueryexSchemaProvider>().Map(MultilingualShape.All);

        [Fact]
        public void Depth_one_columns_land_on_the_root_converted_to_their_property_types_and_unread_ones_keep_their_defaults()
        {
            QueryRowSet rows = Rows(
                [
                    Column("Id", QueryexType.QxNumeric, typeof(int), "Id"),
                    Column("Name", QueryexType.QxString, typeof(string), "Name"),
                    Column("Kind", QueryexType.QxString, typeof(string), "Kind"),
                    Column("OptionalKind", QueryexType.QxString, typeof(string), "OptionalKind"),
                    Column("Price", QueryexType.QxNumeric, typeof(decimal), "Price"),
                    Column("DueOn", QueryexType.QxDate, typeof(DateTime), "DueOn"),
                    Column("Settings", QueryexType.QxString, typeof(string)),
                ],
                [1, "Lamp", "Large", "Small", 9.5m, new DateTime(2026, 3, 14), /*lang=json,strict*/ """{"a":1}"""],
                [2, "Desk", "Small", null, null, null, null]);

            Materialized read = Materializer.Read(Map, Metadata<Gadget>(), rows, new RelatedCollector());

            Gadget[] gadgets = [.. read.Instances.Cast<Gadget>()];
            Assert.Equal([1, 2], gadgets.Select(static gadget => gadget.Id));
            Assert.Equal((GadgetKind.Large, GadgetKind.Small), (gadgets[0].Kind, gadgets[0].OptionalKind));
            Assert.Equal((GadgetKind.Small, null), (gadgets[1].Kind, gadgets[1].OptionalKind));
            Assert.Equal((9.5m, null), (gadgets[0].Price, gadgets[1].Price));
            Assert.Equal(new DateOnly(2026, 3, 14), gadgets[0].DueOn);
            Assert.Equal(/*lang=json,strict*/ """{"a":1}""", gadgets[0].Settings);
            Assert.Null(gadgets[1].Settings);

            // Code was not read: the property keeps what a fresh instance has.
            Assert.Null(gadgets[0].Code);
            Assert.Equal(["DueOn", "Id", "Kind", "Name", "OptionalKind", "Price", "Settings"], read.Carried.Order(StringComparer.Ordinal));
        }

        [Fact]
        public void Deeper_columns_land_on_one_related_instance_per_id_with_the_projection_every_instance_carries()
        {
            QueryRowSet rows = Rows(
                [
                    Column("Id", QueryexType.QxNumeric, typeof(int), "Id"),
                    Column("ZoneId", QueryexType.QxNumeric, typeof(int), "ZoneId"),
                    Column("Zone.Id", QueryexType.QxNumeric, typeof(int), "Zone", "Id"),
                    Column("Zone.Name", QueryexType.QxString, typeof(string), "Zone", "Name"),
                    Column("Extra.Id", QueryexType.QxNumeric, typeof(int), "Extra", "Id"),
                    Column("Extra.Note", QueryexType.QxString, typeof(string), "Extra", "Note"),
                ],
                [1, 10, 10, "North", 1, "first"],
                [2, 10, 10, "North", null, null],
                [3, null, null, null, 3, null],
                [4, 12, 12, "South", null, null]);
            RelatedCollector related = new();

            Materialized read = Materializer.Read(Map, Metadata<Gadget>(), rows, related);

            // The root keeps its foreign key and never a navigation.
            Assert.Equal([10, 10, null, 12], read.Instances.Cast<Gadget>().Select(static gadget => gadget.ZoneId));
            Assert.DoesNotContain("Zone.Name", read.Carried);

            // A second statement brings a zone already held, with a column more, and a new one; zone 12
            // never gets the column, so the set's projection leaves it out.
            QueryRowSet ancestors = Rows(
                [
                    Column("Id", QueryexType.QxNumeric, typeof(int), "Id"),
                    Column("Name", QueryexType.QxString, typeof(string), "Name"),
                    Column("Code", QueryexType.QxString, typeof(string), "Code"),
                ],
                [10, "North", "N"],
                [11, "World", "W"]);
            Materialized zones = Materializer.Read(Map, Metadata<Zone>(), ancestors, related);
            EntityMaterializer.AddWhole(related, Metadata<Zone>(), zones.Instances, zones.Carried);

            RelatedEntities sets = Materializer.Build(related);
            RelatedEntitySet zoneSet = sets.Sets["qx.Zone"];
            Assert.Equal(typeof(Zone), zoneSet.EntityType);
            Assert.Equal([10, 12, 11], zoneSet.Entities.Cast<Zone>().Select(static zone => zone.Id));
            Assert.Equal("N", zoneSet.Entities.Cast<Zone>().First().Code);
            Assert.Equal(["Id", "Name"], zoneSet.Projection.Order(StringComparer.Ordinal));

            // An optional step that found nothing adds no instance; one found with a null column still counts.
            RelatedEntitySet extras = sets.Sets["qx.GadgetExtra"];
            Assert.Equal([1, 3], extras.Entities.Cast<GadgetExtra>().Select(static extra => extra.Id));
            Assert.Equal(["first", null], extras.Entities.Cast<GadgetExtra>().Select(static extra => extra.Note));
        }

        [Fact]
        public void Children_and_grandchildren_are_stitched_by_parent_key_in_their_order_and_every_parent_gets_a_collection()
        {
            EntityMetadata gadget = Metadata<Gadget>();
            ChildCollectionMetadata parts = gadget.Children.Single(static child => child.Property == nameof(Gadget.Parts));
            ChildCollectionMetadata notes = parts.Child.Children.Single(static child => child.Property == nameof(GadgetPart.PartNotes));
            RelatedCollector related = new();

            Materialized roots = Materializer.Read(
                Map, gadget, Rows([Column("Id", QueryexType.QxNumeric, typeof(int), "Id")], [1], [2]), related);
            Materialized children = Materializer.Read(
                Map,
                parts.Child,
                Rows(
                    [
                        Column("Id", QueryexType.QxNumeric, typeof(int), "Id"),
                        Column("GadgetId", QueryexType.QxNumeric, typeof(int), "GadgetId"),
                        Column("Ordinal", QueryexType.QxNumeric, typeof(int), "Ordinal"),
                        Column("Settings", QueryexType.QxString, typeof(string)),
                    ],
                    [7, 1, 2, "{}"],
                    [5, 1, 1, null],
                    [9, 99, 1, null]),
                related);
            Materialized grandchildren = Materializer.Read(
                Map,
                notes.Child,
                Rows(
                    [
                        Column("Id", QueryexType.QxNumeric, typeof(int), "Id"),
                        Column("GadgetPartId", QueryexType.QxNumeric, typeof(int), "GadgetPartId"),
                        Column("Text", QueryexType.QxString, typeof(string), "Text"),
                    ],
                    [3, 5, "a"]),
                related);

            Materializer.Stitch(gadget, parts, roots.Instances, children.Instances);
            Materializer.Stitch(parts.Child, notes, children.Instances, grandchildren.Instances);

            Gadget[] gadgets = [.. roots.Instances.Cast<Gadget>()];
            List<GadgetPart> stitched = Assert.IsType<List<GadgetPart>>(gadgets[0].Parts);
            Assert.Equal([7, 5], stitched.Select(static part => part.Id));
            Assert.Equal("{}", stitched[0].Settings);
            Assert.Empty(Assert.IsType<List<GadgetPart>>(gadgets[1].Parts));
            Assert.Equal(["a"], stitched[1].PartNotes!.Select(static note => note.Text));
            Assert.Empty(stitched[0].PartNotes!);
            Assert.Empty(related.Sets);
        }

        [Fact]
        public void A_column_that_matches_nothing_or_a_related_prefix_without_its_key_is_refused()
        {
            RelatedCollector related = new();

            _ = Assert.Throws<InvalidOperationException>(() => Materializer.Read(
                Map, Metadata<Gadget>(), Rows([Column("Nope", QueryexType.QxString, typeof(string), "Nope")], ["x"]), related));
            _ = Assert.Throws<InvalidOperationException>(() => Materializer.Read(
                Map, Metadata<Gadget>(), Rows([Column("Unknown", QueryexType.QxString, typeof(string))], ["x"]), related));
            _ = Assert.Throws<InvalidOperationException>(() => Materializer.Read(
                Map, Metadata<Gadget>(), Rows([Column("Zone.Name", QueryexType.QxString, typeof(string), "Zone", "Name")], ["x"]), related));
            _ = Assert.Throws<InvalidOperationException>(() => Materializer.Read(
                Map, Metadata<Gadget>(), Rows([Column("Nope.Id", QueryexType.QxNumeric, typeof(int), "Nope", "Id")], [1]), related));
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _harness.Dispose();
        }

        /// <summary>A column of a hand-made row set.</summary>
        /// <param name="name">The column's name.</param>
        /// <param name="type">Its query type.</param>
        /// <param name="fieldType">The provider's field type.</param>
        /// <param name="path">Its path; none for a JSON column.</param>
        /// <returns>The column and its field type.</returns>
        private static (QueryColumn Column, Type FieldType) Column(string name, QueryexType type, Type fieldType, params string[] path)
        {
            return (new QueryColumn(name, type, null, null, Nullable: true, path.Length == 0 ? null : path, GroupingKey: false), fieldType);
        }

        /// <summary>Reads rows into a row set, as the batch's reader does.</summary>
        /// <param name="columns">The columns.</param>
        /// <param name="rows">The rows, null for SQL <c>NULL</c>.</param>
        /// <returns>The row set.</returns>
        private static QueryRowSet Rows(IReadOnlyList<(QueryColumn Column, Type FieldType)> columns, params object?[][] rows)
        {
            using DataTable table = new();
            for (int index = 0; index < columns.Count; index++)
            {
                _ = table.Columns.Add($"c{index}", columns[index].FieldType);
            }

            foreach (object?[] row in rows)
            {
                _ = table.Rows.Add([.. row.Select(static value => value ?? DBNull.Value)]);
            }

            using DbDataReader reader = table.CreateDataReader();
            QueryRowSet.Builder builder = new([.. columns.Select(static column => column.Column)], [.. columns.Select(static column => column.FieldType)], 4);
            while (reader.Read())
            {
                builder.AppendRow(reader);
            }

            return builder.Build();
        }

        /// <summary>The metadata of an entity class.</summary>
        /// <typeparam name="T">The class.</typeparam>
        /// <returns>The metadata.</returns>
        private EntityMetadata Metadata<T>()
        {
            return _harness.Services.GetRequiredService<IEntityMetadataProvider>().Get(typeof(T));
        }
    }
}
