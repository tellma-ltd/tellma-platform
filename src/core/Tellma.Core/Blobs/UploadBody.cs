// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Security.Cryptography;

namespace Tellma.Core.Blobs
{
    /// <summary>
    ///     One upload's bytes once read: in memory, or in a delete-on-close scratch file, with the head
    ///     kept for sniffing and the SHA-256 of the whole.
    /// </summary>
    /// <remarks>
    ///     The body hands out read streams over the same bytes, one at a time: each
    ///     <see cref="OpenRead" /> starts from the first byte, and <see cref="ReleaseAsync" /> gives the
    ///     stream back. Disposing the body deletes its file.
    /// </remarks>
    internal sealed class UploadBody : IAsyncDisposable
    {
        private readonly byte[]? _bytes;
        private readonly FileStream? _file;

        /// <summary>Creates a body over its bytes in memory or its file.</summary>
        /// <param name="bytes">The bytes, when in memory.</param>
        /// <param name="file">The file, when streamed.</param>
        /// <param name="length">The body's length.</param>
        /// <param name="head">The body's first bytes.</param>
        /// <param name="sha256">The body's SHA-256, when already computed.</param>
        private UploadBody(byte[]? bytes, FileStream? file, long length, byte[] head, byte[]? sha256)
        {
            _bytes = bytes;
            _file = file;
            Length = length;
            Head = head;
            if (sha256 is not null)
            {
                Sha256 = sha256;
            }
        }

        /// <summary>The body's length in bytes.</summary>
        public long Length { get; }

        /// <summary>The body's first bytes, up to <see cref="BlobContentSniffer.SniffLength" />.</summary>
        public byte[] Head { get; }

        /// <summary>The body's bytes; only a body read into memory has them.</summary>
        /// <exception cref="InvalidOperationException">The body is in a file.</exception>
        public byte[] Bytes => _bytes ?? throw new InvalidOperationException("The upload was streamed to a file, not read into memory.");

        /// <summary>The SHA-256 of the whole body.</summary>
        public byte[] Sha256 { get => field ??= SHA256.HashData(Bytes); private init; }

        /// <summary>Creates a body over bytes read into memory.</summary>
        /// <param name="bytes">The bytes.</param>
        /// <returns>The body.</returns>
        public static UploadBody InMemory(byte[] bytes)
        {
            ArgumentNullException.ThrowIfNull(bytes);
            return new UploadBody(bytes, null, bytes.LongLength, bytes[..Math.Min(bytes.Length, BlobContentSniffer.SniffLength)], null);
        }

        /// <summary>Creates a body over a scratch file it now owns.</summary>
        /// <param name="file">The file, holding the whole body.</param>
        /// <param name="length">The body's length.</param>
        /// <param name="head">The body's first bytes.</param>
        /// <param name="sha256">The body's SHA-256, computed while it was written.</param>
        /// <returns>The body.</returns>
        public static UploadBody InFile(FileStream file, long length, byte[] head, byte[] sha256)
        {
            ArgumentNullException.ThrowIfNull(file);
            ArgumentNullException.ThrowIfNull(head);
            ArgumentNullException.ThrowIfNull(sha256);
            return new UploadBody(null, file, length, head, sha256);
        }

        /// <summary>Opens a read stream positioned at the body's first byte.</summary>
        /// <returns>The stream, to give back through <see cref="ReleaseAsync" />.</returns>
        public Stream OpenRead()
        {
            if (_file is not null)
            {
                // The file is the body's own; a reader rewinds it and never closes it.
                _file.Position = 0;
                return _file;
            }

            return new MemoryStream(_bytes!, writable: false);
        }

        /// <summary>Gives back a stream from <see cref="OpenRead" />, or any stream the caller made.</summary>
        /// <param name="stream">The stream.</param>
        /// <returns>A task that completes when the stream is released.</returns>
        public ValueTask ReleaseAsync(Stream stream)
        {
            ArgumentNullException.ThrowIfNull(stream);

            // The file stays open until the body is disposed; any other stream is the caller's to close.
            return ReferenceEquals(stream, _file) ? ValueTask.CompletedTask : stream.DisposeAsync();
        }

        /// <inheritdoc />
        public ValueTask DisposeAsync()
        {
            // Closing the file deletes it: it was opened delete-on-close.
            return _file?.DisposeAsync() ?? ValueTask.CompletedTask;
        }
    }
}
