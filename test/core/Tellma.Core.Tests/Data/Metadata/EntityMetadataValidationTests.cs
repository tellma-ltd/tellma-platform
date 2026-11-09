// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Data.Metadata;
using Tellma.Core.Tests.Data.Metadata.Malformed;

namespace Tellma.Core.Tests.Data.Metadata
{
    /// <summary>
    ///     The entity contract's startup rules, each as a model that breaks it once: the metadata
    ///     reports the problem, naming the entity and the member, and builds no provider.
    /// </summary>
    public class EntityMetadataValidationTests
    {
        // --- Tables and column names ---

        [Fact]
        public void An_entity_without_a_table_declaration_is_refused()
        {
            Assert.Contains("Malformed.Untabled declares no [Table]", MetadataModels.SingleProblem(typeof(Untabled)), StringComparison.Ordinal);
        }

        [Fact]
        public void An_entity_in_the_dbo_schema_is_refused()
        {
            Assert.Contains("maps to [dbo].[Dboed]; the dbo schema holds only the platform's own tables", MetadataModels.SingleProblem(typeof(Dboed)), StringComparison.Ordinal);
        }

        [Fact]
        public void A_leaf_that_redeclares_another_table_is_refused()
        {
            string problem = MetadataModels.SingleProblem(MetadataModels.Build(
                [typeof(Tabled)], [], new Dictionary<Type, Type> { [typeof(Tabled)] = typeof(RetabledLeaf) }));

            Assert.Contains("RetabledLeaf re-declares [Table(\"Retabled\", Schema = \"fixture\")], but its default", problem, StringComparison.Ordinal);
        }

        [Fact]
        public void A_leaf_that_redeclares_its_defaults_table_is_sound()
        {
            EntityMetadataBuild build = MetadataModels.Build([typeof(Tabled)], [], new Dictionary<Type, Type> { [typeof(Tabled)] = typeof(SameTabledLeaf) });

            Assert.Empty(build.Problems);
            Assert.Equal("fixture.Tabled", build.Provider!.Get(typeof(Tabled)).Name);
        }

        [Fact]
        public void A_column_name_on_a_default_leafs_property_is_refused_and_a_column_type_is_allowed()
        {
            string problem = MetadataModels.SingleProblem(typeof(Renamed));

            Assert.Contains("maps Code to the column code_column", problem, StringComparison.Ordinal);
            Assert.DoesNotContain("Typed", problem, StringComparison.Ordinal);
        }

        // --- Capability columns and table types ---

        [Fact]
        public void A_capability_member_implemented_explicitly_is_refused()
        {
            Assert.Contains("ExplicitActive implements IActivatable.IsActive explicitly, so the model cannot map the column", MetadataModels.SingleProblem(typeof(ExplicitActive)), StringComparison.Ordinal);
        }

        [Fact]
        public void A_top_level_entity_without_a_table_type_is_refused()
        {
            Assert.Contains("Malformed.Untyped has no table type", MetadataModels.SingleProblem(typeof(Untyped)), StringComparison.Ordinal);
        }

        [Fact]
        public void A_capability_column_left_out_of_the_table_type_is_refused()
        {
            string problem = MetadataModels.SingleProblem(MetadataModels.Build([typeof(Excluding)], [typeof(ExcludingConfiguration)]));

            Assert.Contains("The table type of Tellma.Core.Tests.Data.Metadata.Malformed.Excluding leaves out ModifiedAt", problem, StringComparison.Ordinal);
        }

        [Fact]
        public void A_shadow_node_column_inside_the_table_type_is_refused()
        {
            string problem = MetadataModels.SingleProblem(MetadataModels.Build([typeof(NodeTyped)], [typeof(NodeTypedConfiguration)]));

            Assert.Contains("NodeTyped carries the column Node, which the server alone writes", problem, StringComparison.Ordinal);
        }

        // --- Trees ---

        [Theory]
        [InlineData(typeof(ShallowTree), 1)]
        [InlineData(typeof(DeepTree), 40)]
        public void A_tree_depth_outside_two_to_thirty_two_is_refused(Type tree, int depth)
        {
            Assert.Contains($"declares [Tree(MaxDepth = {depth})]; a tree's depth is between 2 and 32", MetadataModels.SingleProblem(tree), StringComparison.Ordinal);
        }

