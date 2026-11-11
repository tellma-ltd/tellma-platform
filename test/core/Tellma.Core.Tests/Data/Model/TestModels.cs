// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Tellma.Core.Abstractions.Hosting;
using Tellma.Core.Data;

namespace Tellma.Core.Tests.Data.Model
{
    /// <summary>Builds small one-off tenant models straight from a definition, for the cases a composition is not about.</summary>
    internal static class TestModels
    {
        /// <summary>The deployment every one-off model is built for.</summary>
        public static DeploymentIdentity Deployment { get; } = new("acme", "Production");

        /// <summary>A definition over entities and standalone types, with no leaf substitution.</summary>
        /// <param name="entities">The contributed entities.</param>
        /// <param name="tableTypes">The standalone table types.</param>
        /// <returns>The definition.</returns>
        public static TellmaModelDefinition Definition(IEnumerable<Type> entities, IEnumerable<Type>? tableTypes = null)
        {
            return TellmaModelDefinition.Create(entities, tableTypes ?? [], [], new Dictionary<Type, Type>());
        }

        /// <summary>The design-time model of a definition.</summary>
        /// <param name="definition">The definition.</param>
        /// <returns>The model.</returns>
        public static IModel DesignModel(TellmaModelDefinition definition)
        {
            using TellmaDbContext context = new TellmaModelFactory(definition, Deployment).CreateContext(DataModel.UnusedConnectionString);
            return context.GetService<IDesignTimeModel>().Model;
        }

        /// <summary>The design-time model of a set of entities, with the platform tables the audit columns reference.</summary>
        /// <param name="entities">The entities beside the platform stand-ins.</param>
        /// <returns>The model.</returns>
        public static IModel DesignModel(params Type[] entities)
        {
            return DesignModel(Definition([typeof(TestUser), typeof(TestJob), typeof(TestBlob), .. entities]));
        }
    }
}
