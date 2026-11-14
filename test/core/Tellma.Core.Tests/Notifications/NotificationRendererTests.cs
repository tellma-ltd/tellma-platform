// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Text.Json;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Calendars;
using Tellma.Core.Abstractions.Localization;
using Tellma.Core.Calendars;
using Tellma.Core.Composition;
using Tellma.Core.Localization;
using Tellma.Core.Notifications;
using Tellma.Core.Tests.Data.Batches;
using static Tellma.Core.Tests.Notifications.NotificationTestModel;

namespace Tellma.Core.Tests.Notifications
{
    /// <summary>
    ///     The server-side renderer over the test assembly's notification texts and Core's own: the
    ///     resource of the feature that registered the type, the arguments by their JSON kind, a
    ///     localized text in the reader's slot, dates in the reader's zone and calendar, the detail
    ///     line, a missing resource and malformed arguments.
    /// </summary>
    public sealed class NotificationRendererTests : IDisposable
    {
        /// <summary>A fixed zone three hours ahead of UTC, the same on every host.</summary>
        private static readonly TimeZoneInfo PlusThree = TimeZoneInfo.CreateCustomTimeZone("Test+03", TimeSpan.FromHours(3), "Test+03", "Test+03");

        /// <summary>The Gregorian calendar.</summary>
        private static readonly GregorianCalendarSystem Gregorian = new();

        /// <summary>The composition that registers the test types.</summary>
        private readonly BatchHarness _harness = new(compose: Compose);

        /// <summary>The meter factory and the logs.</summary>
        private readonly ServiceProvider _services = new ServiceCollection()
            .AddMetrics()
            .AddLogging(static logging => logging.SetMinimumLevel(LogLevel.Trace))
            .AddFakeLogging()
            .BuildServiceProvider();

        /// <summary>The renderer under test.</summary>
        private readonly NotificationRenderer _renderer;

        /// <summary>Creates the renderer over the test composition.</summary>
        public NotificationRendererTests()
        {
            ILoggerFactory loggers = _services.GetRequiredService<ILoggerFactory>();
            IcuStringLocalizerFactory localizers = new(
                new LanguageCatalog(new LanguageDeclaration(["en-GB", "ar-SA"], [])), loggers, _services.GetRequiredService<IMeterFactory>());
            TellmaCompositionRecord record = _harness.Services.GetRequiredService<TellmaCompositionRecord>();
            _renderer = new NotificationRenderer(localizers, record, new NeutralStringKeys(record), loggers);
        }

        /// <summary>A British English reader three hours ahead of UTC.</summary>
        private static LocalizationContext English { get; } = new(NegotiatedCultures.Get("en", "GB"), Gregorian, PlusThree, 1);

        /// <inheritdoc />
        public void Dispose()
        {
            _harness.Dispose();
            _services.Dispose();
        }

        [Fact]
        public void The_title_and_the_detail_line_format_the_arguments_by_their_kind()
        {
            string text = _renderer.Render(
                Shipped,
                /*lang=json,strict*/ """{"number":"SO-42","customer":"Acme","shippedAt":"2026-03-02T22:30:00Z","boxes":3,"express":true}""",
                OrderResource,
                42,
                English);

            // 22:30 UTC is already the next day three hours ahead.
            Assert.Equal("Order SO-42 shipped to Acme on 3 Mar 2026\n3 boxes, express", text);
        }

        [Fact]
        public void A_type_without_a_detail_resource_renders_its_title_alone()
        {
            Assert.Equal("Order SO-42 is held", _renderer.Render(Held, /*lang=json,strict*/ """{"number":"SO-42"}""", null, null, English));
        }

