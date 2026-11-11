// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using System.Data;
using Tellma.Core.Abstractions.Tenancy;

namespace Tellma.Core.Tenancy
{
    /// <summary>
    ///     The membership hints in the catalog: the subject's list in one query, the best-effort
    ///     upsert of the three writers, and the per-tenant reconciliation that also deletes the hints
    ///     a tenant no longer has. Rows reach the catalog through the membership table type, bound by
    ///     column name from the catalog model.
    /// </summary>
    /// <param name="connections">Opens the catalog connection.</param>
    /// <param name="catalogModel">The catalog model, whose membership table type the rows bind to.</param>
    /// <param name="metrics">The tenancy instruments.</param>
    /// <param name="logger">The logger.</param>
    internal sealed class TenantMembershipDirectory(
        ITenantConnectionFactory connections,
        CatalogModel catalogModel,
        TenancyMetrics metrics,
        ILogger<TenantMembershipDirectory> logger) : ITenantMembershipDirectory
    {
        /// <summary>The longest subject the catalog holds.</summary>
        public const int MaxSubjectLength = 255;

        /// <summary>The name of the table-valued parameter the write statements read.</summary>
        private const string RowsParameter = "@rows";

        /// <inheritdoc />
        public async Task<IReadOnlyList<TenantMembership>> ListAsync(string subject, CancellationToken cancellationToken = default)
        {
            ValidateSubject(subject, nameof(subject));

            SqlConnection connection = await connections.OpenCatalogAsync(cancellationToken).ConfigureAwait(false);
            List<TenantMembership> memberships = [];
            await using (connection.ConfigureAwait(false))
            {
                await using SqlCommand command = connection.CreateCommand();
                command.CommandText = CatalogStatements.Memberships;

                // Typed as the column is, so the comparison seeks the key and stays binary.
                _ = command.Parameters.Add(new SqlParameter("@subject", SqlDbType.VarChar, MaxSubjectLength) { Value = subject });

                await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    if (ReadMembership(reader) is TenantMembership membership)
                    {
                        memberships.Add(membership);
                    }
                }
            }

            memberships.Sort(static (left, right) => left.Tenant.Id.CompareTo(right.Tenant.Id));
            return memberships.AsReadOnly();
        }

        /// <inheritdoc />
        public Task RecordAsync(IReadOnlyList<TenantMembershipRecord> records, CancellationToken cancellationToken = default)
        {
            return RecordAsync(records, TenantMembershipWriteSource.PostCommit, cancellationToken);
        }

        /// <summary>
        ///     Records hints for a named writer: the upsert, best effort. A failure, a malformed record
        ///     included, is logged and metered and never thrown; a cancellation is.
        /// </summary>
        /// <param name="records">The hints; a later record for the same tenant and subject wins.</param>
        /// <param name="source">Which writer writes them.</param>
        /// <param name="cancellationToken">Cancels the write.</param>
        /// <returns>A task that completes when the write succeeded or its failure was logged.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="records" /> or one of its entries is null.</exception>
        public async Task RecordAsync(
            IReadOnlyList<TenantMembershipRecord> records, TenantMembershipWriteSource source, CancellationToken cancellationToken)
        {
            List<TenantMembershipRecord> rows = Deduplicate(records);
            if (rows.Count == 0)
            {
                return;
            }

            try
            {
                foreach (TenantMembershipRecord row in rows)
                {
                    ValidateSubject(row.Subject, nameof(records));
                }

                _ = await WriteAsync(CatalogStatements.UpsertMemberships, rows, tenantId: null, cancellationToken).ConfigureAwait(false);
                metrics.RecordMembershipWrite(succeeded: true, source);
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                // A hint is never worth failing the save that wrote the users it describes.
                metrics.RecordMembershipWrite(succeeded: false, source);
                TenancyLog.MembershipWriteFailed(logger, rows.Count, exception);
            }
        }

