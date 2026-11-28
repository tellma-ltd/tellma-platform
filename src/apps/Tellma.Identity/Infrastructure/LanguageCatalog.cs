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
        ///     How many trailing subtags are removed from a language tag in search of an offered
        ///     language. The depth request localization itself falls back through.
        /// </summary>
        private const int MaxParentDepth = 5;

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

        /// <summary>
        ///     The offered language a <c>ui_locales</c> value selects: the first of its tags, in
        ///     the order given, that names an offered language or a regional form of one.
        ///     <para>
        ///         The same rule request localization applies to the query value, so a language
        ///         list reads the same wherever it arrives from: a tag matches without case, and
        ///         a tag that is not offered is retried with its last subtag removed, so
        ///         <c>ar-SA</c> selects <c>ar</c>.
        ///     </para>
        /// </summary>
        /// <param name="uiLocales">The language tags, separated by spaces, most preferred first.</param>
        /// <returns>The offered culture name, or null when no tag selects one.</returns>
        public string? Match(string? uiLocales)
        {
            if (CarriedQuery.NormalizeUiLocales(uiLocales) is not { } tags)
            {
                return null;
            }

            foreach (string tag in tags.Split(' '))
            {
                // Each pass drops one trailing subtag; the bound is the one request localization
                // uses, and a well-formed tag has few enough subtags never to reach it.
                ReadOnlySpan<char> candidate = tag;
                for (int depth = 0; depth <= MaxParentDepth; depth++)
                {
                    foreach (LanguageChoice language in Offered)
                    {
                        if (candidate.Equals(language.Culture, StringComparison.OrdinalIgnoreCase))
                        {
                            return language.Culture;
                        }
                    }

                    int hyphen = candidate.LastIndexOf('-');
                    if (hyphen <= 0)
                    {
                        break;
                    }

                    candidate = candidate[..hyphen];
                }
            }

            return null;
        }

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
