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
        ///     The tag the batch expects, <c>uniqueidentifier</c>; all zeros for a name the batch bumps
        ///     without depending on it, which the guard locks but never compares.
        /// </summary>
        public Guid Tag { get; set; }

        /// <summary>
        ///     True when the batch itself bumps the name; <c>bit</c>. A batch that bumps any name reads
        ///     every row it names under an update lock, so two writers serialise instead of deadlocking.
        /// </summary>
        public bool Bumped { get; set; }
    }
}
