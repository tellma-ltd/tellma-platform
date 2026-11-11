// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using System.Collections;
using System.Diagnostics.Metrics;
using System.Globalization;
using Tellma.Core.Abstractions.Calendars;
using Tellma.Core.Abstractions.Localization;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Calendars;
using Tellma.Core.Localization;
using Tellma.Core.Resources;
using Tellma.Core.Tests.Infrastructure;

namespace Tellma.Core.Tests.Localization
{
    /// <summary>
    ///     The ICU string localizer: plural and select in each language's own rules, named
    ///     arguments, dates and times through the flow's calendar system and zone, the resource
    ///     fallback chain, missing keys and malformed messages, and the composition that installs it.
    /// </summary>
    /// <remarks>
    ///     The messages are the test assembly's own (<see cref="TestMessages" />), addressed the way a
    ///     feature addresses its resources. The distribution offers English, Arabic and Amharic; each
    ///     test fixes its culture with a <see cref="LocalizationScope" />, so no test touches the
    ///     thread's cultures except the ones about the flow outside every scope, which run on a flow
    ///     of their own.
    /// </remarks>
    public sealed class IcuStringLocalizerTests : IDisposable
    {
        private static readonly GregorianCalendarSystem Gregorian = new();

        /// <summary>A fixed zone three hours ahead of UTC, the same on every host.</summary>
        private static readonly TimeZoneInfo PlusThree = TimeZoneInfo.CreateCustomTimeZone("Test+03", TimeSpan.FromHours(3), "Test+03", "Test+03");

        private readonly ServiceProvider _metrics = new ServiceCollection().AddMetrics().BuildServiceProvider();
        private readonly FakeLoggerProvider _logs = new();
        private readonly LoggerFactory _loggers;
        private readonly MetricCollector<long> _missing;
        private readonly IcuStringLocalizerFactory _factory;
        private readonly IStringLocalizer _messages;

        public IcuStringLocalizerTests()
        {
            _loggers = new LoggerFactory([_logs]);

            // The real meter factory, because the collector matches on the meter's scope.
            IMeterFactory meters = _metrics.GetRequiredService<IMeterFactory>();
            _missing = new MetricCollector<long>(meters, LocalizationTelemetryNames.MeterName, LocalizationTelemetryNames.Missing);
            _factory = new IcuStringLocalizerFactory(new LanguageCatalog(new LanguageDeclaration(["en-GB", "ar-SA", "am-ET"], [])), _loggers, meters);
            _messages = _factory.Create(typeof(TestMessages));
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _missing.Dispose();
            _loggers.Dispose();
            _logs.Dispose();
            _metrics.Dispose();
        }

        // --- Plural and select ---

        [Theory]
        [InlineData(0, "لا عناصر")]
        [InlineData(1, "عنصر واحد")]
        [InlineData(2, "عنصران")]
        [InlineData(3, "3 عناصر")]
        [InlineData(10, "10 عناصر")]
        [InlineData(11, "11 عنصرًا")]
        [InlineData(99, "99 عنصرًا")]
        [InlineData(100, "100 عنصر")]
        [InlineData(102, "102 عنصر")]
        [InlineData(103, "103 عناصر")]
        [InlineData(111, "111 عنصرًا")]
        public void Arabic_counts_take_each_of_the_six_plural_categories(int count, string expected)
        {
            using IDisposable scope = Scope("ar", "SA");

            Assert.Equal(expected, _messages["Items", new { count }].Value);
        }

        [Theory]
        [InlineData(0, "0 ንጥል")]
        [InlineData(1, "1 ንጥል")]
        [InlineData(2, "2 ንጥሎች")]
        [InlineData(11, "11 ንጥሎች")]
        public void Amharic_counts_zero_and_one_alike(int count, string expected)
        {
            using IDisposable scope = Scope("am", "ET");

            Assert.Equal(expected, _messages["Items", new { count }].Value);
        }

        [Theory]
        [InlineData(0, "0 items")]
        [InlineData(1, "1 item")]
        [InlineData(2, "2 items")]
        public void English_counts_one_and_other(int count, string expected)
        {
            using IDisposable scope = Scope("en", "GB");

            Assert.Equal(expected, _messages["Items", new { count }].Value);
        }

        [Theory]
        [InlineData("en", "GB", "female", "She invited you")]
        [InlineData("en", "GB", "male", "He invited you")]
        [InlineData("en", "GB", "other", "They invited you")]
        [InlineData("en", "GB", "unspecified", "They invited you")]
        [InlineData("ar", "SA", "female", "دعتك")]
        [InlineData("ar", "SA", "male", "دعاك")]
        [InlineData("ar", "SA", "nonbinary", "دعوك")]
        public void Gender_selects_its_branch_and_anything_else_takes_other(string language, string region, string gender, string expected)
        {
            using IDisposable scope = Scope(language, region);

            Assert.Equal(expected, _messages["Invitation", new { gender }].Value);
        }

