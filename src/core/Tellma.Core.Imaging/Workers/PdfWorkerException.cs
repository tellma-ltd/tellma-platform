// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Imaging.Workers
{
    /// <summary>How a PDF worker failed, as opposed to refusing a document.</summary>
    internal enum PdfWorkerFailure
    {
        /// <summary>The worker could not be started, or its limits could not be applied.</summary>
        Start,

        /// <summary>The worker exited, crashed or was stopped by a limit before it answered.</summary>
        Exited,

        /// <summary>The worker answered with something the protocol does not allow: a malformed frame or image.</summary>
        Protocol,

        /// <summary>The worker answered that it failed itself, out of memory under its cap for example.</summary>
        Failed,
    }

    /// <summary>
    ///     A PDF worker failed: it could not start, died, broke the protocol or reported its own
    ///     failure. The thumbnail is not stored; the upload is unaffected.
    /// </summary>
    /// <param name="failure">How it failed.</param>
    /// <param name="message">What failed, for the processor's log.</param>
    /// <param name="innerException">The exception that caused it, if any.</param>
    internal sealed class PdfWorkerException(PdfWorkerFailure failure, string message, Exception? innerException = null)
        : Exception(message, innerException)
    {
        /// <summary>How the worker failed.</summary>
        public PdfWorkerFailure Failure { get; } = failure;

        /// <summary>The worker's exit code, when it had exited.</summary>
        public int? ExitCode { get; init; }
    }
}
