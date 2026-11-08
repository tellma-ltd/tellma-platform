// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Tellma.Core.Abstractions.Hosting;

namespace Tellma.Core.Blobs
{
    /// <summary>
    ///     Fills an unset <see cref="FileSystemBlobStoreOptions.RootPath" /> in Development with
    ///     <c>{LocalApplicationData}/Tellma/{slug}/blobs</c>, so a fresh clone stores blobs with no
    ///     configuration. Outside Development it leaves the root unset, for the validator to report.
    /// </summary>
    /// <remarks>
    ///     Runs after every configuration action, so a bound or configured root always wins. The
    ///     environment and the slug are read from the services the composition root registers
    ///     (<see cref="IHostEnvironment" /> and <see cref="DeploymentIdentity" />); a composition
    ///     without them gets no default.
    /// </remarks>
    /// <param name="services">The application's services.</param>
    internal sealed class FileSystemBlobStoreDefaults(IServiceProvider services) : IPostConfigureOptions<FileSystemBlobStoreOptions>
    {
        /// <inheritdoc />
        public void PostConfigure(string? name, FileSystemBlobStoreOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);

            if (!string.IsNullOrWhiteSpace(options.RootPath))
            {
                return;
            }

            IHostEnvironment? environment = services.GetService<IHostEnvironment>();
            DeploymentIdentity? identity = services.GetService<DeploymentIdentity>();
            if (environment is null || identity is null || !environment.IsDevelopment())
            {
                return;
            }

            options.RootPath = DevelopmentRoot(identity.Application);
        }

        /// <summary>The Development root of a distribution.</summary>
        /// <param name="slug">The distribution's slug.</param>
        /// <returns><c>{LocalApplicationData}/Tellma/{slug}/blobs</c>.</returns>
        public static string DevelopmentRoot(string slug)
        {
            // DoNotVerify: the folder may not exist yet on a fresh machine or container; the store
            // creates the directories it needs. An empty result (no home directory) yields a
            // relative path, which the validator then reports.
            string appData = Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify);
            return Path.Combine(appData, "Tellma", slug, "blobs");
        }
    }
}
