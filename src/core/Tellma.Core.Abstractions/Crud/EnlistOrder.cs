// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Crud
{
    /// <summary>
    ///     Where an enlisted group's statements sit in the host's persist batch, relative to the host's
    ///     own group. Groups on the same side keep their enlistment order, and a nested enlistment is
    ///     positioned relative to its immediate host.
    /// </summary>
    public enum EnlistOrder
    {
        /// <summary>After the host's statements and effects: the default for a save, whose rows may reference the host's.</summary>
        AfterHost = 0,

        /// <summary>Ahead of the host's statements: the default for a delete, which clears rows before the host writes.</summary>
        BeforeHost = 1,
    }
}