        [Fact]
        public void A_number_is_grouped_in_the_culture()
        {
            using (Scope("en", "GB"))
            {
                Assert.Equal("Total: 1,234,567.5", _messages["Total", new { amount = 1_234_567.5m }].Value);
            }

            using (Scope("de", "DE"))
            {
                Assert.Equal("Total: 1.234.567,5", _messages["Total", new { amount = 1_234_567.5m }].Value);
            }
        }

        [Fact]
        public void A_typographic_apostrophe_is_plain_text()
        {
            using IDisposable scope = Scope("en", "GB");

            Assert.Equal("It’s Sara’s turn", _messages["Possessive", new { name = "Sara" }].Value);
        }

        [Fact]
        public void A_value_without_placeholders_renders_as_it_is_with_or_without_arguments()
        {
            using IDisposable scope = Scope("ar", "SA");

            Assert.Equal("مرحبًا", _messages["Greeting"].Value);
            Assert.Equal("مرحبًا", _messages["Greeting", new { unused = 1 }].Value);
            Assert.Empty(Warnings());
        }

        // --- Named arguments ---

        [Fact]
        public void Arguments_are_read_from_any_dictionary_keyed_by_name_or_from_an_objects_properties()
        {
            using IDisposable scope = Scope("en", "GB");

            Assert.Equal("3 items", _messages["Items", new Dictionary<string, object?> { ["count"] = 3 }].Value);
            Assert.Equal("3 items", _messages["Items", new Dictionary<string, int> { ["count"] = 3 }].Value);
            Assert.Equal("3 items", _messages["Items", new Hashtable { ["count"] = 3 }].Value);
            Assert.Equal("3 items", _messages["Items", new { count = 3 }].Value);
        }

        [Fact]
        public void A_second_argument_object_is_positional_and_refused()
        {
            using IDisposable scope = Scope("en", "GB");

            ArgumentException refused = Assert.Throws<ArgumentException>(() => _messages["Items", new { count = 3 }, new { count = 4 }]);
            Assert.Equal("arguments", refused.ParamName);

            // Refused before the lookup, so a missing key fails the same way.
            _ = Assert.Throws<ArgumentException>(() => _messages["Nope", 1, 2]);
        }

        [Fact]
        public void A_lone_value_names_no_argument_and_is_refused()
        {
            using IDisposable scope = Scope("en", "GB");

            _ = Assert.Throws<ArgumentException>(() => _messages["Template", "Sara"]);
            _ = Assert.Throws<ArgumentException>(() => _messages["Items", 3]);
            _ = Assert.Throws<ArgumentException>(() => _messages["Date", new DateOnly(2026, 3, 2)]);
            _ = Assert.Throws<ArgumentException>(() => _messages["Items", new List<int> { 3 }]);
            _ = Assert.Throws<ArgumentException>(() => _messages["Items", new Hashtable { [1] = 3 }]);
        }

        [Fact]
        public void A_template_read_without_arguments_comes_back_raw_and_unlogged()
        {
            using IDisposable scope = Scope("en", "GB");

            Assert.Equal("Hello {name}", _messages["Template"].Value);
            Assert.Equal("Hello {name}", _messages["Template", [null!]].Value);
            Assert.Equal("Hello Sara", _messages["Template", new { name = "Sara" }].Value);
            Assert.Empty(Warnings());
        }

        // --- Malformed messages ---

        [Fact]
        public void A_malformed_message_renders_raw_and_is_logged_once_per_culture()
        {
            using (Scope("en", "GB"))
            {
                Assert.Equal("Hello {name", _messages["Malformed", new { name = "Sara" }].Value);
                Assert.Equal("Hello {name", _messages["Malformed", new { name = "Sara" }].Value);
                Assert.False(_messages["Malformed", new { name = "Sara" }].ResourceNotFound);
            }

            FakeLogRecord logged = Assert.Single(Warnings());
            Assert.Equal("MessageFormatMalformed", logged.Id.Name);
            Assert.Equal(LogLevel.Warning, logged.Level);
            Assert.NotNull(logged.Exception);
            Assert.Contains("Malformed", logged.Message, StringComparison.Ordinal);

            using (Scope("ar", "SA"))
            {
                Assert.Equal("مرحبًا {name", _messages["Malformed", new { name = "سارة" }].Value);
            }

            Assert.Equal(2, Warnings().Count(static record => record.Id.Name == "MessageFormatMalformed"));
        }

