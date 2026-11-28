// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.ComponentModel.DataAnnotations;

namespace Tellma.Core.Abstractions.Caching
{
    /// <summary>
    ///     The limits of the platform's in-process caches, bound from <c>Tellma:Cache</c> and validated
    ///     at startup. A distribution changes a limit in configuration, never in code.
    /// </summary>
    /// <remarks>
    ///     Each cache kind has its own private, bounded store, so a burst in one kind never evicts
    ///     another's entries. Every limit is at least 1, and <see cref="EntityMaxRowsDefault" /> is at
    ///     most <see cref="EntityMaxRowsCeiling" />.
    /// </remarks>
    public sealed class CacheOptions : IValidatableObject
    {
        /// <summary>The configuration section the options bind from.</summary>
        public const string SectionPath = "Tellma:Cache";

        /// <summary>How many tenants' settings the settings cache holds.</summary>
        [Range(1, int.MaxValue)]
        public int SettingsEntries { get; set; } = 10000;

        /// <summary>How many users' profiles and preferences the preferences cache holds.</summary>
        [Range(1, int.MaxValue)]
        public int PreferencesEntries { get; set; } = 50000;

        /// <summary>How many users' resolved permissions the permissions cache holds.</summary>
        [Range(1, int.MaxValue)]
        public int PermissionsEntries { get; set; } = 50000;

        /// <summary>How many rows, across every tenant and entity, the cacheable-entity cache holds.</summary>
        [Range(1, long.MaxValue)]
        public long EntityRows { get; set; } = 2000000;

        /// <summary>The row limit of a cacheable entity that declares none.</summary>
        [Range(1, int.MaxValue)]
        public int EntityMaxRowsDefault { get; set; } = 1000;

        /// <summary>
        ///     The highest row limit any cacheable entity may resolve to; above it, a "cacheable" type
        ///     is a configuration bug and fails startup.
        /// </summary>
        [Range(1, int.MaxValue)]
        public int EntityMaxRowsCeiling { get; set; } = 10000;

        /// <summary>How many tenants' version-tag snapshots the process keeps.</summary>
        [Range(1, int.MaxValue)]
        public int TagSnapshots { get; set; } = 10000;

        /// <inheritdoc />
        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (EntityMaxRowsDefault > EntityMaxRowsCeiling)
            {
                yield return new ValidationResult(
                    $"{nameof(EntityMaxRowsDefault)} ({EntityMaxRowsDefault}) is above {nameof(EntityMaxRowsCeiling)} ({EntityMaxRowsCeiling}).",
                    [nameof(EntityMaxRowsDefault), nameof(EntityMaxRowsCeiling)]);
            }
        }
    }
}
