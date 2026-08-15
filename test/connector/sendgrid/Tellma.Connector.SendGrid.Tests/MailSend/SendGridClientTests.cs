// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Net;
using System.Text.Json;
using Tellma.Testing.Support.Http;

namespace Tellma.Connector.SendGrid.Tests.MailSend
{
    /// <summary>
    ///     The wire shape of a mail-send request, snapshotted through a scripted handler. SendGrid
    ///     rejects several of these details outright, so they are worth pinning rather than
    ///     rediscovering against the live API.
    /// </summary>
    public class SendGridClientTests
    {
        [Fact]
        public async Task Posts_the_documented_payload_shape_with_a_bearer_token()
        {
            ScriptedHttpMessageHandler handler = new(
                static (_, _) => ScriptedHttpMessageHandler.Respond(HttpStatusCode.Accepted, messageIdHeader: "msg-1"));

            using HttpClient httpClient = new(handler);
            SendGridClient client = new(httpClient, new SendGridClientOptions("SG.key", TimeSpan.FromSeconds(30)));

            SendGridSendResult result = await client.SendAsync(FullFeatureRequest(), TestContext.Current.CancellationToken);

            Assert.True(result.IsSuccess);
            Assert.Equal("msg-1", result.MessageId);

            HttpRequestMessage request = Assert.Single(handler.Requests);
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal("SG.key", request.Headers.Authorization?.Parameter);
            Assert.Equal(new Uri("https://api.sendgrid.com/v3/mail/send"), request.RequestUri);

            using var body = JsonDocument.Parse(Assert.Single(handler.RequestBodies));
            JsonElement root = body.RootElement;

            Assert.Equal("no-reply@tellma.com", root.GetProperty("from").GetProperty("email").GetString());
            Assert.Equal("Tellma", root.GetProperty("from").GetProperty("name").GetString());
            Assert.Equal("Subject", root.GetProperty("subject").GetString());

            JsonElement personalization = root.GetProperty("personalizations")[0];
            Assert.Equal("to@example.com", personalization.GetProperty("to")[0].GetProperty("email").GetString());
            Assert.Equal("cc@example.com", personalization.GetProperty("cc")[0].GetProperty("email").GetString());

            // SendGrid requires text/plain before text/html and rejects the reverse.
            JsonElement content = root.GetProperty("content");
            Assert.Equal("text/plain", content[0].GetProperty("type").GetString());
            Assert.Equal("text/html", content[1].GetProperty("type").GetString());

            JsonElement attachment = root.GetProperty("attachments")[0];
            Assert.Equal("logo.png", attachment.GetProperty("filename").GetString());
            Assert.Equal("inline", attachment.GetProperty("disposition").GetString());
            Assert.Equal("logo", attachment.GetProperty("content_id").GetString());

            Assert.Equal(
                "etpharma:outbox:3:42",
                root.GetProperty("custom_args").GetProperty("tellma_correlation").GetString());
            Assert.True(root.GetProperty("mail_settings").GetProperty("sandbox_mode").GetProperty("enable").GetBoolean());
        }

        [Fact]
        public async Task Omits_every_absent_field_because_sendgrid_rejects_explicit_nulls()
        {
            ScriptedHttpMessageHandler handler = new(
                static (_, _) => ScriptedHttpMessageHandler.Respond(HttpStatusCode.Accepted));

            using HttpClient httpClient = new(handler);
            SendGridClient client = new(httpClient, new SendGridClientOptions("SG.key", TimeSpan.FromSeconds(30)));

            await client.SendAsync(MinimalRequest(), TestContext.Current.CancellationToken);

            using var body = JsonDocument.Parse(Assert.Single(handler.RequestBodies));
            JsonElement root = body.RootElement;

            Assert.False(root.TryGetProperty("reply_to", out _));
            Assert.False(root.TryGetProperty("attachments", out _));
            Assert.False(root.TryGetProperty("custom_args", out _));
            Assert.False(root.TryGetProperty("mail_settings", out _));
            Assert.False(root.GetProperty("personalizations")[0].TryGetProperty("cc", out _));
            Assert.False(root.GetProperty("from").TryGetProperty("name", out _));
        }

