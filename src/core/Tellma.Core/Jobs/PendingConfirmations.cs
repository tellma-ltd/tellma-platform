// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using System.Text;
using System.Text.Json;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Jobs;

namespace Tellma.Core.Jobs
{
    // Spec 0020 §3.8: a job whose handler raised warnings it may not pass waits for its initiator. The
    // record is { count, items }, each item the confirmation item of the API's error details, with its
    // first 100 ids; past 64 KB the items are dropped from the end, never the first item of a key, so
    // the record names every key. More than 16 distinct keys, or keys that would take the job's
    // confirmed keys past their column, cannot be confirmed in one go, and fail the job instead.
    /// <summary>The warnings a job waits to have confirmed: the record, its trimming and the key cap.</summary>
    internal static class PendingConfirmations
    {
        /// <summary>The separator of a job's confirmed keys.</summary>
        public const char KeySeparator = ',';

        /// <summary>The number of distinct keys among the warnings.</summary>
        /// <param name="requests">The warnings.</param>
        /// <returns>The count.</returns>
        public static int DistinctKeys(IReadOnlyList<ConfirmationRequest> requests)
        {
            ArgumentNullException.ThrowIfNull(requests);
            return requests.Select(static request => request.Key).Distinct(StringComparer.Ordinal).Count();
        }

        /// <summary>
        ///     Whether one confirmation can cover the warnings: at most
        ///     <see cref="JobLimits.MaxConfirmationKeys" /> distinct keys, which together with the keys
        ///     the job already carries fit <see cref="JobLimits.MaxConfirmedKeysLength" /> characters.
        /// </summary>
        /// <param name="requests">The warnings.</param>
        /// <param name="confirmedKeys">The keys the job already carries, comma-separated; null when none.</param>
        /// <returns>True when they can be confirmed in one go.</returns>
        public static bool Confirmable(IReadOnlyList<ConfirmationRequest> requests, string? confirmedKeys)
        {
            ArgumentNullException.ThrowIfNull(requests);
            if (DistinctKeys(requests) > JobLimits.MaxConfirmationKeys)
            {
                return false;
            }

            // --- The merged keys, as a confirm would store them ---
            HashSet<string> merged = new(Split(confirmedKeys), StringComparer.Ordinal);
            merged.UnionWith(requests.Select(static request => request.Key));
            int length = merged.Sum(static key => key.Length) + Math.Max(0, merged.Count - 1);
            return length <= JobLimits.MaxConfirmedKeysLength;
        }

        /// <summary>The keys a job carries.</summary>
        /// <param name="confirmedKeys">The stored keys, comma-separated; null when none.</param>
        /// <returns>The keys, without empty entries.</returns>
        public static IReadOnlyList<string> Split(string? confirmedKeys)
        {
            return confirmedKeys is null
                ? []
                : confirmedKeys.Split(KeySeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        /// <summary>
        ///     The record of the warnings: <c>{ count, items }</c>, each item with its first
        ///     <see cref="JobLimits.MaxPendingConfirmationIds" /> ids, trimmed from the end to
        ///     <see cref="JobLimits.MaxPendingConfirmationsBytes" /> without dropping the first item of a key.
        /// </summary>
        /// <param name="requests">The warnings, in raised order.</param>
        /// <returns>The record as JSON.</returns>
        public static string Serialize(IReadOnlyList<ConfirmationRequest> requests)
        {
            ArgumentNullException.ThrowIfNull(requests);

            // --- Each item serialised alone, its ids cut to the limit, so the record is their concatenation ---
            List<(string Key, string Json, bool First)> items = new(requests.Count);
            HashSet<string> seen = new(StringComparer.Ordinal);
            foreach (ConfirmationRequest request in requests)
            {
                ConfirmationRequest kept = request.Ids.Count <= JobLimits.MaxPendingConfirmationIds
                    ? request
                    : new ConfirmationRequest(request.Key, [.. request.Ids.Take(JobLimits.MaxPendingConfirmationIds)], request.Arguments);
                items.Add((request.Key, JsonSerializer.Serialize(kept, TellmaJson.Options), seen.Add(request.Key)));
            }

            // --- The envelope's bytes, then the items', counted as UTF-8 ---
            string head = string.Create(CultureInfo.InvariantCulture, $"{{\"count\":{requests.Count},\"items\":[");
            const string Tail = "]}";
            long bytes = Encoding.UTF8.GetByteCount(head) + Encoding.UTF8.GetByteCount(Tail)
                + items.Sum(static item => (long)Encoding.UTF8.GetByteCount(item.Json))
                + Math.Max(0, items.Count - 1);

            // --- Past the limit, drop items from the end, never the first of a key ---
            for (int index = items.Count - 1; index >= 0 && bytes > JobLimits.MaxPendingConfirmationsBytes; index--)
            {
                if (!items[index].First)
                {
                    bytes -= Encoding.UTF8.GetByteCount(items[index].Json) + 1;
                    items.RemoveAt(index);
                }
            }

            // --- The record ---
            StringBuilder record = new(head);
            _ = record.AppendJoin(',', items.Select(static item => item.Json)).Append(Tail);
            return record.ToString();
        }
    }
}
