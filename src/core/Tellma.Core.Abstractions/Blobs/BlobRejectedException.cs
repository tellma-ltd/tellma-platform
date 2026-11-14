// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using Tellma.Core.Abstractions.Errors;

namespace Tellma.Core.Abstractions.Blobs
{
    /// <summary>
    ///     An upload was refused before anything was stored: an unknown kind, a missing length, a body
    ///     too large, a refused content type, the staging quota, or an image the processor refused.
    ///     Maps by its code to the status of the refusal, with the problem code <c>blob-rejected</c>
    ///     and one <c>errors[]</c> item at <c>body</c> carrying the code and the arguments.
    /// </summary>
    /// <remarks>
    ///     Unlike the other members of the closed set, whose code is the problem code, this one's code
    ///     is the refusal's dotted resource key (<c>Blob.TooLarge</c>), from which the status is
    ///     derived.
    /// </remarks>
    /// <param name="code">The refusal's code, a dotted PascalCase resource key such as <c>Blob.TooLarge</c>.</param>
    /// <param name="arguments">The raw values its message renders from, such as <c>maxBytes</c>.</param>
    /// <exception cref="ArgumentException"><paramref name="code" /> is null, empty or white space.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="arguments" /> is null.</exception>
    public sealed class BlobRejectedException(string code, IReadOnlyDictionary<string, object?> arguments)
        : TellmaException(code, Describe(code), arguments ?? throw new ArgumentNullException(nameof(arguments)))
    {
        /// <summary>Renders the log message.</summary>
        /// <param name="code">The code.</param>
        /// <returns>The message.</returns>
        private static string Describe(string code)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(code);
            return string.Create(CultureInfo.InvariantCulture, $"The upload was rejected: {code}.");
        }
    }
}
