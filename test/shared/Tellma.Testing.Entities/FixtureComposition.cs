// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Tellma.Core.Abstractions.Hosting;
using Tellma.Core.Composition;
using Tellma.Core.Data;

namespace Tellma.Testing.Entities
{
    /// <summary>
    ///     The composition every fixture host is built from: the platform with the
    ///     <see cref="FixtureFeature" />, as a distribution composes its features. The fixture
    ///     context's model is built from the same composition, so the database the fixture migrations
    ///     create is the one a fixture host's model describes, fingerprint included.
    /// </summary>
    public static class FixtureComposition
    {
        /// <summary>The slug the fixture composes under.</summary>
        public const string Slug = "fixture";

        /// <summary>The composition's model definition, from a composition built once and never run.</summary>
        private static readonly Lazy<TellmaModelDefinition> ModelDefinition = new(
            static () =>
            {
                ServiceCollection services = [];
                _ = Compose(services);
                return services
                    .Where(static descriptor => descriptor.ServiceType == typeof(TellmaModelDefinition))
                    .Select(static descriptor => descriptor.ImplementationInstance)
                    .OfType<TellmaModelDefinition>()
                    .Single();
            },
            LazyThreadSafetyMode.ExecutionAndPublication);

        /// <summary>The deployment the fixture's connections are named for.</summary>
        public static DeploymentIdentity Deployment { get; } = new(Slug, Environments.Development);

        /// <summary>The fixture composition's model definition.</summary>
        internal static TellmaModelDefinition Definition => ModelDefinition.Value;

        /// <summary>Composes the platform with the fixture feature into a collection.</summary>
        /// <param name="services">The collection.</param>
        /// <param name="configure">Changes the configuration before composing.</param>
        /// <param name="compose">Adds to the composition beside the fixture feature.</param>
        /// <returns>The same collection.</returns>
        public static IServiceCollection Compose(
            IServiceCollection services,
            Action<Dictionary<string, string?>>? configure = null,
            Action<TellmaBuilder>? compose = null)
        {
            ArgumentNullException.ThrowIfNull(services);

            // The catalog and the profile are never connected to: the fixture binds its tenant
            // database itself, through the connection seams a fixture host replaces.
            Dictionary<string, string?> values = new(StringComparer.OrdinalIgnoreCase)
            {
                ["Tellma:PublicOrigin"] = "https://localhost:4200",
                ["Tellma:Catalog:ConnectionString"] = "Server=(localdb)\\MSSQLLocalDB;Database=TellmaFixtureCatalog;Trusted_Connection=True;TrustServerCertificate=True",
                ["Tellma:Sql:Profiles:default"] = "Trusted_Connection=True;TrustServerCertificate=True",

                // A suite creates databases in parallel on one server, which slows every statement
                // well past a request's budget; the fixture's batches get a generous one.
                ["Tellma:Data:CommandTimeout"] = "00:02:00",
            };
            configure?.Invoke(values);
            IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();

            return services.AddTellma(Slug, configuration, new FixtureEnvironment(), tellma =>
            {
                _ = tellma.AddFeature(new FixtureFeature());
                compose?.Invoke(tellma);
            });
        }

        /// <summary>The host environment the fixture composes under: Development.</summary>
        private sealed class FixtureEnvironment : IHostEnvironment
        {
            /// <inheritdoc />
            public string EnvironmentName { get; set; } = Environments.Development;

            /// <inheritdoc />
            public string ApplicationName { get; set; } = "Tellma.Testing.Entities";

            /// <inheritdoc />
            public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

            /// <inheritdoc />
            public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        }
    }
}
