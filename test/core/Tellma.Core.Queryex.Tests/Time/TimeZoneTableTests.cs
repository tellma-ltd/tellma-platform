// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Reflection;
using Tellma.Queryex.Testing.Probe;
using Tellma.Queryex.Testing.Schema;

namespace Tellma.Core.Queryex.Tests.Time
{
    /// <summary>
    ///     The embedded zone table: current ids only, the aliases browsers report, and nothing else.
    /// </summary>
    /// <remarks>
    ///     A host builds its list of zones from the table, so a wrong entry here is a wrong choice
    ///     offered to every user. The list of current ids is checked against a copy of the time-zone
    ///     database's own country table, kept beside this file and refreshed with the engine's.
    /// </remarks>
    public sealed class TimeZoneTableTests
    {
        /// <summary>The engine under test.</summary>
        private static readonly QueryexEngine Engine = new();

        /// <summary>The checked-in location ids of the time-zone database.</summary>
        private static readonly HashSet<string> LocationIds = ReadLocationIds();

        /// <summary>Every current id is a location of the time-zone database, or is UTC.</summary>
        [Fact]
        public void EveryCurrentId_IsALocationOrUtc()
        {
            string[] strays = [.. QueryexTimeZones.Ids.Where(id => id != "UTC" && !LocationIds.Contains(id))];

            Assert.Empty(strays);
            Assert.Contains("UTC", QueryexTimeZones.Ids);
        }

        /// <summary>
        ///     Nearly every location compiles. Losing more than the odd one the backend has no zone
        ///     for means the table was refreshed from a broken source.
        /// </summary>
        [Fact]
        public void NearlyEveryLocation_IsCurrent()
        {
            string[] missing = [.. LocationIds.Where(id => !QueryexTimeZones.Ids.Contains(id))];

            Assert.True(missing.Length <= 2, "locations without a zone: " + string.Join(", ", missing));
            Assert.Equal(QueryexProbe.ZoneIdentifierCount, QueryexTimeZones.Ids.Count);
        }