        [Fact]
        public void A_tree_without_its_unique_node_index_is_refused()
        {
            string problem = MetadataModels.SingleProblem(MetadataModels.Build([typeof(RenodedTree)], [typeof(RenodedTreeConfiguration)]));

            Assert.Contains("has no unique index UX_Renode_Node over its node column", problem, StringComparison.Ordinal);
        }

        // --- Children ---

        [Fact]
        public void A_child_with_two_parent_keys_is_refused()
        {
            Assert.Contains("TwoKeyed declares [ParentKey] on BadParentId and OtherParentId; a child has exactly one parent key", MetadataModels.SingleProblem(typeof(BadParent)), StringComparison.Ordinal);
        }

        [Fact]
        public void A_child_without_a_parent_key_is_refused()
        {
            Assert.Contains("Keyless declares no [ParentKey]", MetadataModels.SingleProblem(typeof(Orphanage)), StringComparison.Ordinal);
        }

        [Fact]
        public void A_parent_key_on_a_top_level_entity_is_refused()
        {
            Assert.Contains("Misparented declares [ParentKey] on TabledId, but it is not a child entity", MetadataModels.SingleProblem(typeof(Misparented), typeof(Tabled)), StringComparison.Ordinal);
        }

        [Fact]
        public void A_not_mapped_list_of_a_non_child_class_is_refused()
        {
            Assert.Contains("declares the [NotMapped] list Others of Tabled, which is not a child entity", MetadataModels.SingleProblem(typeof(Lister)), StringComparison.Ordinal);
        }

        [Fact]
        public void A_child_whose_aggregate_has_no_top_level_root_is_refused()
        {
            Assert.Contains("SystemChild belongs to Tellma.Core.Tests.Data.Metadata.Malformed.SystemParent, which is not a top-level entity", MetadataModels.SingleProblem(typeof(SystemParent)), StringComparison.Ordinal);
        }

        [Fact]
        public void A_child_no_collection_carries_is_refused()
        {
            Assert.Contains("Stray is in the model, but no entity's collection carries it", MetadataModels.SingleProblem(typeof(Stray), typeof(Tabled)), StringComparison.Ordinal);
        }

        [Fact]
        public void A_child_cap_below_one_is_refused()
        {
            Assert.Contains("Capped.Children declares [MaxChildren(0)]", MetadataModels.SingleProblem(typeof(Capped)), StringComparison.Ordinal);
        }

        [Fact]
        public void Children_that_carry_each_other_are_refused()
        {
            IReadOnlyList<CompositionProblem> problems = MetadataModels.Build(typeof(CycleA)).Problems;

            Assert.Equal(2, problems.Count);
            Assert.All(problems, static problem => Assert.Contains("is carried by a cycle of child collections", problem.Problem, StringComparison.Ordinal));
        }

        // --- Temporal ---

        [Fact]
        public void A_temporal_child_of_a_non_temporal_root_is_refused()
        {
            Assert.Contains("VersionedChild is temporal, but its aggregate's root", MetadataModels.SingleProblem(typeof(PlainRoot)), StringComparison.Ordinal);
        }

        // --- Multilingual ---

        [Fact]
        public void A_multilingual_group_missing_a_twin_is_refused()
        {
            Assert.Contains("declares the multilingual group Title, but maps no twin Title3", MetadataModels.SingleProblem(typeof(TwinLess)), StringComparison.Ordinal);
        }

        [Fact]
        public void A_twin_of_another_length_is_refused()
        {
            Assert.Contains("maps the twin Title2 as nvarchar(50), but its anchor Title is nvarchar(100)", MetadataModels.SingleProblem(typeof(Mismatched)), StringComparison.Ordinal);
        }

        [Fact]
        public void A_required_twin_is_refused()
        {
            Assert.Contains("maps the twin Title3 as nvarchar(100) NOT NULL", MetadataModels.SingleProblem(typeof(RequiredTwin)), StringComparison.Ordinal);
        }

        // --- Display names ---

        [Fact]
        public void A_display_name_source_the_entity_lacks_is_refused()
        {
            Assert.Contains("declares [DisplayNameSource(\"Missing\")], which names no mapped property", MetadataModels.SingleProblem(typeof(Unsourced)), StringComparison.Ordinal);
        }

