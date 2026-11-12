// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.ComponentModel.DataAnnotations;
using Tellma.Core.Tenancy;

namespace Tellma.Core.Tests.Tenancy
{
    /// <summary>The SQL options' own validation of the application principal the migrator adds to the application role.</summary>
    public class TellmaSqlOptionsTests
    {
        [Theory]
        [InlineData("acme-web")]
        [InlineData("acme web (managed identity)")]
        [InlineData("o'brien]x")]
        public void A_login_or_managed_identity_name_is_accepted(string principal)
        {
            Assert.Empty(Validate(new TellmaSqlOptions { ApplicationPrincipal = principal }));
        }

        [Theory]
        [InlineData("tellma_app")]
        [InlineData("TELLMA_APP")]
        public void The_application_role_itself_is_refused(string principal)
        {
            Assert.Contains("the application role's own name", Assert.Single(Validate(new TellmaSqlOptions { ApplicationPrincipal = principal })), StringComparison.Ordinal);
        }

        [Theory]
        [InlineData(" ")]
        [InlineData("acme\nweb")]
        public void A_blank_or_control_character_principal_is_refused(string principal)
        {
            Assert.Contains("ApplicationPrincipal must be 1 to 128 characters", Assert.Single(Validate(new TellmaSqlOptions { ApplicationPrincipal = principal })), StringComparison.Ordinal);
        }

        [Fact]
        public void A_principal_longer_than_a_database_user_name_is_refused()
        {
            Assert.Single(Validate(new TellmaSqlOptions { ApplicationPrincipal = new string('p', TellmaSqlOptions.MaxPrincipalLength + 1) }));
            Assert.Empty(Validate(new TellmaSqlOptions { ApplicationPrincipal = new string('p', TellmaSqlOptions.MaxPrincipalLength) }));
        }

        /// <summary>The options' validation messages.</summary>
        /// <param name="options">The options.</param>
        /// <returns>The messages.</returns>
        private static List<string> Validate(TellmaSqlOptions options)
        {
            return [.. options.Validate(new ValidationContext(options)).Select(static result => result.ErrorMessage!)];
        }
    }
}
