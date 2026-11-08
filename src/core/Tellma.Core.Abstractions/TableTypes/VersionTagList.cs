// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.ComponentModel.DataAnnotations;

namespace Tellma.Core.Abstractions.TableTypes
{
    /// <summary>
    ///     A row of the version-tag guard's table-valued parameter: one tenant-level tag name a persist
    ///     batch depends on, the tag it expects, and whether the batch itself bumps the name.
    /// </summary>
    /// <remarks>
    ///     A standalone table-type shape beside <see cref="IdList" /> and <see cref="StringList" />,
    ///     annotated with BCL data annotations only.
    /// </remarks>
    public class VersionTagList
    {
        /// <summary>The tag name, <c>nvarchar(128)</c>; the key.</summary>
        [Key]
        [MaxLength(128)]
        public string Name { get; set; } = null!;

        /// <summary>The tag the batch expects, <c>uniqueidentifier</c>.</summary>
        public Guid Tag { get; set; }

        /// <summary>
        ///     True when the batch itself bumps the name, so the guard reads the row under an update
        ///     lock and two writers of it serialise instead of deadlocking; <c>bit</c>.
        /// </summary>
        public bool Bumped { get; set; }
    }
}
