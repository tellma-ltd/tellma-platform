// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Localization
{
    /// <summary>
    ///     The languages the platform can describe and the subset the distribution offers. A tenant's
    ///     content languages and a request's culture are both offered languages; describing a language
    ///     the distribution stopped offering still resolves a stored tenant's column labels.
    /// </summary>
    /// <remarks>Codes are bare language subtags, compared ordinally without regard to case.</remarks>
    public interface ILanguageCatalog
    {
        /// <summary>The offered languages, in the distribution's declaration order; the first is the distribution default.</summary>
        IReadOnlyList<LanguageInfo> Offered { get; }

        /// <summary>
        ///     Every language the platform can describe, offered or not: the catalogue's entries in
        ///     catalogue order, then the languages the distribution added. What <see cref="Find" />
        ///     searches, and the values of the language value set.
        /// </summary>
        IReadOnlyList<LanguageInfo> All { get; }

        /// <summary>A language of the whole catalogue, offered or not, or one the distribution added.</summary>
        /// <param name="code">The language code.</param>
        /// <returns>The language, or null when the catalogue does not describe it.</returns>
        LanguageInfo? Find(string code);

        /// <summary>Whether a bare language code is offered: <c>ar</c> may be; <c>ar-SA</c> never is.</summary>
        /// <param name="code">The language code.</param>
        /// <returns>True when it is offered.</returns>
        bool IsOffered(string code);

        /// <summary>The regional formats of a catalogued or added language.</summary>
        /// <param name="language">The language code.</param>
        /// <returns>Its formats.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="language" /> is null.</exception>
        /// <exception cref="KeyNotFoundException">The catalogue does not describe the language.</exception>
        LanguageFormats Formats(string language);

        /// <summary>Whether a region is offered with at least one offered language.</summary>
        /// <param name="region">The ISO 3166-1 alpha-2 code, upper case.</param>
        /// <returns>True when it is.</returns>
        bool IsRegionOffered(string region);
    }
}
