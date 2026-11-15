// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Tellma.Core.Abstractions.Calendars;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Localization;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Tenancy;
using TypedSettings = Tellma.Core.Abstractions.Settings.Settings;

namespace Tellma.Core.Settings
{
    // The step is spec 0013 §5.7's provisioning: it completes the HasData placeholder through the
    // emitter, so the history row, the audit stamps and the settings tag bump come with the save.
    /// <summary>
    ///     <c>core.settings</c>: completes the tenant database's settings placeholder with the
    ///     tenant's id and name, the distribution's default language, the Gregorian calendar and UTC,
    ///     or the languages and calendars a seeded tenant lists.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Settings are not a stack, so the step saves the row itself, in a persist batch of the
    ///         step's own system scope, which accepts the placeholder. A database whose settings are
    ///         already the tenant's is left as it is, so an administrator's changes survive a re-run.
    ///     </para>
    ///     <para>
    ///         A seeded tenant's codes are validated as the settings editor validates them: each
    ///         language offered by the distribution and each calendar registered, none twice, at most
    ///         three languages and two calendars. A seed that breaks a rule fails the step.
    ///     </para>
    /// </remarks>
    /// <param name="languages">The language catalogue.</param>
    /// <param name="calendars">The calendar registry.</param>
    /// <param name="seed">The seed configuration, whose tenants may list their languages and calendars.</param>
    internal sealed class SettingsProvisioningStep(ILanguageCatalog languages, ICalendarRegistry calendars, IOptions<TellmaSeedOptions> seed)
        : ITenantProvisioningStep
    {
        /// <summary>The step's name.</summary>
        public const string StepName = "core.settings";

        /// <summary>The step's order: after the bootstrap administrator (10), before the blob container (20).</summary>
        public const int StepOrder = 15;

        /// <summary>The most content languages a tenant has.</summary>
        private const int MaxLanguages = 3;

        /// <summary>The most calendars a tenant has.</summary>
        private const int MaxCalendars = 2;

        /// <inheritdoc />
        public string Name => StepName;

        /// <inheritdoc />
        public int Order => StepOrder;

        /// <inheritdoc />
        public int Version => 1;

        /// <inheritdoc />
        public async Task RunAsync(TenantProvisioningContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            TenantDescriptor tenant = context.Tenant;
            ITenantDatabase database = context.Services.GetRequiredService<ITenantDatabase>();

            // --- The row as it stands: its tenant and its stamp ---
            IDataBatch read = database.CreateBatch(BatchPurpose.Read);
            BatchResult<EntityQueryResult<TypedSettings>> current = read.Query(new EntityQuery<TypedSettings>
            {
                Select = $"{nameof(TypedSettings.Id)},{nameof(TypedSettings.TenantId)},{nameof(TypedSettings.ModifiedAt)}",
            });
            _ = await read.ExecuteAsync().ConfigureAwait(false);
            TypedSettings row = current.Value.Entities.SingleOrDefault()
                ?? throw new InvalidOperationException("The tenant database holds no row of core.Settings. Its migrations seed the row; run the migrator against the database.");

            // A row already completed (by this step's earlier run) is the administrator's from then on.
            if (row.TenantId == tenant.Id)
            {
                return;
            }

            // --- The languages and calendars: the seed's, else the defaults ---
            TellmaSeedTenant? seeded = seed.Value.Tenants.FirstOrDefault(candidate => candidate.Id == tenant.Id);
            List<string> codes = seeded is { Languages.Count: > 0 } ? Languages(seeded.Languages, tenant.Id) : [languages.Offered[0].Code];
            List<string> calendarCodes = seeded is { Calendars.Count: > 0 } ? Calendars(seeded.Calendars, tenant.Id) : [CalendarCodes.Gregorian];

            // --- The completed row, saved through the emitter against the stamp just read ---
            TypedSettings completed = new()
            {
                Id = TypedSettings.SingleRowId,
                TenantId = tenant.Id,
                Name = tenant.Name,
                Name2 = codes.Count > 1 ? tenant.Name2 : null,
                Name3 = codes.Count > 2 ? tenant.Name3 : null,
                PrimaryLanguage = codes[0],
                SecondaryLanguage = codes.Count > 1 ? codes[1] : null,
                TertiaryLanguage = codes.Count > 2 ? codes[2] : null,
                PrimaryCalendar = calendarCodes[0],
                SecondaryCalendar = calendarCodes.Count > 1 ? calendarCodes[1] : null,
                TimeZone = TenantSettingsStore.DefaultTimeZone,
                ModifiedAt = row.ModifiedAt,
            };

            IDataBatch write = database.CreateBatch(BatchPurpose.Persist);
            _ = write.Save([completed]);
            _ = await write.ExecuteAsync().ConfigureAwait(false);
        }

        /// <summary>A seeded tenant's languages as the catalogue spells them, validated.</summary>
        /// <param name="seeded">The seed's codes.</param>
        /// <param name="tenantId">The tenant, for the message.</param>
        /// <returns>The codes.</returns>
        /// <exception cref="InvalidOperationException">A code is not offered, repeats, or there are more than three.</exception>
        private List<string> Languages(IList<string> seeded, int tenantId)
        {
            List<string> codes = [];
            foreach (string code in seeded)
            {
                string trimmed = code.Trim();
                if (!languages.IsOffered(trimmed) || languages.Find(trimmed) is not LanguageInfo language)
                {
                    throw Refused(tenantId, $"lists the language '{trimmed}', which the distribution does not offer");
                }

                if (codes.Contains(language.Code, StringComparer.OrdinalIgnoreCase))
                {
                    throw Refused(tenantId, $"lists the language '{language.Code}' twice");
                }

                codes.Add(language.Code);
            }

            return codes.Count <= MaxLanguages ? codes : throw Refused(tenantId, $"lists {codes.Count} languages; a tenant has at most {MaxLanguages}");
        }

        /// <summary>A seeded tenant's calendars as the registry spells them, validated.</summary>
        /// <param name="seeded">The seed's codes.</param>
        /// <param name="tenantId">The tenant, for the message.</param>
        /// <returns>The codes.</returns>
        /// <exception cref="InvalidOperationException">A code is not registered, repeats, or there are more than two.</exception>
        private List<string> Calendars(IList<string> seeded, int tenantId)
        {
            List<string> codes = [];
            foreach (string code in seeded)
            {
                string trimmed = code.Trim();
                if (calendars.TryGet(trimmed) is not ICalendarSystem calendar)
                {
                    throw Refused(tenantId, $"lists the calendar '{trimmed}', which no composed feature registers");
                }

                if (codes.Contains(calendar.Code, StringComparer.Ordinal))
                {
                    throw Refused(tenantId, $"lists the calendar '{calendar.Code}' twice");
                }

                codes.Add(calendar.Code);
            }

            return codes.Count <= MaxCalendars ? codes : throw Refused(tenantId, $"lists {codes.Count} calendars; a tenant has at most {MaxCalendars}");
        }

        /// <summary>The failure of a seed the settings editor would refuse.</summary>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="problem">What the seed does wrong.</param>
        /// <returns>The exception.</returns>
        private static InvalidOperationException Refused(int tenantId, string problem)
        {
            return new InvalidOperationException($"The seed of tenant {tenantId} ({TellmaSeedOptions.SectionPath}:{nameof(TellmaSeedOptions.Tenants)}) {problem}. Fix the seed and run the migrator again.");
        }
    }
}
