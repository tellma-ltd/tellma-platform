// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Tellma.Core.Abstractions.Composition;

namespace Tellma.Core.Tests.Infrastructure
{
    /// <summary>A check that always reports one problem.</summary>
    internal sealed class FailingCheck : IStartupCheck
    {
        /// <inheritdoc />
        public string Name => "test.failing";

        /// <inheritdoc />
        public bool RequiresInfrastructure => false;

        /// <inheritdoc />
        public Task<IReadOnlyList<CompositionProblem>> CheckAsync(IServiceProvider services, CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<CompositionProblem>>([new CompositionProblem(Name, "The failing check failed.", "Remove the failing check.")]);
        }
    }

    /// <summary>A check that reads infrastructure and always reports one problem.</summary>
    internal sealed class FailingInfrastructureCheck : IStartupCheck
    {
        /// <inheritdoc />
        public string Name => "test.infrastructure";

        /// <inheritdoc />
        public bool RequiresInfrastructure => true;

        /// <inheritdoc />
        public Task<IReadOnlyList<CompositionProblem>> CheckAsync(IServiceProvider services, CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<CompositionProblem>>([new CompositionProblem(Name, "The store is misconfigured.", "Configure the store.")]);
        }
    }

    /// <summary>A check that throws.</summary>
    internal sealed class ThrowingCheck : IStartupCheck
    {
        /// <inheritdoc />
        public string Name => "test.throwing";

        /// <inheritdoc />
        public bool RequiresInfrastructure => false;

        /// <inheritdoc />
        public Task<IReadOnlyList<CompositionProblem>> CheckAsync(IServiceProvider services, CancellationToken cancellationToken)
        {
            throw new FormatException("The check crashed.");
        }
    }

    /// <summary>A check that throws an exception whose message spans lines.</summary>
    internal sealed class MultiLineThrowingCheck : IStartupCheck
    {
        /// <inheritdoc />
        public string Name => "test.multiline";

        /// <inheritdoc />
        public bool RequiresInfrastructure => false;

        /// <inheritdoc />
        public Task<IReadOnlyList<CompositionProblem>> CheckAsync(IServiceProvider services, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("first error\r\nsecond error");
        }
    }

    /// <summary>A check whose constructor throws.</summary>
    internal sealed class UnconstructibleCheck : IStartupCheck
    {
        /// <summary>Always throws.</summary>
        /// <exception cref="ArgumentException">Always.</exception>
        public UnconstructibleCheck()
        {
            throw new ArgumentException("The check refuses to be created.");
        }

        /// <inheritdoc />
        public string Name => "test.unconstructible";

        /// <inheritdoc />
        public bool RequiresInfrastructure => false;

        /// <inheritdoc />
        public Task<IReadOnlyList<CompositionProblem>> CheckAsync(IServiceProvider services, CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<CompositionProblem>>([]);
        }
    }

    /// <summary>A scoped dependency two checks share.</summary>
    internal sealed class ScopedProbe
    {
        /// <summary>A per-instance identity.</summary>
        public Guid Id { get; } = Guid.NewGuid();
    }

    /// <summary>A check that records the scoped probe it resolves.</summary>
    /// <param name="seen">Where the probe instances are recorded.</param>
    internal sealed class ScopeRecordingCheck(List<Guid> seen) : IStartupCheck
    {
        /// <inheritdoc />
        public string Name => "test.scope";

        /// <inheritdoc />
        public bool RequiresInfrastructure => false;

        /// <inheritdoc />
        public Task<IReadOnlyList<CompositionProblem>> CheckAsync(IServiceProvider services, CancellationToken cancellationToken)
        {
            seen.Add(services.GetRequiredService<ScopedProbe>().Id);
            return Task.FromResult<IReadOnlyList<CompositionProblem>>([]);
        }
    }

    /// <summary>A hosted service that records whether the host started it.</summary>
    internal sealed class ProbeHostedService : IHostedService
    {
        /// <summary>Whether <see cref="StartAsync" /> ran.</summary>
        public bool Started { get; private set; }

        /// <inheritdoc />
        public Task StartAsync(CancellationToken cancellationToken)
        {
            Started = true;
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    /// <summary>A lifecycle hosted service that records whether the host called its first hook.</summary>
    internal sealed class ProbeLifecycleService : IHostedLifecycleService
    {
        /// <summary>Whether <see cref="StartingAsync" /> ran.</summary>
        public bool Starting { get; private set; }

        /// <inheritdoc />
        public Task StartingAsync(CancellationToken cancellationToken)
        {
            Starting = true;
            return Task.CompletedTask;
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
