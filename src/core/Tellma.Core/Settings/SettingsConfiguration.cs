// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Calendars;
using Tellma.Core.Abstractions.Settings;
using Tellma.Core.Access;
using TypedSettings = Tellma.Core.Abstractions.Settings.Settings;

namespace Tellma.Core.Settings
{
    // The table is spec 0013 §5.2's core.Settings: one row, its constraints, and the placeholder seed.
    /// <summary>
    ///     The model of <c>core.Settings</c>: the single-row and language and calendar checks, the
    ///     primary calendar's default, and the unprovisioned placeholder every database is created with.
    /// </summary>
    /// <remarks>
    ///     The conventions add the rest: the positive-key check, the history table, the table type,
    ///     the never-consumed sequence and the audit foreign keys.
    /// </remarks>
    internal sealed class SettingsConfiguration : IEntityTypeConfiguration<TypedSettings>
    {
        /// <summary>The single-row check's name.</summary>
        public const string SingleRowCheck = "CK_Settings_SingleRow";

        /// <summary>The language check's name.</summary>
        public const string LanguagesCheck = "CK_Settings_Languages";

        /// <summary>The calendar check's name.</summary>
        public const string CalendarsCheck = "CK_Settings_Calendars";

        /// <inheritdoc />
        public void Configure(EntityTypeBuilder<TypedSettings> builder)
        {
            ArgumentNullException.ThrowIfNull(builder);

            // --- Columns the annotations cannot express ---
            _ = builder.Property(static settings => settings.PrimaryCalendar).HasDefaultValue(CalendarCodes.Gregorian);

            // --- Checks: one row, distinct languages in position order, distinct calendars ---
            _ = builder.ToTable(table =>
            {
                _ = table.HasCheckConstraint(SingleRowCheck, $"[Id] = {TypedSettings.SingleRowId}");

                // A tertiary language needs a secondary one, and no language takes two positions.
                _ = table.HasCheckConstraint(
                    LanguagesCheck,
                    "([SecondaryLanguage] IS NULL OR [SecondaryLanguage] <> [PrimaryLanguage]) AND ([TertiaryLanguage] IS NULL OR ([SecondaryLanguage] IS NOT NULL AND [TertiaryLanguage] <> [PrimaryLanguage] AND [TertiaryLanguage] <> [SecondaryLanguage]))");
                _ = table.HasCheckConstraint(CalendarsCheck, "[SecondaryCalendar] IS NULL OR [SecondaryCalendar] <> [PrimaryCalendar]");
            });

            // --- The unprovisioned placeholder, authored by the system user; provisioning completes it ---
            // spec 0013 §5.7: the routing guard accepts tenant 0 only in the platform's own work.
            _ = builder.HasData(new
            {
                Id = TypedSettings.SingleRowId,
                TenantId = TypedSettings.PlaceholderTenantId,
                Name = string.Empty,
                PrimaryLanguage = TenantSettingsStore.DefaultLanguage,
                PrimaryCalendar = CalendarCodes.Gregorian,
                TimeZone = TenantSettingsStore.DefaultTimeZone,
                CreatedAt = AccessSeeds.SeededAt,
                CreatedById = WellKnownIds.SystemUserId,
                ModifiedAt = AccessSeeds.SeededAt,
                ModifiedById = WellKnownIds.SystemUserId,
            });
        }
    }

    /// <summary>The model of <c>core.SettingEntries</c>; the conventions derive all of it from the entity.</summary>
    internal sealed class SettingEntryConfiguration : IEntityTypeConfiguration<SettingEntry>
    {
        /// <inheritdoc />
        public void Configure(EntityTypeBuilder<SettingEntry> builder)
        {
            // The unique key, the history table, the table type, the sequence and the
            // audit foreign keys all come from the entity's annotations and the platform's conventions.
            ArgumentNullException.ThrowIfNull(builder);
        }
    }
}
