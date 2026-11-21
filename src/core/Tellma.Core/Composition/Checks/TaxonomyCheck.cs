// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Hosting;
using System.Text.Json;
using Tellma.Core.Abstractions.Composition;

namespace Tellma.Core.Composition.Checks
{
    /// <summary>
    ///     <c>core.taxonomy</c>: in Development, the slug is listed in the <c>distributions</c> of the
    ///     repository's <c>taxonomy.json</c>, so a distribution developed inside the platform
    ///     repository cannot ship unregistered.
    /// </summary>
    /// <remarks>
    ///     The repository root is the nearest directory at or above the content root that holds a
    ///     <c>.git</c> entry (a directory, or a file in a worktree). Outside Development, outside a
    ///     repository, or in a repository without the file, the check reports nothing.
    /// </remarks>
    /// <param name="record">The realised composition.</param>
    internal sealed class TaxonomyCheck(TellmaCompositionRecord record) : IStartupCheck
    {
        /// <summary>The file's name at the repository root.</summary>
        public const string FileName = "taxonomy.json";

        /// <inheritdoc />
        public string Name => CoreCheckNames.Taxonomy;

        /// <inheritdoc />
        public bool RequiresInfrastructure => false;

        /// <inheritdoc />
        public async Task<IReadOnlyList<CompositionProblem>> CheckAsync(IServiceProvider services, CancellationToken cancellationToken)
        {
            if (!record.Environment.IsDevelopment()
                || FindRepositoryRoot(record.Environment.ContentRootPath) is not string root
                || Path.Combine(root, FileName) is not string path
                || !File.Exists(path))
            {
                return [];
            }

            // --- Read the registry ---
            List<string> distributions = [];
            try
            {
                FileStream stream = File.OpenRead(path);
                await using (stream.ConfigureAwait(false))
                {
                    using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
                    if (document.RootElement.ValueKind != JsonValueKind.Object
                        || !document.RootElement.TryGetProperty("distributions", out JsonElement listed)
                        || listed.ValueKind != JsonValueKind.Array)
                    {
                        return [Unreadable(path, "it has no 'distributions' array")];
                    }

                    foreach (JsonElement entry in listed.EnumerateArray())
                    {
                        if (entry.ValueKind == JsonValueKind.String)
                        {
                            distributions.Add(entry.GetString()!);
                        }
                    }
                }
            }
            catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
            {
                return [Unreadable(path, exception.Message)];
            }

            // --- The slug must be listed ---
            return distributions.Contains(record.Slug, StringComparer.Ordinal)
                ? []
                : [new CompositionProblem(
                    Name,
                    $"The distribution slug '{record.Slug}' is not listed in the distributions of {path}.",
                    $"Add \"{record.Slug}\" to the \"distributions\" array of {FileName} at the repository root.")];
        }

        /// <summary>The nearest directory at or above <paramref name="start" /> that holds a <c>.git</c> entry.</summary>
        /// <param name="start">The directory to start from.</param>
        /// <returns>The repository root, or null outside a repository.</returns>
        internal static string? FindRepositoryRoot(string? start)
        {
            if (string.IsNullOrWhiteSpace(start))
            {
                return null;
            }

            for (DirectoryInfo? directory = new(start); directory is not null; directory = directory.Parent)
            {
                string git = Path.Combine(directory.FullName, ".git");
                if (Directory.Exists(git) || File.Exists(git))
                {
                    return directory.FullName;
                }
            }

            return null;
        }

        /// <summary>The problem of a registry file that exists but cannot be read as a registry.</summary>
        /// <param name="path">The file's path.</param>
        /// <param name="reason">Why it cannot be read.</param>
        /// <returns>The problem.</returns>
        private CompositionProblem Unreadable(string path, string reason)
        {
            return new CompositionProblem(
                Name,
                $"The registry {path} cannot be read: {reason}.",
                $"Restore {FileName} to a JSON object whose \"distributions\" array lists every distribution slug the repository hosts.");
        }
    }
}
