// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using Tellma.Core.Abstractions.Internal;

namespace Tellma.Core.Abstractions.Errors
{
    /// <summary>
    ///     The operation's input broke one or more rules: every error validation found, at once.
    ///     Nothing was written. Maps to 422 with the problem code <c>validation</c> and one
    ///     <c>errors[]</c> item per error.
    /// </summary>
    /// <param name="errors">Every error found, in the order found; at least one, none null.</param>
    /// <exception cref="ArgumentNullException"><paramref name="errors" /> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="errors" /> is empty or holds a null entry.</exception>
    public sealed class ValidationException(IReadOnlyList<ValidationError> errors)
        : TellmaException(ProblemCode, Describe(errors))
    {
        /// <summary>The problem code of this outcome.</summary>
        private const string ProblemCode = "validation";

        // Member initializers run before the base constructor, so the errors are validated here,
        // before the log message is rendered from them.
        /// <summary>Every error found, in the order found.</summary>
        public IReadOnlyList<ValidationError> Errors { get; } = ReadOnlyCopies.OfEntries(errors, requireOne: true, nameof(errors));

        /// <summary>Renders the log message.</summary>
        /// <param name="errors">The errors, already validated.</param>
        /// <returns>The message.</returns>
        private static string Describe(IReadOnlyList<ValidationError> errors)
        {
            // The root path renders as the empty string, which would read as a missing location.
            string list = LogText.List(errors, static error =>
                error.Path.Segments.Count == 0 ? $"{error.Code} on the whole payload" : $"{error.Code} at {error.Path}");

            return string.Create(CultureInfo.InvariantCulture, $"Validation failed with {errors.Count} error(s): {list}.");
        }
    }
}