        /// <summary>
        ///     Rebuilds one tenant's hints from its users: the upsert of <paramref name="records" />, and
        ///     the delete of the tenant's hints they do not hold, in one catalog transaction. Unlike the
        ///     writers' upsert, a failure is thrown, so the reconciliation can log the tenant and go on.
        /// </summary>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="records">Every hint the tenant should have; each record's tenant must be <paramref name="tenantId" />.</param>
        /// <param name="cancellationToken">Cancels the write.</param>
        /// <returns>How many hint rows were inserted, updated or deleted.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="records" /> or one of its entries is null.</exception>
        /// <exception cref="ArgumentException">A record names another tenant, or a subject is malformed.</exception>
        public async Task<int> ReconcileAsync(int tenantId, IReadOnlyList<TenantMembershipRecord> records, CancellationToken cancellationToken)
        {
            List<TenantMembershipRecord> rows = Deduplicate(records);
            foreach (TenantMembershipRecord row in rows)
            {
                if (row.TenantId != tenantId)
                {
                    throw new ArgumentException($"Every record of a reconciliation of tenant {tenantId} must name that tenant; one names {row.TenantId}.", nameof(records));
                }

                ValidateSubject(row.Subject, nameof(records));
            }

            try
            {
                int changed = await WriteAsync(CatalogStatements.ReconcileMemberships, rows, tenantId, cancellationToken).ConfigureAwait(false);
                metrics.RecordMembershipWrite(succeeded: true, TenantMembershipWriteSource.Reconcile);
                return changed;
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                metrics.RecordMembershipWrite(succeeded: false, TenantMembershipWriteSource.Reconcile);
                throw;
            }
        }

        /// <summary>
        ///     Refuses a subject the catalog cannot hold and compare exactly: empty, longer than 255
        ///     characters, or holding anything but printable ASCII without spaces.
        /// </summary>
        /// <param name="subject">The subject.</param>
        /// <param name="parameterName">The parameter it came from.</param>
        /// <exception cref="ArgumentException">The subject is malformed.</exception>
        internal static void ValidateSubject(string subject, string parameterName)
        {
            // The column is varchar: a character outside ASCII would be stored as another one, and two
            // distinct subjects could then collide. Spaces are refused too, because SQL Server ignores
            // trailing spaces when it compares strings, binary collations included, so 'alice ' would
            // see alice's tenants.
            if (string.IsNullOrEmpty(subject) || subject.Length > MaxSubjectLength || !subject.All(static c => c is > ' ' and <= '~'))
            {
                throw new ArgumentException($"A subject is 1 to {MaxSubjectLength} printable ASCII characters without spaces.", parameterName);
            }
        }

        /// <summary>The records with null refused and duplicates collapsed, the later record winning.</summary>
        /// <param name="records">The records.</param>
        /// <returns>The distinct records, in first-seen order.</returns>
        private static List<TenantMembershipRecord> Deduplicate(IReadOnlyList<TenantMembershipRecord> records)
        {
            ArgumentNullException.ThrowIfNull(records);

            // The table type has no key, so two rows of one pair would make the insert collide.
            Dictionary<(int, string), int> positions = [];
            List<TenantMembershipRecord> distinct = [];
            foreach (TenantMembershipRecord record in records)
            {
                ArgumentNullException.ThrowIfNull(record, nameof(records));
                if (positions.TryGetValue((record.TenantId, record.Subject), out int position))
                {
                    distinct[position] = record;
                }
                else
                {
                    positions[(record.TenantId, record.Subject)] = distinct.Count;
                    distinct.Add(record);
                }
            }

            return distinct;
        }

        /// <summary>A membership row of the list statement, or null for a tenant this runtime cannot describe.</summary>
        /// <param name="reader">The reader, on the row.</param>
        /// <returns>The membership, or null.</returns>
        private static TenantMembership? ReadMembership(SqlDataReader reader)
        {
            // The list statement's columns: the descriptor's, then the hint's.
            TenantRow row = new(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.GetString(7),
                reader.IsDBNull(9) ? null : reader.GetInt32(9),
                reader.GetString(8),
                string.Empty,
                string.Empty,
                string.Empty,
                null,
                Guid.Empty);

            return row.ToDescriptor() is TenantDescriptor descriptor
                ? new TenantMembership(descriptor, reader.GetBoolean(10), reader.GetDateTimeOffset(11))
                : null;
        }

        /// <summary>Runs one membership write statement over the rows.</summary>
        /// <param name="statement">The statement.</param>
        /// <param name="rows">The rows, bound to <c>@rows</c>.</param>
        /// <param name="tenantId">The reconciled tenant, bound to <c>@tenantId</c>; null for the upsert.</param>
        /// <param name="cancellationToken">Cancels the write.</param>
        /// <returns>How many rows the statement inserted, updated or deleted.</returns>
        private async Task<int> WriteAsync(string statement, IReadOnlyList<TenantMembershipRecord> rows, int? tenantId, CancellationToken cancellationToken)
        {
            SqlConnection connection = await connections.OpenCatalogAsync(cancellationToken).ConfigureAwait(false);
            await using (connection.ConfigureAwait(false))
            {
                await using SqlCommand command = connection.CreateCommand();
                command.CommandText = statement;
                _ = command.Parameters.Add(catalogModel.Binder.Parameter(RowsParameter, rows));
                if (tenantId is int id)
                {
                    _ = command.Parameters.Add(new SqlParameter("@tenantId", SqlDbType.Int) { Value = id });
                }

                return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
