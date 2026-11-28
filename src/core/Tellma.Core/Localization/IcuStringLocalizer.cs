// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Localization;
using System.Globalization;
using Tellma.Core.Abstractions.Localization;

namespace Tellma.Core.Localization
{
    /// <summary>
    ///     A localizer that looks resources up through the framework and renders them as ICU messages,
    ///     in the culture of the flow's <see cref="LocalizationScope" /> when one is entered, else in
    ///     the current UI culture.
    /// </summary>
    /// <param name="resources">The framework's lookup of one resource set.</param>
    /// <param name="renderer">The shared ICU renderer.</param>
    /// <param name="missing">Reports keys no culture holds.</param>
    internal sealed class IcuStringLocalizer(
        CultureResourceLocalizer resources,
        IcuMessageRenderer renderer,
        MissingResources missing) : IStringLocalizer
    {
        /// <inheritdoc />
        public LocalizedString this[string name] => Localize(name, [], argumentsSupplied: false);

        /// <inheritdoc />
        /// <exception cref="ArgumentException">
        ///     More than one argument object was passed, or the one passed is a lone value rather than
        ///     a set of named arguments.
        /// </exception>
        public LocalizedString this[string name, params object[] arguments]
        {
            get
            {
                ArgumentNullException.ThrowIfNull(arguments);
                return Localize(name, arguments, argumentsSupplied: arguments.Length > 0 && arguments[0] is not null);
            }
        }

        /// <summary>The culture of this flow's lookups: the scope's, else the current UI culture.</summary>
        private static CultureInfo Culture => LocalizationScope.Current?.Culture ?? CultureInfo.CurrentUICulture;

        /// <inheritdoc />
        /// <remarks>The values are the raw messages, unformatted, in the culture of this flow's lookups.</remarks>
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures)
        {
            return resources.FindAll(includeParentCultures, Culture);
        }

        /// <summary>Looks a resource up and renders it.</summary>
        /// <param name="name">The key.</param>
        /// <param name="arguments">The indexer's arguments.</param>
        /// <param name="argumentsSupplied">Whether the caller passed an argument object.</param>
        /// <returns>The rendered string; the key itself, flagged not found, when no culture holds it.</returns>
        private LocalizedString Localize(string name, object[] arguments, bool argumentsSupplied)
        {
            ArgumentNullException.ThrowIfNull(name);

            // --- The arguments first, so a positional call fails whether or not the key exists ---
            IReadOnlyDictionary<string, object?> named = MessageArguments.From(arguments);

            // --- The lookup: the culture's parent chain to the neutral English resources ---
            CultureInfo culture = Culture;
            string? value = resources.Find(name, culture);
            if (value is null)
            {
                missing.Report(resources.BaseName, name, culture);
                return new LocalizedString(name, name, resourceNotFound: true, searchedLocation: resources.BaseName);
            }

            // --- The rendering, in the same culture ---
            string text = renderer.Render(resources.BaseName, name, value, named, culture, argumentsSupplied);
            return new LocalizedString(name, text, resourceNotFound: false, searchedLocation: resources.BaseName);
        }
    }
}
