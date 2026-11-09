// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Settings
{
    /// <summary>
    ///     Every setting key of the composition: the public static <see cref="SettingKey{T}" /> fields
    ///     of each type a feature registered, in feature order and, within a type, declaration order.
    ///     Built at composition and validated at the startup gate.
    /// </summary>
    public interface ISettingKeyRegistry
    {
        /// <summary>Every key, in registration order.</summary>
        IReadOnlyList<SettingKey> Keys { get; }

        /// <summary>
        ///     The settings tabs: <c>general</c> first, then each other category in the order its first
        ///     key appears in <see cref="Keys" />.
        /// </summary>
        IReadOnlyList<SettingCategoryDescriptor> Categories { get; }

        /// <summary>The key with a name, compared ordinally.</summary>
        /// <param name="name">The name.</param>
        /// <returns>The key, or null when no key has the name.</returns>
        SettingKey? Find(string name);

        /// <summary>One descriptor per key, in <see cref="Keys" /> order, as the client metadata carries them.</summary>
        /// <returns>The descriptors.</returns>
        IReadOnlyList<SettingKeyDescriptor> Describe();
    }
}
