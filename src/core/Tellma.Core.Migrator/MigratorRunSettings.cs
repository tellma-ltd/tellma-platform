// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace Tellma.Core.Migrator
{
    /// <summary>
    ///     How one migrator run is hosted: where its tenant migrations, configuration and output come
    ///     from. A distribution's migrator runs with the defaults; the suites replace them.
    /// </summary>
    internal sealed class MigratorRunSettings
    {
        /// <summary>
        ///     The assembly that carries the tenant model's migrations: the process's entry assembly, which
        ///     is the distribution's migrator project, unless set.
        /// </summary>
        public Assembly? MigrationsAssembly { get; init; }

        /// <summary>Where the report and the command's output are written; the console's standard output unless set.</summary>
        public TextWriter? Out { get; init; }

        /// <summary>Where usage errors and composition problems are written; the console's standard error unless set.</summary>
        public TextWriter? Error { get; init; }

        /// <summary>The host environment's name; the environment variables decide, else Production, unless set.</summary>
        public string? EnvironmentName { get; init; }

        /// <summary>The directory the configuration files are read from; the application's base directory unless set.</summary>
        public string? ContentRootPath { get; init; }

        /// <summary>Adds configuration after the host's own sources and before the migrator's forced settings.</summary>
        public Action<IConfigurationBuilder>? ConfigureConfiguration { get; init; }

        /// <summary>Changes the host's services after the composition, such as replacing the logging.</summary>
        public Action<IServiceCollection>? ConfigureServices { get; init; }

        /// <summary>The tenant migrations' assembly in effect.</summary>
        /// <returns>The assembly.</returns>
        /// <exception cref="InvalidOperationException">No assembly is set and the process has no managed entry assembly.</exception>
        public Assembly ResolveMigrationsAssembly()
        {
            return MigrationsAssembly
                ?? Assembly.GetEntryAssembly()
                ?? throw new InvalidOperationException("The migrator runs from a distribution's migrator project, whose entry assembly carries the tenant migrations.");
        }
    }
}
