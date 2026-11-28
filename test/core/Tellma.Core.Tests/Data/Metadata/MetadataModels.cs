// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore.Metadata;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Hosting;
using Tellma.Core.Data;
using Tellma.Core.Data.Metadata;

namespace Tellma.Core.Tests.Data.Metadata
{
    /// <summary>Builds tenant models and their entity metadata straight from a definition, for the metadata suites.</summary>
    internal static class MetadataModels
    {
        /// <summary>The deployment every model is built for.</summary>
        public static DeploymentIdentity Deployment { get; } = new("acme", "Production");

        /// <summary>The platform tables every model with audit, job or blob columns needs.</summary>
        public static Type[] PlatformStandIns { get; } = [typeof(MdUser), typeof(MdJob), typeof(MdBlob)];

        /// <summary>The leaf substitutions of the sound model.</summary>
        public static Dictionary<Type, Type> SoundLeaves { get; } = new()
        {
            [typeof(MdUser)] = typeof(MyMdUser),
            [typeof(Region)] = typeof(MyRegion),
        };

        /// <summary>The definition of the sound model.</summary>
        /// <returns>The definition.</returns>
        public static TellmaModelDefinition SoundDefinition()
        {
            return TellmaModelDefinition.Create(
                [.. PlatformStandIns, typeof(Account), typeof(Region), typeof(Shipment), typeof(ShipmentExtra), typeof(Carrier)],
                [],
                [typeof(AccountConfiguration)],
                SoundLeaves);
        }

        /// <summary>The outcome of building the metadata of the platform stand-ins and the given entities.</summary>
        /// <param name="entities">The entities beside the stand-ins.</param>
        /// <returns>The outcome.</returns>
        public static EntityMetadataBuild Build(params Type[] entities)
        {
            return Build(TellmaModelDefinition.Create([.. PlatformStandIns, .. entities], [], [], new Dictionary<Type, Type>()));
        }

        /// <summary>The outcome of building the metadata of the platform stand-ins, the given entities and configurations.</summary>
        /// <param name="entities">The entities beside the stand-ins.</param>
        /// <param name="configurations">The model configurations.</param>
        /// <param name="leaves">The leaf substitutions; none when null.</param>
        /// <returns>The outcome.</returns>
        public static EntityMetadataBuild Build(Type[] entities, Type[] configurations, Dictionary<Type, Type>? leaves = null)
        {
            return Build(TellmaModelDefinition.Create([.. PlatformStandIns, .. entities], [], configurations, leaves ?? []));
        }

        /// <summary>The outcome of building the metadata of a definition's model.</summary>
        /// <param name="definition">The definition.</param>
        /// <returns>The outcome.</returns>
        public static EntityMetadataBuild Build(TellmaModelDefinition definition)
        {
            IModel model = new TellmaModelFactory(definition, Deployment).BuildModel();
            return EntityMetadataBuilder.Build(model, definition);
        }

        /// <summary>The one problem the metadata of the stand-ins and the given entities reports.</summary>
        /// <param name="entities">The entities beside the stand-ins.</param>
        /// <returns>The problem's text.</returns>
        public static string SingleProblem(params Type[] entities)
        {
            return SingleProblem(Build(entities));
        }

        /// <summary>The one problem an outcome reports, from the metadata check.</summary>
        /// <param name="build">The outcome.</param>
        /// <returns>The problem's text.</returns>
        public static string SingleProblem(EntityMetadataBuild build)
        {
            CompositionProblem problem = Assert.Single(build.Problems);
            Assert.Equal("core.entity-metadata", problem.Source);
            Assert.False(string.IsNullOrWhiteSpace(problem.Fix));
            return problem.Problem;
        }
    }
}
