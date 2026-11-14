// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Diagnostics;
using System.Globalization;
using Tellma.Core.Tenancy;

namespace Tellma.Distro.Acme.Web.Provisioning
{
    /// <summary>
    ///     The Development provisioning trigger: provisions a tenant the admin surface registered by
    ///     running the distribution's own Migrator project, <c>dotnet run --project … -- provision
    ///     --tenant &lt;id&gt;</c>, from the source checkout beside this host. It returns once the
    ///     process starts; the process's output and exit code reach this host's log.
    /// </summary>
    /// <remarks>
    ///     Registered only in Development, where the host runs from its project folder. A deployment
    ///     provisions through its own job instead.
    /// </remarks>
    /// <param name="environment">The host environment: its content root locates the Migrator project.</param>
    /// <param name="logger">The trigger's log.</param>
    internal sealed partial class LocalMigratorProvisioningTrigger(IHostEnvironment environment, ILogger<LocalMigratorProvisioningTrigger> logger)
        : ITenantProvisioningTrigger
    {
        /// <summary>The Migrator project's folder, beside the web project's.</summary>
        public const string MigratorProjectFolder = "Tellma.Distro.Acme.Migrator";

        /// <summary>The Migrator project's file.</summary>
        public const string MigratorProjectFile = "Tellma.Distro.Acme.Migrator.csproj";

        /// <inheritdoc />
        /// <exception cref="InvalidOperationException">The Migrator project is not beside the web project, or the process did not start.</exception>
        public Task StartAsync(int tenantId, CancellationToken cancellationToken = default)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(tenantId, 1);
            cancellationToken.ThrowIfCancellationRequested();

            ProcessStartInfo start = StartInfo(environment.ContentRootPath, environment.EnvironmentName, tenantId);
            Process process = Process.Start(start)
                ?? throw new InvalidOperationException($"The provisioning of tenant {tenantId} could not start '{start.FileName}'.");

            // --- The run is observed, not awaited: the caller learns only that it started ---
            ProvisioningStarted(logger, tenantId, process.Id);
            _ = ObserveAsync(process, tenantId);
            return Task.CompletedTask;
        }

        /// <summary>The command that provisions a tenant: the Migrator project run in this host's environment.</summary>
        /// <param name="contentRoot">The web project's folder.</param>
        /// <param name="environmentName">The host's environment, which the migrator runs in too.</param>
        /// <param name="tenantId">The tenant.</param>
        /// <returns>The process's start information.</returns>
        /// <exception cref="InvalidOperationException">The Migrator project is not beside the web project.</exception>
        internal static ProcessStartInfo StartInfo(string contentRoot, string environmentName, int tenantId)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(contentRoot);
            ArgumentException.ThrowIfNullOrWhiteSpace(environmentName);

            string project = Path.GetFullPath(Path.Combine(contentRoot, "..", MigratorProjectFolder, MigratorProjectFile));
            if (!File.Exists(project))
            {
                throw new InvalidOperationException(
                    $"Provisioning in Development runs the distribution's migrator from source, but {project} does not exist; run the web host from its project folder in a source checkout, or provision with the migrator's provision command.");
            }

