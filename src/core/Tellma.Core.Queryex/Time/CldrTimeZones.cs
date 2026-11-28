// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Frozen;
using System.Collections.Immutable;
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
    ///         The table is embedded in this assembly rather than read from the machine. Which zone
    ///         ids compile is part of the language surface, so the answer has to be identical on a
    ///         developer's laptop, on a build agent, and in production. Asking
    ///         <see cref="TimeZoneInfo"/> instead would delegate that decision to whatever
    ///         time-zone database the host happens to carry, and an expression that compiled in one
    ///         place would be rejected in another.
    ///     </para>
    ///     <para>
    ///         It is two maps. <see cref="Zones"/> holds every current id — one the time-zone
    ///         database lists as a location in its country table, or <c>UTC</c> — with the Windows
    ///         name it resolves to. <see cref="Aliases"/> holds the older ids a supported browser
    ///         still reports for a zone that was renamed, each pointing at the current id it
    ///         stands for. Every other id is unknown: the renamed and deprecated spellings no
    ///         browser reports, and the fixed-offset <c>Etc/GMT±n</c> zones, whose signs read
    ///         inverted and which no current id means.
    ///     </para>
    ///     <para>
    ///         The application's list of zones is built from <see cref="Zones"/> alone, so each
    ///         location is offered once, under its current id; an alias exists only so that the id
    ///         a device reports still finds the zone it means.
    ///     </para>
    ///     <para>
    ///         Resolution is deliberately one way. Windows zones are coarser than IANA zones, so
    ///         many ids share one Windows name and there is no reverse lookup worth offering.
    ///     </para>
    /// </remarks>
    internal static class CldrTimeZones
    {
        /// <summary>
        ///     The current ids in their embedded form: one record per Windows zone, records
        ///     separated by line feeds, each record the Windows name, a vertical bar, then the
        ///     current IANA ids that resolve to it separated by semicolons.
        /// </summary>
        /// <remarks>
        ///     Held as one constant rather than as hundreds of separate entries so that the
        ///     assembly carries a single string and the source stays reviewable: one line per
        ///     Windows zone, ordered ordinally by that name, with each line's ids ordered ordinally
        ///     too, so refreshing the table from newer releases produces a diff a reader can
        ///     actually check. Neither kind of name can contain a vertical bar or a semicolon, so
        ///     the format needs no escaping.
        /// </remarks>
        private const string ZoneData =
            "AUS Central Standard Time|Australia/Darwin\n"
            + "AUS Eastern Standard Time|Australia/Melbourne;Australia/Sydney\n"
            + "Afghanistan Standard Time|Asia/Kabul\n"
            + "Alaskan Standard Time|America/Anchorage;America/Juneau;America/Metlakatla;America/Nome;America/Sitka;America/Yakutat\n"
            + "Aleutian Standard Time|America/Adak\n"
            + "Altai Standard Time|Asia/Barnaul\n"
            + "Arab Standard Time|Asia/Aden;Asia/Bahrain;Asia/Kuwait;Asia/Qatar;Asia/Riyadh\n"
            + "Arabian Standard Time|Asia/Dubai;Asia/Muscat\n"
            + "Arabic Standard Time|Asia/Baghdad\n"
            + "Argentina Standard Time|America/Argentina/Buenos_Aires;America/Argentina/Catamarca;America/Argentina/Cordoba;America/Argentina/Jujuy;America/Argentina/La_Rioja;America/Argentina/Mendoza;America/Argentina/Rio_Gallegos;America/Argentina/Salta;America/Argentina/San_Juan;America/Argentina/San_Luis;America/Argentina/Tucuman;America/Argentina/Ushuaia\n"
            + "Astrakhan Standard Time|Europe/Astrakhan;Europe/Ulyanovsk\n"
            + "Atlantic Standard Time|America/Glace_Bay;America/Goose_Bay;America/Halifax;America/Moncton;America/Thule;Atlantic/Bermuda\n"
            + "Aus Central W. Standard Time|Australia/Eucla\n"
            + "Azerbaijan Standard Time|Asia/Baku\n"
            + "Azores Standard Time|America/Scoresbysund;Atlantic/Azores\n"
            + "Bahia Standard Time|America/Bahia\n"
            + "Bangladesh Standard Time|Asia/Dhaka;Asia/Thimphu\n"
            + "Belarus Standard Time|Europe/Minsk\n"
            + "Bougainville Standard Time|Pacific/Bougainville\n"
            + "Canada Central Standard Time|America/Regina;America/Swift_Current\n"
            + "Cape Verde Standard Time|Atlantic/Cape_Verde\n"
            + "Caucasus Standard Time|Asia/Yerevan\n"
            + "Cen. Australia Standard Time|Australia/Adelaide;Australia/Broken_Hill\n"
            + "Central America Standard Time|America/Belize;America/Costa_Rica;America/El_Salvador;America/Guatemala;America/Managua;America/Tegucigalpa;Pacific/Galapagos\n"
            + "Central Asia Standard Time|Antarctica/Vostok;Asia/Bishkek;Asia/Urumqi;Indian/Chagos\n"
            + "Central Brazilian Standard Time|America/Campo_Grande;America/Cuiaba\n"
            + "Central Europe Standard Time|Europe/Belgrade;Europe/Bratislava;Europe/Budapest;Europe/Ljubljana;Europe/Podgorica;Europe/Prague;Europe/Tirane\n"
            + "Central European Standard Time|Europe/Sarajevo;Europe/Skopje;Europe/Warsaw;Europe/Zagreb\n"
            + "Central Pacific Standard Time|Antarctica/Casey;Pacific/Efate;Pacific/Guadalcanal;Pacific/Kosrae;Pacific/Noumea;Pacific/Pohnpei\n"
            + "Central Standard Time|America/Chicago;America/Indiana/Knox;America/Indiana/Tell_City;America/Matamoros;America/Menominee;America/North_Dakota/Beulah;America/North_Dakota/Center;America/North_Dakota/New_Salem;America/Ojinaga;America/Rankin_Inlet;America/Resolute;America/Winnipeg\n"
            + "Central Standard Time (Mexico)|America/Bahia_Banderas;America/Chihuahua;America/Merida;America/Mexico_City;America/Monterrey\n"
            + "Chatham Islands Standard Time|Pacific/Chatham\n"
            + "China Standard Time|Asia/Hong_Kong;Asia/Macau;Asia/Shanghai\n"
            + "Cuba Standard Time|America/Havana\n"
            + "E. Africa Standard Time|Africa/Addis_Ababa;Africa/Asmara;Africa/Dar_es_Salaam;Africa/Djibouti;Africa/Kampala;Africa/Mogadishu;Africa/Nairobi;Antarctica/Syowa;Indian/Antananarivo;Indian/Comoro;Indian/Mayotte\n"
            + "E. Australia Standard Time|Australia/Brisbane;Australia/Lindeman\n"
            + "E. Europe Standard Time|Europe/Chisinau\n"
            + "E. South America Standard Time|America/Sao_Paulo\n"
            + "Easter Island Standard Time|Pacific/Easter\n"
            + "Eastern Standard Time|America/Detroit;America/Indiana/Petersburg;America/Indiana/Vincennes;America/Indiana/Winamac;America/Iqaluit;America/Kentucky/Louisville;America/Kentucky/Monticello;America/Nassau;America/New_York;America/Toronto\n"
            + "Eastern Standard Time (Mexico)|America/Cancun\n"
            + "Egypt Standard Time|Africa/Cairo\n"
            + "Ekaterinburg Standard Time|Asia/Yekaterinburg\n"
            + "FLE Standard Time|Europe/Helsinki;Europe/Kyiv;Europe/Mariehamn;Europe/Riga;Europe/Sofia;Europe/Tallinn;Europe/Vilnius\n"
            + "Fiji Standard Time|Pacific/Fiji\n"
            + "GMT Standard Time|Atlantic/Canary;Atlantic/Faroe;Atlantic/Madeira;Europe/Dublin;Europe/Guernsey;Europe/Isle_of_Man;Europe/Jersey;Europe/Lisbon;Europe/London\n"
            + "GTB Standard Time|Asia/Famagusta;Asia/Nicosia;Europe/Athens;Europe/Bucharest\n"
            + "Georgian Standard Time|Asia/Tbilisi\n"
            + "Greenland Standard Time|America/Nuuk\n"
            + "Greenwich Standard Time|Africa/Abidjan;Africa/Accra;Africa/Bamako;Africa/Banjul;Africa/Bissau;Africa/Conakry;Africa/Dakar;Africa/Freetown;Africa/Lome;Africa/Monrovia;Africa/Nouakchott;Africa/Ouagadougou;America/Danmarkshavn;Atlantic/Reykjavik;Atlantic/St_Helena\n"
            + "Haiti Standard Time|America/Port-au-Prince\n"
            + "Hawaiian Standard Time|Pacific/Honolulu;Pacific/Rarotonga;Pacific/Tahiti\n"
            + "India Standard Time|Asia/Kolkata\n"
            + "Iran Standard Time|Asia/Tehran\n"
            + "Israel Standard Time|Asia/Jerusalem\n"
            + "Jordan Standard Time|Asia/Amman\n"
            + "Kaliningrad Standard Time|Europe/Kaliningrad\n"
            + "Korea Standard Time|Asia/Seoul\n"
            + "Libya Standard Time|Africa/Tripoli\n"
            + "Line Islands Standard Time|Pacific/Kiritimati\n"
            + "Lord Howe Standard Time|Australia/Lord_Howe\n"
            + "Magadan Standard Time|Asia/Magadan\n"
            + "Magallanes Standard Time|America/Coyhaique;America/Punta_Arenas\n"
            + "Marquesas Standard Time|Pacific/Marquesas\n"
            + "Mauritius Standard Time|Indian/Mahe;Indian/Mauritius;Indian/Reunion\n"
            + "Middle East Standard Time|Asia/Beirut\n"
            + "Montevideo Standard Time|America/Montevideo\n"
            + "Morocco Standard Time|Africa/Casablanca;Africa/El_Aaiun\n"
            + "Mountain Standard Time|America/Boise;America/Cambridge_Bay;America/Ciudad_Juarez;America/Denver;America/Edmonton;America/Inuvik\n"
            + "Mountain Standard Time (Mexico)|America/Mazatlan\n"
            + "Myanmar Standard Time|Asia/Yangon;Indian/Cocos\n"
            + "N. Central Asia Standard Time|Asia/Novosibirsk\n"
            + "Namibia Standard Time|Africa/Windhoek\n"
            + "Nepal Standard Time|Asia/Kathmandu\n"
            + "New Zealand Standard Time|Antarctica/McMurdo;Pacific/Auckland\n"
            + "Newfoundland Standard Time|America/St_Johns\n"
            + "Norfolk Standard Time|Pacific/Norfolk\n"
            + "North Asia East Standard Time|Asia/Irkutsk\n"
            + "North Asia Standard Time|Asia/Krasnoyarsk;Asia/Novokuznetsk\n"
            + "North Korea Standard Time|Asia/Pyongyang\n"
            + "Omsk Standard Time|Asia/Omsk\n"
            + "Pacific SA Standard Time|America/Santiago\n"
            + "Pacific Standard Time|America/Los_Angeles;America/Vancouver\n"
            + "Pacific Standard Time (Mexico)|America/Tijuana\n"
            + "Pakistan Standard Time|Asia/Karachi\n"
            + "Paraguay Standard Time|America/Asuncion\n"
            + "Qyzylorda Standard Time|Asia/Qyzylorda\n"
            + "Romance Standard Time|Africa/Ceuta;Europe/Brussels;Europe/Copenhagen;Europe/Madrid;Europe/Paris\n"
            + "Russia Time Zone 10|Asia/Srednekolymsk\n"
            + "Russia Time Zone 11|Asia/Anadyr;Asia/Kamchatka\n"
            + "Russia Time Zone 3|Europe/Samara\n"
            + "Russian Standard Time|Europe/Kirov;Europe/Moscow;Europe/Simferopol\n"
            + "SA Eastern Standard Time|America/Belem;America/Cayenne;America/Fortaleza;America/Maceio;America/Paramaribo;America/Recife;America/Santarem;Antarctica/Palmer;Antarctica/Rothera;Atlantic/Stanley\n"
            + "SA Pacific Standard Time|America/Atikokan;America/Bogota;America/Cayman;America/Eirunepe;America/Guayaquil;America/Jamaica;America/Lima;America/Panama;America/Rio_Branco\n"
            + "SA Western Standard Time|America/Anguilla;America/Antigua;America/Aruba;America/Barbados;America/Blanc-Sablon;America/Boa_Vista;America/Curacao;America/Dominica;America/Grenada;America/Guadeloupe;America/Guyana;America/Kralendijk;America/La_Paz;America/Lower_Princes;America/Manaus;America/Marigot;America/Martinique;America/Montserrat;America/Port_of_Spain;America/Porto_Velho;America/Puerto_Rico;America/Santo_Domingo;America/St_Barthelemy;America/St_Kitts;America/St_Lucia;America/St_Thomas;America/St_Vincent;America/Tortola\n"
            + "SE Asia Standard Time|Antarctica/Davis;Asia/Bangkok;Asia/Ho_Chi_Minh;Asia/Jakarta;Asia/Phnom_Penh;Asia/Pontianak;Asia/Vientiane;Indian/Christmas\n"
            + "Saint Pierre Standard Time|America/Miquelon\n"
            + "Sakhalin Standard Time|Asia/Sakhalin\n"
            + "Samoa Standard Time|Pacific/Apia\n"
            + "Sao Tome Standard Time|Africa/Sao_Tome\n"
            + "Saratov Standard Time|Europe/Saratov\n"
            + "Singapore Standard Time|Asia/Brunei;Asia/Kuala_Lumpur;Asia/Kuching;Asia/Makassar;Asia/Manila;Asia/Singapore\n"
            + "South Africa Standard Time|Africa/Blantyre;Africa/Bujumbura;Africa/Gaborone;Africa/Harare;Africa/Johannesburg;Africa/Kigali;Africa/Lubumbashi;Africa/Lusaka;Africa/Maputo;Africa/Maseru;Africa/Mbabane\n"
            + "South Sudan Standard Time|Africa/Juba\n"
            + "Sri Lanka Standard Time|Asia/Colombo\n"
            + "Sudan Standard Time|Africa/Khartoum\n"
            + "Syria Standard Time|Asia/Damascus\n"
            + "Taipei Standard Time|Asia/Taipei\n"
            + "Tasmania Standard Time|Antarctica/Macquarie;Australia/Hobart\n"
            + "Tocantins Standard Time|America/Araguaina\n"
            + "Tokyo Standard Time|Asia/Dili;Asia/Jayapura;Asia/Tokyo;Pacific/Palau\n"
            + "Tomsk Standard Time|Asia/Tomsk\n"
            + "Tonga Standard Time|Pacific/Tongatapu\n"
            + "Transbaikal Standard Time|Asia/Chita\n"
            + "Turkey Standard Time|Europe/Istanbul\n"
            + "Turks And Caicos Standard Time|America/Grand_Turk\n"
            + "US Eastern Standard Time|America/Indiana/Indianapolis;America/Indiana/Marengo;America/Indiana/Vevay\n"
            + "US Mountain Standard Time|America/Creston;America/Dawson_Creek;America/Fort_Nelson;America/Hermosillo;America/Phoenix\n"
            + "UTC|UTC\n"
            + "UTC+12|Pacific/Funafuti;Pacific/Kwajalein;Pacific/Majuro;Pacific/Nauru;Pacific/Tarawa;Pacific/Wake;Pacific/Wallis\n"
            + "UTC+13|Pacific/Fakaofo;Pacific/Kanton\n"
            + "UTC-02|America/Noronha;Atlantic/South_Georgia\n"
            + "UTC-08|Pacific/Pitcairn\n"
            + "UTC-09|Pacific/Gambier\n"
            + "UTC-11|Pacific/Midway;Pacific/Niue;Pacific/Pago_Pago\n"
            + "Ulaanbaatar Standard Time|Asia/Ulaanbaatar\n"
            + "Venezuela Standard Time|America/Caracas\n"
            + "Vladivostok Standard Time|Asia/Ust-Nera;Asia/Vladivostok\n"
            + "Volgograd Standard Time|Europe/Volgograd\n"
            + "W. Australia Standard Time|Australia/Perth\n"
            + "W. Central Africa Standard Time|Africa/Algiers;Africa/Bangui;Africa/Brazzaville;Africa/Douala;Africa/Kinshasa;Africa/Lagos;Africa/Libreville;Africa/Luanda;Africa/Malabo;Africa/Ndjamena;Africa/Niamey;Africa/Porto-Novo;Africa/Tunis\n"
            + "W. Europe Standard Time|Arctic/Longyearbyen;Europe/Amsterdam;Europe/Andorra;Europe/Berlin;Europe/Busingen;Europe/Gibraltar;Europe/Luxembourg;Europe/Malta;Europe/Monaco;Europe/Oslo;Europe/Rome;Europe/San_Marino;Europe/Stockholm;Europe/Vaduz;Europe/Vatican;Europe/Vienna;Europe/Zurich\n"
            + "W. Mongolia Standard Time|Asia/Hovd\n"
            + "West Asia Standard Time|Antarctica/Mawson;Asia/Almaty;Asia/Aqtau;Asia/Aqtobe;Asia/Ashgabat;Asia/Atyrau;Asia/Dushanbe;Asia/Oral;Asia/Qostanay;Asia/Samarkand;Asia/Tashkent;Indian/Kerguelen;Indian/Maldives\n"
            + "West Bank Standard Time|Asia/Gaza;Asia/Hebron\n"
            + "West Pacific Standard Time|Antarctica/DumontDUrville;Pacific/Chuuk;Pacific/Guam;Pacific/Port_Moresby;Pacific/Saipan\n"
            + "Yakutsk Standard Time|Asia/Khandyga;Asia/Yakutsk\n"
            + "Yukon Standard Time|America/Dawson;America/Whitehorse";

        /// <summary>
        ///     The aliases in their embedded form: one record per alias, records separated by line
        ///     feeds, each record the alias, a vertical bar, then the current id it stands for.
        /// </summary>
        /// <remarks>
        ///     The ids a supported browser engine reports in place of a current one. The CLDR keeps
        ///     its own id for zones the time-zone database renamed, and Chromium reports the CLDR's,
        ///     so a device in India reports <c>Asia/Calcutta</c> where the current id is
        ///     <c>Asia/Kolkata</c>. Ordered ordinally by alias.
        /// </remarks>
        private const string AliasData =
            "Africa/Asmera|Africa/Asmara\n"
            + "America/Buenos_Aires|America/Argentina/Buenos_Aires\n"
            + "America/Catamarca|America/Argentina/Catamarca\n"
            + "America/Coral_Harbour|America/Atikokan\n"
            + "America/Cordoba|America/Argentina/Cordoba\n"
            + "America/Godthab|America/Nuuk\n"
            + "America/Indianapolis|America/Indiana/Indianapolis\n"
            + "America/Jujuy|America/Argentina/Jujuy\n"
            + "America/Louisville|America/Kentucky/Louisville\n"
            + "America/Mendoza|America/Argentina/Mendoza\n"
            + "Asia/Calcutta|Asia/Kolkata\n"
            + "Asia/Katmandu|Asia/Kathmandu\n"
            + "Asia/Rangoon|Asia/Yangon\n"
            + "Asia/Saigon|Asia/Ho_Chi_Minh\n"
            + "Atlantic/Faeroe|Atlantic/Faroe\n"
            + "Europe/Kiev|Europe/Kyiv\n"
            + "Pacific/Enderbury|Pacific/Kanton\n"
            + "Pacific/Ponape|Pacific/Pohnpei\n"
            + "Pacific/Truk|Pacific/Chuuk";

        /// <summary>
        ///     Every current id, mapped to its Windows zone name and matched without regard to case.
        /// </summary>
        /// <remarks>
        ///     Built once, on first use of the class, and then only read, which is exactly the
        ///     trade <see cref="FrozenDictionary{TKey, TValue}"/> is built for: it pays for an
        ///     expensive construction with faster lookups forever after. Case is ignored because
        ///     the ids arrive as author-written text, where <c>utc</c> and <c>UTC</c> are the same
        ///     intent, and no two ids in the table differ only by case.
        /// </remarks>
        internal static FrozenDictionary<string, string> Zones { get; } = ParseZones();

        /// <summary>
        ///     Every alias, mapped to the current id it stands for and matched without regard to
        ///     case.
        /// </summary>
        internal static FrozenDictionary<string, string> Aliases { get; } = ParseAliases();

        /// <summary>The current ids, in ordinal order.</summary>
        internal static ImmutableArray<string> Ids { get; } = [.. Zones.Keys.Order(StringComparer.Ordinal)];

        /// <summary>
        ///     How many current ids the embedded table knows.
        /// </summary>
        /// <remarks>
        ///     Exposed so that a test can assert the table is still whole. A parsing mistake in a
        ///     refreshed table would otherwise leave a dictionary that is merely small rather than
        ///     obviously broken, and the shortfall would only surface as unexplained compile
        ///     errors on the zones that went missing.
        /// </remarks>
        internal static int Count => Zones.Count;

        /// <summary>Every backend zone name the table can resolve to, deduplicated.</summary>
        /// <remarks>
        ///     Exposed so a run against a real server can check that the table and the server still
        ///     agree on which zones exist. The two are refreshed on different schedules, and a name
        ///     the server has never heard of is a run-time failure on somebody's report.
        /// </remarks>
        internal static IEnumerable<string> BackendNames => Zones.Values.Distinct(StringComparer.Ordinal);

        /// <summary>
        ///     Looks up the Windows time-zone name that stands for an IANA time-zone id.
        /// </summary>
        /// <param name="ianaId">
        ///     The IANA id to resolve, for example <c>Africa/Nairobi</c>: a current id, or an alias
        ///     of one. Matched without regard to case.
        /// </param>
        /// <param name="windowsName">
        ///     When this method returns <see langword="true"/>, the Windows name to write into an
        ///     <c>AT TIME ZONE</c> clause, for example <c>E. Africa Standard Time</c>; otherwise
        ///     <see langword="null"/>.
        /// </param>
        /// <returns>
        ///     <see langword="true"/> when the id is current or an alias; otherwise
        ///     <see langword="false"/>, which the caller should report as an unknown zone rather
        ///     than fall back on, since guessing a zone would silently shift every timestamp the
        ///     query returns.
        /// </returns>
        /// <exception cref="ArgumentNullException"><paramref name="ianaId"/> is <see langword="null"/>.</exception>
        internal static bool TryResolve(string ianaId, [MaybeNullWhen(false)] out string windowsName)
        {
            ArgumentNullException.ThrowIfNull(ianaId);

            // An alias resolves through the current id it stands for, so the Windows name lives in
            // one place and an alias can never drift from the zone it means.
            string current = Aliases.GetValueOrDefault(ianaId) ?? ianaId;
            return Zones.TryGetValue(current, out windowsName);
        }

        /// <summary>
        ///     Parses <see cref="ZoneData"/> into the map of current ids.
        /// </summary>
        /// <returns>Every current id, mapped to its Windows zone name.</returns>
        private static FrozenDictionary<string, string> ParseZones()
        {
            Dictionary<string, string> map = new(StringComparer.OrdinalIgnoreCase);

            ReadOnlySpan<char> data = ZoneData;
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
                    // Added rather than assigned: an id listed under two Windows names is a mistake
                    // in a refreshed table, and failing on first use is louder than keeping
                    // whichever line came last.
                    map.Add(new string(ids[idRange]), windowsName);
                }
            }

            return map.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        ///     Parses <see cref="AliasData"/> into the map of aliases.
        /// </summary>
        /// <returns>Every alias, mapped to the current id it stands for.</returns>
        private static FrozenDictionary<string, string> ParseAliases()
        {
            Dictionary<string, string> map = new(StringComparer.OrdinalIgnoreCase);

            ReadOnlySpan<char> data = AliasData;
            foreach (Range recordRange in data.Split('\n'))
            {
                // A record is "<alias>|<current id>".
                ReadOnlySpan<char> record = data[recordRange];
                int bar = record.IndexOf('|');
                map.Add(new string(record[..bar]), new string(record[(bar + 1)..]));
            }

            return map.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        }
    }
}
