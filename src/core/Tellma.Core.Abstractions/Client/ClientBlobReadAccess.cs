// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text.Json.Serialization;

namespace Tellma.Core.Abstractions.Client
{
    /// <summary>
    ///     Who may read a blob of a kind.
    ///     Each member travels as the name its <see cref="JsonStringEnumMemberNameAttribute" /> declares.
    /// </summary>
    public enum ClientBlobReadAccess
    {
        /// <summary>Whoever may read the row that references it.</summary>
        [JsonStringEnumMemberName("owner-read")]
        OwnerRead,

        /// <summary>Any member of the tenant.</summary>
        [JsonStringEnumMemberName("any-member")]
        AnyMember,
    }
}