            ProcessStartInfo start = new("dotnet")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = Path.GetDirectoryName(project),
            };

            // No launch profile, so the environment is the one set below and nothing else.
            foreach (string argument in (string[])["run", "--project", project, "--no-launch-profile", "--", "provision", "--tenant", tenantId.ToString(CultureInfo.InvariantCulture)])
            {
                start.ArgumentList.Add(argument);
            }

            // The migrator's generic host reads DOTNET_ENVIRONMENT, not the web host's ASPNETCORE_ one.
            start.Environment["DOTNET_ENVIRONMENT"] = environmentName;
            return start;
        }

        /// <summary>Logs the process's output and its exit, then releases it.</summary>
        /// <param name="process">The started process.</param>
        /// <param name="tenantId">The tenant it provisions.</param>
        /// <returns>A task that completes when the process has exited.</returns>
        private async Task ObserveAsync(Process process, int tenantId)
        {
            using (process)
            {
                try
                {
                    Task output = PipeAsync(process.StandardOutput, tenantId, isError: false);
                    Task error = PipeAsync(process.StandardError, tenantId, isError: true);
                    await process.WaitForExitAsync().ConfigureAwait(false);
                    await Task.WhenAll(output, error).ConfigureAwait(false);

                    if (process.ExitCode == 0)
                    {
                        ProvisioningSucceeded(logger, tenantId);
                    }
                    else
                    {
                        ProvisioningFailed(logger, tenantId, process.ExitCode);
                    }
                }
                catch (Exception exception) when (exception is InvalidOperationException or IOException)
                {
                    // The process's streams closed under the observer; the run's outcome is in the catalog.
                    ProvisioningUnobserved(logger, exception, tenantId);
                }
            }
        }

        /// <summary>Copies one of the process's streams to the log, line by line.</summary>
        /// <param name="reader">The stream.</param>
        /// <param name="tenantId">The tenant being provisioned.</param>
        /// <param name="isError">Whether it is the standard error.</param>
        /// <returns>A task that completes at the stream's end.</returns>
        private async Task PipeAsync(StreamReader reader, int tenantId, bool isError)
        {
            while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
            {
                if (isError)
                {
                    MigratorError(logger, tenantId, line);
                }
                else
                {
                    MigratorOutput(logger, tenantId, line);
                }
            }
        }

        // ------------------------------------------------------------------------------------------
        // The log events
        // ------------------------------------------------------------------------------------------

        /// <summary>The migrator process started.</summary>
        /// <param name="logger">The log.</param>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="processId">The process's id.</param>
        [LoggerMessage(Level = LogLevel.Information, Message = "Provisioning tenant {TenantId} in the local migrator, process {ProcessId}.")]
        private static partial void ProvisioningStarted(ILogger logger, int tenantId, int processId);

        /// <summary>The migrator process exited 0.</summary>
        /// <param name="logger">The log.</param>
        /// <param name="tenantId">The tenant.</param>
        [LoggerMessage(Level = LogLevel.Information, Message = "The local migrator provisioned tenant {TenantId}.")]
        private static partial void ProvisioningSucceeded(ILogger logger, int tenantId);

        /// <summary>The migrator process exited otherwise.</summary>
        /// <param name="logger">The log.</param>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="exitCode">Its exit code.</param>
        [LoggerMessage(Level = LogLevel.Error, Message = "The local migrator failed to provision tenant {TenantId}, exit code {ExitCode}; its output is logged above.")]
        private static partial void ProvisioningFailed(ILogger logger, int tenantId, int exitCode);

        /// <summary>The process could not be observed to its end.</summary>
        /// <param name="logger">The log.</param>
        /// <param name="exception">What went wrong.</param>
        /// <param name="tenantId">The tenant.</param>
        [LoggerMessage(Level = LogLevel.Warning, Message = "The local migrator's provisioning of tenant {TenantId} could not be observed to its end.")]
        private static partial void ProvisioningUnobserved(ILogger logger, Exception exception, int tenantId);

        /// <summary>A line of the migrator's output.</summary>
        /// <param name="logger">The log.</param>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="line">The line.</param>
        [LoggerMessage(Level = LogLevel.Information, Message = "Migrator (tenant {TenantId}): {Line}")]
        private static partial void MigratorOutput(ILogger logger, int tenantId, string line);

        /// <summary>A line of the migrator's error output.</summary>
        /// <param name="logger">The log.</param>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="line">The line.</param>
        [LoggerMessage(Level = LogLevel.Warning, Message = "Migrator (tenant {TenantId}): {Line}")]
        private static partial void MigratorError(ILogger logger, int tenantId, string line);
    }
}
