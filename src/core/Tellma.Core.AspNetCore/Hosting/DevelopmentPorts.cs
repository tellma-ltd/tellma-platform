// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.Hosting;
using System.Globalization;

namespace Tellma.Core.AspNetCore.Hosting
{
    /// <summary>
    ///     The per-worktree ports of local development. A gitignored <c>.dev-ports.local</c> at the
    ///     repository root names the web host's port (<c>&lt;SLUG&gt;_WEB</c>) and the client dev
    ///     server's (<c>&lt;SLUG&gt;_CLIENT</c>), so several worktrees of one distribution run side by
    ///     side. In Development the web host listens on the web port, and its public origin is the
    ///     client's, the origin the browser sees.
    /// </summary>
    /// <remarks>
    ///     The derived values sit below the environment's variables and the command line, so
    ///     <c>Tellma__PublicOrigin</c> and <c>--urls</c> still win, and above the JSON files and the
    ///     launch profile, whose ports are the fallbacks used when the file is absent.
    /// </remarks>
    internal static class DevelopmentPorts
    {
        /// <summary>The file's name.</summary>
        public const string FileName = ".dev-ports.local";

        /// <summary>The configuration key the server's listening addresses are read from.</summary>
        public const string UrlsKey = "urls";

        /// <summary>The configuration key of the public origin.</summary>
        public const string PublicOriginKey = "Tellma:PublicOrigin";

        /// <summary>The key of the web host's port.</summary>
        /// <param name="slug">The distribution's slug.</param>
        /// <returns>The key, such as <c>ACME_WEB</c>.</returns>
        public static string WebKey(string slug)
        {
            return KeyPrefix(slug) + "_WEB";
        }

        /// <summary>The key of the client dev server's port.</summary>
        /// <param name="slug">The distribution's slug.</param>
        /// <returns>The key, such as <c>ACME_CLIENT</c>.</returns>
        public static string ClientKey(string slug)
        {
            return KeyPrefix(slug) + "_CLIENT";
        }

        /// <summary>
        ///     In Development, adds the values derived from the worktree's ports file to the builder's
        ///     configuration; elsewhere, or without the file, changes nothing.
        /// </summary>
        /// <param name="builder">The web application builder.</param>
        /// <param name="slug">The distribution's slug.</param>
        /// <exception cref="InvalidOperationException">The file names a port that is not a port.</exception>
        public static void Apply(WebApplicationBuilder builder, string slug)
        {
            ArgumentNullException.ThrowIfNull(builder);
            if (!builder.Environment.IsDevelopment() || Find(builder.Environment.ContentRootPath) is not { } file)
            {
                return;
            }

            Dictionary<string, string?> derived = Derive(slug, Parse(File.ReadAllText(file)), file);
            if (derived.Count == 0)
            {
                return;
            }

            // Above the JSON files, the user secrets and the launch profile's ASPNETCORE_URLS (read
            // through the host's prefixed variables, the first sources); below the unprefixed
            // environment variables and the command line, which follow the files.
            IList<IConfigurationSource> sources = builder.Configuration.Sources;
            int index = IndexAboveFiles(sources);
            sources.Insert(index, new MemoryConfigurationSource { InitialData = derived });
        }

