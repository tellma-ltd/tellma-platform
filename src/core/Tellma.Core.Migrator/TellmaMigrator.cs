// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Composition;
using Tellma.Core.Migrator.CommandLine;
using Tellma.Core.Migrator.Commands;
using Tellma.Core.Tenancy;

namespace Tellma.Core.Migrator
{
    /// <summary>
    ///     The entry point of a distribution's migrator, the single executable that changes a
    ///     database's schema: it creates the catalog and the tenant databases, migrates them, grants
    ///     the application's role, provisions tenants, moves them between states, reports their
    ///     status, and emits the client metadata.
    /// </summary>
    /// <remarks>
    ///     Every command but <c>emit-client-assets</c> runs inside a started generic host built from
    ///     the distribution's composition, so the startup gate checks the deployment before the
    ///     command touches a database, and no background job runs beside a schema change.
    /// </remarks>
    public static class TellmaMigrator
    {
        /// <summary>The configuration key that turns the job worker off, forced by the migrator's host.</summary>
        public const string JobsEnabledKey = "Tellma:Jobs:Enabled";

        /// <summary>The assembly the in-proc identity store's migrations ship in.</summary>
        public const string IdentityMigrationsAssembly = "Tellma.Identity.Migrations";

        /// <summary>Runs one migrator command.</summary>
        /// <param name="args">The command line: a command, then its operands and options.</param>
        /// <param name="slug">The distribution slug.</param>
        /// <param name="compose">The distribution's composition method, the one its web host runs.</param>
        /// <param name="migrateIdentityStore">
        ///     Migrates the in-proc identity store, given the catalog's connection string; required for
        ///     <c>migrate</c> when the identity mode is in-proc.
        /// </param>
        /// <returns>
        ///     The exit code: 0 converged, 1 a usage error, 2 a partial failure, 3 composition problems.
        /// </returns>
        /// <exception cref="ArgumentNullException">An argument other than <paramref name="migrateIdentityStore" /> is null.</exception>
        public static Task<int> RunAsync(
            IReadOnlyList<string> args, string slug, Action<TellmaBuilder> compose, Func<string, Task>? migrateIdentityStore = null)
        {
            return RunAsync(args, slug, compose, migrateIdentityStore, new MigratorRunSettings(), CancellationToken.None);
        }

