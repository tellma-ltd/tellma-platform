// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Jobs;

namespace Tellma.Testing.Entities
{
    /// <summary>The fixture's job handler keys.</summary>
    public static class FixtureJobKeys
    {
        /// <summary>The arguments-only handler, <see cref="ProbeJobHandler" />.</summary>
        public const string Probe = "fixture.probe";

        /// <summary>The handler over the shipments, <see cref="ShipJobHandler" />.</summary>
        public const string Ship = "fixture.ship";

        /// <summary>The handler over the exports, <see cref="ExportJobHandler" />.</summary>
        public const string Export = "fixture.export";
    }

    // The fixture's handlers give the queue registered keys to enqueue under. The suites write and
    // read job rows; none of them runs a job, so each handler completes without doing anything.

    /// <summary>An arguments-only handler, which processes no rows.</summary>
    [JobHandler(FixtureJobKeys.Probe, Schedulable = true)]
    public sealed class ProbeJobHandler : IJobHandler
    {
        /// <inheritdoc />
        public Task ExecuteAsync(JobBatch<Job> batch)
        {
            return Task.CompletedTask;
        }
    }

    /// <summary>A handler over the <see cref="Shipment" /> rows, which are keyed by a <see cref="long" />.</summary>
    [JobHandler(FixtureJobKeys.Ship)]
    public sealed class ShipJobHandler : IEntityJobHandler<Shipment>
    {
        /// <inheritdoc />
        public Task ExecuteAsync(JobBatch<Shipment> batch)
        {
            return Task.CompletedTask;
        }
    }

    /// <summary>A handler over the <see cref="Export" /> rows, which are keyed by an <see cref="int" />.</summary>
    [JobHandler(FixtureJobKeys.Export)]
    public sealed class ExportJobHandler : IEntityJobHandler<Export>
    {
        /// <inheritdoc />
        public Task ExecuteAsync(JobBatch<Export> batch)
        {
            return Task.CompletedTask;
        }
    }
}
