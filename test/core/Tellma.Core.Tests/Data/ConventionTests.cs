// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Tellma.Core.Data.Conventions;
using Tellma.Core.EntityFrameworkCore.TableTypes;
using Tellma.Core.Tests.Data.Model;

namespace Tellma.Core.Tests.Data
{
    /// <summary>
    ///     Each convention of the tenant model, read back from the design-time model a composition
    ///     builds: what it configures, under which name, and what it leaves alone.
    /// </summary>
    public class ConventionTests(DataModel model) : IClassFixture<DataModel>
    {
        // --- Enums ---

        [Theory]
        [InlineData(nameof(Widget.Kind), 8, false)]
        [InlineData(nameof(Widget.OptionalKind), 8, true)]
        [InlineData(nameof(Widget.Status), 29, false)]
        [InlineData(nameof(Widget.Access), 19, false)]
        public void Every_enum_is_stored_by_name_as_a_sized_varchar(string property, int length, bool nullable)
        {
            IProperty column = model.Entity<Widget>().FindProperty(property)!;

            Assert.Equal(typeof(string), column.GetProviderClrType());
            Assert.Equal(length, column.GetMaxLength());
            Assert.False(column.IsUnicode());
            Assert.Equal($"varchar({length})", column.GetColumnType());
            Assert.Equal(nullable, column.IsNullable);
        }

        [Fact]
        public void A_declared_length_wins_over_the_enum_sizing_and_the_column_stays_non_unicode()
        {
            IProperty column = model.Entity<Widget>().FindProperty(nameof(Widget.SizedKind))!;

            Assert.Equal("varchar(20)", column.GetColumnType());
        }

        [Fact]
        public void An_enum_has_no_check_constraint_listing_its_members()
        {
            Assert.All(model.Entity<Widget>().GetCheckConstraints(), static check => Assert.DoesNotContain("Plain", check.Sql, StringComparison.Ordinal));
        }

        [Theory]
        [InlineData(typeof(WidgetKind), 8)]
        [InlineData(typeof(LongStatus), 29)]
        [InlineData(typeof(Access), 19)]
        public void Enum_lengths_take_the_longest_value_and_at_least_eight(Type enumType, int length)
        {
            // A flags combination is written "Read, Write, Delete" by the string conversion.
            Assert.Equal(length, EnumStringLengthConvention.LengthOf(enumType));
        }

        [Fact]
        public void The_enum_conversion_reads_and_writes_member_names()
        {
            IProperty kind = model.RuntimeModel.FindEntityType(typeof(Widget))!.FindProperty(nameof(Widget.Kind))!;

            Assert.Equal("Fancy", kind.GetTypeMapping().Converter!.ConvertToProvider(WidgetKind.Fancy));
            Assert.Equal(WidgetKind.Plain, kind.GetTypeMapping().Converter!.ConvertFromProvider("Plain"));
        }

        // --- JSON columns ---

        [Fact]
        public void A_json_column_is_nvarchar_max_text()
        {
            IProperty settings = model.Entity<Widget>().FindProperty(nameof(Widget.Settings))!;

            Assert.Equal("nvarchar(max)", settings.GetColumnType());
            Assert.Equal(typeof(string), settings.GetProviderClrType() ?? settings.ClrType);
        }

        // --- Audit columns ---

        [Theory]
        [InlineData(nameof(Widget.CreatedById))]
        [InlineData(nameof(Widget.ModifiedById))]
        public void The_audit_user_columns_reference_core_users_without_cascade_or_index(string column)
        {
            IEntityType widget = model.Entity<Widget>();
            IForeignKey foreignKey = Assert.Single(widget.FindProperty(column)!.GetContainingForeignKeys());

            Assert.Equal($"FK_Widgets_{column}", foreignKey.GetConstraintName());
            Assert.Equal(DeleteBehavior.NoAction, foreignKey.DeleteBehavior);
            Assert.Equal(("core", "Users"), (foreignKey.PrincipalEntityType.GetSchema(), foreignKey.PrincipalEntityType.GetTableName()));
            Assert.Null(foreignKey.DependentToPrincipal);
            Assert.Null(foreignKey.PrincipalToDependent);
            Assert.Empty(widget.FindProperty(column)!.GetContainingIndexes());
        }

