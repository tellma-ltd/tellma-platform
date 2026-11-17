// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Jobs;

namespace Tellma.Distro.Acme.IntegrationTests.Infrastructure
{
    /// <summary>
    ///     A job handler composed beside Acme's that the worker runs on a read-only tenant, as it runs
    ///     an export or a delivery there: every job it claims succeeds.
    /// </summary>
    [JobHandler(Key, RunsWhenReadOnly = true)]
    internal sealed class ProbeJobHandler : IJobHandler
    {
        /// <summary>The handler's key, under Acme's slug.</summary>
        public const string Key = "acme.probe-read-only";

        /// <inheritdoc />
        public Task ExecuteAsync(JobBatch<Job> batch)
        {
            ArgumentNullException.ThrowIfNull(batch);
            foreach (JobItem<Job> item in batch.Items)
            {
                item.Succeed();
            }

            return Task.CompletedTask;
        }
    }

    /// <summary>The feature that contributes <see cref="ProbeJobHandler" />; it changes no model.</summary>
    internal sealed class ProbeJobFeature : ITellmaFeature
    {
        /// <inheritdoc />
        public string Name => "probe-jobs";

        /// <inheritdoc />
        public void Declare(FeatureDeclaration declaration)
        {
        }

        /// <inheritdoc />
        public void Contribute(FeatureContribution contribution)
        {
            ArgumentNullException.ThrowIfNull(contribution);
            _ = contribution.JobHandler<ProbeJobHandler>();
        }
    }
}
