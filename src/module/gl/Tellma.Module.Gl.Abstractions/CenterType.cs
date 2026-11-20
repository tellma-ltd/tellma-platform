// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Module.Gl
{
    /// <summary>
    ///     What a responsibility center stands for. Only <see cref="Abstract" /> and
    ///     <see cref="BusinessUnit" /> centers group others: a center of any other type has no children.
    /// </summary>
    /// <remarks>Stored by member name, so a member is never renamed once shipped.</remarks>
    public enum CenterType
    {
        /// <summary>A grouping center that never receives postings.</summary>
        Abstract,

        /// <summary>A profit or investment center that groups its operating centers.</summary>
        BusinessUnit,

        /// <summary>A service cost center, such as IT, human resources or maintenance; has no children.</summary>
        Service,

        /// <summary>A production or operating cost center; has no children.</summary>
        Operation,

        /// <summary>A revenue center; has no children.</summary>
        Sale,
    }
}
