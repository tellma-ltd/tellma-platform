// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text.Json;
using Tellma.Identity.IntegrationTests.Infrastructure;

namespace Tellma.Identity.IntegrationTests.Flows
{
    /// <summary>
    ///     The device authorization grant end-to-end: a headless client obtains device and user
    ///     codes, the user approves the code in a browser, and the device polls the token endpoint
    ///     until it receives tokens.
    /// </summary>
    [Collection(SqlServerCollectionDefinition.Name)]
    [Trait("Category", "Integration")]
    public sealed class DeviceFlowTests(SqlServerFixture fixture)
    {
        private static readonly Dictionary<string, string?> CliSeed = new()
        {
            ["TellmaIdentity:Seed:Clients:0:ClientId"] = "tellma-cli",
            ["TellmaIdentity:Seed:Clients:0:DisplayName"] = "Tellma CLI",
            ["TellmaIdentity:Seed:Clients:0:Kind"] = "Cli",
            ["TellmaIdentity:Seed:Clients:0:RedirectUris:0"] = "http://127.0.0.1/callback",
        };

        [Fact]
        public async Task Device_grant_completes_after_the_user_approves_the_code()
        {
            using StandaloneFactory factory = await DatabaseBackedFactory.CreateStandaloneAsync(fixture, "iddev", CliSeed);
            await TestData.CreateActiveUserAsync(factory, "frank@example.com");

            using OidcFlowClient flow = new(factory);

            // 1. The device requests device + user codes.
            using JsonDocument device = await PostDeviceAuthorizationAsync(factory);
            string deviceCode = device.RootElement.GetProperty("device_code").GetString()!;
            string userCode = device.RootElement.GetProperty("user_code").GetString()!;
            Assert.False(string.IsNullOrEmpty(device.RootElement.GetProperty("verification_uri").GetString()));

            // 2. Polling before approval yields authorization_pending.
            string? pending = await PollExpectingErrorAsync(factory, deviceCode);
            Assert.Equal("authorization_pending", pending);

            // 3. The user signs in and approves the code in the browser.
            await ApproveUserCodeAsync(flow, userCode, "frank@example.com");

            // 4. The device's next poll returns tokens.
            using HttpClient client = factory.CreateClient();
            using HttpResponseMessage tokenResponse = await client.PostAsync(
                new Uri("/connect/token", UriKind.Relative),
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
                    ["client_id"] = "tellma-cli",
                    ["device_code"] = deviceCode,
                }),
                TestContext.Current.CancellationToken);

            string body = await tokenResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            Assert.True(tokenResponse.IsSuccessStatusCode, body);
            using var tokens = JsonDocument.Parse(body);
            string accessToken = tokens.RootElement.GetProperty("access_token").GetString()!;
            using JsonDocument payload = ClientCredentialsTests.DecodeJwtPayload(accessToken);
            Assert.Equal("frank@example.com", payload.RootElement.GetProperty("email").GetString());
        }

        /// <summary>Posts a device-authorization request for the seeded CLI client.</summary>
        private static async Task<JsonDocument> PostDeviceAuthorizationAsync(StandaloneFactory factory)
        {
            using HttpClient client = factory.CreateClient();
            using HttpResponseMessage response = await client.PostAsync(
                new Uri("/connect/device", UriKind.Relative),
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"] = "tellma-cli",
                    ["scope"] = "openid offline_access",
                }),
                TestContext.Current.CancellationToken);

            string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            Assert.True(response.IsSuccessStatusCode, body);
            return JsonDocument.Parse(body);
        }

        /// <summary>Polls the token endpoint expecting a still-pending error.</summary>
        private static async Task<string?> PollExpectingErrorAsync(StandaloneFactory factory, string deviceCode)
        {
            using HttpClient client = factory.CreateClient();
            using HttpResponseMessage response = await client.PostAsync(
                new Uri("/connect/token", UriKind.Relative),
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
                    ["client_id"] = "tellma-cli",
                    ["device_code"] = deviceCode,
                }),
                TestContext.Current.CancellationToken);

            using var document = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            return document.RootElement.GetProperty("error").GetString();
        }

        /// <summary>Signs the user in and approves the device user code in the browser.</summary>
        private static async Task ApproveUserCodeAsync(OidcFlowClient flow, string userCode, string email)
        {
            // Opening the verification URI requires a login; the device page is [Authorize].
            string verifyUrl = "/connect/verify?user_code=" + Uri.EscapeDataString(userCode);
            string loginUrl;
            using (HttpResponseMessage challenge = await flow.Browser.GetAsync(
                new Uri(verifyUrl, UriKind.Relative), TestContext.Current.CancellationToken))
            {
                loginUrl = challenge.Headers.Location!.ToString();
            }

            string afterLogin = await flow.SignInWithEmailCodeAsync(email, loginUrl);

            // Load the confirmation page and post the approval.
            using HttpResponseMessage verifyPage = await flow.Browser.GetAsync(
                new Uri(afterLogin, UriKind.RelativeOrAbsolute), TestContext.Current.CancellationToken);
            (string action, Dictionary<string, string> fields) = await OidcFlowClient.ParseFormAsync(verifyPage);
            fields["submit.Accept"] = "yes";
            fields.Remove("submit.Deny");

            using HttpResponseMessage approved = await flow.PostFormAsync(action, fields);
            Assert.Equal(System.Net.HttpStatusCode.Redirect, approved.StatusCode);
        }
    }
}
