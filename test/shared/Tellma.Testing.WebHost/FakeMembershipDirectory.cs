// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Concurrent;
using Tellma.Core.Abstractions.Tenancy;

namespace Tellma.Testing.WebHost
{
    /// <summary>The membership hints in memory: a list per subject, or a failure every read throws, and a count of the reads.</summary>
    internal sealed class FakeMembershipDirectory : ITenantMembershipDirectory
    {
        private int _reads;

        /// <summary>Each subject's memberships.</summary>
        public ConcurrentDictionary<string, IReadOnlyList<TenantMembership>> Memberships { get; } = new(StringComparer.Ordinal);

        /// <summary>A failure every read throws.</summary>
        public Exception? Failure { get; set; }

        /// <summary>How many reads were made, failed ones included.</summary>
        public int Reads => _reads;

        /// <inheritdoc />
        public Task<IReadOnlyList<TenantMembership>> ListAsync(string subject, CancellationToken cancellationToken = default)
        {
            _ = Interlocked.Increment(ref _reads);
            return Failure is not null
                ? Task.FromException<IReadOnlyList<TenantMembership>>(Failure)
                : Task.FromResult(Memberships.GetValueOrDefault(subject) ?? []);
        }

        /// <inheritdoc />
        public Task RecordAsync(IReadOnlyList<TenantMembershipRecord> records, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}
