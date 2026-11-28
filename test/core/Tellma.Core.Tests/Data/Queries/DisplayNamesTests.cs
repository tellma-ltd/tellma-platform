// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Tellma.Core.Abstractions.Calendars;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Settings;
using Tellma.Core.Data.Queries;
using Tellma.Core.Tests.Data.Batches;

namespace Tellma.Core.Tests.Data.Queries
{
    /// <summary>
    ///     The server's display names against the golden file the client reads too, and the entity
    ///     renderer over a composed model.
    /// </summary>
    public sealed class DisplayNamesTests : IDisposable
    {
        /// <summary>The golden file's reading options: camel-case members, enums by name.</summary>
        private static readonly JsonSerializerOptions GoldenJson = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

        /// <summary>The golden file, read once.</summary>
        private static readonly Lazy<Golden> File = new(Load);

        /// <summary>The composition over the query model.</summary>
        private readonly BatchHarness _harness = new(compose: QueryModel.Compose);

        /// <summary>The names of the golden's render rows the server renders: every row but the client's own.</summary>
        public static TheoryData<string> RenderRows { get; } = [.. File.Value.Render.Where(static row => !row.ClientOnly).Select(static row => row.Name)];

        /// <summary>The names of the golden's expansion rows.</summary>
        public static TheoryData<string> ExpansionRows { get; } = [.. File.Value.Expansion.Select(static row => row.Name)];

        /// <summary>The display names service.</summary>
        private IDisplayNames DisplayNames => _harness.Services.GetRequiredService<IDisplayNames>();

        [Theory]
        [MemberData(nameof(RenderRows))]
        public void A_golden_row_renders_its_expected_text(string name)
        {
            RenderRow row = File.Value.Render.Single(candidate => candidate.Name == name);
            DisplayNameRule rule = row.Rule.Create();

            // The server never formats native digits, so the Latin numbering is the culture's default.
            var culture = CultureInfo.GetCultureInfo(row.Locale.Replace("-u-nu-latn", string.Empty, StringComparison.Ordinal));
            ICalendarSystem calendar = _harness.Services.GetRequiredService<ICalendarRegistry>()[row.Calendar];
            IReadOnlyList<object?> values = [.. rule.Columns(row.Shape, null).Select(column => row.Rule.Value(column, row.Row))];

            Assert.Equal(row.Expected, rule.Render(values, row.Slot, culture, calendar));
        }

        [Theory]
        [MemberData(nameof(ExpansionRows))]
        public void A_golden_expansion_row_selects_its_columns(string name)
        {
            ExpansionRow row = File.Value.Expansion.Single(candidate => candidate.Name == name);

            Assert.Equal(row.Columns, row.Rule.Create().Columns(row.Shape, row.Navigation));
        }

        [Fact]
        public void The_golden_holds_client_only_rows_the_server_skips()
        {
            Assert.Contains(File.Value.Render, static row => row.ClientOnly);
        }

        [Fact]
        public void An_entity_renders_by_its_metadata_rule_under_the_shape_its_values_carry()
        {
            Assert.Equal(["Id", "Name", "Code"], DisplayNames.Columns("qx.Gadget", MultilingualShape.Primary));
            Assert.Equal(["Zone.Id", "Zone.Name", "Zone.Name2"], DisplayNames.Columns("qx.Zone", MultilingualShape.PrimaryAndSecondary, "Zone"));

            ICalendarSystem calendar = _harness.Services.GetRequiredService<ICalendarRegistry>()["gc"];
            var english = CultureInfo.GetCultureInfo("en");
            Assert.Equal("1101 - Cash", DisplayNames.Render("qx.Gadget", [7, "Cash", "1101"], 1, english, calendar));
            Assert.Equal("1101 - النقد", DisplayNames.Render("qx.Gadget", [7, "Cash", "النقد", "1101"], 2, english, calendar));
            Assert.Equal("#7", DisplayNames.Render("qx.Zone", [7, null], 1, english, calendar));
        }

