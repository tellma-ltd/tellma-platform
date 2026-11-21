// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Data;

namespace Tellma.Core.Composition.Checks
{
    /// <summary>
    ///     <c>core.model-contributors</c>: every model configuration Core's features contribute is one
    ///     the tenant model can apply — a closed entity type configuration, or an open one generic over
    ///     exactly the entity it configures, whose type parameter's class constraint names the
    ///     replaceable default the model closes it over — with a parameterless constructor.
    /// </summary>
    /// <remarks>Reads the composition alone. A configuration it reports is left out of the model.</remarks>
    /// <param name="record">The realised composition.</param>
    internal sealed class ModelContributorsCheck(TellmaCompositionRecord record) : IStartupCheck
    {
        /// <inheritdoc />
        public string Name => CoreCheckNames.ModelContributors;

        /// <inheritdoc />
        public bool RequiresInfrastructure => false;

        /// <inheritdoc />
        public Task<IReadOnlyList<CompositionProblem>> CheckAsync(IServiceProvider services, CancellationToken cancellationToken)
        {
            List<CompositionProblem> problems = [];
            foreach (ContributedType configuration in record.ModelConfigurations)
            {
                if (Problem(configuration.Type) is string problem)
                {
                    problems.Add(new CompositionProblem(
                        Name,
                        $"The feature '{configuration.Owner.Name}' contributes the model configuration {TypeDisplay.Qualified(configuration.Type)}, which {problem}.",
                        "Contribute a class implementing IEntityTypeConfiguration<TEntity> with a parameterless constructor: closed over an entity no leaf replaces, or generic over one parameter constrained to the replaceable default (class UserConfiguration<TUser> : IEntityTypeConfiguration<TUser> where TUser : User)."));
                }
            }

            return Task.FromResult<IReadOnlyList<CompositionProblem>>(problems);
        }

        /// <summary>What is wrong with a configuration, or <see langword="null" /> when the model can apply it.</summary>
        /// <param name="configuration">The configuration class.</param>
        /// <returns>The problem, phrased to follow "which", or <see langword="null" />.</returns>
        private string? Problem(Type configuration)
        {
            if (!configuration.IsClass || configuration.IsAbstract)
            {
                return "is not a concrete class";
            }

            if (TellmaModelDefinition.ConfiguredType(configuration) is not Type configured)
            {
                return "does not implement IEntityTypeConfiguration<TEntity> exactly once";
            }

            if (configuration.ContainsGenericParameters)
            {
                if (!configuration.IsGenericTypeDefinition || configuration.GetGenericArguments().Length != 1)
                {
                    return "is open over more than one type parameter";
                }

                Type parameter = configuration.GetGenericArguments()[0];
                if (configured != parameter)
                {
                    return "is open over a type parameter other than the entity it configures";
                }

                if (TellmaModelDefinition.DefaultOf(parameter) is null)
                {
                    return "constrains its type parameter to no entity class, so no leaf can close it";
                }
            }

            return TellmaModelDefinition.Close(configuration, record.LeafOf) is null
                ? "cannot be created over the registered leaf: it needs a parameterless constructor, and the leaf must satisfy every constraint of its type parameter"
                : null;
        }
    }
}