        /// <summary>
        ///     Finds the ports file: in the content root or the nearest directory above it, up to and
        ///     including the repository's root, the first directory that holds <c>.git</c>.
        /// </summary>
        /// <param name="contentRoot">The host's content root.</param>
        /// <returns>The file's path, or null when there is none.</returns>
        public static string? Find(string contentRoot)
        {
            for (DirectoryInfo? directory = new(contentRoot); directory is not null; directory = directory.Parent)
            {
                string candidate = Path.Combine(directory.FullName, FileName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                // A worktree's .git is a file and a clone's a directory; either ends the search.
                string git = Path.Combine(directory.FullName, ".git");
                if (Directory.Exists(git) || File.Exists(git))
                {
                    return null;
                }
            }

            return null;
        }

        /// <summary>
        ///     Reads the file's <c>KEY=VALUE</c> lines; blank lines and <c>#</c> comments are skipped and
        ///     a value may be quoted, as the client tooling reads the same file.
        /// </summary>
        /// <param name="text">The file's text.</param>
        /// <returns>The values by key.</returns>
        public static Dictionary<string, string> Parse(string text)
        {
            ArgumentNullException.ThrowIfNull(text);
            Dictionary<string, string> values = new(StringComparer.Ordinal);
            foreach (string raw in text.Split('\n'))
            {
                string line = raw.Trim();
                int equals = line.IndexOf('=', StringComparison.Ordinal);
                if (line.Length == 0 || line.StartsWith('#') || equals <= 0)
                {
                    continue;
                }

                string value = line[(equals + 1)..].Trim();
                if (value.Length >= 2 && (value[0] == '"' || value[0] == '\'') && value[^1] == value[0])
                {
                    value = value[1..^1];
                }

                values[line[..equals].Trim()] = value;
            }

            return values;
        }

        /// <summary>The configuration the ports derive: the listening address and the public origin.</summary>
        /// <param name="slug">The distribution's slug.</param>
        /// <param name="ports">The file's values.</param>
        /// <param name="file">The file's path, named by a failure.</param>
        /// <returns>The derived configuration; empty when the file names neither port.</returns>
        /// <exception cref="InvalidOperationException">A named port is not a port.</exception>
        public static Dictionary<string, string?> Derive(string slug, IReadOnlyDictionary<string, string> ports, string file)
        {
            ArgumentNullException.ThrowIfNull(ports);
            Dictionary<string, string?> derived = new(StringComparer.OrdinalIgnoreCase);
            if (Port(ports, WebKey(slug), file) is { } web)
            {
                derived[UrlsKey] = string.Create(CultureInfo.InvariantCulture, $"https://localhost:{web}");
            }

            if (Port(ports, ClientKey(slug), file) is { } client)
            {
                derived[PublicOriginKey] = string.Create(CultureInfo.InvariantCulture, $"https://localhost:{client}");
            }

            return derived;
        }

        /// <summary>One port the file names, or null when it names none under the key.</summary>
        /// <param name="ports">The file's values.</param>
        /// <param name="key">The key.</param>
        /// <param name="file">The file's path.</param>
        /// <returns>The port, or null.</returns>
        /// <exception cref="InvalidOperationException">The value is not a port.</exception>
        private static int? Port(IReadOnlyDictionary<string, string> ports, string key, string file)
        {
            return !ports.TryGetValue(key, out string? value) || value.Length == 0
                ? null
                : int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int port) && port is > 0 and <= 65535
                    ? port
                    : throw new InvalidOperationException($"{file} sets {key} to '{value}', which is not a TCP port; fix or delete the line.");
        }

        /// <summary>The upper-case key prefix of a slug: <c>acme</c> answers <c>ACME</c>, <c>my-erp</c> answers <c>MY_ERP</c>.</summary>
        /// <param name="slug">The slug.</param>
        /// <returns>The prefix.</returns>
        private static string KeyPrefix(string slug)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(slug);
            return slug.ToUpperInvariant().Replace('-', '_');
        }

        /// <summary>
        ///     Where the derived values go: right above the last configuration file (the JSON files and
        ///     the user secrets), so every source added after the files, the environment's variables,
        ///     the command line and a test's own values among them, still wins; first when there is no
        ///     file.
        /// </summary>
        /// <param name="sources">The configuration's sources.</param>
        /// <returns>The index to insert at.</returns>
        private static int IndexAboveFiles(IList<IConfigurationSource> sources)
        {
            for (int i = sources.Count - 1; i >= 0; i--)
            {
                if (sources[i] is FileConfigurationSource)
                {
                    return i + 1;
                }
            }

            return 0;
        }
    }
}
