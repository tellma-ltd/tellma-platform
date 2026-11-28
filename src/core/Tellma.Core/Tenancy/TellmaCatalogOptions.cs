// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient;
using System.ComponentModel.DataAnnotations;

namespace Tellma.Core.Tenancy
{
    /// <summary>
    ///     The catalog database and the tenant registry's refresh, bound from <c>Tellma:Catalog</c>.
    /// </summary>
    /// <remarks>
    ///     Validated by the startup gate. The connection string may carry a credential, so no
    ///     validation message ever quotes it.
    /// </remarks>
    public sealed class TellmaCatalogOptions : IValidatableObject
    {
        /// <summary>The configuration section the options bind from.</summary>
        public const string SectionPath = "Tellma:Catalog";

        /// <summary>The shortest refresh interval accepted.</summary>
        public static readonly TimeSpan MinRefreshInterval = TimeSpan.FromSeconds(1);

        /// <summary>The longest refresh interval accepted.</summary>
        public static readonly TimeSpan MaxRefreshInterval = TimeSpan.FromHours(1);

        /// <summary>The longest staleness bound accepted.</summary>
        public static readonly TimeSpan MaxMaxStaleness = TimeSpan.FromDays(1);

        /// <summary>
        ///     The catalog's SqlClient connection string. It names the catalog database and the
        ///     credential the host reaches it with, and its own pool size: <c>Max Pool Size=10</c> is
        ///     the recommended bound.
        /// </summary>
        [Required]
        public string? ConnectionString { get; set; }

        /// <summary>How often each instance probes the catalog's version; 15 seconds by default.</summary>
        public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromSeconds(15);

        /// <summary>
        ///     How long a snapshot is served while refreshes fail; 5 minutes by default. Beyond it
        ///     every tenant request is refused as catalog-unavailable, so a suspension never stays
        ///     ignored because the catalog is unreachable.
        /// </summary>
        public TimeSpan MaxStaleness { get; set; } = TimeSpan.FromMinutes(5);

        /// <inheritdoc />
        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            // The connection string is parsed, never quoted: it may hold a password.
            if (!string.IsNullOrWhiteSpace(ConnectionString) && !Parses(ConnectionString))
            {
                yield return new ValidationResult(
                    $"{SectionPath}:{nameof(ConnectionString)} is not a valid SqlClient connection string.",
                    [nameof(ConnectionString)]);
            }

            if (RefreshInterval < MinRefreshInterval || RefreshInterval > MaxRefreshInterval)
            {
                yield return new ValidationResult(
                    $"{SectionPath}:{nameof(RefreshInterval)} is {RefreshInterval}; it must lie between {MinRefreshInterval} and {MaxRefreshInterval}.",
                    [nameof(RefreshInterval)]);
            }

            if (MaxStaleness < RefreshInterval || MaxStaleness > MaxMaxStaleness)
            {
                yield return new ValidationResult(
                    $"{SectionPath}:{nameof(MaxStaleness)} is {MaxStaleness}; it must be at least {nameof(RefreshInterval)} ({RefreshInterval}) and at most {MaxMaxStaleness}.",
                    [nameof(MaxStaleness)]);
            }
        }

        /// <summary>Whether a value parses as a SqlClient connection string.</summary>
        /// <param name="connectionString">The value.</param>
        /// <returns>True when it parses.</returns>
        internal static bool Parses(string connectionString)
        {
            try
            {
                _ = new SqlConnectionStringBuilder(connectionString);
                return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
            catch (FormatException)
            {
                return false;
            }
            catch (KeyNotFoundException)
            {
                return false;
            }
        }
    }
}
