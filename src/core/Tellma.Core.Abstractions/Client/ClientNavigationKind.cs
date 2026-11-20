// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text.Json.Serialization;

namespace Tellma.Core.Abstractions.Client
{
    /// <summary>
    ///     How a navigation reaches its target.
    ///     Each member travels as the name its <see cref="JsonStringEnumMemberNameAttribute" /> declares.
    /// </summary>
    public enum ClientNavigationKind
    {
        /// <summary>Through a foreign key the entity holds.</summary>
        [JsonStringEnumMemberName("reference")]
        Reference,

        /// <summary>From a child to the entity that owns it, through the child's parent key.</summary>
        [JsonStringEnumMemberName("owner")]
        Owner,

        /// <summary>From an entity to its one-row sibling, which shares its key.</summary>
        [JsonStringEnumMemberName("sibling")]
        Sibling,
    }
}
