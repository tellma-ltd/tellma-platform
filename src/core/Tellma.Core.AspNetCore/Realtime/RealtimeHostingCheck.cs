// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Hosting;
using Tellma.Core.Abstractions.Jobs;
using Tellma.Core.AspNetCore.Hosting;

namespace Tellma.Core.AspNetCore.Realtime
{
    /// <summary>
    ///     The startup check of the hub's hosting mode against the deployment. It fails when Azure
    ///     SignalR Service is configured but not selected, when it is selected under another
    ///     deployment's application name or for a deployment whose id cannot name an application,
    ///     when the hosting mode is one the platform does not know,
    ///     and when this instance runs no job worker yet its hub reaches its own connections alone,
    ///     so the events of the instance that does would never reach them. It warns when the host
    ///     reports several instances and no mode reaches every instance's connections.
    /// </summary>
    /// <remarks>Nothing is checked when the hub is turned off: the host maps no hub to host.</remarks>
    /// <param name="hosting">The hosting mode the services select.</param>
    /// <param name="deployment">The deployment.</param>
    /// <param name="configuration">The application's configuration.</param>
    /// <param name="logger">Where the warning is written.</param>
    internal sealed class RealtimeHostingCheck(
        RealtimeHosting hosting,
        DeploymentIdentity deployment,
        IConfiguration configuration,
        ILogger<RealtimeHostingCheck> logger) : IStartupCheck
    {
        /// <summary>The check's name.</summary>
        public const string CheckName = "host.realtime-hosting";

        /// <summary>The configuration key of Azure SignalR Service's connection string.</summary>
        public const string AzureConnectionStringKey = "Azure:SignalR:ConnectionString";

        /// <summary>The environment variable App Service sets on every instance it runs, read through configuration.</summary>
        public const string AppServiceInstanceVariable = "WEBSITE_INSTANCE_ID";

        /// <inheritdoc />
        public string Name => CheckName;

        /// <inheritdoc />
        public bool RequiresInfrastructure => true;

        /// <inheritdoc />
        public Task<IReadOnlyList<CompositionProblem>> CheckAsync(IServiceProvider services, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(services);
            List<CompositionProblem> problems = [];
            RealtimeOptions realtime = HostSettings.Read<RealtimeOptions>(configuration, RealtimeOptions.SectionPath);
            if (!realtime.Enabled)
            {
                return Task.FromResult<IReadOnlyList<CompositionProblem>>(problems);
            }

            // --- The mode is one the platform knows ---
            if (hosting.Mode == RealtimeMode.Unknown)
            {
                problems.Add(new CompositionProblem(
                    CheckName,
                    $"The hub's lifetime manager is {hosting.LifetimeManager?.FullName ?? "not registered"}, which is none of the platform's hosting modes (in-process, a Redis backplane, Azure SignalR Service).",
                    "Select the hosting mode in the web host's composeWeb delegate through AddTellmaRealtime(configure), with AddStackExchangeRedis or AddAzureSignalR, or select none for the in-process mode."));
            }

            // --- Azure SignalR Service: configured means selected, under this deployment's name ---
            bool azureConfigured = !string.IsNullOrWhiteSpace(configuration[AzureConnectionStringKey]);
            if (azureConfigured && hosting.Mode != RealtimeMode.Azure)
            {
                problems.Add(new CompositionProblem(
                    CheckName,
                    $"{AzureConnectionStringKey} is set, but Azure SignalR Service is not the hub's hosting mode, so the clients of other instances would never receive this instance's events.",
                    "Compose UseAzureWebDefaults() in the web host's composeWeb delegate, or remove the connection string."));
            }

            if (hosting.Mode == RealtimeMode.Azure)
            {
                // A deployment id the service cannot take as a name leaves the options without one.
                if (!RealtimeApplicationName.TryFrom(deployment, out string? expected))
                {
                    problems.Add(new CompositionProblem(
                        CheckName,
                        $"Azure SignalR Service is selected, but the deployment id '{deployment.DeploymentId}' does not start with a letter, so it cannot name the deployment's application on a shared service instance.",
                        "Give the distribution an application slug that starts with a letter, or select a Redis backplane through AddTellmaRealtime(configure)."));
                    return Task.FromResult<IReadOnlyList<CompositionProblem>>(problems);
                }

                string? actual = hosting.AzureApplicationName(services);
                if (!string.Equals(actual, expected, StringComparison.Ordinal))
                {
                    problems.Add(new CompositionProblem(
                        CheckName,
                        $"Azure SignalR Service is selected with the application name '{actual ?? "(none)"}', not this deployment's '{expected}', so its hub could meet another deployment's on a shared service instance.",
                        "Set ServiceOptions.ApplicationName to RealtimeApplicationName.From(deployment), as UseAzureWebDefaults() does."));
                }
            }

            // --- An instance without the worker: the worker's events must reach this instance's clients ---
            JobsOptions jobs = HostSettings.Read<JobsOptions>(configuration, JobsOptions.SectionPath);
            if (!jobs.Enabled && !hosting.ReachesEveryInstance)
            {
                problems.Add(new CompositionProblem(
                    CheckName,
                    $"{JobsOptions.SectionPath}:{nameof(JobsOptions.Enabled)} is false and the hub runs in-process, so the events of the instance that runs the job worker could never reach this instance's connections.",
                    $"Select a Redis backplane or Azure SignalR Service, run the job worker on this instance, or set {RealtimeOptions.SectionPath}:{nameof(RealtimeOptions.Enabled)} to false."));
            }

            // --- Several instances, one of which a send reaches: a warning, since sticky clients still work ---
            // Read from configuration, which holds the host's environment variables.
            bool appService = !string.IsNullOrEmpty(configuration[AppServiceInstanceVariable]);
            if (!hosting.ReachesEveryInstance && (appService || realtime.ExpectedInstances > 1))
            {
                RealtimeLog.RealtimeHostingWarning(
                    logger,
                    appService
                        ? $"The host runs on App Service ({AppServiceInstanceVariable} is set) and the hub runs in-process, so an event published on one instance never reaches another instance's connections. Configure {AzureConnectionStringKey} and compose UseAzureWebDefaults()."
                        : $"{RealtimeOptions.SectionPath}:{nameof(RealtimeOptions.ExpectedInstances)} is {realtime.ExpectedInstances} and the hub runs in-process, so an event published on one instance never reaches another instance's connections. Select a Redis backplane through AddTellmaRealtime(configure).");
            }

            return Task.FromResult<IReadOnlyList<CompositionProblem>>(problems);
        }
    }
}
