// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Tellma.Core.Abstractions.Validation;

namespace Tellma.Core.AspNetCore.Problems
{
    /// <summary>What a validation path's leading index names: the request member that carries the payload's rows.</summary>
    internal enum ValidationRoot
    {
        /// <summary>A body without rows: a leading index renders under <c>body</c>, and the empty path is <c>body</c>.</summary>
        Body,

        /// <summary>A save: a leading index is a row of <c>entities</c>.</summary>
        Entities,

        /// <summary>An entity action: a leading index is a target of <c>ids</c>.</summary>
        Ids,

        /// <summary>An entity action on one target: its one target is <c>id</c>.</summary>
        SingleId,
    }

    // Spec 0016 §7.3: the camelCase index grammar the SPA splits back into property and index
    // segments, nothing else interpreting it.
    /// <summary>Renders validation paths on the wire: <c>entities[3].roleMemberships[1].roleId</c>, <c>ids[2]</c>, <c>id</c>, <c>body</c>.</summary>
    internal static class ValidationPaths
    {
        /// <summary>The path of the whole body.</summary>
        public const string BodyPath = "body";

        /// <summary>The request member of a save's rows.</summary>
        private const string EntitiesMember = "entities";

        /// <summary>The request member of an action's targets.</summary>
        private const string IdsMember = "ids";

        /// <summary>The request member of a single-target action's target.</summary>
        private const string IdMember = "id";

        /// <summary>The property segment an action's target list may be reported under.</summary>
        private const string IdsProperty = "Ids";

        /// <summary>Renders a path.</summary>
        /// <param name="path">The path.</param>
        /// <param name="root">What a leading index names.</param>
        /// <returns>The wire path.</returns>
        public static string Render(ValidationPath path, ValidationRoot root)
        {
            ArgumentNullException.ThrowIfNull(path);
            IReadOnlyList<PathSegment> segments = path.Segments;
            if (segments.Count == 0)
            {
                return BodyPath;
            }

            // --- The head: a leading index under the member carrying the rows, or the single target ---
            StringBuilder text = new();
            int start = 0;
            if (segments[0] is IndexSegment row)
            {
                _ = root switch
                {
                    ValidationRoot.Entities => Index(text.Append(EntitiesMember), row.Index),
                    ValidationRoot.SingleId when row.Index == 0 => text.Append(IdMember),
                    ValidationRoot.Ids or ValidationRoot.SingleId => Index(text.Append(IdsMember), row.Index),
                    ValidationRoot.Body => Index(text.Append(BodyPath), row.Index),
                    _ => throw new ArgumentOutOfRangeException(nameof(root), root, "The root is not a defined validation root."),
                };
                start = 1;
            }
            else if (root == ValidationRoot.SingleId && segments is [PropertySegment { Name: IdsProperty }, IndexSegment { Index: 0 }, ..])
            {
                _ = text.Append(IdMember);
                start = 2;
            }

            // --- The rest: each property camelCased after a dot, each index in brackets ---
            for (int i = start; i < segments.Count; i++)
            {
                switch (segments[i])
                {
                    case IndexSegment index:
                        _ = Index(text, index.Index);
                        break;

                    case PropertySegment property:
                        if (text.Length > 0)
                        {
                            _ = text.Append('.');
                        }

                        _ = text.Append(JsonNamingPolicy.CamelCase.ConvertName(property.Name));
                        break;

                    default:
                        throw new InvalidOperationException($"A validation path holds an unknown segment kind, {segments[i].GetType().Name}.");
                }
            }

            return text.ToString();
        }

        /// <summary>
        ///     The type that declares the last property a path names, walked from the payload's row
        ///     type through each property's type and each list's element type.
        /// </summary>
        /// <param name="path">The path.</param>
        /// <param name="root">What a leading index names.</param>
        /// <param name="rowType">The type of the payload's rows; null when the payload has none.</param>
        /// <returns>The declaring type, or null when the path names no property or leaves the row type's members.</returns>
        public static Type? OwnerOfLastProperty(ValidationPath path, ValidationRoot root, Type? rowType)
        {
            ArgumentNullException.ThrowIfNull(path);
            IReadOnlyList<PathSegment> segments = path.Segments;

            // Only a payload of rows has a row type the path starts from.
            if (rowType is null || root == ValidationRoot.Body || segments.Count == 0 || segments[0] is not IndexSegment)
            {
                return null;
            }

            Type current = rowType;
            Type? owner = null;
            for (int i = 1; i < segments.Count; i++)
            {
                if (segments[i] is not PropertySegment property)
                {
                    continue;
                }

                // An index follows a list's property, so each property's type is unwrapped to its element.
                PropertyInfo? member = current.GetProperty(property.Name, BindingFlags.Public | BindingFlags.Instance);
                if (member is null)
                {
                    return null;
                }

                owner = current;
                current = ElementOf(member.PropertyType);
            }

            return owner;
        }

        /// <summary>Appends an index in brackets.</summary>
        /// <param name="text">The text so far.</param>
        /// <param name="index">The index.</param>
        /// <returns>The text.</returns>
        private static StringBuilder Index(StringBuilder text, int index)
        {
            return text.Append('[').Append(index.ToString(CultureInfo.InvariantCulture)).Append(']');
        }

        /// <summary>A list type's element type, or the type itself.</summary>
        /// <param name="type">The type.</param>
        /// <returns>The element type of a generic list other than a string, else the type.</returns>
        private static Type ElementOf(Type type)
        {
            if (type == typeof(string))
            {
                return type;
            }

            Type? enumerable = type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>)
                ? type
                : type.GetInterfaces().FirstOrDefault(static candidate => candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(IEnumerable<>));
            return enumerable?.GetGenericArguments()[0] ?? type;
        }
    }
}
