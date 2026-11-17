// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Tellma.Core.Abstractions.Tenancy;

namespace Tellma.Distro.Acme.IntegrationTests.Infrastructure
{
    /// <summary>
    ///     Which of the platform's capabilities Acme's composition holds, read once per run from a web
    ///     host built over placeholder settings and never started, so a test that needs a capability
    ///     runs as soon as the composition gains it and is skipped, naming it, until then. A
    ///     capability whose presence cannot be read off the composition counts as present, so its
    ///     tests run and say for themselves.
    /// </summary>
    public static class PlatformSeams
    {
        /// <summary>The order the initializer that binds a tenant request to its tenant user runs at.</summary>
        private const int UserConnectionOrder = 100;

        /// <summary>The name of the provisioning step that makes the seeded administrator a tenant user.</summary>
        private const string BootstrapAdministratorStep = "core.bootstrap-administrator";

        /// <summary>The route the users stack's own-locale action is projected onto, under a surface's group.</summary>
        private const string UserLocaleRoute = "/users/me/locale";

        /// <summary>The composition, read on first use.</summary>
        private static readonly Lazy<Composition> Composed = new(Read, LazyThreadSafetyMode.ExecutionAndPublication);

        /// <summary>The platform's provisioning steps, which every tenant's record lists once they ran.</summary>
        public static IReadOnlyList<string> PlatformSteps { get; } = [BootstrapAdministratorStep, "core.settings", "core.blob-container"];

        /// <summary>
        ///     Whether a request a tenant serves reaches the seeded administrator: an initializer binds
        ///     the request to its tenant user, and a provisioning step makes the administrator one.
        /// </summary>
        public static bool ServesTheAdministrator => Composed.Value.UserConnection && Composed.Value.Steps.Contains(BootstrapAdministratorStep);

        /// <summary>
        ///     Whether an initializer at the user connection's order is composed, so a tenant request makes
        ///     its first round trip to the tenant's database inside the access guard.
        /// </summary>
        public static bool ConnectsTheUser => Composed.Value.UserConnection;

        /// <summary>Whether the users stack's own-locale action is projected onto the web API, for a served administrator.</summary>
        public static bool ServesTheLocaleAction => ServesTheAdministrator && Composed.Value.UserLocaleAction;

        /// <summary>Whether a background worker claims and runs the tenants' jobs.</summary>
        public static bool RunsJobs => Composed.Value.JobWorker;

        /// <summary>Whether every one of the platform's provisioning steps is composed.</summary>
        public static bool ComposesPlatformSteps => PlatformSteps.All(Composed.Value.Steps.Contains);

        /// <summary>Builds Acme's web host over placeholder settings and reads what it composes.</summary>
        /// <returns>What the composition holds.</returns>
        private static Composition Read()
        {
            // Nothing below connects anywhere: the host is built, never started.
            Dictionary<string, string?> values = new(StringComparer.OrdinalIgnoreCase)
            {
                ["Tellma:PublicOrigin"] = AcmeMigrator.PublicOrigin,
                ["Tellma:Catalog:ConnectionString"] = "Data Source=localhost;Initial Catalog=acme_seams;Integrated Security=true;TrustServerCertificate=true",
                ["Tellma:Sql:Profiles:default"] = "Integrated Security=true;TrustServerCertificate=true",
                ["Tellma:Sql:DatabasePrefix"] = "acme_seams_t",
            };

            IServiceCollection? services = null;
            var host = AcmeHost.Build(values, new AcmeHostOptions { Services = composed => services = composed });
            try
            {
                List<ServiceDescriptor> descriptors = [.. services!];
                IReadOnlyList<string> routes = host.RoutePatterns;

                // A host that maps nothing was read wrongly, and would hide every route-keyed capability.
                if (routes.Count == 0)
                {
                    throw new InvalidOperationException("Acme's web host mapped no route, so its composition cannot be read.");
                }

                using IServiceScope scope = host.Services.CreateScope();
                return new Composition(
                    UserConnection: ComposesUserConnection(scope.ServiceProvider),
                    Steps: StepNames(descriptors, scope.ServiceProvider),
                    UserLocaleAction: routes.Any(static pattern => pattern.EndsWith(UserLocaleRoute, StringComparison.OrdinalIgnoreCase)),
                    JobWorker: descriptors.Any(static descriptor => descriptor.ServiceType == typeof(IHostedService) && IsJobWorker(descriptor)));
            }
            finally
            {
                // The host never started, so stopping it is immediate; the run reads this once.
                host.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        }

        /// <summary>Whether an initializer at the user connection's order is composed.</summary>
        /// <param name="services">A scope of the built host.</param>
        /// <returns>True when one is, or when the initializers cannot be built outside a request.</returns>
        private static bool ComposesUserConnection(IServiceProvider services)
        {
            try
            {
                return services.GetServices<IRequestContextInitializer>().Any(static initializer => initializer.Order == UserConnectionOrder);
            }
            catch (InvalidOperationException)
            {
                // An initializer that needs a request's services exists, and may be this one.
                return true;
            }
        }

        /// <summary>The names of the composed provisioning steps.</summary>
        /// <param name="descriptors">The host's registrations; each step is registered as its own class.</param>
        /// <param name="services">A scope of the built host.</param>
        /// <returns>
        ///     The names; when a step cannot be built outside a tenant's run, the platform's steps too,
        ///     since it may be any of them.
        /// </returns>
        private static HashSet<string> StepNames(List<ServiceDescriptor> descriptors, IServiceProvider services)
        {
            HashSet<string> names = new(StringComparer.Ordinal);
            IEnumerable<Type> steps = descriptors
                .Where(static descriptor => !descriptor.IsKeyedService && descriptor.ServiceType == descriptor.ImplementationType)
                .Select(static descriptor => descriptor.ServiceType)
                .Where(static type => typeof(ITenantProvisioningStep).IsAssignableFrom(type));
            foreach (Type step in steps)
            {
                try
                {
                    _ = names.Add(((ITenantProvisioningStep)services.GetRequiredService(step)).Name);
                }
                catch (InvalidOperationException)
                {
                    names.UnionWith(PlatformSteps);
                }
            }

            return names;
        }

        /// <summary>Whether a hosted service's registration is the jobs worker's.</summary>
        /// <param name="descriptor">The registration.</param>
        /// <returns>True when the class it registers is the worker's.</returns>
        private static bool IsJobWorker(ServiceDescriptor descriptor)
        {
            Type? type = descriptor.IsKeyedService
                ? descriptor.KeyedImplementationType ?? descriptor.KeyedImplementationInstance?.GetType()
                : descriptor.ImplementationType ?? descriptor.ImplementationInstance?.GetType();
            return type is { Name: "JobWorker", Namespace: "Tellma.Core.Jobs" };
        }

        /// <summary>What Acme's composition holds.</summary>
        /// <param name="UserConnection">Whether an initializer binds a tenant request to its tenant user.</param>
        /// <param name="Steps">The composed provisioning steps' names.</param>
        /// <param name="UserLocaleAction">Whether the users stack's own-locale action is projected.</param>
        /// <param name="JobWorker">Whether the jobs worker is hosted.</param>
        private sealed record Composition(bool UserConnection, IReadOnlySet<string> Steps, bool UserLocaleAction, bool JobWorker);
    }
}
