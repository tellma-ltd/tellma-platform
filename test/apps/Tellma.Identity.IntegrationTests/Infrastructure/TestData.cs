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

        /// <summary>Creates an active, email-confirmed user directly in the store.</summary>
        /// <param name="factory">The host under test.</param>
        /// <param name="email">The user's email.</param>
        /// <returns>The created user.</returns>
        public static async Task<TellmaIdentityUser> CreateActiveUserAsync(StandaloneFactory factory, string email)
        {
            using IServiceScope scope = factory.Services.CreateScope();
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
