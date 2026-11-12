// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Tellma.Core.Abstractions.Client;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Composition;
using Tellma.Core.Migrator.CommandLine;
using Tellma.Core.Migrator.Emit;

namespace Tellma.Core.Migrator.Commands
{
    /// <summary>
    ///     <c>emit-client-assets &lt;dir&gt;</c>: realises the composition with no host, database or
    ///     connection, runs the startup checks that read the composition alone, runs every client
    ///     asset contributor and writes the client metadata into the generated folder (spec 0011
    ///     §6.1, spec 0024 §3.2).
    /// </summary>
    internal static class EmitClientAssetsCommand
    {
        /// <summary>The source every problem of the emit's own names.</summary>
        public const string ProblemSource = "emit-client-assets";

        /// <summary>The only configuration file the emit reads.</summary>
        public const string SettingsFile = "appsettings.json";

        /// <summary>Runs the command.</summary>
        /// <param name="line">The parsed command line.</param>
        /// <param name="slug">The distribution slug.</param>
        /// <param name="compose">The distribution's composition method.</param>
        /// <param name="settings">How the run is hosted.</param>
        /// <param name="output">Where the written files are listed.</param>
        /// <param name="error">Where composition problems are written.</param>
        /// <param name="cancellationToken">Cancels the checks.</param>
        /// <returns><see cref="MigratorExitCodes.Converged" /> when written, else <see cref="MigratorExitCodes.CompositionProblems" />.</returns>
        public static async Task<int> RunAsync(
            EmitClientAssetsCommandLine line,
            string slug,
            Action<TellmaBuilder> compose,
            MigratorRunSettings settings,
            TextWriter output,
            TextWriter error,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(line);
            ArgumentNullException.ThrowIfNull(settings);
            ArgumentNullException.ThrowIfNull(output);
            ArgumentNullException.ThrowIfNull(error);

            // --- Steps 1 to 4 of the composition, over appsettings.json alone ---
            string contentRoot = settings.ContentRootPath ?? AppContext.BaseDirectory;
            ConfigurationBuilder configurationBuilder = new();
            _ = configurationBuilder.SetBasePath(contentRoot).AddJsonFile(SettingsFile, optional: true, reloadOnChange: false);
            settings.ConfigureConfiguration?.Invoke(configurationBuilder);
            IConfigurationRoot configuration = configurationBuilder.Build();
            EmitEnvironment environment = new(settings.EnvironmentName ?? Environments.Production, contentRoot);

            ServiceCollection services = new();
            _ = services.AddLogging();
            _ = services.AddSingleton<IConfiguration>(configuration);
            _ = services.AddSingleton<IHostEnvironment>(environment);
            try
            {
                _ = services.AddTellma(slug, configuration, environment, compose);
            }
            catch (TellmaCompositionException exception)
            {
                await error.WriteLineAsync(exception.Message).ConfigureAwait(false);
                return MigratorExitCodes.CompositionProblems;
            }

            ServiceProvider provider = services.BuildServiceProvider();
            await using (provider.ConfigureAwait(false))
            {
                // --- The checks that read the composition alone, then the contributors ---
                List<CompositionProblem> problems = [.. await TellmaStartupGate.RunChecksAsync(provider, includeInfrastructure: false, cancellationToken).ConfigureAwait(false)];
                ClientAssetContext context = new(provider);
                problems.AddRange(Contribute(provider, context));

                ClientAssetBundle? bundle = null;
                if (problems.Count == 0)
                {
                    bundle = ClientAssetBundle.Create(context.Sections, context.Files, out IReadOnlyList<string> bundleProblems);
                    problems.AddRange(bundleProblems.Select(static problem => new CompositionProblem(ProblemSource, problem, null)));
                }

                if (bundle is null || problems.Count > 0)
                {
                    await error.WriteLineAsync(new TellmaCompositionException(problems).Message).ConfigureAwait(false);
                    return MigratorExitCodes.CompositionProblems;
                }

                // --- The files ---
                foreach (string written in await WriteAsync(Path.GetFullPath(line.Directory), bundle, cancellationToken).ConfigureAwait(false))
                {
                    await output.WriteLineAsync(written).ConfigureAwait(false);
                }

                return MigratorExitCodes.Converged;
            }
        }

