// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Data;

namespace Tellma.Core.Caching
{
    // The table is spec 0013 §2.6.
    /// <summary>
    ///     What a batch does when a cached input it declared moved under it, and the comparison the
    ///     executor makes after a read or validate batch.
    /// </summary>
    /// <remarks>
    ///     <list type="bullet">
    ///         <item><c>permissions</c>, and every input of a persist batch: <see cref="VersionTagMismatchPolicy.Rerun" />.</item>
    ///         <item>
    ///             <c>settings</c>, <c>entities</c> and <c>entity:*</c> lists, and a pack's tags:
    ///             <see cref="VersionTagMismatchPolicy.Refresh" /> on a read batch, where the result is
    ///             served and the fresh tag invalidates the cached input; <see cref="VersionTagMismatchPolicy.Rerun" />
    ///             on a validate batch, since a validator never decides under an input that changed.
    ///         </item>
    ///         <item>The caller's preferences tag: <see cref="VersionTagMismatchPolicy.Refresh" />, see <see cref="ForPreferences" />.</item>
    ///     </list>
    /// </remarks>
    internal static class VersionTagPolicies
    {
        /// <summary>The policy of a tenant-level input on a batch of a purpose.</summary>
        /// <param name="name">The input's tag name.</param>
        /// <param name="purpose">The batch's purpose.</param>
        /// <returns>The policy.</returns>
        /// <exception cref="ArgumentOutOfRangeException">The purpose is a maintenance batch's, which depends on no cached input.</exception>
        public static VersionTagMismatchPolicy For(string name, BatchPurpose purpose)
        {
            ArgumentNullException.ThrowIfNull(name);
            return purpose switch
            {
                BatchPurpose.Read => name == VersionTagNames.Permissions ? VersionTagMismatchPolicy.Rerun : VersionTagMismatchPolicy.Refresh,
                BatchPurpose.Validate or BatchPurpose.Persist => VersionTagMismatchPolicy.Rerun,
                BatchPurpose.Maintenance => throw new ArgumentOutOfRangeException(nameof(purpose), purpose, "A maintenance batch depends on no cached input."),
                _ => throw new ArgumentOutOfRangeException(nameof(purpose), purpose, "The purpose is not one the platform defines."),
            };
        }

        /// <summary>The policy of the caller's preferences tag, which covers the cached profile: served, then refreshed.</summary>
        /// <returns><see cref="VersionTagMismatchPolicy.Refresh" />.</returns>
        public static VersionTagMismatchPolicy ForPreferences()
        {
            return VersionTagMismatchPolicy.Refresh;
        }

        /// <summary>
        ///     The dependencies of a read or validate batch whose input moved and whose policy discards
        ///     the result. A <see cref="VersionTagMismatchPolicy.Refresh" /> dependency that moved needs
        ///     no action here: the fresh tags replace the process's snapshot, which invalidates the
        ///     cached input.
        /// </summary>
        /// <param name="dependencies">The dependencies the batch declared.</param>
        /// <param name="read">The tags the batch's prelude read.</param>
        /// <returns>The moved re-run dependencies, in declaration order; empty when the result stands.</returns>
        public static IReadOnlyList<VersionTagDependency> StaleReruns(IReadOnlyList<VersionTagDependency> dependencies, VersionTagSnapshot read)
        {
            ArgumentNullException.ThrowIfNull(dependencies);
            ArgumentNullException.ThrowIfNull(read);

            // A name the database lacks reads as the empty tag, which moved from any tag it carried.
            return [.. dependencies.Where(dependency => dependency.OnMismatch == VersionTagMismatchPolicy.Rerun && read[dependency.Name] != dependency.ExpectedTag)];
        }
    }
}
