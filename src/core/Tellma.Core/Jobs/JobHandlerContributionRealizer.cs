// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using System.Reflection;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Entities;
using Tellma.Core.Abstractions.Jobs;
using Tellma.Core.Composition;

namespace Tellma.Core.Jobs
{
    /// <summary>
    ///     Realises a <see cref="JobHandlerContributionItem" />: reads the class's
    ///     <see cref="JobHandlerAttribute" />, infers its shape from the handler interface it
    ///     implements, holds the key and the options to their rules, records the handler and registers
    ///     the class as a scoped service, one instance per invocation.
    /// </summary>
    /// <remarks>
    ///     What one handler cannot show on its own — a key registered twice, a key also retired — is
    ///     reported once every item is realised, by <see cref="JobContributionChecks" />; an entity the
    ///     model does not map, by the <c>jobs.handlers</c> startup check, once the model is built.
    /// </remarks>
    internal sealed class JobHandlerContributionRealizer : IContributionRealizer<JobHandlerContributionItem>
    {
        /// <inheritdoc />
        public void Realize(JobHandlerContributionItem item, ITellmaFeature owner, RealizationContext context)
        {
            ArgumentNullException.ThrowIfNull(item);
            ArgumentNullException.ThrowIfNull(owner);
            ArgumentNullException.ThrowIfNull(context);

            // --- A concrete, closed class ---
            Type? handlerType = item.HandlerType;
            if (handlerType is null || !handlerType.IsClass || handlerType.IsAbstract || handlerType.ContainsGenericParameters)
            {
                string described = handlerType is null ? "no handler class" : TypeDisplay.Qualified(handlerType);
                context.Report(new CompositionProblem(
                    CompositionNames.Source,
                    $"Feature '{owner.Name}' registers the job handler {described}, which is not a concrete, closed class.",
                    "Register a job handler as a concrete, non-generic class: contribution.JobHandler<THandler>()."));
                return;
            }

            string handler = TypeDisplay.Qualified(handlerType);

            // --- The attribute, which carries the key and the options ---
            if (handlerType.GetCustomAttribute<JobHandlerAttribute>(inherit: false) is not JobHandlerAttribute attribute)
            {
                context.Report(new CompositionProblem(
                    CompositionNames.Source,
                    $"Feature '{owner.Name}' registers the job handler {handler}, which carries no [JobHandler] attribute.",
                    "Declare the handler's key and options on the class: [JobHandler(\"acme.recompute-balances\")]."));
                return;
            }

            // --- The shape: exactly one of the two handler interfaces ---
            bool problems = false;
            IReadOnlyList<Type> shapes = JobHandlerShapes.Of(handlerType);
            Type? entityType = null;
            if (shapes.Count != 1)
            {
                string found = shapes.Count == 0 ? "neither" : string.Join(" and ", shapes.Select(TypeDisplay.Short));
                context.Report(new CompositionProblem(
                    CompositionNames.Source,
                    $"The job handler {handler} of the feature '{owner.Name}' implements {found}; a handler implements exactly one of IJobHandler and IEntityJobHandler<TEntity>.",
                    "Implement IJobHandler for a job that carries only arguments, or IEntityJobHandler<TEntity> for a job that processes the row whose JobId names it, not both."));
                problems = true;
            }
            else if (shapes[0].IsGenericType)
            {
                // The interface's constraint already holds TEntity to IJobEntity; the check keeps a
                // shape the constraint cannot see (a class that is not an entity) out of the registry.
                entityType = shapes[0].GetGenericArguments()[0];
                if (!entityType.IsClass || !typeof(IJobEntity).IsAssignableFrom(entityType))
                {
                    context.Report(new CompositionProblem(
                        CompositionNames.Source,
                        $"The job handler {handler} of the feature '{owner.Name}' processes {TypeDisplay.Qualified(entityType)}, which is not an entity implementing IJobEntity.",
                        "Process an entity class that implements IJobEntity, whose JobId column names the job."));
                    problems = true;
                }
            }

            // --- The key and the options, each against its rule ---
            foreach (string problem in attribute.FindProblems())
            {
                context.Report(new CompositionProblem(
                    CompositionNames.Source,
                    $"The job handler {handler} of the feature '{owner.Name}' is declared out of range: {problem}",
                    "Correct the [JobHandler] attribute's value."));
                problems = true;
            }

            // --- The platform's prefix belongs to the platform ---
            if (JobHandlerKeys.IsValid(attribute.Key) && JobHandlerKeys.IsPlatformKey(attribute.Key) && owner.Name != CoreFeature.FeatureName)
            {
                context.Report(new CompositionProblem(
                    CompositionNames.Source,
                    $"The job handler {handler} of the feature '{owner.Name}' has the key '{attribute.Key}', but the prefix '{JobHandlerKeys.PlatformPrefix}' is reserved for the platform.",
                    "Prefix the key with the module's or the distribution's slug instead, such as 'acme.recompute-balances'."));
                problems = true;
            }

            if (problems)
            {
                return;
            }

            // --- Recorded for the registry and the checks; resolved per invocation from the job's scope ---
            context.Record.JobHandlers.Add(new ContributedJobHandler(handlerType, attribute, entityType, owner));
            context.Services.AddScoped(handlerType);
        }
    }

    /// <summary>The handler interfaces a class implements.</summary>
    internal static class JobHandlerShapes
    {
        /// <summary>
        ///     The handler interfaces the class implements: <see cref="IJobHandler" />, and each closed
        ///     <see cref="IEntityJobHandler{TEntity}" />. A sound handler implements exactly one.
        /// </summary>
        /// <param name="handlerType">The class.</param>
        /// <returns>The interfaces, the arguments-only one first.</returns>
        public static IReadOnlyList<Type> Of(Type handlerType)
        {
            ArgumentNullException.ThrowIfNull(handlerType);

            return [.. handlerType.GetInterfaces()
                .Where(static candidate => candidate == typeof(IJobHandler)
                    || (candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(IEntityJobHandler<>)))
                .OrderBy(static candidate => candidate.IsGenericType)
                .ThenBy(static candidate => candidate.FullName, StringComparer.Ordinal)];
        }
    }
}
