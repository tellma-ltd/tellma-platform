// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Access
{
    // Spec 0018 §6.1 binds the development administrator to the in-proc identity server's seeded
    // subject without an identity call. Development sample data binds its other principals the same
    // way, because provisioning runs in the migrator, where no identity server is reachable.
    /// <summary>
    ///     Binds the users a save creates to identity subjects known in advance, in a development
    ///     environment only: each created user whose email address is held is recorded as an invitation
    ///     records it (its subject, the invitation's instant and an invited status), so its first
    ///     sign-in joins it with no invitation sent. One per scope.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The subjects must be those the development identity server seeds for the same email
    ///         addresses; a subject nobody can sign in as only leaves the user invited.
    ///     </para>
    ///     <para>
    ///         A user that exists already keeps its evidence: only a row the save inserts is bound. The
    ///         binding writes no tenant-membership hint; the caller records the hints of the users it
    ///         bound.
    ///     </para>
    /// </remarks>
    public interface IDevelopmentSubjects
    {
        /// <summary>
        ///     Holds subjects for the saves the scope makes until the returned handle is disposed.
        /// </summary>
        /// <param name="subjects">
        ///     The subject of each email address; an address is compared trimmed and lower-cased, as
        ///     the users' stack stores it.
        /// </param>
        /// <returns>The hold; disposing it releases the subjects.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="subjects" /> is null.</exception>
        /// <exception cref="ArgumentException">
        ///     An email address or a subject is empty or white space, or two email addresses are the
        ///     same address.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        ///     The environment is not a development one, the scope is not the platform's own, or the
        ///     scope holds subjects already.
        /// </exception>
        IDisposable Hold(IReadOnlyDictionary<string, string> subjects);
    }
}
