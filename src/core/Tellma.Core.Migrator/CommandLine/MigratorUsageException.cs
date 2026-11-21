// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Migrator.CommandLine
{
    /// <summary>
    ///     The migrator was called with arguments or configuration that do not form a command it can
    ///     run. It exits with the usage code and prints the message, then the usage text.
    /// </summary>
    internal sealed class MigratorUsageException : Exception
    {
        /// <summary>Creates the exception.</summary>
        public MigratorUsageException()
        {
        }

        /// <summary>Creates the exception.</summary>
        /// <param name="message">What is wrong with the call.</param>
        public MigratorUsageException(string message)
            : base(message)
        {
        }

        /// <summary>Creates the exception.</summary>
        /// <param name="message">What is wrong with the call.</param>
        /// <param name="innerException">The failure behind it.</param>
        public MigratorUsageException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
