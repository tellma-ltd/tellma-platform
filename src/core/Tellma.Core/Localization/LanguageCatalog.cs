// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Localization;

namespace Tellma.Core.Localization
{
    /// <summary>
    ///     The language catalogue of a distribution: the frozen list of languages the platform can
    ///     describe, the languages the distribution added, and the subset it offers. The platform
    ///     registers it as a singleton over the distribution's <see cref="LanguageDeclaration" />.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The catalogue is data in code. Every language carries the regions its users may
    ///         choose, frozen when the language was catalogued, so the host's ICU release never
    ///         changes what a user may store.
    ///     </para>
    ///     <para>
    ///         Construction never throws: a declaration with mistakes resolves to what is sound in
    ///         it (English when nothing is), and the startup gate refuses the host with every
    ///         mistake listed.
    ///     </para>
    /// </remarks>
    public sealed class LanguageCatalog : ILanguageCatalog
    {
        private readonly LanguageSet _set;

        /// <summary>Creates the catalogue of a distribution.</summary>
        /// <param name="declaration">What the distribution declared.</param>
        /// <exception cref="ArgumentNullException"><paramref name="declaration" /> is null.</exception>
        public LanguageCatalog(LanguageDeclaration declaration)
        {
            ArgumentNullException.ThrowIfNull(declaration);
            _set = LanguageSet.Resolve(declaration);
        }

        /// <inheritdoc />
        public IReadOnlyList<LanguageInfo> Offered => _set.Offered;

        /// <inheritdoc />
        public IReadOnlyList<LanguageInfo> All => _set.All;

        /// <inheritdoc />
        public LanguageInfo? Find(string code)
        {
            return code is not null && _set.ByCode.TryGetValue(code, out LanguageInfo? language) ? language : null;
        }

        /// <inheritdoc />
        public bool IsOffered(string code)
        {
            return code is not null && _set.OfferedCodes.Contains(code);
        }

        /// <inheritdoc />
        public LanguageFormats Formats(string language)
        {
            ArgumentNullException.ThrowIfNull(language);
            return _set.Formats.TryGetValue(language, out LanguageFormats? formats)
                ? formats
                : throw new KeyNotFoundException($"The language catalogue does not describe the language '{language}'.");
        }

        /// <inheritdoc />
        public bool IsRegionOffered(string region)
        {
            return region is not null && _set.OfferedRegions.Contains(region);
        }
    }
}
