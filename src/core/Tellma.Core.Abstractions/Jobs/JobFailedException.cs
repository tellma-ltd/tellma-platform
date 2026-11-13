// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Jobs
{
    /// <summary>
    ///     Thrown by a handler to fail, at once and with <see cref="Error" />, every item it has not
    ///     marked with an outcome of its own. Never retried, and never reaches a request: the worker
    ///     catches it.
    /// </summary>
    /// <remarks>
    ///     A handler that wants some items retried and others failed marks each item instead; this
    ///     exception settles only the unmarked ones. It derives from <see cref="Exception" /> rather
    ///     than the platform's client-facing exception base, because no client ever sees it.
    /// </remarks>
    /// <param name="error">The error every unmarked item fails with.</param>
    /// <exception cref="ArgumentNullException"><paramref name="error" /> is null.</exception>
    /// <exception cref="ArgumentException">
    ///     The error's code is longer than <see cref="JobLimits.MaxKeyLength" /> characters, or its
    ///     arguments take more than <see cref="JobLimits.MaxArgumentsLength" /> once serialised.
    /// </exception>
    public sealed class JobFailedException(JobError error) : Exception(Describe(error))
    {
        /// <summary>The error every unmarked item fails with.</summary>
        public JobError Error { get; } = error ?? throw new ArgumentNullException(nameof(error));

        /// <summary>The error's arguments as the job table stores them; null when it has none.</summary>
        /// <remarks>Serialised when the exception is created, which is the size guard on its error.</remarks>
        internal string? ArgumentsJson { get; } = JobJson.SerializeArguments(
            error ?? throw new ArgumentNullException(nameof(error)),
            nameof(error));

        /// <summary>Refuses a null error and renders the log message.</summary>
        /// <param name="error">The error.</param>
        /// <returns>The message, naming the error's code.</returns>
        private static string Describe(JobError error)
        {
            ArgumentNullException.ThrowIfNull(error);
            return $"The job handler failed its unmarked items with the error '{error.Code}'.";
        }
    }
}
