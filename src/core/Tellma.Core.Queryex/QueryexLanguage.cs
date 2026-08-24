// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Queryex
{
    /// <summary>
    ///     Facts about the Queryex language this engine implements.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Queryex text is persisted in configuration — report definitions, saved filters,
    ///         access criteria — and recompiled long after it was written. A host therefore stores
    ///         the version each stored expression was validated under and supplies it back when
    ///         that text is validated or compiled again, which is what lets the language change
    ///         meaning without silently changing what a stored expression means.
    ///     </para>
    ///     <para>
    ///         The obligation matters most where the text is a security boundary. An access
    ///         criterion whose meaning shifts from restrictive to permissive under a new engine is
    ///         not a compatibility inconvenience, and the stamp is what makes that class of change
    ///         detectable rather than silent.
    ///     </para>
    /// </remarks>
    public static class QueryexLanguage
    {
        /// <summary>
        ///     The language version this engine writes. Incremented only by a change that alters
        ///     the meaning of some currently-valid expression; additive changes, such as a new
        ///     function or a new calendar code, do not move it.
        /// </summary>
        public const int Version = 1;

        /// <summary>
        ///     The oldest language version this engine still compiles.
        /// </summary>
        /// <remarks>
        ///     Equal to <see cref="Version" /> while there has only ever been one. It moves above
        ///     it only when support for a version is dropped, which strands every expression
        ///     stored under that version until a migration rewrites it.
        /// </remarks>
        public const int Minimum = 1;

        /// <summary>Whether this engine compiles expressions authored under a given version.</summary>
        /// <param name="version">The version an expression was validated under.</param>
        /// <returns>True when this engine can compile it.</returns>
        /// <remarks>
        ///     A version above <see cref="Version" /> is refused rather than accepted optimistically:
        ///     it was authored by a newer engine, under rules this one does not have, so compiling
        ///     it here would produce whatever this engine's rules happen to make of the text.
        /// </remarks>
        public static bool IsSupported(int version)
        {
            return version is >= Minimum and <= Version;
        }
    }
}
