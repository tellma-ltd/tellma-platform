// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using System.Text;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Provisioning;
using Tellma.Core.Queryex;

namespace Tellma.Core.Access
{
    // The scan is spec 0014 §9: the drift table applied to every stored permission by the classifier
    // the permission sets are built with, so the scan reports exactly the rows that grant nothing.
    // Spec 0011 §6.1's migrator prints its lines after migrate, through the drift-report seam.
    /// <summary>
    ///     Reads every permission of the scope's tenant once and classifies each against the running
    ///     deployment's securables and the tenant's current schema, reporting the ones that grant
    ///     nothing. One per scope, in the platform's own scope of the tenant.
    /// </summary>
    /// <param name="database">The tenant's database, whose schema the filters validate against.</param>
    /// <param name="classifier">The permission classifier.</param>
    /// <param name="accessor">The scope's request context, which must be the platform's own.</param>
    internal sealed class PermissionDriftScanner(ITenantDatabase database, PermissionClassifier classifier, IRequestContextAccessor accessor)
        : IPermissionDriftScanner, ITenantDriftReport
    {
        /// <inheritdoc />
        public async Task<IReadOnlyList<PermissionDriftItem>> ScanAsync(CancellationToken cancellationToken = default)
        {
            if (!accessor.Current.IsSystem)
            {
                throw new InvalidOperationException("The permission drift scan runs in the platform's own scope of the tenant; this scope belongs to a user.");
            }

            // --- Every permission with its role's code and visibility, in one read ---
            IDataBatch read = database.CreateBatch(BatchPurpose.Read);
            BatchResult<RawResult> found = read.Sql(
                $"""
                SELECT P.[Id], P.[RoleId], R.[Code], R.[IsPublic], P.[Resource], P.[Action], P.[Filter], P.[FilterQueryexVersion]
                FROM [core].[Permissions] AS P JOIN [core].[Roles] AS R ON R.[Id] = P.[RoleId]
                ORDER BY P.[Id];
                """,
                new SqlOptions(resultSets: 1));
            _ = await read.ExecuteAsync(cancellationToken).ConfigureAwait(false);

            // --- Each row through the drift table; one validation per root, text and version ---
            QueryRowSet rows = found.Value.ResultSets[0];
            QueryexSchema schema = database.Schema;
            int[] ids = (int[])rows.GetBuffer(0);
            int[] roleIds = (int[])rows.GetBuffer(1);
            string?[] codes = (string?[])rows.GetBuffer(2);
            bool[] publics = (bool[])rows.GetBuffer(3);
            string?[] resources = (string?[])rows.GetBuffer(4);
            string?[] actions = (string?[])rows.GetBuffer(5);
            string?[] filters = (string?[])rows.GetBuffer(6);
            int[] versions = (int[])rows.GetBuffer(7);
            Dictionary<(string Root, string Text, int Version), PermissionClassifier.Validation> validations = [];
            List<PermissionDriftItem> drifted = [];
            for (int row = 0; row < rows.RowCount; row++)
            {
                int? version = rows.IsNull(7, row) ? null : versions[row];
                PermissionClassification classification = classifier.Classify(resources[row], actions[row], filters[row], version, publics[row], schema, validations);
                if (classification.Problem is AccessProblemCode problem)
                {
                    drifted.Add(new PermissionDriftItem(ids[row], roleIds[row], codes[row], problem, classification.Diagnostics));
                }
            }

            return drifted;
        }

        /// <inheritdoc />
        async Task<IReadOnlyList<string>> ITenantDriftReport.ScanAsync(CancellationToken cancellationToken)
        {
            IReadOnlyList<PermissionDriftItem> items = await ScanAsync(cancellationToken).ConfigureAwait(false);
            return [.. items.Select(Describe)];
        }

        /// <summary>One printable line for a drifted permission: where it is, why it grants nothing, and the engine's diagnostics.</summary>
        /// <param name="item">The drifted permission.</param>
        /// <returns>The line.</returns>
        internal static string Describe(PermissionDriftItem item)
        {
            ArgumentNullException.ThrowIfNull(item);
            StringBuilder line = new();
            _ = line.Append(CultureInfo.InvariantCulture, $"Permission {item.PermissionId} of role {item.RoleId}");
            if (item.RoleCode is string code)
            {
                _ = line.Append(CultureInfo.InvariantCulture, $" ({code})");
            }

            _ = line.Append(CultureInfo.InvariantCulture, $" grants nothing: {item.Code}");

            // Each diagnostic as its code and its arguments, which name the schema items it could not resolve.
            foreach (QueryexDiagnostic diagnostic in item.Diagnostics)
            {
                _ = line.Append(CultureInfo.InvariantCulture, $"; {diagnostic.Code}");
                foreach (KeyValuePair<string, string> argument in diagnostic.Arguments)
                {
                    _ = line.Append(CultureInfo.InvariantCulture, $" {argument.Key}={argument.Value}");
                }
            }

            return line.Append('.').ToString();
        }
    }
}
