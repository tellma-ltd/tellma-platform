// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Tellma.Core.Composition;

namespace Tellma.Module.Gl.Tests
{
    /// <summary>
    ///     A composition of the platform with the GL feature, as a distribution composes it, under
    ///     Development and never started: the suites resolve what the composition registers without
    ///     opening a database.
    /// </summary>
    internal static class GlComposition
    {
        /// <summary>Composes the platform with the GL feature and whatever else the caller adds.</summary>
        /// <param name="compose">Adds features beside GL; nothing when null.</param>
        /// <returns>The realised services, which the caller disposes.</returns>
        public static ServiceProvider Build(Action<TellmaBuilder>? compose = null)
        {
            IConfiguration configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Tellma:PublicOrigin"] = "https://localhost:4200",
                    ["Tellma:Catalog:ConnectionString"] = "Server=(localdb)\\MSSQLLocalDB;Database=TellmaGlCatalog;Trusted_Connection=True;TrustServerCertificate=True",
                    ["Tellma:Sql:Profiles:default"] = "Trusted_Connection=True;TrustServerCertificate=True",
                })
                .Build();

            ServiceCollection services = [];
            _ = services.AddLogging();
            _ = services.AddTellma("acme", configuration, new DevelopmentEnvironment(), tellma =>
            {
                _ = tellma.AddFeature<GlFeature>();
                compose?.Invoke(tellma);
            });
            return services.BuildServiceProvider();
        }

        /// <summary>The host environment the composition runs under: Development.</summary>
        private sealed class DevelopmentEnvironment : IHostEnvironment
        {
            /// <inheritdoc />
            public string EnvironmentName { get; set; } = Environments.Development;

            /// <inheritdoc />
            public string ApplicationName { get; set; } = "Tellma.Module.Gl.Tests";

            /// <inheritdoc />
            public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

            /// <inheritdoc />
            public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        }
    }
}
