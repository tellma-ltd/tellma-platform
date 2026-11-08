// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Buffers;
using System.Text.Json;

namespace Tellma.Core.Abstractions.Client
{
    /// <summary>
    ///     What an <see cref="IClientAssetContributor" /> writes into: named sections of the client
    ///     metadata chunks, and generated source files for the distribution's client project. The emit
    ///     creates one context, runs every contributor against it, then writes the chunks and files.
    /// </summary>
    /// <remarks>
    ///     Each section is written by exactly one contributor, and each file by exactly one: a second
    ///     write of the same section or path throws, because two owners of one fact would make the
    ///     emitted metadata depend on contributor order.
    /// </remarks>
    /// <param name="services">The realised composition's services.</param>
    public sealed class ClientAssetContext(IServiceProvider services)
    {
        // Characters a generated file path never carries, whatever the operating system: the
        // emitted tree must be identical on every build machine.
        private static readonly SearchValues<char> ForbiddenPathCharacters = SearchValues.Create("\\<>:\"|?*");

        private readonly List<ClientMetadataSection> _sections = [];
        private readonly List<ClientGeneratedFile> _files = [];

        /// <summary>
        ///     The realised composition's services. Contributors read registries and options from
        ///     it; it reaches no database or other store.
        /// </summary>
        public IServiceProvider Services { get; } = services ?? throw new ArgumentNullException(nameof(services));

        /// <summary>The sections written so far, in the order they were written.</summary>
        public IReadOnlyList<ClientMetadataSection> Sections => _sections;

        /// <summary>The files written so far, in the order they were written.</summary>
        public IReadOnlyList<ClientGeneratedFile> Files => _files;

        /// <summary>
        ///     Writes one member of a metadata chunk. <paramref name="write" /> runs at once and must
        ///     write exactly one complete JSON value: the member's value, without its name.
        /// </summary>
        /// <param name="chunk">The chunk the member belongs to.</param>
        /// <param name="name">The member's name: camelCase, starting with a lowercase letter.</param>
        /// <param name="write">Writes the member's value.</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="chunk" /> is not a defined chunk.</exception>
        /// <exception cref="ArgumentException"><paramref name="name" /> is not a camelCase identifier.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="write" /> is null.</exception>
        /// <exception cref="InvalidOperationException">The member was already written, or <paramref name="write" /> did not write exactly one complete value.</exception>
        public void Section(ClientMetadataChunk chunk, string name, Action<Utf8JsonWriter> write)
        {
            // --- Argument validation ---
            if (!Enum.IsDefined(chunk))
            {
                throw new ArgumentOutOfRangeException(nameof(chunk), chunk, "The chunk is not a defined client metadata chunk.");
            }

            if (!IsCamelCaseIdentifier(name))
            {
                throw new ArgumentException(
                    $"A client metadata section name is a camelCase identifier starting with a lowercase letter; received '{name}'.",
                    nameof(name));
            }

            ArgumentNullException.ThrowIfNull(write);

            if (_sections.Any(section => section.Chunk == chunk && string.Equals(section.Name, name, StringComparison.Ordinal)))
            {
                throw new InvalidOperationException($"The {chunk} chunk's '{name}' section was already written.");
            }

            // --- Capture the value ---
            // The writer validates structure, so a second root value or an unbalanced object throws
            // inside the contributor's own call, where the stack trace points at the culprit.
            ArrayBufferWriter<byte> buffer = new();
            using (Utf8JsonWriter writer = new(buffer))
            {
                write(writer);
                writer.Flush();

                if (writer.BytesCommitted == 0 || writer.CurrentDepth != 0)
                {
                    throw new InvalidOperationException(
                        $"The {chunk} chunk's '{name}' section must be written as exactly one complete JSON value.");
                }
            }

            _sections.Add(new ClientMetadataSection(chunk, name, buffer.WrittenMemory.ToArray()));
        }

        /// <summary>
        ///     Writes one generated source file into the client project's generated folder.
        /// </summary>
        /// <param name="relativePath">The file's path relative to the generated folder, with forward
        ///     slashes, such as <c>tellma-entities.d.ts</c>.</param>
        /// <param name="content">The file's full text.</param>
        /// <exception cref="ArgumentException"><paramref name="relativePath" /> is not a plain relative path.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="content" /> is null.</exception>
        /// <exception cref="InvalidOperationException">A file was already written at the path.</exception>
        public void File(string relativePath, string content)
        {
            if (!IsPlainRelativePath(relativePath))
            {
                throw new ArgumentException(
                    $"A generated file path is a relative path of non-empty segments separated by '/', without '.' or '..' segments or characters outside the portable set; received '{relativePath}'.",
                    nameof(relativePath));
            }

            ArgumentNullException.ThrowIfNull(content);

            // Compared without case: two paths that differ only in case collide on Windows and macOS.
            if (_files.Any(file => string.Equals(file.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException($"The generated file '{relativePath}' was already written.");
            }

            _files.Add(new ClientGeneratedFile(relativePath, content));
        }

        /// <summary>Whether a section name starts with a lowercase letter and holds only ASCII letters and digits.</summary>
        /// <param name="name">The candidate name.</param>
        /// <returns>True for a camelCase identifier.</returns>
        private static bool IsCamelCaseIdentifier(string? name)
        {
            if (string.IsNullOrEmpty(name) || name[0] is not (>= 'a' and <= 'z'))
            {
                return false;
            }

            foreach (char c in name)
            {
                if (c is not ((>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9')))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Whether a path is relative, separated by <c>/</c>, and free of empty, <c>.</c> and <c>..</c> segments and of non-portable characters.</summary>
        /// <param name="path">The candidate path.</param>
        /// <returns>True for a plain relative path.</returns>
        private static bool IsPlainRelativePath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path) || path.AsSpan().ContainsAny(ForbiddenPathCharacters))
            {
                return false;
            }

            foreach (string segment in path.Split('/'))
            {
                // An empty segment covers a leading '/' (rooted), a trailing '/' and '//'.
                if (segment.Length == 0 || segment is "." or ".." || segment.Any(char.IsControl))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
