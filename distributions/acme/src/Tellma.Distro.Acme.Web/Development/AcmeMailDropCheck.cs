// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Composition;

namespace Tellma.Distro.Acme.Web.Development
{
    /// <summary>
    ///     The development mail drop's startup check: outside Development neither of its settings may
    ///     be present; in Development, the provider and an absolute directory come together or not at
    ///     all, so a drop the suites wait on is never silently replaced by the log sink.
    /// </summary>
    internal sealed class AcmeMailDropCheck : IStartupCheck
    {
        /// <inheritdoc />
        public string Name => AcmeMailDrop.CheckName;

        /// <inheritdoc />
        public bool RequiresInfrastructure => true;

        /// <inheritdoc />
        public Task<IReadOnlyList<CompositionProblem>> CheckAsync(IServiceProvider services, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(services);
            IConfiguration configuration = services.GetRequiredService<IConfiguration>();
            IHostEnvironment environment = services.GetRequiredService<IHostEnvironment>();
            return Task.FromResult(Check(configuration[AcmeMailDrop.ProviderKey], configuration[AcmeMailDrop.DirectoryKey], environment));
        }

        /// <summary>The check's rules over the two settings.</summary>
        /// <param name="provider">The configured email provider.</param>
        /// <param name="directory">The configured drop directory.</param>
        /// <param name="environment">The host environment.</param>
        /// <returns>The problems; empty when the settings agree.</returns>
        internal static IReadOnlyList<CompositionProblem> Check(string? provider, string? directory, IHostEnvironment environment)
        {
            List<CompositionProblem> problems = [];
            bool selected = string.Equals(provider?.Trim(), AcmeMailDrop.TransportName, StringComparison.OrdinalIgnoreCase);
            bool directorySet = !string.IsNullOrWhiteSpace(directory);

            // --- Outside Development: neither setting, since the drop would swallow real mail ---
            if (!environment.IsDevelopment())
            {
                if (selected)
                {
                    problems.Add(new CompositionProblem(
                        AcmeMailDrop.CheckName,
                        $"{AcmeMailDrop.ProviderKey} is '{AcmeMailDrop.TransportName}' in the '{environment.EnvironmentName}' environment. The development mail drop writes every message to disk and delivers none, so it runs only in Development.",
                        $"Set {AcmeMailDrop.ProviderKey} to a real transport in this environment's configuration."));
                }

                if (directorySet)
                {
                    problems.Add(new CompositionProblem(
                        AcmeMailDrop.CheckName,
                        $"{AcmeMailDrop.DirectoryKey} is set in the '{environment.EnvironmentName}' environment, but the development mail drop runs only in Development.",
                        $"Remove {AcmeMailDrop.DirectoryKey} from this environment's configuration."));
                }

                return problems;
            }

            // --- In Development: both settings or neither, and the directory absolute ---
            if (selected && !directorySet)
            {
                problems.Add(new CompositionProblem(
                    AcmeMailDrop.CheckName,
                    $"{AcmeMailDrop.ProviderKey} is '{AcmeMailDrop.TransportName}', but {AcmeMailDrop.DirectoryKey} is not set, so the drop has nowhere to write.",
                    $"Set {AcmeMailDrop.DirectoryKey} to an absolute directory."));
            }
            else if (directorySet && !selected)
            {
                problems.Add(new CompositionProblem(
                    AcmeMailDrop.CheckName,
                    $"{AcmeMailDrop.DirectoryKey} is set, but {AcmeMailDrop.ProviderKey} is '{provider}', so no message would reach the drop.",
                    $"Set {AcmeMailDrop.ProviderKey} to '{AcmeMailDrop.TransportName}', or remove {AcmeMailDrop.DirectoryKey}."));
            }
            else if (directorySet && !Path.IsPathFullyQualified(directory!))
            {
                problems.Add(new CompositionProblem(
                    AcmeMailDrop.CheckName,
                    $"{AcmeMailDrop.DirectoryKey} is '{directory}', which is not an absolute path.",
                    $"Set {AcmeMailDrop.DirectoryKey} to an absolute directory."));
            }

            return problems;
        }
    }
}
