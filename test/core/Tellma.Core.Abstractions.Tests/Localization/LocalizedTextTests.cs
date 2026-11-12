// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text.Json;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Entities;
using Tellma.Core.Abstractions.Localization;

namespace Tellma.Core.Abstractions.Tests.Localization
{
    /// <summary>
    ///     A text in the three content languages: its JSON shape under any options, its slot fallback,
    ///     and the copy of an entity's multilingual group.
    /// </summary>
    public class LocalizedTextTests
    {
        // --- The JSON shape ---

        [Fact]
        public void It_is_written_as_an_array_of_three_under_the_platforms_options_and_the_defaults()
        {
            LocalizedText text = new("Acme", "أكمي");

            Assert.Equal("""["Acme","أكمي",null]""", JsonSerializer.Serialize(text, TellmaJson.Options));

            // The default options escape non-ASCII text; the shape is the same.
            string escaped = JsonSerializer.Serialize(text);
            Assert.StartsWith("""["Acme",""", escaped, StringComparison.Ordinal);
            Assert.EndsWith(""",null]""", escaped, StringComparison.Ordinal);
            Assert.Equal(text, JsonSerializer.Deserialize<LocalizedText>(escaped));
        }

        [Fact]
        public void It_round_trips_as_an_argument_among_others()
        {
            Dictionary<string, object> arguments = new() { ["title"] = new LocalizedText("Q3 report", null, "Rapport T3"), ["count"] = 3 };
            string json = JsonSerializer.Serialize(arguments, TellmaJson.Options);

            Assert.Equal(/*lang=json,strict*/ """{"title":["Q3 report",null,"Rapport T3"],"count":3}""", json);
            Assert.Equal(
                new LocalizedText("Q3 report", null, "Rapport T3"),
                JsonSerializer.Deserialize<LocalizedText>(JsonDocument.Parse(json).RootElement.GetProperty("title").GetRawText(), TellmaJson.Options));
        }

        [Fact]
        public void It_reads_three_strings()
        {
            Assert.Equal(new LocalizedText("a", "b", "c"), JsonSerializer.Deserialize<LocalizedText>("""["a","b","c"]"""));
        }

        [Theory]
        [InlineData(/*lang=json,strict*/ """{"text":"a"}""")]
        [InlineData("""["a"]""")]
        [InlineData("""["a",null]""")]
        [InlineData("""["a",null,null,null]""")]
        [InlineData("""[null,"b","c"]""")]
        [InlineData("""[1,null,null]""")]
        [InlineData("""["a",2,null]""")]
        [InlineData("""["a",null,["c"]]""")]
        [InlineData("\"a\"")]
        public void Any_other_shape_is_refused(string json)
        {
            JsonException refused = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<LocalizedText>(json, TellmaJson.Options));
            Assert.StartsWith("A localized text is a JSON array of three elements", refused.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void A_null_first_slot_is_refused_when_built()
        {
            Assert.Throws<ArgumentNullException>(static () => new LocalizedText(null!));
        }

        // --- The slots ---

        [Theory]
        [InlineData(1, "Acme")]
        [InlineData(2, "أكمي")]
        [InlineData(3, "Acme")]
        public void A_slot_reads_its_text_else_the_first(int slot, string expected)
        {
            Assert.Equal(expected, new LocalizedText("Acme", "أكمي", null).In(slot));
        }

        [Fact]
        public void An_empty_slot_falls_back_to_the_first()
        {
            Assert.Equal("Acme", new LocalizedText("Acme", string.Empty, string.Empty).In(2));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(4)]
        [InlineData(-1)]
        public void A_slot_outside_one_to_three_is_refused(int slot)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new LocalizedText("Acme").In(slot));
        }

        // --- The copy of an entity's multilingual group ---

        [Fact]
        public void Names_copies_the_name_group_and_another_group_by_name()
        {
            Labelled entity = new() { Name = "Acme", Name2 = "أكمي", Name3 = null, Title = "Mr", Title2 = null, Title3 = "M." };

            Assert.Equal(new LocalizedText("Acme", "أكمي", null), LocalizedText.Names(entity));
            Assert.Equal(new LocalizedText("Mr", null, "M."), LocalizedText.Names(entity, nameof(Labelled.Title)));
        }

        [Fact]
        public void Names_reads_an_inherited_anchor_and_a_snapshot_of_its_values()
        {
            DerivedLabelled entity = new() { Name = "First" };
            var copy = LocalizedText.Names(entity);
            entity.Name = "Renamed";

            Assert.Equal("First", copy.Text);
            Assert.Equal("Renamed", LocalizedText.Names(entity).Text);
        }

        [Fact]
        public void Names_refuses_a_property_that_is_not_multilingual()
        {
            ArgumentException refused = Assert.Throws<ArgumentException>(static () => LocalizedText.Names(new Labelled { Name = "a" }, nameof(Labelled.Code)));
            Assert.Contains("is not declared [Multilingual]", refused.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void Names_refuses_a_missing_property_or_a_missing_twin()
        {
            Assert.Throws<ArgumentException>(static () => LocalizedText.Names(new Labelled { Name = "a" }, "Missing"));
            Assert.Throws<ArgumentException>(static () => LocalizedText.Names(new Twinless { Name = "a" }));
        }

        [Fact]
        public void Names_refuses_an_anchor_without_text()
        {
            ArgumentException refused = Assert.Throws<ArgumentException>(static () => LocalizedText.Names(new Labelled()));
            Assert.Contains("holds no text", refused.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void Names_refuses_null_arguments()
        {
            Assert.Throws<ArgumentNullException>(static () => LocalizedText.Names(null!));
            Assert.Throws<ArgumentNullException>(static () => LocalizedText.Names(new Labelled(), null!));
        }

        /// <summary>A record with two multilingual groups and a plain string.</summary>
        public class Labelled
        {
            /// <summary>The name.</summary>
            [Multilingual]
            public virtual string? Name { get; set; }

            /// <summary>The name in the second content language.</summary>
            public string? Name2 { get; set; }

            /// <summary>The name in the third content language.</summary>
            public string? Name3 { get; set; }

            /// <summary>The title.</summary>
            [Multilingual]
            public string? Title { get; set; }

            /// <summary>The title in the second content language.</summary>
            public string? Title2 { get; set; }

            /// <summary>The title in the third content language.</summary>
            public string? Title3 { get; set; }

            /// <summary>A plain code.</summary>
            public string? Code { get; set; }
        }

        /// <summary>A record whose name overrides the multilingual anchor of its base.</summary>
        public sealed class DerivedLabelled : Labelled
        {
            /// <summary>The name, overridden without restating the attribute.</summary>
            public override string? Name { get; set; }
        }

        /// <summary>A record whose multilingual name lacks its twins.</summary>
        public sealed class Twinless
        {
            /// <summary>The name.</summary>
            [Multilingual]
            public string? Name { get; set; }
        }
    }
}
