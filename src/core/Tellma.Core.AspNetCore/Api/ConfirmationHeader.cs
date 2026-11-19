// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Primitives;
using System.Globalization;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Errors;

namespace Tellma.Core.AspNetCore.Api
{
    /// <summary>
    ///     Reads the <c>Tellma-Confirm</c> header: the confirmation keys a caller resends a request
    ///     with after it was asked to confirm warnings. A comma-separated list of at most
    ///     <see cref="MaxKeys" /> keys, each 1 to <see cref="MaxKeyLength" /> visible ASCII characters.
    /// </summary>
    internal static class ConfirmationHeader
    {
        /// <summary>The most keys one request confirms.</summary>
        public const int MaxKeys = 16;

        /// <summary>The longest key.</summary>
        public const int MaxKeyLength = 100;

        /// <summary>Reads the keys of every value the header carries, in order.</summary>
        /// <param name="values">The header's values.</param>
        /// <returns>The keys; empty entries between commas are skipped.</returns>
        /// <exception cref="BadRequestException">The header carries too many keys, or a key that is too long or not visible ASCII.</exception>
        public static IReadOnlyList<string> Parse(StringValues values)
        {
            List<string> keys = [];
            foreach (string? value in values)
            {
                foreach (string entry in (value ?? string.Empty).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                {
                    // --- Each key: short, visible ASCII; the header is logged only as the keys it confirmed ---
                    if (entry.Length > MaxKeyLength || !entry.All(static c => c is > ' ' and <= '~'))
                    {
                        throw new BadRequestException(
                            string.Create(CultureInfo.InvariantCulture, $"The {TellmaHeaders.Confirm} header carries a key that is not 1 to {MaxKeyLength} visible ASCII characters."),
                            new Dictionary<string, object?>(StringComparer.Ordinal) { ["header"] = TellmaHeaders.Confirm });
                    }

                    keys.Add(entry);
                }
            }

            // --- The count: a bound on what one request can confirm ---
            return keys.Count <= MaxKeys
                ? keys
                : throw new BadRequestException(
                    string.Create(CultureInfo.InvariantCulture, $"The {TellmaHeaders.Confirm} header carries {keys.Count} keys; at most {MaxKeys} are allowed."),
                    new Dictionary<string, object?>(StringComparer.Ordinal) { ["header"] = TellmaHeaders.Confirm });
        }
    }
}
