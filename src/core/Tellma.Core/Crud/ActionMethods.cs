// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Reflection;
using System.Text;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.Composition;
using Tellma.Core.Composition.Checks;

namespace Tellma.Core.Crud
{
    /// <summary>
    ///     Reads the action methods of a class: every method marked <see cref="EntityActionAttribute" />
    ///     or <see cref="ApiActionAttribute" />, and the descriptor each conforming one projects.
    ///     A method that does not conform is a problem naming its class and its name.
    /// </summary>
    internal static class ActionMethods
    {
        /// <summary>Every method of a class and its bases that the action reader inspects.</summary>
        private const BindingFlags Everything = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        /// <summary>
        ///     The methods of a class, its bases' included, that carry an action attribute, each once
        ///     as its most derived override, in declaration order: a base class's before its
        ///     subclass's, each class's in the order its source declares them.
        /// </summary>
        /// <param name="type">The class.</param>
        /// <returns>The methods.</returns>
        public static IReadOnlyList<MethodInfo> Declared(Type type)
        {
            ArgumentNullException.ThrowIfNull(type);

            // --- Up the chain, most derived first, so an override shadows what it overrides ---
            List<Type> chain = [];
            for (Type? current = type; current is not null && current != typeof(object); current = current.BaseType)
            {
                chain.Add(current);
            }

            HashSet<(Type? Declaring, int Token)> overridden = [];
            List<(int Depth, int Token, MethodInfo Method)> found = [];
            for (int depth = 0; depth < chain.Count; depth++)
            {
                foreach (MethodInfo method in chain[depth].GetMethods(Everything))
                {
                    MethodInfo definition = method.GetBaseDefinition();
                    if (!overridden.Add((definition.DeclaringType, definition.MetadataToken)))
                    {
                        continue;
                    }

                    if (method.IsDefined(typeof(EntityActionAttribute), inherit: true) || method.IsDefined(typeof(ApiActionAttribute), inherit: true))
                    {
                        // Ordered where the action was first declared: the base definition's class and token.
                        int declaredAt = definition.DeclaringType is Type declaring ? chain.IndexOf(declaring) : -1;
                        found.Add((declaredAt < 0 ? depth : declaredAt, definition.MetadataToken, method));
                    }
                }
            }

            return [.. found.OrderByDescending(static entry => entry.Depth).ThenBy(static entry => entry.Token).Select(static entry => entry.Method)];
        }

        /// <summary>How a problem names a method: its class and its name.</summary>
        /// <param name="handler">The class the method is read from.</param>
        /// <param name="method">The method.</param>
        /// <returns>The words.</returns>
        public static string Describe(Type handler, MethodInfo method)
        {
            return $"{TypeDisplay.Qualified(handler)}.{method.Name}";
        }

