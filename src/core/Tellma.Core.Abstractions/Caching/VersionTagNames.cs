// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Caching
{
    /// <summary>
    ///     The tenant-level version-tag names the platform itself uses, and the form of an entity's.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A name has one of two forms, at most <see cref="MaxNameLength" /> characters, compared
    ///         ordinally. A plain name (the platform's three and every pack's) is lower-case ASCII,
    ///         <c>^[a-z][a-z0-9.-]*$</c>. An entity's name is <see cref="EntityPrefix" /> followed by
    ///         the entity's qualified name (<c>entity:gl.Center</c>), produced only by
    ///         <see cref="ForEntity" /> for a cacheable entity; a hand-written <c>entity:</c> name
    ///         fails the startup gate.
    ///     </para>
    ///     <para>
    ///         User-level tags are not names: they are the two columns of
    ///         <see cref="UserVersionTagNames" />.
    ///     </para>
    /// </remarks>
    public static class VersionTagNames
    {
        /// <summary>The tenant's settings: bumped by every write to the settings tables.</summary>
        public const string Settings = "settings";

        /// <summary>
        ///     What every member of the tenant may do: bumped by writes to roles and permissions, and
        ///     when the tenant's content languages change. A user's own permissions also depend on
        ///     their <see cref="UserVersionTagNames.Permissions" /> tag.
        /// </summary>
        public const string Permissions = "permissions";

        /// <summary>The composite of every cacheable entity: bumped whenever any one of them is.</summary>
        public const string Entities = "entities";

        /// <summary>The reserved prefix of an entity's name.</summary>
        public const string EntityPrefix = "entity:";

        /// <summary>The longest a name may be.</summary>
        public const int MaxNameLength = 128;

        /// <summary>The name of a cacheable entity's tag.</summary>
        /// <param name="entityName">The entity's qualified name (<c>gl.Center</c>).</param>
        /// <returns>The name, <c>entity:gl.Center</c>.</returns>
        /// <exception cref="ArgumentException">
        ///     <paramref name="entityName" /> is null, empty or white space, or the name would be longer
        ///     than <see cref="MaxNameLength" />.
        /// </exception>
        public static string ForEntity(string entityName)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(entityName);
            string name = EntityPrefix + entityName;
            return name.Length <= MaxNameLength
                ? name
                : throw new ArgumentException(
                    $"The tag name of entity '{entityName}' would be {name.Length} characters, more than {MaxNameLength}.", nameof(entityName));
        }
    }
}
