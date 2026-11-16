// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Validation;

namespace Tellma.Core.Crud.Validation
{
    // Spec 0015 §7.5's ordering rule, applied where a validation outcome is known (§6.6, §10.1, §11.2).
    /// <summary>
    ///     The outcome of a validation stage, raised in the order a caller can act on: a stale stamp
    ///     first, then the errors, then every unconfirmed warning together. A user is never asked to
    ///     confirm an operation that cannot succeed.
    /// </summary>
    internal static class ValidationOutcome
    {
        /// <summary>Raises the stage's outcome, or returns when the operation may proceed.</summary>
        /// <param name="staleStamp">The early concurrency conflict the stage found, or null.</param>
        /// <param name="errors">The stage's errors.</param>
        /// <param name="confirmations">The frame's view, holding its unconfirmed warnings.</param>
        /// <exception cref="ConcurrencyException">A row's stamp was stale.</exception>
        /// <exception cref="ValidationException">An error was recorded.</exception>
        /// <exception cref="ConfirmationRequiredException">A warning was raised and not confirmed.</exception>
        public static void Raise(ConcurrencyException? staleStamp, ValidationErrors errors, ConfirmationFrame confirmations)
        {
            ArgumentNullException.ThrowIfNull(errors);
            ArgumentNullException.ThrowIfNull(confirmations);
            if (staleStamp is not null)
            {
                throw staleStamp;
            }

            if (errors.HasErrors)
            {
                throw new ValidationException(errors.Items);
            }

            IReadOnlyList<ConfirmationRequest> warnings = confirmations.Unconfirmed;
            if (warnings.Count > 0)
            {
                throw new ConfirmationRequiredException(warnings);
            }
        }
    }
}
