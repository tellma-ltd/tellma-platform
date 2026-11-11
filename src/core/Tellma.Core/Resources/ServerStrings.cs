// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Resources
{
    /// <summary>
    ///     The marker of Core's server-only resources, <c>Resources/ServerStrings.resx</c> (neutral
    ///     English) and its satellites: text only the server renders, such as an email's subject and
    ///     body or a document's footer. Unlike <see cref="Strings" />, these never reach the browser
    ///     application's string pack.
    /// </summary>
    /// <remarks>
    ///     The keys and the message conventions are those of <see cref="Strings" />. A string the
    ///     browser application might render belongs in <see cref="Strings" /> instead: sharing it
    ///     costs bytes, while keeping it here costs the application a fallback to text the server
    ///     rendered.
    /// </remarks>
    internal sealed class ServerStrings
    {
        /// <summary>Not instantiable: the type only marks the resources.</summary>
        private ServerStrings()
        {
        }
    }
}