        [Fact]
        public void The_text_renders_in_the_readers_culture_and_calendar()
        {
            UmAlQuraCalendarSystem calendar = new();
            CultureInfo arabic = NegotiatedCultures.Get("ar", "SA");
            LocalizationContext reader = new(arabic, calendar, PlusThree, 1);

            string text = _renderer.Render(
                Shipped,
                /*lang=json,strict*/ """{"number":"SO-42","customer":"Acme","shippedAt":"2026-03-02T22:30:00Z","boxes":1,"express":false}""",
                OrderResource,
                42,
                reader);

            Assert.Equal(
                $"شُحن الطلب SO-42 إلى Acme في {calendar.Format(new DateOnly(2026, 3, 3), arabic, DateStyle.Medium)}\nصندوق واحد، عادي",
                text);
        }

        [Fact]
        public void A_bare_date_is_a_calendar_date_that_never_shifts()
        {
            LocalizationContext behind = English with { TimeZone = TimeZoneInfo.CreateCustomTimeZone("Test-10", TimeSpan.FromHours(-10), "Test-10", "Test-10") };

            Assert.Equal("The report is due on 2 Mar 2026", _renderer.Render(ReportDue, /*lang=json,strict*/ """{"dueOn":"2026-03-02"}""", null, null, behind));
        }

        [Fact]
        public void Any_other_argument_renders_as_its_json_text_and_null_as_nothing()
        {
            string text = _renderer.Render(
                Raw,
                /*lang=json,strict*/ """{"payload":{"a":1},"list":[1,2],"note":null,"ratio":0.5,"stamp":"2026-03-02T10:00:00"}""",
                null,
                null,
                English);

            // A date-time without an offset names no instant, so it stays text.
            Assert.Equal("""{"a":1} | [1,2] |  | 0.5 | 2026-03-02T10:00:00""", text);
        }

        [Theory]
        [InlineData(1, "Ahmad added you to Acme")]
        [InlineData(2, "أحمد added you to أكمي")]
        [InlineData(3, "Ahmad added you to Acme")]
        public void A_localized_text_renders_the_readers_content_slot_and_falls_back_to_the_first(int slot, string expected)
        {
            // Core's own type, rendered through Core's resources; the third slots are null and empty.
            string arguments = JsonSerializer.Serialize(
                new { tenantName = new LocalizedText("Acme", "أكمي", null), actorName = new LocalizedText("Ahmad", "أحمد", "") },
                TellmaJson.Options);

            Assert.Equal(expected, _renderer.Render("core.user.added", arguments, null, null, English with { ContentLanguageIndex = slot }));
        }

        [Fact]
        public void A_type_with_no_resource_renders_its_key_and_counts_the_miss()
        {
            using MetricCollector<long> missing = new(_services.GetRequiredService<IMeterFactory>(), LocalizationTelemetryNames.MeterName, LocalizationTelemetryNames.Missing);

            Assert.Equal("acme.order.lost", _renderer.Render("acme.order.lost", null, null, null, English));
            Assert.NotEmpty(missing.GetMeasurementSnapshot());
        }

        [Theory]
        [InlineData("{not json")]
        [InlineData("[1, 2]")]
        public void Malformed_arguments_render_the_message_without_them_and_warn_once_per_scope(string arguments)
        {
            Assert.Equal("Order {number} is held", _renderer.Render(Held, arguments, null, null, English));
            Assert.Equal("Order {number} is held", _renderer.Render(Held, arguments, null, null, English));

            FakeLogRecord warning = Assert.Single(_services.GetFakeLogCollector().GetSnapshot(), static record => record.Id.Name == "NotificationArgumentsInvalid");
            Assert.Equal(LogLevel.Warning, warning.Level);
            Assert.Equal($"The arguments of a notification of type {Held} are not a JSON object; its text renders without them.", warning.Message);
        }

        [Fact]
        public void The_renderer_leaves_no_localization_scope_behind()
        {
            _ = _renderer.Render(Held, /*lang=json,strict*/ """{"number":"SO-42"}""", null, null, English);

            Assert.Null(LocalizationScope.Current);
        }

        [Fact]
        public void The_resource_key_replaces_every_dot_of_the_type_key()
        {
            Assert.Equal("Notification_core_job_pending-confirmation", NotificationRenderer.ResourceKey("core.job.pending-confirmation"));
        }
    }
}