        [Theory]
        [InlineData(nameof(Widget.CreatedAt))]
        [InlineData(nameof(Widget.ModifiedAt))]
        public void The_audit_timestamps_carry_seven_fractional_digits(string column)
        {
            Assert.Equal("datetimeoffset(7)", model.Entity<Widget>().FindProperty(column)!.GetColumnType());
        }

        [Fact]
        public void The_audit_columns_of_the_user_table_reference_the_user_table_itself()
        {
            IEntityType users = model.Entity<MyTestUser>();

            Assert.All(users.GetForeignKeys(), foreignKey => Assert.Same(users, foreignKey.PrincipalEntityType));
            Assert.Equal(["FK_Users_CreatedById", "FK_Users_ModifiedById"], users.GetForeignKeys().Select(static fk => fk.GetConstraintName()).Order());
        }

        // --- Collations ---

        [Fact]
        public void A_collation_on_an_entity_property_reaches_the_column_and_its_row_image()
        {
            IEntityType widget = model.Entity<Widget>();

            Assert.Equal("Latin1_General_100_BIN2", widget.FindProperty(nameof(Widget.Subject))!.GetCollation());
            Assert.Equal("Latin1_General_100_BIN2", widget.GetTableType()!.Columns.Single(static column => column.Name == nameof(Widget.Subject)).Collation);
            Assert.Null(widget.FindProperty(nameof(Widget.Name))!.GetCollation());
        }

        [Fact]
        public void A_collation_on_a_standalone_class_property_reaches_its_table_type_column()
        {
            TableTypeDefinition list = model.DesignModel.GetTableTypes().Single(static type => type.Name == "CollatedKeyList");

            Assert.Equal("Latin1_General_100_BIN2", list.Columns.Single(static column => column.Name == nameof(CollatedKeyRow.Key)).Collation);
            Assert.Contains("[Key] nvarchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL", model.Sql, StringComparison.Ordinal);
        }

        // --- Table types ---

        [Fact]
        public void An_opted_in_entity_gets_its_row_image_type_granted_to_the_application_role()
        {
            TableTypeDefinition widgets = model.Entity<Widget>().GetTableType()!;

            Assert.Equal(("fixture", "WidgetsList"), (widgets.Schema, widgets.Name));
            Assert.Equal(["tellma_app"], widgets.Grants);
            Assert.Contains($"GRANT EXECUTE ON TYPE::[fixture].[{widgets.PhysicalName}] TO [tellma_app];", model.Sql, StringComparison.Ordinal);
        }

        [Fact]
        public void An_excluded_property_and_the_shadow_columns_are_absent_from_the_row_image()
        {
            string[] widgetColumns = [.. model.Entity<Widget>().GetTableType()!.Columns.Select(static column => column.Name)];
            string[] nodeColumns = [.. model.Entity<Node>().GetTableType()!.Columns.Select(static column => column.Name)];

            Assert.DoesNotContain(nameof(Widget.Scratch), widgetColumns);
            Assert.DoesNotContain(TemporalConvention.PeriodStart, widgetColumns);
            Assert.DoesNotContain(TemporalConvention.PeriodEnd, widgetColumns);
            Assert.DoesNotContain(TreeConvention.NodeColumn, nodeColumns);
            Assert.Contains(nameof(Widget.IsActive), widgetColumns);
            Assert.NotNull(model.Entity<Widget>().FindProperty(nameof(Widget.Scratch)));
        }

        [Fact]
        public void An_entity_without_the_attribute_has_no_table_type()
        {
            Assert.Null(model.Entity<WidgetExtra>().GetTableType());
            Assert.Null(model.Entity<TestJob>().GetTableType());
        }

        [Fact]
        public void A_leaf_inherits_its_defaults_table_type_and_table()
        {
            IEntityType centers = model.Entity<MyCenter>();

            Assert.Equal(("gl", "Centers"), (centers.GetSchema(), centers.GetTableName()));
            Assert.Equal("CentersList", centers.GetTableType()!.Name);
            Assert.Contains("Region", centers.GetTableType()!.Columns.Select(static column => column.Name));
        }

