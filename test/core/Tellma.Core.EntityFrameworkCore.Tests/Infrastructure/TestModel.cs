// Copyright (c) 2026 Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Tellma.Core.EntityFrameworkCore.TableTypes;

namespace Tellma.Core.EntityFrameworkCore.Tests.Infrastructure
{
    /// <summary>
    ///     A context whose model is supplied per instance. Service-provider caching is disabled by
    ///     the factory so every instance gets a fresh model cache (no cross-test contamination).
    /// </summary>
    public sealed class ModelTestContext(DbContextOptions options, Action<ModelBuilder> configureModel) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            configureModel(modelBuilder);
        }
    }

    /// <summary>Factories for building models/services without ever opening a connection.</summary>
    public static class TestModel
    {
        /// <summary>
        ///     Creates a context with <c>UseSqlServer().UseTableTypes()</c> and the given model.
        ///     Nothing connects: tests only read the model and generate SQL text.
        /// </summary>
        public static ModelTestContext CreateContext(Action<ModelBuilder> configureModel, bool useTableTypes = true)
        {
            DbContextOptionsBuilder optionsBuilder = new();
            optionsBuilder
                .UseSqlServer("Server=(local);Database=TellmaTableTypeTests;Integrated Security=true;TrustServerCertificate=true")
                .EnableServiceProviderCaching(false);
            if (useTableTypes)
            {
                optionsBuilder.UseTableTypes();
            }

            return new ModelTestContext(optionsBuilder.Options, configureModel);
        }

        /// <summary>The finalized design-time model (the one migrations work from).</summary>
        public static IModel GetFinalizedModel(DbContext context)
        {
            return context.GetService<IDesignTimeModel>().Model;
        }

        /// <summary>The relational model of the finalized design-time model.</summary>
        public static IRelationalModel GetRelationalModel(DbContext context)
        {
            return GetFinalizedModel(context).GetRelationalModel();
        }
    }

    // ---------------------------------------------------------------------------------------
    // Pack→leaf inheritance sample: opt-in and exclusion declared on the (pack) base class and
    // inherited by the (distribution) leaf.
    // ---------------------------------------------------------------------------------------

    /// <summary>Pack-style base class with attribute opt-in and a per-property exclusion.</summary>
    [TableType]
    public abstract class PackProduct
    {
        public int Id { get; set; }

        [MaxLength(255)]
        public string Name { get; set; } = null!;

        [ExcludeFromTableType]
        public string? InternalNotes { get; set; }
    }

    /// <summary>Distribution leaf extending the pack default; inherits the opt-in.</summary>
    public class Product : PackProduct
    {
        public decimal Price { get; set; }
    }

    /// <summary>A simple standalone entity used by most single-table tests.</summary>
    public class Order
    {
        public int Id { get; set; }

        public int CustomerId { get; set; }

        [MaxLength(255)]
        public string? Memo { get; set; }

        public decimal Total { get; set; }

        [Timestamp]
        public byte[]? RowVersion { get; set; }
    }

    /// <summary>An entity that never opts into a table type.</summary>
    public class Plain
    {
        public int Id { get; set; }

        public string? Value { get; set; }
    }

    /// <summary>An entity exercising every common SQL Server column type and facet.</summary>
    public class AllTypes
    {
        public int Id { get; set; }
        public long Int64 { get; set; }
        public short Int16 { get; set; }
        public byte Byte { get; set; }
        public bool Bool { get; set; }
        public decimal Decimal { get; set; }
        public double Double { get; set; }
        public float Float { get; set; }
        public DateTime DateTime { get; set; }
        public DateTimeOffset DateTimeOffset { get; set; }
        public DateOnly DateOnly { get; set; }
        public TimeOnly TimeOnly { get; set; }
        public Guid Guid { get; set; }
        public string RequiredString { get; set; } = null!;
        public string? BoundedString { get; set; }
        public string? AnsiString { get; set; }
        public string? FixedString { get; set; }
        public string? CollatedString { get; set; }
        public byte[]? Binary { get; set; }
        public decimal Money { get; set; }
    }
}
