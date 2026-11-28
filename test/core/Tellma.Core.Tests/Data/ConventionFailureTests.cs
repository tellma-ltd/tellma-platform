// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Tellma.Core.Abstractions.Entities;
using Tellma.Core.Data;
using Tellma.Core.Tests.Data.Model;

namespace Tellma.Core.Tests.Data
{
    /// <summary>
    ///     The declarations a model cannot be built with: each fails the model with the class and the
    ///     declaration named, rather than building a model that is silently wrong.
    /// </summary>
    public class ConventionFailureTests
    {
        [Fact]
        public void A_reference_to_an_entity_outside_the_model_names_the_contribution_to_add()
        {
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => TestModels.DesignModel(typeof(Referrer)));

            Assert.StartsWith("[References] on ", exception.Message, StringComparison.Ordinal);
            Assert.Contains("Referrer.TargetId names the entity", exception.Message, StringComparison.Ordinal);
            Assert.Contains("Target>() in the feature that owns it", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void A_reference_on_a_property_that_is_not_an_integer_key_is_refused()
        {
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => TestModels.DesignModel(typeof(StringReferrer), typeof(Target)));

            Assert.Contains("an int or long foreign key", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void A_reference_to_a_default_with_two_leaves_in_the_model_is_ambiguous()
        {
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => TestModels.DesignModel(typeof(Referrer), typeof(TargetLeafA), typeof(TargetLeafB)));

            Assert.Contains("TargetLeafA and ", exception.Message, StringComparison.Ordinal);
            Assert.Contains("Target, TLeaf>()", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void A_top_level_entity_without_the_users_table_names_the_missing_platform_table()
        {
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => TestModels.DesignModel(TestModels.Definition([typeof(Target)])));

            Assert.Contains("[core].[Users]", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void A_table_index_over_an_unknown_property_is_refused()
        {
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => TestModels.DesignModel(typeof(BadIndex)));

            Assert.Contains("names 'Missing'", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void A_key_that_is_neither_int_nor_long_is_refused()
        {
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => TestModels.DesignModel(typeof(ShortKeyed)));

            Assert.Contains("key of type Int16", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void A_blob_reference_that_is_not_an_int_column_is_refused()
        {
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => TestModels.DesignModel(typeof(BadBlob)));

            Assert.Contains("[BlobReference] on", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void A_child_carried_by_two_parent_types_is_refused()
        {
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => TestModels.DesignModel(typeof(OwnerA), typeof(OwnerB)));

            Assert.Contains("can reference only one parent", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void A_temporal_table_per_type_leaf_is_refused()
        {
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => TestModels.DesignModel(TestModels.Definition([typeof(TestUser), typeof(TptRoot), typeof(TptLeaf)])));

            Assert.Contains("table-per-type", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void An_ignored_capability_column_is_refused_with_the_entity_and_the_column_named()
        {
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => TestModels.DesignModel(TellmaModelDefinition.Create(
                [typeof(TestUser), typeof(Target)], [], [typeof(IgnoreModifiedBy)], new Dictionary<Type, Type>())));

            Assert.Contains("Target does not map ModifiedById, which the audit columns of its table needs", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void A_collation_configured_fluently_is_refused_with_the_property_named()
        {
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => TestModels.DesignModel(TellmaModelDefinition.Create(
                [typeof(TestUser), typeof(FluentlyCollated)], [], [typeof(FluentCollation)], new Dictionary<Type, Type>())));

            Assert.StartsWith("FluentlyCollated.Code configures its collation with UseCollation.", exception.Message, StringComparison.Ordinal);
            Assert.Contains("[Collation]", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void A_column_name_with_a_closing_bracket_is_delimited_in_the_generated_sql()
        {
            IModel built = TestModels.DesignModel(typeof(Bracketed));
            IEntityType bracketed = built.FindEntityType(typeof(Bracketed))!;

            Assert.Equal("[Code]]x] IS NOT NULL", Assert.Single(bracketed.GetIndexes()).GetFilter());
        }

        /// <summary>Ignores an audit column.</summary>
        public sealed class IgnoreModifiedBy : IEntityTypeConfiguration<Target>
        {
            public void Configure(EntityTypeBuilder<Target> builder)
            {
                _ = builder.Ignore(static target => target.ModifiedById);
            }
        }

        /// <summary>Gives <see cref="FluentlyCollated.Code" /> a collation through the fluent API.</summary>
        public sealed class FluentCollation : IEntityTypeConfiguration<FluentlyCollated>
        {
            /// <inheritdoc />
            public void Configure(EntityTypeBuilder<FluentlyCollated> builder)
            {
                _ = builder.Property(static entity => entity.Code).UseCollation("Latin1_General_100_BIN2");
            }
        }

        /// <summary>An entity whose code is collated fluently rather than by attribute.</summary>
        [Table("FluentlyCollated", Schema = "probe")]
        public sealed class FluentlyCollated : TopLevelEntity
        {
            /// <summary>The code, collated by the configuration above.</summary>
            [MaxLength(64)]
            public string? Code { get; set; }
        }

        /// <summary>A unique column whose name holds a closing bracket.</summary>
        [Table("Bracketed", Schema = "probe")]
        public sealed class Bracketed : TopLevelEntity
        {
            [Column("Code]x")]
            [MaxLength(10)]
            [Unique]
            public string? Code { get; set; }
        }

        /// <summary>A plain referenced entity.</summary>
        [Table("Targets", Schema = "probe")]
        public class Target : TopLevelEntity
        {
        }

        /// <summary>One leaf of <see cref="Target" />.</summary>
        [Table("TargetsA", Schema = "probe")]
        public sealed class TargetLeafA : Target
        {
        }

        /// <summary>Another leaf of <see cref="Target" />.</summary>
        [Table("TargetsB", Schema = "probe")]
        public sealed class TargetLeafB : Target
        {
        }

        /// <summary>References <see cref="Target" />.</summary>
        [Table("Referrers", Schema = "probe")]
        public sealed class Referrer : TopLevelEntity
        {
            [References(typeof(Target))]
            public int? TargetId { get; set; }
        }

        /// <summary>References with a string.</summary>
        [Table("StringReferrers", Schema = "probe")]
        public sealed class StringReferrer : TopLevelEntity
        {
            [MaxLength(10)]
            [References(typeof(Target))]
            public string? TargetCode { get; set; }
        }

        /// <summary>Indexes a property it lacks.</summary>
        [Table("BadIndexes", Schema = "probe")]
        [TableIndex("Missing")]
        public sealed class BadIndex : TopLevelEntity
        {
        }

        /// <summary>A key of an unsupported type.</summary>
        [Table("ShortKeys", Schema = "probe")]
        public sealed class ShortKeyed : Entity<short>
        {
        }

        /// <summary>A blob reference of the wrong type.</summary>
        [Table("BadBlobs", Schema = "probe")]
        public sealed class BadBlob : TopLevelEntity
        {
            [BlobReference("probe")]
            public long? FileId { get; set; }
        }

        /// <summary>A child two parents carry.</summary>
        [Table("SharedChildren", Schema = "probe")]
        public sealed class SharedChild : ChildEntity
        {
            [ParentKey]
            public int OwnerId { get; set; }
        }

        /// <summary>The first parent of <see cref="SharedChild" />.</summary>
        [Table("OwnersA", Schema = "probe")]
        public sealed class OwnerA : TopLevelEntity
        {
            [NotMapped]
            public List<SharedChild>? Children { get; set; }
        }

        /// <summary>The second parent of <see cref="SharedChild" />.</summary>
        [Table("OwnersB", Schema = "probe")]
        public sealed class OwnerB : TopLevelEntity
        {
            [NotMapped]
            public List<SharedChild>? Children { get; set; }
        }

        /// <summary>A table-per-type root.</summary>
        [Table("TptRoots", Schema = "probe")]
        public class TptRoot : TopLevelEntity
        {
        }

        /// <summary>A temporal table-per-type leaf.</summary>
        [Table("TptLeaves", Schema = "probe")]
        [Temporal]
        public sealed class TptLeaf : TptRoot
        {
        }
    }
}
