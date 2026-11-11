// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using System.Diagnostics.Metrics;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Tenancy;
using Tellma.Core.Tests.Infrastructure;

namespace Tellma.Core.Tests.Tenancy
{
    /// <summary>
    ///     The request-context runtime exactly as the composition root registers it, over a fake
    ///     catalog and a fake clock, with the initializers a test registers. Scopes are validated, so a
    ///     scoped service resolved from the root fails the test.
    /// </summary>
    internal sealed class TenantContextHarness : IAsyncDisposable
    {
        /// <summary>The instant the fake clock starts at.</summary>
        public static readonly DateTimeOffset Start = new(2026, 11, 9, 8, 0, 0, TimeSpan.Zero);

        private readonly ServiceProvider _provider;

        /// <summary>Creates the harness over composed services.</summary>
        /// <param name="provider">The composed services.</param>
        /// <param name="time">The clock.</param>
        /// <param name="catalog">The catalog.</param>
        private TenantContextHarness(ServiceProvider provider, FakeTimeProvider time, FakeCatalogReader catalog)
        {
            _provider = provider;
            Time = time;
            Catalog = catalog;
        }

        /// <summary>The clock.</summary>
        public FakeTimeProvider Time { get; }

        /// <summary>The catalog.</summary>
        public FakeCatalogReader Catalog { get; }

        /// <summary>The root services.</summary>
        public IServiceProvider Services => _provider;

        /// <summary>The meter factory the runtime's instruments are created from.</summary>
        public IMeterFactory MeterFactory => _provider.GetRequiredService<IMeterFactory>();

        /// <summary>The registry, loaded with the catalog's rows.</summary>
        public TenantRegistry Registry => _provider.GetRequiredService<TenantRegistry>();

        /// <summary>The scope factory under test.</summary>
        public ITenantScopeFactory Scopes => _provider.GetRequiredService<ITenantScopeFactory>();

        /// <summary>Composes the runtime and loads the first snapshot.</summary>
        /// <param name="rows">The catalog's rows.</param>
        /// <param name="configure">Adds services after the composition, such as initializers.</param>
        /// <returns>The harness.</returns>
        public static async Task<TenantContextHarness> CreateAsync(TenantRow[]? rows = null, Action<IServiceCollection>? configure = null)
        {
            FakeTimeProvider time = new(Start);
            FakeCatalogReader catalog = new();
            catalog.Write(rows ?? []);

            ServiceCollection services = [];
            _ = services.AddSingleton<TimeProvider>(time);
            IServiceCollection composed = TestComposition.Compose(static _ => { }, services: services).UseFakeCatalog(catalog);
            configure?.Invoke(composed);

            ServiceProvider provider = composed.BuildServiceProvider(validateScopes: true);
            await provider.GetRequiredService<TenantRegistry>().InitializeAsync(TestContext.Current.CancellationToken);
            return new TenantContextHarness(provider, time, catalog);
        }

        /// <summary>A descriptor in a state, as the tenant middleware would bind it.</summary>
        /// <param name="state">The state.</param>
        /// <param name="category">The category.</param>
        /// <param name="id">The id.</param>
        /// <returns>The descriptor.</returns>
        public static TenantDescriptor Tenant(TenantState state = TenantState.Active, TenantCategory category = TenantCategory.Live, int id = 17)
        {
            return new TenantDescriptor(id, "Acme", null, null, ["en"], category, state, null);
        }

        /// <summary>Opens a scope and binds it the way the tenant middleware binds a request: the tenant, the principal and the clock.</summary>
        /// <param name="tenant">The tenant.</param>
        /// <param name="kind">The principal's kind.</param>
        /// <param name="subject">The principal's subject.</param>
        /// <param name="assurance">The session's assurance.</param>
        /// <returns>The bound scope.</returns>
        public AsyncServiceScope BindRequest(
            TenantDescriptor tenant,
            PrincipalKind kind = PrincipalKind.User,
            string? subject = "sub-1",
            AuthenticationAssurance? assurance = null)
        {
            AsyncServiceScope scope = _provider.CreateAsyncScope();
            IRequestContextHolder holder = scope.ServiceProvider.GetRequiredService<IRequestContextHolder>();
            holder.Set(holder.Current with
            {
                Tenant = tenant,
                Kind = kind,
                Subject = subject,
                Assurance = assurance,
                Now = Time.GetUtcNow(),
            });
            return scope;
        }

