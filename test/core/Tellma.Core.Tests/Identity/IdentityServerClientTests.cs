// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Logging.Testing;
using System.Net;
using System.Text.Json.Nodes;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Identity;
using Tellma.Core.Identity;

namespace Tellma.Core.Tests.Identity
{
    /// <summary>
    ///     The identity-server client's requests and answers: the routes under the authority's path,
    ///     the bodies' wire shapes, chunking at a thousand in input order, the mapping of every result,
    ///     the refusal of an answer that cannot be recorded, and the service-account calls.
    /// </summary>
    public sealed class IdentityServerClientTests : IDisposable
    {
        /// <summary>The client over the fake server.</summary>
        private readonly IdentityClientHarness _harness = new();

        /// <summary>The test's cancellation.</summary>
        private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

        /// <inheritdoc />
        public void Dispose()
        {
            _harness.Dispose();
        }

        // --- The requests ---

        [Fact]
        public async Task An_invite_posts_camel_case_users_under_the_authority_path_with_the_service_token()
        {
            IReadOnlyList<IdentityInvitationResult> results = await _harness.Client.InviteAsync(
                [
                    new IdentityInvitation("ada@acme.com", "Ada", "en", Gender.Female, "https://acme.app.tellma.com/a/1", ExistingOnly: true),
                    new IdentityInvitation("bob@acme.com", null, null, null, null),
                ],
                Cancellation);

            Assert.Equal(2, results.Count);
            RecordedRequest request = Assert.Single(_harness.Server.ApiRequests);
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("https://acme.app.tellma.com/id/api/identity/v1/invitations", request.Uri.AbsoluteUri);
            Assert.Equal("Bearer token-1", request.Authorization);
            Assert.Equal("application/json", request.Accept);
            Assert.StartsWith("Tellma.Core/", request.UserAgent, StringComparison.Ordinal);
            Assert.Equal("application/json", request.ContentType);

            // ExistingOnly is written either way; a null member is omitted; gender is its name.
            Assert.Equal(
                /*lang=json,strict*/
                """{"users":[{"email":"ada@acme.com","displayName":"Ada","locale":"en","gender":"Female","returnUrl":"https://acme.app.tellma.com/a/1","existingOnly":true},{"email":"bob@acme.com","existingOnly":false}]}""",
                request.Body);
        }

        [Fact]
        public async Task The_token_is_a_client_credentials_grant_for_the_management_scope_with_no_resource()
        {
            _ = await _harness.Client.InviteAsync(IdentityClientHarness.Invitations(1), Cancellation);

            RecordedRequest token = Assert.Single(_harness.Server.TokenRequests);
            Assert.Equal(HttpMethod.Post, token.Method);
            Assert.Equal("https://acme.app.tellma.com/id/connect/token", token.Uri.AbsoluteUri);
            Assert.Equal("application/x-www-form-urlencoded", token.ContentType);
            Assert.Null(token.Authorization);
            Assert.Equal(
                new Dictionary<string, string>
                {
                    ["grant_type"] = "client_credentials",
                    ["scope"] = "tellma_identity",
                    ["client_id"] = "acme-svc",
                    ["client_secret"] = IdentityClientHarness.Secret,
                },
                token.Form());
            Assert.All(_harness.Server.ClientNames, static name => Assert.Equal(IdentityServerClient.HttpClientName, name));
        }

        [Fact]
        public async Task A_delivery_status_posts_the_subjects_and_maps_each_state()
        {
            _harness.Server.Api = static (request, _) => Task.FromResult(FakeIdentityServer.JsonAnswer(HttpStatusCode.OK, new JsonObject
            {
                ["results"] = new JsonArray(
                    new JsonObject
                    {
                        ["sub"] = "s-1",
                        ["state"] = "Bounced",
                        ["expectsDeliveryEvents"] = false,
                        ["sentUtc"] = "2026-11-14T12:00:00.1234567+00:00",
                        ["updatedUtc"] = "2026-11-14T12:05:00Z",
                        ["reason"] = "550 mailbox unavailable",
                    },
                    new JsonObject { ["sub"] = "s-2", ["state"] = "NotFound", ["expectsDeliveryEvents"] = false, ["futureMember"] = 1 }),
            }));

            IReadOnlyList<IdentityDeliveryStatus> statuses = await _harness.Client.GetInvitationDeliveryAsync(["s-1", "s-2"], Cancellation);

            RecordedRequest request = Assert.Single(_harness.Server.ApiRequests);
            Assert.Equal("https://acme.app.tellma.com/id/api/identity/v1/invitations/delivery-status", request.Uri.AbsoluteUri);
            Assert.Equal(/*lang=json,strict*/ """{"subs":["s-1","s-2"]}""", request.Body);
            Assert.Equal(
                [
                    new IdentityDeliveryStatus(
                        "s-1",
                        IdentityDeliveryState.Bounced,
                        false,
                        new DateTimeOffset(2026, 11, 14, 12, 0, 0, TimeSpan.Zero).AddTicks(1234567),
                        new DateTimeOffset(2026, 11, 14, 12, 5, 0, TimeSpan.Zero),
                        "550 mailbox unavailable"),
                    new IdentityDeliveryStatus("s-2", IdentityDeliveryState.NotFound, false, null, null, null),
                ],
                statuses);
        }

