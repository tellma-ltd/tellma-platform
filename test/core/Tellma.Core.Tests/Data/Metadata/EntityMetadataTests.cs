// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Blobs;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Entities;
using Tellma.Core.Data;
using Tellma.Core.Data.Metadata;

namespace Tellma.Core.Tests.Data.Metadata
{
    /// <summary>The metadata of a sound model: what each entity's description holds and how its members behave.</summary>
    /// <param name="model">The sound model, built once for the class.</param>
    public class EntityMetadataTests(SoundMetadataModel model) : IClassFixture<SoundMetadataModel>
    {
        private static readonly string[] MultilingualNames = ["Name", "Name2", "Name3"];

        private readonly IEntityMetadataProvider _provider = model.Provider;

        // --- The provider ---

        [Fact]
        public void Every_leaf_is_described_in_model_order_with_children_after_their_parents()
        {
            Assert.Equal(
                [typeof(MyMdUser), typeof(MdJob), typeof(MdBlob), typeof(Account), typeof(AccountLine), typeof(AccountLineNote), typeof(MyRegion), typeof(Shipment), typeof(ShipmentExtra), typeof(Carrier)],
                _provider.All.Select(static metadata => metadata.ClrType));
        }

        [Theory]
        [InlineData(typeof(MyRegion))]
        [InlineData(typeof(Region))]
        public void A_leaf_resolves_from_itself_and_from_the_default_it_replaces(Type entityType)
        {
            EntityMetadata region = _provider.Get(entityType);

            Assert.Equal(typeof(MyRegion), region.ClrType);
            Assert.Same(_provider.Get(typeof(MyRegion)), region);
        }

