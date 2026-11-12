// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Migrator;

namespace Tellma.Core.Tests.Migrator
{
    /// <summary>A design-time factory's connection string: <c>--connection</c>, else the variable, else LocalDB.</summary>
    public class DesignTimeConnectionsTests
    {
        private const string Fallback = "Server=(localdb)\\MSSQLLocalDB;Database=Design";

        [Fact]
        public void The_argument_wins_in_either_spelling_and_the_last_one_counts()
        {
            string variable = Unset();

            Assert.Equal("Server=a", DesignTimeConnections.Resolve(["--connection", "Server=a"], variable, Fallback));
            Assert.Equal("Server=b;Database=c", DesignTimeConnections.Resolve(["--connection=Server=b;Database=c"], variable, Fallback));
            Assert.Equal("Server=d", DesignTimeConnections.Resolve(["--connection", "Server=x", "--verbose", "--connection=Server=d"], variable, Fallback));
        }

        [Fact]
        public void The_variable_counts_when_no_argument_is_given()
        {
            string variable = Unset();
            Environment.SetEnvironmentVariable(variable, "Server=from-variable");
            try
            {
                Assert.Equal("Server=from-variable", DesignTimeConnections.Resolve(["--verbose"], variable, Fallback));
                Assert.Equal("Server=from-argument", DesignTimeConnections.Resolve(["--connection", "Server=from-argument"], variable, Fallback));
            }
            finally
            {
                Environment.SetEnvironmentVariable(variable, null);
            }
        }

        [Fact]
        public void LocalDB_is_the_fallback()
        {
            Assert.Equal(Fallback, DesignTimeConnections.Resolve(null, Unset(), Fallback));
            Assert.Equal(
                "Server=(localdb)\\MSSQLLocalDB;Database=TellmaTenantDesign;Trusted_Connection=True;TrustServerCertificate=True",
                DesignTimeConnections.LocalDb("TellmaTenantDesign"));
        }

        [Theory]
        [InlineData("--connection")]
        [InlineData("--connection=")]
        [InlineData("--connection", " ")]
        public void A_connection_option_without_a_value_is_refused(params string[] args)
        {
            _ = Assert.Throws<ArgumentException>(() => DesignTimeConnections.Resolve(args, Unset(), Fallback));
        }

        /// <summary>A variable name no other test sets, so the tests may run in parallel.</summary>
        /// <returns>The name.</returns>
        private static string Unset()
        {
            return "TELLMA_DESIGN_TEST_" + Guid.NewGuid().ToString("N", System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
