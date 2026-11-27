// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Entities;

namespace Tellma.Core.Abstractions.Access
{
    /// <summary>
    ///     The caller's own profile as the caller edits it: the eight members a member may change on
    ///     their own row, the stamp the edit was made against, and how a stamp that moved since is
    ///     treated. The locale members change through a request of their own. The name and its twins
    ///     form a multilingual group, as on the user, so the twins carry the name's label.
    /// </summary>
    /// <param name="Name">The name in the tenant's first content language.</param>
    /// <param name="Name2">The name in the second content language; null for none.</param>
    /// <param name="Name3">The name in the third content language; null for none.</param>
    /// <param name="ImageId">The profile picture's blob; null for none.</param>
    /// <param name="SignatureId">The signature's blob; null for none.</param>
    /// <param name="Gender">The gender; null when not stated.</param>
    /// <param name="ContactEmail">The address notifications go to instead of the sign-in email; null for the sign-in email.</param>
    /// <param name="ContactMobile">The mobile number, in international format; null for none.</param>
    /// <param name="ModifiedAt">The row's stamp as the caller last read it.</param>
    /// <param name="Concurrency">
    ///     <see cref="ConcurrencyMode.Check" /> to refuse the edit when the row changed since
    ///     <paramref name="ModifiedAt" />; <see cref="ConcurrencyMode.Override" /> once the caller chose
    ///     to overwrite that change.
    /// </param>
    public sealed record MeSaveRequest(
        [property: Multilingual] string Name,
        string? Name2,
        string? Name3,
        int? ImageId,
        int? SignatureId,
        Gender? Gender,
        string? ContactEmail,
        string? ContactMobile,
        DateTimeOffset ModifiedAt,
        ConcurrencyMode Concurrency = ConcurrencyMode.Check);
}
