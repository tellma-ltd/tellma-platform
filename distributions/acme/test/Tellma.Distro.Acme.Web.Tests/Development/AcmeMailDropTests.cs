// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using System.Text.Json;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Email;
using Tellma.Core.Email;
using Tellma.Distro.Acme.Web.Development;
using Tellma.Distro.Acme.Web.Tests.Infrastructure;

namespace Tellma.Distro.Acme.Web.Tests.Development
{
    /// <summary>
    ///     The development mail drop the browser suites read sign-in codes from: the file each message
    ///     becomes, the messages it rejects, and the rules that keep it out of every environment but
    ///     Development.
    /// </summary>
    public sealed class AcmeMailDropTests : IDisposable
    {
        /// <summary>The instant the fixed clock reads.</summary>
        private static readonly DateTimeOffset Now = new(2026, 11, 23, 10, 15, 30, 123, TimeSpan.Zero);

        /// <summary>A fresh drop directory per test, removed afterwards.</summary>
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "tellma-acme-mail-drop-tests", Guid.NewGuid().ToString("N"));

        /// <inheritdoc />
        public void Dispose()
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }

        /// <summary>A sent message becomes one JSON file holding its addresses, bodies and attachment sizes, but no bytes.</summary>
        [Fact]
        public async Task A_sent_message_becomes_one_json_file_with_its_addresses_bodies_and_attachment_sizes()
        {
            AcmeMailDropSender sender = new(Environment(Environments.Development), _directory, EmailChannel.Sandbox, new FixedTime(Now));
            EmailMessage message = new()
            {
                From = new EmailAddress("noreply@localhost", "Acme"),
                To = [new EmailAddress("admin@localhost")],
                Cc = [new EmailAddress("sales@localhost", "Sales")],
                Subject = "Your sign-in code",
                TextBody = "Your code is 12345678.",
                HtmlBody = "<p>Your code is 12345678.</p>",
                Attachments = [new EmailAttachment("terms.pdf", "application/pdf", new byte[] { 1, 2, 3 })],
                Audience = EmailAudience.Internal,
            };

            IReadOnlyList<EmailSendResult> results = await sender.SendAsync([message], TestContext.Current.CancellationToken);

            Assert.Equal(EmailSendOutcome.Sent, Assert.Single(results).Outcome);
            string file = Assert.Single(Directory.GetFiles(_directory));
            Assert.StartsWith("20261123T101530123", Path.GetFileName(file), StringComparison.Ordinal);
            Assert.EndsWith(AcmeMailDropSender.Extension, file, StringComparison.Ordinal);

            // --- The file's members, in the camelCase the browser helpers read ---
            using var json = JsonDocument.Parse(await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken));
            JsonElement root = json.RootElement;
            Assert.Equal(1, root.GetProperty("version").GetInt32());
            Assert.Equal(Now, root.GetProperty("sentAt").GetDateTimeOffset());
            Assert.Equal("sandbox", root.GetProperty("channel").GetString());
            Assert.Equal("internal", root.GetProperty("audience").GetString());
            Assert.Equal("Acme", root.GetProperty("from").GetProperty("displayName").GetString());
            Assert.Equal("admin@localhost", Assert.Single(root.GetProperty("to").EnumerateArray()).GetProperty("address").GetString());
            Assert.Equal(JsonValueKind.Null, root.GetProperty("to")[0].GetProperty("displayName").ValueKind);
            Assert.Equal("Sales", root.GetProperty("cc")[0].GetProperty("displayName").GetString());
            Assert.Empty(root.GetProperty("bcc").EnumerateArray());
            Assert.Equal("Your sign-in code", root.GetProperty("subject").GetString());
            Assert.Equal("Your code is 12345678.", root.GetProperty("textBody").GetString());
            Assert.Equal("<p>Your code is 12345678.</p>", root.GetProperty("htmlBody").GetString());
            JsonElement attachment = Assert.Single(root.GetProperty("attachments").EnumerateArray());
            Assert.Equal("terms.pdf", attachment.GetProperty("fileName").GetString());
            Assert.Equal(3, attachment.GetProperty("length").GetInt32());
            Assert.False(attachment.TryGetProperty("content", out _));
        }

        /// <summary>An invalid message in a batch is rejected on its own; the valid ones beside it are still dropped.</summary>
        [Fact]
        public async Task An_invalid_message_is_rejected_on_its_own_and_the_rest_of_the_batch_is_dropped()
        {
            AcmeMailDropSender sender = new(Environment(Environments.Development), _directory, EmailChannel.Live, TimeProvider.System);
            EmailMessage valid = new() { To = [new EmailAddress("admin@localhost")], Subject = "Hello", TextBody = "Hello.", Audience = EmailAudience.External };
            EmailMessage invalid = valid with { To = [] };

            IReadOnlyList<EmailSendResult> results = await sender.SendAsync([invalid, valid, valid], TestContext.Current.CancellationToken);

            Assert.Equal([EmailSendOutcome.Rejected, EmailSendOutcome.Sent, EmailSendOutcome.Sent], results.Select(static result => result.Outcome));
            Assert.NotNull(results[0].Error);
            Assert.Equal(2, Directory.GetFiles(_directory, "*" + AcmeMailDropSender.Extension).Length);
            Assert.Empty(Directory.GetFiles(_directory, "*.partial"));
        }

        /// <summary>The sender refuses to exist outside Development, or without an absolute directory.</summary>
        [Fact]
        public void The_sender_refuses_any_environment_but_development_and_any_directory_but_an_absolute_one()
        {
            InvalidOperationException production = Assert.Throws<InvalidOperationException>(
                () => new AcmeMailDropSender(Environment(Environments.Production), _directory, EmailChannel.Live, TimeProvider.System));
            Assert.Contains("'Production'", production.Message, StringComparison.Ordinal);

            _ = Assert.Throws<InvalidOperationException>(
                () => new AcmeMailDropSender(Environment(Environments.Development), null, EmailChannel.Live, TimeProvider.System));
            _ = Assert.Throws<InvalidOperationException>(
                () => new AcmeMailDropSender(Environment(Environments.Development), "relative/drop", EmailChannel.Live, TimeProvider.System));
        }

        /// <summary>Outside Development the check refuses either setting; with neither it passes.</summary>
        [Theory]
        [InlineData("Production")]
        [InlineData("Staging")]
        public void Outside_development_the_check_refuses_the_drop_and_its_directory(string environment)
        {
            IHostEnvironment host = Environment(environment);

            Assert.Equal(2, AcmeMailDropCheck.Check(AcmeMailDrop.TransportName, _directory, host).Count);
            Assert.Single(AcmeMailDropCheck.Check("sendgrid", _directory, host));
            Assert.Single(AcmeMailDropCheck.Check(" Development-Drop ", null, host));
            Assert.Empty(AcmeMailDropCheck.Check("sendgrid", null, host));
            Assert.Empty(AcmeMailDropCheck.Check(null, null, host));
        }

        /// <summary>In Development the provider and an absolute directory come together or not at all.</summary>
        [Fact]
        public void In_development_the_check_wants_the_provider_and_an_absolute_directory_together()
        {
            IHostEnvironment host = Environment(Environments.Development);

            Assert.Empty(AcmeMailDropCheck.Check(AcmeMailDrop.TransportName, _directory, host));
            Assert.Empty(AcmeMailDropCheck.Check(null, null, host));
            Assert.Empty(AcmeMailDropCheck.Check("log", null, host));

            CompositionProblem missing = Assert.Single(AcmeMailDropCheck.Check(AcmeMailDrop.TransportName, " ", host));
            Assert.Equal(AcmeMailDrop.CheckName, missing.Source);
            Assert.Contains(AcmeMailDrop.DirectoryKey, missing.Problem, StringComparison.Ordinal);

            Assert.Single(AcmeMailDropCheck.Check("log", _directory, host));
            Assert.Single(AcmeMailDropCheck.Check(AcmeMailDrop.TransportName, "relative/drop", host));
        }

        /// <summary>
        ///     The web host registers the check among its startup checks, so a drop configured wrongly
        ///     stops the host at startup rather than at its first send.
        /// </summary>
        /// <returns>A task that completes when the host's check has run.</returns>
        [Fact]
        public async Task The_web_host_runs_the_check_among_its_startup_checks()
        {
            await using WebApplication app = AcmeWebApplication.Build(Environments.Development, new Dictionary<string, string?>
            {
                [AcmeMailDrop.ProviderKey] = AcmeMailDrop.TransportName,
                [AcmeMailDrop.DirectoryKey] = "relative/drop",
            });

            IStartupCheck check = Assert.Single(app.Services.GetServices<IStartupCheck>(), static check => check.Name == AcmeMailDrop.CheckName);
            IReadOnlyList<CompositionProblem> problems = await check.CheckAsync(app.Services, TestContext.Current.CancellationToken);
            Assert.Equal(AcmeMailDrop.CheckName, Assert.Single(problems).Source);
        }

        /// <summary>A host environment of a given name.</summary>
        /// <param name="name">The environment's name.</param>
        /// <returns>The environment.</returns>
        private static TestEnvironment Environment(string name)
        {
            return new TestEnvironment { EnvironmentName = name };
        }

        /// <summary>A host environment that names itself and nothing else.</summary>
        private sealed class TestEnvironment : IHostEnvironment
        {
            /// <inheritdoc />
            public string EnvironmentName { get; set; } = Environments.Development;

            /// <inheritdoc />
            public string ApplicationName { get; set; } = "Tellma.Distro.Acme.Web";

            /// <inheritdoc />
            public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

            /// <inheritdoc />
            public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        }

        /// <summary>A clock that always reads one instant.</summary>
        /// <param name="now">The instant.</param>
        private sealed class FixedTime(DateTimeOffset now) : TimeProvider
        {
            /// <inheritdoc />
            public override DateTimeOffset GetUtcNow()
            {
                return now;
            }
        }
    }
}
