// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.EntityFrameworkCore.LockEscalation
{
    /// <summary>The model annotation that carries a table's lock escalation option.</summary>
    public static class LockEscalationAnnotationNames
    {
        /// <summary>
        ///     The annotation on an entity type, and on the relational table and the table operations
        ///     the migrations differ derives from it, whose value is the option's SQL keyword:
        ///     <c>TABLE</c>, <c>AUTO</c> or <c>DISABLE</c>.
        /// </summary>
        public const string LockEscalation = "Tellma:LockEscalation";
    }
}