        [Fact]
        public void Arguments_a_message_cannot_format_with_render_it_raw_and_are_logged()
        {
            using IDisposable scope = Scope("en", "GB");

            // A plural over text, and arguments that lack the one the message names.
            Assert.Equal("{count, plural, one {# item} other {# items}}", _messages["Items", new { count = "many" }].Value);
            Assert.Equal("Hello {name}", _messages["Template", new { other = 1 }].Value);

            Assert.Equal(2, Warnings().Count(static record => record.Id.Name == "MessageFormatMalformed"));
        }

        // --- Dates and times ---

        [Theory]
        [InlineData("gc", "en", "GB")]
        [InlineData("gc", "ar", "SA")]
        [InlineData("uq", "en", "GB")]
        [InlineData("uq", "ar", "SA")]
        [InlineData("et", "en", "GB")]
        [InlineData("et", "am", "ET")]
        public void A_date_renders_in_the_scopes_calendar_in_every_style(string code, string language, string region)
        {
            ICalendarSystem calendar = Calendar(code);
            CultureInfo culture = NegotiatedCultures.Get(language, region);
            DateOnly date = new(2026, 3, 2);
            using IDisposable scope = LocalizationScope.Enter(new LocalizationContext(culture, calendar, PlusThree, 1));

            Assert.Equal(calendar.Format(date, culture, DateStyle.Short), _messages["Date_short", new { d = date }].Value);
            Assert.Equal(calendar.Format(date, culture, DateStyle.Medium), _messages["Date_medium", new { d = date }].Value);
            Assert.Equal(calendar.Format(date, culture, DateStyle.Long), _messages["Date_long", new { d = date }].Value);
            Assert.Equal(calendar.Format(date, culture, DateStyle.Full), _messages["Date_full", new { d = date }].Value);

            // No style is the medium style.
            Assert.Equal(calendar.Format(date, culture, DateStyle.Medium), _messages["Date", new { d = date }].Value);
        }

        [Fact]
        public void A_medium_date_reads_as_the_calendar_writes_it()
        {
            DateOnly date = new(2026, 3, 2);

            using (Scope("en", "GB"))
            {
                Assert.Equal("2 Mar 2026", _messages["Date_medium", new { d = date }].Value);
            }

            using (Scope("en", "GB", Calendar("uq")))
            {
                Assert.Equal(Calendar("uq").Format(date, NegotiatedCultures.Get("en", "GB"), DateStyle.Medium), _messages["Date_medium", new { d = date }].Value);
                Assert.NotEqual("2 Mar 2026", _messages["Date_medium", new { d = date }].Value);
            }
        }

        [Fact]
        public void An_instant_renders_as_its_date_in_the_scopes_zone_and_a_wall_clock_date_never_shifts()
        {
            CultureInfo culture = NegotiatedCultures.Get("en", "GB");
            using IDisposable scope = LocalizationScope.Enter(new LocalizationContext(culture, Gregorian, PlusThree, 1));

            // 22:30 UTC is already the next day three hours ahead.
            Assert.Equal("3 Mar 2026", _messages["Date", new { d = new DateTimeOffset(2026, 3, 2, 22, 30, 0, TimeSpan.Zero) }].Value);
            Assert.Equal("3 Mar 2026", _messages["Date", new { d = new DateTime(2026, 3, 2, 22, 30, 0, DateTimeKind.Utc) }].Value);

            // A date, and a date and time of no kind, are read on the wall clock.
            Assert.Equal("2 Mar 2026", _messages["Date", new { d = new DateOnly(2026, 3, 2) }].Value);
            Assert.Equal("2 Mar 2026", _messages["Date", new { d = new DateTime(2026, 3, 2, 23, 30, 0, DateTimeKind.Unspecified) }].Value);
        }

        [Fact]
        public void A_time_renders_in_the_cultures_pattern_and_an_instant_in_the_scopes_zone()
        {
            using (Scope("en", "GB", zone: PlusThree))
            {
                Assert.Equal("14:05", _messages["Time_short", new { t = new TimeOnly(14, 5, 9) }].Value);
                Assert.Equal("14:05:09", _messages["Time", new { t = new TimeOnly(14, 5, 9) }].Value);
                Assert.Equal("01:30", _messages["Time_short", new { t = new DateTimeOffset(2026, 3, 2, 22, 30, 0, TimeSpan.Zero) }].Value);
                Assert.Equal("23:30", _messages["Time_short", new { t = new DateTime(2026, 3, 2, 23, 30, 0, DateTimeKind.Unspecified) }].Value);
            }

            using (Scope("ar", "SA"))
            {
                string text = _messages["Time_short", new { t = new TimeOnly(14, 5) }].Value;

                // Latin digits and no bidirectional control, as every server rendering.
                Assert.Contains("2:05", text, StringComparison.Ordinal);
                Assert.DoesNotContain(text, static c => c is '؜' or '‎' or '‏' or (>= '‪' and <= '‮') or (>= '⁦' and <= '⁩'));
            }
        }

