// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Hosting;
using Tellma.Core.AspNetCore.Observability;
using Tellma.Core.AspNetCore.Tests.Infrastructure;

namespace Tellma.Core.AspNetCore.Tests.Observability
{
    /// <summary>
    ///     The relay's rules and writer on their own: the envelope's refusals, each item's drop
    ///     reasons, the trace id's form, the properties an item keeps, the backstop masks, the error
    ///     kinds the error counter takes held to the client's own list, and an item's record written
    ///     under its trace with the caller's activity restored.
    /// </summary>
    public sealed partial class ClientTelemetryRulesTests
    {
        /// <summary>A batch's time.</summary>
        private static readonly DateTimeOffset SentAt = new(2026, 10, 20, 11, 3, 12, 512, TimeSpan.Zero);

        [Fact]
        public void A_sound_envelope_is_accepted_and_its_tenant_id_may_be_null()
        {
            Assert.Null(ClientTelemetryRules.Refusal(Batch(), "1.0.0"));
            Assert.Null(ClientTelemetryRules.Refusal(Batch() with { TenantId = null }, "1.0.0"));
            Assert.Null(ClientTelemetryRules.Refusal(Batch() with { Items = [] }, "1.0.0"));
        }

        [Theory]
        [InlineData("version")]
        [InlineData("no-version")]
        [InlineData("tab-short")]
        [InlineData("tab-long")]
        [InlineData("tab-chars")]
        [InlineData("tab-newline")]
        [InlineData("no-tab")]
        [InlineData("tenant-zero")]
        [InlineData("no-time")]
        [InlineData("no-items")]
        [InlineData("too-many")]
        public void A_broken_envelope_is_refused_whole(string broken)
        {
            ClientTelemetryBatch batch = broken switch
            {
                "version" => Batch() with { Version = "1.0.1" },
                "no-version" => Batch() with { Version = null! },
                "tab-short" => Batch() with { TabId = "k3J7q2M" },
                "tab-long" => Batch() with { TabId = new string('a', 41) },
                "tab-chars" => Batch() with { TabId = "k3J7q2M9.A" },
                "tab-newline" => Batch() with { TabId = "k3J7q2M9xA\n" },
                "no-tab" => Batch() with { TabId = null! },
                "tenant-zero" => Batch() with { TenantId = 0 },
                "no-time" => Batch() with { SentAt = default },
                "no-items" => Batch() with { Items = null! },
                _ => Batch() with { Items = [.. Enumerable.Repeat(Item(), ClientTelemetryRules.MaxItems + 1)] },
            };

            Assert.NotNull(ClientTelemetryRules.Refusal(batch, "1.0.0"));
        }

        [Fact]
        public void A_batch_of_exactly_the_most_items_is_accepted()
        {
            Assert.Null(ClientTelemetryRules.Refusal(Batch() with { Items = [.. Enumerable.Repeat(Item(), ClientTelemetryRules.MaxItems)] }, "1.0.0"));
        }

        [Fact]
        public void A_batch_without_the_header_s_version_is_refused()
        {
            Assert.NotNull(ClientTelemetryRules.Refusal(Batch(), null));
        }

        [Theory]
        [InlineData("event", "tm.page.visit", "None")]
        [InlineData("timing", "tm.page.visit", "None")]
        [InlineData("error", "tm.error.captured", "None")]
        [InlineData("event", "acme.credit-exposure.run", "None")]
        [InlineData("event", "tm.a.b.c.d.e", "None")]
        [InlineData("metric", "tm.page.visit", "Kind")]
        [InlineData("Event", "tm.page.visit", "Kind")]
        [InlineData(null, "tm.page.visit", "Kind")]
        [InlineData("event", "tm.page", "Name")]
        [InlineData("event", "tm.a.b.c.d.e.f", "Name")]
        [InlineData("event", "Tm.page.visit", "Name")]
        [InlineData("event", "tm.page.-visit", "Name")]
        [InlineData("event", "tm.page.visit\n", "Name")]
        [InlineData("event", null, "Name")]
        [InlineData("error", "tm.error.other", "Name")]
        [InlineData("error", null, "Name")]
        public void An_item_is_dropped_for_its_kind_or_its_name(string? kind, string? name, string expected)
        {
            Assert.Equal(Enum.Parse<ClientItemDrop>(expected), ClientTelemetryRules.Check(Item() with { Kind = kind!, Name = name! }, SentAt));
        }

        [Fact]
        public void A_name_past_64_characters_is_dropped_though_it_follows_the_grammar()
        {
            string name = "tm.page." + new string('a', 56);
            Assert.Equal(64, name.Length);
            Assert.Equal(ClientItemDrop.None, ClientTelemetryRules.Check(Item() with { Name = name }, SentAt));
            Assert.Equal(ClientItemDrop.Name, ClientTelemetryRules.Check(Item() with { Name = name + "a" }, SentAt));
        }

