// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Globalization;
using System.Security.Claims;
using Tellma.Core.AspNetCore;
using Tellma.Core.AspNetCore.Hosting;
using Tellma.Core.AspNetCore.Observability;
using Tellma.Core.AspNetCore.Sessions;
using Tellma.Core.AspNetCore.Tenancy;
using Tellma.Core.IntegrationTests.Infrastructure;

namespace Tellma.Core.IntegrationTests.Catalog
{
    /// <summary>
    ///     The session store against a real catalog: the rows the statements write, the renewal the
    ///     tokens version guards, the refresh's compare-and-set across two instances, the termination
    ///     deletes that output the handles, the sweep, and the reconciliation's application lock.
    /// </summary>
    [Trait("Category", "Integration")]
    public class SessionStoreTests(SqlServerFixture sql)
    {
        private static CancellationToken Token => TestContext.Current.CancellationToken;

        [Fact]
        public async Task A_stored_session_reads_back_with_its_handle_and_renews_and_removes()
        {
            await using CatalogHost host = await CatalogHost.CreateAsync(sql, "sessions_crud");
            Instance instance = new(host);
            DateTimeOffset expiresAt = Whole(DateTimeOffset.UtcNow.AddDays(7));

            string key = await instance.Store.StoreAsync(Ticket("sub-1", "sid-1", expiresAt), httpContext: null, Token);

            // --- The row: the handle derived from the key, the names, version 0, the expiry ---
            (string handle, string subject, string? sid, int version, DateTimeOffset rowExpiry) = await ReadRowAsync(host, key);
            Assert.Equal(SessionKeys.HandleOf(key), handle);
            Assert.Equal("sub-1", subject);
            Assert.Equal("sid-1", sid);
            Assert.Equal(0, version);
            Assert.Equal(expiresAt, rowExpiry);

            // --- Read back through another instance, so from the row rather than a cache ---
            AuthenticationTicket? ticket = await new Instance(host).Store.RetrieveAsync(key, httpContext: null, Token);
            Assert.NotNull(ticket);
            Assert.Equal("sub-1", ticket.Principal.FindFirst("sub")?.Value);
            Assert.Equal(handle, ticket.Properties.Items[TellmaAuthentication.SessionHandleProperty]);

            // --- A renewal moves the expiry; a removal deletes the row ---
            DateTimeOffset renewed = expiresAt.AddDays(1);
            ticket.Properties.ExpiresUtc = renewed;
            await instance.Store.RenewAsync(key, ticket, httpContext: null, Token);
            Assert.Equal(renewed, (await ReadRowAsync(host, key)).ExpiresAt);

            await instance.Store.RemoveAsync(key, httpContext: null, Token);
            Assert.Null(await new Instance(host).Store.RetrieveAsync(key, httpContext: null, Token));
            Assert.Equal(0, await CountAsync(host));
        }

        [Fact]
        public async Task A_renewal_carrying_an_older_tokens_version_writes_nothing()
        {
            await using CatalogHost host = await CatalogHost.CreateAsync(sql, "sessions_renew_guard");
            SqlSessionRows rows = new(host.Connections);
            const string Key = "renewal-guard-key-aaaaaaaaaaaaaaaaaaaaaaaaaaa";
            DateTimeOffset expiresAt = Whole(DateTimeOffset.UtcNow.AddDays(7));
            await rows.InsertAsync(Key, SessionKeys.HandleOf(Key), "sub-1", "sid-1", [1], expiresAt, Token);

            // A refresh moves the row to version 1.
            Assert.Equal(1, await rows.ReplaceAsync(Key, [2], expiresAt, expectedVersion: 0, Token));

            // A renewal read at version 0 writes nothing, and a second refresh from 0 loses its CAS.
            Assert.False(await rows.RenewAsync(Key, [3], expiresAt.AddDays(1), tokensVersion: 0, Token));
            Assert.Null(await rows.ReplaceAsync(Key, [4], expiresAt, expectedVersion: 0, Token));
            SessionRow? row = await rows.ReadAsync(Key, Token);
            Assert.NotNull(row);
            Assert.Equal([2], row.Ticket);
            Assert.Equal(1, row.TokensVersion);
            Assert.Equal(expiresAt, row.ExpiresAt);

            // At the row's version, it writes.
            Assert.True(await rows.RenewAsync(Key, [5], expiresAt.AddDays(1), tokensVersion: 1, Token));
        }

