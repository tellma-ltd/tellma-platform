// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Concurrent;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Tenancy;

namespace Tellma.Testing.WebHost
{
    /// <summary>
    ///     The tenants' users as the scripted connection and the scripted batch runner see them: the
    ///     user id each subject connects as, and the subjects a fresh read of the tenant's users finds
    ///     deactivated although the connect, as a warm cache would, still admits them.
    /// </summary>
    internal sealed class TestUsers
    {
        private int _prologues;

        /// <summary>Each subject's user id, by tenant; a subject not listed connects as <see cref="TellmaTestHost.UserId" />.</summary>
        public ConcurrentDictionary<(int TenantId, string Subject), int> UserIds { get; } = new();

        /// <summary>The subjects a fresh read finds deactivated, by tenant.</summary>
        public ConcurrentDictionary<(int TenantId, string Subject), bool> Deactivated { get; } = new();

        /// <summary>What every guarded round trip throws, as an unreachable tenant database would; null for none.</summary>
        public Exception? Failure { get; set; }

        /// <summary>How many guarded round trips ran.</summary>
        public int Prologues => Volatile.Read(ref _prologues);

        /// <summary>The user id a subject connects as in a tenant.</summary>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="subject">The subject.</param>
        /// <returns>The user id.</returns>
        public int UserIdOf(int tenantId, string subject)
        {
            return UserIds.TryGetValue((tenantId, subject), out int userId) ? userId : TellmaTestHost.UserId;
        }

        /// <summary>Counts a guarded round trip.</summary>
        public void CountPrologue()
        {
            _ = Interlocked.Increment(ref _prologues);
        }
    }

    /// <summary>
    ///     The guarded batch runner, as one round trip's prologue sees the tenant's users: a subject
    ///     <see cref="TestUsers.Deactivated" /> lists is refused as no longer a member; any other's
    ///     batch runs empty and reads nothing, unless <see cref="TestUsers.Failure" /> fails them all.
    /// </summary>
    /// <param name="users">The scripted users.</param>
    /// <param name="accessor">The scope's request context.</param>
    internal sealed class FakeGuardedBatchRunner(TestUsers users, IRequestContextAccessor accessor) : IGuardedBatchRunner
    {
        /// <inheritdoc />
        public Task<TResult> RunAsync<TResult>(
            BatchPurpose purpose,
            Func<ConnectedUser, IDataBatch, ValueTask> compose,
            Func<ConnectedUser, BatchOutcome, TResult> read,
            CancellationToken cancellationToken = default)
        {
            users.CountPrologue();
            if (users.Failure is { } failure)
            {
                throw failure;
            }

            RequestContext context = accessor.Current;
            int tenantId = context.RequireTenant().Id;
            if (context.Subject is { } subject && users.Deactivated.ContainsKey((tenantId, subject)))
            {
                throw new TenantNotFoundException(tenantId);
            }

            // The suite's callers compose nothing and read no result, so none is made.
            return Task.FromResult(default(TResult)!);
        }
    }
}
