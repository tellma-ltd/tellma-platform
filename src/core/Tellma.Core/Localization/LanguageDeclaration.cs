// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Localization;

namespace Tellma.Core.Localization
{
    /// <summary>
    ///     What a distribution's composition method said about languages: the codes it offers, in
    ///     order, and the languages it added to the catalogue. The composition root registers the one
    ///     declaration of a host; <see cref="LanguageCatalog" /> reads it.
    /// </summary>
    /// <remarks>
    ///     A declaration is recorded as written, valid or not. The catalogue resolves it leniently
    ///     and never throws; the startup gate reports everything wrong with it.
    /// </remarks>
    public sealed class LanguageDeclaration
    {
        /// <summary>Creates a declaration.</summary>
        /// <param name="codes">
        ///     The offered codes in declaration order, each a bare language (<c>ar</c>) or a language
        ///     with the region its users get by default (<c>ar-SA</c>); null when the distribution
        ///     declared none, which offers English.
        /// </param>
        /// <param name="added">The languages added to the catalogue, in the order they were added.</param>
        /// <exception cref="ArgumentNullException"><paramref name="added" /> is null.</exception>
        public LanguageDeclaration(IReadOnlyList<string>? codes, IReadOnlyList<AddedLanguage> added)
        {
            ArgumentNullException.ThrowIfNull(added);
            Codes = codes is null ? null : [.. codes];
            Added = [.. added];
        }

        /// <summary>The declaration of a distribution that said nothing: English, and no added language.</summary>
        public static LanguageDeclaration None { get; } = new(null, []);

        /// <summary>The offered codes in declaration order; null when the distribution declared none.</summary>
        public IReadOnlyList<string>? Codes { get; }

        /// <summary>The languages added to the catalogue, in the order they were added.</summary>
        public IReadOnlyList<AddedLanguage> Added { get; }
    }

    /// <summary>A language a distribution added to the catalogue, with its formats.</summary>
    /// <param name="Info">The language.</param>
    /// <param name="Formats">Its regions, its default region and its native digits, under the same code.</param>
    public sealed record AddedLanguage(LanguageInfo Info, LanguageFormats Formats);
}
