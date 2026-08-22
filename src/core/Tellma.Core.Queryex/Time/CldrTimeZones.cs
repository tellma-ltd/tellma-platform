// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;

namespace Tellma.Core.Queryex.Time
{
    /// <summary>
    ///     Resolves an IANA time-zone id, such as <c>Africa/Nairobi</c>, to the Windows time-zone
    ///     name that SQL Server's <c>AT TIME ZONE</c> clause expects, such as
    ///     <c>E. Africa Standard Time</c>.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         SQL Server names time zones the Windows way on every platform it runs on, Linux
    ///         included, so a single table is correct for every deployment.
    ///     </para>
    ///     <para>
    ///         The table is the Unicode CLDR mapping, embedded in this assembly rather than read
    ///         from the machine. Which zone ids compile is part of the language surface, so the
    ///         answer has to be identical on a developer's laptop, on a build agent, and in
    ///         production. Asking <see cref="TimeZoneInfo"/> instead would delegate that decision
    ///         to whatever time-zone database the host happens to carry, and an expression that
    ///         compiled in one place would be rejected in another.
    ///     </para>
    ///     <para>
    ///         The table records the deprecated ids alongside the current ones — <c>Asia/Calcutta</c>
    ///         as well as <c>Asia/Kolkata</c>, <c>America/Godthab</c> as well as <c>America/Nuuk</c> —
    ///         because stored expressions outlive the renames upstream, and a query that worked
    ///         yesterday must not stop compiling because the zone was given a new name.
    ///     </para>
    ///     <para>
    ///         Resolution is deliberately one way. Windows zones are coarser than IANA zones, so
    ///         many ids share one Windows name and there is no reverse lookup worth offering.
    ///     </para>
    /// </remarks>
    internal static class CldrTimeZones
    {
        /// <summary>
        ///     The mapping table in its embedded form: one record per Windows zone, records
        ///     separated by line feeds, each record the Windows name, a vertical bar, then the
        ///     IANA ids that resolve to it separated by semicolons.
        /// </summary>
        /// <remarks>
        ///     Held as one constant rather than as hundreds of separate entries so that the
        ///     assembly carries a single string and the source stays reviewable: one line per
        ///     Windows zone, ordered ordinally by that name, so refreshing the table from a newer
        ///     CLDR release produces a diff a reader can actually check. Neither kind of name can
        ///     contain a vertical bar or a semicolon, so the format needs no escaping.
        /// </remarks>
        private const string Data =
            "AUS Central Standard Time|Australia/Darwin;Australia/North\n"
            + "AUS Eastern Standard Time|Australia/ACT;Australia/Canberra;Australia/Melbourne;Australia/NSW;Australia/Sydney;Australia/Victoria\n"
            + "Afghanistan Standard Time|Asia/Kabul\n"
            + "Alaskan Standard Time|America/Anchorage;America/Juneau;America/Metlakatla;America/Nome;America/Sitka;America/Yakutat;US/Alaska\n"
            + "Aleutian Standard Time|America/Adak;America/Atka;US/Aleutian\n"
            + "Altai Standard Time|Asia/Barnaul\n"
            + "Arab Standard Time|Asia/Aden;Asia/Bahrain;Asia/Kuwait;Asia/Qatar;Asia/Riyadh\n"
            + "Arabian Standard Time|Asia/Dubai;Asia/Muscat;Etc/GMT-4\n"
            + "Arabic Standard Time|Asia/Baghdad\n"
            + "Argentina Standard Time|America/Argentina/Buenos_Aires;America/Argentina/Catamarca;America/Argentina/ComodRivadavia;America/Argentina/Cordoba;America/Argentina/Jujuy;America/Argentina/La_Rioja;America/Argentina/Mendoza;America/Argentina/Rio_Gallegos;America/Argentina/Salta;America/Argentina/San_Juan;America/Argentina/San_Luis;America/Argentina/Tucuman;America/Argentina/Ushuaia;America/Buenos_Aires;America/Catamarca;America/Cordoba;America/Jujuy;America/Mendoza;America/Rosario\n"
            + "Astrakhan Standard Time|Europe/Astrakhan;Europe/Ulyanovsk\n"
            + "Atlantic Standard Time|America/Glace_Bay;America/Goose_Bay;America/Halifax;America/Moncton;America/Thule;Atlantic/Bermuda;Canada/Atlantic\n"
            + "Aus Central W. Standard Time|Australia/Eucla\n"
            + "Azerbaijan Standard Time|Asia/Baku\n"
            + "Azores Standard Time|America/Scoresbysund;Atlantic/Azores\n"
            + "Bahia Standard Time|America/Bahia\n"
            + "Bangladesh Standard Time|Asia/Dacca;Asia/Dhaka;Asia/Thimbu;Asia/Thimphu\n"
            + "Belarus Standard Time|Europe/Minsk\n"
            + "Bougainville Standard Time|Pacific/Bougainville\n"
            + "Canada Central Standard Time|America/Regina;America/Swift_Current;Canada/East-Saskatchewan;Canada/Saskatchewan\n"
            + "Cape Verde Standard Time|Atlantic/Cape_Verde;Etc/GMT+1\n"
            + "Caucasus Standard Time|Asia/Yerevan\n"
            + "Cen. Australia Standard Time|Australia/Adelaide;Australia/Broken_Hill;Australia/South;Australia/Yancowinna\n"
            + "Central America Standard Time|America/Belize;America/Costa_Rica;America/El_Salvador;America/Guatemala;America/Managua;America/Tegucigalpa;Etc/GMT+6;Pacific/Galapagos\n"
            + "Central Asia Standard Time|Antarctica/Vostok;Asia/Bishkek;Asia/Kashgar;Asia/Urumqi;Etc/GMT-6;Indian/Chagos\n"
            + "Central Brazilian Standard Time|America/Campo_Grande;America/Cuiaba\n"
            + "Central Europe Standard Time|Europe/Belgrade;Europe/Bratislava;Europe/Budapest;Europe/Ljubljana;Europe/Podgorica;Europe/Prague;Europe/Tirane\n"
            + "Central European Standard Time|Europe/Sarajevo;Europe/Skopje;Europe/Warsaw;Europe/Zagreb;Poland\n"
            + "Central Pacific Standard Time|Antarctica/Casey;Etc/GMT-11;Pacific/Efate;Pacific/Guadalcanal;Pacific/Kosrae;Pacific/Noumea;Pacific/Pohnpei;Pacific/Ponape\n"
            + "Central Standard Time|America/Chicago;America/Indiana/Knox;America/Indiana/Tell_City;America/Knox_IN;America/Matamoros;America/Menominee;America/North_Dakota/Beulah;America/North_Dakota/Center;America/North_Dakota/New_Salem;America/Ojinaga;America/Rainy_River;America/Rankin_Inlet;America/Resolute;America/Winnipeg;CST6CDT;Canada/Central;US/Central;US/Indiana-Starke\n"
            + "Central Standard Time (Mexico)|America/Bahia_Banderas;America/Chihuahua;America/Merida;America/Mexico_City;America/Monterrey;Mexico/General\n"
            + "Chatham Islands Standard Time|NZ-CHAT;Pacific/Chatham\n"
            + "China Standard Time|Asia/Chongqing;Asia/Chungking;Asia/Harbin;Asia/Hong_Kong;Asia/Macao;Asia/Macau;Asia/Shanghai;Hongkong;PRC\n"
            + "Cuba Standard Time|America/Havana;Cuba\n"
            + "Dateline Standard Time|Etc/GMT+12\n"
            + "E. Africa Standard Time|Africa/Addis_Ababa;Africa/Asmara;Africa/Asmera;Africa/Dar_es_Salaam;Africa/Djibouti;Africa/Kampala;Africa/Mogadishu;Africa/Nairobi;Antarctica/Syowa;Etc/GMT-3;Indian/Antananarivo;Indian/Comoro;Indian/Mayotte\n"
            + "E. Australia Standard Time|Australia/Brisbane;Australia/Lindeman;Australia/Queensland\n"
            + "E. Europe Standard Time|Europe/Chisinau;Europe/Tiraspol\n"
            + "E. South America Standard Time|America/Sao_Paulo;Brazil/East\n"
            + "Easter Island Standard Time|Chile/EasterIsland;Pacific/Easter\n"
            + "Eastern Standard Time|America/Detroit;America/Indiana/Petersburg;America/Indiana/Vincennes;America/Indiana/Winamac;America/Iqaluit;America/Kentucky/Louisville;America/Kentucky/Monticello;America/Louisville;America/Montreal;America/Nassau;America/New_York;America/Nipigon;America/Pangnirtung;America/Thunder_Bay;America/Toronto;Canada/Eastern;EST5EDT;US/Eastern;US/Michigan\n"
            + "Eastern Standard Time (Mexico)|America/Cancun\n"
            + "Egypt Standard Time|Africa/Cairo;Egypt\n"
            + "Ekaterinburg Standard Time|Asia/Yekaterinburg\n"
            + "FLE Standard Time|Europe/Helsinki;Europe/Kiev;Europe/Kyiv;Europe/Mariehamn;Europe/Riga;Europe/Sofia;Europe/Tallinn;Europe/Uzhgorod;Europe/Vilnius;Europe/Zaporozhye\n"
            + "Fiji Standard Time|Pacific/Fiji\n"
            + "GMT Standard Time|Atlantic/Canary;Atlantic/Faeroe;Atlantic/Faroe;Atlantic/Madeira;Eire;Europe/Belfast;Europe/Dublin;Europe/Guernsey;Europe/Isle_of_Man;Europe/Jersey;Europe/Lisbon;Europe/London;GB;GB-Eire;Portugal;WET\n"
            + "GTB Standard Time|Asia/Famagusta;Asia/Nicosia;EET;Europe/Athens;Europe/Bucharest;Europe/Nicosia\n"
            + "Georgian Standard Time|Asia/Tbilisi\n"
            + "Greenland Standard Time|America/Godthab;America/Nuuk\n"
            + "Greenwich Standard Time|Africa/Abidjan;Africa/Accra;Africa/Bamako;Africa/Banjul;Africa/Bissau;Africa/Conakry;Africa/Dakar;Africa/Freetown;Africa/Lome;Africa/Monrovia;Africa/Nouakchott;Africa/Ouagadougou;Africa/Timbuktu;America/Danmarkshavn;Atlantic/Reykjavik;Atlantic/St_Helena;Iceland\n"
            + "Haiti Standard Time|America/Port-au-Prince\n"
            + "Hawaiian Standard Time|Etc/GMT+10;HST;Pacific/Honolulu;Pacific/Johnston;Pacific/Rarotonga;Pacific/Tahiti;US/Hawaii\n"
            + "India Standard Time|Asia/Calcutta;Asia/Kolkata\n"
            + "Iran Standard Time|Asia/Tehran;Iran\n"
            + "Israel Standard Time|Asia/Jerusalem;Asia/Tel_Aviv;Israel\n"
            + "Jordan Standard Time|Asia/Amman\n"
            + "Kaliningrad Standard Time|Europe/Kaliningrad\n"
            + "Korea Standard Time|Asia/Seoul;ROK\n"
            + "Libya Standard Time|Africa/Tripoli;Libya\n"
            + "Line Islands Standard Time|Etc/GMT-14;Pacific/Kiritimati\n"
            + "Lord Howe Standard Time|Australia/LHI;Australia/Lord_Howe\n"
            + "Magadan Standard Time|Asia/Magadan\n"
            + "Magallanes Standard Time|America/Coyhaique;America/Punta_Arenas\n"
            + "Marquesas Standard Time|Pacific/Marquesas\n"
            + "Mauritius Standard Time|Indian/Mahe;Indian/Mauritius;Indian/Reunion\n"
            + "Middle East Standard Time|Asia/Beirut\n"
            + "Montevideo Standard Time|America/Montevideo\n"
            + "Morocco Standard Time|Africa/Casablanca;Africa/El_Aaiun\n"
            + "Mountain Standard Time|America/Boise;America/Cambridge_Bay;America/Ciudad_Juarez;America/Denver;America/Edmonton;America/Inuvik;America/Shiprock;America/Yellowknife;Canada/Mountain;MST7MDT;Navajo;US/Mountain\n"
            + "Mountain Standard Time (Mexico)|America/Mazatlan;Mexico/BajaSur\n"
            + "Myanmar Standard Time|Asia/Rangoon;Asia/Yangon;Indian/Cocos\n"
            + "N. Central Asia Standard Time|Asia/Novosibirsk\n"
            + "Namibia Standard Time|Africa/Windhoek\n"
            + "Nepal Standard Time|Asia/Kathmandu;Asia/Katmandu\n"
            + "New Zealand Standard Time|Antarctica/McMurdo;Antarctica/South_Pole;NZ;Pacific/Auckland\n"
            + "Newfoundland Standard Time|America/St_Johns;Canada/Newfoundland\n"
            + "Norfolk Standard Time|Pacific/Norfolk\n"
            + "North Asia East Standard Time|Asia/Irkutsk\n"
            + "North Asia Standard Time|Asia/Krasnoyarsk;Asia/Novokuznetsk\n"
            + "North Korea Standard Time|Asia/Pyongyang\n"
            + "Omsk Standard Time|Asia/Omsk\n"
            + "Pacific SA Standard Time|America/Santiago;Chile/Continental\n"
            + "Pacific Standard Time|America/Los_Angeles;America/Vancouver;Canada/Pacific;PST8PDT;US/Pacific;US/Pacific-New\n"
            + "Pacific Standard Time (Mexico)|America/Ensenada;America/Santa_Isabel;America/Tijuana;Mexico/BajaNorte\n"
            + "Pakistan Standard Time|Asia/Karachi\n"
            + "Paraguay Standard Time|America/Asuncion\n"
            + "Qyzylorda Standard Time|Asia/Qyzylorda\n"
            + "Romance Standard Time|Africa/Ceuta;CET;Europe/Brussels;Europe/Copenhagen;Europe/Madrid;Europe/Paris;MET\n"
            + "Russia Time Zone 10|Asia/Srednekolymsk\n"
            + "Russia Time Zone 11|Asia/Anadyr;Asia/Kamchatka\n"
            + "Russia Time Zone 3|Europe/Samara\n"
            + "Russian Standard Time|Europe/Kirov;Europe/Moscow;Europe/Simferopol;W-SU\n"
            + "SA Eastern Standard Time|America/Belem;America/Cayenne;America/Fortaleza;America/Maceio;America/Paramaribo;America/Recife;America/Santarem;Antarctica/Palmer;Antarctica/Rothera;Atlantic/Stanley;Etc/GMT+3\n"
            + "SA Pacific Standard Time|America/Atikokan;America/Bogota;America/Cayman;America/Coral_Harbour;America/Eirunepe;America/Guayaquil;America/Jamaica;America/Lima;America/Panama;America/Porto_Acre;America/Rio_Branco;Brazil/Acre;EST;Etc/GMT+5;Jamaica\n"
            + "SA Western Standard Time|America/Anguilla;America/Antigua;America/Aruba;America/Barbados;America/Blanc-Sablon;America/Boa_Vista;America/Curacao;America/Dominica;America/Grenada;America/Guadeloupe;America/Guyana;America/Kralendijk;America/La_Paz;America/Lower_Princes;America/Manaus;America/Marigot;America/Martinique;America/Montserrat;America/Port_of_Spain;America/Porto_Velho;America/Puerto_Rico;America/Santo_Domingo;America/St_Barthelemy;America/St_Kitts;America/St_Lucia;America/St_Thomas;America/St_Vincent;America/Tortola;America/Virgin;Brazil/West;Etc/GMT+4\n"
            + "SE Asia Standard Time|Antarctica/Davis;Asia/Bangkok;Asia/Ho_Chi_Minh;Asia/Jakarta;Asia/Phnom_Penh;Asia/Pontianak;Asia/Saigon;Asia/Vientiane;Etc/GMT-7;Indian/Christmas\n"
            + "Saint Pierre Standard Time|America/Miquelon\n"
            + "Sakhalin Standard Time|Asia/Sakhalin\n"
            + "Samoa Standard Time|Pacific/Apia\n"
            + "Sao Tome Standard Time|Africa/Sao_Tome\n"
            + "Saratov Standard Time|Europe/Saratov\n"
            + "Singapore Standard Time|Asia/Brunei;Asia/Kuala_Lumpur;Asia/Kuching;Asia/Makassar;Asia/Manila;Asia/Singapore;Asia/Ujung_Pandang;Etc/GMT-8;Singapore\n"
            + "South Africa Standard Time|Africa/Blantyre;Africa/Bujumbura;Africa/Gaborone;Africa/Harare;Africa/Johannesburg;Africa/Kigali;Africa/Lubumbashi;Africa/Lusaka;Africa/Maputo;Africa/Maseru;Africa/Mbabane;Etc/GMT-2\n"
            + "South Sudan Standard Time|Africa/Juba\n"
            + "Sri Lanka Standard Time|Asia/Colombo\n"
            + "Sudan Standard Time|Africa/Khartoum\n"
            + "Syria Standard Time|Asia/Damascus\n"
            + "Taipei Standard Time|Asia/Taipei;ROC\n"
            + "Tasmania Standard Time|Antarctica/Macquarie;Australia/Currie;Australia/Hobart;Australia/Tasmania\n"
            + "Tocantins Standard Time|America/Araguaina\n"
            + "Tokyo Standard Time|Asia/Dili;Asia/Jayapura;Asia/Tokyo;Etc/GMT-9;Japan;Pacific/Palau\n"
            + "Tomsk Standard Time|Asia/Tomsk\n"
            + "Tonga Standard Time|Pacific/Tongatapu\n"
            + "Transbaikal Standard Time|Asia/Chita\n"
            + "Turkey Standard Time|Asia/Istanbul;Europe/Istanbul;Turkey\n"
            + "Turks And Caicos Standard Time|America/Grand_Turk\n"
            + "US Eastern Standard Time|America/Fort_Wayne;America/Indiana/Indianapolis;America/Indiana/Marengo;America/Indiana/Vevay;America/Indianapolis;US/East-Indiana\n"
            + "US Mountain Standard Time|America/Creston;America/Dawson_Creek;America/Fort_Nelson;America/Hermosillo;America/Phoenix;Etc/GMT+7;MST;US/Arizona\n"
            + "UTC|Etc/GMT;Etc/GMT+0;Etc/GMT-0;Etc/GMT0;Etc/Greenwich;Etc/UCT;Etc/UTC;Etc/Universal;Etc/Zulu;GMT;GMT+0;GMT-0;GMT0;Greenwich;UCT;UTC;Universal;Zulu\n"
            + "UTC+12|Etc/GMT-12;Kwajalein;Pacific/Funafuti;Pacific/Kwajalein;Pacific/Majuro;Pacific/Nauru;Pacific/Tarawa;Pacific/Wake;Pacific/Wallis\n"
            + "UTC+13|Etc/GMT-13;Pacific/Enderbury;Pacific/Fakaofo;Pacific/Kanton\n"
            + "UTC-02|America/Noronha;Atlantic/South_Georgia;Brazil/DeNoronha;Etc/GMT+2\n"
            + "UTC-08|Etc/GMT+8;Pacific/Pitcairn\n"
            + "UTC-09|Etc/GMT+9;Pacific/Gambier\n"
            + "UTC-11|Etc/GMT+11;Pacific/Midway;Pacific/Niue;Pacific/Pago_Pago;Pacific/Samoa;US/Samoa\n"
            + "Ulaanbaatar Standard Time|Asia/Choibalsan;Asia/Ulaanbaatar;Asia/Ulan_Bator\n"
            + "Venezuela Standard Time|America/Caracas\n"
            + "Vladivostok Standard Time|Asia/Ust-Nera;Asia/Vladivostok\n"
            + "Volgograd Standard Time|Europe/Volgograd\n"
            + "W. Australia Standard Time|Australia/Perth;Australia/West\n"
            + "W. Central Africa Standard Time|Africa/Algiers;Africa/Bangui;Africa/Brazzaville;Africa/Douala;Africa/Kinshasa;Africa/Lagos;Africa/Libreville;Africa/Luanda;Africa/Malabo;Africa/Ndjamena;Africa/Niamey;Africa/Porto-Novo;Africa/Tunis;Etc/GMT-1\n"
            + "W. Europe Standard Time|Arctic/Longyearbyen;Atlantic/Jan_Mayen;Europe/Amsterdam;Europe/Andorra;Europe/Berlin;Europe/Busingen;Europe/Gibraltar;Europe/Luxembourg;Europe/Malta;Europe/Monaco;Europe/Oslo;Europe/Rome;Europe/San_Marino;Europe/Stockholm;Europe/Vaduz;Europe/Vatican;Europe/Vienna;Europe/Zurich\n"
            + "W. Mongolia Standard Time|Asia/Hovd\n"
            + "West Asia Standard Time|Antarctica/Mawson;Asia/Almaty;Asia/Aqtau;Asia/Aqtobe;Asia/Ashgabat;Asia/Ashkhabad;Asia/Atyrau;Asia/Dushanbe;Asia/Oral;Asia/Qostanay;Asia/Samarkand;Asia/Tashkent;Etc/GMT-5;Indian/Kerguelen;Indian/Maldives\n"
            + "West Bank Standard Time|Asia/Gaza;Asia/Hebron\n"
            + "West Pacific Standard Time|Antarctica/DumontDUrville;Etc/GMT-10;Pacific/Chuuk;Pacific/Guam;Pacific/Port_Moresby;Pacific/Saipan;Pacific/Truk;Pacific/Yap\n"
            + "Yakutsk Standard Time|Asia/Khandyga;Asia/Yakutsk\n"
            + "Yukon Standard Time|America/Dawson;America/Whitehorse;Canada/Yukon";

