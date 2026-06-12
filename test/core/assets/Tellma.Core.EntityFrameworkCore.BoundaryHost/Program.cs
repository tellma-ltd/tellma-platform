// Copyright (c) 2026 Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

// Web-host stand-in for the publish boundary check (spec 0001 Rule 3). CI publishes this project and
// asserts the output contains no assembly from the EF Design dependency tree (Roslyn,
// templating, Humanizer) — see eng/check-publish-boundary.ps1. The code exercises the runtime
// library the way a host would (options wired, model built, metadata API queried) without
// connecting to a database, so trimming/publish cannot silently drop the dependency.

using Microsoft.EntityFrameworkCore;
using Tellma.Core.EntityFrameworkCore.BoundaryHost;
using Tellma.Core.EntityFrameworkCore.TableTypes;

DbContextOptionsBuilder<BoundaryContext> optionsBuilder = new();
optionsBuilder
    .UseSqlServer("Server=(local);Database=TellmaBoundaryHost;Integrated Security=true;TrustServerCertificate=true")
    .UseTableTypes();

using BoundaryContext context = new(optionsBuilder.Options);
foreach (TableTypeDefinition definition in context.Model.GetTableTypes())
{
    Console.WriteLine($"{definition.DisplayName}: {string.Join(", ", definition.Columns.Select(c => c.Name))}");
}

namespace Tellma.Core.EntityFrameworkCore.BoundaryHost
{
    /// <summary>A minimal context proving the runtime surface works with no Design package present.</summary>
    public class BoundaryContext(DbContextOptions<BoundaryContext> options) : DbContext(options)
    {
        /// <summary>The sample entities.</summary>
        public DbSet<Widget> Widgets => Set<Widget>();

        /// <inheritdoc />
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.HasTableType("IdList", schema: "dbo", type => type.Column<int>("Id").HasKey("Id"));
            modelBuilder.Entity<Widget>(entity =>
            {
                entity.ToTable("Widgets", "app");
                entity.HasTableType();
            });
        }
    }

    /// <summary>A sample entity with a table type.</summary>
    public class Widget
    {
        /// <summary>The app-assigned surrogate key.</summary>
        public int Id { get; set; }

        /// <summary>The widget's name.</summary>
        public string Name { get; set; } = null!;
    }
}