        // --- Temporal ---

        [Fact]
        public void A_temporal_entity_is_system_versioned_with_shadow_periods_and_its_history_table()
        {
            IEntityType widget = model.Entity<Widget>();

            Assert.True(widget.IsTemporal());
            Assert.Equal(("WidgetsHistory", "fixture"), (widget.GetHistoryTableName(), widget.GetHistoryTableSchema()));
            Assert.Equal((TemporalConvention.PeriodStart, TemporalConvention.PeriodEnd), (widget.GetPeriodStartPropertyName(), widget.GetPeriodEndPropertyName()));
            Assert.True(widget.FindProperty(TemporalConvention.PeriodStart)!.IsShadowProperty());
            Assert.Equal("datetime2", widget.FindProperty(TemporalConvention.PeriodStart)!.GetColumnType());
            Assert.Contains("SYSTEM_VERSIONING = ON (HISTORY_TABLE = [fixture].[WidgetsHistory])", model.Sql, StringComparison.Ordinal);
        }

        [Fact]
        public void A_child_follows_its_temporal_parent_unless_it_opts_out()
        {
            Assert.True(model.Entity<WidgetPart>().IsTemporal());
            Assert.Equal("WidgetPartsHistory", model.Entity<WidgetPart>().GetHistoryTableName());
            Assert.False(model.Entity<WidgetPartNote>().IsTemporal());
            Assert.False(model.Entity<Node>().IsTemporal());
        }

        // --- Children ---

        [Fact]
        public void A_parent_key_is_a_foreign_key_to_the_parent_indexed_with_the_child_key()
        {
            IEntityType parts = model.Entity<WidgetPart>();
            IForeignKey foreignKey = Assert.Single(parts.FindProperty(nameof(WidgetPart.WidgetId))!.GetContainingForeignKeys());
            IIndex index = parts.GetIndexes().Single(static index => index.GetDatabaseName() == "IX_WidgetParts_WidgetId");

            Assert.Equal("FK_WidgetParts_WidgetId", foreignKey.GetConstraintName());
            Assert.Same(model.Entity<Widget>(), foreignKey.PrincipalEntityType);
            Assert.Equal(DeleteBehavior.NoAction, foreignKey.DeleteBehavior);
            Assert.False(index.IsUnique);
            Assert.Equal(["Id"], index.GetIncludeProperties());
            Assert.Contains("CREATE INDEX [IX_WidgetParts_WidgetId] ON [fixture].[WidgetParts] ([WidgetId]) INCLUDE ([Id]);", model.Sql, StringComparison.Ordinal);
        }

        [Fact]
        public void A_grandchild_pairs_with_its_child_parent()
        {
            IForeignKey foreignKey = Assert.Single(model.Entity<WidgetPartNote>().GetForeignKeys());

            Assert.Equal("FK_WidgetPartNotes_WidgetPartId", foreignKey.GetConstraintName());
            Assert.Same(model.Entity<WidgetPart>(), foreignKey.PrincipalEntityType);
        }

        [Fact]
        public void The_child_collection_itself_is_never_mapped()
        {
            Assert.Null(model.Entity<Widget>().FindNavigation(nameof(Widget.Parts)));
            Assert.Null(model.Entity<Widget>().FindProperty(nameof(Widget.Parts)));
            Assert.Empty(model.DesignModel.GetEntityTypes().SelectMany(static entityType => entityType.GetNavigations()));
        }

        // --- References ---

        [Fact]
        public void A_reference_to_a_replaced_default_targets_the_leafs_table()
        {
            IEntityType widget = model.Entity<Widget>();
            IForeignKey foreignKey = Assert.Single(widget.FindProperty(nameof(Widget.CenterId))!.GetContainingForeignKeys());

            Assert.Equal("FK_Widgets_CenterId", foreignKey.GetConstraintName());
            Assert.Same(model.Entity<MyCenter>(), foreignKey.PrincipalEntityType);
            Assert.Equal(DeleteBehavior.NoAction, foreignKey.DeleteBehavior);
            Assert.False(foreignKey.IsUnique);
            Assert.Contains(widget.GetIndexes(), static index => index.GetDatabaseName() == "IX_Widgets_CenterId" && !index.IsUnique);
        }

