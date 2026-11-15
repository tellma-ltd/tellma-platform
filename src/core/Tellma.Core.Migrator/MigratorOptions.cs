// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Tellma.Core.Migrator.Sql;

namespace Tellma.Core.Migrator
{
    /// <summary>
    ///     The migrator's own bounds, bound from <c>Tellma:Migrator</c> and checked by the startup gate:
    ///     how long each class of statement the migrator runs may take, how many times a database
    ///     creation that found SQL Server's <c>model</c> database locked is run, and how long the
    ///     version-tag move on the way out of a failed tenant run may take.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Statements fall into three classes, each with its own bound, because what they wait for
    ///         differs by orders of magnitude: a database's creation or option change waits for the
    ///         server (Azure SQL Database allocates a database before it answers, and SQL Server first
    ///         waits for the <c>model</c> lock), a migration may rebuild a large table, and everything
    ///         else reads metadata or grants a role.
    ///     </para>
    ///     <para>
    ///         A statement that outlives its bound fails its database's command and is never run
    ///         again in the same run: the client gives up on a statement the server may still finish,
    ///         so the next run, which converges, decides what is left to do.
    ///     </para>
    /// </remarks>
    internal sealed class MigratorOptions : IValidatableObject
    {
        /// <summary>The configuration section the options bind from.</summary>
        public const string SectionPath = "Tellma:Migrator";

        /// <summary>The most times a database creation may be run, the first run included.</summary>
        public const int MaxCreateDatabaseAttempts = 20;

        /// <summary>The bound of a statement outside the other two classes when the configuration does not say: five minutes.</summary>
        public static readonly TimeSpan DefaultCommandTimeout = TimeSpan.FromMinutes(5);

        /// <summary>The bound of a database's creation or option change when the configuration does not say: thirty minutes.</summary>
        public static readonly TimeSpan DefaultDatabaseCommandTimeout = TimeSpan.FromMinutes(30);

        /// <summary>The bound of a migration's statement when the configuration does not say: an hour.</summary>
        public static readonly TimeSpan DefaultMigrationCommandTimeout = TimeSpan.FromHours(1);

        /// <summary>How long the version-tag move on the way out of a failed tenant run may take when the configuration does not say.</summary>
        public static readonly TimeSpan DefaultVersionTagBumpTimeout = TimeSpan.FromSeconds(30);

        /// <summary>The shortest bound accepted: one second, the unit SQL Server's command timeout counts in.</summary>
        public static readonly TimeSpan MinTimeout = TimeSpan.FromSeconds(1);

        /// <summary>The longest bound accepted: a day; a longer one is a unit mistake rather than a bound.</summary>
        public static readonly TimeSpan MaxTimeout = TimeSpan.FromDays(1);

        /// <summary>
        ///     How long each statement may run that is neither a database's creation or option change
        ///     nor a migration's: the reads of the server and the database, the role, the application
        ///     principal, the grants and the schema fingerprint. Five minutes by default.
        /// </summary>
        public TimeSpan CommandTimeout { get; set; } = DefaultCommandTimeout;

        /// <summary>
        ///     How long a <c>CREATE DATABASE</c>, and a fresh database's <c>ALTER DATABASE</c> statements
        ///     (read-committed snapshot, the query store), may run. Thirty minutes by default.
        /// </summary>
        public TimeSpan DatabaseCommandTimeout { get; set; } = DefaultDatabaseCommandTimeout;

        /// <summary>
        ///     How long each statement of a migration may run: the catalog's, the in-proc identity
        ///     store's and each tenant's. An hour by default.
        /// </summary>
        public TimeSpan MigrationCommandTimeout { get; set; } = DefaultMigrationCommandTimeout;

        /// <summary>
        ///     How many times a <c>CREATE DATABASE</c> that failed only because the <c>model</c> database
        ///     was locked is run, the first run included: from 1, which never runs it again, to
        ///     <see cref="MaxCreateDatabaseAttempts" />. Five by default.
        /// </summary>
        public int CreateDatabaseAttempts { get; set; } = CreateDatabaseRetry.DefaultMaxAttempts;

        /// <summary>
        ///     How long the version-tag move on the way out of a failed tenant run may take; the move is
        ///     not stopped by the run's cancellation. Thirty seconds by default.
        /// </summary>
        public TimeSpan VersionTagBumpTimeout { get; set; } = DefaultVersionTagBumpTimeout;

        /// <summary>The database creations' retry policy these options describe.</summary>
        /// <remarks>
        ///     A method rather than a property: the options' validation reads every property, and a
        ///     property that threw for an out-of-range value would hide the message naming its key.
        /// </remarks>
        /// <returns>The policy.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><see cref="CreateDatabaseAttempts" /> is below 1.</exception>
        public CreateDatabaseRetry CreateDatabaseRetryPolicy()
        {
            return new CreateDatabaseRetry(CreateDatabaseAttempts);
        }

        /// <summary>A bound in SQL Server's command timeout unit, whole seconds, rounded up so a bound never shrinks.</summary>
        /// <param name="timeout">The bound, from <see cref="MinTimeout" /> to <see cref="MaxTimeout" />.</param>
        /// <returns>The seconds.</returns>
        /// <exception cref="ArgumentOutOfRangeException">The bound is outside its range.</exception>
        public static int Seconds(TimeSpan timeout)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(timeout, MinTimeout);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(timeout, MaxTimeout);
            return (int)Math.Ceiling(timeout.TotalSeconds);
        }

        /// <summary>
        ///     A connection string whose commands default to <see cref="MigrationCommandTimeout" />, for a
        ///     migration the migrator hands to code that creates its own commands, such as the in-proc
        ///     identity store's.
        /// </summary>
        /// <param name="connectionString">The database's connection string.</param>
        /// <returns>The same connection string with its <c>Command Timeout</c> set.</returns>
        public string MigrationConnectionString(string connectionString)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
            return new SqlConnectionStringBuilder(connectionString) { CommandTimeout = Seconds(MigrationCommandTimeout) }.ConnectionString;
        }

        /// <inheritdoc />
        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            // --- Every bound is positive and below a day ---
            foreach ((string name, TimeSpan value) in new[]
            {
                (nameof(CommandTimeout), CommandTimeout),
                (nameof(DatabaseCommandTimeout), DatabaseCommandTimeout),
                (nameof(MigrationCommandTimeout), MigrationCommandTimeout),
                (nameof(VersionTagBumpTimeout), VersionTagBumpTimeout),
            })
            {
                if (value < MinTimeout || value > MaxTimeout)
                {
                    yield return new ValidationResult(
                        string.Create(CultureInfo.InvariantCulture, $"{SectionPath}:{name} is {value:c}; it must be a time span from {MinTimeout:c} to {MaxTimeout:c}."),
                        [name]);
                }
            }

            // --- The creation runs at least once, and a bounded number of times ---
            if (CreateDatabaseAttempts is < 1 or > MaxCreateDatabaseAttempts)
            {
                yield return new ValidationResult(
                    string.Create(CultureInfo.InvariantCulture, $"{SectionPath}:{nameof(CreateDatabaseAttempts)} is {CreateDatabaseAttempts}; it must be from 1, which never runs a creation again, to {MaxCreateDatabaseAttempts}."),
                    [nameof(CreateDatabaseAttempts)]);
            }
        }
    }
}
