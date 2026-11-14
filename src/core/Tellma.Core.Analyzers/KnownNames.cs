// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Analyzers
{
    /// <summary>
    ///     The namespaces, metadata names and member names the analyzers recognise. Everything is
    ///     matched by name, never by symbol identity, so a rule applies to the platform's contract
    ///     whichever assembly declares it and needs no reference to it.
    /// </summary>
    internal static class KnownNames
    {
        /// <summary>The assembly a condition exempts by name: the platform's runtime.</summary>
        public const string RuntimeAssembly = "Tellma.Core";

        /// <summary>The suffix of the assembly names TELLMA0002 exempts: the migrator projects.</summary>
        public const string MigratorSuffix = ".Migrator";

        /// <summary>The namespace of the data-access contract.</summary>
        public const string DataNamespace = "Tellma.Core.Abstractions.Data";

        /// <summary>The namespace of the entity contract.</summary>
        public const string EntitiesNamespace = "Tellma.Core.Abstractions.Entities";

        /// <summary>The namespace of the service-pipeline contract.</summary>
        public const string CrudNamespace = "Tellma.Core.Abstractions.Crud";

        /// <summary>The namespace of the tenant data model.</summary>
        public const string RuntimeDataNamespace = "Tellma.Core.Data";

        /// <summary>The namespace of EF Core.</summary>
        public const string EfNamespace = "Microsoft.EntityFrameworkCore";

        /// <summary>The namespace of EF Core's database facade.</summary>
        public const string EfInfrastructureNamespace = "Microsoft.EntityFrameworkCore.Infrastructure";

        /// <summary>The namespace of the BCL's mapping attributes.</summary>
        public const string SchemaNamespace = "System.ComponentModel.DataAnnotations.Schema";

        /// <summary>The namespace of SqlClient's record type.</summary>
        public const string SqlClientServerNamespace = "Microsoft.Data.SqlClient.Server";

        /// <summary>The namespace of the legacy SqlClient's record type.</summary>
        public const string LegacySqlServerNamespace = "Microsoft.SqlServer.Server";

        /// <summary>The batch interface.</summary>
        public const string DataBatch = "IDataBatch";

        /// <summary>The identifier a raw statement's hole emits as text.</summary>
        public const string SqlIdentifier = "SqlIdentifier";

        /// <summary>What a raw statement declares about itself.</summary>
        public const string SqlOptions = "SqlOptions";

        /// <summary>A schema-qualified table name.</summary>
        public const string TableName = "TableName";

        /// <summary>The handle a save returns.</summary>
        public const string SaveHandle = "SaveHandle";

        /// <summary>The handle a delete returns.</summary>
        public const string DeleteHandle = "DeleteHandle";

        /// <summary>One captured column of a save or a delete.</summary>
        public const string ColumnCapture = "ColumnCapture";

        /// <summary>The marker of the types that bind table-valued parameter rows.</summary>
        public const string TableTypeBinderAttribute = "TableTypeBinderAttribute";

        /// <summary>The keyed entity base.</summary>
        public const string Entity = "Entity`1";

        /// <summary>The top-level entity base: every stack's entity derives from it.</summary>
        public const string TopLevelEntity = "TopLevelEntity`1";

        /// <summary>The child entity base.</summary>
        public const string ChildEntity = "ChildEntity`1";

        /// <summary>The marker of a table whose writes bump a user-level version tag.</summary>
        public const string BumpsUserVersionTagAttribute = "BumpsUserVersionTagAttribute";

        /// <summary>The stack annotation.</summary>
        public const string StackAttribute = "StackAttribute";

        /// <summary>The marker of an API action method.</summary>
        public const string ApiActionAttribute = "ApiActionAttribute";

        /// <summary>The marker of an entity action method.</summary>
        public const string EntityActionAttribute = "EntityActionAttribute";

        /// <summary>The entity service base.</summary>
        public const string EntityService = "EntityService`2";

        /// <summary>The extension operations of the entity service.</summary>
        public const string EntityServiceExtensions = "EntityServiceExtensions";

        /// <summary>A validation component.</summary>
        public const string EntityValidator = "IEntityValidator`1";

        /// <summary>A persist-effect component.</summary>
        public const string PersistEffect = "IPersistEffect`1";

        /// <summary>The platform's tenant context.</summary>
        public const string TellmaDbContext = "TellmaDbContext";

        /// <summary>EF Core's context base.</summary>
        public const string DbContext = "DbContext";

        /// <summary>EF Core's entity set.</summary>
        public const string DbSet = "DbSet`1";

        /// <summary>EF Core's database facade.</summary>
        public const string DatabaseFacade = "DatabaseFacade";

        /// <summary>EF Core's raw-SQL extensions of the database facade.</summary>
        public const string RelationalDatabaseFacadeExtensions = "RelationalDatabaseFacadeExtensions";

        /// <summary>SqlClient's table-valued parameter record.</summary>
        public const string SqlDataRecord = "SqlDataRecord";

        /// <summary>The BCL's table-mapping attribute.</summary>
        public const string TableAttribute = "TableAttribute";

        /// <summary>The BCL's unmapped-property attribute, which marks a child collection.</summary>
        public const string NotMappedAttribute = "NotMappedAttribute";
    }
}