        [Fact]
        public async Task Every_delivery_state_name_maps_to_its_member()
        {
            _harness.Server.Api = static (request, _) => Task.FromResult(FakeIdentityServer.JsonAnswer(HttpStatusCode.OK, new JsonObject
            {
                ["results"] = new JsonArray([.. request.Json()["subs"]!.AsArray().Select(static sub => (JsonNode)new JsonObject
                {
                    ["sub"] = sub!.GetValue<string>(),
                    ["state"] = sub.GetValue<string>(),
                    ["expectsDeliveryEvents"] = true,
                })]),
            }));
            IdentityDeliveryState[] states = Enum.GetValues<IdentityDeliveryState>();

            IReadOnlyList<IdentityDeliveryStatus> statuses = await _harness.Client.GetInvitationDeliveryAsync(
                [.. states.Select(static state => state.ToString())],
                Cancellation);

            Assert.Equal(states, statuses.Select(static status => status.State));
        }

        // --- Chunking ---

        [Fact]
        public async Task A_bulk_invite_is_sent_in_chunks_of_a_thousand_and_answered_in_input_order()
        {
            List<IdentityInvitation> invitations = IdentityClientHarness.Invitations(2500);

            IReadOnlyList<IdentityInvitationResult> results = await _harness.Client.InviteAsync(invitations, Cancellation);

            Assert.Equal([1000, 1000, 500], _harness.Server.ApiRequests.Select(static request => request.Json()["users"]!.AsArray().Count));
            Assert.Equal(invitations.Select(static invitation => invitation.Email), results.Select(static result => result.Email));
            Assert.All(results, static result => Assert.Equal(("sub-" + result.Email, InviteStatus.Invited, null), (result.Subject, result.Status, result.Error)));
            Assert.Single(_harness.Server.TokenRequests);
        }

        [Fact]
        public async Task A_bulk_delivery_status_is_sent_in_chunks_of_a_thousand()
        {
            List<string> subjects = [.. Enumerable.Range(0, 1001).Select(static i => $"s-{i}")];

            IReadOnlyList<IdentityDeliveryStatus> statuses = await _harness.Client.GetInvitationDeliveryAsync(subjects, Cancellation);

            Assert.Equal([1000, 1], _harness.Server.ApiRequests.Select(static request => request.Json()["subs"]!.AsArray().Count));
            Assert.Equal(subjects, statuses.Select(static status => status.Subject));
        }

        [Fact]
        public async Task An_empty_input_makes_no_call()
        {
            Assert.Empty(await _harness.Client.InviteAsync([], Cancellation));
            Assert.Empty(await _harness.Client.GetInvitationDeliveryAsync([], Cancellation));
            Assert.Empty(_harness.Server.Requests);
        }

        [Fact]
        public async Task An_invitation_without_an_address_or_a_blank_subject_is_refused_before_any_call()
        {
            _ = await Assert.ThrowsAsync<ArgumentException>(() => _harness.Client.InviteAsync(
                [new IdentityInvitation("ada@acme.com", null, null, null, null), new IdentityInvitation(" ", null, null, null, null)],
                Cancellation));
            _ = await Assert.ThrowsAsync<ArgumentException>(() => _harness.Client.InviteAsync([null!], Cancellation));
            _ = await Assert.ThrowsAsync<ArgumentException>(() => _harness.Client.GetInvitationDeliveryAsync(["s-1", ""], Cancellation));
            _ = await Assert.ThrowsAsync<ArgumentNullException>(() => _harness.Client.InviteAsync(null!, Cancellation));
            Assert.Empty(_harness.Server.Requests);
        }

        // --- Mapping the invite's results ---

