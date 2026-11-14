// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Tellma.Core.Abstractions.Entities;
using Tellma.Core.Composition;
using Tellma.Core.Tests.Infrastructure;

namespace Tellma.Core.Tests.Caching
{
    // A sound model for the cacheable entities: a small activatable list with a multilingual name, a
    // pack's default list the distribution replaces with a leaf of its own, and an entity that is not
    // cacheable.

    /// <summary>Composes the cache model into a composition.</summary>
    internal static class CacheModel
    {
        /// <summary>The currencies' entity name.</summary>
        public const string Currencies = "cm.CmCurrency";

        /// <summary>The units' entity name.</summary>
        public const string Units = "cm.CmUnit";

        /// <summary>Adds the model's feature and replaces the units' default with the distribution's leaf.</summary>
        /// <param name="tellma">The builder.</param>
        public static void Compose(TellmaBuilder tellma)
        {
            _ = tellma
                .AddFeature(new TestFeature<Tag.C>("cache", contribute: static contribution => contribution
                    .Entity<CmCurrency>()
                    .Entity<CmUnit>()
                    .Entity<CmCountry>()))
                .UseEntity<CmUnit, CmMyUnit>();
        }
    }

    /// <summary>A small cacheable list of at most three rows.</summary>
    [Table("Currencies", Schema = "cm")]
    [TableType]
    [Cacheable(MaxRows = 3)]
    public class CmCurrency : TopLevelEntity, IActivatable
    {
        /// <summary>The code.</summary>
        [MaxLength(3)]
        public string Code { get; set; } = null!;

        /// <summary>The name.</summary>
        [MaxLength(50)]
        [Multilingual]
        public string Name { get; set; } = null!;

        /// <summary>The name in the second language.</summary>
        public string? Name2 { get; set; }

        /// <summary>The name in the third language.</summary>
        public string? Name3 { get; set; }

        /// <inheritdoc />
        public bool IsActive { get; set; }
    }

    /// <summary>A pack's cacheable default, which the distribution replaces with <see cref="CmMyUnit" />.</summary>
    [Table("Units", Schema = "cm")]
    [TableType]
    [Cacheable]
    public class CmUnit : TopLevelEntity
    {
        /// <summary>The name.</summary>
        [MaxLength(50)]
        public string Name { get; set; } = null!;
    }

    /// <summary>The distribution's leaf of <see cref="CmUnit" />.</summary>
    public sealed class CmMyUnit : CmUnit
    {
        /// <summary>The unit's symbol.</summary>
        [MaxLength(10)]
        public string? Symbol { get; set; }
    }

    /// <summary>A cacheable entity with a bigint key, which a cached list cannot be keyed by; not composed by <see cref="CacheModel" />.</summary>
    [Table("Ledger", Schema = "cm")]
    [TableType]
    [Cacheable]
    public class CmLedgerLine : TopLevelEntity<long>
    {
        /// <summary>The memo.</summary>
        [MaxLength(50)]
        public string? Memo { get; set; }
    }

    /// <summary>An entity that is not cacheable.</summary>
    [Table("Countries", Schema = "cm")]
    [TableType]
    public class CmCountry : TopLevelEntity
    {
        /// <summary>The name.</summary>
        [MaxLength(50)]
        public string Name { get; set; } = null!;
    }
}
