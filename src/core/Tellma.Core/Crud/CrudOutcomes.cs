// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Blobs;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Excel;
using Tellma.Core.Abstractions.Tenancy;

namespace Tellma.Core.Crud
{
    // The closed set is spec 0015 §16.1's crud.outcome column; the classification follows the
    // exceptions' statuses in spec 0016 §7.1.
    /// <summary>
    ///     The <c>crud.outcome</c> values of an operation, and the classification of the exception an
    ///     operation ended with into one of them.
    /// </summary>
    internal static class CrudOutcomes
    {
        /// <summary>The operation completed.</summary>
        public const string Ok = "ok";

        /// <summary>The request was refused as malformed, invalid or failing validation.</summary>
        public const string Validation = "validation";

        /// <summary>The request raised warnings the caller has not confirmed.</summary>
        public const string Confirmation = "confirmation";

        /// <summary>The caller may not do what the request asks.</summary>
        public const string Forbidden = "forbidden";

        /// <summary>The rows, or the tenant, the request names are not there for the caller.</summary>
        public const string NotFound = "not_found";

        /// <summary>The request conflicts with the stored state.</summary>
        public const string Conflict = "conflict";

        /// <summary>The request exceeds a ceiling.</summary>
        public const string Limit = "limit";

        /// <summary>The context the request ran under moved again after the one recompose.</summary>
        public const string Stale = "stale";

        /// <summary>The tenant or a dependency is unavailable.</summary>
        public const string Unavailable = "unavailable";

        /// <summary>The caller abandoned the request.</summary>
        public const string Cancelled = "cancelled";

        /// <summary>Anything else: a fault, never a user outcome.</summary>
        public const string Error = "error";

        /// <summary>The outcome of an operation that ended with an exception.</summary>
        /// <param name="exception">The exception.</param>
        /// <param name="aborted">Whether the request's token, or the scope's data cancellation, was cancelled when it ended.</param>
        /// <returns>The outcome.</returns>
        public static string Classify(Exception exception, bool aborted)
        {
            ArgumentNullException.ThrowIfNull(exception);
            return exception switch
            {
                // --- An abort the caller asked for, whatever raised it; any other cancellation is a fault ---
                OperationCanceledException when aborted => Cancelled,

                // --- 400 and 422 ---
                BadRequestException or InvalidQueryException or ValidationException or ImportException or BlobRejectedException => Validation,

                // --- 409 ---
                ConfirmationRequiredException => Confirmation,
                ConcurrencyException or CountMismatchException or TenantStateException => Conflict,

                // --- 401 and 403 ---
                ForbiddenException or IndividualRequiredException or StepUpRequiredException => Forbidden,

                // --- 404 ---
                NotFoundException or TenantNotFoundException => NotFound,

                // --- 413 ---
                LimitExceededException => Limit,

                // --- 503, and the tenant's own refusals ---
                StaleContextException => Stale,
                DependencyUnavailableException or TenantUnavailableException => Unavailable,

                _ => Error,
            };
        }
    }
}