        [Fact]
        public async Task Treats_the_sandbox_modes_200_as_a_success_like_the_ordinary_202()
        {
            ScriptedHttpMessageHandler handler = new(
                static (_, _) => ScriptedHttpMessageHandler.Respond(HttpStatusCode.OK));

            using HttpClient httpClient = new(handler);
            SendGridClient client = new(httpClient, new SendGridClientOptions("SG.key", TimeSpan.FromSeconds(30)));

            SendGridSendResult result = await client.SendAsync(MinimalRequest(), TestContext.Current.CancellationToken);

            Assert.True(result.IsSuccess);
        }

        [Fact]
        public async Task Parses_the_error_body_and_keeps_it_verbatim()
        {
            const string ErrorBody = /*lang=json,strict*/ """{"errors":[{"message":"Does not contain a valid address.","field":"from.email"}]}""";
            ScriptedHttpMessageHandler handler = new(
                static (_, _) => ScriptedHttpMessageHandler.Respond(HttpStatusCode.BadRequest, ErrorBody));

            using HttpClient httpClient = new(handler);
            SendGridClient client = new(httpClient, new SendGridClientOptions("SG.key", TimeSpan.FromSeconds(30)));

            SendGridSendResult result = await client.SendAsync(MinimalRequest(), TestContext.Current.CancellationToken);

            Assert.False(result.IsSuccess);
            Assert.Equal(400, result.StatusCode);
            Assert.Equal("from.email: Does not contain a valid address.", result.DescribeErrors());
            Assert.Equal(ErrorBody, result.RawErrorBody);
        }

        [Fact]
        public async Task Keeps_a_failure_body_that_is_not_json()
        {
            // A gateway error page is exactly the body an operator needs to see, and exactly the one
            // a parser would throw away.
            ScriptedHttpMessageHandler handler = new(
                static (_, _) => ScriptedHttpMessageHandler.Respond(HttpStatusCode.BadGateway, "<html>502</html>"));

            using HttpClient httpClient = new(handler);
            SendGridClient client = new(httpClient, new SendGridClientOptions("SG.key", TimeSpan.FromSeconds(30)));

            SendGridSendResult result = await client.SendAsync(MinimalRequest(), TestContext.Current.CancellationToken);

            Assert.Equal("<html>502</html>", result.DescribeErrors());
        }

        private static SendGridMailRequest MinimalRequest()
        {
            return new SendGridMailRequest
            {
                Personalizations = [new SendGridPersonalization([new SendGridEmailAddress("to@example.com")])],
                From = new SendGridEmailAddress("no-reply@tellma.com"),
                Subject = "Subject",
                Content = [new SendGridContent("text/plain", "Body")],
            };
        }

        private static SendGridMailRequest FullFeatureRequest()
        {
            return new SendGridMailRequest
            {
                Personalizations =
                [
                    new SendGridPersonalization(
                        [new SendGridEmailAddress("to@example.com")],
                        [new SendGridEmailAddress("cc@example.com")]),
                ],
                From = new SendGridEmailAddress("no-reply@tellma.com", "Tellma"),
                ReplyTo = new SendGridEmailAddress("support@tellma.com"),
                Subject = "Subject",
                Content =
                [
                    new SendGridContent("text/plain", "Body"),
                    new SendGridContent("text/html", "<p>Body</p>"),
                ],
                Attachments =
                [
                    new SendGridAttachment(
                        Convert.ToBase64String([1, 2, 3]), "image/png", "logo.png", "inline", "logo"),
                ],
                CustomArgs = new Dictionary<string, string> { ["tellma_correlation"] = "etpharma:outbox:3:42" },
                MailSettings = new SendGridMailSettings(new SendGridSandboxMode(true)),
            };
        }
    }
}
