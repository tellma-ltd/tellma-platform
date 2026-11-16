// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient;
using Tellma.Core.Abstractions.Hosting;
using Tellma.Testing.Support.Sql;

namespace Tellma.Core.Testing.Tests.Sql
{
    /// <summary>
    ///     The shared database creation without a server: the statement it runs, the guard on the
    ///     collation it splices in unquoted, and the command bound that must outlast the server's own
    ///     wait for the <c>model</c> lock.
    /// </summary>
    public sealed class SqlDatabaseCreationTests
    {
        /// <summary>A connection string to nowhere: every refusal here must come before a connection is opened.</summary>
        private const string Nowhere = "Data Source=tellma-no-such-server.invalid;Initial Catalog=master;Connect Timeout=1";

        /// <summary>How long SQL Server itself waits for the <c>model</c> lock before it raises 1807, as measured; the bound must exceed it by a wide margin.</summary>
        private static readonly TimeSpan ServerLockWait = TimeSpan.FromSeconds(30);

        /// <summary>The statement brackets the name, doubling a closing bracket, and states the collation.</summary>
        [Fact]
        public void The_statement_brackets_the_name_and_states_the_collation()
        {
            Assert.Equal(
                "CREATE DATABASE [fx_a]]b] COLLATE Latin1_General_100_CI_AS;",
                SqlDatabaseCreation.CreateStatement("fx_a]b", DatabaseCollations.Platform));
        }

        /// <summary>A collation holding anything but ASCII letters, digits and underscores is refused before any connection.</summary>
        /// <param name="collation">The collation.</param>
        [Theory]
        [InlineData("Latin1_General_100_CI_AS; DROP DATABASE master")]
        [InlineData("Latin1_General_100_CI_AS--")]
        [InlineData("Latin1 General")]
        [InlineData("Latin1_General_100_CI_AS\n")]
        [InlineData("Latin1_G\u00e9n\u00e9ral")]
        public async Task A_collation_that_is_not_a_plain_name_is_refused_before_any_connection(string collation)
        {
            ArgumentException refused = await Assert.ThrowsAsync<ArgumentException>(
                () => SqlDatabaseCreation.CreateAsync(Nowhere, "fx_guard", collation, CreateDatabaseRetry.Default, static _ => { }, TestContext.Current.CancellationToken));

            Assert.Equal("collation", refused.ParamName);
        }

        /// <summary>A blank name or collation is refused before any connection.</summary>
        /// <param name="database">The name.</param>
        /// <param name="collation">The collation.</param>
        [Theory]
        [InlineData("fx_blank", "")]
        [InlineData("fx_blank", "  ")]
        [InlineData("", DatabaseCollations.Platform)]
        [InlineData(" ", DatabaseCollations.Platform)]
        public async Task A_blank_name_or_collation_is_refused_before_any_connection(string database, string collation)
        {
            _ = await Assert.ThrowsAnyAsync<ArgumentException>(
                () => SqlDatabaseCreation.CreateAsync(Nowhere, database, collation, CreateDatabaseRetry.Default, static _ => { }, TestContext.Current.CancellationToken));
        }

        /// <summary>
        ///     Every command the helper runs, creation and preparation alike, carries the creation
        ///     bound, and the bound outlasts the server's own lock wait several times over.
        /// </summary>
        [Fact]
        public void Every_command_carries_a_bound_far_past_the_servers_lock_wait()
        {
            using SqlConnection connection = new(Nowhere);
            using SqlCommand command = SqlDatabaseCreation.CreateCommand(connection, "SELECT 1;");

            Assert.Equal("SELECT 1;", command.CommandText);
            Assert.Equal(300, command.CommandTimeout);
            Assert.True(SqlDatabaseCreation.CommandTimeout >= 5 * ServerLockWait, $"The bound {SqlDatabaseCreation.CommandTimeout} must outlast several of the server's lock waits.");
        }
    }
}
