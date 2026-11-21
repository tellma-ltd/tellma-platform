// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
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
                using IServiceScope scope = host.Services.CreateScope();
                return new Composition(
                    UserConnection: ComposesUserConnection(scope.ServiceProvider),
                    Steps: StepNames(descriptors, scope.ServiceProvider));
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

        /// <summary>What Acme's composition holds.</summary>
        /// <param name="UserConnection">Whether an initializer binds a tenant request to its tenant user.</param>
        /// <param name="Steps">The composed provisioning steps' names.</param>
        private sealed record Composition(bool UserConnection, IReadOnlySet<string> Steps);
    }
}
