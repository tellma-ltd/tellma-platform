// Copyright (c) 2026 Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore;
using Tellma.Core.EntityFrameworkCore.TableTypes;
using Tellma.Core.EntityFrameworkCore.Tests.Infrastructure;

namespace Tellma.Core.EntityFrameworkCore.Tests.Conventions
{
    /// <summary>
    ///     Finalizing-time rejections (spec 0001 §2): opt-ins the derivation cannot honor must fail
    ///     with an actionable error rather than silently producing a partial row image (the failure
    ///     mode that killed <c>ForSave</c>).
    /// </summary>
    public class DerivationRejectionTests
    {
        private static InvalidOperationException Reject(Action<ModelBuilder> configure)
        {
            return Assert.Throws<InvalidOperationException>(() =>
            {
                using ModelTestContext context = TestModel.CreateContext(configure);
                _ = TestModel.GetFinalizedModel(context);
            });
        }

        [Fact]
        public void Keyless_entity_opt_in_is_rejected()
        {
            InvalidOperationException ex = Reject(mb => mb.Entity<KeylessThing>(e =>
            {
                e.HasNoKey();
                e.ToTable("Keyless", "x");
                e.HasTableType();
            }));

            Assert.Contains("primary key", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Owned_type_mapped_into_the_owner_table_is_rejected()
        {
            InvalidOperationException ex = Reject(mb => mb.Entity<Holder>(e =>
            {
                e.ToTable("Holders", "x");
                e.OwnsOne(h => h.Address);
                e.HasTableType();
            }));

            Assert.Contains("owned", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Tph_root_with_derived_declared_columns_is_rejected()
        {
            InvalidOperationException ex = Reject(mb =>
            {
                mb.Entity<Animal>(e =>
                {
                    e.ToTable("Animals", "zoo");
                    e.HasTableType();
                });
                mb.Entity<Dog>(); // TPH-derived, declares Breed on the shared table
            });

            Assert.Contains("Breed", ex.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void Tph_root_with_a_derived_complex_property_is_rejected()
        {
            // A complex property declared on a TPH-derived type maps columns into the shared table
            // that the root's row image cannot see — the same hole as derived scalar columns.
            InvalidOperationException ex = Reject(mb =>
            {
                mb.Entity<Animal>(e =>
                {
                    e.ToTable("Animals", "zoo");
                    e.HasTableType();
                });
                mb.Entity<Tagged>(e => e.ComplexProperty(t => t.Tag));
            });

            Assert.Contains("Tag", ex.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void Pure_discriminator_tph_hierarchy_is_allowed()
        {
            using ModelTestContext context = TestModel.CreateContext(mb =>
            {
                mb.Entity<Animal>(e =>
                {
                    e.ToTable("Animals", "zoo");
                    e.HasTableType();
                });
                mb.Entity<Cat>(); // TPH-derived, declares no own column
            });

            // No throw; the root's image is complete.
            TableTypeDefinition definition = Assert.Single(TestModel.GetFinalizedModel(context).GetTableTypes());
            Assert.Equal("AnimalsList", definition.Name);
        }

        [Fact]
        public void Logical_name_exceeding_the_physical_name_budget_is_rejected()
        {
            string tooLong = new('a', TableTypeNaming.MaxLogicalNameLength + 1);

            InvalidOperationException ex = Reject(mb => mb.Entity<Order>(e =>
            {
                e.ToTable("Orders", "gl");
                e.HasTableType(name: tooLong);
            }));

            Assert.Contains("128-character", ex.Message, StringComparison.Ordinal);
        }

        private sealed class KeylessThing
        {
            public int Value { get; set; }
        }

        private sealed class Holder
        {
            public int Id { get; set; }

            public Address Address { get; set; } = new();
        }

        private sealed class Address
        {
            public string City { get; set; } = string.Empty;
        }

        private class Animal
        {
            public int Id { get; set; }
        }

        private sealed class Dog : Animal
        {
            public string Breed { get; set; } = string.Empty;
        }

        private sealed class Cat : Animal
        {
        }

        private sealed class Tagged : Animal
        {
            public Tag Tag { get; set; } = new();
        }

        private sealed class Tag
        {
            public string Color { get; set; } = string.Empty;
        }
    }
}
