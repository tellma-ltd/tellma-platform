// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Tellma.Core.Analyzers
{
    /// <summary>Name-based symbol tests shared by the analyzers.</summary>
    internal static class SymbolExtensions
    {
        /// <summary>Whether a type's definition is the top-level type <paramref name="metadataName" /> in <paramref name="ns" />.</summary>
        /// <param name="type">The type, or null.</param>
        /// <param name="ns">The namespace, dotted.</param>
        /// <param name="metadataName">The metadata name, with its generic arity (<c>EntityService`2</c>).</param>
        /// <returns>True when it is.</returns>
        public static bool Is(this ITypeSymbol? type, string ns, string metadataName)
        {
            return type?.OriginalDefinition is INamedTypeSymbol { ContainingType: null } named
                && string.Equals(named.MetadataName, metadataName, StringComparison.Ordinal)
                && IsNamespace(named.ContainingNamespace, ns);
        }

        /// <summary>The type itself or the nearest base class whose definition is the named type, or null.</summary>
        /// <param name="type">The type, or null.</param>
        /// <param name="ns">The namespace.</param>
        /// <param name="metadataName">The metadata name.</param>
        /// <returns>The constructed type, so its type arguments can be read.</returns>
        public static INamedTypeSymbol? FindSelfOrBase(this ITypeSymbol? type, string ns, string metadataName)
        {
            for (ITypeSymbol? current = type; current is not null; current = current.BaseType)
            {
                if (current is INamedTypeSymbol named && named.Is(ns, metadataName))
                {
                    return named;
                }
            }

            return null;
        }

        /// <summary>Whether the type is, or derives from, the named class.</summary>
        /// <param name="type">The type, or null.</param>
        /// <param name="ns">The namespace.</param>
        /// <param name="metadataName">The metadata name.</param>
        /// <returns>True when it does.</returns>
        public static bool IsOrDerivesFrom(this ITypeSymbol? type, string ns, string metadataName)
        {
            return type.FindSelfOrBase(ns, metadataName) is not null;
        }

        /// <summary>Every interface the type implements whose definition is the named interface.</summary>
        /// <param name="type">The type.</param>
        /// <param name="ns">The namespace.</param>
        /// <param name="metadataName">The metadata name.</param>
        /// <returns>The constructed interfaces.</returns>
        public static IEnumerable<INamedTypeSymbol> FindInterfaces(this ITypeSymbol type, string ns, string metadataName)
        {
            if (type is INamedTypeSymbol { TypeKind: TypeKind.Interface } self && self.Is(ns, metadataName))
            {
                yield return self;
            }

            foreach (INamedTypeSymbol candidate in type.AllInterfaces)
            {
                if (candidate.Is(ns, metadataName))
                {
                    yield return candidate;
                }
            }
        }

        /// <summary>Whether the type is, or implements, the named interface.</summary>
        /// <param name="type">The type, or null.</param>
        /// <param name="ns">The namespace.</param>
        /// <param name="metadataName">The metadata name.</param>
        /// <returns>True when it does.</returns>
        public static bool IsOrImplements(this ITypeSymbol? type, string ns, string metadataName)
        {
            return type is not null && type.FindInterfaces(ns, metadataName).Any();
        }

        /// <summary>Whether the symbol carries the named attribute itself.</summary>
        /// <param name="symbol">The symbol.</param>
        /// <param name="ns">The attribute's namespace.</param>
        /// <param name="metadataName">The attribute's metadata name.</param>
        /// <returns>True when it does.</returns>
        public static bool HasAttribute(this ISymbol symbol, string ns, string metadataName)
        {
            return symbol.FindAttribute(ns, metadataName) is not null;
        }

        /// <summary>The first application of the named attribute on the symbol itself, or null.</summary>
        /// <param name="symbol">The symbol.</param>
        /// <param name="ns">The attribute's namespace.</param>
        /// <param name="metadataName">The attribute's metadata name.</param>
        /// <returns>The attribute's data.</returns>
        public static AttributeData? FindAttribute(this ISymbol symbol, string ns, string metadataName)
        {
            foreach (AttributeData attribute in symbol.GetAttributes())
            {
                if (attribute.AttributeClass.Is(ns, metadataName))
                {
                    return attribute;
                }
            }

            return null;
        }

        /// <summary>
        ///     The first application of the named attribute on the type or the nearest base class that
        ///     carries one: how an inherited class attribute resolves.
        /// </summary>
        /// <param name="type">The type.</param>
        /// <param name="ns">The attribute's namespace.</param>
        /// <param name="metadataName">The attribute's metadata name.</param>
        /// <returns>The attribute's data, or null.</returns>
        public static AttributeData? FindInheritedAttribute(this ITypeSymbol type, string ns, string metadataName)
        {
            for (ITypeSymbol? current = type; current is not null; current = current.BaseType)
            {
                if (current.FindAttribute(ns, metadataName) is AttributeData found)
                {
                    return found;
                }
            }

            return null;
        }

        /// <summary>
        ///     Whether a method, or any method it overrides, carries the named attribute: how an
        ///     inherited method attribute resolves.
        /// </summary>
        /// <param name="method">The method.</param>
        /// <param name="ns">The attribute's namespace.</param>
        /// <param name="metadataName">The attribute's metadata name.</param>
        /// <returns>True when one does.</returns>
        public static bool HasInheritedAttribute(this IMethodSymbol method, string ns, string metadataName)
        {
            for (IMethodSymbol? current = method.OriginalDefinition; current is not null; current = current.OverriddenMethod?.OriginalDefinition)
            {
                if (current.HasAttribute(ns, metadataName))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>The type that declares the symbol, then each type that contains it, innermost first.</summary>
        /// <param name="symbol">The symbol.</param>
        /// <returns>The types.</returns>
        public static IEnumerable<INamedTypeSymbol> EnclosingTypes(this ISymbol symbol)
        {
            INamedTypeSymbol? current = symbol as INamedTypeSymbol ?? symbol.ContainingType;
            while (current is not null)
            {
                yield return current;
                current = current.ContainingType;
            }
        }

        /// <summary>
        ///     The member whose body holds an operation: the method, accessor, constructor or field it
        ///     sits in, never the lambda or local function that holds it.
        /// </summary>
        /// <param name="symbol">The analysis context's containing symbol.</param>
        /// <returns>The member.</returns>
        public static ISymbol EnclosingMember(this ISymbol symbol)
        {
            ISymbol current = symbol;
            while (current is IMethodSymbol { MethodKind: MethodKind.AnonymousFunction or MethodKind.LocalFunction } && current.ContainingSymbol is ISymbol outer)
            {
                current = outer;
            }

            return current;
        }

        /// <summary>
        ///     Whether a method is a member of the named interface or of a type that implements it,
        ///     found by name and shape: the interface member itself, or an implementation of it.
        /// </summary>
        /// <param name="method">The called method.</param>
        /// <param name="ns">The interface's namespace.</param>
        /// <param name="metadataName">The interface's metadata name.</param>
        /// <returns>True when it is.</returns>
        public static bool IsOnTypeImplementing(this IMethodSymbol method, string ns, string metadataName)
        {
            return method.ContainingType.IsOrImplements(ns, metadataName);
        }

        /// <summary>Whether an operation is part of a <c>nameof</c> argument, which names a member without using it.</summary>
        /// <param name="operation">The operation.</param>
        /// <returns>True when it is.</returns>
        public static bool IsInsideNameOf(this IOperation operation)
        {
            for (IOperation? current = operation.Parent; current is not null; current = current.Parent)
            {
                if (current is INameOfOperation)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Strips the implicit conversions the compiler inserts around a value.</summary>
        /// <param name="operation">The operation.</param>
        /// <returns>The operation inside them.</returns>
        public static IOperation WithoutImplicitConversions(this IOperation operation)
        {
            IOperation current = operation;
            while (current is IConversionOperation { IsImplicit: true } conversion)
            {
                current = conversion.Operand;
            }

            return current;
        }

        /// <summary>Strips every conversion, implicit or written, around a value.</summary>
        /// <param name="operation">The operation.</param>
        /// <returns>The operation inside them.</returns>
        public static IOperation WithoutConversions(this IOperation operation)
        {
            IOperation current = operation;
            while (current is IConversionOperation conversion)
            {
                current = conversion.Operand;
            }

            return current;
        }

        /// <summary>The argument bound to the parameter named <paramref name="name" />, or null when the call omitted it.</summary>
        /// <param name="arguments">The call's arguments.</param>
        /// <param name="name">The parameter's name.</param>
        /// <returns>The value, or null for an omitted argument (its default).</returns>
        public static IOperation? ArgumentFor(this IEnumerable<IArgumentOperation> arguments, string name)
        {
            foreach (IArgumentOperation argument in arguments)
            {
                if (argument.Parameter is IParameterSymbol parameter && string.Equals(parameter.Name, name, StringComparison.Ordinal))
                {
                    return argument.ArgumentKind == ArgumentKind.DefaultValue ? null : argument.Value;
                }
            }

            return null;
        }

        /// <summary>Whether a namespace's dotted name is exactly <paramref name="name" />, compared part by part without allocating.</summary>
        /// <param name="symbol">The namespace.</param>
        /// <param name="name">The dotted name; empty for the global namespace.</param>
        /// <returns>True when it is.</returns>
        private static bool IsNamespace(INamespaceSymbol? symbol, string name)
        {
            // Walk from the innermost part outwards, matching the name from its end.
            int end = name.Length;
            for (INamespaceSymbol? current = symbol; current is { IsGlobalNamespace: false }; current = current.ContainingNamespace)
            {
                string part = current.Name;
                int start = end - part.Length;
                if (end == 0 || start < 0 || string.CompareOrdinal(name, start, part, 0, part.Length) != 0)
                {
                    return false;
                }

                if (start == 0)
                {
                    end = 0;
                }
                else if (name[start - 1] == '.')
                {
                    end = start - 1;
                }
                else
                {
                    return false;
                }
            }

            return end == 0;
        }
    }
}
