// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Reflection;
using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Entities;

namespace Tellma.Core.Abstractions.Tests.Entities
{
    /// <summary>
    ///     The entity contract: the bases and the members each adds, the capability interfaces, and
    ///     every annotation's target, inheritance, repeatability and defaults.
    /// </summary>
    public class EntityContractTests
    {
        [Fact]
        public void Each_base_adds_exactly_its_own_members()
        {
            Assert.Equal(["Id"], DeclaredProperties(typeof(Entity<>)));
            Assert.Equal(["CreatedAt", "CreatedById", "ModifiedAt", "ModifiedById"], DeclaredProperties(typeof(TopLevelEntity<>)));
            Assert.Empty(DeclaredProperties(typeof(ChildEntity<>)));
            Assert.Equal(["ParentId", "SubtreeCount"], DeclaredProperties(typeof(TreeEntity<>)));
            Assert.Equal(["IsActive", "ActiveSubtreeCount"], DeclaredProperties(typeof(ActivatableTreeEntity<>)));
        }

        [Fact]
        public void The_non_generic_bases_close_over_int_keys()
        {
            Assert.Equal(typeof(TopLevelEntity<int>), typeof(TopLevelEntity).BaseType);
            Assert.Equal(typeof(ChildEntity<int>), typeof(ChildEntity).BaseType);
            Assert.Equal(typeof(TreeEntity<int>), typeof(TreeEntity).BaseType);
            Assert.Equal(typeof(ActivatableTreeEntity<int>), typeof(ActivatableTreeEntity).BaseType);
            Assert.All(
                [typeof(Entity<>), typeof(TopLevelEntity), typeof(ChildEntity), typeof(TreeEntity), typeof(ActivatableTreeEntity)],
                static type => Assert.True(type.IsAbstract));
        }

        [Fact]
        public void A_trees_parent_is_a_nullable_key_of_its_key_type()
        {
            Assert.Equal(typeof(long?), typeof(TreeEntity<long>).GetProperty(nameof(TreeEntity.ParentId))!.PropertyType);
            Assert.Equal(typeof(int?), typeof(TreeEntity).GetProperty(nameof(TreeEntity.ParentId))!.PropertyType);
        }

        [Fact]
        public void An_activatable_tree_row_is_created_active_and_is_activatable()
        {
            SampleCenter center = new();

            Assert.True(center.IsActive);
            Assert.IsAssignableFrom<IActivatable>(center);
            Assert.Equal(0, center.Id);
        }

        [Fact]
        public void The_capability_interfaces_carry_one_property_each()
        {
            Assert.Equal([nameof(IActivatable.IsActive)], typeof(IActivatable).GetProperties().Select(static property => property.Name));
            Assert.Equal(typeof(int?), Assert.Single(typeof(IJobEntity).GetProperties()).PropertyType);
        }

        [Theory]
        [InlineData(typeof(TemporalAttribute), AttributeTargets.Class, false)]
        [InlineData(typeof(TableTypeAttribute), AttributeTargets.Class, false)]
        [InlineData(typeof(ExcludeFromTableTypeAttribute), AttributeTargets.Property, false)]
        [InlineData(typeof(CollationAttribute), AttributeTargets.Property, false)]
        [InlineData(typeof(TreeAttribute), AttributeTargets.Class, false)]
        [InlineData(typeof(ParentKeyAttribute), AttributeTargets.Property, false)]
        [InlineData(typeof(ReferencesAttribute), AttributeTargets.Property, false)]
        [InlineData(typeof(TableIndexAttribute), AttributeTargets.Class, true)]
        [InlineData(typeof(MaxChildrenAttribute), AttributeTargets.Property, false)]
        [InlineData(typeof(ServerOwnedAttribute), AttributeTargets.Property, false)]
        [InlineData(typeof(WriteOnceAttribute), AttributeTargets.Property, false)]
        [InlineData(typeof(DerivedAttribute), AttributeTargets.Property, false)]
        [InlineData(typeof(DefaultedAttribute), AttributeTargets.Property, false)]
        [InlineData(typeof(NaturalKeyAttribute), AttributeTargets.Property, false)]
        [InlineData(typeof(UniqueAttribute), AttributeTargets.Property, false)]
        [InlineData(typeof(SearchableAttribute), AttributeTargets.Property, false)]
        [InlineData(typeof(PreserveWhitespaceAttribute), AttributeTargets.Property, false)]
        [InlineData(typeof(JsonColumnAttribute), AttributeTargets.Property, false)]
        [InlineData(typeof(MultilingualAttribute), AttributeTargets.Property, false)]
        [InlineData(typeof(DisplayNameSourceAttribute), AttributeTargets.Class, false)]
        [InlineData(typeof(CacheableAttribute), AttributeTargets.Class, false)]
        [InlineData(typeof(BumpsVersionTagAttribute), AttributeTargets.Class, true)]
        [InlineData(typeof(BumpsUserVersionTagAttribute), AttributeTargets.Class, true)]
        [InlineData(typeof(ExcludeFromExcelAttribute), AttributeTargets.Property, false)]
        [InlineData(typeof(SiblingAttribute), AttributeTargets.Class, false)]
        public void Each_annotation_has_its_target_and_is_inherited_by_a_leaf(Type attribute, AttributeTargets target, bool allowMultiple)
        {
            AttributeUsageAttribute usage = attribute.GetCustomAttribute<AttributeUsageAttribute>()!;

            Assert.Equal((target, allowMultiple, true), (usage.ValidOn, usage.AllowMultiple, usage.Inherited));
            Assert.True(attribute.IsSealed);
        }