        [Fact]
        public void An_item_s_time_lies_between_a_day_before_and_a_minute_after_the_batch_s()
        {
            Assert.Equal(ClientItemDrop.None, ClientTelemetryRules.Check(Item() with { At = SentAt - TimeSpan.FromHours(24) }, SentAt));
            Assert.Equal(ClientItemDrop.None, ClientTelemetryRules.Check(Item() with { At = SentAt + TimeSpan.FromMinutes(1) }, SentAt));
            Assert.Equal(ClientItemDrop.Time, ClientTelemetryRules.Check(Item() with { At = SentAt - TimeSpan.FromHours(24) - TimeSpan.FromTicks(1) }, SentAt));
            Assert.Equal(ClientItemDrop.Time, ClientTelemetryRules.Check(Item() with { At = SentAt + TimeSpan.FromMinutes(1) + TimeSpan.FromTicks(1) }, SentAt));

            // A missing time, and the extremes of the calendar, are dropped without overflowing.
            Assert.Equal(ClientItemDrop.Time, ClientTelemetryRules.Check(Item() with { At = default }, SentAt));
            Assert.Equal(ClientItemDrop.Time, ClientTelemetryRules.Check(Item() with { At = DateTimeOffset.MaxValue }, SentAt));
            Assert.Equal(ClientItemDrop.Time, ClientTelemetryRules.Check(Item() with { At = DateTimeOffset.MaxValue }, DateTimeOffset.MinValue));
        }

        [Fact]
        public void A_null_in_the_list_is_dropped_as_a_kind()
        {
            Assert.Equal(ClientItemDrop.Kind, ClientTelemetryRules.Check(null, SentAt));
        }

        [Theory]
        [InlineData("TmApiError", "TmApiError")]
        [InlineData("/:t/centers/:id", "/:t/centers/:id")]
        [InlineData("a+b@c.d-e_f", "a+b@c.d-e_f")]
        [InlineData("", "(none)")]
        [InlineData("Type Error", "(other)")]
        [InlineData("X\r\nforged", "(other)")]
        [InlineData("X\u001b[31m", "(other)")]
        [InlineData("X\t", "(other)")]
        [InlineData("caf\u00e9", "(other)")]
        public void A_message_names_a_value_only_within_the_value_grammar(string value, string expected)
        {
            Assert.Equal(expected, ClientTelemetryRules.MessageValue(value));
        }

        [Fact]
        public void A_message_names_a_missing_long_or_non_text_value_by_its_placeholder()
        {
            Assert.Equal("(none)", ClientTelemetryRules.MessageValue(null));
            Assert.Equal(new string('a', 64), ClientTelemetryRules.MessageValue(new string('a', 64)));
            Assert.Equal("(other)", ClientTelemetryRules.MessageValue(new string('a', 65)));
            Assert.Equal("(other)", ClientTelemetryRules.MessageValue(7L));
            Assert.Equal("(other)", ClientTelemetryRules.MessageValue(true));
        }

