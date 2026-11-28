// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Tellma.Core.Abstractions.Tenancy;

namespace Tellma.Core.Tenancy
{
    /// <summary>
    ///     <c>Tellma:Seed</c>: the tenants the migrator's <c>migrate</c> registers and provisions when
    ///     the catalog lacks them, and the administrator it names in Development. Development only by
    ///     convention; both hosts validate the section at startup.
    /// </summary>
    public sealed class TellmaSeedOptions : IValidatableObject
    {
        /// <summary>The configuration section the options bind from.</summary>
        public const string SectionPath = "Tellma:Seed";

        /// <summary>The highest id a seed tenant may request; ids above it are allocated by registration.</summary>
        public const int MaxSeedTenantId = 999;

        /// <summary>The seed tenants, registered in this order.</summary>
        public IList<TellmaSeedTenant> Tenants { get; } = [];

        /// <summary>The email address of the administrator a Development provisioning names, or null.</summary>
        public string? AdminEmail { get; set; }

        /// <inheritdoc />
        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            // --- The administrator: an address, or nothing ---
            if (AdminEmail is not null && !IsEmailAddress(AdminEmail))
            {
                yield return new ValidationResult(
                    $"{SectionPath}:{nameof(AdminEmail)} is '{AdminEmail}', which is not an email address.",
                    [nameof(AdminEmail)]);
            }

            // --- Each tenant on its own ---
            HashSet<int> seen = [];
            for (int i = 0; i < Tenants.Count; i++)
            {
                TellmaSeedTenant tenant = Tenants[i];
                string entry = string.Create(CultureInfo.InvariantCulture, $"{SectionPath}:{nameof(Tenants)}:{i}");
                if (tenant.Id is < 1 or > MaxSeedTenantId)
                {
                    yield return Failure(entry, $"{nameof(tenant.Id)} is {tenant.Id}; a seed tenant requests an id from 1 to {MaxSeedTenantId}.");
                }
                else if (!seen.Add(tenant.Id))
                {
                    yield return Failure(entry, $"{nameof(tenant.Id)} {tenant.Id} is seeded twice.");
                }

                if (string.IsNullOrWhiteSpace(tenant.Name))
                {
                    yield return Failure(entry, $"{nameof(tenant.Name)} is missing.");
                }

                if (tenant.Category == TenantCategory.Sandbox && tenant.LiveTenantId is null)
                {
                    yield return Failure(entry, $"is a sandbox without the {nameof(tenant.LiveTenantId)} it derives from.");
                }
                else if (tenant.Category != TenantCategory.Sandbox && tenant.LiveTenantId is not null)
                {
                    yield return Failure(entry, $"names a {nameof(tenant.LiveTenantId)} but is not a sandbox.");
                }
                else if (tenant.LiveTenantId == tenant.Id)
                {
                    yield return Failure(entry, $"names itself as its {nameof(tenant.LiveTenantId)}.");
                }

                if (tenant.Languages.Any(string.IsNullOrWhiteSpace) || tenant.Calendars.Any(string.IsNullOrWhiteSpace))
                {
                    yield return Failure(entry, "lists a blank language or calendar.");
                }
            }

            // --- Across tenants: a seeded sandbox derives from a seeded live tenant when it names one ---
            foreach (TellmaSeedTenant sandbox in Tenants.Where(static tenant => tenant.LiveTenantId is not null))
            {
                TellmaSeedTenant? live = Tenants.FirstOrDefault(tenant => tenant.Id == sandbox.LiveTenantId);
                if (live is not null && live.Category != TenantCategory.Live)
                {
                    yield return new ValidationResult(
                        string.Create(CultureInfo.InvariantCulture, $"{SectionPath}:{nameof(Tenants)}: the sandbox {sandbox.Id} derives from {live.Id}, which is not a live tenant."),
                        [nameof(Tenants)]);
                }
            }
        }

        /// <summary>A failure of one seed tenant.</summary>
        /// <param name="entry">The entry's configuration path.</param>
        /// <param name="message">What is wrong with it.</param>
        /// <returns>The failure.</returns>
        private static ValidationResult Failure(string entry, string message)
        {
            return new ValidationResult($"{entry} {message}", [nameof(Tenants)]);
        }

        /// <summary>Whether a value has the shape of an email address: a local part, one <c>@</c> and a domain.</summary>
        /// <param name="value">The value.</param>
        /// <returns>True when it has.</returns>
        private static bool IsEmailAddress(string value)
        {
            int at = value.IndexOf('@', StringComparison.Ordinal);
            return at > 0
                && at == value.LastIndexOf('@')
                && at < value.Length - 1
                && !value.Any(char.IsWhiteSpace);
        }
    }

    /// <summary>One entry of <c>Tellma:Seed:Tenants</c>.</summary>
    public sealed class TellmaSeedTenant
    {
        /// <summary>The tenant's id: the id its registration requests, from 1 to 999.</summary>
        public int Id { get; set; }

        /// <summary>The tenant's name.</summary>
        public string? Name { get; set; }

        /// <summary>Whether the tenant is live or a sandbox.</summary>
        public TenantCategory Category { get; set; } = TenantCategory.Live;

        /// <summary>The live tenant a sandbox derives from; null for a live tenant.</summary>
        public int? LiveTenantId { get; set; }

        /// <summary>The tenant's content languages, such as <c>en</c> and <c>ar</c>, written into its settings by its first provisioning.</summary>
        public IList<string> Languages { get; } = [];

        /// <summary>The tenant's calendars, such as <c>gc</c> and <c>uq</c>, written into its settings by its first provisioning.</summary>
        public IList<string> Calendars { get; } = [];
    }
}
