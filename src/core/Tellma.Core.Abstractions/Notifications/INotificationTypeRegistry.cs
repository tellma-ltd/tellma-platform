// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Notifications
{
    /// <summary>
    ///     The notification types of the composition: every <see cref="NotificationTypeDescriptor" /> a
    ///     feature registered, in registration order. A singleton, built once when the composition is
    ///     realised; the startup gate refuses a composition whose types break the registry's rules.
    /// </summary>
    /// <remarks>
    ///     Types are fixed per deployment and the same for every tenant, so they reach the client in
    ///     its metadata rather than through a route.
    /// </remarks>
    public interface INotificationTypeRegistry
    {
        /// <summary>Every registered type, in registration order: the features' realisation order, then each feature's own order.</summary>
        IReadOnlyList<NotificationTypeDescriptor> All { get; }

        /// <summary>
        ///     Every category, each once, in the order of the first type registered in it. The user's
        ///     notification settings group the types by these.
        /// </summary>
        IReadOnlyList<string> Categories { get; }

        /// <summary>The type registered under a key, compared ordinally.</summary>
        /// <param name="key">The type's key.</param>
        /// <returns>The type, or <see langword="null" /> when no type has the key.</returns>
        NotificationTypeDescriptor? Find(string key);
    }
}
