// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Tenancy
{
    /// <summary>
    ///     The catalog's statements: parameterised, and run as one batch each over the catalog
    ///     connection. Every write opens a transaction with <c>SET XACT_ABORT ON</c> and replaces
    ///     <c>catalog.CatalogState</c>'s version in it, except the membership hints, which no snapshot
    ///     holds.
    /// </summary>
    /// <remarks>
    ///     A refused precondition throws in the platform's error band: <c>50409</c> with the problem
    ///     code as its message for a state precondition, <c>50404</c> for a tenant that does not
    ///     exist.
    /// </remarks>
    internal static class CatalogStatements
    {
        // The shapes are spec 0011 §7.5's. Two departures: registration draws the sequence's id in a
        // statement of its own (NEXT VALUE FOR is not allowed inside COALESCE) and derives the default
        // database name in SQL, where the id is known; a rename of a missing tenant throws 50404
        // instead of committing a version bump that changed nothing.

        /// <summary>The error number of a refused state precondition; its message is the problem code.</summary>
        public const int PreconditionErrorNumber = 50409;

        /// <summary>The error number of a tenant the statement did not find.</summary>
        public const int NotFoundErrorNumber = 50404;

        /// <summary>SQL Server's primary-key violation.</summary>
        public const int PrimaryKeyViolation = 2627;

        /// <summary>SQL Server's unique-index violation.</summary>
        public const int UniqueIndexViolation = 2601;

        /// <summary>SQL Server's foreign-key or check-constraint violation.</summary>
        public const int ConstraintViolation = 547;

        /// <summary>SQL Server's "cannot open database" error: the database is missing, or the login has no access to it.</summary>
        public const int CannotOpenDatabase = 4060;

        /// <summary>
        ///     The client's "no process is on the other end of the pipe" error: the server ended the
        ///     session during the login, and the client never read why. Over named pipes and shared
        ///     memory, LocalDB's only transports, a login refused for a missing database sometimes
        ///     arrives as this error alone.
        /// </summary>
        public const int LoginInterrupted = 233;

        /// <summary>
        ///     Whether a database exists on the server, asked of <c>master</c>: 1 when the caller sees
        ///     it, 0 when the caller may see every database and this one is not among them, and NULL
        ///     when the caller may not see every database, so its absence proves nothing.
        /// </summary>
        public const string DatabaseExists =
            "SELECT CASE WHEN DB_ID(@database) IS NOT NULL THEN 1 WHEN HAS_PERMS_BY_NAME(NULL, NULL, N'VIEW ANY DATABASE') = 1 THEN 0 END;";

        /// <summary>The refresh probe, every refresh interval per instance.</summary>
        public const string Probe = "SELECT [Version] FROM [catalog].[CatalogState] WHERE [Id] = 1;";

        /// <summary>
        ///     The snapshot reload: the version first, then the rows, so a write landing in between
        ///     leaves the snapshot labelled older than its rows and the next probe reloads again,
        ///     never the reverse.
        /// </summary>
        public const string Reload = """
            SELECT [Version] FROM [catalog].[CatalogState] WHERE [Id] = 1;
            SELECT [Id], [Name], [Name2], [Name3], [Language1], [Language2], [Language3], [Category], [LiveTenantId], [State], [Server], [Database], [CredentialProfile], [Properties], [Version]
            FROM [catalog].[Tenants];
            """;

        /// <summary>One row, in the reload's column order, read before a write validates it.</summary>
        public const string SelectTenant = """
            SELECT [Id], [Name], [Name2], [Name3], [Language1], [Language2], [Language3], [Category], [LiveTenantId], [State], [Server], [Database], [CredentialProfile], [Properties], [Version]
            FROM [catalog].[Tenants] WHERE [Id] = @tenantId;
            """;

        /// <summary>
        ///     The catalog's migration state against the platform's latest migration: <c>-1</c> without
        ///     the history table, <c>1</c> when it lists <c>@latest</c>, else <c>0</c>. The history
        ///     table is read only when it exists, statement by statement.
        /// </summary>
        public const string History = """
            IF OBJECT_ID(N'[catalog].[__EFMigrationsHistory]', N'U') IS NULL
                SELECT -1;
            ELSE
                SELECT CASE WHEN EXISTS (SELECT 1 FROM [catalog].[__EFMigrationsHistory] WHERE [MigrationId] = @latest) THEN 1 ELSE 0 END;
            """;

        /// <summary>
        ///     Registration as <c>Provisioning</c>, with the requested id or the sequence's next, and
        ///     the default database name <c>&lt;prefix&gt;.&lt;id&gt;</c> when none is given. The id is
        ///     drawn in its own statement because <c>NEXT VALUE FOR</c> may not appear inside
        ///     <c>COALESCE</c>.
        /// </summary>
        public const string Register = """
            SET XACT_ABORT ON; BEGIN TRAN;
            DECLARE @id int = @requestedId;
            IF @id IS NULL SET @id = NEXT VALUE FOR [catalog].[sq_Tenants];
            DECLARE @database nvarchar(128) = COALESCE(@requestedDatabase, CONCAT(@databasePrefix, N'.', @id));
            INSERT INTO [catalog].[Tenants] ([Id], [Name], [Language1], [Category], [LiveTenantId], [State], [StateChangedAt], [StateChangedBy],
                [Server], [Database], [CredentialProfile], [Properties], [Version], [CreatedAt], [ModifiedAt])
            VALUES (@id, @name, @language1, @category, @liveTenantId, N'Provisioning', SYSUTCDATETIME(), @actor,
                @server, @database, @profile, NULL, NEWID(), SYSUTCDATETIME(), SYSUTCDATETIME());
            UPDATE [catalog].[CatalogState] SET [Version] = NEWID(), [ModifiedAt] = SYSUTCDATETIME() WHERE [Id] = 1;
            COMMIT;
            SELECT @id;
            """;

        /// <summary>A state transition, guarded by <c>@from</c>, the precondition of the validated transition.</summary>
        public const string Transition = """
            SET XACT_ABORT ON; BEGIN TRAN;
            UPDATE [catalog].[Tenants]
               SET [State] = @to, [StateReason] = @reason, [StateChangedAt] = SYSUTCDATETIME(), [StateChangedBy] = @actor,
                   [Version] = NEWID(), [ModifiedAt] = SYSUTCDATETIME()
             WHERE [Id] = @tenantId AND [State] = @from;
            IF @@ROWCOUNT <> 1 THROW 50409, N'tenant-illegal-transition', 1;
            UPDATE [catalog].[CatalogState] SET [Version] = NEWID(), [ModifiedAt] = SYSUTCDATETIME() WHERE [Id] = 1;
            COMMIT;
            """;

        /// <summary>A rename: the name group and language codes mirrored from the tenant's settings.</summary>
        public const string Rename = """
            SET XACT_ABORT ON; BEGIN TRAN;
            UPDATE [catalog].[Tenants]
               SET [Name] = @name, [Name2] = @name2, [Name3] = @name3,
                   [Language1] = @language1, [Language2] = @language2, [Language3] = @language3,
                   [Version] = NEWID(), [ModifiedAt] = SYSUTCDATETIME()
             WHERE [Id] = @tenantId;
            IF @@ROWCOUNT <> 1 THROW 50404, N'tenant-not-found', 1;
            UPDATE [catalog].[CatalogState] SET [Version] = NEWID(), [ModifiedAt] = SYSUTCDATETIME() WHERE [Id] = 1;
            COMMIT;
            """;

        /// <summary>A relocation, of a tenant that is frozen or provisioning only.</summary>
        public const string Relocate = """
            SET XACT_ABORT ON; BEGIN TRAN;
            UPDATE [catalog].[Tenants]
               SET [Server] = @server, [Database] = @database, [CredentialProfile] = @profile,
                   [Version] = NEWID(), [ModifiedAt] = SYSUTCDATETIME()
             WHERE [Id] = @tenantId AND [State] IN (N'Frozen', N'Provisioning');
            IF @@ROWCOUNT <> 1 THROW 50409, N'tenant-location-refused', 1;
            UPDATE [catalog].[CatalogState] SET [Version] = NEWID(), [ModifiedAt] = SYSUTCDATETIME() WHERE [Id] = 1;
            COMMIT;
            """;

        /// <summary>A subject's membership list, retired tenants excluded.</summary>
        public const string Memberships = """
            SELECT t.[Id], t.[Name], t.[Name2], t.[Name3], t.[Language1], t.[Language2], t.[Language3], t.[Category], t.[State], t.[LiveTenantId], m.[IsActive], m.[UpdatedAt]
              FROM [catalog].[TenantMemberships] AS m JOIN [catalog].[Tenants] AS t ON t.[Id] = m.[TenantId]
             WHERE m.[Subject] = @subject AND t.[State] <> N'Retired';
            """;

        /// <summary>
        ///     One tenant's membership hints in subject order, for the admin surface; seeks
        ///     <c>IX_TenantMemberships_TenantId</c>.
        /// </summary>
        public const string MembersOf = """
            SELECT m.[Subject], m.[IsActive], m.[UpdatedAt]
              FROM [catalog].[TenantMemberships] AS m
             WHERE m.[TenantId] = @tenantId
             ORDER BY m.[Subject];
            """;

        /// <summary>The membership upsert from <c>@rows</c>, without <c>MERGE</c>, in one transaction.</summary>
        public const string UpsertMemberships = """
            SET XACT_ABORT ON; BEGIN TRAN;
            UPDATE m SET m.[IsActive] = r.[IsActive], m.[UpdatedAt] = SYSUTCDATETIME()
              FROM [catalog].[TenantMemberships] AS m JOIN @rows AS r ON r.[TenantId] = m.[TenantId] AND r.[Subject] = m.[Subject]
             WHERE m.[IsActive] <> r.[IsActive];
            INSERT INTO [catalog].[TenantMemberships] ([Subject], [TenantId], [IsActive], [UpdatedAt])
            SELECT r.[Subject], r.[TenantId], r.[IsActive], SYSUTCDATETIME() FROM @rows AS r
             WHERE NOT EXISTS (SELECT 1 FROM [catalog].[TenantMemberships] AS m WHERE m.[Subject] = r.[Subject] AND m.[TenantId] = r.[TenantId]);
            COMMIT;
            """;

        /// <summary>
        ///     The reconciliation of one tenant's hints from <c>@rows</c>: the upsert, then the delete of
        ///     the tenant's hints <c>@rows</c> does not hold, in one transaction.
        /// </summary>
        public const string ReconcileMemberships = """
            SET XACT_ABORT ON; BEGIN TRAN;
            UPDATE m SET m.[IsActive] = r.[IsActive], m.[UpdatedAt] = SYSUTCDATETIME()
              FROM [catalog].[TenantMemberships] AS m JOIN @rows AS r ON r.[TenantId] = m.[TenantId] AND r.[Subject] = m.[Subject]
             WHERE m.[IsActive] <> r.[IsActive];
            INSERT INTO [catalog].[TenantMemberships] ([Subject], [TenantId], [IsActive], [UpdatedAt])
            SELECT r.[Subject], r.[TenantId], r.[IsActive], SYSUTCDATETIME() FROM @rows AS r
             WHERE NOT EXISTS (SELECT 1 FROM [catalog].[TenantMemberships] AS m WHERE m.[Subject] = r.[Subject] AND m.[TenantId] = r.[TenantId]);
            DELETE m FROM [catalog].[TenantMemberships] AS m
             WHERE m.[TenantId] = @tenantId AND NOT EXISTS (SELECT 1 FROM @rows AS r WHERE r.[Subject] = m.[Subject]);
            COMMIT;
            """;
    }
}
