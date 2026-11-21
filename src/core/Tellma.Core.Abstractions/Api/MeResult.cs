// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Caching;

namespace Tellma.Core.Abstractions.Api
{
    /// <summary>
    ///     The <c>me</c> envelope: the caller's profile, preference bag and access summary, with the
    ///     four client tags as the version-tags header carries them, so a client caches all of it
    ///     under the tags it was read with.
    /// </summary>
    /// <param name="User">The caller's profile.</param>
    /// <param name="PreferencesTag">The caller's preferences tag, in its wire form; the same value as the <c>preferences</c> item of <paramref name="Tags" />.</param>
    /// <param name="Preferences">The caller's preference bag: every key it holds, with its value.</param>
    /// <param name="Access">What the caller may do.</param>
    /// <param name="Tags">
    ///     The client tags by item name (<c>settings</c>, <c>permissions</c>, <c>preferences</c>,
    ///     <c>entities</c>), each in its wire form, exactly as the version-tags header carries them.
    /// </param>
    public sealed record MeResult(
        UserProfileView User,
        string PreferencesTag,
        IReadOnlyDictionary<string, string> Preferences,
        AccessSummary Access,
        IReadOnlyDictionary<string, string> Tags)
    {
        /// <summary>
        ///     The envelope's format version, which the <c>preferences</c> and <c>permissions</c> wire
        ///     tags carry, so every client refetches when the envelope's shape changes.
        /// </summary>
        public const int FormatVersion = VersionTagWire.MeFormatVersion;
    }
}
