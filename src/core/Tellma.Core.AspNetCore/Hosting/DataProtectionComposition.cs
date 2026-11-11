// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Tellma.Core.Abstractions.Composition;

namespace Tellma.Core.AspNetCore.Hosting
{
    /// <summary>
    ///     Data Protection: one application name per distribution, so every instance and every slot
    ///     reads the others' sessions and protected settings, over the key ring the configuration names.
    /// </summary>
    internal static class DataProtectionComposition
    {
        /// <summary>The prefix of the Data Protection application name, which the slug completes.</summary>
        public const string ApplicationNamePrefix = "tellma-";

        /// <summary>Registers Data Protection and its startup check.</summary>
        /// <param name="services">The services.</param>
        /// <param name="slug">The distribution's slug.</param>
        /// <param name="options">The Data Protection options, as configured.</param>
        public static void Register(IServiceCollection services, string slug, DataProtectionHostOptions options)
        {
            // The application name isolates the key ring's payloads; a re-slugged deployment reads every
            // protected value as unset.
            IDataProtectionBuilder builder = services.AddDataProtection().SetApplicationName(ApplicationNamePrefix + slug);
            if (!string.IsNullOrWhiteSpace(options.KeyRingPath))
            {
                _ = builder.PersistKeysToFileSystem(new DirectoryInfo(options.KeyRingPath));
            }

            // Azure Blob storage under a Key Vault key is the Azure bundle's to configure.
            _ = services.AddSingleton<IStartupCheck, DataProtectionCheck>();
        }
    }

    /// <summary>
    ///     <c>host.data-protection</c>: outside Development, a key repository that outlives the process
    ///     is configured. The framework's default repository is per-machine, or in memory, so every
    ///     instance and every restart would reject the others' cookies and lose every protected setting.
    /// </summary>
    /// <param name="keys">The key management options.</param>
    /// <param name="environment">The host environment.</param>
    internal sealed class DataProtectionCheck(IOptions<KeyManagementOptions> keys, IHostEnvironment environment) : IStartupCheck
    {
        /// <summary>The check's name.</summary>
        public const string CheckName = "host.data-protection";

        /// <inheritdoc />
        public string Name => CheckName;

        /// <inheritdoc />
        public bool RequiresInfrastructure => true;

        /// <inheritdoc />
        public Task<IReadOnlyList<CompositionProblem>> CheckAsync(IServiceProvider services, CancellationToken cancellationToken)
        {
            IReadOnlyList<CompositionProblem> problems = environment.IsDevelopment() || keys.Value.XmlRepository is not null
                ? []
                : [new CompositionProblem(
                    CheckName,
                    "No persistent Data Protection key repository is configured, so each instance would keep its own keys and sessions and protected settings would not survive a restart.",
                    $"Set {DataProtectionHostOptions.SectionPath}:KeyRingPath to a shared folder, or {DataProtectionHostOptions.SectionPath}:BlobUri and KeyId with the Azure host bundle.")];
            return Task.FromResult(problems);
        }
    }
}
