// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using Tellma.Core.Abstractions.Calendars;
using Tellma.Core.Abstractions.Internal;
using Tellma.Core.Abstractions.Localization;

namespace Tellma.Core.Excel
{
    /// <summary>
    ///     What the Excel codec knows of the request it serves: the tenant, the deployment, the one
    ///     instant it stamps, and the culture, calendar, zone and labels its cells and headers are
    ///     rendered in. The codec reads no clock and opens no connection; everything comes from here.
    /// </summary>
    /// <param name="TenantId">The tenant the file is exported from or imported into.</param>
    /// <param name="DeploymentId">The deployment's identity, which, with the tenant, decides whether a file is same-source.</param>
    /// <param name="Now">The request's instant: the manifest's export time and the file name's timestamp.</param>
    /// <param name="TenantLanguages">The tenant's content languages in slot order, BCP 47 codes; one to three.</param>
    /// <param name="Culture">The request culture: number and date patterns, labels, the sheet's direction.</param>
    /// <param name="Calendar">The request calendar dates are rendered in.</param>
    /// <param name="TimeZone">The display zone instants are converted to and unzoned serials read in.</param>
    /// <param name="Labels">The labels of entities, properties and values in the request culture.</param>
    /// <param name="Catalog">The language catalog, for language names and symbols.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="TenantId" /> is not positive.</exception>
    /// <exception cref="ArgumentException">
    ///     <paramref name="DeploymentId" /> is null, empty or white space, or
    ///     <paramref name="TenantLanguages" /> is empty or holds a null entry.
    /// </exception>
    /// <exception cref="ArgumentNullException">Another argument is null.</exception>
    public sealed record ExcelContext(
        int TenantId,
        string DeploymentId,
        DateTimeOffset Now,
        IReadOnlyList<string> TenantLanguages,
        CultureInfo Culture,
        ICalendarSystem Calendar,
        TimeZoneInfo TimeZone,
        ILabelProvider Labels,
        ILanguageCatalog Catalog)
    {
        /// <inheritdoc cref="ExcelContext(int, string, DateTimeOffset, IReadOnlyList{string}, CultureInfo, ICalendarSystem, TimeZoneInfo, ILabelProvider, ILanguageCatalog)" />
        public int TenantId { get; init => field = Positive(value); } = Positive(TenantId);

        /// <inheritdoc cref="ExcelContext(int, string, DateTimeOffset, IReadOnlyList{string}, CultureInfo, ICalendarSystem, TimeZoneInfo, ILabelProvider, ILanguageCatalog)" />
        public string DeploymentId { get; init => field = NotBlank(value); } = NotBlank(DeploymentId);

        /// <inheritdoc cref="ExcelContext(int, string, DateTimeOffset, IReadOnlyList{string}, CultureInfo, ICalendarSystem, TimeZoneInfo, ILabelProvider, ILanguageCatalog)" />
        public IReadOnlyList<string> TenantLanguages { get; init => field = ReadOnlyCopies.OfEntries(value, requireOne: true, nameof(TenantLanguages)); } =
            ReadOnlyCopies.OfEntries(TenantLanguages, requireOne: true, nameof(TenantLanguages));

        /// <inheritdoc cref="ExcelContext(int, string, DateTimeOffset, IReadOnlyList{string}, CultureInfo, ICalendarSystem, TimeZoneInfo, ILabelProvider, ILanguageCatalog)" />
        public CultureInfo Culture { get; init => field = value ?? throw new ArgumentNullException(nameof(Culture)); } =
            Culture ?? throw new ArgumentNullException(nameof(Culture));

        /// <inheritdoc cref="ExcelContext(int, string, DateTimeOffset, IReadOnlyList{string}, CultureInfo, ICalendarSystem, TimeZoneInfo, ILabelProvider, ILanguageCatalog)" />
        public ICalendarSystem Calendar { get; init => field = value ?? throw new ArgumentNullException(nameof(Calendar)); } =
            Calendar ?? throw new ArgumentNullException(nameof(Calendar));

        /// <inheritdoc cref="ExcelContext(int, string, DateTimeOffset, IReadOnlyList{string}, CultureInfo, ICalendarSystem, TimeZoneInfo, ILabelProvider, ILanguageCatalog)" />
        public TimeZoneInfo TimeZone { get; init => field = value ?? throw new ArgumentNullException(nameof(TimeZone)); } =
            TimeZone ?? throw new ArgumentNullException(nameof(TimeZone));

        /// <inheritdoc cref="ExcelContext(int, string, DateTimeOffset, IReadOnlyList{string}, CultureInfo, ICalendarSystem, TimeZoneInfo, ILabelProvider, ILanguageCatalog)" />
        public ILabelProvider Labels { get; init => field = value ?? throw new ArgumentNullException(nameof(Labels)); } =
            Labels ?? throw new ArgumentNullException(nameof(Labels));

        /// <inheritdoc cref="ExcelContext(int, string, DateTimeOffset, IReadOnlyList{string}, CultureInfo, ICalendarSystem, TimeZoneInfo, ILabelProvider, ILanguageCatalog)" />
        public ILanguageCatalog Catalog { get; init => field = value ?? throw new ArgumentNullException(nameof(Catalog)); } =
            Catalog ?? throw new ArgumentNullException(nameof(Catalog));

        /// <summary>Refuses a tenant id that is not positive.</summary>
        /// <param name="value">The id.</param>
        /// <returns>The id.</returns>
        private static int Positive(int value)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value, nameof(TenantId));
            return value;
        }

        /// <summary>Refuses a blank deployment id.</summary>
        /// <param name="value">The id.</param>
        /// <returns>The id.</returns>
        private static string NotBlank(string value)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value, nameof(DeploymentId));
            return value;
        }
    }
}
