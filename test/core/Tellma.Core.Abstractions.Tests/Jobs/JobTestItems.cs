// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Reflection;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Jobs;

namespace Tellma.Core.Abstractions.Tests.Jobs
{
    /// <summary>Builds the items, errors and batches the job contract tests run against.</summary>
    internal static class JobTestItems
    {
        /// <summary>A progress reporter that records nothing.</summary>
        public static IJobProgress NoProgress { get; } = new NullProgress();

        /// <summary>Creates an item over a fresh job, as a claim would.</summary>
        /// <param name="arguments">The job's stored arguments; null for none.</param>
        /// <returns>The item, its job as its item.</returns>
        public static JobItem<Job> Create(string? arguments = null)
        {
            Job job = new() { Id = 1, HandlerKey = "acme.export", ArgumentsJson = arguments };
            return new JobItem<Job> { Job = job, Item = job, Progress = NoProgress };
        }

        /// <summary>An error whose one argument serialises to exactly <paramref name="length" /> characters.</summary>
        /// <param name="length">The serialised length; at least eleven, the length of <c>{"text":""}</c>.</param>
        /// <returns>The error.</returns>
        public static JobError ErrorOfLength(int length)
        {
            return new JobError("Export.Failed", new Dictionary<string, object?> { ["text"] = new string('a', length - 11) });
        }

        /// <summary>Creates a batch that refuses every member; for contracts that only hold it.</summary>
        /// <returns>The batch.</returns>
        public static IDataBatch UnusedBatch()
        {
            return DispatchProxy.Create<IDataBatch, RefusingBatch>();
        }

        /// <summary>A progress reporter that records nothing.</summary>
        private sealed class NullProgress : IJobProgress
        {
            /// <inheritdoc />
            public void Report(byte? percent, JobMessage? message, object? state)
            {
            }

            /// <inheritdoc />
            public Task FlushAsync(CancellationToken cancellationToken = default)
            {
                return Task.CompletedTask;
            }

            /// <inheritdoc />
            public void Append(IDataBatch batch, byte? percent, JobMessage? message, object? state)
            {
            }
        }
    }

    /// <summary>A batch whose every member throws; the job contracts hold a batch without using it.</summary>
    public class RefusingBatch : DispatchProxy
    {
        /// <inheritdoc />
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            throw new NotSupportedException($"The batch is not used by this test, yet {targetMethod?.Name} was called.");
        }
    }
}
