// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using System.Buffers;
using System.IO.Pipelines;

namespace Tellma.Core.Webhooks
{
    /// <summary>
    ///     Turns an ASP.NET Core request into the transport-neutral shape receivers see: headers and
    ///     query parameters in case-insensitive dictionaries, and the body buffered whole so a
    ///     receiver can verify a signature over the exact wire bytes.
    /// </summary>
    internal static class WebhookRequestReader
    {
        /// <summary>The largest buffer allocated before any byte of the body has arrived.</summary>
        private const long InitialBufferBytes = 8192;

        /// <summary>Buffers the request body, refusing anything over the cap.</summary>
        /// <param name="request">The inbound request.</param>
        /// <param name="maxBytes">The largest body to accept.</param>
        /// <param name="cancellationToken">Abandons the read.</param>
        /// <returns>The body bytes, or null when the request exceeded the cap.</returns>
        /// <remarks>
        ///     Every read is consumed with <c>AdvanceTo(buffer.End)</c>. The tempting alternative —
        ///     examining without consuming so the total length can be checked first — deadlocks: the
        ///     request-body pipe stalls at its pause threshold and returns the same buffer forever.
        ///     A declared oversize <c>Content-Length</c> is refused before a single byte is read, and
        ///     the initial buffer is sized from what has to be true rather than from what the caller
        ///     claims: this endpoint is anonymous, so honouring a large <c>Content-Length</c> up front
        ///     would let an unauthenticated request commit the whole cap with an empty body.
        /// </remarks>
        internal static async Task<ReadOnlyMemory<byte>?> TryReadBodyAsync(
            HttpRequest request, long maxBytes, CancellationToken cancellationToken)
        {
            if (request.ContentLength is long declared && declared > maxBytes)
            {
                return null;
            }

            // ArrayBufferWriter allocates its initial capacity eagerly, so the claimed Content-Length
            // is only ever used to make the buffer smaller, never larger than one modest block.
            int initialCapacity = (int)Math.Clamp(
                request.ContentLength ?? InitialBufferBytes, 1L, Math.Min(InitialBufferBytes, Math.Max(1L, maxBytes)));
            ArrayBufferWriter<byte> writer = new(initialCapacity);
            PipeReader reader = request.BodyReader;

            while (true)
            {
                ReadResult result = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                ReadOnlySequence<byte> buffer = result.Buffer;

                foreach (ReadOnlyMemory<byte> segment in buffer)
                {
                    if (writer.WrittenCount + (long)segment.Length > maxBytes)
                    {
                        reader.AdvanceTo(buffer.End);
                        return null;
                    }

                    writer.Write(segment.Span);
                }

                reader.AdvanceTo(buffer.End);

                // Cancellation is checked first: both flags can be set on the same read, and treating
                // that as a complete body would hand a truncated payload to signature verification.
                if (result.IsCanceled)
                {
                    throw new OperationCanceledException("The webhook request body read was cancelled.");
                }

                if (result.IsCompleted)
                {
                    break;
                }
            }

            return writer.WrittenMemory;
        }

        /// <summary>Projects a header or query collection into the contract's dictionary shape.</summary>
        /// <param name="values">The source collection.</param>
        /// <param name="count">The number of entries, used to size the dictionary.</param>
        /// <returns>A dictionary whose keys compare ordinally but case-insensitively.</returns>
        internal static IReadOnlyDictionary<string, IReadOnlyList<string>> ToDictionary(
            IEnumerable<KeyValuePair<string, StringValues>> values, int count)
        {
            // The contract promises receivers a case-insensitive comparer so they can index headers
            // without re-wrapping; this is the one place that promise is kept.
            Dictionary<string, IReadOnlyList<string>> result = new(count, StringComparer.OrdinalIgnoreCase);

            foreach (KeyValuePair<string, StringValues> entry in values)
            {
                string[] entryValues = new string[entry.Value.Count];
                for (int i = 0; i < entryValues.Length; i++)
                {
                    // StringValues can legally hold nulls; receivers should never have to null-check.
                    entryValues[i] = entry.Value[i] ?? string.Empty;
                }

                result[entry.Key] = entryValues;
            }

            return result;
        }
    }
}
