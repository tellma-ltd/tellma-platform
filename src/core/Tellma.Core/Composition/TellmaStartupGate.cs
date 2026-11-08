// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Tellma.Core.Abstractions.Composition;

namespace Tellma.Core.Composition
{
    /// <summary>
    ///     The realised gate: runs every registered <see cref="IStartupCheck" /> when the host starts,
    ///     before any other hosted service and before the host serves traffic, and throws one
    ///     <see cref="TellmaCompositionException" /> carrying every problem they report.
    /// </summary>
    /// <remarks>
    ///     The composition root registers it as the first hosted service and runs it in
    ///     <see cref="IHostedLifecycleService.StartingAsync" />, the earliest point at which a host
    ///     calls into its hosted services, so no hosted service of the host has started when it fails.
    /// </remarks>
    /// <param name="services">The host's root services.</param>
    /// <param name="logger">Where the gate's outcome and any failing check are logged.</param>
    public sealed class TellmaStartupGate(IServiceProvider services, ILogger<TellmaStartupGate> logger) : IHostedLifecycleService
    {
        /// <summary>
        ///     Runs the registered startup checks inside one service scope and returns every problem
        ///     they report, in registration order. A check that throws is reported as a problem.
        /// </summary>
        /// <param name="services">The root services of a realised composition.</param>
        /// <param name="includeInfrastructure">False to run only the checks that read the composition
        ///     alone, as a build step that has no deployment does.</param>
        /// <param name="cancellationToken">Cancels the run.</param>
        /// <returns>Every problem found; empty when every check passes.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="services" /> is null.</exception>
        /// <exception cref="OperationCanceledException"><paramref name="cancellationToken" /> was cancelled.</exception>
        public static async Task<IReadOnlyList<CompositionProblem>> RunChecksAsync(
            IServiceProvider services,
            bool includeInfrastructure,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(services);
            ILogger logger = services.GetService<ILogger<TellmaStartupGate>>() ?? NullLogger<TellmaStartupGate>.Instance;

            // One scope for every check, so a scoped dependency two checks share is one instance and
            // nothing a check resolves outlives the run.
            AsyncServiceScope scope = services.CreateAsyncScope();
            await using (scope.ConfigureAwait(false))
            {
                List<CompositionProblem> problems = [];

                IReadOnlyList<IStartupCheck> checks;
                try
                {
                    checks = [.. scope.ServiceProvider.GetServices<IStartupCheck>()];
                }
                catch (InvalidOperationException exception)
                {
                    // A check whose own dependencies cannot be resolved takes the whole list with it.
                    return [new CompositionProblem(
                        CompositionNames.Source,
                        $"The startup checks could not be created: {exception.Message}",
                        "Register every service the startup checks depend on.")];
                }

                foreach (IStartupCheck check in checks)
                {
                    if (!includeInfrastructure && check.RequiresInfrastructure)
                    {
                        continue;
                    }

                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        problems.AddRange(await check.CheckAsync(scope.ServiceProvider, cancellationToken).ConfigureAwait(false));
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
                    {
                        // A crashing check is one more problem, not the end of the run: the point of the
                        // gate is that every problem is reported at once.
                        TellmaCompositionLog.CheckThrew(logger, check.Name, exception);
                        problems.Add(new CompositionProblem(
                            check.Name,
                            $"The check failed with {exception.GetType().Name}: {exception.Message}",
                            null));
                    }
                }

                return problems;
            }
        }

        /// <inheritdoc />
        public async Task StartingAsync(CancellationToken cancellationToken)
        {
            IReadOnlyList<CompositionProblem> problems = await RunChecksAsync(services, includeInfrastructure: true, cancellationToken).ConfigureAwait(false);
            if (problems.Count > 0)
            {
                throw new TellmaCompositionException(problems);
            }

            TellmaCompositionLog.GatePassed(logger);
        }

        /// <inheritdoc />
        public Task StartAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task StartedAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task StoppingAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task StoppedAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
