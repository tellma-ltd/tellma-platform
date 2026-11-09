// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;
using Tellma.Core.Abstractions.TableTypes;
using Tellma.Core.Data;
using Tellma.Core.Data.TableTypes;
using Tellma.Core.EntityFrameworkCore.TableTypes;
using Tellma.Core.Tests.Data.Model;

namespace Tellma.Core.Tests.Data
{
    /// <summary>
    ///     The tenant model a composition builds: leaves in place of their defaults, the children their
    ///     collections carry, Core's configurations closed over the leaves, the standalone types, the
    ///     platform's own tables and the platform collation; and what the host registers.
    /// </summary>
    public class ModelCompositionTests(DataModel model) : IClassFixture<DataModel>
    {
        [Fact]
        public void Each_table_is_mapped_by_exactly_one_type_and_a_replaced_default_is_absent()
        {
            List<(string?, string?)> tables = [.. model.DesignModel.GetEntityTypes().Select(static entityType => (entityType.GetSchema(), entityType.GetTableName()))];

            Assert.Equal(tables.Count, tables.Distinct().Count());
            Assert.Null(model.DesignModel.FindEntityType(typeof(Center)));
            Assert.Null(model.DesignModel.FindEntityType(typeof(TestUser)));
            Assert.NotNull(model.DesignModel.FindEntityType(typeof(MyCenter)));
            Assert.NotNull(model.DesignModel.FindEntityType(typeof(MyTestUser)));
            Assert.All(model.DesignModel.GetEntityTypes(), static entityType => Assert.Null(entityType.BaseType));
        }

        [Fact]
        public void The_model_holds_the_contributed_entities_their_children_and_the_platform_tables()
        {
            string[] tables = [.. model.DesignModel.GetEntityTypes().Select(static entityType => $"{entityType.GetSchema()}.{entityType.GetTableName()}").Order(StringComparer.Ordinal)];

            Assert.Equal(
                [
                    "core.Blobs", "core.Jobs", "core.Users",
                    "dbo.__TellmaProvisioning", "dbo.__TellmaSchema",
                    "fixture.Links", "fixture.Nodes", "fixture.Shipments", "fixture.WidgetExtras",
                    "fixture.WidgetPartNotes", "fixture.WidgetParts", "fixture.Widgets",
                    "gl.Centers",
                ],
                tables);
        }

        [Fact]
        public void An_open_core_configuration_is_closed_over_the_registered_leaf()
        {
            Assert.Contains(model.Entity<MyTestUser>().GetIndexes(), static index => index.GetDatabaseName() == "IX_Users_Name");
            Assert.Contains(typeof(TestUserConfiguration<MyTestUser>), model.Factory.Definition.Configurations);
        }

        [Fact]
        public void The_platform_tables_have_their_exact_shape()
        {
            string sql = model.Sql.ReplaceLineEndings("\n");

            Assert.Contains(
                "CREATE TABLE [dbo].[__TellmaSchema] (\n    [Fingerprint] char(64) NOT NULL,\n    [MigrationId] nvarchar(150) NOT NULL,\n    [PlatformVersion] nvarchar(64) NOT NULL,\n    [AppliedAt] datetimeoffset(3) NOT NULL,\n    CONSTRAINT [PK___TellmaSchema] PRIMARY KEY ([Fingerprint])\n);",
                sql,
                StringComparison.Ordinal);
            Assert.Contains(
                "CREATE TABLE [dbo].[__TellmaProvisioning] (\n    [Step] nvarchar(128) NOT NULL,\n    [Version] int NOT NULL,\n    [CompletedAt] datetimeoffset(3) NOT NULL,\n    [PlatformVersion] nvarchar(64) NOT NULL,\n    CONSTRAINT [PK___TellmaProvisioning] PRIMARY KEY ([Step])\n);",
                sql,
                StringComparison.Ordinal);
        }

        [Fact]
        public void Core_contributes_the_bulk_list_types_in_dbo_and_every_standalone_type_is_granted()
        {
            var standalone = model.DesignModel.GetTableTypes()
                .Where(static type => type.TableName is null)
                .ToDictionary(static type => type.Name);

            Assert.Equal(
                ["BigIdList", "CollatedKeyList", "DateList", "GuidList", "IdList", "IdStampList", "StringList", "VersionTagList"],
                standalone.Keys.Order(StringComparer.Ordinal));
            Assert.All(standalone.Values, static type => Assert.Equal(("dbo", "tellma_app"), (type.Schema, Assert.Single(type.Grants))));
            Assert.Equal("date", standalone[nameof(DateList)].Columns.Single().StoreType);
            Assert.Equal(["int", "datetimeoffset(7)"], standalone[nameof(IdStampList)].Columns.Select(static column => column.StoreType));
        }

