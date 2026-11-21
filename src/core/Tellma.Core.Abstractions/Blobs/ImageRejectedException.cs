// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.ObjectModel;

namespace Tellma.Core.Abstractions.Blobs
{
    /// <summary>
    ///     An image processor refused an input. The blob service translates it into the upload's
    ///     image-rejected error, carrying <see cref="Reason" /> to the client.
    /// </summary>
    /// <remarks>
    ///     Every <see cref="IImageProcessor" /> throws it, and only it, for an input it refuses, with
    ///     a <see cref="ReasonArgument" /> among the arguments: <see cref="FormatReason" />,
    ///     <see cref="TooManyPixelsReason" />, <see cref="DecodeFailedReason" /> or
    ///     <see cref="TimeoutReason" />.
    /// </remarks>
    /// <param name="code">The processor's code for the refusal; not blank.</param>
    /// <param name="arguments">The refusal's arguments, including a non-blank string under <see cref="ReasonArgument" />.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="code" /> is blank, or the arguments carry no reason.</exception>
    public sealed class ImageRejectedException(string code, IReadOnlyDictionary<string, object?> arguments)
        : Exception($"The image was rejected ({code}): {arguments?.GetValueOrDefault(ReasonArgument)}.")
    {
        /// <summary>The argument that carries the reason.</summary>
        public const string ReasonArgument = "reason";

        /// <summary>The input is not one of the accepted image formats.</summary>
        public const string FormatReason = "format";

        /// <summary>The header declares more pixels than the policy's cap.</summary>
        public const string TooManyPixelsReason = "too-many-pixels";

        /// <summary>The image could not be decoded.</summary>
        public const string DecodeFailedReason = "decode-failed";

        /// <summary>Normalising the image took longer than its time budget.</summary>
        public const string TimeoutReason = "timeout";

        /// <summary>The processor's code for the refusal.</summary>
        public string Code { get; } = code;

        // Initialisers run in textual order and before the base constructor, so this one validates
        // every argument before anything reads them.

        /// <summary>The refusal's arguments, a copy taken at construction.</summary>
        public IReadOnlyDictionary<string, object?> Arguments { get; } = Validate(code, arguments);

        /// <summary>The reason argument: <see cref="FormatReason" />, <see cref="TooManyPixelsReason" />, <see cref="DecodeFailedReason" /> or <see cref="TimeoutReason" />.</summary>
        public string Reason { get; } = (string)arguments[ReasonArgument]!;

        /// <summary>Validates the code and the arguments, and copies the arguments.</summary>
        /// <param name="code">The code.</param>
        /// <param name="arguments">The arguments.</param>
        /// <returns>A read-only copy of the arguments.</returns>
        private static ReadOnlyDictionary<string, object?> Validate(string code, IReadOnlyDictionary<string, object?> arguments)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(code);
            ArgumentNullException.ThrowIfNull(arguments);

            return arguments.TryGetValue(ReasonArgument, out object? reason) && reason is string text && !string.IsNullOrWhiteSpace(text)
                ? new ReadOnlyDictionary<string, object?>(new Dictionary<string, object?>(arguments, StringComparer.Ordinal))
                : throw new ArgumentException($"An image rejection carries a non-blank string argument '{ReasonArgument}'.", nameof(arguments));
        }
    }
}
