// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Module.Gl
{
    /// <summary>What a <see cref="CenterType" /> allows.</summary>
    public static class CenterTypes
    {
        /// <summary>
        ///     Whether a center of a type may have children: true for <see cref="CenterType.Abstract" />
        ///     and <see cref="CenterType.BusinessUnit" />, false for every other type.
        /// </summary>
        /// <param name="type">The center's type.</param>
        /// <returns>Whether the type groups other centers.</returns>
        public static bool IsGrouping(CenterType type)
        {
            return type is CenterType.Abstract or CenterType.BusinessUnit;
        }
    }
}
