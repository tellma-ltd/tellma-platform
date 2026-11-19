// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections;
using System.Globalization;

namespace Tellma.Core.Jobs
{
    // Spec 0020 §9 and spec 0011 §9: the tenant id, the job ids and the user id go to the log scope,
    // never to tags or metrics, so every event of a claim or an invocation carries them; the origin
    // trace parent is the enqueuing request's, for correlation only.
    /// <summary>The log scope of a job's events: the tenant, the run-as user, the job ids and the origin trace parent.</summary>
    internal sealed class JobLogScope : IReadOnlyList<KeyValuePair<string, object?>>
    {
        /// <summary>The scope's member names, in order.</summary>
        private static readonly string[] Names = ["TenantId", "UserId", "JobId", "OriginTraceParent"];

        /// <summary>The members' values, in the order of <see cref="Names" />.</summary>
        private readonly object?[] _values;

        /// <summary>Creates the scope.</summary>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="userId">The run-as user; null for events no single user's jobs share.</param>
        /// <param name="jobIds">The jobs, in claim order.</param>
        /// <param name="originTraceParent">The trace parent of the job's enqueue when the scope covers one job; null otherwise.</param>
        public JobLogScope(int tenantId, int? userId, IEnumerable<int> jobIds, string? originTraceParent)
        {
            ArgumentNullException.ThrowIfNull(jobIds);

            // Several ids join into one value, so a sink indexes the scope's members alike for one job and for a batch.
            _values = [tenantId, userId, string.Join(',', jobIds.Select(static id => id.ToString(CultureInfo.InvariantCulture))), originTraceParent];
        }

        /// <inheritdoc />
        public int Count => Names.Length;

        /// <inheritdoc />
        public KeyValuePair<string, object?> this[int index] => new(Names[index], _values[index]);

        /// <inheritdoc />
        public IEnumerator<KeyValuePair<string, object?>> GetEnumerator()
        {
            for (int index = 0; index < Names.Length; index++)
            {
                yield return this[index];
            }
        }

        /// <inheritdoc />
        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        /// <summary>What a text sink prints for the scope.</summary>
        /// <returns>The tenant, the user and the jobs.</returns>
        public override string ToString()
        {
            return string.Create(CultureInfo.InvariantCulture, $"TenantId:{_values[0]} UserId:{_values[1]} JobId:{_values[2]}");
        }
    }
}
