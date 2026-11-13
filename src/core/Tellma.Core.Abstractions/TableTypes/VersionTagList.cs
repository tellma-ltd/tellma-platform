// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.ComponentModel.DataAnnotations;
using Tellma.Core.Abstractions.Entities;

namespace Tellma.Core.Abstractions.TableTypes
{
    /// <summary>
    ///     A row of the version-tag guard's table-valued parameter: one tenant-level tag name a persist
    ///     batch depends on or bumps, the tag it expects, and whether the batch itself bumps the name.
    /// </summary>
    /// <remarks>
    ///     A standalone table-type shape beside <see cref="IdList" /> and <see cref="StringList" />,
    ///     annotated with BCL data annotations and the platform's <see cref="TableTypeAttribute" /> only.
    /// </remarks>
    [TableType]
    public class VersionTagList
    {
        /// <summary>The tag name, <c>nvarchar(128)</c>; the key.</summary>
        [Key]
        [MaxLength(128)]
        public string Name { get; set; } = null!;

        /// <summary>
        ///     The tag the batch expects, <c>uniqueidentifier</c>; null for a name the batch bumps without
        ///     depending on it, which the guard locks but never compares. An expectation of
        ///     <see cref="Caching.VersionTag.None" /> (all zeros) is compared like any other, so it fails
        ///     once the row exists.
        /// </summary>
        public Guid? Tag { get; set; }

        /// <summary>
        ///     True when the batch itself bumps the name; <c>bit</c>. The guard reads a bumped name's row
        ///     under an exclusive lock and every other name's under a shared one, so the bump converts no
        ///     lock and two writers of one row serialise at the guard.
        /// </summary>
        public bool Bumped { get; set; }
    }
}
