// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tellma.Identity.Data;
using Tellma.Identity.Data.Entities;
using Tellma.Identity.IntegrationTests.Infrastructure;
using Tellma.Identity.Services.Provisioning;
using Tellma.Identity.Services.Sessions;

namespace Tellma.Identity.IntegrationTests.Flows
{
    /// <summary>
    ///     Nothing but an explicit sign-out ends a session row, so without a sweep a lapsed
    ///     session stays "active" forever: listed on the user's own devices page as somewhere
    ///     they are still signed in, and fanned out to on every "sign out everywhere". These
    ///     cover the sweep's two passes, and the activity tracking that keeps it from retiring a
    ///     session the user is still using.
    /// </summary>
    [Collection(SqlServerCollectionDefinition.Name)]
    [Trait("Category", "Integration")]
    public sealed class SessionPruneTests(SqlServerFixture fixture)
    {
        [Fact]
        public async Task The_sweep_ends_lapsed_sessions_and_spares_live_ones()
        {
            using StandaloneFactory factory = await DatabaseBackedFactory.CreateStandaloneAsync(fixture, "idprune1");
            TellmaIdentityUser user = await TestData.CreateActiveUserAsync(factory, "prune1@example.com");

            using IServiceScope scope = factory.Services.CreateScope();
            ISessionRegistry registry = scope.ServiceProvider.GetRequiredService<ISessionRegistry>();

            await registry.UpsertSessionAsync("lapsed1", user.Id, "agent", "203.0.113.7", TestContext.Current.CancellationToken);
            await registry.UpsertSessionAsync("live1", user.Id, "agent", "203.0.113.7", TestContext.Current.CancellationToken);

            // Backdate one row so the cutoff separates the two. The cutoff is a parameter rather
            // than a clock the test has to move, so the two passes can be aimed exactly.
            DateTimeOffset cutoff = DateTimeOffset.UtcNow.AddHours(-1);
            await SetLastSeenAsync(factory, "lapsed1", cutoff.AddMinutes(-5));

            SessionPruneResult result = await registry.PruneAsync(
                idleSince: cutoff, terminatedSince: cutoff, TestContext.Current.CancellationToken);

            Assert.Equal(1, result.Expired);

            IReadOnlyList<IdentitySession> active =
                await registry.GetActiveSessionsAsync(user.Id, TestContext.Current.CancellationToken);
            Assert.Equal(["live1"], active.Select(static session => session.Sid));
        }

