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
    ///     key, so a test can bump it between migrator runs and see when it ran. The configuration can
    ///     also make it fail on one tenant, and a test can hold its next run until released.
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

        /// <summary>The configuration key of the tenant the step fails on.</summary>
        public const string FailTenantKey = "Probe:FailTenant";

        /// <summary>The configuration key of how it fails: <c>canceled</c> for a cancellation that is not the run's, else an error.</summary>
        public const string FailureKey = "Probe:Failure";

        /// <summary>The configuration key that names the gate the step's next run waits at.</summary>
        public const string GateKeyKey = "Probe:GateKey";

        /// <summary>The gates a run waits at, each used once.</summary>
        private static readonly ConcurrentDictionary<string, Gate> Gates = new(StringComparer.Ordinal);

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

        /// <summary>Sets a gate the step's next run with this gate key waits at until released.</summary>
        /// <param name="gateKey">The gate key the run's configuration names.</param>
        /// <returns>The gate.</returns>
        public static Gate Hold(string gateKey)
        {
            Gate gate = new();
            Assert.True(Gates.TryAdd(gateKey, gate));
            return gate;
        }

        /// <inheritdoc />
        public async Task RunAsync(TenantProvisioningContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            string key = configuration[RunKeyKey] ?? throw new InvalidOperationException($"{RunKeyKey} is not set.");

            // --- A gate the test set: signal arrival, then wait for the release ---
            if (configuration[GateKeyKey] is { } gateKey && Gates.TryRemove(gateKey, out Gate? gate))
            {
                _ = gate.Entered.TrySetResult();
                await gate.Released.Task.ConfigureAwait(false);
            }

            // --- The failure the configuration asks for, before the run is counted ---
            if (configuration[FailTenantKey] == context.Tenant.Id.ToString(CultureInfo.InvariantCulture))
            {
                throw configuration[FailureKey] == "canceled"
                    ? new TaskCanceledException("The probe step's own HTTP call timed out.")
                    : new InvalidOperationException("The probe step failed as the test told it to.");
            }

            Runs.GetOrAdd(key, static _ => new()).Enqueue((context.Tenant.Id, Version, context.IsNew));
        }

        /// <summary>A point a step's run waits at: the test learns when the run arrived and releases it.</summary>
        internal sealed class Gate
        {
            /// <summary>Completes when a run arrives at the gate.</summary>
            public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

            /// <summary>Completed by the test to let the run go on.</summary>
            public TaskCompletionSource Released { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
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