        [Theory]
        [InlineData(typeof(TopLevelEntity))]
        [InlineData(typeof(ActivatableTreeEntity))]
        [InlineData(typeof(Entity<int>))]
        [InlineData(typeof(string))]
        public void A_platform_base_or_a_class_outside_the_model_resolves_no_leaf(Type entityType)
        {
            ArgumentException exception = Assert.Throws<ArgumentException>(() => _provider.Get(entityType));
            Assert.Contains("is not an entity of the tenant model", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void The_platform_migration_tables_are_not_entities()
        {
            EntityMetadataBuild build = MetadataModels.Build(TellmaModelDefinition.Create(
                MetadataModels.PlatformStandIns, [], [typeof(TellmaSchemaRowConfiguration)], new Dictionary<Type, Type>()));

            Assert.Empty(build.Problems);
            Assert.DoesNotContain(build.Provider!.All, static metadata => metadata.Table.Name.StartsWith("__", StringComparison.Ordinal));
        }

        // --- Names, tables and table types ---

        [Fact]
        public void An_entity_is_named_by_its_schema_and_its_defaults_class_name()
        {
            Assert.Equal("geo.Region", _provider.Get(typeof(MyRegion)).Name);
            Assert.Equal("core.MdUser", _provider.Get(typeof(MyMdUser)).Name);
            Assert.Equal("fixture.AccountLine", _provider.Get(typeof(AccountLine)).Name);
        }

        [Fact]
        public void The_table_and_the_physical_table_type_name_come_from_the_model()
        {
            EntityMetadata account = _provider.Get(typeof(Account));

            Assert.Equal(new TableName("fixture", "Accounts"), account.Table);
            Assert.Equal("[fixture].[Accounts]", account.Table.Delimited);
            Assert.Matches("^fixture\\.AccountsList_[0-9a-f]{8}$", account.TableTypePhysicalName);
            Assert.Null(_provider.Get(typeof(MdJob)).TableTypePhysicalName);
        }

        // --- Properties ---

        [Fact]
        public void Properties_are_the_mapped_scalars_in_declaration_order_bases_first_without_shadow_columns()
        {
            Assert.Equal(
                ["Id", "CreatedAt", "CreatedById", "ModifiedAt", "ModifiedById", "Number", "Ref", "Name", "Name2", "Name3", "Code", "Subject", "Alias", "Evidence", "Origin", "Total", "Normalized", "Tag", "Note", "Label", "Kind", "IsActive", "ParentAccountId", "ImageId"],
                _provider.Get(typeof(Account)).Properties.Select(static property => property.Name));
            Assert.Equal(
                ["Id", "CreatedAt", "CreatedById", "ModifiedAt", "ModifiedById", "ParentId", "SubtreeCount", "IsActive", "ActiveSubtreeCount", "Code", "Name", "Name2", "Name3", "Zone"],
                _provider.Get(typeof(MyRegion)).Properties.Select(static property => property.Name));
        }

        [Theory]
        [InlineData(typeof(Account), "Id", PropertyOwnership.ServerOwned)]
        [InlineData(typeof(Account), "CreatedAt", PropertyOwnership.ServerOwned)]
        [InlineData(typeof(Account), "CreatedById", PropertyOwnership.ServerOwned)]
        [InlineData(typeof(Account), "ModifiedAt", PropertyOwnership.ServerOwned)]
        [InlineData(typeof(Account), "ModifiedById", PropertyOwnership.ServerOwned)]
        [InlineData(typeof(Account), "IsActive", PropertyOwnership.ServerOwned)]
        [InlineData(typeof(Account), "Evidence", PropertyOwnership.ServerOwned)]
        [InlineData(typeof(Account), "Origin", PropertyOwnership.WriteOnce)]
        [InlineData(typeof(Account), "Total", PropertyOwnership.Derived)]
        [InlineData(typeof(Account), "Label", PropertyOwnership.DatabaseOwned)]
        [InlineData(typeof(Account), "Code", PropertyOwnership.Editable)]
        [InlineData(typeof(Account), "ParentAccountId", PropertyOwnership.Editable)]
        [InlineData(typeof(Account), "ImageId", PropertyOwnership.Editable)]
        [InlineData(typeof(MyRegion), "ParentId", PropertyOwnership.Editable)]
        [InlineData(typeof(MyRegion), "SubtreeCount", PropertyOwnership.ServerOwned)]
        [InlineData(typeof(MyRegion), "ActiveSubtreeCount", PropertyOwnership.ServerOwned)]
        [InlineData(typeof(MyRegion), "IsActive", PropertyOwnership.ServerOwned)]
        [InlineData(typeof(Shipment), "JobId", PropertyOwnership.ServerOwned)]
        [InlineData(typeof(AccountLine), "Id", PropertyOwnership.ServerOwned)]
        [InlineData(typeof(AccountLine), "AccountId", PropertyOwnership.Editable)]
        public void Ownership_is_derived_from_the_bases_the_capability_interfaces_the_model_and_the_annotations(Type entity, string property, PropertyOwnership ownership)
        {
            Assert.Equal(ownership, Property(entity, property).Ownership);
        }

        [Fact]
        public void A_property_carries_its_column_type_facets_and_annotations()
        {
            PropertyMetadata code = Property(typeof(Account), "Code");
            Assert.Equal(("Code", typeof(string), false, true, 50), (code.Column, code.ClrType, code.IsNullable, code.IsUnique, code.MaxLength));

            PropertyMetadata total = Property(typeof(Account), "Total");
            Assert.Equal((19, 4), (total.Precision, total.Scale));

            PropertyMetadata modifiedAt = Property(typeof(Account), "ModifiedAt");
            Assert.Equal(7, modifiedAt.Precision);

            PropertyMetadata kind = Property(typeof(Account), "Kind");
            Assert.Equal(["Asset", "Liability"], kind.EnumValues);
            Assert.Equal(9, kind.MaxLength);
            Assert.False(kind.IsUnique);

            PropertyMetadata name = Property(typeof(Account), "Name");
            Assert.Equal((true, SearchKind.Prefix), (name.IsSearchable, name.SearchKind));
            Assert.All(MultilingualNames, twin => Assert.Equal("Name", Property(typeof(Account), twin).MultilingualGroup));
            Assert.Null(code.MultilingualGroup);
            Assert.Null(code.SearchKind);

            Assert.True(Property(typeof(Account), "Note").ExcludedFromExcel);
            Assert.True(Property(typeof(Account), "Tag").IsDefaulted);
            Assert.Null(code.ValueSet);
        }

        [Fact]
        public void A_leafs_own_property_may_take_another_column_name()
        {
            Assert.Equal("ZoneCode", Property(typeof(MyRegion), "Zone").Column);
        }

        [Fact]
        public void A_childs_property_unique_within_its_parent_is_unique()
        {
            Assert.True(Property(typeof(AccountLine), "Ordinal").IsUnique);
            Assert.True(Property(typeof(AccountLine), "Label").IsUnique);
            Assert.False(Property(typeof(AccountLine), "AccountId").IsUnique);
        }

        [Fact]
        public void The_compiled_accessors_read_and_write_the_property()
        {
            PropertyMetadata code = Property(typeof(Account), "Code");
            PropertyMetadata number = Property(typeof(Account), "Number");
            PropertyMetadata parent = Property(typeof(Account), "ParentAccountId");
            Account account = new() { Code = "1101" };

            code.Setter(account, "1102");
            number.Setter(account, 7);
            parent.Setter(account, null);

            Assert.Equal(("1102", 7, null), (code.Getter(account), number.Getter(account), parent.Getter(account)));
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => number.Setter(account, null));
            Assert.Contains("Account.Number is a non-nullable Int32", exception.Message, StringComparison.Ordinal);
            _ = Assert.Throws<InvalidCastException>(() => number.Setter(account, "seven"));
        }

        [Fact]
        public void An_interface_declared_capability_is_read_through_the_implementing_property()
        {
            Shipment shipment = new() { JobId = 9 };
            Assert.Equal(9, Property(typeof(Shipment), "JobId").Getter(shipment));
            Assert.True(_provider.Get(typeof(Shipment)).IsJobEntity);
            Assert.True(_provider.Get(typeof(Account)).IsActivatable);
        }

        // --- Resets ---

        [Fact]
        public void ResetServerOwned_restores_every_server_owned_property_but_the_key_to_a_fresh_instances_value()
        {
            EntityMetadata metadata = _provider.Get(typeof(Account));
            DateTimeOffset stamp = new(2026, 11, 8, 10, 0, 0, TimeSpan.Zero);
            Account account = new()
            {
                Id = -5,
                CreatedAt = stamp,
                CreatedById = 3,
                ModifiedAt = stamp,
                ModifiedById = 4,
                IsActive = false,
                Evidence = "client",
                Code = "1101",
                Origin = "import",
                Total = 99,
                Normalized = "client",
            };

            metadata.ResetServerOwned(account);

            // A temporary id survives, so the rows that reference it can be rewritten after the reset.
            Assert.Equal(-5, account.Id);
            Assert.Equal((0, 0), (account.CreatedById, account.ModifiedById));
            Assert.Equal(DateTimeOffset.MinValue, account.CreatedAt);
            Assert.Equal(DateTimeOffset.MinValue, account.ModifiedAt);
            Assert.Equal((true, "unverified"), (account.IsActive, account.Evidence));
            Assert.Equal(("1101", "import", 99m, "client"), (account.Code, account.Origin, account.Total, account.Normalized));
        }

        [Fact]
        public void ResetDerived_restores_every_derived_property_and_nothing_else()
        {
            EntityMetadata metadata = _provider.Get(typeof(Account));
            Account account = new() { Id = 5, Evidence = "client", Code = "1101", Total = 99, Normalized = "client" };

            metadata.ResetDerived(account);

            Assert.Equal((0m, "none"), (account.Total, account.Normalized));
            Assert.Equal((5, "client", "1101"), (account.Id, account.Evidence, account.Code));
        }

        [Fact]
        public void A_reset_refuses_an_instance_of_another_class()
        {
            EntityMetadata region = _provider.Get(typeof(Region));

            ArgumentException exception = Assert.Throws<ArgumentException>(() => region.ResetServerOwned(new Region()));
            Assert.Contains("not an instance of MyRegion, the leaf of geo.Region", exception.Message, StringComparison.Ordinal);
            _ = Assert.Throws<ArgumentNullException>(() => region.ResetDerived(null!));
        }

        // --- Natural keys ---

        [Fact]
        public void Natural_keys_are_the_declared_ones_by_order_then_the_inferred_name_code_required_and_nullable_strings()
        {
            IReadOnlyList<NaturalKeyMetadata> keys = _provider.Get(typeof(Account)).NaturalKeys;

            Assert.Equal(
                [("Ref", true, 0), ("Number", true, 1), ("Name", false, 2), ("Code", false, 3), ("Subject", false, 4), ("Alias", false, 5)],
                keys.Select(static key => (key.Property.Name, key.IsDeclared, key.Order)));
            Assert.Same(keys[0].Property, _provider.Get(typeof(Account)).NaturalKey);
        }

        [Fact]
        public void A_childs_key_is_inferred_over_its_per_parent_unique_strings()
        {
            Assert.Equal(["Label"], _provider.Get(typeof(AccountLine)).NaturalKeys.Select(static key => key.Property.Name));
        }

        [Fact]
        public void An_entity_without_a_unique_scalar_has_no_natural_key()
        {
            EntityMetadata carrier = _provider.Get(typeof(Carrier));

            Assert.Empty(carrier.NaturalKeys);
            Assert.Null(carrier.NaturalKey);
        }

        [Fact]
        public void A_declared_key_on_a_leaf_is_inherited_from_its_default()
        {
            Assert.Equal("Code", _provider.Get(typeof(MyRegion)).NaturalKey!.Name);
            Assert.Equal("Email", _provider.Get(typeof(MyMdUser)).NaturalKey!.Name);
        }

        // --- Groups, unique indexes, trees, blobs, flags ---

        [Fact]
        public void A_multilingual_group_pairs_its_anchor_with_its_twins()
        {
            MultilingualGroup group = Assert.Single(_provider.Get(typeof(MyRegion)).MultilingualGroups);

            Assert.Equal(("Name", "Name", "Name2", "Name3"), (group.Name, group.Primary.Name, group.Secondary!.Name, group.Tertiary!.Name));
        }

        [Fact]
        public void Unique_indexes_are_listed_by_name_with_their_properties_and_the_node_index_is_left_out()
        {
            Assert.Equal(
                ["UX_Accounts_Alias", "UX_Accounts_Code", "UX_Accounts_ImageId", "UX_Accounts_Name", "UX_Accounts_Number", "UX_Accounts_Ref", "UX_Accounts_Subject"],
                _provider.Get(typeof(Account)).UniqueIndexes.Select(static index => index.IndexName));
            Assert.Equal(["UX_Regions_Code"], _provider.Get(typeof(MyRegion)).UniqueIndexes.Select(static index => index.IndexName));

            UniqueIndexMetadata ordinal = _provider.Get(typeof(AccountLine)).UniqueIndexes.Single(static index => index.IndexName == "UX_AccountLines_AccountId_Ordinal");
            Assert.Equal(["AccountId", "Ordinal"], ordinal.Properties.Select(static property => property.Name));
        }

        [Fact]
        public void A_tree_records_its_columns_and_depth()
        {
            TreeMetadata tree = _provider.Get(typeof(MyRegion)).Tree!;

            Assert.Equal(("ParentId", "Node", "SubtreeCount", "ActiveSubtreeCount", 8), (tree.ParentKey.Name, tree.NodeColumn, tree.SubtreeCount.Name, tree.ActiveSubtreeCount!.Name, tree.MaxDepth));
            Assert.Null(_provider.Get(typeof(Account)).Tree);
        }

        [Fact]
        public void A_blob_reference_carries_its_kind_and_its_presets_policy()
        {
            BlobReferenceMetadata image = Assert.Single(_provider.Get(typeof(Account)).BlobReferences);

            Assert.Equal(("ImageId", "account-image"), (image.Property.Name, image.Kind));
            Assert.Equal(BlobKindPolicy.For(BlobPreset.Avatar) with { ReadAccess = BlobReadAccess.OwnerRead }, image.Policy);
        }

        [Fact]
        public void The_capability_flags_follow_the_bases_interfaces_and_annotations()
        {
            EntityMetadata account = _provider.Get(typeof(Account));
            EntityMetadata line = _provider.Get(typeof(AccountLine));
            EntityMetadata note = _provider.Get(typeof(AccountLineNote));
            EntityMetadata carrier = _provider.Get(typeof(Carrier));

            Assert.Equal((true, true, true, false, true), (account.IsTopLevel, account.IsTemporal, account.IsActivatable, account.IsJobEntity, account.IsCacheable));
            Assert.Equal((false, true), (line.IsTopLevel, line.IsTemporal));
            Assert.True(note.IsTemporal);
            Assert.Equal((true, false, false, false, false), (carrier.IsTopLevel, carrier.IsTemporal, carrier.IsActivatable, carrier.IsJobEntity, carrier.IsCacheable));
        }

        // --- Display names ---

        [Fact]
        public void A_declared_display_name_resolves_its_source_group_and_prefix()
        {
            DisplayNameMetadata displayName = _provider.Get(typeof(Account)).DisplayName;

            Assert.Equal(("Name", "Name", "Code"), (displayName.Source.Name, displayName.Group!.Name, displayName.Prefix!.Name));
            Assert.Equal(" - ", DisplayNameMetadata.Separator);
        }

        [Theory]
        [InlineData(typeof(MyRegion), "Name", true)]
        [InlineData(typeof(MyMdUser), "Name", true)]
        [InlineData(typeof(AccountLine), "Label", false)]
        [InlineData(typeof(Carrier), "Id", false)]
        [InlineData(typeof(Shipment), "Id", false)]
        public void Without_a_declaration_the_display_name_is_the_name_group_else_the_first_natural_key_else_the_key(Type entity, string source, bool grouped)
        {
            DisplayNameMetadata displayName = _provider.Get(entity).DisplayName;

            Assert.Equal((source, grouped), (displayName.Source.Name, displayName.Group is not null));
            Assert.Null(displayName.Prefix);
        }

        // --- Children and references ---

        [Fact]
        public void A_child_collection_pairs_with_its_childs_parent_key_and_cap()
        {
            ChildCollectionMetadata lines = Assert.Single(_provider.Get(typeof(Account)).Children);
            ChildCollectionMetadata notes = Assert.Single(_provider.Get(typeof(AccountLine)).Children);

            Assert.Equal(("Lines", typeof(AccountLine), "AccountId", 500), (lines.Property, lines.Child.ClrType, lines.ParentKey.Name, lines.MaxCount));
            Assert.Same(_provider.Get(typeof(AccountLine)), lines.Child);
            Assert.Equal(("Notes", "AccountLineId", MaxChildrenAttribute.DefaultCount), (notes.Property, notes.ParentKey.Name, notes.MaxCount));
            Assert.Empty(_provider.Get(typeof(AccountLineNote)).Children);
        }

        [Fact]
        public void A_child_collections_getter_tells_absent_from_empty()
        {
            ChildCollectionMetadata lines = Assert.Single(_provider.Get(typeof(Account)).Children);
            AccountLine line = new() { Label = "first" };

            Assert.Null(lines.Getter(new Account()));
            Assert.Empty(lines.Getter(new Account { Lines = [] })!);
            Assert.Same(line, Assert.Single(lines.Getter(new Account { Lines = [line] })!));
        }

        [Fact]
        public void References_are_every_many_to_one_foreign_key_named_by_the_column_without_its_id()
        {
            Assert.Equal(
                [("CreatedBy", "CreatedById", typeof(MyMdUser)), ("ModifiedBy", "ModifiedById", typeof(MyMdUser)), ("ParentAccount", "ParentAccountId", typeof(Account)), ("Image", "ImageId", typeof(MdBlob))],
                References(typeof(Account)));
            Assert.Equal(
                [("CreatedBy", "CreatedById", typeof(MyMdUser)), ("ModifiedBy", "ModifiedById", typeof(MyMdUser)), ("Parent", "ParentId", typeof(MyRegion))],
                References(typeof(MyRegion)));
            Assert.Equal(
                [("CreatedBy", "CreatedById", typeof(MyMdUser)), ("ModifiedBy", "ModifiedById", typeof(MyMdUser)), ("Job", "JobId", typeof(MdJob)), ("Region", "RegionId", typeof(MyRegion)), ("Carrier", "CarrierId", typeof(Carrier))],
                References(typeof(Shipment)));
            Assert.Equal([("Account", "AccountId", typeof(Account))], References(typeof(AccountLine)));
        }

        [Fact]
        public void A_siblings_key_is_its_owners_and_no_reference()
        {
            Assert.Empty(_provider.Get(typeof(ShipmentExtra)).References);
        }

        [Fact]
        public void The_one_warning_names_the_referenced_entity_without_a_natural_key_and_who_references_it()
        {
            EntityMetadataWarning warning = Assert.Single(model.Build.Warnings);

            Assert.Equal("fixture.Carrier", warning.Entity);
            Assert.Contains("referenced by fixture.Shipment.Carrier but has no natural key", warning.Message, StringComparison.Ordinal);
        }

        /// <summary>A property of an entity's metadata.</summary>
        /// <param name="entity">The entity class.</param>
        /// <param name="name">The property name.</param>
        /// <returns>The property's metadata.</returns>
        private PropertyMetadata Property(Type entity, string name)
        {
            return _provider.Get(entity).Properties.Single(property => property.Name == name);
        }

        /// <summary>An entity's references as (navigation, foreign key, target leaf).</summary>
        /// <param name="entity">The entity class.</param>
        /// <returns>The references.</returns>
        private IEnumerable<(string, string, Type)> References(Type entity)
        {
            return _provider.Get(entity).References.Select(static reference => (reference.Navigation, reference.ForeignKey.Name, reference.Target.ClrType));
        }
    }

    /// <summary>The sound model's metadata, built once for a test class.</summary>
    public sealed class SoundMetadataModel
    {
        /// <summary>The outcome of building the sound model's metadata.</summary>
        internal EntityMetadataBuild Build { get; } = MetadataModels.Build(MetadataModels.SoundDefinition());

        /// <summary>The sound model's provider.</summary>
        public IEntityMetadataProvider Provider => Build.Provider ?? throw new InvalidOperationException(
            string.Join(Environment.NewLine, Build.Problems.Select(static problem => problem.Describe())));
    }
}
