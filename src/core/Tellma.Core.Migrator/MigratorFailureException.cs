// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Migrator
{
    /// <summary>
    ///     A refusal the migrator states in its own words: a database in another collation, a
    ///     missing tenant database, a lock that could not be taken. It fails the database it concerns,
    ///     and its message is printed as the reason.
    /// </summary>
    internal sealed class MigratorFailureException : Exception
    {
        /// <summary>Creates the exception.</summary>
        public MigratorFailureException()
        {
        }

        /// <summary>Creates the exception.</summary>
        /// <param name="message">The reason, as one sentence naming the database and what was found.</param>
        public MigratorFailureException(string message)
            : base(message)
        {
        }

        /// <summary>Creates the exception.</summary>
        /// <param name="message">The reason.</param>
        /// <param name="innerException">The failure behind it.</param>
        public MigratorFailureException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