        [Fact]
        public void A_second_foreign_key_to_a_childs_parent_is_an_ordinary_reference()
        {
            IEntityType links = model.Entity<Link>();
            IForeignKey other = Assert.Single(links.FindProperty(nameof(Link.OtherNodeId))!.GetContainingForeignKeys());

            Assert.Equal("FK_Links_OtherNodeId", other.GetConstraintName());
            Assert.Same(model.Entity<Node>(), other.PrincipalEntityType);
            Assert.Contains(links.GetIndexes(), static index => index.GetDatabaseName() == "IX_Links_OtherNodeId" && index.GetIncludeProperties() is null);
            Assert.Contains(links.GetIndexes(), static index => index.GetDatabaseName() == "IX_Links_NodeId");
        }

        // --- Siblings ---

        [Fact]
        public void A_siblings_key_references_its_owner_with_cascade_and_it_has_no_sequence()
        {
            IEntityType extras = model.Entity<WidgetExtra>();
            IForeignKey foreignKey = Assert.Single(extras.GetForeignKeys());

            Assert.Equal("FK_WidgetExtras_Id", foreignKey.GetConstraintName());
            Assert.Same(model.Entity<Widget>(), foreignKey.PrincipalEntityType);
            Assert.Equal(DeleteBehavior.Cascade, foreignKey.DeleteBehavior);
            Assert.True(foreignKey.IsUnique);
            Assert.Null(model.DesignModel.FindSequence("sq_WidgetExtras", "fixture"));
            Assert.Equal(ValueGenerated.Never, extras.FindPrimaryKey()!.Properties[0].ValueGenerated);
            Assert.Contains("CONSTRAINT [FK_WidgetExtras_Id] FOREIGN KEY ([Id]) REFERENCES [fixture].[Widgets] ([Id]) ON DELETE CASCADE", model.Sql, StringComparison.Ordinal);
        }

        // --- Declared indexes ---

        [Fact]
        public void A_declared_index_covers_its_properties_in_order()
        {
            IIndex index = model.Entity<Shipment>().GetIndexes().Single(static index => index.GetDatabaseName() == "IX_Shipments_State_CreatedAt");

            Assert.Equal([nameof(Shipment.State), nameof(Shipment.CreatedAt)], index.Properties.Select(static property => property.Name));
            Assert.False(index.IsUnique);
            Assert.Contains(model.Entity<Widget>().GetIndexes(), static index => index.GetDatabaseName() == "IX_Widgets_Kind_Price");
        }

        // --- Trees ---

        [Fact]
        public void A_tree_has_a_navigationless_self_reference_indexed_by_parent()
        {
            IEntityType nodes = model.Entity<Node>();
            IForeignKey parent = Assert.Single(nodes.FindProperty(nameof(Node.ParentId))!.GetContainingForeignKeys());

            Assert.Equal("FK_Nodes_ParentId", parent.GetConstraintName());
            Assert.Same(nodes, parent.PrincipalEntityType);
            Assert.Equal(DeleteBehavior.NoAction, parent.DeleteBehavior);
            Assert.False(parent.IsRequired);
            Assert.Contains(nodes.GetIndexes(), static index => index.GetDatabaseName() == "IX_Nodes_ParentId");
        }

        [Fact]
        public void A_tree_has_a_required_unique_shadow_hierarchyid_node()
        {
            IEntityType nodes = model.Entity<Node>();
            IProperty node = nodes.FindProperty(TreeConvention.NodeColumn)!;
            IIndex unique = Assert.Single(node.GetContainingIndexes());

            Assert.True(node.IsShadowProperty());
            Assert.Equal(typeof(HierarchyId), node.ClrType);
            Assert.Equal("hierarchyid", node.GetColumnType());
            Assert.False(node.IsNullable);
            Assert.Equal(("UX_Nodes_Node", true), (unique.GetDatabaseName(), unique.IsUnique));
            Assert.Null(unique.GetFilter());
        }

