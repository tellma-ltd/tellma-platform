// Copyright (c) 2026 Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Tellma.Core.EntityFrameworkCore.TableTypes;
using Tellma.Core.EntityFrameworkCore.Tests.Infrastructure;

namespace Tellma.Core.EntityFrameworkCore.Tests.Conventions
{
    /// <summary>
    ///     Column-order parity: the derived UDTT column order must equal the physical table's resolved
    ///     column order (filtered to the type's columns) — the ordinal-binding contract. This pins the
    ///     convention's <c>SortColumns</c> against EF's private <c>GetSortedColumns</c> using the live
    ///     relational model as the oracle, so divergence fails here without a database.
    /// </summary>
    public class ColumnOrderParityTests
    {
        private static List<string> DeployedTableColumnOrder(ModelTestContext context, string table)
        {
            // The deployed column order is what the CreateTableOperation emits (EF's GetSortedColumns),
            // NOT ITable.Columns (which is model-build order). This is the order the UDTT must mirror.
            IMigrationsModelDiffer differ = context.GetService<IMigrationsModelDiffer>();
            CreateTableOperation create = differ
                .GetDifferences(null, TestModel.GetRelationalModel(context))
                .OfType<CreateTableOperation>()
                .Single(o => o.Name == table);
            return [.. create.Columns.Select(c => c.Name)];
        }

        [Fact]
        public void Shadow_fk_behind_a_reference_navigation_orders_like_the_table()
        {
            // The navigation 'Parent' sits between 'Name'... no: declared before 'Note'. EF groups the
            // shadow FK column 'ParentId' under the navigation and orders it at the navigation's
            // declaration position — *not* appended to the shadow tail. The old behavior would have
            // ordered Id, Note, ParentId; EF (and now the convention) orders Id, ParentId, Note.
            using ModelTestContext context = TestModel.CreateContext(mb =>
            {
                mb.Entity<OrderParent>(e =>
                {
                    e.ToTable("OrderParents", "gl");
                    e.Property(p => p.Id).ValueGeneratedNever();
                });
                mb.Entity<ChildWithNav>(e =>
                {
                    e.ToTable("Children", "gl");
                    e.HasTableType();
                    e.Property(c => c.Id).ValueGeneratedNever();
                    e.HasOne(c => c.Parent).WithMany().HasForeignKey("ParentId");
                });
            });

            List<string> typeColumns =
                [.. TestModel.GetFinalizedModel(context).GetTableTypes().Single().Columns.Select(c => c.Name)];

            Assert.Contains("ParentId", typeColumns);
            // The UDTT order must equal the deployed table order; that order is EF's oracle, whatever
            // it is, and pinning equality is what guards against ordinal divergence.
            Assert.Equal(DeployedTableColumnOrder(context, "Children"), typeColumns);
        }

        [Fact]
        public void Json_container_column_orders_last_like_the_table()
        {
            // EF appends JSON container columns after every scalar column, ordered by name
            // (GetSortedColumns, issue #28539). The UDTT must mirror that, so the derived column order
            // stays equal to the deployed table order even with a ToJson() mapping in the middle.
            using ModelTestContext context = TestModel.CreateContext(mb => mb.Entity<JsonOrder>(e =>
            {
                e.ToTable("JsonOrders", "gl");
                e.HasTableType();
                e.Property(o => o.Id).ValueGeneratedNever();
                e.OwnsOne(o => o.Meta, b => b.ToJson());
            }));

            List<string> typeColumns =
                [.. TestModel.GetFinalizedModel(context).GetTableTypes().Single().Columns.Select(c => c.Name)];

            Assert.Equal("Meta", typeColumns[^1]); // the JSON column is last, after Id and Name
            Assert.Equal(DeployedTableColumnOrder(context, "JsonOrders"), typeColumns);
        }

        private sealed class OrderParent
        {
            public int Id { get; set; }
        }

        private sealed class JsonOrder
        {
            public int Id { get; set; }

            public string Name { get; set; } = string.Empty;

            public JsonMeta? Meta { get; set; }
        }

        private sealed class JsonMeta
        {
            public string Source { get; set; } = string.Empty;

            public int Revision { get; set; }
        }

        private sealed class ChildWithNav
        {
            public int Id { get; set; }

            // Reference navigation whose foreign key is a shadow property ("ParentId").
            public OrderParent Parent { get; set; } = null!;

            public string Note { get; set; } = string.Empty;
        }
    }
}
