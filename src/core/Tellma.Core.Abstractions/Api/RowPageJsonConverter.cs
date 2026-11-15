// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text.Json;
using System.Text.Json.Serialization;
using Tellma.Core.Abstractions.Data;

namespace Tellma.Core.Abstractions.Api
{
    /// <summary>
    ///     Writes a <see cref="RowPage" /> as the rows envelope:
    ///     <c>{ "rows": …, "count": n, "countCapped": b, "ancestors": … }</c>. Write-only: a page is
    ///     never read from JSON.
    /// </summary>
    /// <remarks>
    ///     <c>count</c> is the page's count held to its cap, and <c>countCapped</c> whether the count
    ///     exceeded the cap; both are absent when no count was asked for, and <c>ancestors</c> when
    ///     no ancestors were. A page without a cap reports its count as is, never capped.
    /// </remarks>
    internal sealed class RowPageJsonConverter : JsonConverter<RowPage>
    {
        /// <summary>Writes the page's row sets.</summary>
        private readonly QueryRowSetJsonConverter _rows = new();

        /// <inheritdoc />
        public override RowPage Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            throw new NotSupportedException("A row page is written to JSON only; it is never read from it.");
        }

        /// <inheritdoc />
        public override void Write(Utf8JsonWriter writer, RowPage value, JsonSerializerOptions options)
        {
            ArgumentNullException.ThrowIfNull(writer);
            ArgumentNullException.ThrowIfNull(value);

            writer.WriteStartObject();
            writer.WritePropertyName("rows");
            _rows.Write(writer, value.Rows, options);

            // --- The count: the cap plus one stands for "more than the cap" ---
            if (value.Count is int count)
            {
                int cap = value.CountCap ?? int.MaxValue;
                writer.WriteNumber("count", Math.Min(count, cap));
                writer.WriteBoolean("countCapped", count > cap);
            }

            if (value.Ancestors is QueryRowSet ancestors)
            {
                writer.WritePropertyName("ancestors");
                _rows.Write(writer, ancestors, options);
            }

            writer.WriteEndObject();
        }
    }
}