        /// <summary>
        ///     The parsed table, keyed by IANA id and matched without regard to case.
        /// </summary>
        /// <remarks>
        ///     Built once, on first use of the class, and then only read, which is exactly the
        ///     trade <see cref="FrozenDictionary{TKey, TValue}"/> is built for: it pays for an
        ///     expensive construction with faster lookups forever after. Case is ignored because
        ///     the ids arrive as author-written text, where <c>utc</c> and <c>UTC</c> are the same
        ///     intent, and no two ids in the table differ only by case.
        /// </remarks>
        private static readonly FrozenDictionary<string, string> Map = BuildMap();

        /// <summary>
        ///     How many IANA ids the embedded table knows.
        /// </summary>
        /// <remarks>
        ///     Exposed so that a test can assert the table is still whole. A parsing mistake in a
        ///     refreshed table would otherwise leave a dictionary that is merely small rather than
        ///     obviously broken, and the shortfall would only surface as unexplained compile
        ///     errors on the zones that went missing.
        /// </remarks>
        internal static int Count => Map.Count;

        /// <summary>Every backend zone name the table can resolve to, deduplicated.</summary>
        /// <remarks>
        ///     Exposed so a run against a real server can check that the table and the server still
        ///     agree on which zones exist. The two are refreshed on different schedules, and a name
        ///     the server has never heard of is a run-time failure on somebody's report.
        /// </remarks>
        internal static IEnumerable<string> BackendNames => Map.Values.Distinct(StringComparer.Ordinal);

