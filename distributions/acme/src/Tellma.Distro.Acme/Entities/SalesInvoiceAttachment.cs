// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.ComponentModel.DataAnnotations.Schema;
using Tellma.Core.Abstractions.Entities;

namespace Tellma.Distro.Acme.Entities
{
    /// <summary>A file attached to a sales invoice, such as a signed delivery note.</summary>
    [Table("SalesInvoiceAttachments", Schema = AcmeFeature.Schema)]
    [TableType]
    public class SalesInvoiceAttachment : ChildEntity
    {
        /// <summary>The invoice the file is attached to.</summary>
        [ParentKey]
        public int InvoiceId { get; set; }

        /// <summary>The attached file; a save attaches a file its user staged.</summary>
        [BlobReference("invoice-attachment")]
        public int? FileId { get; set; }
    }
}