        [Fact]
        public void The_binder_marker_is_declared_per_type_and_not_inherited()
        {
            AttributeUsageAttribute usage = typeof(TableTypeBinderAttribute).GetCustomAttribute<AttributeUsageAttribute>()!;

            Assert.Equal((AttributeTargets.Class, false, false), (usage.ValidOn, usage.AllowMultiple, usage.Inherited));
        }

        [Fact]
        public void Annotations_default_as_the_platform_reads_them()
        {
            Assert.True(new TemporalAttribute().Enabled);
            Assert.False(new TemporalAttribute(false).Enabled);
            Assert.Equal(32, new TreeAttribute().MaxDepth);
            Assert.Equal(TreeAttribute.DefaultMaxDepth, new TreeAttribute().MaxDepth);
            Assert.Equal(10_000, MaxChildrenAttribute.DefaultCount);
            Assert.Equal(0, new NaturalKeyAttribute().Order);
            Assert.Equal(SearchKind.Contains, new SearchableAttribute().Kind);
            Assert.Equal(0, new CacheableAttribute().MaxRows);
            Assert.Equal("UserId", new BumpsUserVersionTagAttribute(UserVersionTagNames.Permissions).UserIdProperty);
            Assert.Null(new DisplayNameSourceAttribute("Name").Prefix);
            Assert.Null(new TableTypeAttribute().Name);
            Assert.Null(new TableTypeAttribute().Schema);
            Assert.Equal(("2", "3"), (MultilingualAttribute.SecondarySuffix, MultilingualAttribute.TertiarySuffix));
        }

        [Fact]
        public void Annotations_keep_what_they_declare()
        {
            Assert.Equal(["Kind", "Price"], new TableIndexAttribute("Kind", "Price").Properties);
            Assert.Equal(typeof(SampleCenter), new ReferencesAttribute(typeof(SampleCenter)).Target);
            Assert.Equal((typeof(SampleCenter), "Extra"), (new SiblingAttribute(typeof(SampleCenter), "Extra").Owner, new SiblingAttribute(typeof(SampleCenter), "Extra").Navigation));
            Assert.Equal("Latin1_General_100_BIN2", new CollationAttribute("Latin1_General_100_BIN2").Name);
            Assert.Equal("permissions", new BumpsVersionTagAttribute("permissions").Name);
            Assert.Equal((UserVersionTagNames.Preferences, "Id"), (new BumpsUserVersionTagAttribute(UserVersionTagNames.Preferences, "Id").Column, new BumpsUserVersionTagAttribute(UserVersionTagNames.Preferences, "Id").UserIdProperty));
            Assert.Equal(8, new TreeAttribute(8).MaxDepth);
            Assert.Equal(50, new MaxChildrenAttribute(50).Count);
            Assert.Equal(SearchKind.Prefix, new SearchableAttribute(SearchKind.Prefix).Kind);
        }

        [Fact]
        public void A_leaf_reads_its_defaults_annotations_and_repeatable_ones_accumulate()
        {
            Assert.Equal(8, typeof(SampleCenterLeaf).GetCustomAttribute<TreeAttribute>(inherit: true)!.MaxDepth);
            Assert.Equal(2, typeof(SampleCenterLeaf).GetCustomAttributes<TableIndexAttribute>(inherit: true).Count());
            Assert.NotNull(typeof(SampleCenterLeaf).GetCustomAttribute<TableTypeAttribute>(inherit: true));
        }

        [Fact]
        public void The_contract_types_live_in_the_entities_namespace()
        {
            Assert.All(
                typeof(Entity<>).Assembly.GetExportedTypes().Where(static type => type.Name.EndsWith("Attribute", StringComparison.Ordinal)
                    && type.Namespace == "Tellma.Core.Abstractions.Entities"),
                static type => Assert.True(typeof(Attribute).IsAssignableFrom(type)));
        }

        private static string[] DeclaredProperties(Type type)
        {
            return [.. type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Select(static property => property.Name)];
        }

        /// <summary>A pack's default tree.</summary>
        [TableType]
        [Tree(MaxDepth = 8)]
        [TableIndex("Code")]
        public class SampleCenter : ActivatableTreeEntity
        {
            public string? Code { get; set; }
        }

        /// <summary>A distribution's leaf of <see cref="SampleCenter" />.</summary>
        [TableIndex("Code", "Region")]
        public sealed class SampleCenterLeaf : SampleCenter
        {
            public string? Region { get; set; }
        }
    }
}
