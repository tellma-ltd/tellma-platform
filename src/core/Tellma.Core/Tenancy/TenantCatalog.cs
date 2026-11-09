// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Data;
using System.Globalization;
using Tellma.Core.Abstractions.Hosting;
using Tellma.Core.Abstractions.Localization;
using Tellma.Core.Abstractions.Tenancy;

namespace Tellma.Core.Tenancy
{
    /// <summary>
    ///     The catalog's write side over the catalog connection: each write validates what it can
    ///     before touching the catalog, runs one transaction whose own precondition settles any race,
    ///     and then refreshes this instance's registry.
    /// </summary>
    /// <param name="connections">Opens the catalog connection.</param>
    /// <param name="catalog">The catalog connection's server, the default of a registration.</param>
    /// <param name="registry">The registry refreshed after every write.</param>
    /// <param name="sqlOptions">The SQL options: the profiles, the allowed servers and the database prefix.</param>
    /// <param name="environment">The composition's slug and environment.</param>
    /// <param name="deployment">The running deployment, the application name of the strings <see cref="ListAsync" /> composes.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="languages">The distribution's languages, whose first is a registration's first language; English when none is composed.</param>
    internal sealed class TenantCatalog(
        ITenantConnectionFactory connections,
        CatalogConnection catalog,
        ITenantRegistry registry,
        IOptionsMonitor<TellmaSqlOptions> sqlOptions,
        TenancyEnvironment environment,
        DeploymentIdentity deployment,
        ILogger<TenantCatalog> logger,
        ILanguageCatalog? languages = null) : ITenantCatalog
    {
        /// <summary>The first language of a registration when the composition offers none.</summary>
        public const string FallbackLanguage = "en";

        /// <summary>The longest name a row holds.</summary>
        public const int MaxNameLength = 255;

        /// <summary>The longest language code a row holds.</summary>
        public const int MaxLanguageLength = 35;

        /// <summary>The longest actor a row holds.</summary>
        public const int MaxActorLength = 128;

        /// <summary>The longest state reason a row holds.</summary>
        public const int MaxReasonLength = 1024;

        /// <summary>The highest id a registration may request; the sequence starts above it.</summary>
        public const int MaxRequestedId = CatalogDbContext.FirstSequenceId - 1;

        /// <inheritdoc />
        public async Task<TenantDescriptor> RegisterAsync(
            TenantRegistration registration, string actor, int? requestedId = null, CancellationToken cancellationToken = default)
        {
            // --- What the registration says, checked before the catalog is touched ---
            ArgumentNullException.ThrowIfNull(registration);
            string name = ValidName(registration.Name, nameof(registration));
            string by = ValidActor(actor);
            if (!Enum.IsDefined(registration.Category))
            {
                throw new ArgumentOutOfRangeException(nameof(registration), registration.Category, "Not a tenant category.");
            }

            if (requestedId is int id && id is < 1 or > MaxRequestedId)
            {
                throw Refused(TenantProblemCodes.RegistrationRefused, $"The registration of tenant {id} was refused: a requested id lies in 1-{MaxRequestedId}, below the sequence.");
            }

            if (registration.Category == TenantCategory.Live && registration.LiveTenantId is int liveId)
            {
                throw Refused(TenantProblemCodes.RegistrationRefused, $"The registration of '{name}' was refused: a live tenant cannot name a live tenant ({liveId}); only a sandbox can.");
            }

            // --- Where it lives: the given location, else the catalog's server under the default profile ---
            TellmaSqlOptions sql = sqlOptions.CurrentValue;
            string server = registration.Location?.Server ?? catalog.Server;
            string? database = registration.Location?.Database;
            string profile = registration.Location?.CredentialProfile ?? TellmaSqlOptions.DefaultProfile;
            if (database is not null && !TenantLocations.IsValidDatabaseName(database))
            {
                throw new ArgumentException("The database name must be 1 to 128 characters without ';', '=', ']' or a control character.", nameof(registration));
            }

            if (RefuseLocation(server, profile, sql) is string locationProblem)
            {
                throw Refused(TenantProblemCodes.RegistrationRefused, $"The registration of '{name}' was refused: {locationProblem}");
            }

            SqlConnection connection = await connections.OpenCatalogAsync(cancellationToken).ConfigureAwait(false);
            int registered;
            await using (connection.ConfigureAwait(false))
            {
                // --- A sandbox's live tenant must exist and be live ---
                if (registration.LiveTenantId is int live)
                {
                    TenantRow? liveRow = await ReadRowAsync(connection, live, cancellationToken).ConfigureAwait(false);
                    if (liveRow is null || liveRow.Category != nameof(TenantCategory.Live))
                    {
                        throw Refused(TenantProblemCodes.RegistrationRefused, $"The registration of '{name}' was refused: tenant {live} is not a live tenant a sandbox can derive from.");
                    }
                }

                // --- The insert, the id and the version bump, in one transaction ---
                await using SqlCommand command = connection.CreateCommand();
                command.CommandText = CatalogStatements.Register;
                command.Parameters.AddRange(
                [
                    Parameter("@requestedId", SqlDbType.Int, 0, requestedId),
                    Parameter("@requestedDatabase", SqlDbType.NVarChar, TenantLocations.MaxDatabaseLength, database),
                    Parameter("@databasePrefix", SqlDbType.NVarChar, TenantLocations.MaxDatabaseLength, sql.EffectiveDatabasePrefix(environment.Slug)),
                    Parameter("@name", SqlDbType.NVarChar, MaxNameLength, name),
                    Parameter("@language1", SqlDbType.VarChar, MaxLanguageLength, FirstLanguage()),
                    Parameter("@category", SqlDbType.NVarChar, 16, registration.Category.ToString()),
                    Parameter("@liveTenantId", SqlDbType.Int, 0, registration.LiveTenantId),
                    Parameter("@actor", SqlDbType.NVarChar, MaxActorLength, by),
                    Parameter("@server", SqlDbType.NVarChar, TenantLocations.MaxServerLength, server),
                    Parameter("@profile", SqlDbType.NVarChar, TenantLocations.MaxProfileLength, profile),
                ]);

                try
                {
                    registered = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);
                }
                catch (SqlException exception) when (exception.Number is CatalogStatements.PrimaryKeyViolation or CatalogStatements.UniqueIndexViolation or CatalogStatements.ConstraintViolation)
                {
                    // The id or the location is another tenant's, or the live tenant vanished meanwhile.
                    string what = exception.Number switch
                    {
                        CatalogStatements.PrimaryKeyViolation => $"the requested id {requestedId} is taken",
                        CatalogStatements.UniqueIndexViolation => "its location is another tenant's",
                        _ => "a catalog constraint refused it",
                    };

                    throw new TenantStateException(TenantProblemCodes.RegistrationRefused, $"The registration of '{name}' was refused: {what}.");
                }
            }

            TenancyLog.TenantRegistered(logger, registered, registration.Category, by);
            await RefreshAfterWriteAsync(registered, cancellationToken).ConfigureAwait(false);
            return new TenantDescriptor(registered, name, null, null, [FirstLanguage()], registration.Category, TenantState.Provisioning, registration.LiveTenantId);
        }

