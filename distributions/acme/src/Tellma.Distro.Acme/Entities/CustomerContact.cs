// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Tellma.Core.Abstractions.Entities;

namespace Tellma.Distro.Acme.Entities
{
    /// <summary>A person to contact at a customer: one row of the customer's contacts grid.</summary>
    [Table("CustomerContacts", Schema = AcmeFeature.Schema)]
    [TableType]
    public class CustomerContact : ChildEntity
    {
        /// <summary>The customer the contact belongs to.</summary>
        [ParentKey]
        public int CustomerId { get; set; }

        /// <summary>The contact's name, <c>nvarchar(255)</c>.</summary>
        [MaxLength(255)]
        public string Name { get; set; } = null!;

        /// <summary>The contact's email address, <c>nvarchar(255)</c>; null for none.</summary>
        [MaxLength(255)]
        public string? Email { get; set; }

        /// <summary>The contact's mobile number, <c>nvarchar(50)</c>; null for none.</summary>
        [MaxLength(50)]
        public string? Mobile { get; set; }
    }
}