        [Fact]
        public async Task Concurrent_refreshers_on_one_instance_redeem_once()
        {
            await using CatalogHost host = await CatalogHost.CreateAsync(sql, "sessions_refresh_one");
            GatedRedeemer redeemer = new();
            Instance instance = new(host, redeemer);
            string key = await instance.Store.StoreAsync(Ticket("sub-1", "sid-1", Whole(DateTimeOffset.UtcNow.AddDays(7))), httpContext: null, Token);
            RefreshRequest request = Request(key);

            Task<RefreshOutcome>[] refreshes = [.. Enumerable.Range(0, 5).Select(_ => instance.Refresher.RefreshAsync(request))];
            await redeemer.Entered.Task.WaitAsync(TimeSpan.FromSeconds(30), Token);
            redeemer.Release();
            RefreshOutcome[] outcomes = await Task.WhenAll(refreshes);

            Assert.Equal(1, redeemer.Redemptions);
            Assert.All(outcomes, static outcome => Assert.Equal(RefreshOutcomeKind.Refreshed, outcome.Kind));
            Assert.Equal(1, (await ReadRowAsync(host, key)).Version);
        }

        [Fact]
        public async Task Two_instances_refreshing_at_once_write_one_ticket_and_the_loser_adopts_it()
        {
            await using CatalogHost host = await CatalogHost.CreateAsync(sql, "sessions_refresh_cas");
            GatedRedeemer first = new();
            GatedRedeemer second = new();
            Instance one = new(host, first);
            Instance two = new(host, second);
            string key = await one.Store.StoreAsync(Ticket("sub-1", "sid-1", Whole(DateTimeOffset.UtcNow.AddDays(7))), httpContext: null, Token);
            RefreshRequest request = Request(key);

            // --- Both read version 0 and redeem; the first to write wins the compare-and-set ---
            Task<RefreshOutcome> winning = one.Refresher.RefreshAsync(request);
            Task<RefreshOutcome> losing = two.Refresher.RefreshAsync(request);
            await Task.WhenAll(first.Entered.Task, second.Entered.Task).WaitAsync(TimeSpan.FromSeconds(30), Token);
            first.Release();
            RefreshOutcome won = await winning;
            second.Release();
            RefreshOutcome lost = await losing;

            Assert.Equal(RefreshOutcomeKind.Refreshed, won.Kind);
            Assert.Equal(RefreshOutcomeKind.Adopted, lost.Kind);
            Assert.Equal(1, (await ReadRowAsync(host, key)).Version);
            Assert.Equal(first.LastAccessToken, lost.Session!.Ticket.Properties.GetTokenValue("access_token"));

            // --- A third instance, starting after the write, adopts without redeeming ---
            GatedRedeemer third = new();
            third.Release();
            RefreshOutcome adopted = await new Instance(host, third).Refresher.RefreshAsync(request);
            Assert.Equal(RefreshOutcomeKind.Adopted, adopted.Kind);
            Assert.Equal(0, third.Redemptions);
        }

        [Fact]
        public async Task The_termination_deletes_output_the_handles_of_what_they_deleted()
        {
            await using CatalogHost host = await CatalogHost.CreateAsync(sql, "sessions_revoke");
            SqlSessionRows rows = new(host.Connections);
            DateTimeOffset expiresAt = Whole(DateTimeOffset.UtcNow.AddDays(7));
            string[] keys = [.. Enumerable.Range(0, 4).Select(static index => $"revoke-key-{index}-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")];
            await rows.InsertAsync(keys[0], SessionKeys.HandleOf(keys[0]), "sub-1", "sid-1", [1], expiresAt, Token);
            await rows.InsertAsync(keys[1], SessionKeys.HandleOf(keys[1]), "sub-1", "sid-1", [1], expiresAt, Token);
            await rows.InsertAsync(keys[2], SessionKeys.HandleOf(keys[2]), "sub-1", "sid-2", [1], expiresAt, Token);
            await rows.InsertAsync(keys[3], SessionKeys.HandleOf(keys[3]), "sub-2", "sid-3", [1], expiresAt, Token);

            IReadOnlyList<EndedSession> bySid = await rows.DeleteBySidAsync("sid-1", Token);
            Assert.Equal(
                [.. keys[..2].Select(static key => new EndedSession(key, SessionKeys.HandleOf(key), "sub-1")).OrderBy(static ended => ended.Key, StringComparer.Ordinal)],
                [.. bySid.OrderBy(static ended => ended.Key, StringComparer.Ordinal)]);

            IReadOnlyList<EndedSession> bySubject = await rows.DeleteBySubjectAsync("sub-1", Token);
            Assert.Equal([new EndedSession(keys[2], SessionKeys.HandleOf(keys[2]), "sub-1")], bySubject);
            Assert.Empty(await rows.DeleteBySidAsync("sid-1", Token));
            Assert.Equal(1, await CountAsync(host));
        }