        [Fact]
        public void A_display_name_source_of_an_unrenderable_type_is_refused()
        {
            Assert.Contains("declares [DisplayNameSource(\"Flag\")], a bool", MetadataModels.SingleProblem(typeof(FlagNamed)), StringComparison.Ordinal);
        }

        [Fact]
        public void A_display_name_prefix_that_is_not_a_string_or_integer_is_refused()
        {
            Assert.Contains("declares the display-name prefix Amount, a decimal", MetadataModels.SingleProblem(typeof(AmountPrefixed)), StringComparison.Ordinal);
        }

        [Fact]
        public void A_date_source_with_an_integer_prefix_is_sound()
        {
            EntityMetadataBuild build = MetadataModels.Build(typeof(Dated));

            Assert.Empty(build.Problems);
            Assert.Equal(("On", "Serial"), (build.Provider!.Get(typeof(Dated)).DisplayName.Source.Name, build.Provider.Get(typeof(Dated)).DisplayName.Prefix!.Name));
        }

        [Fact]
        public void A_related_projection_that_drops_display_name_columns_or_the_code_is_refused()
        {
            Assert.Contains("[RelatedSelect(\"Id,Name\")], which leaves out Name2, Name3, Code", MetadataModels.SingleProblem(typeof(Projected)), StringComparison.Ordinal);
        }

        [Fact]
        public void A_related_projection_that_keeps_them_is_sound()
        {
            Assert.Empty(MetadataModels.Build(typeof(FullyProjected)).Problems);
        }

        // --- Uniqueness ---

        [Fact]
        public void A_unique_json_column_is_refused()
        {
            Assert.Contains("declares [Unique] on Settings, a JSON column", MetadataModels.SingleProblem(typeof(UniqueJson)), StringComparison.Ordinal);
        }

        [Fact]
        public void A_natural_key_of_a_type_without_a_key_list_is_refused()
        {
            Assert.Contains("declares [NaturalKey] on Rate, a decimal; a natural key is a string, int, long, Guid or DateOnly", MetadataModels.SingleProblem(typeof(DecimalKeyed)), StringComparison.Ordinal);
        }

        [Fact]
        public void A_natural_key_on_a_binary_column_is_refused()
        {
            IReadOnlyList<CompositionProblem> problems = MetadataModels.Build(typeof(BinaryKeyed)).Problems;

            Assert.Contains(problems, static problem => problem.Problem.Contains("declares [NaturalKey] on Hash, a binary column", StringComparison.Ordinal));
        }

        [Fact]
        public void A_unique_property_without_a_unique_index_is_refused()
        {
            string problem = MetadataModels.SingleProblem(MetadataModels.Build([typeof(Ununique)], [typeof(UnuniqueConfiguration)]));

            Assert.Contains("declares [Unique] on Code, but no unique index backs it", problem, StringComparison.Ordinal);
        }

        // --- Navigations ---

        [Fact]
        public void A_navigation_named_like_a_property_is_refused()
        {
            Assert.Contains("The navigation Owner that the foreign key OwnerId gives", MetadataModels.SingleProblem(typeof(Colliding), typeof(Tabled)), StringComparison.Ordinal);
        }

        [Fact]
        public void Two_navigations_of_one_name_are_refused()
        {
            string problem = MetadataModels.SingleProblem(typeof(SiblingOwner), typeof(SiblingOwnerExtra), typeof(Tabled));

            Assert.Contains("SiblingOwner has two navigations named Extra, from the foreign key ExtraId and the sibling", problem, StringComparison.Ordinal);
        }

        [Fact]
        public void A_foreign_key_whose_name_gives_no_navigation_is_refused()
        {
            Assert.Contains("Unnavigable.OwnerRef does not end in Id", MetadataModels.SingleProblem(typeof(Unnavigable), typeof(Tabled)), StringComparison.Ordinal);
        }

        [Fact]
        public void A_foreign_key_over_a_shadow_column_is_refused()
        {
            string problem = MetadataModels.SingleProblem(MetadataModels.Build([typeof(ShadowKeyed)], [typeof(ShadowKeyedConfiguration)]));

            Assert.Contains("has a foreign key over ShadowJobId, which is not a property of the class", problem, StringComparison.Ordinal);
        }

        // --- Blob references ---

