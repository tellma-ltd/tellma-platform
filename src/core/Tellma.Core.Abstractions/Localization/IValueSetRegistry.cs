// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Localization
{
    /// <summary>
    ///     The value sets of the composition: the platform's four (<see cref="CoreValueSets" />) and
    ///     every set a feature contributed. Built once at composition and never changed.
    /// </summary>
    public interface IValueSetRegistry
    {
        /// <summary>Every set: the platform's four first, then the contributed ones in composition order.</summary>
        IReadOnlyList<ValueSetDescriptor> All { get; }

        /// <summary>The set of a name.</summary>
        /// <param name="name">The set's name, compared ordinally.</param>
        /// <returns>The set, or null when none has the name.</returns>
        ValueSetDescriptor? Find(string name);
    }
}