        [Fact]
        public async Task Each_status_and_a_refusal_map_onto_the_invitation_in_their_position()
        {
            _harness.Server.Api = static (_, _) => Task.FromResult(FakeIdentityServer.JsonAnswer(HttpStatusCode.OK, new JsonObject
            {
                ["results"] = new JsonArray(
                    Result("a@acme.com", "s-a", "Invited", null),
                    Result("b@acme.com", "s-b", "Reinvited", null),
                    Result("c@acme.com", "s-c", "Active", null),
                    Result("d@acme.com", null, null, "Not an address the server accepts.")),
            }));

            IReadOnlyList<IdentityInvitationResult> results = await _harness.Client.InviteAsync(
                [.. "abcd".Select(static c => new IdentityInvitation($"{c}@acme.com", null, null, null, null))],
                Cancellation);

            Assert.Equal(
                [
                    new IdentityInvitationResult("a@acme.com", "s-a", InviteStatus.Invited, null),
                    new IdentityInvitationResult("b@acme.com", "s-b", InviteStatus.Reinvited, null),
                    new IdentityInvitationResult("c@acme.com", "s-c", InviteStatus.Active, null),
                    new IdentityInvitationResult("d@acme.com", null, null, "Not an address the server accepts."),
                ],
                results);
        }

        [Theory]
        [InlineData("a@acme.com", "s-a", "Invited", null, 2)]
        [InlineData("A@acme.com", "s-a", "Invited", null, 1)]
        [InlineData("b@acme.com", "s-a", "Invited", null, 1)]
        [InlineData("a@acme.com", "s-a", "Suspended", null, 1)]
        [InlineData("a@acme.com", null, "Invited", null, 1)]
        [InlineData("a@acme.com", "s-a", "Invited", "Refused anyway.", 1)]
        [InlineData("a@acme.com", "s-a", null, null, 1)]
        public async Task An_invite_answer_that_cannot_be_recorded_is_refused(string email, string? sub, string? status, string? error, int answered)
        {
            // Too many results, a result for another address, an unknown status, a status without its
            // subject, a status with an error, and neither a status nor an error.
            _harness.Server.Api = (_, _) => Task.FromResult(FakeIdentityServer.JsonAnswer(HttpStatusCode.OK, new JsonObject
            {
                ["results"] = new JsonArray([.. Enumerable.Range(0, answered).Select(_ => (JsonNode)Result(email, sub, status, error))]),
            }));

            InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => _harness.Client.InviteAsync([new IdentityInvitation("a@acme.com", null, null, null, null)], Cancellation));

            // The message never quotes an address or a subject.
            Assert.DoesNotContain("@acme.com", exception.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("s-a", exception.Message, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData("s-1", "Sent", 0)]
        [InlineData("s-1", "Sent", 2)]
        [InlineData("s-2", "Sent", 1)]
        [InlineData("s-1", "Opened", 1)]
        [InlineData("s-1", null, 1)]
        public async Task A_delivery_answer_that_cannot_be_recorded_is_refused(string sub, string? state, int answered)
        {
            _harness.Server.Api = (_, _) => Task.FromResult(FakeIdentityServer.JsonAnswer(HttpStatusCode.OK, new JsonObject
            {
                ["results"] = new JsonArray([.. Enumerable.Range(0, answered).Select(_ => (JsonNode)new JsonObject
                {
                    ["sub"] = sub,
                    ["state"] = state,
                    ["expectsDeliveryEvents"] = false,
                })]),
            }));

            _ = await Assert.ThrowsAsync<InvalidOperationException>(() => _harness.Client.GetInvitationDeliveryAsync(["s-1"], Cancellation));
        }

        [Theory]
        [InlineData("{}")]
        [InlineData(/*lang=json,strict*/ """{"results":null}""")]
        [InlineData("[1, 2]")]
        [InlineData("not json")]
        [InlineData("")]
        public async Task An_unreadable_body_is_refused(string body)
        {
            _harness.Server.Api = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
            });

            _ = await Assert.ThrowsAsync<InvalidOperationException>(() => _harness.Client.InviteAsync(IdentityClientHarness.Invitations(1), Cancellation));
        }

        // --- Refusals ---

        [Theory]
        [InlineData(HttpStatusCode.BadRequest)]
        [InlineData(HttpStatusCode.Forbidden)]
        [InlineData(HttpStatusCode.NotFound)]
        [InlineData(HttpStatusCode.Conflict)]
        [InlineData(HttpStatusCode.TooManyRequests)]
        public async Task A_4xx_other_than_401_is_a_refusal_and_is_not_retried(HttpStatusCode status)
        {
            _harness.Server.Api = (_, _) => Task.FromResult(new HttpResponseMessage(status));

            InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => _harness.Client.GetInvitationDeliveryAsync(["s-1"], Cancellation));

