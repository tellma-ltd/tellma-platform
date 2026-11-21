// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Localization
{
    /// <summary>
    ///     A language's regional formats as the distribution offers them: the regions its users may
    ///     choose, the one they get by default, and its native digit system. Fixed in code, so a
    ///     host's ICU release never changes what a user may store.
    /// </summary>
    /// <param name="Language">The bare language code (<c>ar</c>).</param>
    /// <param name="DefaultRegion">
    ///     The region a user of the language gets when neither they nor their tenant chose one: the
    ///     distribution's choice for an offered language (<c>SA</c>), else the catalogue's (<c>EG</c>).
    ///     An ISO 3166-1 alpha-2 code in upper case.
    /// </param>
    /// <param name="Regions">The regions offered with the language, in ordinal order (<c>AE</c>, <c>BH</c>, …, <c>YE</c>).</param>
    /// <param name="NativeDigits">
    ///     The CLDR numbering-system id of the language's native digits (<c>arab</c>, <c>arabext</c>,
    ///     <c>deva</c>); null when its digits are Latin.
    /// </param>
    public sealed record LanguageFormats(
        string Language, string DefaultRegion, IReadOnlyList<string> Regions, string? NativeDigits);
}
