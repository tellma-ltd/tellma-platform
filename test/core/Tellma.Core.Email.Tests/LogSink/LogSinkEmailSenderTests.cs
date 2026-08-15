// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Testing;
using Tellma.Core.Abstractions.Email;

namespace Tellma.Core.Email.Tests.LogSink
{
    /// <summary>
    ///     The sink is the one place in the platform that logs full message content, which is exactly
    ///     why it may only exist in Development.
    /// </summary>
    public class LogSinkEmailSenderTests
    {
        [Fact]
        public async Task Logs_the_whole_message_so_a_developer_can_read_it()
        {
            FakeLogger<LogSinkEmailSender> logger = new();
            LogSinkEmailSender sink = new(Environment("Development"), logger, EmailChannel.Live);

            EmailMessage message = new()
            {
                To = [new EmailAddress("recipient@example.com", "Recipient")],
                Subject = "Your sign-in code",
                TextBody = "Your code is 12345678.",
                HtmlBody = "<p>Your code is 12345678.</p>",
                Audience = EmailAudience.Internal,
                Correlation = new EmailCorrelation("identity", "42"),
                Attachments = [new EmailAttachment("invoice.pdf", "application/pdf", new byte[7])],
            };

            IReadOnlyList<EmailSendResult> results =
                await sink.SendAsync([message], TestContext.Current.CancellationToken);

            EmailSendResult result = Assert.Single(results);
            Assert.Equal(EmailSendOutcome.Sent, result.Outcome);
            Assert.False(result.ExpectsDeliveryEvents);

            FakeLogRecord record = Assert.Single(logger.Collector.GetSnapshot());
            Assert.Equal("Your sign-in code", Field(record, "Subject"));
            Assert.Equal("Your code is 12345678.", Field(record, "TextBody"));
            Assert.Equal("Recipient <recipient@example.com>", Field(record, "To"));
            Assert.Equal("identity::42", Field(record, "Correlation"));
            Assert.Equal("live", Field(record, "Channel"));

            // Attachment names and sizes, never their bytes.
            Assert.Equal("invoice.pdf (application/pdf, 7 bytes)", Field(record, "Attachments"));
        }

        [Fact]
        public async Task Names_the_sandbox_channel_it_stood_in_for()
        {
            FakeLogger<LogSinkEmailSender> logger = new();
            LogSinkEmailSender sink = new(Environment("Development"), logger, EmailChannel.Sandbox);

            await sink.SendAsync(
                [EmailTestMessage()], TestContext.Current.CancellationToken);

            Assert.Equal("sandbox", Field(Assert.Single(logger.Collector.GetSnapshot()), "Channel"));
        }

        [Fact]
        public async Task Rejects_an_invalid_message_like_any_other_transport()
        {
            FakeLogger<LogSinkEmailSender> logger = new();
            LogSinkEmailSender sink = new(Environment("Development"), logger, EmailChannel.Live);

            IReadOnlyList<EmailSendResult> results = await sink.SendAsync(
                [EmailTestMessage() with { To = [] }, EmailTestMessage()],
                TestContext.Current.CancellationToken);

            Assert.Equal(EmailSendOutcome.Rejected, results[0].Outcome);
            Assert.Equal(EmailSendOutcome.Sent, results[1].Outcome);
            Assert.Single(logger.Collector.GetSnapshot());
        }

        [Theory]
        [InlineData("Staging")]
        [InlineData("Production")]
        public void Refuses_to_be_constructed_outside_development(string environmentName)
        {
            // The last tripwire, for a host that hand-registers the sink and thereby leaves the
            // pipeline whose startup gate would otherwise have caught this.
            InvalidOperationException failure = Assert.Throws<InvalidOperationException>(
                () => new LogSinkEmailSender(
                    Environment(environmentName), new FakeLogger<LogSinkEmailSender>(), EmailChannel.Live));

            Assert.Contains(environmentName, failure.Message, StringComparison.Ordinal);
            Assert.Contains("Email:Provider", failure.Message, StringComparison.Ordinal);
        }

        internal static EmailMessage EmailTestMessage()
        {
            return new EmailMessage
            {
                To = [new EmailAddress("recipient@example.com")],
                Subject = "Subject",
                TextBody = "Body",
                Audience = EmailAudience.Internal,
            };
        }

        internal static IHostEnvironment Environment(string environmentName)
        {
            return new StubHostEnvironment(environmentName);
        }

        private static string? Field(FakeLogRecord record, string name)
        {
            return record.StructuredState?.FirstOrDefault(kv => kv.Key == name).Value;
        }

        private sealed class StubHostEnvironment(string environmentName) : IHostEnvironment
        {
            public string EnvironmentName { get; set; } = environmentName;

            public string ApplicationName { get; set; } = "Tellma.Core.Email.Tests";

            public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

            public IFileProvider ContentRootFileProvider { get; set; } =
                new PhysicalFileProvider(AppContext.BaseDirectory);
        }
    }
}
