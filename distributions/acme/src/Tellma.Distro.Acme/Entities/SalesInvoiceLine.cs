// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Tellma.Core.Abstractions.Entities;
using Tellma.Module.Gl;

namespace Tellma.Distro.Acme.Entities
{
    /// <summary>
    ///     One line of a sales invoice: an amount, described, and analysed by the responsibility center
    ///     it is earned in. Queryable on its own as <c>acme.SalesInvoiceLine</c>, under its invoice.
    /// </summary>
    [Table("SalesInvoiceLines", Schema = AcmeFeature.Schema)]
    [TableType]
    public class SalesInvoiceLine : ChildEntity
    {
        /// <summary>The invoice the line belongs to.</summary>
        [ParentKey]
        public int InvoiceId { get; set; }

        /// <summary>The responsibility center the amount is earned in.</summary>
        [References(typeof(Center))]
        public int CenterId { get; set; }

        /// <summary>What the line bills for, <c>nvarchar(255)</c>; null for nothing said.</summary>
        [MaxLength(255)]
        public string? Description { get; set; }

        /// <summary>The amount, in the customer's currency, <c>decimal(19,4)</c>.</summary>
        [Column(TypeName = "decimal(19,4)")]
        public decimal Amount { get; set; }
    }
}
