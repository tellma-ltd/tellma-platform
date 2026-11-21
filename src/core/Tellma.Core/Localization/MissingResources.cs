// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Globalization;
using Tellma.Core.Abstractions.Localization;

namespace Tellma.Core.Localization
{
    /// <summary>
    ///     Reports resource keys that no culture of a lookup's chain holds: the
    ///     <c>tellma.localization.missing</c> counter and the <c>MissingResourceString</c> event, each
    ///     once per key and culture per process.
    /// </summary>
    internal sealed class MissingResources
    {
        /// <summary>The <c>culture</c> tag of a lookup whose language the distribution does not offer.</summary>
        internal const string OtherCulture = "other";

        /// <summary>How many keys the tracker remembers having reported; beyond it, further misses are not reported.</summary>
        private const int ReportedCapacity = 10_000;

        /// <summary>The keys and cultures already reported.</summary>
        private readonly ConcurrentDictionary<(string BaseName, string Key, string Culture), byte> _reported = new();

        /// <summary>The <c>tellma.localization.missing</c> counter.</summary>
        private readonly Counter<long> _missing;

        /// <summary>The language catalogue, which decides whether a culture's language is tagged by its code.</summary>
        private readonly ILanguageCatalog _languages;

        /// <summary>Where a missing key is reported.</summary>
        private readonly ILogger _logger;

        /// <summary>Creates the tracker.</summary>
        /// <param name="meterFactory">Creates the meter the counter is on.</param>
        /// <param name="languages">The language catalogue, whose offered languages are the counter's tag values.</param>
        /// <param name="logger">Where misses are logged.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public MissingResources(IMeterFactory meterFactory, ILanguageCatalog languages, ILogger logger)
        {
            ArgumentNullException.ThrowIfNull(meterFactory);
            ArgumentNullException.ThrowIfNull(languages);
            ArgumentNullException.ThrowIfNull(logger);

            _languages = languages;
            _logger = logger;
            _missing = meterFactory.Create(LocalizationTelemetryNames.MeterName).CreateCounter<long>(
                LocalizationTelemetryNames.Missing,
                "{lookup}",
                "Resource keys absent from every culture of a lookup's fallback chain, once per key and culture.");
        }

        /// <summary>Reports a miss, unless this key was already reported for this culture.</summary>
        /// <param name="baseName">The base name of the resources looked in.</param>
        /// <param name="key">The key.</param>
        /// <param name="culture">The culture the lookup started from.</param>
        public void Report(string baseName, string key, CultureInfo culture)
        {
            // --- Once per key and culture, within a bound a runaway caller cannot exceed ---
            (string, string, string) entry = (baseName, key, culture.Name);
            if (_reported.ContainsKey(entry) || _reported.Count >= ReportedCapacity || !_reported.TryAdd(entry, 0))
            {
                return;
            }

            // --- The tag is a closed set: the offered languages, and one value for anything else ---
            string language = culture.Name.Split('-', 2)[0];
            string tag = language.Length > 0 && _languages.IsOffered(language) ? _languages.Find(language)!.Code : OtherCulture;

            _missing.Add(1, new KeyValuePair<string, object?>(LocalizationTelemetryNames.CultureTag, tag));
            LocalizationLog.MissingResourceString(_logger, baseName, key, culture.Name);
        }
    }
}
