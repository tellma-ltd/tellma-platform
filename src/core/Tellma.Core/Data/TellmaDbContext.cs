// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore;
using System.Reflection;
using Tellma.Core.Abstractions.Hosting;
using Tellma.Core.Data.Conventions;
using Tellma.Core.Jobs;

namespace Tellma.Core.Data
{
    /// <summary>
    ///     The platform's tenant EF context. Its model is built from the composition alone: every
    ///     contributed entity's leaf and the children its collections carry, the standalone table
    ///     types, and Core's model configurations closed over the leaves, shaped by the platform's
    ///     conventions. A model contains exactly one mapped type per table.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The context builds the model and serves the migrator, nothing else: there is no LINQ
    ///         surface, tables are read and written only through the platform's batches, and nothing
    ///         injects the context into distribution or pack code. A distribution names it only in its
    ///         migrator's design-time factory. Packs and distributions declare schema through entity
    ///         annotations and conventions alone and never write a model configuration.
    ///     </para>
    ///     <para>
    ///         The model is set to the platform collation, so a database EF creates for a development or
    ///         test run is created in it too.
    ///     </para>
    /// </remarks>
    public sealed class TellmaDbContext : DbContext
    {
        /// <summary>
        ///     <see cref="ModelBuilder.ApplyConfiguration{TEntity}(IEntityTypeConfiguration{TEntity})" />,
        ///     which applies a configuration whose entity type is known only at run time.
        /// </summary>
        private static readonly MethodInfo ApplyConfigurationMethod = typeof(ModelBuilder).GetMethods()
            .Single(static method => method.Name == nameof(ModelBuilder.ApplyConfiguration)
                && method.GetParameters() is [ParameterInfo parameter]
                && parameter.ParameterType.IsGenericType
                && parameter.ParameterType.GetGenericTypeDefinition() == typeof(IEntityTypeConfiguration<>));

        /// <summary>Creates a context over one composition's model definition.</summary>
        /// <param name="options">
        ///     The options, configured with
        ///     <see cref="TellmaSqlServerDbContextOptionsExtensions.UseTellmaSqlServer" />.
        /// </param>
        /// <param name="definition">What the composition puts in the model.</param>
        internal TellmaDbContext(DbContextOptions<TellmaDbContext> options, TellmaModelDefinition definition)
            : base(options)
        {
            ArgumentNullException.ThrowIfNull(definition);
            Definition = definition;
        }

        /// <summary>What the composition puts in the model; also the model cache's key.</summary>
        internal TellmaModelDefinition Definition { get; }

        /// <inheritdoc />
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            ArgumentNullException.ThrowIfNull(modelBuilder);
            Build(modelBuilder, Definition);
        }

        /// <summary>
        ///     Builds a model from a definition: the platform collation and named default constraints,
        ///     the entities, Core's configurations and the standalone table types. The conventions run
        ///     afterwards, when the model is finalized.
        /// </summary>
        /// <param name="modelBuilder">The model builder.</param>
        /// <param name="definition">What the composition puts in the model.</param>
        internal static void Build(ModelBuilder modelBuilder, TellmaModelDefinition definition)
        {
            // --- Model-wide settings ---
            _ = modelBuilder.UseCollation(DatabaseCollations.Platform);
            _ = modelBuilder.UseNamedDefaultConstraints();

            // --- The entities, leaves only ---
            foreach (Type entityType in definition.EntityTypes)
            {
                _ = modelBuilder.Entity(entityType);
            }

            // --- Core's model configurations, in feature order ---
            foreach (Type configuration in definition.Configurations)
            {
                Type entityType = TellmaModelDefinition.ConfiguredType(configuration)!;
                object instance = Activator.CreateInstance(configuration, nonPublic: true)!;
                _ = ApplyConfigurationMethod.MakeGenericMethod(entityType).Invoke(modelBuilder, [instance]);
            }

            // --- The standalone table types ---
            foreach (Type tableType in definition.TableTypes)
            {
                TableTypeConvention.AddStandalone(modelBuilder, tableType);
            }

            // --- The built-in schedules' seed rows, once the schedule's leaf and its state are mapped ---
            BuiltInScheduleSeeds.Apply(modelBuilder, definition.BuiltInSchedules);
        }
    }
}