        // --- The flow outside every scope ---

        [Fact]
        public async Task Outside_every_scope_the_ui_culture_renders_with_the_requests_calendar_and_zone()
        {
            DateOnly date = new(2026, 3, 2);
            DateTimeOffset instant = new(2026, 3, 2, 22, 30, 0, TimeSpan.Zero);

            // A flow of its own, so the thread's culture and the request's localization end with it.
            await Task.Run(() =>
            {
                CultureInfo culture = NegotiatedCultures.Get("en", "GB");
                CultureInfo.CurrentUICulture = culture;

                // Nothing applied: Gregorian, in UTC.
                Assert.Equal("2 Mar 2026", _messages["Date", new { d = instant }].Value);

                // The request's calendar and zone, in the thread's culture.
                LocalizationScope.ApplyRequest(new LocalizationContext(NegotiatedCultures.Get("ar", "SA"), Calendar("uq"), PlusThree, 1));
                Assert.Equal(Calendar("uq").Format(date.AddDays(1), culture, DateStyle.Medium), _messages["Date", new { d = instant }].Value);
                Assert.Equal("1 item", _messages["Items", new { count = 1 }].Value);

                // A scope wins over the request.
                using (Scope("ar", "SA"))
                {
                    Assert.Equal("عنصر واحد", _messages["Items", new { count = 1 }].Value);
                    Assert.Equal(Gregorian.Format(date, NegotiatedCultures.Get("ar", "SA"), DateStyle.Medium), _messages["Date", new { d = instant }].Value);
                }
            }, TestContext.Current.CancellationToken);
        }

        [Fact]
        public void Rendering_in_a_scope_leaves_the_threads_cultures_as_they_were()
        {
            CultureInfo culture = CultureInfo.CurrentCulture;
            CultureInfo uiCulture = CultureInfo.CurrentUICulture;

            using (Scope("ar", "SA"))
            {
                _ = _messages["Items", new { count = 3 }];
                _ = _messages["Malformed", new { name = "x" }];
            }

            Assert.Same(culture, CultureInfo.CurrentCulture);
            Assert.Same(uiCulture, CultureInfo.CurrentUICulture);
        }

        [Fact]
        public async Task Concurrent_flows_render_each_in_its_own_scope()
        {
            TaskCompletionSource bothEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
            int entered = 0;

            async Task<string> RenderIn(string language, string region)
            {
                using IDisposable scope = Scope(language, region);
                if (Interlocked.Increment(ref entered) == 2)
                {
                    bothEntered.SetResult();
                }

                // Both scopes are open at once before either renders.
                await bothEntered.Task.WaitAsync(TestContext.Current.CancellationToken);
                return _messages["Items", new { count = 3 }].Value;
            }

            string[] rendered = await Task.WhenAll(Task.Run(() => RenderIn("ar", "SA")), Task.Run(() => RenderIn("en", "GB")));

            Assert.Equal(["3 عناصر", "3 items"], rendered);
            Assert.Null(LocalizationScope.Current);
        }

        // --- The fallback chain ---

        [Theory]
        [InlineData("ar", "SA", "Level", "pair")]
        [InlineData("ar", "SA", "LanguageLevel", "language")]
        [InlineData("ar", "SA", "NeutralLevel", "neutral")]
        [InlineData("ar", "EG", "Level", "language")]
        [InlineData("en", "GB", "Level", "neutral")]
        [InlineData("fr", "FR", "Level", "neutral")]
        public void A_key_resolves_from_the_pair_through_the_language_to_the_neutral_resources(string language, string region, string key, string expected)
        {
            using IDisposable scope = Scope(language, region);

            LocalizedString text = _messages[key];
            Assert.Equal(expected, text.Value);
            Assert.False(text.ResourceNotFound);
        }

