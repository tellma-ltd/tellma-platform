// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Configuration;
using System.Collections.Concurrent;
using System.Globalization;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Tenancy;

namespace Tellma.Core.IntegrationTests.Migrator
{
    /// <summary>
    ///     A provisioning step whose version the configuration sets and whose runs are counted per run
    ///     key, so a test can bump it between migrator runs and see when it ran.
    /// </summary>
    /// <param name="configuration">The migrator host's configuration.</param>
    internal sealed class ProbeStep(IConfiguration configuration) : ITenantProvisioningStep
    {
        /// <summary>The step's name.</summary>
        public const string StepName = "probe.counter";

        /// <summary>The configuration key of the step's version.</summary>
        public const string VersionKey = "Probe:StepVersion";

        /// <summary>The configuration key that names the test the runs are counted for.</summary>
        public const string RunKeyKey = "Probe:RunKey";

        /// <summary>The runs so far: each run key's tenants and versions, in the order they ran.</summary>
        private static readonly ConcurrentDictionary<string, ConcurrentQueue<(int TenantId, int Version, bool IsNew)>> Runs = new(StringComparer.Ordinal);

        /// <inheritdoc />
        public string Name => StepName;

        /// <inheritdoc />
        public int Order => 200;

        /// <inheritdoc />
        public int Version => int.Parse(configuration[VersionKey] ?? "1", CultureInfo.InvariantCulture);

        /// <summary>The runs counted for a run key.</summary>
        /// <param name="runKey">The run key.</param>
        /// <returns>Each run's tenant, version and whether the tenant was new.</returns>
        public static IReadOnlyList<(int TenantId, int Version, bool IsNew)> RunsOf(string runKey)
        {
            return Runs.TryGetValue(runKey, out ConcurrentQueue<(int, int, bool)>? runs) ? [.. runs] : [];
        }

        /// <inheritdoc />
        public Task RunAsync(TenantProvisioningContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            string key = configuration[RunKeyKey] ?? throw new InvalidOperationException($"{RunKeyKey} is not set.");
            Runs.GetOrAdd(key, static _ => new()).Enqueue((context.Tenant.Id, Version, context.IsNew));
            return Task.CompletedTask;
        }
    }

    /// <summary>The feature that contributes <see cref="ProbeStep" />.</summary>
    internal sealed class ProbeStepFeature : ITellmaFeature
    {
        /// <inheritdoc />
        public string Name => "probe-steps";

        /// <inheritdoc />
        public void Declare(FeatureDeclaration declaration)
        {
        }

        /// <inheritdoc />
        public void Contribute(FeatureContribution contribution)
        {
            ArgumentNullException.ThrowIfNull(contribution);
            _ = contribution.ProvisioningStep<ProbeStep>();
        }
    }
}