        [Fact]
        public async Task The_sweep_deletes_only_the_rows_expired_for_a_day()
        {
            await using CatalogHost host = await CatalogHost.CreateAsync(sql, "sessions_sweep");
            SqlSessionRows rows = new(host.Connections);
            DateTimeOffset now = DateTimeOffset.UtcNow;
            await rows.InsertAsync("sweep-long-expired-aaaaaaaaaaaaaaaaaaaaaaaaaa", "long-expired-handle-aa", "sub-1", null, [1], now.AddDays(-2), Token);
            await rows.InsertAsync("sweep-just-expired-aaaaaaaaaaaaaaaaaaaaaaaaaa", "just-expired-handle-aa", "sub-1", null, [1], now.AddHours(-23), Token);
            await rows.InsertAsync("sweep-live-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "live-handle-aaaaaaaaaa", "sub-1", null, [1], now.AddDays(1), Token);

            // The expired row no longer reads, though the sweep keeps it for its day.
            Assert.Null(await rows.ReadAsync("sweep-just-expired-aaaaaaaaaaaaaaaaaaaaaaaaaa", Token));

            Assert.Equal(1, await rows.SweepBatchAsync(Token));
            Assert.Equal(0, await rows.SweepBatchAsync(Token));
            Assert.Equal(2, await CountAsync(host));
            Assert.Null(await rows.ReadAsync("sweep-long-expired-aaaaaaaaaaaaaaaaaaaaaaaaaa", Token));
        }

        [Fact]
        public async Task The_reconciliation_s_lock_is_held_by_one_instance_at_a_time()
        {
            await using CatalogHost host = await CatalogHost.CreateAsync(sql, "sessions_applock");
            SqlMembershipReconcileStore one = new(host.Connections, host.Directory);
            SqlMembershipReconcileStore two = new(host.Connections, host.Directory);

            IAsyncDisposable? lease = await one.TryLeadAsync(Token);
            Assert.NotNull(lease);
            Assert.Null(await two.TryLeadAsync(Token));

            await lease.DisposeAsync();
            IAsyncDisposable? next = await two.TryLeadAsync(Token);
            Assert.NotNull(next);
            await next.DisposeAsync();
        }

        // ------------------------------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------------------------------

        /// <summary>A time at whole seconds, as the session's expiries are.</summary>
        /// <param name="time">The time.</param>
        /// <returns>The time without its fraction of a second.</returns>
        private static DateTimeOffset Whole(DateTimeOffset time)
        {
            return DateTimeOffset.FromUnixTimeSeconds(time.ToUnixTimeSeconds());
        }

        /// <summary>A signed-in session's ticket, its access token near expiry.</summary>
        /// <param name="subject">The subject.</param>
        /// <param name="sid">The identity server's session.</param>
        /// <param name="expiresAt">The session's expiry.</param>
        /// <returns>The ticket.</returns>
        private static AuthenticationTicket Ticket(string subject, string sid, DateTimeOffset expiresAt)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            AuthenticationProperties properties = new() { IsPersistent = true, IssuedUtc = now, ExpiresUtc = expiresAt };
            SessionProperties.SetCreatedAt(properties, now);
            properties.StoreTokens(
            [
                new AuthenticationToken { Name = "access_token", Value = "access-0" },
                new AuthenticationToken { Name = "refresh_token", Value = "refresh-0" },
                new AuthenticationToken { Name = SessionProperties.ExpiresAtToken, Value = now.AddSeconds(10).ToString("o", CultureInfo.InvariantCulture) },
            ]);
            ClaimsPrincipal principal = new(new ClaimsIdentity([new Claim("sub", subject), new Claim("sid", sid)], TellmaAuthentication.SessionScheme, "sub", null));
            return new AuthenticationTicket(principal, properties, TellmaAuthentication.SessionScheme);
        }

        /// <summary>The refresh of a session stored at version 0.</summary>
        /// <param name="key">The session's key.</param>
        /// <returns>The request.</returns>
        private static RefreshRequest Request(string key)
        {
            return new RefreshRequest(key, SessionKeys.HandleOf(key), "sub-1", 0, DateTimeOffset.UtcNow.AddSeconds(10));
        }