        /// <summary>
        ///     The descriptor of an <see cref="EntityActionAttribute" /> method of a stack's service:
        ///     a public instance method taking the action context of the leaf and optionally an
        ///     arguments record, returning a task with or without a result.
        /// </summary>
        /// <param name="service">The stack's service.</param>
        /// <param name="method">The method.</param>
        /// <param name="attribute">Its attribute.</param>
        /// <param name="leaf">The stack's leaf.</param>
        /// <param name="key">The stack's key type.</param>
        /// <param name="problems">Receives the problems.</param>
        /// <returns>The descriptor, or null when the method does not conform.</returns>
        public static EntityActionDescriptor? Entity(
            Type service, MethodInfo method, EntityActionAttribute attribute, Type leaf, Type key, List<CompositionProblem> problems)
        {
            string described = Describe(service, method);
            Type context = typeof(ActionContext<,>).MakeGenericType(leaf, key);
            const string Shape = "public Task M(ActionContext<TEntity, TKey> context[, TArguments arguments]), returning Task, ValueTask, Task<TResult> or ValueTask<TResult>";

            // --- A public instance method, not generic of its own ---
            if (!method.IsPublic || method.IsStatic || method.IsGenericMethodDefinition)
            {
                Report(problems, $"The entity action '{attribute.Name}' of {described} is not a public, non-generic instance method; an action is reached only through the stack's pipeline.", $"Declare it as {Shape}.");
                return null;
            }

            // --- The parameters: the context, then the arguments when there are any ---
            ParameterInfo[] parameters = method.GetParameters();
            if (parameters.Length is < 1 or > 2 || parameters[0].ParameterType != context)
            {
                Report(
                    problems,
                    $"The entity action '{attribute.Name}' of {described} takes ({string.Join(", ", parameters.Select(static parameter => TypeDisplay.Short(parameter.ParameterType)))}); an entity action takes {TypeDisplay.Short(context)} and, optionally, its arguments record.",
                    $"Declare it as {Shape}.");
                return null;
            }

            Type? arguments = parameters.Length == 2 ? parameters[1].ParameterType : null;
            if (arguments is not null && !IsSerialisableRecord(arguments, out string? why))
            {
                Report(
                    problems,
                    $"The entity action '{attribute.Name}' of {described} takes the arguments {TypeDisplay.Qualified(arguments)}, which {why}",
                    "Declare the arguments as a record of JSON-serialisable properties: public sealed record PostArguments(DateOnly PostingDate);");
                return null;
            }

            // --- The result: none answers the rows read back, one of its own answers itself ---
            if (!TryResult(method.ReturnType, out Type? result))
            {
                Report(problems, $"The entity action '{attribute.Name}' of {described} returns {TypeDisplay.Short(method.ReturnType)}.", $"Declare it as {Shape}.");
                return null;
            }

            if (attribute.SingleTarget && result is null)
            {
                Report(
                    problems,
                    $"The entity action '{attribute.Name}' of {described} declares SingleTarget but returns no result; a single-target action answers its own result.",
                    "Return Task<TResult> or ValueTask<TResult>, or remove SingleTarget.");
                return null;
            }

            return new EntityActionDescriptor(
                attribute.Name,
                attribute.Description,
                attribute.Mutation,
                attribute.AllowWhenReadOnly,
                attribute.Idempotent,
                attribute.Long,
                attribute.Destructive,
                attribute.Mcp,
                attribute.ContractRevision,
                ResultType: result ?? typeof(EntitiesResult<>).MakeGenericType(leaf),
                Action: attribute.Action ?? StackNames.ActionOf(attribute.Name),
                attribute.SupportsFilter,
                attribute.LoadChildren,
                attribute.PostCheck,
                attribute.SingleTarget,
                IsBuiltIn: false,
                ArgumentsType: arguments);
        }