        [Fact]
        public async Task Retention_removes_long_terminated_rows_and_their_client_registrations()
        {
            using StandaloneFactory factory = await DatabaseBackedFactory.CreateStandaloneAsync(fixture, "idprune2");
            TellmaIdentityUser user = await TestData.CreateActiveUserAsync(factory, "prune2@example.com");
            DistributionClientCredentials distribution = await TestData.ProvisionDistributionAsync(factory);

            using IServiceScope scope = factory.Services.CreateScope();
            ISessionRegistry registry = scope.ServiceProvider.GetRequiredService<ISessionRegistry>();

            foreach (string sid in (string[])["old", "recent"])
            {
                await registry.UpsertSessionAsync(sid, user.Id, "agent", "203.0.113.7", TestContext.Current.CancellationToken);
                await registry.RegisterClientAsync(sid, distribution.BffClientId, null, TestContext.Current.CancellationToken);
                await registry.TerminateAsync(sid, TestContext.Current.CancellationToken);
            }

            DateTimeOffset cutoff = DateTimeOffset.UtcNow.AddHours(-1);
            await SetTerminatedAsync(factory, "old", cutoff.AddMinutes(-5));

            SessionPruneResult result = await registry.PruneAsync(
                idleSince: cutoff, terminatedSince: cutoff, TestContext.Current.CancellationToken);

            Assert.Equal(1, result.Removed);

            // The row is gone, and so is its client registration — a leftover would keep pointing
            // at a session that no longer exists.
            using IServiceScope reader = factory.Services.CreateScope();
            TellmaIdentityDbContext context = reader.ServiceProvider.GetRequiredService<TellmaIdentityDbContext>();
            Assert.Equal(
                ["recent"],
                await context.Set<IdentitySession>().Select(static s => s.Sid).OrderBy(static sid => sid)
                    .ToListAsync(TestContext.Current.CancellationToken));
            Assert.Equal(
                ["recent"],
                await context.Set<IdentitySessionClient>().Select(static c => c.Sid).OrderBy(static sid => sid)
                    .ToListAsync(TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task Recording_activity_does_not_revive_a_terminated_session()
        {
            using StandaloneFactory factory = await DatabaseBackedFactory.CreateStandaloneAsync(fixture, "idprune3");
            TellmaIdentityUser user = await TestData.CreateActiveUserAsync(factory, "prune3@example.com");

            using IServiceScope scope = factory.Services.CreateScope();
            ISessionRegistry registry = scope.ServiceProvider.GetRequiredService<ISessionRegistry>();

            await registry.UpsertSessionAsync("gone", user.Id, "agent", "203.0.113.7", TestContext.Current.CancellationToken);
            await registry.TerminateAsync("gone", TestContext.Current.CancellationToken);

            // A request still holding the signed-out cookie must not put the session back.
            await registry.TouchAsync("gone", TestContext.Current.CancellationToken);

            Assert.Empty(await registry.GetActiveSessionsAsync(user.Id, TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task Browsing_the_account_pages_keeps_a_session_out_of_the_sweep()
        {
            using StandaloneFactory factory = await DatabaseBackedFactory.CreateStandaloneAsync(fixture, "idprune4");

            // Zero makes every request revalidate the stamp, which is what marks the session as
            // seen. At the production interval this same path runs, five minutes apart.
            factory.ServiceOverrides.Add(static services =>
                services.Configure<SecurityStampValidatorOptions>(static validator =>
                    validator.ValidationInterval = TimeSpan.Zero));

            TellmaIdentityUser user = await TestData.CreateActiveUserAsync(factory, "prune4@example.com");

            using OidcFlowClient flow = new(factory);
            await flow.SignInWithEmailCodeAsync("prune4@example.com", "/Identity/Account/Login");

            string sid = await SingleSidAsync(factory, user.Id);

            // Backdate the session past the sweep's reach, then visit an account page — the only
            // kind of request that renews the cookie without issuing any token. Without the
            // activity tracking, LastSeenUtc would still read as the backdated value and the
            // sweep would retire a session the user is signed into.
            DateTimeOffset stale = DateTimeOffset.UtcNow.AddDays(-30);
            await SetLastSeenAsync(factory, sid, stale);

            using HttpResponseMessage page = await flow.Browser.GetAsync(
                new Uri("/Identity/Manage/Index", UriKind.Relative), TestContext.Current.CancellationToken);
            Assert.True(page.IsSuccessStatusCode, $"Account page failed: {page.StatusCode}");

            using IServiceScope scope = factory.Services.CreateScope();
            ISessionRegistry registry = scope.ServiceProvider.GetRequiredService<ISessionRegistry>();
            SessionPruneResult result = await registry.PruneAsync(
                idleSince: DateTimeOffset.UtcNow.AddDays(-14),
                terminatedSince: DateTimeOffset.UtcNow.AddDays(-90),
                TestContext.Current.CancellationToken);

            Assert.Equal(0, result.Expired);
            Assert.Single(await registry.GetActiveSessionsAsync(user.Id, TestContext.Current.CancellationToken));
        }

        /// <summary>Reads the single session the user has, failing the test when there is not exactly one.</summary>
        private static async Task<string> SingleSidAsync(StandaloneFactory factory, string userId)
        {
            using IServiceScope scope = factory.Services.CreateScope();
            TellmaIdentityDbContext context = scope.ServiceProvider.GetRequiredService<TellmaIdentityDbContext>();
            return Assert.Single(
                await context.Set<IdentitySession>()
                    .Where(session => session.UserId == userId)
                    .Select(static session => session.Sid)
                    .ToListAsync(TestContext.Current.CancellationToken));
        }

        /// <summary>Backdates a session's last-seen stamp, which no production path can do.</summary>
        private static async Task SetLastSeenAsync(StandaloneFactory factory, string sid, DateTimeOffset lastSeen)
        {
            using IServiceScope scope = factory.Services.CreateScope();
            TellmaIdentityDbContext context = scope.ServiceProvider.GetRequiredService<TellmaIdentityDbContext>();
            await context.Set<IdentitySession>()
                .Where(session => session.Sid == sid)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(static session => session.LastSeenUtc, lastSeen),
                    TestContext.Current.CancellationToken);
        }

        /// <summary>Backdates a session's termination stamp, which no production path can do.</summary>
        private static async Task SetTerminatedAsync(StandaloneFactory factory, string sid, DateTimeOffset terminated)
        {
            using IServiceScope scope = factory.Services.CreateScope();
            TellmaIdentityDbContext context = scope.ServiceProvider.GetRequiredService<TellmaIdentityDbContext>();
            await context.Set<IdentitySession>()
                .Where(session => session.Sid == sid)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(
                        static session => session.TerminatedUtc, (DateTimeOffset?)terminated),
                    TestContext.Current.CancellationToken);
        }
    }
}
