// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Buffers;
using System.Text;
using System.Text.Json;
using Tellma.Core.Abstractions.Api;

namespace Tellma.Core.Mcp.Rendering
{
    /// <summary>
    ///     Writes a value as the wire's JSON only when it fits a number of characters, and stops writing
    ///     as soon as it cannot: the work a value costs is bounded by the room it is offered, whatever
    ///     the value holds.
    /// </summary>
    internal static class BoundedJson
    {
        /// <summary>The most UTF-8 bytes one UTF-16 character takes.</summary>
        private const int MaxBytesPerChar = 3;

        /// <summary>Writes a value when its JSON fits.</summary>
        /// <param name="value">The value.</param>
        /// <param name="type">The type it is written as.</param>
        /// <param name="maxChars">The characters it may take.</param>
        /// <returns>The JSON, or null when it takes more.</returns>
        public static string? TryWrite(object? value, Type type, int maxChars)
        {
            ArgumentNullException.ThrowIfNull(type);
            if (maxChars <= 0)
            {
                return null;
            }

            // --- The value written into a buffer that refuses to grow past the room's bytes ---
            CappedBuffer buffer = new((long)maxChars * MaxBytesPerChar);
            JsonSerializerOptions options = TellmaJson.Options;
            try
            {
                using Utf8JsonWriter writer = new(buffer, new JsonWriterOptions { Encoder = options.Encoder, Indented = options.WriteIndented });
                JsonSerializer.Serialize(writer, value, options.GetTypeInfo(type));
                writer.Flush();
            }
            catch (Exception) when (buffer.Exceeded)
            {
                // The buffer ended the write: whatever the serializer made of that, the value does not fit.
                return null;
            }

            // --- The characters, which a non-ASCII value takes fewer of than bytes ---
            string json = Encoding.UTF8.GetString(buffer.Written);
            return json.Length <= maxChars ? json : null;
        }

        /// <summary>A buffer that grows in bounded steps and throws once the bytes committed pass its cap, which ends the write.</summary>
        /// <param name="cap">The bytes it accepts.</param>
        private sealed class CappedBuffer(long cap) : IBufferWriter<byte>
        {
            /// <summary>The most a step grows the buffer past the cap, so a writer never holds far more than it may commit.</summary>
            private const int Step = 4096;

            /// <summary>The bytes.</summary>
            private byte[] _bytes = [];

            /// <summary>The bytes committed.</summary>
            private int _written;

            /// <summary>The bytes committed.</summary>
            public ReadOnlySpan<byte> Written => _bytes.AsSpan(0, _written);

            /// <summary>Whether the cap was passed: later commits, such as the writer's flush on dispose, are ignored.</summary>
            public bool Exceeded { get; private set; }

            /// <inheritdoc />
            public void Advance(int count)
            {
                if (Exceeded)
                {
                    return;
                }

                _written += count;
                if (_written > cap)
                {
                    Exceeded = true;
                    throw new InvalidOperationException("The JSON passed the room it was offered.");
                }
            }

            /// <inheritdoc />
            public Memory<byte> GetMemory(int sizeHint = 0)
            {
                Ensure(sizeHint);
                return _bytes.AsMemory(_written);
            }

            /// <inheritdoc />
            public Span<byte> GetSpan(int sizeHint = 0)
            {
                Ensure(sizeHint);
                return _bytes.AsSpan(_written);
            }

            /// <summary>Grows the buffer for a write: doubling, but never far past the cap.</summary>
            /// <param name="sizeHint">The bytes the writer asks for.</param>
            private void Ensure(int sizeHint)
            {
                int needed = _written + Math.Max(sizeHint, 1);
                if (needed <= _bytes.Length)
                {
                    return;
                }

                long ceiling = Math.Min(int.MaxValue, cap + Step);
                int grown = (int)Math.Max(needed, Math.Min(Math.Max(_bytes.Length * 2L, Step), ceiling));
                Array.Resize(ref _bytes, grown);
            }
        }
    }
}
