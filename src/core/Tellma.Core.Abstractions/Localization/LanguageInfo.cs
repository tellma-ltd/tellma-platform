// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Localization
{
    /// <summary>A language the platform can describe: one entry of the language catalogue.</summary>
    /// <remarks>
    ///     Describing a language is not shipping its strings: a key a language lacks falls back to
    ///     English, so an entry is safe before any translation exists. Symbols are unique across the
    ///     catalogue.
    /// </remarks>
    /// <param name="Code">The bare BCP 47 language subtag (<c>ar</c>), never a region pair; compared without regard to case.</param>
    /// <param name="NativeName">The language's name in itself (<c>العربية</c>), the same in every interface language.</param>
    /// <param name="Symbol">
    ///     The short mark in the language's own script that labels its column of a multilingual
    ///     field: <c>E</c> for English, <c>ع</c> for Arabic, as in <c>Name (ع)</c>.
    /// </param>
    /// <param name="IsRightToLeft">Whether the language is written right to left.</param>
    public sealed record LanguageInfo(string Code, string NativeName, string Symbol, bool IsRightToLeft);
}
