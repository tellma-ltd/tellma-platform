// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Realtime
{
    /// <summary>
    ///     The client events of the composition: every name a feature registered, each once, by the
    ///     feature whose code publishes it. A singleton, built once when the composition is realised.
    /// </summary>
    /// <remarks>
    ///     A name follows the grammar of a notification type key: two or more dot-separated segments of
    ///     lowercase letters, digits and hyphens, each starting with a letter, at most 64 characters.
    ///     The startup gate refuses a name that breaks it or is registered twice.
    /// </remarks>
    public interface IClientEventRegistry
    {
        /// <summary>Every registered name, in registration order.</summary>
        IReadOnlyList<string> Names { get; }

        /// <summary>Whether a name is registered, compared ordinally.</summary>
        /// <param name="name">The name.</param>
        /// <returns>True when a feature registered the name.</returns>
        bool IsRegistered(string name);
    }
}