        /// <summary>Runs every contributor in registration order, which is feature order; a contributor that throws is a problem.</summary>
        /// <param name="services">The realised composition's services.</param>
        /// <param name="context">The emit's context.</param>
        /// <returns>Every problem.</returns>
        private static List<CompositionProblem> Contribute(IServiceProvider services, ClientAssetContext context)
        {
            List<CompositionProblem> problems = [];
            IReadOnlyList<IClientAssetContributor> contributors;
            try
            {
                contributors = [.. services.GetServices<IClientAssetContributor>()];
            }
            catch (Exception exception)
            {
                problems.Add(new CompositionProblem(
                    ProblemSource,
                    $"The client asset contributors could not be created: {exception.GetType().Name}: {exception.Message}",
                    "Register every service the contributors depend on, and keep their constructors from throwing."));
                return problems;
            }

            foreach (IClientAssetContributor contributor in contributors)
            {
                try
                {
                    contributor.Contribute(context);
                }
                catch (Exception exception)
                {
                    // Every contributor runs, so one build reports every broken section at once.
                    problems.Add(new CompositionProblem(
                        ProblemSource,
                        $"The client asset contributor {contributor.GetType().FullName} failed: {exception.GetType().Name}: {exception.Message}",
                        null));
                }
            }

            return problems;
        }

        /// <summary>
        ///     Writes the bundle into the generated folder: each file whose bytes changed, then the
        ///     removal of every emitted asset this bundle no longer names, so an unchanged build
        ///     touches nothing.
        /// </summary>
        /// <param name="directory">The generated folder's full path.</param>
        /// <param name="bundle">The files.</param>
        /// <param name="cancellationToken">Cancels the writes.</param>
        /// <returns>One line per file: written, unchanged or removed.</returns>
        private static async Task<List<string>> WriteAsync(string directory, ClientAssetBundle bundle, CancellationToken cancellationToken)
        {
            List<string> lines = [];

            // --- Every file, written when its bytes differ ---
            foreach (EmittedFile file in bundle.Files)
            {
                string path = Path.Combine(directory, file.RelativePath.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(path) && (await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false)).AsSpan().SequenceEqual(file.Content.Span))
                {
                    lines.Add($"unchanged {file.RelativePath}");
                    continue;
                }

                _ = Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllBytesAsync(path, file.Content, cancellationToken).ConfigureAwait(false);
                lines.Add($"wrote {file.RelativePath}");
            }

            // --- Stale assets of an earlier emit ---
            string assets = Path.Combine(directory, ClientAssetBundle.AssetsFolder);
            HashSet<string> current = [.. bundle.Files.Select(static file => file.RelativePath)];
            foreach (string path in Directory.EnumerateFiles(assets).Order(StringComparer.Ordinal))
            {
                string name = Path.GetFileName(path);
                string relative = $"{ClientAssetBundle.AssetsFolder}/{name}";
                if (ClientAssetBundle.IsEmittedAsset(name) && !current.Contains(relative))
                {
                    File.Delete(path);
                    lines.Add($"removed {relative}");
                }
            }

            return lines;
        }

        /// <summary>The environment of an emit: named by the run, with no content files.</summary>
        /// <param name="environmentName">The environment's name.</param>
        /// <param name="contentRootPath">The directory the configuration file was read from.</param>
        private sealed class EmitEnvironment(string environmentName, string contentRootPath) : IHostEnvironment
        {
            /// <inheritdoc />
            public string EnvironmentName { get; set; } = environmentName;

            /// <inheritdoc />
            public string ApplicationName { get; set; } = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name ?? string.Empty;

            /// <inheritdoc />
            public string ContentRootPath { get; set; } = contentRootPath;

            /// <inheritdoc />
            public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        }
    }
}