        [Fact]
        public void A_blob_reference_on_a_non_nullable_column_is_refused()
        {
            Assert.Contains("declares [BlobReference] on ImageId, a int; a blob reference is an int? column", MetadataModels.SingleProblem(typeof(RequiredBlob)), StringComparison.Ordinal);
        }

        [Fact]
        public void A_blob_kind_outside_the_kind_grammar_is_refused()
        {
            Assert.Contains("declares the blob kind 'Bad Kind' on FileId, which is not a valid kind", MetadataModels.SingleProblem(typeof(BadKind)), StringComparison.Ordinal);
        }

        [Fact]
        public void A_blob_kind_two_properties_declare_is_refused()
        {
            Assert.Contains("The blob kind 'shared-file' is declared by both", MetadataModels.SingleProblem(typeof(SharedKind)), StringComparison.Ordinal);
        }

        [Fact]
        public void A_blob_reference_that_widens_its_preset_is_refused()
        {
            Assert.Contains("cannot make its policy", MetadataModels.SingleProblem(typeof(Widened)), StringComparison.Ordinal);
        }

        // --- Enums ---

        [Theory]
        [InlineData(typeof(NumberedPhase), typeof(NumberedPhaseConfiguration), "int")]
        [InlineData(typeof(UnicodePhase), typeof(UnicodePhaseConfiguration), "nvarchar(40)")]
        public void An_enum_not_stored_by_name_in_a_sized_varchar_is_refused(Type entity, Type configuration, string storeType)
        {
            string problem = MetadataModels.SingleProblem(MetadataModels.Build([entity], [configuration]));

            Assert.Contains($"stores the enum Phase as {storeType}; every enum is stored by member name in a sized varchar", problem, StringComparison.Ordinal);
        }

        // --- Ownership ---

        [Fact]
        public void Two_ownership_annotations_on_one_property_are_refused()
        {
            Assert.Contains("declares [WriteOnce], [Derived] on Code; a property has at most one ownership", MetadataModels.SingleProblem(typeof(DoublyOwned)), StringComparison.Ordinal);
        }

        [Fact]
        public void An_ownership_annotation_on_a_parent_key_is_refused()
        {
            Assert.Contains("declares [ServerOwned] on OwnedKeyParentId, which is the child's parent key", MetadataModels.SingleProblem(typeof(OwnedKeyParent)), StringComparison.Ordinal);
        }

        [Fact]
        public void An_ownership_annotation_on_a_computed_column_is_refused()
        {
            string problem = MetadataModels.SingleProblem(MetadataModels.Build([typeof(DerivedComputed)], [typeof(DerivedComputedConfiguration)]));

            Assert.Contains("declares [Derived] on Upper, which is a database-computed column", problem, StringComparison.Ordinal);
        }

        [Fact]
        public void An_ownership_annotation_against_the_derivation_is_refused()
        {
            Assert.Contains("declares [WriteOnce] on JobId, which the server owns by its base or capability interface", MetadataModels.SingleProblem(typeof(WriteOnceJob)), StringComparison.Ordinal);
        }

        [Fact]
        public void A_defaulted_property_that_is_not_editable_is_refused()
        {
            Assert.Contains("declares [Defaulted] on Code, which is ServerOwned", MetadataModels.SingleProblem(typeof(DefaultedOwned)), StringComparison.Ordinal);
        }

        // --- Annotations and members ---

        [Fact]
        public void A_column_annotation_on_an_unmapped_property_is_refused()
        {
            Assert.Contains("declares [Unique] on Display, which the model does not map", MetadataModels.SingleProblem(typeof(UnmappedUnique)), StringComparison.Ordinal);
        }

        [Fact]
        public void A_string_annotation_on_a_number_is_refused()
        {
            Assert.Contains("declares [Searchable] on Count, a int; it applies to a string property only", MetadataModels.SingleProblem(typeof(SearchedNumber)), StringComparison.Ordinal);
        }

        [Fact]
        public void A_mapped_member_that_is_not_a_public_property_is_refused()
        {
            string problem = MetadataModels.SingleProblem(MetadataModels.Build([typeof(Secretive)], [typeof(SecretiveConfiguration)]));

            Assert.Contains("maps Secret, which is not a public property of the class", problem, StringComparison.Ordinal);
        }

        // --- Leaves ---

