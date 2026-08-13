// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Connector.SendGrid
{
    /// <summary>
    ///     A SendGrid request the caller cannot recover from — chiefly an authentication failure,
    ///     which an adapter surfaces as a thrown batch when nothing went out.
    /// </summary>
    public sealed class SendGridRequestException : Exception
    {
        /// <summary>Creates an exception carrying the failed request's result.</summary>
        /// <param name="result">The result SendGrid returned.</param>
        public SendGridRequestException(SendGridSendResult result)
            : base(Describe(result))
        {
            Result = result;
        }

        /// <summary>Creates an exception with a message.</summary>
        /// <param name="message">The message.</param>
        public SendGridRequestException(string message)
            : base(message)
        {
        }

        /// <summary>Creates an exception with a message and a cause.</summary>
        /// <param name="message">The message.</param>
        /// <param name="innerException">The cause.</param>
        public SendGridRequestException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        /// <summary>Creates an exception with no detail.</summary>
        public SendGridRequestException()
        {
        }

        /// <summary>The result SendGrid returned, when the failure came from a response.</summary>
        public SendGridSendResult? Result { get; }

        private static string Describe(SendGridSendResult result)
        {
            ArgumentNullException.ThrowIfNull(result);
            return $"SendGrid rejected the request with status {result.StatusCode}. {result.DescribeErrors()}";
        }
    }
}