        [Fact]
        public void A_trees_counts_default_to_one_under_named_constraints()
        {
            IEntityType nodes = model.Entity<Node>();

            foreach (string column in (string[])[nameof(Node.SubtreeCount), nameof(Node.ActiveSubtreeCount)])
            {
                IProperty property = nodes.FindProperty(column)!;
                Assert.Equal(1, property.GetDefaultValue());
                Assert.Equal($"DF_Nodes_{column}", property.GetDefaultConstraintName());
            }

            Assert.Contains("[SubtreeCount] int NOT NULL CONSTRAINT [DF_Nodes_SubtreeCount] DEFAULT 1", model.Sql, StringComparison.Ordinal);
        }

        [Fact]
        public void A_trees_depth_is_recorded_from_its_declaration_or_defaults_to_32()
        {
            Assert.Equal(8, model.Entity<Node>()["Tellma:Tree:MaxDepth"]);
            Assert.Equal(32, model.Entity<MyCenter>()["Tellma:Tree:MaxDepth"]);
        }

        // --- Activatable ---

        [Fact]
        public void An_activatable_entity_is_created_active_by_a_named_default()
        {
            IProperty isActive = model.Entity<Widget>().FindProperty(nameof(Widget.IsActive))!;

            Assert.Equal(true, isActive.GetDefaultValue());
            Assert.Equal("DF_Widgets_IsActive", isActive.GetDefaultConstraintName());
            Assert.Contains("[IsActive] bit NOT NULL CONSTRAINT [DF_Widgets_IsActive] DEFAULT CAST(1 AS bit)", model.Sql, StringComparison.Ordinal);
        }

        // --- Job entities ---

        [Fact]
        public void A_job_entitys_job_is_nulled_on_delete_and_unique_where_set()
        {
            IEntityType shipments = model.Entity<Shipment>();
            IForeignKey job = Assert.Single(shipments.FindProperty(nameof(Shipment.JobId))!.GetContainingForeignKeys());
            IIndex unique = Assert.Single(shipments.FindProperty(nameof(Shipment.JobId))!.GetContainingIndexes());

            Assert.Equal("FK_Shipments_JobId", job.GetConstraintName());
            Assert.Equal(("core", "Jobs"), (job.PrincipalEntityType.GetSchema(), job.PrincipalEntityType.GetTableName()));
            Assert.Equal(DeleteBehavior.SetNull, job.DeleteBehavior);
            Assert.Equal(("UX_Shipments_JobId", true, "[JobId] IS NOT NULL"), (unique.GetDatabaseName(), unique.IsUnique, unique.GetFilter()));
        }

        // --- Uniqueness ---

        [Fact]
        public void A_nullable_unique_column_is_indexed_where_not_null()
        {
            IIndex code = Assert.Single(model.Entity<Widget>().FindProperty(nameof(Widget.Code))!.GetContainingIndexes());

            Assert.Equal(("UX_Widgets_Code", true, "[Code] IS NOT NULL"), (code.GetDatabaseName(), code.IsUnique, code.GetFilter()));
        }

        [Fact]
        public void A_required_unique_column_is_indexed_without_a_filter()
        {
            IIndex subject = Assert.Single(model.Entity<Widget>().FindProperty(nameof(Widget.Subject))!.GetContainingIndexes());

            Assert.Equal(("UX_Widgets_Subject", true, null), (subject.GetDatabaseName(), subject.IsUnique, subject.GetFilter()));
        }

        [Fact]
        public void A_childs_unique_column_is_unique_within_its_parent()
        {
            IIndex ordinal = model.Entity<WidgetPart>().GetIndexes().Single(static index => index.GetDatabaseName() == "UX_WidgetParts_WidgetId_Ordinal");

            Assert.True(ordinal.IsUnique);
            Assert.Equal([nameof(WidgetPart.WidgetId), nameof(WidgetPart.Ordinal)], ordinal.Properties.Select(static property => property.Name));
        }

        // --- Blob references ---

