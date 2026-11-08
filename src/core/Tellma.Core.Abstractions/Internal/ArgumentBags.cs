// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Internal
{
    /// <summary>
    ///     Content comparison of the named-argument bags coded errors and warnings carry, so two
    ///     values built separately from equal parts compare equal.
    /// </summary>
    internal static class ArgumentBags
    {
        /// <summary>
        ///     Whether two bags hold the same names, each with an equal value by
        ///     <see cref="object.Equals(object, object)" />.
        /// </summary>
        /// <param name="left">One bag.</param>
        /// <param name="right">The other.</param>
        /// <returns>True when they match name by name.</returns>
        public static bool Equal(IReadOnlyDictionary<string, object?> left, IReadOnlyDictionary<string, object?> right)
        {
            if (ReferenceEquals(left, right))
            {
                return true;
            }

            if (left.Count != right.Count)
            {
                return false;
            }

            foreach ((string name, object? value) in left)
            {
                if (!right.TryGetValue(name, out object? otherValue) || !Equals(value, otherValue))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