        /// <summary>
        ///     The descriptor of an <see cref="ApiActionAttribute" /> method: a public instance method
        ///     taking at most one body and optionally a trailing cancellation token, returning a task
        ///     with or without a result.
        /// </summary>
        /// <param name="handler">The class the method is reached through: a stack's service, a companion or a routed service.</param>
        /// <param name="method">The method.</param>
        /// <param name="attribute">Its attribute.</param>
        /// <param name="securable">The securable the invoker evaluates; null for a member-only action.</param>
        /// <param name="problems">Receives the problems.</param>
        /// <returns>The descriptor, or null when the method does not conform.</returns>
        public static ApiActionDescriptor? Api(
            Type handler, MethodInfo method, ApiActionAttribute attribute, SecurableRef? securable, List<CompositionProblem> problems)
        {
            string described = Describe(handler, method);
            const string Shape = "public Task<TResult> M([TBody body][, CancellationToken cancellationToken]), returning Task, ValueTask, Task<TResult> or ValueTask<TResult>";

            // --- A public instance method, not generic of its own ---
            if (!method.IsPublic || method.IsStatic || method.IsGenericMethodDefinition)
            {
                Report(problems, $"The API action '{attribute.Name}' of {described} is not a public, non-generic instance method; an action is reached only through the platform's invoker.", $"Declare it as {Shape}.");
                return null;
            }

            // --- The parameters: at most one body, then optionally the cancellation token ---
            ParameterInfo[] parameters = method.GetParameters();
            int bodies = parameters.Length > 0 && parameters[^1].ParameterType == typeof(CancellationToken) ? parameters.Length - 1 : parameters.Length;
            if (bodies > 1 || parameters.Take(bodies).Any(static parameter => parameter.ParameterType == typeof(CancellationToken) || parameter.ParameterType.IsByRef))
            {
                Report(
                    problems,
                    $"The API action '{attribute.Name}' of {described} takes ({string.Join(", ", parameters.Select(static parameter => TypeDisplay.Short(parameter.ParameterType)))}); an API action takes at most one body, then optionally a cancellation token.",
                    "Gather the inputs into one request record, the body.");
                return null;
            }

            // --- The result ---
            if (!TryResult(method.ReturnType, out Type? result))
            {
                Report(problems, $"The API action '{attribute.Name}' of {described} returns {TypeDisplay.Short(method.ReturnType)}.", $"Declare it as {Shape}.");
                return null;
            }

            return new ApiActionDescriptor(
                attribute.Name,
                attribute.Description,
                attribute.Mutation,
                attribute.AllowWhenReadOnly,
                attribute.Idempotent,
                attribute.Long,
                attribute.Destructive,
                attribute.Mcp,
                attribute.ContractRevision,
                ResultType: result ?? typeof(void),
                Securable: securable,
                RequestType: bodies == 1 ? parameters[0].ParameterType : null,
                HandlerType: handler);
        }

        /// <summary>
        ///     What a method returning a task answers: nothing for <see cref="Task" /> and
        ///     <see cref="ValueTask" />, the result of <see cref="Task{TResult}" /> and
        ///     <see cref="ValueTask{TResult}" />.
        /// </summary>
        /// <param name="returnType">The method's return type.</param>
        /// <param name="result">The result type; null when the task answers none.</param>
        /// <returns>False when the method does not return one of the four.</returns>
        public static bool TryResult(Type returnType, out Type? result)
        {
            result = null;
            if (returnType == typeof(Task) || returnType == typeof(ValueTask))
            {
                return true;
            }

            if (returnType.IsGenericType && (returnType.GetGenericTypeDefinition() == typeof(Task<>) || returnType.GetGenericTypeDefinition() == typeof(ValueTask<>)))
            {
                result = returnType.GetGenericArguments()[0];
                return true;
            }

            return false;
        }

        /// <summary>
        ///     Whether an arguments type is a record the platform can read from JSON: the compiler's
        ///     record shape, and a contract the platform's serializer options can build.
        /// </summary>
        /// <param name="type">The type.</param>
        /// <param name="why">Why it is not, completing a sentence that names it; null when it is.</param>
        /// <returns>True when it is.</returns>
        private static bool IsSerialisableRecord(Type type, out string? why)
        {
            why = null;

            // The compiler gives every record class and record struct a PrintMembers(StringBuilder).
            bool record = type.GetMethod("PrintMembers", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public, [typeof(StringBuilder)]) is not null;
            if (!record || type.IsAbstract || type.ContainsGenericParameters)
            {
                why = "is not a concrete record.";
                return false;
            }

            try
            {
                // Building the contract resolves a converter for every property, which refuses a
                // property no JSON can carry.
                _ = TellmaJson.Options.GetTypeInfo(type);
                return true;
            }
            catch (Exception exception) when (exception is NotSupportedException or InvalidOperationException or ArgumentException)
            {
                why = $"the platform cannot read from JSON: {exception.Message}";
                return false;
            }
        }

        /// <summary>Adds an action problem.</summary>
        /// <param name="problems">Receives it.</param>
        /// <param name="problem">What is wrong.</param>
        /// <param name="fix">The remedy.</param>
        private static void Report(List<CompositionProblem> problems, string problem, string fix)
        {
            problems.Add(new CompositionProblem(CoreCheckNames.StackActions, problem, fix));
        }
    }
}
