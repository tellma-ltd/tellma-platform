// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Identity.Services.ClientMetadata
{
    /// <summary>
    ///     A document client cannot be resolved: a guard or the validation refused its document,
    ///     or the fetch failed. The request it serves is answered <c>invalid_client</c>.
    /// </summary>
    public sealed class ClientMetadataException : Exception
    {
        /// <summary>Creates the exception.</summary>
        /// <param name="reason">One of <see cref="ClientMetadataRejectionReasons" />.</param>
        /// <param name="message">What went wrong, for the log; never shown to the client.</param>
        /// <param name="innerException">The underlying failure, when any.</param>
        public ClientMetadataException(string reason, string message, Exception? innerException = null)
            : base(message, innerException)
        {
            ArgumentException.ThrowIfNullOrEmpty(reason);
            Reason = reason;
        }

        /// <summary>Creates the exception with a generic message.</summary>
        public ClientMetadataException()
            : this(ClientMetadataRejectionReasons.Document, "The client metadata document could not be resolved.")
        {
        }

        /// <summary>Creates the exception with a message and the document reason.</summary>
        /// <param name="message">What went wrong.</param>
        public ClientMetadataException(string message)
            : this(ClientMetadataRejectionReasons.Document, message)
        {
        }

        /// <summary>Creates the exception with a message, the document reason and an inner exception.</summary>
        /// <param name="message">What went wrong.</param>
        /// <param name="innerException">The underlying failure.</param>
        public ClientMetadataException(string message, Exception innerException)
            : this(ClientMetadataRejectionReasons.Document, message, innerException)
        {
        }

        /// <summary>Why the document client was refused, one of <see cref="ClientMetadataRejectionReasons" />.</summary>
        public string Reason { get; }

        /// <summary>
        ///     Whether the remote could not be read — a timeout, an unreachable host, an error
        ///     status — rather than a guard or the validation refusing what it served. The fetch
        ///     counter records the two as <c>failed</c> and <c>rejected</c>.
        /// </summary>
        public bool IsFetchFailure => Reason is ClientMetadataRejectionReasons.Timeout
            or ClientMetadataRejectionReasons.Unreachable
            or ClientMetadataRejectionReasons.Status;
    }
}
