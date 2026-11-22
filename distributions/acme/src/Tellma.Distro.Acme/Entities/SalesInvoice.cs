// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.ComponentModel.DataAnnotations.Schema;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Entities;

namespace Tellma.Distro.Acme.Entities
{
    // Spec 0018 §1.4: acme.SalesInvoice exercises a scalar display name, the child query surface
    // acme.SalesInvoiceLine (spec 0015 §2.2) and file attachments. Its assignments (spec 0028) join
    // with that spec.
    /// <summary>
    ///     A sales invoice: a customer billed on a posting date, its lines analysed by responsibility
    ///     center, and the files attached to it. Shown by its serial number.
    /// </summary>
    /// <remarks>
    ///     Whether the invoice is closed or posted is server-owned: the <c>close</c> action closes it,
    ///     and no save changes either.
    /// </remarks>
    [Table("SalesInvoices", Schema = AcmeFeature.Schema)]
    [TableType]
    [DisplayNameSource(nameof(SerialNumber))]
    [TableIndex(nameof(CustomerId), nameof(PostingDate))]
    [ApiResource(Description = "Sales invoices")]
    public class SalesInvoice : TopLevelEntity
    {
        /// <summary>The serial number, unique: the natural key the invoice is shown and found by.</summary>
        [NaturalKey]
        public int SerialNumber { get; set; }

        /// <summary>The date the invoice is posted on, <c>date</c>.</summary>
        public DateOnly PostingDate { get; set; }

        /// <summary>The customer billed.</summary>
        [References(typeof(Customer))]
        public int CustomerId { get; set; }

        /// <summary>Whether the invoice is closed; server-owned, set by the <c>close</c> action.</summary>
        [ServerOwned]
        public bool IsClosed { get; set; }

        /// <summary>Whether the invoice is posted to the ledger; server-owned.</summary>
        [ServerOwned]
        public bool IsPosted { get; set; }

        /// <summary>
        ///     The invoice's lines, saved with it: null leaves them untouched, an empty list deletes
        ///     them all, and a list replaces them.
        /// </summary>
        [NotMapped]
        public List<SalesInvoiceLine>? Lines { get; set; }

        /// <summary>
        ///     The files attached to the invoice, saved with it: null leaves them untouched, an empty
        ///     list deletes them all, and a list replaces them.
        /// </summary>
        [NotMapped]
        public List<SalesInvoiceAttachment>? Attachments { get; set; }
    }
}