        /// <inheritdoc />
        public ValueTask DisposeAsync()
        {
            return _provider.DisposeAsync();
        }
    }

    /// <summary>What the initializers of one test saw, in the order they ran. A singleton the test reads.</summary>
    internal sealed class InitializerLog
    {
        private readonly Lock _sync = new();
        private readonly List<InitializerCall> _calls = [];

        /// <summary>Every call, in order.</summary>
        public IReadOnlyList<InitializerCall> Calls
        {
            get
            {
                lock (_sync)
                {
                    return [.. _calls];
                }
            }
        }

        /// <summary>The names of the initializers that ran, in order.</summary>
        public string[] Names => [.. Calls.Select(static call => call.Name)];

        /// <summary>Records a call.</summary>
        /// <param name="call">The call.</param>
        public void Add(InitializerCall call)
        {
            lock (_sync)
            {
                _calls.Add(call);
            }
        }
    }

    /// <summary>One initializer call.</summary>
    /// <param name="Name">The initializer's name.</param>
    /// <param name="Context">The context it was given.</param>
    /// <param name="Ambient">The context its scope's accessor answered when it ran.</param>
    /// <param name="Inputs">The inputs it was given.</param>
    /// <param name="CancellationToken">The token it was given.</param>
    internal sealed record InitializerCall(
        string Name, RequestContext Context, RequestContext Ambient, RequestContextInputs Inputs, CancellationToken CancellationToken);

    /// <summary>An initializer that records its call and returns what its script makes of the context.</summary>
    /// <param name="name">The name recorded.</param>
    /// <param name="order">The order.</param>
    /// <param name="script">Turns the context given into the context returned.</param>
    /// <param name="log">The test's log.</param>
    /// <param name="accessor">The scope's accessor.</param>
    internal sealed class ScriptedInitializer(
        string name,
        int order,
        Func<RequestContext, RequestContext> script,
        InitializerLog log,
        IRequestContextAccessor accessor) : IRequestContextInitializer
    {
        /// <inheritdoc />
        public int Order => order;

        /// <inheritdoc />
        public Task<RequestContext> InitializeAsync(RequestContext context, RequestContextInputs inputs, CancellationToken cancellationToken)
        {
            log.Add(new InitializerCall(name, context, accessor.Current, inputs, cancellationToken));
            return Task.FromResult(script(context));
        }
    }

    /// <summary>Registers scripted initializers.</summary>
    internal static class InitializerRegistrations
    {
        /// <summary>The user id the connecting initializer binds.</summary>
        public const int UserId = 5;

        /// <summary>Registers the test's log and an initializer at order 100 that binds the user, as the user connection does.</summary>
        /// <param name="services">The services.</param>
        /// <returns>The services, for chaining.</returns>
        public static IServiceCollection AddConnectingInitializer(this IServiceCollection services)
        {
            return services.AddInitializer("connect", 100, static context => context with { UserId = UserId });
        }

        /// <summary>Registers the test's log, once, and a scripted initializer.</summary>
        /// <param name="services">The services.</param>
        /// <param name="name">The name recorded.</param>
        /// <param name="order">The order.</param>
        /// <param name="script">Turns the context given into the context returned.</param>
        /// <returns>The services, for chaining.</returns>
        public static IServiceCollection AddInitializer(
            this IServiceCollection services, string name, int order, Func<RequestContext, RequestContext> script)
        {
            if (!services.Any(static descriptor => descriptor.ServiceType == typeof(InitializerLog)))
            {
                _ = services.AddSingleton<InitializerLog>();
            }

            // Scoped, like the platform's own initializers, so each takes the accessor of the scope it completes.
            return services.AddScoped<IRequestContextInitializer>(provider => new ScriptedInitializer(
                name,
                order,
                script,
                provider.GetRequiredService<InitializerLog>(),
                provider.GetRequiredService<IRequestContextAccessor>()));
        }
    }
}
