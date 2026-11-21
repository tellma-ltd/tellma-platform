// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Tellma.Core.Abstractions.Entities;

namespace Tellma.Core.Data.Conventions
{
    /// <summary>
    ///     Makes the <c>JobId</c> of every <see cref="IJobEntity" /> table a foreign key to
    ///     <c>core.Jobs</c>, <c>FK_&lt;Table&gt;_JobId</c>, set to null when the job is deleted, and unique
    ///     where it is not null, <c>UX_&lt;Table&gt;_JobId</c>: one job processes at most one row.
    /// </summary>
    internal sealed class JobEntityConvention : IModelFinalizingConvention
    {
        /// <inheritdoc />
        public void ProcessModelFinalizing(IConventionModelBuilder modelBuilder, IConventionContext<IConventionModelBuilder> context)
        {
            foreach (IConventionEntityType entityType in ConventionModel.Tables(modelBuilder.Metadata))
            {
                if (!typeof(IJobEntity).IsAssignableFrom(entityType.ClrType)
                    || entityType.FindProperty(nameof(IJobEntity.JobId)) is not IConventionProperty jobId)
                {
                    continue;
                }

                string declaration = $"The job column {entityType.DisplayName()}.{nameof(IJobEntity.JobId)}";
                IConventionEntityType jobs = ConventionModel.FindCoreTable(modelBuilder.Metadata, CoreTable.Jobs, declaration);
                _ = ConventionModel.AddForeignKey(entityType, jobId, jobs, DeleteBehavior.SetNull, unique: true, declaration);

                string column = ConventionModel.Column(jobId);
                _ = ConventionModel.AddIndex(
                    entityType, [jobId], ConstraintNames.UniqueIndex(ConventionModel.Table(entityType), [column]), unique: true, ConventionModel.NotNullFilter(column));
            }
        }
    }
}