        /// <summary>
        ///     Looks up the Windows time-zone name that stands for an IANA time-zone id.
        /// </summary>
        /// <param name="ianaId">
        ///     The IANA id to resolve, for example <c>Africa/Nairobi</c>. Matched without regard
        ///     to case.
        /// </param>
        /// <param name="windowsName">
        ///     When this method returns <see langword="true"/>, the Windows name to write into an
        ///     <c>AT TIME ZONE</c> clause, for example <c>E. Africa Standard Time</c>; otherwise
        ///     <see langword="null"/>.
        /// </param>
        /// <returns>
        ///     <see langword="true"/> when the id appears in the embedded table; otherwise
        ///     <see langword="false"/>, which the caller should report as an unknown zone rather
        ///     than fall back on, since guessing a zone would silently shift every timestamp the
        ///     query returns.
        /// </returns>
        /// <exception cref="ArgumentNullException"><paramref name="ianaId"/> is <see langword="null"/>.</exception>
        internal static bool TryResolve(string ianaId, [MaybeNullWhen(false)] out string windowsName)
        {
            ArgumentNullException.ThrowIfNull(ianaId);

            return Map.TryGetValue(ianaId, out windowsName);
        }

        /// <summary>
        ///     Parses <see cref="Data"/> into the lookup the class serves.
        /// </summary>
        /// <returns>Every IANA id in the embedded table, mapped to its Windows zone name.</returns>
        private static FrozenDictionary<string, string> BuildMap()
        {
            Dictionary<string, string> map = new(StringComparer.OrdinalIgnoreCase);

            ReadOnlySpan<char> data = Data;
            foreach (Range recordRange in data.Split('\n'))
            {
                // A record is "<windows name>|<iana id>;<iana id>;...". The Windows name is
                // materialised once and then shared by every id on the record, so the table costs
                // one string per Windows zone rather than one per id.
                ReadOnlySpan<char> record = data[recordRange];
                int bar = record.IndexOf('|');
                string windowsName = new(record[..bar]);
                ReadOnlySpan<char> ids = record[(bar + 1)..];

                foreach (Range idRange in ids.Split(';'))
                {
                    // The table is deduplicated where it is generated, so no id appears twice and
                    // the indexer never overwrites a mapping that disagrees with the new one.
                    string ianaId = new(ids[idRange]);
                    map[ianaId] = windowsName;
                }
            }

            return map.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        }
    }
}
