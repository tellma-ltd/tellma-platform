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
    // Spec 0018 §1.4: acme.Currency exercises a cacheable list (spec 0025 §3.3). Its annotations
    // alone produce acme.Currencies through the platform's conventions.
    /// <summary>
    ///     A currency Acme's customers are billed in: a small cacheable list that every reader shares,
    ///     found by its ISO code.
    /// </summary>
    [Table("Currencies", Schema = AcmeFeature.Schema)]
    [TableType]
    [Cacheable]
    [ApiResource(Description = "Currencies")]
    public class Currency : TopLevelEntity
    {
        /// <summary>The ISO 4217 code, <c>nvarchar(3)</c>, unique: the natural key seeds and imports find the currency by.</summary>
        [MaxLength(3)]
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
    }
}