        /// <summary>Runs one migrator command, hosted as the settings say.</summary>
        /// <param name="args">The command line.</param>
        /// <param name="slug">The distribution slug.</param>
        /// <param name="compose">The distribution's composition method.</param>
        /// <param name="migrateIdentityStore">Migrates the in-proc identity store, or null.</param>
        /// <param name="settings">How the run is hosted.</param>
        /// <param name="cancellationToken">Stops the command.</param>
        /// <returns>The exit code.</returns>
        /// <exception cref="ArgumentNullException">An argument other than <paramref name="migrateIdentityStore" /> is null.</exception>
        internal static async Task<int> RunAsync(
            IReadOnlyList<string> args,
            string slug,
            Action<TellmaBuilder> compose,
            Func<string, Task>? migrateIdentityStore,
            MigratorRunSettings settings,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(args);
            ArgumentNullException.ThrowIfNull(slug);
            ArgumentNullException.ThrowIfNull(compose);
            ArgumentNullException.ThrowIfNull(settings);

            TextWriter output = settings.Out ?? Console.Out;
            TextWriter error = settings.Error ?? Console.Error;

            // --- The command line ---
            MigratorCommandLine line;
            try
            {
                line = MigratorCommandLine.Parse(args);
            }
            catch (MigratorUsageException exception)
            {
                await WriteUsageAsync(error, exception).ConfigureAwait(false);
                return MigratorExitCodes.Usage;
            }

            // --- The emit builds no host; its failures map to the same codes as a command's ---
            if (line is EmitClientAssetsCommandLine emit)
            {
                try
                {
                    return await EmitClientAssetsCommand.RunAsync(emit, slug, compose, settings, output, error, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    await error.WriteLineAsync("The command was stopped before it finished; rerun it to converge.").ConfigureAwait(false);
                    return MigratorExitCodes.PartialFailure;
                }
                catch (Exception exception)
                {
                    await error.WriteLineAsync($"emit-client-assets: failed ({TenantOutcome.Describe(exception)})").ConfigureAwait(false);
                    return MigratorExitCodes.PartialFailure;
                }
            }

            // --- The host's configuration, with the job worker forced off by the last source ---
            HostApplicationBuilder builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
            {
                Args = [],
                EnvironmentName = settings.EnvironmentName,
                ContentRootPath = settings.ContentRootPath ?? AppContext.BaseDirectory,
            });

            settings.ConfigureConfiguration?.Invoke(builder.Configuration);
            _ = builder.Configuration.AddInMemoryCollection([new KeyValuePair<string, string?>(JobsEnabledKey, "false")]);

            // In in-proc mode the catalog holds the identity store, which only its own assembly migrates.
            if (line is MigrateCommandLine && migrateIdentityStore is null && MigratorContext.IsInProcIdentity(builder.Configuration))
            {
                await WriteUsageAsync(error, new MigratorUsageException(
                    $"{MigratorContext.IdentityModeKey} is {MigratorContext.InProcIdentityMode}, so migrate also migrates the identity store, but no migration of it was passed; "
                    + $"reference {IdentityMigrationsAssembly} from the migrator project and pass its migration to {nameof(TellmaMigrator)}.{nameof(RunAsync)}.")).ConfigureAwait(false);
                return MigratorExitCodes.Usage;
            }

            // --- The composition, the web host's own, with the registry loaded only by the commands ---
            try
            {
                _ = builder.Services.AddTellma(slug, builder.Configuration, builder.Environment, compose);
            }
            catch (TellmaCompositionException exception)
            {
                await error.WriteLineAsync(exception.Message).ConfigureAwait(false);
                return MigratorExitCodes.CompositionProblems;
            }
            catch (Exception exception)
            {
                // The distribution's composition method itself threw: the composition is the problem.
                await error.WriteLineAsync(ComposeFailed(exception)).ConfigureAwait(false);
                return MigratorExitCodes.CompositionProblems;
            }

            // The catalog may not exist yet: migrate creates it, so the first load waits for the commands.
            _ = builder.Services.Configure<TenantRegistryStartupOptions>(static startup =>
            {
                startup.VerifyCatalog = false;
                startup.LoadOnStart = false;
            });

            settings.ConfigureServices?.Invoke(builder.Services);

            // --- The host: a service graph that does not resolve is a composition problem ---
            (IHost? built, int buildExitCode, string? buildFailure) = Build(builder);
            if (built is null)
            {
                await error.WriteLineAsync(buildFailure).ConfigureAwait(false);
                return buildExitCode;
            }

            // --- The startup gate, then one command, then the host stops ---
            using IHost host = built;
            try
            {
                await host.StartAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (TellmaCompositionException exception)
            {
                await error.WriteLineAsync(exception.Message).ConfigureAwait(false);
                return MigratorExitCodes.CompositionProblems;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                await error.WriteLineAsync("The run was stopped while the host started; rerun it to converge.").ConfigureAwait(false);
                return MigratorExitCodes.PartialFailure;
            }
            catch (Exception exception)
            {
                // A hosted service that failed to start: nothing ran, and a rerun after the fix converges.
                await error.WriteLineAsync($"The host could not start: {TenantOutcome.Describe(exception)}").ConfigureAwait(false);
                return MigratorExitCodes.PartialFailure;
            }

            using var stopping = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping);
            try
            {
                MigratorContext context = new(host.Services, slug, compose, migrateIdentityStore, settings.ResolveMigrationsAssembly(), output);
                return await RunCommandAsync(line, context, stopping.Token).ConfigureAwait(false);
            }
            catch (MigratorUsageException exception)
            {
                await WriteUsageAsync(error, exception).ConfigureAwait(false);
                return MigratorExitCodes.Usage;
            }
            catch (OperationCanceledException) when (stopping.IsCancellationRequested)
            {
                await error.WriteLineAsync("The command was stopped before it finished; rerun it to converge.").ConfigureAwait(false);
                return MigratorExitCodes.PartialFailure;
            }
            catch (Exception exception)
            {
                // A failure outside a tenant's guarded work, a cancellation that is not the run's included: the whole command failed, and a rerun converges.
                MigratorLog.CommandFailed(host.Services.GetRequiredService<ILoggerFactory>().CreateLogger(MigratorLog.Category), exception);
                await error.WriteLineAsync($"The command failed: {TenantOutcome.Describe(exception)}").ConfigureAwait(false);
                return MigratorExitCodes.PartialFailure;
            }
            finally
            {
                await host.StopAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }

        /// <summary>The text of a composition method that threw something other than a composition problem.</summary>
        /// <param name="exception">What it threw.</param>
        /// <returns>The text.</returns>
        internal static string ComposeFailed(Exception exception)
        {
            return $"The composition failed: {exception.GetType().Name}: {exception.Message}";
        }

        /// <summary>
        ///     Builds the host. Development validates the service graph here, and a graph that does
        ///     not resolve is a composition problem; any other failure fails the run.
        /// </summary>
        /// <param name="builder">The configured builder.</param>
        /// <returns>The host, or null with the exit code and the text to print.</returns>
        private static (IHost? Host, int ExitCode, string? Failure) Build(HostApplicationBuilder builder)
        {
            try
            {
                return (builder.Build(), MigratorExitCodes.Converged, null);
            }
            catch (AggregateException exception)
            {
                return (null, MigratorExitCodes.CompositionProblems, $"The composition's services do not resolve: {exception.Message}");
            }
            catch (Exception exception)
            {
                return (null, MigratorExitCodes.PartialFailure, $"The host could not be built: {TenantOutcome.Describe(exception)}");
            }
        }

        /// <summary>Runs the parsed command.</summary>
        /// <param name="line">The command.</param>
        /// <param name="context">The run.</param>
        /// <param name="cancellationToken">Stops the command.</param>
        /// <returns>The exit code.</returns>
        private static Task<int> RunCommandAsync(MigratorCommandLine line, MigratorContext context, CancellationToken cancellationToken)
        {
            return line switch
            {
                MigrateCommandLine migrate => new MigrateCommand(context).RunAsync(migrate, cancellationToken),
                ProvisionCommandLine provision => new ProvisionCommand(context).RunAsync(provision, cancellationToken),
                SetStateCommandLine setState => new SetStateCommand(context).RunAsync(setState, cancellationToken),
                StatusCommandLine => new StatusCommand(context).RunAsync(cancellationToken),
                _ => throw new InvalidOperationException($"The command {line.GetType().Name} has no runner."),
            };
        }

        /// <summary>Writes a usage error and the usage text.</summary>
        /// <param name="error">The error writer.</param>
        /// <param name="exception">The usage error.</param>
        /// <returns>A task that completes when written.</returns>
        private static async Task WriteUsageAsync(TextWriter error, MigratorUsageException exception)
        {
            await error.WriteLineAsync(exception.Message).ConfigureAwait(false);
            await error.WriteLineAsync(MigratorCommandLine.Usage).ConfigureAwait(false);
        }
    }
}
