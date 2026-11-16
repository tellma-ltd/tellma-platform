// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient;
using System.ComponentModel.DataAnnotations;
using Tellma.Core.Migrator;

namespace Tellma.Core.Tests.Migrator
{
    /// <summary>
    ///     The migrator's own bounds: their defaults, their ranges, their conversion to SQL Server's
    ///     whole-second command timeouts, and the connection string that carries the migration bound
    ///     to code that creates its own commands.
    /// </summary>
    public sealed class MigratorOptionsTests
    {
        /// <summary>Unset, each statement class has its own bound and a creation has five attempts.</summary>
        [Fact]
        public void The_defaults_bound_each_statement_class_by_what_it_waits_for()
        {
            MigratorOptions options = new();

            Assert.Equal(TimeSpan.FromMinutes(5), options.CommandTimeout);
            Assert.Equal(TimeSpan.FromMinutes(30), options.DatabaseCommandTimeout);
            Assert.Equal(TimeSpan.FromHours(1), options.MigrationCommandTimeout);
            Assert.Equal(TimeSpan.FromSeconds(30), options.VersionTagBumpTimeout);
            Assert.Equal(5, options.CreateDatabaseAttempts);
            Assert.Equal(5, options.CreateDatabaseRetryPolicy().MaxAttempts);
            Assert.Empty(Validate(options));
        }

        /// <summary>A bound becomes whole seconds rounded up, so it never shrinks, and a bound outside the range is refused.</summary>
        [Fact]
        public void A_bound_becomes_whole_seconds_rounded_up()
        {
            Assert.Equal(1, MigratorOptions.Seconds(TimeSpan.FromSeconds(1)));
            Assert.Equal(2, MigratorOptions.Seconds(TimeSpan.FromMilliseconds(1200)));
            Assert.Equal(1800, MigratorOptions.Seconds(TimeSpan.FromMinutes(30)));
            Assert.Equal(86_400, MigratorOptions.Seconds(TimeSpan.FromDays(1)));

            // A zero would mean no bound at all to SQL Server, which the options never allow.
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => MigratorOptions.Seconds(TimeSpan.Zero));
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => MigratorOptions.Seconds(TimeSpan.FromDays(1) + TimeSpan.FromSeconds(1)));
        }

        /// <summary>Each bound out of its range is refused with a message naming its configuration key.</summary>
        /// <param name="property">The bound.</param>
        /// <param name="value">The value set, a time span.</param>
        [Theory]
        [InlineData(nameof(MigratorOptions.CommandTimeout), "00:00:00")]
        [InlineData(nameof(MigratorOptions.CommandTimeout), "00:00:00.5")]
        [InlineData(nameof(MigratorOptions.DatabaseCommandTimeout), "-00:01:00")]
        [InlineData(nameof(MigratorOptions.MigrationCommandTimeout), "1.00:00:01")]
        [InlineData(nameof(MigratorOptions.VersionTagBumpTimeout), "00:00:00")]
        public void A_bound_outside_its_range_is_refused_naming_its_key(string property, string value)
        {
            MigratorOptions options = new();
            typeof(MigratorOptions).GetProperty(property)!.SetValue(options, TimeSpan.Parse(value, System.Globalization.CultureInfo.InvariantCulture));

            ValidationResult result = Assert.Single(Validate(options));

            Assert.Equal([property], result.MemberNames);
            Assert.StartsWith($"Tellma:Migrator:{property} is ", result.ErrorMessage, StringComparison.Ordinal);
        }

        /// <summary>A creation runs at least once and at most twenty times.</summary>
        /// <param name="attempts">The attempts configured.</param>
        /// <param name="valid">Whether they are accepted.</param>
        [Theory]
        [InlineData(0, false)]
        [InlineData(-1, false)]
        [InlineData(1, true)]
        [InlineData(20, true)]
        [InlineData(21, false)]
        public void The_creation_runs_from_once_to_twenty_times(int attempts, bool valid)
        {
            MigratorOptions options = new() { CreateDatabaseAttempts = attempts };

            List<ValidationResult> results = Validate(options);

            Assert.Equal(valid, results.Count == 0);
            if (!valid)
            {
                Assert.Contains("Tellma:Migrator:CreateDatabaseAttempts", Assert.Single(results).ErrorMessage, StringComparison.Ordinal);
            }
        }

        /// <summary>
        ///     The migration connection string carries the migration bound, and a command created on
        ///     its connection inherits it, as a store that creates its own commands does; every other
        ///     setting is kept.
        /// </summary>
        [Fact]
        public void The_migration_connection_string_gives_every_command_on_it_the_migration_bound()
        {
            MigratorOptions options = new() { MigrationCommandTimeout = TimeSpan.FromMinutes(90) };

            string migration = options.MigrationConnectionString("Server=tcp:sql.example.com,1433;Database=tellma-acme-catalog;Encrypt=True;Max Pool Size=10");

            SqlConnectionStringBuilder builder = new(migration);
            Assert.Equal(5400, builder.CommandTimeout);
            Assert.Equal("tcp:sql.example.com,1433", builder.DataSource);
            Assert.Equal("tellma-acme-catalog", builder.InitialCatalog);
            Assert.Equal(10, builder.MaxPoolSize);

            // No connection is opened: a command takes its default bound from the connection's.
            using SqlConnection connection = new(migration);
            using SqlCommand command = connection.CreateCommand();
            Assert.Equal(5400, command.CommandTimeout);
        }

        /// <summary>Validates options as the startup gate does: data annotations and the object's own rules.</summary>
        /// <param name="options">The options.</param>
        /// <returns>The failures.</returns>
        private static List<ValidationResult> Validate(MigratorOptions options)
        {
            List<ValidationResult> results = [];
            _ = Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true);
            return results;
        }
    }
}