        [Fact]
        public void A_contributed_class_that_is_not_an_entity_is_refused()
        {
            Assert.Contains("NotAnEntity does not derive from Entity<TKey>", MetadataModels.SingleProblem(typeof(NotAnEntity)), StringComparison.Ordinal);
        }

        [Fact]
        public void A_generic_entity_class_is_refused()
        {
            Assert.Contains("is a generic class, which cannot name an entity", MetadataModels.SingleProblem(typeof(GenericEntity<int>)), StringComparison.Ordinal);
        }

        [Fact]
        public void Two_leaves_that_share_a_base_are_refused()
        {
            string problem = MetadataModels.SingleProblem(typeof(Customer), typeof(Supplier));

            Assert.Contains("Customer and Tellma.Core.Tests.Data.Metadata.Malformed.Supplier both derive from Tellma.Core.Tests.Data.Metadata.Malformed.Party", problem, StringComparison.Ordinal);
        }

        [Fact]
        public void A_model_configuration_that_names_a_replaced_default_is_refused()
        {
            string problem = MetadataModels.SingleProblem(MetadataModels.Build(
                [typeof(Tabled)], [typeof(DefaultTabledConfiguration)], new Dictionary<Type, Type> { [typeof(Tabled)] = typeof(SameTabledLeaf) }));

            Assert.Contains("which UseEntity<Tabled, SameTabledLeaf>() replaced: a model configuration or navigation names the default", problem, StringComparison.Ordinal);
        }

        [Fact]
        public void Two_entities_that_share_an_entity_name_are_refused()
        {
            Assert.Contains("share the entity name 'fixture.Duplicate'", MetadataModels.SingleProblem(typeof(First.Duplicate), typeof(Second.Duplicate)), StringComparison.Ordinal);
        }

        [Fact]
        public void An_entity_mapped_to_a_view_is_refused()
        {
            string problem = MetadataModels.SingleProblem(MetadataModels.Build([typeof(Viewed)], [typeof(ViewedConfiguration)]));

            Assert.Contains("Malformed.Viewed is not mapped to a table", problem, StringComparison.Ordinal);
        }

        [Fact]
        public void An_entity_mapped_as_derived_from_another_is_refused()
        {
            Assert.Contains("maps Tellma.Core.Tests.Data.Metadata.Malformed.VipCustomer as derived from Tellma.Core.Tests.Data.Metadata.Malformed.Customer", MetadataModels.SingleProblem(typeof(Customer), typeof(VipCustomer)), StringComparison.Ordinal);
        }

        [Fact]
        public void A_leaf_registered_for_two_defaults_is_refused()
        {
            string problem = MetadataModels.SingleProblem(MetadataModels.Build(
                [typeof(FirstDefault)],
                [],
                new Dictionary<Type, Type> { [typeof(FirstDefault)] = typeof(TwoDefaultsLeaf), [typeof(SecondDefault)] = typeof(TwoDefaultsLeaf) }));

            Assert.Contains("TwoDefaultsLeaf replaces Tellma.Core.Tests.Data.Metadata.Malformed.FirstDefault and Tellma.Core.Tests.Data.Metadata.Malformed.SecondDefault", problem, StringComparison.Ordinal);
        }

        [Fact]
        public void Two_leaves_on_one_table_are_refused()
        {
            Assert.Contains("The table [fixture].[Split] is mapped by", MetadataModels.SingleProblem(typeof(SplitOwner), typeof(SplitHalf)), StringComparison.Ordinal);
        }

        [Fact]
        public void An_entity_without_a_parameterless_constructor_is_refused()
        {
            Assert.Contains("Constructed has no parameterless constructor", MetadataModels.SingleProblem(typeof(Constructed)), StringComparison.Ordinal);
        }

        // --- Every problem at once ---

        [Fact]
        public void A_malformed_model_reports_every_problem_at_once_and_builds_no_provider()
        {
            EntityMetadataBuild build = MetadataModels.Build(typeof(Untabled), typeof(Untyped), typeof(ShallowTree), typeof(TwinLess), typeof(Unsourced), typeof(DoublyOwned), typeof(BadKind));

            Assert.Equal(7, build.Problems.Count);
            Assert.All(build.Problems, static problem => Assert.Equal("core.entity-metadata", problem.Source));
            Assert.Null(build.Provider);
        }
    }
}