        [Fact]
        public void A_blob_reference_is_a_foreign_key_to_core_blobs_unique_where_set()
        {
            IEntityType widget = model.Entity<Widget>();
            IForeignKey blob = Assert.Single(widget.FindProperty(nameof(Widget.ImageId))!.GetContainingForeignKeys());
            IIndex unique = Assert.Single(widget.FindProperty(nameof(Widget.ImageId))!.GetContainingIndexes());

            Assert.Equal("FK_Widgets_ImageId", blob.GetConstraintName());
            Assert.Equal(("core", "Blobs"), (blob.PrincipalEntityType.GetSchema(), blob.PrincipalEntityType.GetTableName()));
            Assert.Equal(DeleteBehavior.NoAction, blob.DeleteBehavior);
            Assert.Equal(("UX_Widgets_ImageId", true, "[ImageId] IS NOT NULL"), (unique.GetDatabaseName(), unique.IsUnique, unique.GetFilter()));
        }

        [Fact]
        public void A_childs_blob_reference_is_unique_on_its_own_column()
        {
            IIndex unique = Assert.Single(model.Entity<WidgetPart>().FindProperty(nameof(WidgetPart.AttachmentId))!.GetContainingIndexes());

            Assert.Equal("UX_WidgetParts_AttachmentId", unique.GetDatabaseName());
            Assert.Equal([nameof(WidgetPart.AttachmentId)], unique.Properties.Select(static property => property.Name));
        }

        // --- Sequences and keys ---

        [Theory]
        [InlineData("fixture", "Widgets", typeof(int))]
        [InlineData("fixture", "Shipments", typeof(long))]
        [InlineData("core", "Blobs", typeof(int))]
        [InlineData("fixture", "WidgetParts", typeof(int))]
        public void Every_keyed_table_has_its_sequence_from_1000(string schema, string table, Type type)
        {
            ISequence sequence = model.DesignModel.FindSequence($"sq_{table}", schema)!;

            Assert.Equal((type, 1000L, 1, false), (sequence.Type, sequence.StartValue, sequence.IncrementBy, sequence.IsCyclic));
            Assert.Equal(ValueGenerated.Never, model.Table(schema, table).FindPrimaryKey()!.Properties[0].ValueGenerated);
        }

        [Fact]
        public void No_key_is_generated_by_the_database()
        {
            Assert.DoesNotContain("IDENTITY", model.Sql, StringComparison.Ordinal);
            Assert.Contains("CREATE SEQUENCE [fixture].[sq_Widgets] AS int START WITH 1000 INCREMENT BY 1 NO CYCLE;", model.Sql, StringComparison.Ordinal);
        }

        [Fact]
        public void Every_keyed_table_refuses_a_non_positive_id()
        {
            foreach (IEntityType entityType in model.DesignModel.GetEntityTypes().Where(static entityType => entityType.GetSchema() != "dbo"))
            {
                ICheckConstraint check = Assert.Single(entityType.GetCheckConstraints());
                Assert.Equal(($"CK_{entityType.GetTableName()}_Id", "[Id] > 0"), (check.Name, check.Sql));
            }
        }

        [Fact]
        public void The_platform_tables_are_neither_sequenced_nor_checked()
        {
            Assert.Empty(model.Table("dbo", "__TellmaSchema").GetCheckConstraints());
            Assert.Null(model.DesignModel.FindSequence("sq___TellmaSchema", "dbo"));
        }

        // --- Multilingual groups ---

        [Fact]
        public void The_twins_of_a_multilingual_anchor_share_its_facets_and_are_nullable()
        {
            IEntityType widget = model.Entity<Widget>();

            foreach (string twin in (string[])[nameof(Widget.Name2), nameof(Widget.Name3)])
            {
                IProperty property = widget.FindProperty(twin)!;
                Assert.Equal(("nvarchar(255)", true), (property.GetColumnType(), property.IsNullable));
            }

            Assert.False(widget.FindProperty(nameof(Widget.Name))!.IsNullable);
        }

        [Fact]
        public void A_twin_that_declares_its_own_length_keeps_it_for_the_validation_to_report()
        {
            Assert.Equal("nvarchar(100)", model.Entity<MyCenter>().FindProperty(nameof(Center.Name2))!.GetColumnType());
            Assert.Equal("nvarchar(255)", model.Entity<MyCenter>().FindProperty(nameof(Center.Name3))!.GetColumnType());
        }
    }
}
