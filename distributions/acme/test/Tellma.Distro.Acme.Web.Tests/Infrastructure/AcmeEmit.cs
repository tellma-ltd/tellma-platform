// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Configuration;
using System.Globalization;
using Tellma.Core.Migrator;
using Tellma.Distro.Acme.Migrator;
using Tellma.Identity.Migrations;

namespace Tellma.Distro.Acme.Web.Tests.Infrastructure
{
    /// <summary>
    ///     Acme's <c>emit-client-assets</c>, run in process as the migrator's <c>Program.cs</c> runs it,
    ///     over the web project's production configuration and a catalog nothing answers, into a
    ///     temporary folder the emit deletes when disposed.
    /// </summary>
    internal sealed class AcmeEmit : IDisposable
    {
        /// <summary>Initializes an emit into a folder of its own, which does not exist yet.</summary>
        private AcmeEmit()
        {
            Output = Path.Combine(Path.GetTempPath(), "tellma-acme-emit-" + Guid.NewGuid().ToString("N"));
        }

        /// <summary>The folder the emit wrote.</summary>
        public string Output { get; }

        /// <summary>Runs the emit, failing the test when it does not exit 0.</summary>
        /// <param name="cancellationToken">Cancels the emit.</param>
        /// <returns>The finished emit.</returns>
        public static async Task<AcmeEmit> RunAsync(CancellationToken cancellationToken)
        {
            AcmeEmit emit = new();
            using StringWriter written = new(CultureInfo.InvariantCulture);
            using StringWriter errors = new(CultureInfo.InvariantCulture);

            // The production configuration the client build runs with, over a catalog nothing answers.
            int exitCode = await TellmaMigrator.RunAsync(
                ["emit-client-assets", emit.Output],
                AcmeComposition.Slug,
                AcmeComposition.Compose,
                TellmaIdentityStore.MigrateAsync,
                new MigratorRunSettings
                {
                    MigrationsAssembly = typeof(AcmeDesignTimeFactory).Assembly,
                    Out = written,
                    Error = errors,
                    ContentRootPath = AcmePaths.Web,
                    ConfigureConfiguration = static configuration => configuration.AddInMemoryCollection(
                    [
                        new KeyValuePair<string, string?>("Tellma:Catalog:ConnectionString", "Server=tcp:127.0.0.1,9;Database=unreachable;Integrated Security=True;Connect Timeout=1"),
                    ]),
                },
                cancellationToken);

            Assert.True(exitCode == 0, $"emit-client-assets exited {exitCode}:\n{written}\n{errors}");
            return emit;
        }

        /// <summary>Every file the emit wrote, by its path relative to the output, with its bytes.</summary>
        /// <returns>The files, in ordinal order of their paths.</returns>
        public SortedDictionary<string, byte[]> Files()
        {
            SortedDictionary<string, byte[]> files = new(StringComparer.Ordinal);
            foreach (string file in Directory.EnumerateFiles(Output, "*", SearchOption.AllDirectories))
            {
                files[Path.GetRelativePath(Output, file).Replace('\\', '/')] = File.ReadAllBytes(file);
            }

            return files;
        }

        /// <summary>The emitted chunk whose name starts with a prefix, such as <c>core-</c>.</summary>
        /// <param name="prefix">The chunk's name before its hash.</param>
        /// <returns>The chunk's text.</returns>
        public string Chunk(string prefix)
        {
            string file = Assert.Single(Directory.EnumerateFiles(Path.Combine(Output, "assets"), prefix + "*.json"));
            return File.ReadAllText(file);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (Directory.Exists(Output))
            {
                Directory.Delete(Output, recursive: true);
            }
        }
    }
}
