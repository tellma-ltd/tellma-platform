// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Concurrent;
using System.Reflection;
using Tellma.Core.Abstractions.Crud;

namespace Tellma.Core.Crud
{
    // Spec 0015 §10.1 and §11.1: a delete by ids, and the built-in activate and deactivate, run as one
    // SQL-only round trip unless the service or a component validates them. A hook that is not
    // overridden is the empty base method, so whether one is overridden is a fact of the class: the
    // service's protected virtual hook below its EntityService base, or a component's default
    // interface method left in place. Each class is inspected once.
    /// <summary>
    ///     Tells whether a stack's service or a component overrides a delete or action validation hook,
    ///     so the operation needs the round that runs it.
    /// </summary>
    internal static class HookOverrides
    {
        /// <summary>The service's and the components' delete validation hook.</summary>
        public const string ValidateDelete = "ValidateDeleteAsync";

        /// <summary>The service's and the components' action validation hook.</summary>
        public const string ValidateAction = "ValidateActionAsync";

        /// <summary>Every instance method a class declares itself, of any visibility.</summary>
        private const BindingFlags Declared = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;

        /// <summary>Whether a class overrides a hook, by the class and the hook's name.</summary>
        private static readonly ConcurrentDictionary<(Type Type, string Hook), bool> Overrides = new();

        /// <summary>Whether the service or any component validates the stack's deletes.</summary>
        /// <typeparam name="TEntity">The stack's entity type.</typeparam>
        /// <typeparam name="TKey">The stack's key type.</typeparam>
        /// <param name="behavior">The service's hooks.</param>
        /// <param name="validators">The entity's validators.</param>
        /// <returns>True when a delete needs its validation round.</returns>
        public static bool ValidatesDeletes<TEntity, TKey>(IEntityBehavior<TEntity, TKey> behavior, IEnumerable<IEntityValidator<TEntity>> validators)
        {
            return Validates(behavior, validators, ValidateDelete);
        }

        /// <summary>Whether the service or any component validates the stack's actions.</summary>
        /// <typeparam name="TEntity">The stack's entity type.</typeparam>
        /// <typeparam name="TKey">The stack's key type.</typeparam>
        /// <param name="behavior">The service's hooks.</param>
        /// <param name="validators">The entity's validators.</param>
        /// <returns>True when an action needs its validation round.</returns>
        public static bool ValidatesActions<TEntity, TKey>(IEntityBehavior<TEntity, TKey> behavior, IEnumerable<IEntityValidator<TEntity>> validators)
        {
            return Validates(behavior, validators, ValidateAction);
        }

        /// <summary>Whether the service or any component overrides one validation hook.</summary>
        /// <typeparam name="TEntity">The stack's entity type.</typeparam>
        /// <typeparam name="TKey">The stack's key type.</typeparam>
        /// <param name="behavior">The service's hooks.</param>
        /// <param name="validators">The entity's validators.</param>
        /// <param name="hook">The hook's name.</param>
        /// <returns>True when one does.</returns>
        private static bool Validates<TEntity, TKey>(IEntityBehavior<TEntity, TKey> behavior, IEnumerable<IEntityValidator<TEntity>> validators, string hook)
        {
            ArgumentNullException.ThrowIfNull(behavior);
            ArgumentNullException.ThrowIfNull(validators);
            return Overrides.GetOrAdd((behavior.GetType(), hook), static key => ServiceOverrides(key.Type, key.Hook, typeof(TEntity), typeof(TKey)))
                || validators.Any(validator => Overrides.GetOrAdd((validator.GetType(), hook), static key => ComponentOverrides(key.Type, key.Hook, typeof(TEntity))));
        }

        /// <summary>
        ///     Whether a service class overrides a hook of its <c>EntityService</c> base; a behavior that
        ///     derives from no such base implements every hook itself, so it counts as overriding.
        /// </summary>
        /// <param name="service">The service's class.</param>
        /// <param name="hook">The hook's name.</param>
        /// <param name="entity">The stack's entity type.</param>
        /// <param name="key">The stack's key type.</param>
        /// <returns>True when it does.</returns>
        private static bool ServiceOverrides(Type service, string hook, Type entity, Type key)
        {
            // --- The base's own virtual hook, which an override's base definition names ---
            Type root = typeof(EntityService<,>).MakeGenericType(entity, key);
            if (!root.IsAssignableFrom(service))
            {
                return true;
            }

            MethodInfo virtualHook = root.GetMethods(Declared).Single(method => method.Name == hook && method.IsVirtual && method.IsFamily);

            // --- Below the base, any class declaring an override of it ---
            for (Type? current = service; current is not null && current != root; current = current.BaseType)
            {
                foreach (MethodInfo method in current.GetMethods(Declared))
                {
                    if (method.Name == hook && method.GetBaseDefinition() is MethodInfo definition && definition.DeclaringType == root && definition.HasSameMetadataDefinitionAs(virtualHook))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>Whether a component class implements a hook of the validator interface rather than keeping its default.</summary>
        /// <param name="component">The component's class.</param>
        /// <param name="hook">The hook's name.</param>
        /// <param name="entity">The stack's entity type.</param>
        /// <returns>True when it does.</returns>
        private static bool ComponentOverrides(Type component, string hook, Type entity)
        {
            // A default interface method the class leaves in place maps to the interface's own method.
            Type contract = typeof(IEntityValidator<>).MakeGenericType(entity);
            InterfaceMapping map = component.GetInterfaceMap(contract);
            for (int index = 0; index < map.InterfaceMethods.Length; index++)
            {
                if (map.InterfaceMethods[index].Name == hook)
                {
                    return map.TargetMethods[index].DeclaringType != contract;
                }
            }

            return false;
        }
    }
}
