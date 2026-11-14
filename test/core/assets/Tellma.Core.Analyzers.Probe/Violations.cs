// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.Data.SqlClient.Server;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Entities;
using Tellma.Core.Abstractions.Jobs;
using Tellma.Core.Data;

namespace Tellma.Core.Analyzers.Probe
{
    /// <summary>A stack's entity, for the pipeline rules.</summary>
    [Table("Probes", Schema = "probe")]
    public class ProbeEntity : TopLevelEntity;

    /// <summary>The probe entity's service, whose actions and hooks the rules guard.</summary>
    public class ProbeService : EntityService<ProbeEntity>
    {
        /// <summary>An action, reached only through the invoker; an action may run the front door.</summary>
        /// <returns>A task.</returns>
        [ApiAction("ping")]
        public Task PingAsync()
        {
            return SaveAsync([]);
        }

        /// <summary>TELLMA0005: a hook override running a nested pipeline.</summary>
        /// <param name="entities">The entities.</param>
        /// <returns>A task.</returns>
        protected override Task ValidateAsync(IReadOnlyList<ProbeEntity> entities)
        {
            return SaveAsync(entities);
        }
    }

    /// <summary>One violation of each of the other rules.</summary>
    public static class Violations
    {
        /// <summary>TELLMA0001: a record built outside a binder.</summary>
        /// <returns>The record.</returns>
        public static SqlDataRecord Record()
        {
            return new SqlDataRecord(new SqlMetaData("Id", System.Data.SqlDbType.Int));
        }

        /// <summary>TELLMA0002: a save over the tenant context.</summary>
        /// <param name="context">The context.</param>
        /// <returns>The count of rows saved.</returns>
        public static int Save(TellmaDbContext context)
        {
            return context.SaveChanges();
        }

        /// <summary>TELLMA0003: a MERGE.</summary>
        /// <param name="batch">The batch.</param>
        /// <returns>The result.</returns>
        public static BatchResult<RawResult> Merge(IDataBatch batch)
        {
            return batch.Sql($"MERGE INTO [probe].[Ledger] AS t USING (SELECT 1 AS [Id]) AS s ON t.[Id] = s.[Id] WHEN MATCHED THEN DELETE;", SqlOptions.None);
        }

        /// <summary>TELLMA0004: an action called from outside its declaring type.</summary>
        /// <param name="service">The service.</param>
        /// <returns>A task.</returns>
        public static Task Ping(ProbeService service)
        {
            return service.PingAsync();
        }

        /// <summary>TELLMA0006: a system table written by a type that owns no stack.</summary>
        /// <param name="batch">The batch.</param>
        /// <returns>The handle.</returns>
        public static SaveHandle Jobs(IDataBatch batch)
        {
            return batch.Save<Job>([]);
        }
    }
}
