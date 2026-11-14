// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient.Server;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using System.ComponentModel.DataAnnotations.Schema;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Entities;
using Tellma.Core.Analyzers.Sql;
using Tellma.Core.Data;

namespace Tellma.Core.Analyzers.Tests
{
    /// <summary>
    ///     The names the analyzers match by text, held to the real types and rules they stand for, so a
    ///     rename or a rule change on either side fails here rather than silently switching a rule off.
    /// </summary>
    public class NamePinTests
    {
        /// <summary>Every matched type, by the namespace and metadata name the analyzers use, and the real type.</summary>
        public static TheoryData<string, string, Type> MatchedTypes => new()
        {
            { KnownNames.DataNamespace, KnownNames.DataBatch, typeof(IDataBatch) },
            { KnownNames.DataNamespace, KnownNames.SqlIdentifier, typeof(SqlIdentifier) },
            { KnownNames.DataNamespace, KnownNames.SqlOptions, typeof(SqlOptions) },
            { KnownNames.DataNamespace, KnownNames.TableName, typeof(TableName) },
            { KnownNames.DataNamespace, KnownNames.SaveHandle, typeof(SaveHandle) },
            { KnownNames.DataNamespace, KnownNames.DeleteHandle, typeof(DeleteHandle) },
            { KnownNames.DataNamespace, KnownNames.ColumnCapture, typeof(ColumnCapture) },
            { KnownNames.EntitiesNamespace, KnownNames.TableTypeBinderAttribute, typeof(TableTypeBinderAttribute) },
            { KnownNames.EntitiesNamespace, KnownNames.Entity, typeof(Entity<>) },
            { KnownNames.EntitiesNamespace, KnownNames.TopLevelEntity, typeof(TopLevelEntity<>) },
            { KnownNames.EntitiesNamespace, KnownNames.ChildEntity, typeof(ChildEntity<>) },
            { KnownNames.EntitiesNamespace, KnownNames.BumpsUserVersionTagAttribute, typeof(BumpsUserVersionTagAttribute) },
            { KnownNames.EntitiesNamespace, KnownNames.TableTypeAttribute, typeof(TableTypeAttribute) },
            { KnownNames.CrudNamespace, KnownNames.StackAttribute, typeof(StackAttribute) },
            { KnownNames.CrudNamespace, KnownNames.ApiActionAttribute, typeof(ApiActionAttribute) },
            { KnownNames.CrudNamespace, KnownNames.EntityActionAttribute, typeof(EntityActionAttribute) },
            { KnownNames.RuntimeDataNamespace, KnownNames.TellmaDbContext, typeof(TellmaDbContext) },
            { KnownNames.EfNamespace, KnownNames.DbContext, typeof(DbContext) },
            { KnownNames.EfNamespace, KnownNames.DbSet, typeof(DbSet<>) },
            { KnownNames.EfInfrastructureNamespace, KnownNames.DatabaseFacade, typeof(DatabaseFacade) },
            { KnownNames.EfNamespace, KnownNames.RelationalDatabaseFacadeExtensions, typeof(RelationalDatabaseFacadeExtensions) },
            { KnownNames.SqlClientServerNamespace, KnownNames.SqlDataRecord, typeof(SqlDataRecord) },
            { KnownNames.SchemaNamespace, KnownNames.TableAttribute, typeof(TableAttribute) },
            { KnownNames.SchemaNamespace, KnownNames.NotMappedAttribute, typeof(NotMappedAttribute) },
        };

        /// <summary>
        ///     The service pipeline's contract types the suite and the analyzer probes declare as
        ///     stand-ins, by metadata name, until the contract ships them.
        /// </summary>
        public static TheoryData<string> StubbedPipelineTypes =>
        [
            KnownNames.EntityService, "EntityService`1", KnownNames.EntityServiceExtensions, KnownNames.EntityValidator, KnownNames.PersistEffect,
        ];

        /// <summary>Names a reserved-name rule must agree on, either way.</summary>
        public static TheoryData<string> Names =>
        [
            "@tm_", "@tm_CallerId", "@TM_x", "@tmp", "@tm", "@t",
            "@qx", "@qx_", "@qx1", "@qx1_", "@qx12_rate", "@QX3_y", "@qxRate", "@qx1a_",
            "@tb", "@tb_", "@tb1", "@tb1_", "@tb10_p0", "@TB2_keys", "@tbl", "@tb1x_",
            "@x", "tm_", "@__tellma_hole0",
        ];

        /// <summary>Each matched name is the real type's namespace and metadata name.</summary>
        /// <param name="ns">The namespace the analyzers match.</param>
        /// <param name="metadataName">The metadata name the analyzers match.</param>
        /// <param name="type">The real type.</param>
        [Theory]
        [MemberData(nameof(MatchedTypes))]
        public void A_matched_name_is_the_real_types(string ns, string metadataName, Type type)
        {
            Assert.Equal((type.Namespace, type.Name), (ns, metadataName));
        }

        /// <summary>
        ///     A stand-in fails here as soon as the real type ships: a stand-in would otherwise keep
        ///     shadowing the contract's type, with only a warning, and the rules would go on being
        ///     tested against shapes that are no longer the contract's.
        /// </summary>
        /// <param name="metadataName">The stubbed type's metadata name.</param>
        [Theory]
        [MemberData(nameof(StubbedPipelineTypes))]
        public void A_stubbed_pipeline_type_has_not_shipped(string metadataName)
        {
            string fullName = $"{KnownNames.CrudNamespace}.{metadataName}";
            bool shipped = typeof(SqlOptions).Assembly.GetType(fullName) is not null || typeof(TellmaDbContext).Assembly.GetType(fullName) is not null;
            Assert.False(shipped, $"The platform now ships {fullName}. Delete its stand-in from Infrastructure/PlatformStubs.cs and from the analyzer probe's PipelineStubs.cs, pin the name to the real type in MatchedTypes, and re-run the probe.");
        }

        /// <summary>The analyzers' reserved-name rule is the contract's.</summary>
        /// <param name="name">The name.</param>
        [Theory]
        [MemberData(nameof(Names))]
        public void The_reserved_name_rule_is_the_contracts(string name)
        {
            Assert.Equal(ReservedSqlNames.IsReserved(name), ReservedNames.IsReserved(name));
        }

        /// <summary>The runtime's name, which the rules exempt, is the real runtime assembly's name.</summary>
        [Fact]
        public void The_exempt_runtime_name_is_the_real_one()
        {
            Assert.Equal(KnownNames.RuntimeAssembly, typeof(TellmaDbContext).Assembly.GetName().Name);
        }
    }
}
