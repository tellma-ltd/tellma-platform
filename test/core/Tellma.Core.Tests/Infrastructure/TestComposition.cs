// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Composition;
using Tellma.Core.Composition.Realizers;
using Tellma.Core.Tenancy;

namespace Tellma.Core.Tests.Infrastructure
{
    /// <summary>Runs compositions the way a host does, with test configuration and environments.</summary>
    internal static class TestComposition
    {
        /// <summary>The slug every composition under test uses unless it says otherwise.</summary>
        public const string Slug = "acme";

        /// <summary>The configuration a sound composition passes every built-in check with outside Development.</summary>
        public static readonly IReadOnlyDictionary<string, string?> SoundConfiguration = new Dictionary<string, string?>
        {
            ["Tellma:PublicOrigin"] = "https://acme.app.tellma.com",
            ["Tellma:Catalog:ConnectionString"] = "Data Source=sql01.database.windows.net;Initial Catalog=tellma-acme-catalog;Authentication=Active Directory Managed Identity;Encrypt=True;Max Pool Size=10",
            ["Tellma:Sql:Profiles:default"] = "Authentication=Active Directory Managed Identity;Encrypt=True;Max Pool Size=20",
            ["Tellma:Sql:AllowedServers:0"] = ".database.windows.net",
        };

        /// <summary>The sound configuration with some values added, replaced or removed (a null value removes the key).</summary>
        /// <param name="changes">The changes.</param>
        /// <returns>The configuration's values.</returns>
        public static IReadOnlyDictionary<string, string?> Sound(params (string Key, string? Value)[] changes)
        {
            Dictionary<string, string?> values = new(SoundConfiguration, StringComparer.OrdinalIgnoreCase);
            foreach ((string key, string? value) in changes)
            {
                if (value is null)
                {
                    _ = values.Remove(key);
                }
                else
                {
                    values[key] = value;
                }
            }

            return values;
        }

        /// <summary>
        ///     Replaces the catalog the composed registry reads with an in-memory one, so a host the
        ///     suite starts never reaches for a SQL Server.
        /// </summary>
        /// <param name="services">The composed services.</param>
        /// <param name="catalog">The catalog; an empty, migrated one when null.</param>
        /// <returns>The services, for chaining.</returns>
        public static IServiceCollection UseFakeCatalog(this IServiceCollection services, FakeCatalogReader? catalog = null)
        {
            ArgumentNullException.ThrowIfNull(services);
            return services.Replace(ServiceDescriptor.Singleton<ICatalogReader>(catalog ?? new FakeCatalogReader()));
        }

        /// <summary>Builds configuration from key-value pairs.</summary>
        /// <param name="values">The values.</param>
        /// <returns>The configuration.</returns>
        public static IConfiguration Configuration(IReadOnlyDictionary<string, string?>? values = null)
        {
            return new ConfigurationBuilder().AddInMemoryCollection(values ?? SoundConfiguration).Build();
        }

        /// <summary>
        ///     Composes into a fresh service collection with a sandbox context registered, so only the
        ///     problem under test fails the gate.
        /// </summary>
        /// <param name="compose">The composition method.</param>
        /// <param name="configuration">The configuration; the sound configuration when null.</param>
        /// <param name="environment">The environment; Production when null.</param>
        /// <param name="slug">The slug.</param>
        /// <param name="realizers">The realizers; the built-in ones when null.</param>
        /// <param name="icuProbe">The ICU probe; reports ICU when null.</param>
        /// <param name="services">The collection to compose into; a fresh one when null.</param>
        /// <returns>The composed services.</returns>
        public static IServiceCollection Compose(
            Action<TellmaBuilder> compose,
            IConfiguration? configuration = null,
            IHostEnvironment? environment = null,
            string slug = Slug,
            ContributionRealizerSet? realizers = null,
            Func<bool>? icuProbe = null,
            IServiceCollection? services = null)
        {
            services ??= new ServiceCollection().AddSingleton(SandboxContext.Never);
            return TellmaServiceCollectionExtensions.AddTellma(
                services,
                slug,
                configuration ?? Configuration(),
                environment ?? new TestEnvironment(),
                compose,
                realizers ?? ContributionRealizerSet.CreateBuiltIn(),
                icuProbe ?? (static () => true))
                .UseFakeCatalog();
        }

        /// <summary>Composes, expecting the composition root to throw, and returns the problems.</summary>
        /// <param name="compose">The composition method.</param>
        /// <param name="slug">The slug.</param>
        /// <param name="realizers">The realizers; the built-in ones when null.</param>
        /// <param name="environment">The environment; Production when null.</param>
        /// <returns>The problems thrown.</returns>
        public static IReadOnlyList<CompositionProblem> Problems(
            Action<TellmaBuilder> compose,
            string slug = Slug,
            ContributionRealizerSet? realizers = null,
            IHostEnvironment? environment = null)
        {
            TellmaCompositionException exception = Assert.Throws<TellmaCompositionException>(
                () => Compose(compose, slug: slug, realizers: realizers, environment: environment));
            return exception.Problems;
        }

        /// <summary>Composes, builds the provider and runs every startup check.</summary>
        /// <param name="compose">The composition method.</param>
        /// <param name="configuration">The configuration; the sound configuration when null.</param>
        /// <param name="environment">The environment; Production when null.</param>
        /// <param name="icuProbe">The ICU probe; reports ICU when null.</param>
        /// <param name="services">The collection to compose into; a fresh one with a sandbox context when null.</param>
        /// <returns>The problems the checks reported.</returns>
        public static async Task<IReadOnlyList<CompositionProblem>> CheckAsync(
            Action<TellmaBuilder> compose,
            IConfiguration? configuration = null,
            IHostEnvironment? environment = null,
            Func<bool>? icuProbe = null,
            IServiceCollection? services = null)
        {
            IServiceCollection composed = Compose(compose, configuration, environment, icuProbe: icuProbe, services: services);
            await using ServiceProvider provider = composed.BuildServiceProvider(validateScopes: true);
            return await TellmaStartupGate.RunChecksAsync(provider, includeInfrastructure: true, TestContext.Current.CancellationToken);
        }

        /// <summary>The problems of one source among <paramref name="problems" />.</summary>
        /// <param name="problems">The problems.</param>
        /// <param name="source">The source.</param>
        /// <returns>The source's problems.</returns>
        public static List<CompositionProblem> From(this IReadOnlyList<CompositionProblem> problems, string source)
        {
            return [.. problems.Where(problem => problem.Source == source)];
        }
    }

    /// <summary>An <see cref="IHostEnvironment" /> a test sets directly.</summary>
    /// <param name="environmentName">The environment name.</param>
    /// <param name="contentRootPath">The content root.</param>
    internal sealed class TestEnvironment(string environmentName = "Production", string? contentRootPath = null) : IHostEnvironment
    {
        /// <inheritdoc />
        public string EnvironmentName { get; set; } = environmentName;

        /// <inheritdoc />
        public string ApplicationName { get; set; } = "Tellma.Core.Tests";

        /// <inheritdoc />
        public string ContentRootPath { get; set; } = contentRootPath ?? AppContext.BaseDirectory;

        /// <inheritdoc />
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