        /// <summary>Reads a session row's columns directly.</summary>
        /// <param name="host">The catalog.</param>
        /// <param name="key">The key.</param>
        /// <returns>The row's handle, subject, sid, tokens version and expiry.</returns>
        private static async Task<(string Handle, string Subject, string? Sid, int Version, DateTimeOffset ExpiresAt)> ReadRowAsync(CatalogHost host, string key)
        {
            await using SqlConnection connection = await host.Connections.OpenCatalogAsync(Token);
            await using SqlCommand command = connection.CreateCommand();
            command.CommandText = "SELECT [Handle], [Subject], [Sid], [TokensVersion], [ExpiresAt] FROM [catalog].[Sessions] WHERE [Key] = @key;";
            _ = command.Parameters.AddWithValue("@key", key);
            await using SqlDataReader reader = await command.ExecuteReaderAsync(Token);
            Assert.True(await reader.ReadAsync(Token));
            return (reader.GetString(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2), reader.GetInt32(3), reader.GetDateTimeOffset(4));
        }

        /// <summary>How many session rows the catalog holds.</summary>
        /// <param name="host">The catalog.</param>
        /// <returns>The count.</returns>
        private static async Task<int> CountAsync(CatalogHost host)
        {
            await using SqlConnection connection = await host.Connections.OpenCatalogAsync(Token);
            await using SqlCommand command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM [catalog].[Sessions];";
            return (int)await command.ExecuteScalarAsync(Token);
        }

        /// <summary>One web instance's session store and refresher, with its own cache, over the shared catalog.</summary>
        private sealed class Instance
        {
            /// <summary>The Data Protection every instance shares, as a deployment's key ring is shared.</summary>
            private static readonly IDataProtectionProvider SharedProtection = new EphemeralDataProtectionProvider();

            /// <summary>Creates the instance.</summary>
            /// <param name="host">The catalog.</param>
            /// <param name="redeemer">The authority's token endpoint.</param>
            public Instance(CatalogHost host, ITokenRedeemer? redeemer = null)
            {
                IOptions<TellmaSessionOptions> options = Options.Create(new TellmaSessionOptions());
                HostMetrics metrics = new(host.Services.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>());
                SessionTerminationNotifier notifier = new(host.Services.GetRequiredService<IServiceScopeFactory>(), NullLogger<SessionTerminationNotifier>.Instance);
                Store = new CatalogSessionStore(
                    new SqlSessionRows(host.Connections),
                    SharedProtection,
                    options,
                    TimeProvider.System,
                    metrics,
                    notifier,
                    new SessionCookieSecurity(Secure: true),
                    NullLogger<CatalogSessionStore>.Instance);
                Refresher = new SessionRefresher(Store, redeemer ?? new GatedRedeemer(), options, TimeProvider.System, metrics, NullLogger<SessionRefresher>.Instance);
            }

            /// <summary>The store.</summary>
            public CatalogSessionStore Store { get; }

            /// <summary>The refresher.</summary>
            public SessionRefresher Refresher { get; }
        }

        /// <summary>A token endpoint that holds each redemption until released, and counts them.</summary>
        private sealed class GatedRedeemer : ITokenRedeemer
        {
            private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            private int _redemptions;

            /// <summary>Completes when the first redemption arrives.</summary>
            public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

            /// <summary>How many redemptions arrived.</summary>
            public int Redemptions => Volatile.Read(ref _redemptions);

            /// <summary>The access token the latest redemption issued.</summary>
            public string? LastAccessToken { get; private set; }

            /// <summary>Lets every redemption, held or later, complete.</summary>
            public void Release()
            {
                _ = _gate.TrySetResult();
            }

            /// <inheritdoc />
            public async Task<Redemption> RedeemAsync(string refreshToken, CancellationToken cancellationToken)
            {
                _ = Interlocked.Increment(ref _redemptions);
                _ = Entered.TrySetResult();
                await _gate.Task.WaitAsync(cancellationToken);
                string access = "access-" + Guid.NewGuid().ToString("N");
                LastAccessToken = access;
                return Redemption.Redeemed(new RedeemedTokens(access, "refresh-" + Guid.NewGuid().ToString("N"), null, null, "Bearer", TimeSpan.FromHours(1)));
            }
        }
    }
}
