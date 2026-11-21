// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Logging;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Access;
using Tellma.Core.Data.Queries;

namespace Tellma.Core.Users
{
    // A distribution closes RoleService over its own leaf, so the service's constructor is public and
    // can name only public types; the platform's internal collaborators reach it through this one
    // public bundle, which only the platform can build, as the users' stack does.
    /// <summary>
    ///     What the roles' stack service needs from the platform beyond its pipeline: the permission
    ///     classifier its <c>problems</c> extra judges each permission with, the query schemas and
    ///     the entity metadata its <c>members</c> extra reads under, and the access logger. One per
    ///     process.
    /// </summary>
    /// <remarks>
    ///     The platform registers and builds it; a service that derives from the roles' stack service
    ///     receives it through its own constructor and passes it on unchanged.
    /// </remarks>
    public sealed class RoleServiceDependencies
    {
        /// <summary>Gathers the collaborators.</summary>
        /// <param name="classifier">The permission classifier, which the permission set build and the holders classify with too.</param>
        /// <param name="schemas">The query schemas, one per multilingual shape.</param>
        /// <param name="metadata">The entity metadata, whose membership and user leaves the members extra reads.</param>
        /// <param name="loggerFactory">Creates the access logger.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        internal RoleServiceDependencies(
            PermissionClassifier classifier,
            QueryexSchemaProvider schemas,
            IEntityMetadataProvider metadata,
            ILoggerFactory loggerFactory)
        {
            Classifier = classifier ?? throw new ArgumentNullException(nameof(classifier));
            Schemas = schemas ?? throw new ArgumentNullException(nameof(schemas));
            Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
            Logger = (loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory))).CreateLogger(AccessEvents.Category);
        }

        /// <summary>The permission classifier.</summary>
        internal PermissionClassifier Classifier { get; }

        /// <summary>The query schemas, one per multilingual shape.</summary>
        internal QueryexSchemaProvider Schemas { get; }

        /// <summary>The entity metadata.</summary>
        internal IEntityMetadataProvider Metadata { get; }

        /// <summary>The access logger, which names a caller's filter too complex to compile.</summary>
        internal ILogger Logger { get; }
    }
}
