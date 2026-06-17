// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Tellma.Core.EntityFrameworkCore.TableTypes;

namespace Tellma.Core.EntityFrameworkCore.MigrationsHost.Model
{
    /// <summary>
    ///     A pack-style base class: the table-type opt-in and the per-property exclusion are
    ///     declared here and inherited by the distribution leaf (<see cref="Customer" />).
    /// </summary>
    [TableType]
    public abstract class CustomerBase
    {
        /// <summary>The app-assigned surrogate key.</summary>
        public int Id { get; set; }

        /// <summary>The customer's display name.</summary>
        [MaxLength(255)]
        public string Name { get; set; } = null!;

        /// <summary>Excluded from the table type by the (inherited) attribute.</summary>
        [ExcludeFromTableType]
        public string? InternalNotes { get; set; }
    }

    /// <summary>
    ///     The distribution leaf: inherits the pack's table-type opt-in and exclusions. Mapped
    ///     leaf-only — per EF's table ordering, leaf-declared columns land before base-class
    ///     columns, in both the table and the type.
    /// </summary>
    public class Customer : CustomerBase
    {
        /// <summary>A leaf-added column; lands before the base class's columns (after the PK).</summary>
        public int LoyaltyPoints { get; set; }
    }

    /// <summary>An invoice header; opts into a table type via the fluent API.</summary>
    public class Invoice
    {
        /// <summary>The app-assigned surrogate key.</summary>
        public int Id { get; set; }

        /// <summary>The owning customer.</summary>
        public int CustomerId { get; set; }

        /// <summary>A free-text memo.</summary>
        [MaxLength(255)]
        public string? Memo { get; set; }

        /// <summary>The invoice total.</summary>
        public decimal Total { get; set; }

        /// <summary>Computed in the database; always excluded from the table type.</summary>
        public decimal TotalWithTax { get; set; }

        /// <summary>Optimistic-concurrency token; included in the type as nullable binary(8).</summary>
        [Timestamp]
        public byte[]? RowVersion { get; set; }

        /// <summary>The invoice's lines.</summary>
        public List<InvoiceLine> Lines { get; } = [];
    }

    /// <summary>An invoice line; opts into a table type via the attribute, with a name override.</summary>
    [TableType(Name = "InvoiceLinesList")]
    public class InvoiceLine
    {
        /// <summary>The app-assigned surrogate key.</summary>
        public int Id { get; set; }

        /// <summary>The owning invoice (real FK, app-assigned before save).</summary>
        public int InvoiceId { get; set; }

        /// <summary>The line description.</summary>
        [MaxLength(500)]
        public string Description { get; set; } = null!;

        /// <summary>The line quantity.</summary>
        [Column(TypeName = "decimal(19,4)")]
        public decimal Quantity { get; set; }
    }

    /// <summary>
    ///     A standalone table-type shape (spec 0001 §5): paired with no table, used for bulk state
    ///     updates, and doubling as the DTO for the rows bound into the TVP at runtime.
    /// </summary>
    [TableType(Name = "DocumentStatesList")]
    public class DocumentState
    {
        /// <summary>The targeted document.</summary>
        [Key]
        public int Id { get; set; }

        /// <summary>The new workflow state.</summary>
        public short State { get; set; }
    }

    /// <summary>A table with no table type, proving the opt-in is per table.</summary>
    public class AppSetting
    {
        /// <summary>The app-assigned surrogate key (seeded rows use the reserved low band).</summary>
        public int Id { get; set; }

        /// <summary>The setting key.</summary>
        [MaxLength(128)]
        public string Key { get; set; } = null!;

        /// <summary>The setting value.</summary>
        [MaxLength(2048)]
        public string? Value { get; set; }
    }
}
