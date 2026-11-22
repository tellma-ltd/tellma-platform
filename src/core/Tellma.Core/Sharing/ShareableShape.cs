// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Reflection;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Entities;
using Tellma.Core.Abstractions.Sharing;

namespace Tellma.Core.Sharing
{
    /// <summary>
    ///     How a shareable entity and its share entity meet in the model: the shareable's metadata, the
    ///     share's, and the share's reference to the shared row, with the names the criteria, the
    ///     commands and the extra spell.
    /// </summary>
    /// <param name="Entity">The shareable entity's metadata.</param>
    /// <param name="Share">The share entity's metadata.</param>
    /// <param name="Row">The share's reference to the shared row.</param>
    internal sealed record ShareableShape(EntityMetadata Entity, EntityMetadata Share, ReferenceMetadata Row)
    {
        /// <summary>The share's navigation to the grant's user.</summary>
        public const string UserNavigation = "User";

        /// <summary>The share's navigation to the grant's role.</summary>
        public const string RoleNavigation = "Role";

        /// <summary>The share's navigation to the user who made the grant.</summary>
        public const string SharedByNavigation = "SharedBy";

        /// <summary>The notification dedup key of a grant of a row: the entity's name and the row's id.</summary>
        /// <param name="id">The row's id.</param>
        /// <returns>The key.</returns>
        public string DedupKey(int id)
        {
            return DedupKeyPrefix + id.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>The start every dedup key of the entity's grants shares: the entity's name and a colon.</summary>
        public string DedupKeyPrefix => Entity.Name + ":";

        /// <summary>The model's shape of a shareable entity and its share entity.</summary>
        /// <param name="metadata">The model's entity metadata.</param>
        /// <param name="entity">The shareable entity, or the default its leaf replaces.</param>
        /// <param name="share">The share entity.</param>
        /// <returns>The shape.</returns>
        /// <exception cref="InvalidOperationException">The share entity has other than one reference to the shareable entity; the startup check reports it.</exception>
        public static ShareableShape Of(IEntityMetadataProvider metadata, Type entity, Type share)
        {
            ArgumentNullException.ThrowIfNull(metadata);
            EntityMetadata shareable = metadata.Get(entity);
            EntityMetadata shares = metadata.Get(share);
            IReadOnlyList<ReferenceMetadata> rows = RowReferences(shareable, shares);
            return rows.Count == 1
                ? new ShareableShape(shareable, shares, rows[0])
                : throw new InvalidOperationException(
                    $"The share entity {shares.Name} has {rows.Count} references to {shareable.Name}; a share entity has exactly one.");
        }

        /// <summary>The share entity's references to the shareable entity.</summary>
        /// <param name="entity">The shareable entity's metadata.</param>
        /// <param name="share">The share entity's metadata.</param>
        /// <returns>The references.</returns>
        public static IReadOnlyList<ReferenceMetadata> RowReferences(EntityMetadata entity, EntityMetadata share)
        {
            ArgumentNullException.ThrowIfNull(entity);
            ArgumentNullException.ThrowIfNull(share);
            return [.. share.References.Where(reference => string.Equals(reference.Target.Name, entity.Name, StringComparison.Ordinal))];
        }

        /// <summary>The property of a share class that references the shareable entity, by its annotation; null unless exactly one does.</summary>
        /// <param name="share">The share class.</param>
        /// <param name="entity">The shareable entity, or a class its leaf derives from.</param>
        /// <returns>The property's name, or null.</returns>
        public static string? RowForeignKey(Type share, Type entity)
        {
            ArgumentNullException.ThrowIfNull(share);
            ArgumentNullException.ThrowIfNull(entity);
            string[] found = [.. share.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(property => property.GetCustomAttribute<ReferencesAttribute>() is { } references && references.Target.IsAssignableFrom(entity))
                .Select(static property => property.Name)];
            return found.Length == 1 ? found[0] : null;
        }

        /// <summary>Whether a type is a closed class that is or derives from a closed generic type's definition closed over given arguments.</summary>
        /// <param name="type">The type.</param>
        /// <param name="definition">The open generic definition, such as <c>typeof(ShareableService&lt;,&gt;)</c>.</param>
        /// <param name="arguments">The definition's arguments.</param>
        /// <returns>True when it derives from the closed type.</returns>
        public static bool DerivesFrom(Type? type, Type definition, params Type[] arguments)
        {
            ArgumentNullException.ThrowIfNull(definition);
            Type closed = definition.MakeGenericType(arguments);
            return type is not null && closed.IsAssignableFrom(type);
        }

        /// <summary>Whether a type is a shareable entity class: closed, concrete, a top-level entity implementing <see cref="IShareable" />.</summary>
        /// <param name="type">The type.</param>
        /// <returns>True when it is.</returns>
        public static bool IsShareable(Type? type)
        {
            return type is { IsClass: true, IsAbstract: false, ContainsGenericParameters: false }
                && typeof(TopLevelEntity).IsAssignableFrom(type)
                && typeof(IShareable).IsAssignableFrom(type);
        }

        /// <summary>Whether a type is a share entity class: closed, concrete, an <c>Entity&lt;int&gt;</c> implementing <see cref="IShareRow" />.</summary>
        /// <param name="type">The type.</param>
        /// <returns>True when it is.</returns>
        public static bool IsShareRow(Type? type)
        {
            return type is { IsClass: true, IsAbstract: false, ContainsGenericParameters: false }
                && typeof(Entity<int>).IsAssignableFrom(type)
                && typeof(IShareRow).IsAssignableFrom(type);
        }
    }
}
