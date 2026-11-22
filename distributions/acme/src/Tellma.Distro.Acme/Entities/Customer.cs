// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Entities;

namespace Tellma.Distro.Acme.Entities
{
    // Spec 0018 §1.4: acme.Customer exercises an enum, a decimal, a cacheable navigation, an avatar
    // and a child grid on an activatable stack. The annotations alone produce acme.Customers.
    /// <summary>
    ///     A customer Acme sells to: a retail, corporate or government buyer with a credit limit in its
    ///     currency, a picture, and the people to contact. Deactivated rather than deleted once it has
    ///     history.
    /// </summary>
    [Table("Customers", Schema = AcmeFeature.Schema)]
    [TableType]
    [DisplayNameSource(nameof(Name), Prefix = nameof(Code))]
    [TableIndex(nameof(IsActive), nameof(Name))]
    [ApiResource(Description = "Customers")]
    public class Customer : TopLevelEntity, IActivatable
    {
        /// <summary>The code, <c>nvarchar(50)</c>, unique: the natural key seeds and imports find the customer by.</summary>
        [MaxLength(50)]
        [NaturalKey]
        [Searchable(SearchKind.Prefix)]
        public string Code { get; set; } = null!;

        /// <summary>The name in the tenant's primary language, <c>nvarchar(255)</c>.</summary>
        [MaxLength(255)]
        [Multilingual]
        [Searchable]
        public string Name { get; set; } = null!;

        /// <summary>The name in the tenant's secondary language.</summary>
        public string? Name2 { get; set; }

        /// <summary>The name in the tenant's tertiary language.</summary>
        public string? Name3 { get; set; }

        /// <summary>What kind of buyer the customer is.</summary>
        public CustomerType CustomerType { get; set; }

        /// <summary>The currency the customer is billed in; null for none chosen yet.</summary>
        [References(typeof(Currency))]
        public int? CurrencyId { get; set; }

        /// <summary>The most the customer may owe, in its currency, <c>decimal(19,4)</c>; never negative.</summary>
        [Column(TypeName = "decimal(19,4)")]
        public decimal CreditLimit { get; set; }

        /// <summary>The customer's picture, a square avatar; null for none.</summary>
        [BlobReference("customer-image", BlobPreset.Avatar)]
        public int? ImageId { get; set; }

        /// <summary>Whether the customer is in use; server-owned, changed by the activate and deactivate actions alone.</summary>
        public bool IsActive { get; set; } = true;

        /// <summary>
        ///     The people to contact at the customer, saved with it: null leaves them untouched, an
        ///     empty list deletes them all, and a list replaces them.
        /// </summary>
        [NotMapped]
        public List<CustomerContact>? Contacts { get; set; }
    }
}
