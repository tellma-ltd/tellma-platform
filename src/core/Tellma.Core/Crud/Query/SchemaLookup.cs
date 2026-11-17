// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Queryex;

namespace Tellma.Core.Crud.Query
{
    /// <summary>
    ///     Name lookups over a tenant's Queryex schema, ignoring case as the engine resolves names.
    /// </summary>
    internal static class SchemaLookup
    {
        /// <summary>A property of an entity, by name.</summary>
        /// <param name="entity">The entity.</param>
        /// <param name="name">The property's name.</param>
        /// <returns>The property; null when the schema does not declare it.</returns>
        public static PropertyDescriptor? Property(EntityDescriptor entity, string name)
        {
            ArgumentNullException.ThrowIfNull(entity);
            ArgumentNullException.ThrowIfNull(name);
            foreach (PropertyDescriptor property in entity.Properties)
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return property;
                }
            }

            return null;
        }

        /// <summary>A navigation of an entity, by name.</summary>
        /// <param name="entity">The entity.</param>
        /// <param name="name">The navigation's name.</param>
        /// <returns>The navigation; null when the schema does not declare it.</returns>
        public static NavigationDescriptor? Navigation(EntityDescriptor entity, string name)
        {
            ArgumentNullException.ThrowIfNull(entity);
            ArgumentNullException.ThrowIfNull(name);
            foreach (NavigationDescriptor navigation in entity.Navigations)
            {
                if (string.Equals(navigation.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return navigation;
                }
            }

            return null;
        }

        /// <summary>Whether a path walks navigations from an entity and ends at one of the last entity's properties.</summary>
        /// <param name="entity">The entity the path starts at.</param>
        /// <param name="segments">The path's segments; at least one.</param>
        /// <returns>True when the path resolves.</returns>
        public static bool Resolves(EntityDescriptor entity, IReadOnlyList<string> segments)
        {
            ArgumentNullException.ThrowIfNull(entity);
            ArgumentNullException.ThrowIfNull(segments);
            EntityDescriptor at = entity;
            for (int index = 0; index < segments.Count - 1; index++)
            {
                if (Navigation(at, segments[index]) is not NavigationDescriptor navigation)
                {
                    return false;
                }

                at = navigation.Target;
            }

            return segments.Count > 0 && Property(at, segments[^1]) is not null;
        }
    }
}