            Assert.Contains(((int)status).ToString(System.Globalization.CultureInfo.InvariantCulture), exception.Message, StringComparison.Ordinal);
            Assert.Single(_harness.Server.ApiRequests);
            Assert.Single(_harness.Server.TokenRequests);
        }

        // --- Service accounts ---

        [Fact]
        public async Task Creating_a_service_account_posts_its_name_and_the_public_origin_and_answers_its_credentials()
        {
            IdentityServiceAccount account = await _harness.Client.CreateServiceAccountAsync("Nightly import", Cancellation);

            Assert.Equal(new IdentityServiceAccount("acme-sa-1", "sa-secret-3b9e"), account);
            RecordedRequest request = Assert.Single(_harness.Server.ApiRequests);
            Assert.Equal((HttpMethod.Post, "https://acme.app.tellma.com/id/api/identity/v1/service-accounts"), (request.Method, request.Uri.AbsoluteUri));
            Assert.Equal(/*lang=json,strict*/ """{"displayName":"Nightly import","resources":["https://acme.app.tellma.com"]}""", request.Body);
        }

        [Fact]
        public void A_service_account_prints_without_its_secret()
        {
            string text = new IdentityServiceAccount("acme-sa-1", "sa-secret-3b9e").ToString();

            Assert.Contains("acme-sa-1", text, StringComparison.Ordinal);
            Assert.DoesNotContain("sa-secret-3b9e", text, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData(/*lang=json,strict*/ """{"clientId":"acme-sa-1"}""")]
        [InlineData(/*lang=json,strict*/ """{"clientId":" ","clientSecret":"x"}""")]
        public async Task A_created_account_without_its_credentials_is_refused(string body)
        {
            _harness.Server.Api = (_, _) => Task.FromResult(FakeIdentityServer.JsonAnswer(HttpStatusCode.Created, JsonNode.Parse(body)!));

            _ = await Assert.ThrowsAsync<InvalidOperationException>(() => _harness.Client.CreateServiceAccountAsync("Nightly import", Cancellation));
        }

        [Theory]
        [InlineData(HttpStatusCode.NoContent)]
        [InlineData(HttpStatusCode.NotFound)]
        public async Task Deleting_a_service_account_succeeds_on_204_and_on_404(HttpStatusCode status)
        {
            _harness.Server.Api = (_, _) => Task.FromResult(new HttpResponseMessage(status));

            await _harness.Client.DeleteServiceAccountAsync("acme sa/1", Cancellation);

            RecordedRequest request = Assert.Single(_harness.Server.ApiRequests);
            Assert.Equal(HttpMethod.Delete, request.Method);
            Assert.Equal("https://acme.app.tellma.com/id/api/identity/v1/service-accounts/acme%20sa%2F1", request.Uri.AbsoluteUri);
            AssertServiceAccountCall(UsersTelemetryNames.OutcomeOk);
        }

        [Fact]
        public async Task A_delete_refused_with_another_4xx_is_a_refusal()
        {
            _harness.Server.Api = static (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));

            _ = await Assert.ThrowsAsync<InvalidOperationException>(() => _harness.Client.DeleteServiceAccountAsync("acme-sa-1", Cancellation));
        }

        /// <summary>Asserts the single <c>IdentityCall</c> log record so far is a service-account call with the outcome.</summary>
        /// <param name="outcome">The expected outcome.</param>
        private void AssertServiceAccountCall(string outcome)
        {
            FakeLogRecord call = Assert.Single(_harness.Calls);
            Assert.Equal(
                (UsersTelemetryNames.EndpointServiceAccount, outcome),
                (call.GetStructuredStateValue("Endpoint"), call.GetStructuredStateValue("Outcome")));
        }

        /// <summary>One result of the invite's answer.</summary>
        /// <param name="email">The echoed address.</param>
        /// <param name="sub">The subject.</param>
        /// <param name="status">The status.</param>
        /// <param name="error">The refusal.</param>
        /// <returns>The result's JSON.</returns>
        private static JsonObject Result(string email, string? sub, string? status, string? error)
        {
            return new JsonObject { ["email"] = email, ["sub"] = sub, ["status"] = status, ["error"] = error };
        }
    }
}