        /// <summary>No fixed-offset zone is current: their signs read inverted and no location means one.</summary>
        [Fact]
        public void NoFixedOffsetZone_IsCurrent()
        {
            Assert.DoesNotContain(QueryexTimeZones.Ids, id => id.StartsWith("Etc/", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(QueryexTimeZones.Aliases.Keys, id => id.StartsWith("Etc/", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>The list is the table's own current ids, in ordinal order, each once.</summary>
        [Fact]
        public void TheIds_AreTheTablesCurrentIdsInOrdinalOrder()
        {
            Assert.Equal(
                [.. QueryexProbe.CurrentZones.Keys.Order(StringComparer.Ordinal)],
                QueryexTimeZones.Ids);
            Assert.Equal(QueryexTimeZones.Ids.Count, QueryexTimeZones.Ids.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        }

        /// <summary>The aliases are exactly the nineteen ids browsers report for renamed zones.</summary>
        [Fact]
        public void TheAliases_AreTheOnesBrowsersReport()
        {
            Dictionary<string, string> expected = new(StringComparer.Ordinal)
            {
                ["Africa/Asmera"] = "Africa/Asmara",
                ["America/Buenos_Aires"] = "America/Argentina/Buenos_Aires",
                ["America/Catamarca"] = "America/Argentina/Catamarca",
                ["America/Coral_Harbour"] = "America/Atikokan",
                ["America/Cordoba"] = "America/Argentina/Cordoba",
                ["America/Godthab"] = "America/Nuuk",
                ["America/Indianapolis"] = "America/Indiana/Indianapolis",
                ["America/Jujuy"] = "America/Argentina/Jujuy",
                ["America/Louisville"] = "America/Kentucky/Louisville",
                ["America/Mendoza"] = "America/Argentina/Mendoza",
                ["Asia/Calcutta"] = "Asia/Kolkata",
                ["Asia/Katmandu"] = "Asia/Kathmandu",
                ["Asia/Rangoon"] = "Asia/Yangon",
                ["Asia/Saigon"] = "Asia/Ho_Chi_Minh",
                ["Atlantic/Faeroe"] = "Atlantic/Faroe",
                ["Europe/Kiev"] = "Europe/Kyiv",
                ["Pacific/Enderbury"] = "Pacific/Kanton",
                ["Pacific/Ponape"] = "Pacific/Pohnpei",
                ["Pacific/Truk"] = "Pacific/Chuuk",
            };

            Assert.Equal(
                expected.OrderBy(pair => pair.Key, StringComparer.Ordinal),
                QueryexTimeZones.Aliases.OrderBy(pair => pair.Key, StringComparer.Ordinal));
        }

        /// <summary>Every alias stands for a current id, and no id is both.</summary>
        [Fact]
        public void EveryAlias_StandsForACurrentId()
        {
            foreach ((string alias, string current) in QueryexTimeZones.Aliases)
            {
                Assert.Contains(current, QueryexTimeZones.Ids);
                Assert.DoesNotContain(alias, QueryexTimeZones.Ids, StringComparer.OrdinalIgnoreCase);
                Assert.DoesNotContain(alias, LocationIds);
            }
        }

        /// <summary>An alias resolves to its current id's backend zone, ignoring case.</summary>
        [Fact]
        public void AnAlias_ResolvesThroughItsCurrentId()
        {
            foreach ((string alias, string current) in QueryexTimeZones.Aliases)
            {
                string? expected = QueryexProbe.ResolveZone(current);
                Assert.NotNull(expected);
                Assert.Equal(expected, QueryexProbe.ResolveZone(alias));
                Assert.Equal(expected, QueryexProbe.ResolveZone(alias.ToUpperInvariant()));
            }

            Assert.True(QueryexTimeZones.Aliases.ContainsKey("asia/calcutta"));
            Assert.Equal("India Standard Time", QueryexProbe.ResolveZone("Asia/Calcutta"));
        }

        /// <summary>A country's own id stays current even where the database links it to a merged zone.</summary>
        /// <param name="id">The country's id.</param>
        /// <param name="backendName">The backend zone it resolves to.</param>
        [Theory]
        [InlineData("Asia/Kuwait", "Arab Standard Time")]
        [InlineData("Africa/Addis_Ababa", "E. Africa Standard Time")]
        [InlineData("Asia/Kolkata", "India Standard Time")]
        [InlineData("UTC", "UTC")]
        [InlineData("utc", "UTC")]
        public void ACurrentId_Resolves(string id, string backendName)
        {
            Assert.Equal(backendName, QueryexProbe.ResolveZone(id));
        }

        /// <summary>
        ///     An id that is neither current nor an alias is refused as a value the position does not
        ///     accept, whatever it once meant.
        /// </summary>
        /// <param name="id">The id.</param>
        [Theory]
        [InlineData("US/Alaska")]
        [InlineData("Etc/GMT-4")]
        [InlineData("Etc/GMT+3")]
        [InlineData("Australia/ACT")]
        [InlineData("GB")]
        [InlineData("Etc/Zulu")]
        [InlineData("Etc/UTC")]
        public void ARetiredId_IsRefused(string id)
        {
            Assert.Null(QueryexProbe.ResolveZone(id));

            QueryexResult<ValidatedExpression> result = Engine.Validate(
                "local(PostedAt, '" + id + "')",
                new ValidationOptions
                {
                    QueryexVersion = QueryexLanguage.Version,
                    Schema = LedgerFixture.Schema,
                    Root = LedgerFixture.Invoice,
                    Mode = QueryexMode.Value,
                });

            Assert.False(result.Succeeded);
            QueryexDiagnostic problem = Assert.Single(result.Diagnostics);
            Assert.Equal("QX3101", problem.Code);
            Assert.Equal(new QueryexSpan(16, id.Length + 2), problem.Span);
        }

        /// <summary>An alias compiles to the backend zone of the id it stands for.</summary>
        [Fact]
        public void AnAlias_CompilesToItsCurrentZone()
        {
            QueryexResult<CompiledQuery> result = Engine.CompileQuery(
                new QuerySpec { Root = LedgerFixture.Invoice, Select = "local(PostedAt, 'Asia/Calcutta')" },
                new QueryCompilationOptions { QueryexVersion = QueryexLanguage.Version, Schema = LedgerFixture.Schema });

            Assert.True(result.Succeeded);
            Assert.Equal("India Standard Time", Assert.Single(result.Value.Parameters).Value);
        }

        /// <summary>Reads the checked-in list of location ids.</summary>
        /// <returns>The ids.</returns>
        private static HashSet<string> ReadLocationIds()
        {
            using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("zone-tab-ids.txt")
                ?? throw new InvalidOperationException("The checked-in list of location ids is not embedded.");
            using StreamReader reader = new(stream);

            HashSet<string> ids = new(StringComparer.Ordinal);
            while (reader.ReadLine() is string line)
            {
                if (line.Length > 0 && !line.StartsWith('#'))
                {
                    ids.Add(line);
                }
            }

            return ids;
        }
    }
}
