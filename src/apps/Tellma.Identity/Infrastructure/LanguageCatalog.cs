// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Options;
using Tellma.Identity.Options;

namespace Tellma.Identity.Infrastructure
{
    /// <summary>One language the sign-in UI offers.</summary>
    /// <param name="Culture">The culture name, for example <c>ar</c>.</param>
    /// <param name="NativeName">
    ///     What the language calls itself. A picker written in the language the user cannot read
    ///     is useless, so entries are never translated.
    /// </param>
    public sealed record LanguageChoice(string Culture, string NativeName);

    /// <summary>
    ///     The languages this deployment offers: the engine's shipped resources, narrowed by
    ///     configuration.
    ///     <para>
    ///         Narrowing is a real restriction rather than a cosmetic one — the offered set is also
    ///         what request localization accepts, so a language a deployment excluded cannot be
    ///         reached by a query string or a stale cookie either.
    ///     </para>
    /// </summary>
    /// <param name="options">The engine options.</param>
    public sealed class LanguageCatalog(IOptions<TellmaIdentityOptions> options)
    {
        /// <summary>
        ///     Every language the engine ships resources for, in display order. Adding one means
        ///     adding its resx and its font subset; this list is what makes it reachable.
        /// </summary>
        public static IReadOnlyList<LanguageChoice> Shipped { get; } =
        [
            new LanguageChoice("en", "English"),
            new LanguageChoice("ar", "العربية"),
        ];

        /// <summary>
        ///     What this deployment offers: the configured subset, or everything shipped when the
        ///     configuration names nothing. Order follows <see cref="Shipped" /> so the picker does
        ///     not reshuffle with configuration.
        /// </summary>
        public IReadOnlyList<LanguageChoice> Offered { get; } = Resolve(options);

        /// <summary>The offered culture names, for request-localization setup.</summary>
        public IReadOnlyList<string> OfferedCultures => [.. Offered.Select(static language => language.Culture)];

        /// <summary>Whether a culture name is one this deployment offers.</summary>
        /// <param name="culture">The culture name to test.</param>
        /// <returns><c>true</c> when the language is offered.</returns>
        public bool IsOffered(string? culture)
        {
            return culture is not null
                && Offered.Any(language => string.Equals(language.Culture, culture, StringComparison.Ordinal));
        }

        /// <summary>Intersects the configured names with what the engine ships.</summary>
        private static IReadOnlyList<LanguageChoice> Resolve(IOptions<TellmaIdentityOptions> options)
        {
            ArgumentNullException.ThrowIfNull(options);

            IList<string> configured = options.Value.Ui.Languages;
            return configured.Count == 0
                ? Shipped
                : [.. Shipped.Where(language => configured.Contains(language.Culture, StringComparer.OrdinalIgnoreCase))];
        }
    }
}
