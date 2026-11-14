// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Tellma.Identity.Data;
using Tellma.Identity.Services.Provisioning;

namespace Tellma.Identity.IntegrationTests.Infrastructure
{
    /// <summary>Common data setup used across the flow suites.</summary>
    public static class TestData
    {
        /// <summary>Provisions a distribution (BFF + backend clients) through the engine's API.</summary>
        /// <param name="factory">The host under test.</param>
        /// <param name="slug">The distribution slug.</param>
        /// <param name="backchannelLogoutUri">The BFF's back-channel logout endpoint, when tested.</param>
        /// <param name="allowTokenExchange">Grant the backend client token exchange.</param>
        /// <returns>The provisioned credentials.</returns>
        public static async Task<DistributionClientCredentials> ProvisionDistributionAsync(
            StandaloneFactory factory, string slug = "acme", Uri? backchannelLogoutUri = null, bool allowTokenExchange = false)
        {
            using IServiceScope scope = factory.Services.CreateScope();
            IClientProvisioningService provisioning =
                scope.ServiceProvider.GetRequiredService<IClientProvisioningService>();
            return await provisioning.CreateDistributionAsync(
                slug,
                new Uri($"https://{slug}.app.tellma.com"),
                backchannelLogoutUri,
                allowTokenExchange,
                TestContext.Current.CancellationToken);
        }

        /// <summary>
        ///     Provisions a distribution and obtains a client-credentials token for its backend
        ///     client carrying the <c>tellma_identity</c> scope, the token the management API takes.
        /// </summary>
        /// <param name="factory">The host under test.</param>
        /// <param name="slug">The distribution slug.</param>
        /// <returns>The access token.</returns>
        public static async Task<string> GetIdentityScopeTokenAsync(StandaloneFactory factory, string slug = "acme")
        {
            DistributionClientCredentials distribution = await ProvisionDistributionAsync(factory, slug);

            using HttpClient client = factory.CreateClient();
            using HttpResponseMessage response = await client.PostAsync(
                new Uri("/connect/token", UriKind.Relative),
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "client_credentials",
                    ["client_id"] = distribution.ServiceClientId,
                    ["client_secret"] = distribution.ServiceClientSecret,
                    ["scope"] = "tellma_identity",
                }),
                TestContext.Current.CancellationToken);

            string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            Assert.True(response.IsSuccessStatusCode, body);
            using var document = System.Text.Json.JsonDocument.Parse(body);
            return document.RootElement.GetProperty("access_token").GetString()!;
        }

        /// <summary>Gives a user a password, so that they count as holding a credential.</summary>
        /// <param name="factory">The host under test.</param>
        /// <param name="user">The user.</param>
        /// <returns>A task that completes when the password is stored.</returns>
        public static async Task AddPasswordAsync(StandaloneFactory factory, TellmaIdentityUser user)
        {
            using IServiceScope scope = factory.Services.CreateScope();
            UserManager<TellmaIdentityUser> userManager =
                scope.ServiceProvider.GetRequiredService<UserManager<TellmaIdentityUser>>();

            TellmaIdentityUser tracked = (await userManager.FindByIdAsync(user.Id))!;
            IdentityResult result = await userManager.AddPasswordAsync(tracked, "correct horse battery staple");

            Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(static e => e.Description)));
        }

        /// <summary>Creates an active, email-confirmed user directly in the store.</summary>
        /// <param name="factory">The host under test.</param>
        /// <param name="email">The user's email.</param>
        /// <returns>The created user.</returns>
        public static Task<TellmaIdentityUser> CreateActiveUserAsync(StandaloneFactory factory, string email)
        {
            return CreateActiveUserAsync(factory.Services, email);
        }

        /// <summary>Creates an active, email-confirmed user directly in a distribution host's store.</summary>
        /// <param name="factory">The host under test.</param>
        /// <param name="email">The user's email.</param>
        /// <returns>The created user.</returns>
        public static Task<TellmaIdentityUser> CreateActiveUserAsync(InProcFactory factory, string email)
        {
            return CreateActiveUserAsync(factory.Services, email);
        }

        /// <summary>Creates an active, email-confirmed user directly in the store of a host's services.</summary>
        /// <param name="services">The services of the host under test.</param>
        /// <param name="email">The user's email.</param>
        /// <returns>The created user.</returns>
        private static async Task<TellmaIdentityUser> CreateActiveUserAsync(IServiceProvider services, string email)
        {
            using IServiceScope scope = services.CreateScope();
            UserManager<TellmaIdentityUser> userManager =
                scope.ServiceProvider.GetRequiredService<UserManager<TellmaIdentityUser>>();

            TellmaIdentityUser user = new()
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                DisplayName = email.Split('@')[0],
                Locale = "en",
                LifecycleState = UserLifecycleState.Active,
                CreatedUtc = DateTimeOffset.UtcNow,
            };

            IdentityResult result = await userManager.CreateAsync(user);
            Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(static e => e.Description)));
            return user;
        }

        /// <summary>Records an external login against a user, the way a completed link would.</summary>
        /// <param name="factory">The host under test.</param>
        /// <param name="email">The user the identity belongs to.</param>
        /// <param name="provider">The provider scheme.</param>
        /// <param name="key">The provider's stable subject for this identity.</param>
        /// <param name="account">The address recorded beside the link.</param>
        /// <returns>A task that completes when the link exists.</returns>
        public static async Task AddExternalLoginAsync(
            StandaloneFactory factory, string email, string provider, string key, string? account = null)
        {
            using IServiceScope scope = factory.Services.CreateScope();
            UserManager<TellmaIdentityUser> userManager =
                scope.ServiceProvider.GetRequiredService<UserManager<TellmaIdentityUser>>();

            TellmaIdentityUser user = (await userManager.FindByEmailAsync(email))!;
            IdentityResult result = await userManager.AddLoginAsync(
                user, new UserLoginInfo(provider, key, account ?? email));

            Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(static e => e.Description)));
        }

        /// <summary>Moves a user to the given lifecycle state directly in the store.</summary>
        /// <param name="factory">The host under test.</param>
        /// <param name="user">The user to move.</param>
        /// <param name="state">The state to move them to.</param>
        /// <returns>A task that completes when the state is stored.</returns>
        public static async Task SetLifecycleStateAsync(
            StandaloneFactory factory, TellmaIdentityUser user, UserLifecycleState state)
        {
            using IServiceScope scope = factory.Services.CreateScope();
            UserManager<TellmaIdentityUser> userManager =
                scope.ServiceProvider.GetRequiredService<UserManager<TellmaIdentityUser>>();

            TellmaIdentityUser tracked = (await userManager.FindByIdAsync(user.Id))!;
            tracked.LifecycleState = state;
            IdentityResult result = await userManager.UpdateAsync(tracked);

            Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(static e => e.Description)));
        }
    }
}
