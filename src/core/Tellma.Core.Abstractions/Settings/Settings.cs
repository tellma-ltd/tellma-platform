// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Entities;
using Tellma.Core.Abstractions.Localization;

namespace Tellma.Core.Abstractions.Settings
{
    /// <summary>
    ///     The tenant's typed settings: the single row of the settings the platform itself reads to
    ///     build every request's context (the routing guard, the display name, the content
    ///     languages, the calendars, the zone and the default formats).
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Every tenant database holds exactly one row, <see cref="SingleRowId" />, seeded as an
    ///         unprovisioned placeholder (<see cref="TenantId" /> zero) and completed when the tenant
    ///         is provisioned. Every other setting is a <see cref="SettingEntry" />.
    ///     </para>
    ///     <para>
    ///         The class is sealed: a pack or a distribution cannot add a column, and declares its
    ///         settings as <see cref="SettingKey{T}" /> fields instead. It is a mapped entity of its
    ///         own, never a stack, and no securable reads it through a query.
    ///     </para>
    /// </remarks>
    [Table("Settings", Schema = "core")]
    [TableType]
    [Temporal]
    [BumpsVersionTag(VersionTagNames.Settings)]
    public sealed class Settings : TopLevelEntity
    {
        /// <summary>The id of the one row every tenant database holds.</summary>
        public const int SingleRowId = 1;

        /// <summary>The tenant id of the unprovisioned placeholder row.</summary>
        public const int PlaceholderTenantId = 0;

        /// <summary>The longest language code a language column holds.</summary>
        public const int MaxLanguageLength = 35;

        /// <summary>The longest calendar code a calendar column holds.</summary>
        public const int MaxCalendarLength = 16;

        /// <summary>The longest zone id the zone column holds.</summary>
        public const int MaxTimeZoneLength = 64;

        /// <summary>
        ///     The tenant the database belongs to: a routing guard compared with the routed tenant on
        ///     every load, never a filter. Zero marks the unprovisioned placeholder.
        /// </summary>
        public int TenantId { get; set; }

        /// <summary>The tenant's display name in its primary language, <c>nvarchar(255)</c>.</summary>
        [MaxLength(255)]
        [Multilingual]
        public string Name { get; set; } = null!;

        /// <summary>The tenant's display name in its secondary language.</summary>
        public string? Name2 { get; set; }

        /// <summary>The tenant's display name in its tertiary language.</summary>
        public string? Name3 { get; set; }

        /// <summary>The primary content language, a code the distribution offers, <c>varchar(35)</c>.</summary>
        [ValueSet(CoreValueSets.Language)]
        [Column(TypeName = "varchar(35)")]
        public string PrimaryLanguage { get; set; } = null!;

        /// <summary>The secondary content language, distinct from the primary; null for none.</summary>
        [ValueSet(CoreValueSets.Language)]
        [Column(TypeName = "varchar(35)")]
        public string? SecondaryLanguage { get; set; }

        /// <summary>The tertiary content language, which requires a secondary one and differs from both; null for none.</summary>
        [ValueSet(CoreValueSets.Language)]
        [Column(TypeName = "varchar(35)")]
        public string? TertiaryLanguage { get; set; }

        /// <summary>The primary calendar, a code of the calendar registry, <c>varchar(16)</c>.</summary>
        [ValueSet(CoreValueSets.Calendar)]
        [Column(TypeName = "varchar(16)")]
        public string PrimaryCalendar { get; set; } = null!;

        /// <summary>The secondary calendar, distinct from the primary; null for none.</summary>
        [ValueSet(CoreValueSets.Calendar)]
        [Column(TypeName = "varchar(16)")]
        public string? SecondaryCalendar { get; set; }

        /// <summary>
        ///     The tenant's zone, an IANA id, <c>varchar(64)</c>: the zone the tenant's business date is
        ///     computed in.
        /// </summary>
        [ValueSet(CoreValueSets.TimeZone)]
        [Column(TypeName = "varchar(64)")]
        public string TimeZone { get; set; } = null!;

        /// <summary>
        ///     The tenant's default region, an upper-case ISO 3166-1 alpha-2 code, <c>varchar(2)</c>,
        ///     below each user's own preference; null for each language's default region.
        /// </summary>
        [ValueSet(CoreValueSets.Region)]
        [Column(TypeName = "varchar(2)")]
        public string? Region { get; set; }

        /// <summary>The tenant's default digits, below each user's own preference; null for the region's default.</summary>
        public DigitStyle? Digits { get; set; }
    }
}
