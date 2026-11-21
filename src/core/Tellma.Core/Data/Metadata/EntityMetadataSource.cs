// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore.Metadata;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Composition.Checks;

namespace Tellma.Core.Data.Metadata
{
    /// <summary>
    ///     Builds the tenant model's entity metadata once, on first use, and keeps the outcome: the
    ///     provider when the model is sound, and every problem and warning found either way. The startup
    ///     gate's <c>core.entity-metadata</c> check reads the outcome, so the model is built and validated
    ///     before the host serves traffic, and the <see cref="IEntityMetadataProvider" /> the host resolves
    ///     is the same instance.
    /// </summary>
    /// <param name="model">Resolves the tenant model; it may throw when a declaration cannot be built.</param>
    /// <param name="definition">What the composition put in the model.</param>
    internal sealed class EntityMetadataSource(Func<IModel> model, TellmaModelDefinition definition)
    {
        private readonly Lazy<EntityMetadataBuild> _build = new(() => Create(model, definition), LazyThreadSafetyMode.ExecutionAndPublication);

        /// <summary>The outcome of building and validating the metadata.</summary>
        public EntityMetadataBuild Build => _build.Value;

        /// <summary>The validated metadata.</summary>
        /// <exception cref="InvalidOperationException">The model failed validation; the message lists every problem.</exception>
        public IEntityMetadataProvider Provider => Build.Provider ?? throw new InvalidOperationException(
            "The tenant model failed entity metadata validation, which the startup gate reports: "
            + string.Join(" ", Build.Problems.Select(static problem => problem.Describe())));

        /// <summary>Builds the model, then its metadata, reporting a model that cannot be built as one problem.</summary>
        /// <param name="model">Resolves the tenant model.</param>
        /// <param name="definition">What the composition put in the model.</param>
        /// <returns>The outcome.</returns>
        private static EntityMetadataBuild Create(Func<IModel> model, TellmaModelDefinition definition)
        {
            IModel built;
            try
            {
                built = model();
            }
            catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or NotSupportedException)
            {
                // The conventions refuse what no model can be built with by throwing, naming the class and
                // the declaration; the rest of the rules need a model, so this is the one problem left.
                return new EntityMetadataBuild(
                    null,
                    [new CompositionProblem(
                        CoreCheckNames.EntityMetadata,
                        $"The tenant model cannot be built: {exception.Message}",
                        "Correct the declaration the message names; the entity metadata is validated in full once the model builds.")],
                    []);
            }

            return EntityMetadataBuilder.Build(built, definition);
        }
    }

    /// <summary>The outcome of building the entity metadata.</summary>
    /// <param name="Provider">The provider; null when the model has a problem.</param>
    /// <param name="Problems">Every problem found, each failing startup.</param>
    /// <param name="Warnings">Every warning found, each logged at startup.</param>
    internal sealed record EntityMetadataBuild(
        EntityMetadataProvider? Provider,
        IReadOnlyList<CompositionProblem> Problems,
        IReadOnlyList<EntityMetadataWarning> Warnings);

    /// <summary>Something about an entity that does not fail startup but should be fixed: a referenced entity with no natural key.</summary>
    /// <param name="Entity">The entity's name.</param>
    /// <param name="Message">What is wrong and why it matters, as one sentence.</param>
    internal sealed record EntityMetadataWarning(string Entity, string Message);
}
