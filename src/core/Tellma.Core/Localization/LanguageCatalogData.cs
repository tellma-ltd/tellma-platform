// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Immutable;
using Tellma.Core.Abstractions.Localization;

namespace Tellma.Core.Localization
{
    /// <summary>
    ///     The language catalogue as data: every language the platform can describe, with the regions
    ///     its users may choose, its native digits and its default region. Additive by platform
    ///     change only, and frozen in code so a host's ICU release never changes what a user may
    ///     store.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A language's regions are the two-letter regions <c>R</c> for which .NET 10 predefines
    ///         the culture <c>&lt;language&gt;-R</c>: 238 pairs. Chinese adds five alias names that
    ///         both .NET and browsers resolve (<c>zh-CN</c> for <c>zh-Hans-CN</c>, and so on), for
    ///         243. <c>en-SA</c> exists nowhere, so <c>SA</c> is not an English region.
    ///     </para>
    ///     <para>
    ///         The default region of a language is its CLDR likely region, except English, whose
    ///         default is the United Kingdom's. A distribution's own choice for a language it offers
    ///         comes first.
    ///     </para>
    /// </remarks>
    internal static class LanguageCatalogData
    {
        /// <summary>The catalogue's entries, in catalogue order.</summary>
        public static ImmutableArray<CatalogueLanguage> Languages { get; } =
        [
            Entry(
                "en",
                "English",
                "E",
                rightToLeft: false,
                defaultRegion: "GB",
                nativeDigits: null,
                regions:
                    "AE,AG,AI,AS,AT,AU,BB,BE,BI,BM,BS,BW,BZ,CA,CC,CH,CK,CM,CX,CY,DE,DK,DM,ER,FI,FJ,FK,FM,GB,GD,GG,GH,GI,GM,GU,GY,"
                    + "HK,ID,IE,IL,IM,IN,IO,JE,JM,KE,KI,KN,KY,LC,LR,LS,MG,MH,MO,MP,MS,MT,MU,MV,MW,MY,NA,NF,NG,NL,NR,NU,NZ,PG,PH,PK,"
                    + "PN,PR,PW,RW,SB,SC,SD,SE,SG,SH,SI,SL,SS,SX,SZ,TC,TK,TO,TT,TV,TZ,UG,UM,US,VC,VG,VI,VU,WS,ZA,ZM,ZW"),
            Entry(
                "ar",
                "العربية",
                "ع",
                rightToLeft: true,
                defaultRegion: "EG",
                nativeDigits: "arab",
                regions: "AE,BH,DJ,DZ,EG,ER,IL,IQ,JO,KM,KW,LB,LY,MA,MR,OM,PS,QA,SA,SD,SO,SS,SY,TD,TN,YE"),
            Entry("am", "አማርኛ", "አ", rightToLeft: false, defaultRegion: "ET", nativeDigits: null, regions: "ET"),
            Entry("om", "Oromoo", "Om", rightToLeft: false, defaultRegion: "ET", nativeDigits: null, regions: "ET,KE"),
            Entry("ti", "ትግርኛ", "ት", rightToLeft: false, defaultRegion: "ET", nativeDigits: null, regions: "ER,ET"),
            Entry("so", "Soomaali", "So", rightToLeft: false, defaultRegion: "SO", nativeDigits: null, regions: "DJ,ET,KE,SO"),
            Entry("sw", "Kiswahili", "Sw", rightToLeft: false, defaultRegion: "TZ", nativeDigits: null, regions: "CD,KE,TZ,UG"),
            Entry(
                "fr",
                "Français",
                "F",
                rightToLeft: false,
                defaultRegion: "FR",
                nativeDigits: null,
                regions:
                    "BE,BF,BI,BJ,BL,CA,CD,CF,CG,CH,CI,CM,DJ,DZ,FR,GA,GF,GN,GP,GQ,HT,KM,LU,MA,MC,MF,MG,ML,MQ,MR,MU,NC,NE,PF,PM,RE,"
                    + "RW,SC,SN,SY,TD,TG,TN,VU,WF,YT"),
            Entry(
                "es",
                "Español",
                "Es",
                rightToLeft: false,
                defaultRegion: "ES",
                nativeDigits: null,
                regions: "AR,BO,BR,BZ,CL,CO,CR,CU,DO,EC,ES,GQ,GT,HN,MX,NI,PA,PE,PH,PR,PY,SV,US,UY,VE"),
            Entry(
                "pt",
                "Português",
                "P",
                rightToLeft: false,
                defaultRegion: "BR",
                nativeDigits: null,
                regions: "AO,BR,CH,CV,GQ,GW,LU,MO,MZ,PT,ST,TL"),
            Entry("de", "Deutsch", "D", rightToLeft: false, defaultRegion: "DE", nativeDigits: null, regions: "AT,BE,CH,DE,IT,LI,LU"),
            Entry("tr", "Türkçe", "T", rightToLeft: false, defaultRegion: "TR", nativeDigits: null, regions: "CY,TR"),
            Entry("ur", "اردو", "ا", rightToLeft: true, defaultRegion: "PK", nativeDigits: "arabext", regions: "IN,PK"),
            Entry("hi", "हिन्दी", "हि", rightToLeft: false, defaultRegion: "IN", nativeDigits: "deva", regions: "IN"),

            // The five Chinese names are aliases (zh-CN is zh-Hans-CN), which is why enumerating the
            // predefined cultures never lists them; every host resolves them all the same.
            Entry("zh", "中文", "中", rightToLeft: false, defaultRegion: "CN", nativeDigits: null, regions: "CN,HK,MO,SG,TW"),
        ];

        /// <summary>Makes one entry.</summary>
        /// <param name="code">The bare language code.</param>
        /// <param name="nativeName">The language's name in itself.</param>
        /// <param name="symbol">The short native-script mark.</param>
        /// <param name="rightToLeft">Whether the language is written right to left.</param>
        /// <param name="defaultRegion">The catalogue's default region.</param>
        /// <param name="nativeDigits">The CLDR numbering-system id of its native digits, or null for Latin.</param>
        /// <param name="regions">The regions, comma-separated in ordinal order.</param>
        /// <returns>The entry.</returns>
        private static CatalogueLanguage Entry(
            string code,
            string nativeName,
            string symbol,
            bool rightToLeft,
            string defaultRegion,
            string? nativeDigits,
            string regions)
        {
            return new CatalogueLanguage(
                new LanguageInfo(code, nativeName, symbol, rightToLeft),
                new LanguageFormats(code, defaultRegion, Array.AsReadOnly(regions.Split(',')), nativeDigits));
        }
    }

    /// <summary>One language of the catalogue with its formats.</summary>
    /// <param name="Info">The language.</param>
    /// <param name="Formats">Its regions, its catalogue default region and its native digits.</param>
    internal sealed record CatalogueLanguage(LanguageInfo Info, LanguageFormats Formats);
}
