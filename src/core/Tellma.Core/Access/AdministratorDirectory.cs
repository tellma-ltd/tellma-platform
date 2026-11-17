// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.ObjectModel;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Data;

namespace Tellma.Core.Access
{
    // The directory is spec 0014 §10.
    /// <summary>
    ///     The tenant's administrators, read in one guarded round trip on the scope's caller's behalf:
    ///     active individuals, invited or joined, with a current membership of the administrator role.
    ///     One per scope.
    /// </summary>
    /// <param name="runner">Runs the guarded read.</param>
    internal sealed class AdministratorDirectory(IGuardedBatchRunner runner) : IAdministratorDirectory
    {
        /// <summary>The most administrators the directory returns.</summary>
        public const int MaxAdministrators = 20;

        /// <inheritdoc />
        public Task<IReadOnlyList<int>> GetAdministratorIdsAsync(CancellationToken cancellationToken = default)
        {
            BatchResult<RawResult>? found = null;
            return runner.RunAsync<IReadOnlyList<int>>(
                BatchPurpose.Read,
                (_, batch) =>
                {
                    // The membership predicate is the literal form of AccessFilters.CurrentMembershipSql("M"):
                    // a text hole would bind as a parameter. The system user is of its own kind, never listed.
                    found = batch.Sql(
                        $"""
                        SELECT TOP ({MaxAdministrators}) U.[Id] FROM [core].[Users] AS U
                        WHERE U.[Kind] = 'Individual' AND U.[IsActive] = 1 AND U.[State] IN ('Invited', 'Joined')
                          AND EXISTS (SELECT 1 FROM [core].[RoleMemberships] AS M
                                      WHERE M.[UserId] = U.[Id] AND M.[RoleId] = {WellKnownIds.AdministratorRoleId}
                                        AND (([M].[StartsAt] IS NULL OR [M].[StartsAt] <= SYSUTCDATETIME()) AND ([M].[EndsAt] IS NULL OR [M].[EndsAt] > SYSUTCDATETIME())))
                        ORDER BY U.[Id];
                        """,
                        new SqlOptions(resultSets: 1));
                    return ValueTask.CompletedTask;
                },
                (_, _) => Ids(found!.Value.ResultSets[0]),
                cancellationToken);
        }

        /// <summary>The ids of a one-column result set of ids.</summary>
        /// <param name="set">The set.</param>
        /// <returns>The ids, in the set's order.</returns>
        private static ReadOnlyCollection<int> Ids(QueryRowSet set)
        {
            int[] ids = (int[])set.GetBuffer(0);
            return Array.AsReadOnly(ids[..set.RowCount]);
        }
    }
}
