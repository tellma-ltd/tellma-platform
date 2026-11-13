// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Data;
using Tellma.Core.Queryex;

namespace Tellma.Core.Data.Queries
{
    /// <summary>
    ///     What a batch's query statements compile and materialise with: the engine, the per-shape
    ///     schemas, the entity metadata and the materializer. One per process.
    /// </summary>
    /// <param name="engine">The query engine, shared by every compilation of the process.</param>
    /// <param name="schemas">The per-shape schemas and their maps.</param>
    /// <param name="metadata">The model's entity metadata.</param>
    /// <param name="materializer">The entity materializer.</param>
    internal sealed class QueryServices(QueryexEngine engine, QueryexSchemaProvider schemas, IEntityMetadataProvider metadata, EntityMaterializer materializer)
    {
        /// <summary>The query engine.</summary>
        public QueryexEngine Engine { get; } = engine ?? throw new ArgumentNullException(nameof(engine));

        /// <summary>The per-shape schemas and their maps.</summary>
        public QueryexSchemaProvider Schemas { get; } = schemas ?? throw new ArgumentNullException(nameof(schemas));

        /// <summary>The model's entity metadata.</summary>
        public IEntityMetadataProvider Metadata { get; } = metadata ?? throw new ArgumentNullException(nameof(metadata));

        /// <summary>The entity materializer.</summary>
        public EntityMaterializer Materializer { get; } = materializer ?? throw new ArgumentNullException(nameof(materializer));
    }
}