        [Fact]
        public void An_unknown_entity_values_of_no_shape_and_a_slot_out_of_range_are_refused()
        {
            ICalendarSystem calendar = _harness.Services.GetRequiredService<ICalendarRegistry>()["gc"];
            var english = CultureInfo.GetCultureInfo("en");

            _ = Assert.Throws<ArgumentException>(() => DisplayNames.Columns("qx.Nope", MultilingualShape.Primary));
            _ = Assert.Throws<ArgumentException>(() => DisplayNames.Render("qx.Gadget", [7], 1, english, calendar));
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => DisplayNames.Render("qx.Gadget", [7, "Cash", "1101"], 0, english, calendar));
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => DisplayNames.Columns("qx.Gadget", 0));
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _harness.Dispose();
        }

        /// <summary>Reads the golden file copied beside the suite.</summary>
        /// <returns>The golden.</returns>
        private static Golden Load()
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Data", "Queries", "display-name-golden.json");
            return JsonSerializer.Deserialize<Golden>(System.IO.File.ReadAllText(path), GoldenJson)
                ?? throw new InvalidOperationException("The display-name golden is empty.");
        }

        /// <summary>The golden file.</summary>
        /// <param name="Render">The render rows.</param>
        /// <param name="Expansion">The expansion rows.</param>
        private sealed record Golden(IReadOnlyList<RenderRow> Render, IReadOnlyList<ExpansionRow> Expansion);

        /// <summary>A display-name rule, by property name.</summary>
        /// <param name="Key">The key property.</param>
        /// <param name="Twins">A group source's twins, absent ones null; null for a scalar source.</param>
        /// <param name="Source">A scalar source.</param>
        /// <param name="SourceType">The scalar source's type: <c>integer</c>, <c>date</c> or text when absent.</param>
        /// <param name="Prefix">The prefix property, or null.</param>
        private sealed record GoldenRule(string Key, IReadOnlyList<string?>? Twins, string? Source, string? SourceType, string? Prefix)
        {
            /// <summary>The server's rule.</summary>
            /// <returns>The rule.</returns>
            public DisplayNameRule Create()
            {
                return new DisplayNameRule(Key, Twins, Source, Prefix);
            }

            /// <summary>A column's value in a wire row, typed as the rule types its properties.</summary>
            /// <param name="column">The column.</param>
            /// <param name="row">The wire row, by camel-case member.</param>
            /// <returns>The value, or null when the row has none.</returns>
            public object? Value(string column, IReadOnlyDictionary<string, JsonElement> row)
            {
                JsonElement? cell = row.FirstOrDefault(entry => string.Equals(entry.Key, column, StringComparison.OrdinalIgnoreCase)).Value;
                if (cell is not { ValueKind: not JsonValueKind.Null and not JsonValueKind.Undefined } value)
                {
                    return null;
                }

                string? type = column == Key ? "integer" : column == Source ? SourceType : null;
                return type switch
                {
                    "integer" => value.GetInt32(),
                    "date" => DateOnly.Parse(value.GetString()!, CultureInfo.InvariantCulture),
                    _ => value.GetString(),
                };
            }
        }

        /// <summary>A render row: a rule, a wire row, the active slot, a locale and a calendar, and the text.</summary>
        /// <param name="Name">What the row shows.</param>
        /// <param name="Rule">The rule.</param>
        /// <param name="Shape">The shape the row was selected under.</param>
        /// <param name="Row">The wire row.</param>
        /// <param name="Slot">The active content slot.</param>
        /// <param name="Locale">The locale.</param>
        /// <param name="Calendar">The calendar's code.</param>
        /// <param name="Expected">The text.</param>
        /// <param name="ClientOnly">Whether only the client renders the row (native digits).</param>
        private sealed record RenderRow(
            string Name,
            GoldenRule Rule,
            MultilingualShape Shape,
            IReadOnlyDictionary<string, JsonElement> Row,
            int Slot,
            string Locale,
            string Calendar,
            string Expected,
            bool ClientOnly);

        /// <summary>An expansion row: a rule, a shape and a navigation, and the columns selected.</summary>
        /// <param name="Name">What the row shows.</param>
        /// <param name="Rule">The rule.</param>
        /// <param name="Shape">The shape.</param>
        /// <param name="Navigation">The navigation, or null for the root.</param>
        /// <param name="Columns">The column paths.</param>
        private sealed record ExpansionRow(string Name, GoldenRule Rule, MultilingualShape Shape, string? Navigation, IReadOnlyList<string> Columns);
    }
}
