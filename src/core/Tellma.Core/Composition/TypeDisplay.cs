// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;

namespace Tellma.Core.Composition
{
    /// <summary>
    ///     Renders types and interface members as C# source, for problem texts and the code a fix
    ///     tells the reader to write.
    /// </summary>
    internal static class TypeDisplay
    {
        private static readonly Dictionary<Type, string> Keywords = new()
        {
            [typeof(bool)] = "bool",
            [typeof(byte)] = "byte",
            [typeof(sbyte)] = "sbyte",
            [typeof(char)] = "char",
            [typeof(decimal)] = "decimal",
            [typeof(double)] = "double",
            [typeof(float)] = "float",
            [typeof(int)] = "int",
            [typeof(uint)] = "uint",
            [typeof(long)] = "long",
            [typeof(ulong)] = "ulong",
            [typeof(short)] = "short",
            [typeof(ushort)] = "ushort",
            [typeof(nint)] = "nint",
            [typeof(nuint)] = "nuint",
            [typeof(object)] = "object",
            [typeof(string)] = "string",
            [typeof(void)] = "void",
        };

        /// <summary>Renders a type as C# without namespaces: <c>Center</c>, <c>int?</c>, <c>List&lt;string&gt;</c>.</summary>
        /// <param name="type">The type.</param>
        /// <returns>The rendering.</returns>
        public static string Short(Type type)
        {
            return Render(type, qualified: false);
        }

        /// <summary>Renders a type as C# with namespaces: <c>Tellma.Module.Gl.Center</c>.</summary>
        /// <param name="type">The type.</param>
        /// <returns>The rendering.</returns>
        public static string Qualified(Type type)
        {
            return Render(type, qualified: true);
        }

        /// <summary>
        ///     Lists the members a class must write to implement <paramref name="shape" />: its
        ///     abstract properties, methods and events and those of the interfaces it extends,
        ///     rendered as C# declarations.
        /// </summary>
        /// <param name="shape">The interface.</param>
        /// <returns>One declaration per member, in declaration order, interface before its bases.</returns>
        public static IReadOnlyList<string> RequiredMembers(Type shape)
        {
            NullabilityInfoContext nullability = new();
            List<string> members = [];

            foreach (Type type in (Type[])[shape, .. shape.GetInterfaces()])
            {
                const BindingFlags Declared = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

                // --- Properties ---
                foreach (PropertyInfo property in type.GetProperties(Declared))
                {
                    MethodInfo? getter = property.GetMethod;
                    MethodInfo? setter = property.SetMethod;

                    // A default implementation needs nothing from the implementing class.
                    if (getter is { IsAbstract: false } || setter is { IsAbstract: false })
                    {
                        continue;
                    }

                    StringBuilder accessors = new();
                    if (getter is not null)
                    {
                        accessors.Append("get; ");
                    }

                    if (setter is not null)
                    {
                        bool isInit = setter.ReturnParameter.GetRequiredCustomModifiers().Contains(typeof(IsExternalInit));
                        accessors.Append(isInit ? "init; " : "set; ");
                    }

                    string propertyType = WithNullability(property.PropertyType, nullability.Create(property).ReadState);
                    members.Add($"{StaticPrefix(getter ?? setter)}{propertyType} {property.Name} {{ {accessors}}}");
                }

                // --- Methods (accessors excluded) ---
                foreach (MethodInfo method in type.GetMethods(Declared))
                {
                    if (method.IsSpecialName || !method.IsAbstract)
                    {
                        continue;
                    }

                    string parameters = string.Join(", ", method.GetParameters().Select(
                        parameter => $"{WithNullability(parameter.ParameterType, nullability.Create(parameter).ReadState)} {parameter.Name}"));
                    string returnType = WithNullability(method.ReturnType, nullability.Create(method.ReturnParameter).ReadState);
                    members.Add($"{StaticPrefix(method)}{returnType} {method.Name}({parameters})");
                }

                // --- Events ---
                foreach (EventInfo @event in type.GetEvents(Declared))
                {
                    if (@event.AddMethod is { IsAbstract: true } && @event.EventHandlerType is Type handler)
                    {
                        members.Add($"{StaticPrefix(@event.AddMethod)}event {WithNullability(handler, nullability.Create(@event).ReadState)} {@event.Name}");
                    }
                }
            }

            return members;
        }

        /// <summary>The <c>static </c> prefix of a static member, else nothing.</summary>
        /// <param name="accessor">The member's method or accessor.</param>
        /// <returns>The prefix.</returns>
        private static string StaticPrefix(MethodInfo? accessor)
        {
            return accessor is { IsStatic: true } ? "static " : string.Empty;
        }

        /// <summary>Renders a type with the <c>?</c> of a nullable reference.</summary>
        /// <param name="type">The type.</param>
        /// <param name="state">Its nullability where it is used.</param>
        /// <returns>The rendering.</returns>
        private static string WithNullability(Type type, NullabilityState state)
        {
            // Nullable value types already render with '?'; only reference types need the annotation.
            string rendered = Short(type);
            return state == NullabilityState.Nullable && !type.IsValueType && !rendered.EndsWith('?')
                ? rendered + "?"
                : rendered;
        }

        /// <summary>Renders a type as C#.</summary>
        /// <param name="type">The type.</param>
        /// <param name="qualified">Whether to prefix namespaces.</param>
        /// <returns>The rendering.</returns>
        private static string Render(Type type, bool qualified)
        {
            if (Keywords.TryGetValue(type, out string? keyword))
            {
                return keyword;
            }

            if (Nullable.GetUnderlyingType(type) is Type underlying)
            {
                return Render(underlying, qualified) + "?";
            }

            if (type.IsArray)
            {
                return Render(type.GetElementType()!, qualified) + "[" + new string(',', type.GetArrayRank() - 1) + "]";
            }

            if (type.IsByRef)
            {
                return "ref " + Render(type.GetElementType()!, qualified);
            }

            if (type.IsGenericParameter)
            {
                return type.Name;
            }

            // Nested types render through their declaring type; generic arguments of the declaring
            // type are carried by the nested type's own argument list, so the declaring type renders
            // by name alone.
            string prefix = type.DeclaringType is Type declaring
                ? Render(declaring.IsGenericType ? declaring.GetGenericTypeDefinition() : declaring, qualified).Split('<')[0] + "."
                : qualified && !string.IsNullOrEmpty(type.Namespace) ? type.Namespace + "." : string.Empty;

            if (!type.IsGenericType)
            {
                return prefix + type.Name;
            }

            string name = type.Name;
            int tick = name.IndexOf('`', StringComparison.Ordinal);
            if (tick >= 0)
            {
                name = name[..tick];
            }

            string arguments = string.Join(", ", type.GetGenericArguments().Select(argument => Render(argument, qualified)));
            return $"{prefix}{name}<{arguments}>";
        }
    }
}