        /// <inheritdoc />
        public async Task<TenantDescriptor> SetStateAsync(
            int tenantId, TenantState state, string? reason, string actor, CancellationToken cancellationToken = default)
        {
            // --- The arguments ---
            if (!Enum.IsDefined(state))
            {
                throw new ArgumentOutOfRangeException(nameof(state), state, "Not a tenant state.");
            }

            string by = ValidActor(actor);
            if (reason is { Length: > MaxReasonLength })
            {
                throw new ArgumentException($"A state reason is at most {MaxReasonLength} characters.", nameof(reason));
            }

            SqlConnection connection = await connections.OpenCatalogAsync(cancellationToken).ConfigureAwait(false);
            TenantDescriptor current;
            await using (connection.ConfigureAwait(false))
            {
                // --- The transition, validated against the tenant's state as the catalog holds it ---
                current = (await ReadRowAsync(connection, tenantId, cancellationToken).ConfigureAwait(false))?.ToDescriptor()
                    ?? throw new TenantNotFoundException(tenantId);

                if (!TenantStateTransitions.IsLegal(current.State, state))
                {
                    throw Refused(TenantProblemCodes.IllegalTransition, $"Tenant {tenantId} cannot move from {current.State} to {state}.");
                }

                // --- The write, guarded by the state it was validated from ---
                await TransitionAsync(connection, tenantId, current.State, state, reason, by, cancellationToken).ConfigureAwait(false);
            }

            TenancyLog.StateChanged(logger, tenantId, current.State, state, by);
            await RefreshAfterWriteAsync(tenantId, cancellationToken).ConfigureAwait(false);
            return current with { State = state };
        }

