// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Composition
{
    /// <summary>
    ///     How a class a pack writes against its default entities follows a distribution's leaves: a
    ///     stack's service, a companion, a component, a provisioning step.
    /// </summary>
    internal static class LeafClosure
    {
        /// <summary>
        ///     A constructed generic class closed over the leaf of each type argument instead; any
        ///     other class as it is.
        /// </summary>
        /// <param name="type">The contributed class, such as <c>CenterService&lt;Center&gt;</c>.</param>
        /// <param name="leafOf">Resolves an entity to the leaf registered for it, else itself.</param>
        /// <returns>The class to register, such as <c>CenterService&lt;MyCenter&gt;</c>.</returns>
        /// <exception cref="ArgumentException">A leaf does not satisfy a type parameter's constraints.</exception>
        public static Type Close(Type type, Func<Type, Type> leafOf)
        {
            ArgumentNullException.ThrowIfNull(type);
            ArgumentNullException.ThrowIfNull(leafOf);
            if (!type.IsConstructedGenericType)
            {
                return type;
            }

            Type[] arguments = type.GetGenericArguments();
            Type[] leaves = [.. arguments.Select(leafOf)];
            return arguments.SequenceEqual(leaves) ? type : type.GetGenericTypeDefinition().MakeGenericType(leaves);
        }

        /// <summary>
        ///     The closed construction of an open generic base class that a class derives from, or
        ///     null when it derives from none.
        /// </summary>
        /// <param name="type">The class.</param>
        /// <param name="openBase">The open generic base class definition, such as <c>EntityService&lt;,&gt;</c>.</param>
        /// <returns>The construction, or null.</returns>
        public static Type? GenericBase(Type type, Type openBase)
        {
            ArgumentNullException.ThrowIfNull(type);
            ArgumentNullException.ThrowIfNull(openBase);
            for (Type? current = type; current is not null; current = current.BaseType)
            {
                if (current.IsGenericType && current.GetGenericTypeDefinition() == openBase)
                {
                    return current;
                }
            }

            return null;
        }
    }
}