        [Fact]
        public void A_key_absent_everywhere_is_shown_as_itself_logged_once_and_counted_once_per_culture()
        {
            using (Scope("en", "GB"))
            {
                LocalizedString text = _messages["Nope"];
                Assert.Equal("Nope", text.Value);
                Assert.True(text.ResourceNotFound);
                Assert.Equal("Tellma.Core.Tests.Localization.TestMessages", text.SearchedLocation);
                _ = _messages["Nope", new { count = 1 }];
            }

            // Two Arabic cultures count apart; a culture of a language not offered counts as other.
            using (Scope("ar", "SA"))
            {
                _ = _messages["Nope"];
            }

            using (Scope("ar", "EG"))
            {
                _ = _messages["Nope"];
            }

            using (Scope("fr", "FR"))
            {
                _ = _messages["Nope"];
            }

            Assert.Equal(
                ["en", "ar", "ar", "other"],
                _missing.GetMeasurementSnapshot().Select(static measurement => (string)measurement.Tags[LocalizationTelemetryNames.CultureTag]!));
            Assert.All(_missing.GetMeasurementSnapshot(), static measurement => Assert.Equal(1, measurement.Value));
            Assert.Equal(4, Warnings().Count(static record => record.Id.Name == "MissingResourceString"));
        }

        [Fact]
        public void Every_string_lists_its_raw_value_in_the_flows_culture()
        {
            using IDisposable scope = Scope("ar", "SA");

            var all = _messages.GetAllStrings(includeParentCultures: true).ToDictionary(static s => s.Name, static s => s.Value);

            Assert.Equal("pair", all["Level"]);
            Assert.Equal("neutral", all["NeutralLevel"]);
            Assert.StartsWith("{count, plural, zero", all["Items"], StringComparison.Ordinal);
        }

        // --- Addressing and composition ---

        [Fact]
        public void Cores_resources_are_addressed_from_another_assembly_by_name()
        {
            IStringLocalizer core = _factory.Create(CoreStrings.BaseName, CoreStrings.Assembly);

            using (Scope("en", "GB"))
            {
                Assert.Equal("Gregorian", core["Calendar_gc"].Value);
            }

            using (Scope("ar", "SA"))
            {
                Assert.Equal("الميلادي", core["Calendar_gc"].Value);
                Assert.Equal("المملكة العربية السعودية", core["Region_SA"].Value);
            }

            Assert.Same(_factory.Create(typeof(Strings)), _factory.Create(typeof(Strings)));
            Assert.Same(core, _factory.Create(CoreStrings.BaseName, CoreStrings.Assembly));
        }

        [Fact]
        public void A_composed_host_renders_icu_in_every_localizer_whatever_it_registered_before()
        {
            // The host registered the framework's localization first, with a resources path.
            IServiceCollection services = new ServiceCollection()
                .AddSingleton(SandboxContext.Never)
                .AddLocalization(static options => options.ResourcesPath = "Resources");
            using ServiceProvider provider = TestComposition.Compose(static tellma => tellma.Languages(["en-GB", "ar-SA"]), services: services)
                .BuildServiceProvider(validateScopes: true);

            _ = Assert.IsType<IcuStringLocalizerFactory>(provider.GetRequiredService<IStringLocalizerFactory>());
            _ = Assert.Single(services, static descriptor => descriptor.ServiceType == typeof(IStringLocalizerFactory));

            IStringLocalizer<TestMessages> messages = provider.GetRequiredService<IStringLocalizer<TestMessages>>();
            using IDisposable scope = Scope("ar", "SA");
            Assert.Equal("عنصران", messages["Items", new { count = 2 }].Value);
        }

        /// <summary>The warnings logged so far; the framework's own lookups log at debug level.</summary>
        /// <returns>The warnings, in order.</returns>
        private List<FakeLogRecord> Warnings()
        {
            return [.. _logs.Collector.GetSnapshot().Where(static record => record.Level >= LogLevel.Warning)];
        }

        /// <summary>Opens a scope over a negotiated culture.</summary>
        /// <param name="language">The language.</param>
        /// <param name="region">The region.</param>
        /// <param name="calendar">The calendar system; Gregorian when null.</param>
        /// <param name="zone">The display zone; UTC when null.</param>
        /// <returns>The scope.</returns>
        private static IDisposable Scope(string language, string region, ICalendarSystem? calendar = null, TimeZoneInfo? zone = null)
        {
            return LocalizationScope.Enter(new LocalizationContext(NegotiatedCultures.Get(language, region), calendar ?? Gregorian, zone ?? TimeZoneInfo.Utc, 1));
        }

        /// <summary>One of the platform's three calendar systems.</summary>
        /// <param name="code">The system's code.</param>
        /// <returns>The system.</returns>
        private static ICalendarSystem Calendar(string code)
        {
            return code switch
            {
                "uq" => new UmAlQuraCalendarSystem(),
                "et" => new EthiopianCalendarSystem(),
                _ => Gregorian,
            };
        }
    }
}
