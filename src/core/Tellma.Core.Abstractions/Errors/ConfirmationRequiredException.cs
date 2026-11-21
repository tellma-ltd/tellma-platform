// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using Tellma.Core.Abstractions.Internal;

namespace Tellma.Core.Abstractions.Errors
{
    /// <summary>
    ///     Validation found no error but recorded warnings the caller has not confirmed: every one of
    ///     them, so two warnings of one operation are confirmed in one round. Nothing was written.
    ///     Maps to 409 with the problem code <c>confirmation-required</c> and the warnings in
    ///     <c>errorDetails.confirmations</c>; the caller resends with the confirmed keys.
    /// </summary>
    /// <remarks>
    ///     A stale stamp is reported before any error, and any error before the warnings, so a user is
    ///     never asked to confirm an operation that cannot succeed.
    /// </remarks>
    /// <param name="requests">One request per unconfirmed warning; at least one, none null.</param>
    /// <exception cref="ArgumentNullException"><paramref name="requests" /> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="requests" /> is empty or holds a null entry.</exception>
    public sealed class ConfirmationRequiredException(IReadOnlyList<ConfirmationRequest> requests)
        : TellmaException(ProblemCode, Describe(requests))
    {
        /// <summary>The problem code of this outcome.</summary>
        private const string ProblemCode = "confirmation-required";

        // Member initializers run before the base constructor, so the requests are validated here,
        // before the log message is rendered from them.
        /// <summary>One request per unconfirmed warning, in the order recorded.</summary>
        public IReadOnlyList<ConfirmationRequest> Requests { get; } = ReadOnlyCopies.OfEntries(requests, requireOne: true, nameof(requests));

        /// <summary>Renders the log message.</summary>
        /// <param name="requests">The requests, already validated.</param>
        /// <returns>The message.</returns>
        private static string Describe(IReadOnlyList<ConfirmationRequest> requests)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"Confirmation required for {requests.Count} warning(s): {LogText.List(requests, static request => request.Key)}.");
        }
    }
}
