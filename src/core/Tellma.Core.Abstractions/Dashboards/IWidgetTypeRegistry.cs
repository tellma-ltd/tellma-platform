// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Dashboards
{
    /// <summary>
    ///     The dashboard widget types of the composition: the keys the features registered, in the
    ///     order the features were realised. A widget whose type the registry does not list is
    ///     refused on save; the client renders only the types it lists.
    /// </summary>
    public interface IWidgetTypeRegistry
    {
        /// <summary>Every registered type, in realisation order: the platform's first.</summary>
        IReadOnlyList<WidgetTypeDescriptor> Types { get; }

        /// <summary>Finds the type registered under a key, compared ordinally.</summary>
        /// <param name="key">The type's key.</param>
        /// <returns>The type; null when no feature registered the key.</returns>
        WidgetTypeDescriptor? Find(string key);
    }
}
