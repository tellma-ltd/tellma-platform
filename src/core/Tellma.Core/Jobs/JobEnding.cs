// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text.Json;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Jobs;

namespace Tellma.Core.Jobs
{
    /// <summary>How a handler's invocation ended, as the outcome rules read it for the items it left unmarked.</summary>
    internal enum JobEndingKind
    {
        /// <summary>The handler returned.</summary>
        Returned,

        /// <summary>The handler failed its unmarked items with an error of its own.</summary>
        Failed,

        /// <summary>A validation refused the work: the items fail with its first error.</summary>
        Invalid,

        /// <summary>The work raised warnings the job carries no consent for.</summary>
        ConfirmationRequired,

        /// <summary>The handler stopped because the worker cancelled it.</summary>
        Cancelled,

        /// <summary>The handler threw anything else.</summary>
        Threw,
    }

    /// <summary>
    ///     How a handler's invocation ended, or how a completion that carried its work failed: the
    ///     kind, the error it records and the warnings it waits on.
    /// </summary>
    /// <param name="Kind">How it ended.</param>
    /// <param name="Error">The error a failure records: the code and its arguments, before a trace id is attached; null for the other kinds.</param>
    /// <param name="Confirmations">The warnings of a confirmation the work required; null for the other kinds.</param>
    /// <param name="Exception">What was thrown; null when the handler returned.</param>
    internal sealed record JobEnding(
        JobEndingKind Kind,
        JobOutcomeError? Error,
        IReadOnlyList<ConfirmationRequest>? Confirmations,
        Exception? Exception)
    {
        /// <summary>The ending of a handler that returned.</summary>
        public static JobEnding Returned { get; } = new(JobEndingKind.Returned, null, null, null);

        // Spec 0020 §3.4's table, read with two refinements. An OperationCanceledException is the
        // worker's stop only when one of the tokens it handed out was cancelled: a library's own
        // timeout surfaces as one too, and mapping it to a release would requeue the job at once
        // with its attempt uncounted, for ever. And once the host is stopping, anything the handler
        // throws is the stop's echo (a lost connection, an ambiguous commit), so it ends the same way.
        /// <summary>Classifies what an invocation threw.</summary>
        /// <param name="exception">What the handler threw; null when it returned.</param>
        /// <param name="cancelled">Whether a token the worker handed the invocation was cancelled: an item's, the batch's or the scope's.</param>
        /// <param name="hostStopping">Whether the host was stopping when the handler ended.</param>
        /// <returns>The ending.</returns>
        public static JobEnding Of(Exception? exception, bool cancelled, bool hostStopping)
        {
            return exception switch
            {
                null => Returned,
                JobFailedException failed => new(JobEndingKind.Failed, new JobOutcomeError(failed.Error.Code, failed.ArgumentsJson, null), null, failed),
                ValidationException invalid => Invalid(invalid),
                ConfirmationRequiredException required => new(JobEndingKind.ConfirmationRequired, null, required.Requests, required),
                OperationCanceledException when cancelled || hostStopping => new(JobEndingKind.Cancelled, null, null, exception),
                _ when hostStopping => new(JobEndingKind.Cancelled, null, null, exception),
                _ => new(JobEndingKind.Threw, null, null, exception),
            };
        }

        /// <summary>
        ///     The ending a validation failure stands for: the items fail with its first error. An error
        ///     whose code exceeds the job table's column, or whose arguments do not fit it, keeps what
        ///     fits: the platform's internal code, or the code without its arguments.
        /// </summary>
        /// <param name="invalid">The validation failure.</param>
        /// <returns>The ending.</returns>
        public static JobEnding Invalid(ValidationException invalid)
        {
            ArgumentNullException.ThrowIfNull(invalid);
            ValidationError? first = invalid.Errors.Count > 0 ? invalid.Errors[0] : null;
            JobOutcomeError error = first is null || first.Code.Length > JobLimits.MaxKeyLength
                ? new JobOutcomeError(JobErrorCodes.Internal, null, null)
                : new JobOutcomeError(first.Code, ArgumentsOrNull(first), null);
            return new(JobEndingKind.Invalid, error, null, invalid);
        }

        /// <summary>The ending of a failure that is neither a validation nor a cancellation: the items retry with the internal error.</summary>
        /// <param name="exception">The failure.</param>
        /// <returns>The ending.</returns>
        public static JobEnding Unexpected(Exception exception)
        {
            ArgumentNullException.ThrowIfNull(exception);
            return new(JobEndingKind.Threw, null, null, exception);
        }

        /// <summary>A validation error's arguments as the job table stores them; null when there are none or they do not fit.</summary>
        /// <param name="error">The error.</param>
        /// <returns>The arguments as JSON, or null.</returns>
        private static string? ArgumentsOrNull(ValidationError error)
        {
            // A validation error was raised for a request, whose arguments are not held to the job
            // table's 4,000 characters; the code alone still tells the reader what failed.
            try
            {
                return JobJson.SerializeArguments(error.Code, error.Arguments, nameof(error));
            }
            catch (ArgumentException)
            {
                return null;
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }
}