        [Theory]
        [InlineData("0af7651916cd43dd8448eb211c80319c", true)]
        [InlineData("0AF7651916CD43DD8448EB211C80319C", false)]
        [InlineData("00000000000000000000000000000000", false)]
        [InlineData("0af7651916cd43dd8448eb211c80319", false)]
        [InlineData("0af7651916cd43dd8448eb211c80319cd", false)]
        [InlineData("0af7651916cd43dd8448eb211c80319g", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void A_trace_id_is_32_lowercase_hex_digits_not_all_zeros(string? traceId, bool expected)
        {
            Assert.Equal(expected, ClientTelemetryRules.IsTraceId(traceId));
            if (expected)
            {
                // Every id the rule admits is one the activity's parent accepts.
                Assert.Equal(traceId, ActivityTraceId.CreateFromString(traceId).ToHexString());
            }
        }

        [Fact]
        public void An_item_keeps_its_numbers_booleans_and_short_texts_and_drops_the_rest()
        {
            Dictionary<string, object> kept = new(StringComparer.Ordinal);
            int dropped = ClientTelemetryRules.KeepProperties(
                Properties(/*lang=json*/ """
                    {
                      "route": "/:t/centers", "ms": 412, "ratio": 0.5, "big": 1e300, "huge": 1e400, "ready": true, "stale": false,
                      "Upper": 1, "has-dash": 1, "k1234567890123456789012345678901": 1, "k12345678901234567890123456789012": 1,
                      "nested": { "a": 1 }, "list": [1], "none": null,
                      "text": "TEXT128", "long": "TEXT129", "message": "TEXT256", "stack": "TEXT4096"
                    }
                    """),
                kept);

            Assert.Equal(412L, kept["ms"]);
            Assert.Equal(0.5, kept["ratio"]);
            Assert.Equal(1e300, kept["big"]);
            Assert.True(Assert.IsType<bool>(kept["ready"]));
            Assert.False(Assert.IsType<bool>(kept["stale"]));
            Assert.Equal("/:t/centers", kept["route"]);
            Assert.Equal(1L, kept["k1234567890123456789012345678901"]);
            Assert.Equal(128, Assert.IsType<string>(kept["text"]).Length);
            Assert.Equal(256, Assert.IsType<string>(kept["message"]).Length);
            Assert.Equal(4096, Assert.IsType<string>(kept["stack"]).Length);

            // A number past a double, a key that is not camelCase or is too long, anything nested, a
            // null and a text too long for its key: each dropped and counted.
            Assert.Equal(8, dropped);
            Assert.DoesNotContain("huge", kept.Keys);
            Assert.DoesNotContain("long", kept.Keys);
        }

        [Fact]
        public void A_message_or_a_stack_too_long_for_its_key_is_dropped()
        {
            Dictionary<string, object> kept = new(StringComparer.Ordinal);
            Assert.Equal(2, ClientTelemetryRules.KeepProperties(Properties(/*lang=json*/ """{ "message": "TEXT257", "stack": "TEXT4097" }"""), kept));
            Assert.Empty(kept);
        }

        [Fact]
        public void An_item_keeps_its_first_16_properties()
        {
            Dictionary<string, object> kept = new(StringComparer.Ordinal);
            string json = "{" + string.Join(", ", Enumerable.Range(1, 20).Select(static index => $"\"p{index}\": {index}")) + "}";

            Assert.Equal(4, ClientTelemetryRules.KeepProperties(Properties(json), kept));
            Assert.Equal([.. Enumerable.Range(1, 16).Select(static index => "p" + index)], kept.Keys);
        }

        [Fact]
        public void A_bad_property_does_not_take_one_of_the_16_places()
        {
            Dictionary<string, object> kept = new(StringComparer.Ordinal);
            string json = """{ "bad": null, """ + string.Join(", ", Enumerable.Range(1, 16).Select(static index => $"\"p{index}\": {index}")) + "}";

            Assert.Equal(1, ClientTelemetryRules.KeepProperties(Properties(json), kept));
            Assert.Equal(16, kept.Count);
        }

        [Fact]
        public void A_message_and_a_stack_are_masked_and_other_texts_are_not()
        {
            Dictionary<string, object> kept = new(StringComparer.Ordinal);
            _ = ClientTelemetryRules.KeepProperties(
                Properties(/*lang=json*/ """{ "message": "Failed to load https://acme.app.tellma.com/17/centers", "stack": "at farah@acme.com", "code": "www.acme.com" }"""),
                kept);

            Assert.Equal("Failed to load <url>", kept["message"]);
            Assert.Equal("at <email>", kept["stack"]);
            Assert.Equal("www.acme.com", kept["code"]);
        }

        [Theory]
        [InlineData("Failed to load https://acme.app.tellma.com/17/centers?q=jed", "Failed to load <url>")]
        [InlineData("see www.acme.com/help now", "see <url> now")]
        [InlineData("GET /17/api/web/centers?search=jed failed", "GET <url> failed")]
        [InlineData("/17/centers#top", "<url>")]
        [InlineData("chunk-K3J7Q2M9.js:1:2345", "chunk-K3J7Q2M9.js:1:2345")]
        [InlineData("a/b?c stays: its slash follows a letter", "a/b?c stays: its slash follows a letter")]
        [InlineData("x.y/b?c stays too", "x.y/b?c stays too")]
        [InlineData("Write to farah.nasser@acme.com today", "Write to <email> today")]
        [InlineData("(farah@acme.com)", "(<email>)")]
        [InlineData("\u201Cfarah@acme.com\u201D", "\u201C<email>\u201D")]
        [InlineData("farah@localhost stays", "farah@localhost stays")]
        [InlineData("mailto:farah@acme.com", "<email>")]
        [InlineData("Cannot read properties of undefined (reading 'Name')", "Cannot read properties of undefined (reading 'Name')")]
        [InlineData("", "")]
        public void The_masks_hide_urls_and_then_email_addresses(string text, string expected)
        {
            Assert.True(ClientTelemetryMasks.TryMask(text, out string? masked));
            Assert.Equal(expected, masked);
        }

        [Fact]
        public void The_masks_run_without_backtracking_and_leave_a_hostile_text_as_it_is()
        {
            // The engine that never backtracks costs linear time, so no text of a few kilobytes runs out the timeout.
            Assert.True(ClientTelemetryMasks.Urls().Options.HasFlag(RegexOptions.NonBacktracking));
            Assert.True(ClientTelemetryMasks.Emails().Options.HasFlag(RegexOptions.NonBacktracking));

            // Texts that make a backtracking engine retry from every position: no scheme, no at sign.
            string[] hostile =
            [
                new string('a', ClientTelemetryRules.MaxStackLength),
                string.Concat(Enumerable.Repeat("a.", ClientTelemetryRules.MaxStackLength / 2)),
                string.Concat(Enumerable.Repeat("a@", ClientTelemetryRules.MaxStackLength / 2)),
            ];
            foreach (string text in hostile)
            {
                Assert.True(ClientTelemetryMasks.TryMask(text, out string? masked));
                Assert.Equal(text, masked);
            }
        }

        [Fact]
        public void The_error_kinds_are_the_client_s_api_error_kinds()
        {
            // The client's list, linked beside the suite, is the source of the kinds.
            string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Observability", "tm-api-error.ts"));
            Match union = ApiErrorKindUnion().Match(source);
            Assert.True(union.Success, "The client's TmApiErrorKind union was not found.");
            string[] kinds = [.. QuotedKind().Matches(union.Groups["members"].Value).Select(static match => match.Groups["kind"].Value)];

            Assert.Equal(kinds.Order(StringComparer.Ordinal), ClientTelemetryMetrics.ErrorKinds.Order(StringComparer.Ordinal));
        }

        [Fact]
        public void An_item_s_record_is_written_under_its_trace_and_the_caller_s_activity_restored()
        {
            CapturingLoggerProvider capture = new();
            using ILoggerFactory loggers = LoggerFactory.Create(builder => builder.AddProvider(capture));
            using ServiceProvider services = new ServiceCollection().AddMetrics().BuildServiceProvider();
            ClientTelemetryWriter writer = new(
                loggers,
                new ClientTelemetryMetrics(services.GetRequiredService<IMeterFactory>()),
                new DeploymentVersions("1.0.0", "1.0.0"),
                new DeploymentIdentity("acme", "Test"));
            const string TraceId = "0af7651916cd43dd8448eb211c80319c";

            // The item with a trace id is written under it, the one without under the caller's.
            using Activity caller = new Activity("caller").Start();
            writer.Write(Batch() with { Items = [Item() with { TraceId = TraceId }, Item()] }, new ClientTelemetryCaller("sub-1", null), SentAt);

            Assert.Same(caller, Activity.Current);
            Assert.Equal([TraceId, caller.TraceId.ToHexString()], capture.Events.Select(static captured => captured.TraceId));
        }

        /// <summary>A sound batch of one item.</summary>
        /// <returns>The batch.</returns>
        private static ClientTelemetryBatch Batch()
        {
            return new ClientTelemetryBatch("1.0.0", "k3J7q2M9xA", 17, SentAt, [Item()]);
        }

        /// <summary>A sound item.</summary>
        /// <returns>The item.</returns>
        private static ClientTelemetryItem Item()
        {
            return new ClientTelemetryItem("event", "tm.page.visit", SentAt - TimeSpan.FromSeconds(30), null, null);
        }

        /// <summary>
        ///     Properties parsed from JSON, with <c>TEXT</c> followed by a length standing for a text of
        ///     that many letters.
        /// </summary>
        /// <param name="json">The properties' JSON object.</param>
        /// <returns>The properties.</returns>
        private static Dictionary<string, JsonElement> Properties(string json)
        {
            string expanded = TextPlaceholder().Replace(json, static match => new string('x', int.Parse(match.Groups["length"].Value, CultureInfo.InvariantCulture)));
            return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(expanded, TellmaJson.Options)!;
        }

        /// <summary>The client's <c>TmApiErrorKind</c> union, its members captured.</summary>
        /// <returns>The pattern.</returns>
        [GeneratedRegex(@"export type TmApiErrorKind\s*=(?<members>[^;]*);", RegexOptions.CultureInvariant, 1000)]
        private static partial Regex ApiErrorKindUnion();

        /// <summary>One quoted member of a string union.</summary>
        /// <returns>The pattern.</returns>
        [GeneratedRegex(@"'(?<kind>[^']+)'", RegexOptions.CultureInvariant, 1000)]
        private static partial Regex QuotedKind();

        /// <summary>A text placeholder: <c>TEXT</c> and a length.</summary>
        /// <returns>The pattern.</returns>
        [GeneratedRegex(@"TEXT(?<length>[0-9]+)", RegexOptions.CultureInvariant, 1000)]
        private static partial Regex TextPlaceholder();
    }
}