        /// <inheritdoc />
        public async Task RenameAsync(int tenantId, TenantDisplayNames names, CancellationToken cancellationToken = default)
        {
            // --- Each name paired with the code at its position ---
            ArgumentNullException.ThrowIfNull(names);
            int count = names.Languages.Count;
            if (count > 3 || (names.Name2 is null) != (count < 2) || (names.Name3 is null) != (count < 3))
            {
                throw new ArgumentException("Each name must be paired with the language code at its position: one to three codes, and a name for each.", nameof(names));
            }

            string name = ValidName(names.Name, nameof(names));
            string? name2 = names.Name2 is null ? null : ValidName(names.Name2, nameof(names));
            string? name3 = names.Name3 is null ? null : ValidName(names.Name3, nameof(names));
            foreach (string language in names.Languages)
            {
                _ = ValidLanguage(language, nameof(names));
            }

            SqlConnection connection = await connections.OpenCatalogAsync(cancellationToken).ConfigureAwait(false);
            await using (connection.ConfigureAwait(false))
            {
                await using SqlCommand command = connection.CreateCommand();
                command.CommandText = CatalogStatements.Rename;
                command.Parameters.AddRange(
                [
                    Parameter("@tenantId", SqlDbType.Int, 0, tenantId),
                    Parameter("@name", SqlDbType.NVarChar, MaxNameLength, name),
                    Parameter("@name2", SqlDbType.NVarChar, MaxNameLength, name2),
                    Parameter("@name3", SqlDbType.NVarChar, MaxNameLength, name3),
                    Parameter("@language1", SqlDbType.VarChar, MaxLanguageLength, names.Languages[0]),
                    Parameter("@language2", SqlDbType.VarChar, MaxLanguageLength, count > 1 ? names.Languages[1] : null),
                    Parameter("@language3", SqlDbType.VarChar, MaxLanguageLength, count > 2 ? names.Languages[2] : null),
                ]);

                try
                {
                    _ = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (SqlException exception) when (exception.Number == CatalogStatements.NotFoundErrorNumber)
                {
                    throw new TenantNotFoundException(tenantId);
                }
            }

            await RefreshAfterWriteAsync(tenantId, cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task RelocateAsync(int tenantId, TenantLocation location, string actor, CancellationToken cancellationToken = default)
        {
            // --- The location, checked before the catalog is touched ---
            ArgumentNullException.ThrowIfNull(location);
            string by = ValidActor(actor);
            if (!TenantLocations.IsValidDatabaseName(location.Database))
            {
                throw new ArgumentException("The database name must be 1 to 128 characters without ';', '=', ']' or a control character.", nameof(location));
            }

            if (RefuseLocation(location.Server, location.CredentialProfile, sqlOptions.CurrentValue) is string locationProblem)
            {
                throw Refused(TenantProblemCodes.LocationRefused, $"The relocation of tenant {tenantId} was refused: {locationProblem}");
            }

            SqlConnection connection = await connections.OpenCatalogAsync(cancellationToken).ConfigureAwait(false);
            await using (connection.ConfigureAwait(false))
            {
                // --- Only a frozen or provisioning tenant moves; the statement re-checks inside its transaction ---
                TenantDescriptor current = (await ReadRowAsync(connection, tenantId, cancellationToken).ConfigureAwait(false))?.ToDescriptor()
                    ?? throw new TenantNotFoundException(tenantId);

                if (current.State is not (TenantState.Frozen or TenantState.Provisioning))
                {
                    throw Refused(TenantProblemCodes.LocationRefused, $"The relocation of tenant {tenantId} was refused: it is {current.State}, and only a frozen or provisioning tenant moves.");
                }

                await using SqlCommand command = connection.CreateCommand();
                command.CommandText = CatalogStatements.Relocate;
                command.Parameters.AddRange(
                [
                    Parameter("@tenantId", SqlDbType.Int, 0, tenantId),
                    Parameter("@server", SqlDbType.NVarChar, TenantLocations.MaxServerLength, location.Server),
                    Parameter("@database", SqlDbType.NVarChar, TenantLocations.MaxDatabaseLength, location.Database),
                    Parameter("@profile", SqlDbType.NVarChar, TenantLocations.MaxProfileLength, location.CredentialProfile),
                ]);

                try
                {
                    _ = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (SqlException exception) when (IsPrecondition(exception, TenantProblemCodes.LocationRefused))
                {
                    throw Refused(TenantProblemCodes.LocationRefused, $"The relocation of tenant {tenantId} was refused: its state moved away from frozen or provisioning first.");
                }
                catch (SqlException exception) when (exception.Number is CatalogStatements.UniqueIndexViolation or CatalogStatements.ConstraintViolation)
                {
                    throw Refused(TenantProblemCodes.LocationRefused, $"The relocation of tenant {tenantId} was refused: the location is another tenant's.");
                }
            }

            TenancyLog.Relocated(logger, tenantId, location.Server, location.Database, location.CredentialProfile, by);
            await RefreshAfterWriteAsync(tenantId, cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<TenantInfo>> ListAsync(CancellationToken cancellationToken = default)
        {
            SqlConnection connection = await connections.OpenCatalogAsync(cancellationToken).ConfigureAwait(false);
            List<TenantRow> rows = [];
            await using (connection.ConfigureAwait(false))
            {
                await using SqlCommand command = connection.CreateCommand();
                command.CommandText = CatalogStatements.Reload;
                await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

                // The version's result set comes first; the rows follow.
                _ = await reader.NextResultAsync(cancellationToken).ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    rows.Add(TenantRow.Read(reader));
                }
            }

            // Composed as the snapshot composes them; a row the snapshot refuses is listed with no
            // string, and one this runtime cannot even describe is left out.
            TenantRowComposer composer = new(sqlOptions.CurrentValue, environment.IsDevelopment, deployment.DeploymentId);
            List<TenantInfo> tenants = [];
            foreach (TenantRow row in rows.OrderBy(static row => row.Id))
            {
                if (composer.TryCompose(row, out TenantInfo tenant, out _))
                {
                    tenants.Add(tenant);
                }
                else if (row.ToDescriptor() is TenantDescriptor descriptor)
                {
                    tenants.Add(new TenantInfo(descriptor, new TenantLocation(row.Server, row.Database, row.CredentialProfile), string.Empty, row.Properties, row.Version));
                }
            }

            return tenants.AsReadOnly();
        }

        /// <summary>
        ///     Runs the transition statement from a given state. A tenant whose state is no longer
        ///     <paramref name="from" /> makes the statement throw, which is the refusal of a transition
        ///     lost to a concurrent one.
        /// </summary>
        /// <param name="connection">The open catalog connection.</param>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="from">The state the transition was validated from.</param>
        /// <param name="to">The state to move to.</param>
        /// <param name="reason">The reason, or null.</param>
        /// <param name="actor">Who moves it.</param>
        /// <param name="cancellationToken">Cancels the write.</param>
        /// <returns>A task that completes when the transition committed.</returns>
        /// <exception cref="TenantStateException"><c>tenant-illegal-transition</c>: the tenant is no longer in <paramref name="from" />.</exception>
        internal static async Task TransitionAsync(
            SqlConnection connection, int tenantId, TenantState from, TenantState to, string? reason, string actor, CancellationToken cancellationToken)
        {
            await using SqlCommand command = connection.CreateCommand();
            command.CommandText = CatalogStatements.Transition;
            command.Parameters.AddRange(
            [
                Parameter("@tenantId", SqlDbType.Int, 0, tenantId),
                Parameter("@from", SqlDbType.NVarChar, 16, from.ToString()),
                Parameter("@to", SqlDbType.NVarChar, 16, to.ToString()),
                Parameter("@reason", SqlDbType.NVarChar, MaxReasonLength, reason),
                Parameter("@actor", SqlDbType.NVarChar, MaxActorLength, actor),
            ]);

            try
            {
                _ = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (SqlException exception) when (IsPrecondition(exception, TenantProblemCodes.IllegalTransition))
            {
                throw new TenantStateException(
                    TenantProblemCodes.IllegalTransition,
                    $"Tenant {tenantId} cannot move from {from} to {to}: a concurrent transition changed its state first.");
            }
        }

        /// <summary>Reads one tenant's row, in the reload's shape.</summary>
        /// <param name="connection">The open catalog connection.</param>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="cancellationToken">Cancels the read.</param>
        /// <returns>The row, or null when there is none.</returns>
        private static async Task<TenantRow?> ReadRowAsync(SqlConnection connection, int tenantId, CancellationToken cancellationToken)
        {
            await using SqlCommand command = connection.CreateCommand();
            command.CommandText = CatalogStatements.SelectTenant;
            _ = command.Parameters.Add(Parameter("@tenantId", SqlDbType.Int, 0, tenantId));
            await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? TenantRow.Read(reader) : null;
        }

        /// <summary>Whether a SQL error is the catalog's refused state precondition carrying a given code.</summary>
        /// <param name="exception">The error.</param>
        /// <param name="code">The code its message must be.</param>
        /// <returns>True when it is.</returns>
        private static bool IsPrecondition(SqlException exception, string code)
        {
            return exception.Number == CatalogStatements.PreconditionErrorNumber && string.Equals(exception.Message, code, StringComparison.Ordinal);
        }

        /// <summary>A typed, sized parameter; null becomes <see cref="DBNull" />.</summary>
        /// <param name="name">The name, with its <c>@</c>.</param>
        /// <param name="type">The SQL type, as the column has it.</param>
        /// <param name="size">The column's length; 0 for a fixed-size type.</param>
        /// <param name="value">The value, or null.</param>
        /// <returns>The parameter.</returns>
        private static SqlParameter Parameter(string name, SqlDbType type, int size, object? value)
        {
            SqlParameter parameter = new(name, type) { Value = value ?? DBNull.Value };
            if (size > 0)
            {
                parameter.Size = size;
            }

            return parameter;
        }

        /// <summary>A refusal of a catalog write.</summary>
        /// <param name="code">The refusal's code.</param>
        /// <param name="detail">The sentence for logs.</param>
        /// <returns>The exception.</returns>
        private static TenantStateException Refused(string code, string detail)
        {
            return new TenantStateException(code, detail);
        }

        /// <summary>A name a row may hold: not blank, at most 255 characters.</summary>
        /// <param name="name">The name.</param>
        /// <param name="parameterName">The parameter it came from.</param>
        /// <returns>The name.</returns>
        private static string ValidName(string name, string parameterName)
        {
            return !string.IsNullOrWhiteSpace(name) && name.Length <= MaxNameLength
                ? name
                : throw new ArgumentException($"A tenant name is 1 to {MaxNameLength} characters and not blank.", parameterName);
        }

        /// <summary>A language code a row may hold: 1 to 35 ASCII letters, digits or hyphens.</summary>
        /// <param name="language">The code.</param>
        /// <param name="parameterName">The parameter it came from.</param>
        /// <returns>The code.</returns>
        private static string ValidLanguage(string language, string parameterName)
        {
            return !string.IsNullOrEmpty(language)
                && language.Length <= MaxLanguageLength
                && language.All(static c => char.IsAsciiLetterOrDigit(c) || c == '-')
                ? language
                : throw new ArgumentException($"A language code is 1 to {MaxLanguageLength} ASCII letters, digits or hyphens.", parameterName);
        }

        /// <summary>An actor a row may hold: not blank, at most 128 characters, no control character.</summary>
        /// <param name="actor">The actor.</param>
        /// <returns>The actor.</returns>
        private static string ValidActor(string actor)
        {
            return !string.IsNullOrWhiteSpace(actor) && actor.Length <= MaxActorLength && !actor.Any(char.IsControl)
                ? actor
                : throw new ArgumentException($"An actor is 1 to {MaxActorLength} characters, not blank and without control characters.", nameof(actor));
        }

        /// <summary>Why a server and profile cannot hold a tenant, or null when they can.</summary>
        /// <param name="server">The server.</param>
        /// <param name="profile">The profile's name.</param>
        /// <param name="sql">The SQL options in effect.</param>
        /// <returns>The problem, as the end of a sentence, or null.</returns>
        private string? RefuseLocation(string server, string profile, TellmaSqlOptions sql)
        {
            return !TenantLocations.IsServerAllowed(server, sql.AllowedServers, environment.IsDevelopment)
                ? $"the server '{server}' is malformed or outside Tellma:Sql:AllowedServers."
                : !TenantLocations.IsValidProfileName(profile) || !sql.Profiles.ContainsKey(profile)
                    ? $"the credential profile '{profile}' is not configured under Tellma:Sql:Profiles."
                    : null;
        }

        /// <summary>The distribution's default language: the first it offers, else English.</summary>
        /// <returns>The code.</returns>
        private string FirstLanguage()
        {
            return languages?.Offered is [LanguageInfo first, ..] ? first.Code : FallbackLanguage;
        }

        /// <summary>Refreshes this instance's registry after a committed write; a failure is logged, never thrown.</summary>
        /// <param name="tenantId">The tenant written.</param>
        /// <param name="cancellationToken">Stops waiting for the refresh.</param>
        /// <returns>A task that completes when the refresh did, or its failure was logged.</returns>
        private async Task RefreshAfterWriteAsync(int tenantId, CancellationToken cancellationToken)
        {
            try
            {
                await registry.RefreshAsync(force: true, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                // The write committed: reporting it as failed would invite a retry of a done write.
                TenancyLog.WriteRefreshFailed(logger, tenantId, exception);
            }
        }
    }
}
