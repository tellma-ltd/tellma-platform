// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Tellma.Core.Abstractions.Composition;

namespace Tellma.Core.Crud.Checks
{
    /// <summary>What the stack checks share: resolving the stack registry once, and its problems for one check.</summary>
    internal static class StackCheckSupport
    {
        /// <summary>
        ///     Resolves the stack registry and builds it, reporting a registry the container cannot
        ///     build as one problem of the named check.
        /// </summary>
        /// <param name="services">The root service provider.</param>
        /// <param name="check">The name of the check asking.</param>
        /// <param name="problems">Receives the registry's problems sourced to the check, or the failure to build it.</param>
        /// <returns>The registry, or null when none is composed or it could not be built.</returns>
        public static StackRegistry? Registry(IServiceProvider services, string check, List<CompositionProblem> problems)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(problems);
            try
            {
                if (services.GetService<StackRegistry>() is not StackRegistry registry)
                {
                    return null;
                }

                // The build runs here, once, on the first check that asks.
                problems.AddRange(registry.Problems.Where(problem => string.Equals(problem.Source, check, StringComparison.Ordinal)));
                return registry;
            }
            catch (Exception exception) when (exception is not OperationCanceledException and not OutOfMemoryException)
            {
                problems.Add(new CompositionProblem(
                    check,
                    $"The stack registry could not be built: {exception.GetType().Name}: {exception.Message}",
                    "Fix what the message names; until the registry builds, no stack is served."));
                return null;
            }
        }
    }
}
