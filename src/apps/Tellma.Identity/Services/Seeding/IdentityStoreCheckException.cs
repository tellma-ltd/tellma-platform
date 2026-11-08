// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Identity.Services.Seeding
{
    /// <summary>
    ///     The identity store failed the check the engine runs at every start: the database is in
    ///     another collation, or it is missing, empty, or behind the engine's last migration. Thrown
    ///     before seeding writes anything, so the host stops with the message as its cause.
    /// </summary>
    public sealed class IdentityStoreCheckException : InvalidOperationException
    {
        /// <summary>Creates the exception with no message.</summary>
        public IdentityStoreCheckException()
        {
        }

        /// <summary>Creates the exception with a message naming the store, what was expected and what was found.</summary>
        /// <param name="message">The message.</param>
        public IdentityStoreCheckException(string message)
            : base(message)
        {
        }

        /// <summary>Creates the exception with a message and the failure that caused it.</summary>
        /// <param name="message">The message.</param>
        /// <param name="innerException">The underlying failure, such as an unreachable server.</param>
        public IdentityStoreCheckException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
