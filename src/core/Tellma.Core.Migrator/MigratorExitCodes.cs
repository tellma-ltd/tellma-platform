// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Migrator
{
    /// <summary>The exit codes of <see cref="TellmaMigrator.RunAsync(IReadOnlyList{string}, string, Action{Composition.TellmaBuilder}, Func{string, Task})" />.</summary>
    public static class MigratorExitCodes
    {
        /// <summary>The command converged: every database it touched is where it should be, or the files are written.</summary>
        public const int Converged = 0;

        /// <summary>The arguments or the configuration do not form a command the migrator can run; nothing was touched.</summary>
        public const int Usage = 1;

        /// <summary>The command ran and some database failed; the report names each, and a re-run retries them.</summary>
        public const int PartialFailure = 2;

        /// <summary>The composition has problems; they are printed, and no database was touched.</summary>
        public const int CompositionProblems = 3;
    }
}
