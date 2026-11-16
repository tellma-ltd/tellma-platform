// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Options;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Access;

namespace Tellma.Core.Tests.Access
{
    /// <summary>
    ///     The connect cache: entries keyed by tenant and subject compared byte for byte, and cold
    ///     connects of one key single-flighted, a starter's cancellation failing only the starter.
    /// </summary>
    public sealed class ConnectCacheTests : IDisposable
    {
        /// <summary>The cache under test.</summary>
        private readonly ConnectCache _cache = new(Options.Create(new CacheOptions()));

        /// <inheritdoc />
        public void Dispose()
        {
            _cache.Dispose();
        }

        [Fact]
        public void Entries_are_keyed_by_tenant_and_by_the_subject_byte_for_byte()
        {
            ConnectedUser first = User(7);
            _cache.Set(1, "sub-1", first);

            Assert.Same(first, _cache.Find(1, "sub-1"));
            Assert.Null(_cache.Find(2, "sub-1"));
            Assert.Null(_cache.Find(1, "SUB-1"));

            _cache.Remove(1, "sub-1");
            Assert.Null(_cache.Find(1, "sub-1"));
        }

        [Fact]
        public async Task Concurrent_cold_connects_of_one_key_share_one_connect()
        {
            TaskCompletionSource<ConnectedUser> gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            int connects = 0;
            Task<ConnectedUser> Connect(CancellationToken cancellationToken)
            {
                _ = Interlocked.Increment(ref connects);
                return gate.Task;
            }

            Task<ConnectedUser>[] callers = [.. Enumerable.Range(0, 5).Select(_ => _cache.ConnectOnceAsync(1, "sub-1", Connect, TestContext.Current.CancellationToken))];
            Task<ConnectedUser> otherTenant = _cache.ConnectOnceAsync(2, "sub-1", static _ => Task.FromResult(User(9)), TestContext.Current.CancellationToken);
            ConnectedUser user = User(7);
            gate.SetResult(user);

            ConnectedUser[] users = await Task.WhenAll(callers);
            Assert.All(users, connected => Assert.Same(user, connected));
            Assert.Equal(1, connects);
            Assert.Equal(9, (await otherTenant).UserId);
        }

        [Fact]
        public async Task A_failed_connect_fails_every_caller_waiting_and_the_next_caller_connects_afresh()
        {
            TaskCompletionSource<ConnectedUser> gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<ConnectedUser> starter = _cache.ConnectOnceAsync(1, "sub-1", _ => gate.Task, TestContext.Current.CancellationToken);
            Task<ConnectedUser> joiner = _cache.ConnectOnceAsync(1, "sub-1", static _ => Task.FromResult(User(8)), TestContext.Current.CancellationToken);
            gate.SetException(new InvalidOperationException("refused"));

            _ = await Assert.ThrowsAsync<InvalidOperationException>(() => starter);
            _ = await Assert.ThrowsAsync<InvalidOperationException>(() => joiner);
            Assert.Equal(8, (await _cache.ConnectOnceAsync(1, "sub-1", static _ => Task.FromResult(User(8)), TestContext.Current.CancellationToken)).UserId);
        }

        [Fact]
        public async Task A_connect_cancelled_under_its_starter_leaves_a_waiting_caller_to_connect_on_its_own()
        {
            using CancellationTokenSource starterCancellation = new();
            TaskCompletionSource<ConnectedUser> gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<ConnectedUser> starter = _cache.ConnectOnceAsync(
                1, "sub-1", gate.Task.WaitAsync, starterCancellation.Token);
            Task<ConnectedUser> joiner = _cache.ConnectOnceAsync(1, "sub-1", static _ => Task.FromResult(User(8)), TestContext.Current.CancellationToken);

            await starterCancellation.CancelAsync();

            _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => starter);
            Assert.Equal(8, (await joiner).UserId);
        }

        [Fact]
        public async Task A_waiting_caller_cancelled_on_its_own_stops_waiting_and_the_connect_runs_on()
        {
            using CancellationTokenSource joinerCancellation = new();
            TaskCompletionSource<ConnectedUser> gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<ConnectedUser> starter = _cache.ConnectOnceAsync(1, "sub-1", _ => gate.Task, TestContext.Current.CancellationToken);
            Task<ConnectedUser> joiner = _cache.ConnectOnceAsync(1, "sub-1", static _ => Task.FromResult(User(8)), joinerCancellation.Token);

            await joinerCancellation.CancelAsync();
            _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => joiner);

            gate.SetResult(User(7));
            Assert.Equal(7, (await starter).UserId);
        }

        /// <summary>A connected user.</summary>
        /// <param name="userId">Its id.</param>
        /// <returns>The user.</returns>
        private static ConnectedUser User(int userId)
        {
            return new ConnectedUser(
                userId,
                UserKind.Individual,
                UserState.Joined,
                new UserProfile("Ada", null, null, null, null, null, null, null, null, null, null, null, null, null, DateTimeOffset.UnixEpoch),
                Guid.NewGuid(),
                UserAccess.System,
                new Dictionary<string, Guid>(StringComparer.Ordinal),
                null);
        }
    }
}