        [Fact]
        public void A_standalone_class_stores_enums_by_name_and_leaves_its_excluded_property_out()
        {
            TableTypeDefinition list = model.DesignModel.GetTableTypes().Single(static type => type.Name == "CollatedKeyList");

            Assert.Equal(
                [("Key", "nvarchar(64)"), ("Status", "varchar(29)"), ("OptionalKind", "varchar(8)"), ("On", "date")],
                list.Columns.Select(static column => (column.Name, column.StoreType)));
            Assert.True(list.Columns.Single(static column => column.Name == "OptionalKind").IsNullable);
        }

        [Fact]
        public void The_model_sets_the_platform_collation()
        {
            // A database EF creates takes the model's collation at CREATE DATABASE, so no migration
            // ever alters it.
            Assert.Equal("Latin1_General_100_CI_AS", model.DesignModel.GetCollation());
            Assert.DoesNotContain(model.Operations.OfType<AlterDatabaseOperation>(), static operation => operation.Collation != operation.OldDatabase.Collation);
        }

        [Fact]
        public void The_host_registers_the_runtime_model_once_with_its_table_types()
        {
            IModel registered = model.Services.GetRequiredService<IModel>();

            Assert.Same(registered, model.Services.GetRequiredService<IModel>());
            Assert.Same(model.RuntimeModel, registered);
            Assert.NotNull(registered.FindEntityType(typeof(Widget)));
            Assert.Equal(
                model.DesignModel.GetTableTypes().Select(static type => type.PhysicalName),
                registered.GetTableTypes().Select(static type => type.PhysicalName));
            Assert.NotNull(model.Services.GetRequiredService<TableTypeBinder>());
        }

        [Fact]
        public void One_definition_builds_one_model_and_another_composition_builds_its_own()
        {
            using TellmaDbContext first = model.Factory.CreateContext(null);
            using TellmaDbContext second = model.Factory.CreateContext(null);
            using TellmaDbContext other = new TellmaModelFactory(TestModels.Definition([typeof(TestUser), typeof(TestJob)]), TestModels.Deployment).CreateContext(null);

            Assert.Same(first.Model, second.Model);
            Assert.NotSame(first.Model, other.Model);
            Assert.Null(other.Model.FindEntityType(typeof(Widget)));
        }

        [Fact]
        public void The_definition_lists_children_after_their_parents_with_leaves_substituted()
        {
            var definition = TellmaModelDefinition.Create(
                [typeof(Widget), typeof(Node)],
                [],
                [],
                static type => type == typeof(Link) ? typeof(LinkLeaf) : type);

            Assert.Equal([typeof(Widget), typeof(WidgetPart), typeof(WidgetPartNote), typeof(Node), typeof(LinkLeaf)], definition.EntityTypes);
        }

        [Fact]
        public void A_child_leaf_replaces_its_default_in_the_model()
        {
            var definition = TellmaModelDefinition.Create(
                [typeof(TestUser), typeof(Node)],
                [],
                [],
                static type => type == typeof(Link) ? typeof(LinkLeaf) : type);
            IModel built = TestModels.DesignModel(definition);

            Assert.Null(built.FindEntityType(typeof(Link)));
            IForeignKey parentKey = Assert.Single(built.FindEntityType(typeof(LinkLeaf))!.FindProperty(nameof(Link.NodeId))!.GetContainingForeignKeys());
            Assert.Equal("FK_Links_NodeId", parentKey.GetConstraintName());
        }

        [Fact]
        public void A_model_with_no_entity_holds_the_platform_tables_and_types()
        {
            IModel built = TestModels.DesignModel(TellmaModelDefinition.Create(
                [], [typeof(IdList)], [typeof(TellmaSchemaRowConfiguration), typeof(TellmaProvisioningRowConfiguration)], static type => type));

            Assert.Equal(2, built.GetEntityTypes().Count());
            Assert.Equal("IdList", Assert.Single(built.GetTableTypes()).Name);
        }

        /// <summary>A distribution's leaf of the <see cref="Link" /> child.</summary>
        public sealed class LinkLeaf : Link
        {
            public int Weight { get; set; }
        }
    }
}
