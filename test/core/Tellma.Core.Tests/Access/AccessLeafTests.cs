// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using System.ComponentModel.DataAnnotations;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Jobs;
using Tellma.Core.Abstractions.Notifications;
using Tellma.Core.Access;
using Tellma.Core.Composition;
using Tellma.Core.Jobs;
using Tellma.Core.Tests.Infrastructure;

namespace Tellma.Core.Tests.Access
{
    /// <summary>
    ///     A distribution that extends all four access entities: every access configuration closes over
    ///     its leaf, every foreign key to a replaced default reaches the leaf's table, and the seeds and
    ///     names stay the defaults'.
    /// </summary>
    public class AccessLeafTests : IClassFixture<AccessLeafTests.LeafModel>
    {
        private readonly AccessModel _model;

        /// <summary>Creates the tests over the leaf model.</summary>
        /// <param name="model">The leaf model.</param>
        public AccessLeafTests(LeafModel model)
        {
            ArgumentNullException.ThrowIfNull(model);
            _model = model.Model;
        }

        [Fact]
        public void Every_access_configuration_is_closed_over_the_registered_leaf()
        {
            // Core's other open configuration, the schedules', is closed over its own default.
            Assert.Equal(
                [typeof(UserConfiguration<AcmeUser>), typeof(RoleConfiguration<AcmeRole>), typeof(RoleMembershipConfiguration<AcmeMembership>), typeof(PermissionConfiguration<AcmeGrant>), typeof(ScheduleConfiguration<Schedule>)],
                _model.Definition.Configurations.Where(static configuration => configuration.IsGenericType));
            Assert.Equal([typeof(AcmeUser), typeof(AcmeMembership), typeof(AcmeRole), typeof(AcmeGrant), typeof(Notification), typeof(Job), typeof(Schedule)], _model.Definition.EntityTypes);
        }

        [Fact]
        public void The_leaves_keep_the_defaults_tables_names_and_seeds()
        {
            Assert.Null(_model.DesignModel.FindEntityType(typeof(User)));
            Assert.Equal(typeof(AcmeUser), _model.Table("Users").ClrType);
            Assert.Equal("core.User", _model.Metadata.Get(typeof(User)).Name);
            Assert.Equal("core.Permission", _model.Metadata.Get(typeof(Permission)).Name);
            Assert.Single(_model.Table("Users").GetSeedData());
            Assert.Single(_model.Table("RoleMemberships").GetSeedData());
            Assert.Contains(_model.Table("Users").GetCheckConstraints(), static check => check.Name == "CK_Users_KindEmail");
        }

        [Theory]
        [InlineData("UserStamps", "FK_UserStamps_UserId", typeof(AcmeUser))]
        [InlineData("UserPreferences", "FK_UserPreferences_UserId", typeof(AcmeUser))]
        [InlineData("Blobs", "FK_Blobs_CreatedById", typeof(AcmeUser))]
        [InlineData("RoleMemberships", "FK_RoleMemberships_UserId", typeof(AcmeUser))]
        [InlineData("RoleMemberships", "FK_RoleMemberships_RoleId", typeof(AcmeRole))]
        [InlineData("Permissions", "FK_Permissions_RoleId", typeof(AcmeRole))]
        public void Every_foreign_key_to_a_replaced_default_reaches_the_leaf(string table, string name, Type principal)
        {
            IForeignKey foreignKey = _model.Table(table).GetForeignKeys().Single(foreignKey => foreignKey.GetConstraintName() == name);

            Assert.Equal(principal, foreignKey.PrincipalEntityType.ClrType);
        }

        [Fact]
        public async Task The_extended_model_passes_the_startup_gate()
        {
            IReadOnlyList<CompositionProblem> problems = await TestComposition.CheckAsync(LeafModel.Compose);

            Assert.Empty(problems.From("core.entity-metadata"));
            Assert.Empty(problems.From("core.model-contributors"));
        }

        /// <summary>The model of a distribution that extends the four access entities.</summary>
        public sealed class LeafModel : IDisposable
        {
            /// <summary>Composes the distribution.</summary>
            public LeafModel()
            {
                Model = new AccessModel(Compose);
            }

            /// <summary>The model.</summary>
            public AccessModel Model { get; }

            /// <summary>The distribution's composition method.</summary>
            /// <param name="tellma">The builder.</param>
            public static void Compose(TellmaBuilder tellma)
            {
                ArgumentNullException.ThrowIfNull(tellma);
                _ = tellma
                    .UseEntity<User, AcmeUser>()
                    .UseEntity<Role, AcmeRole>()
                    .UseEntity<RoleMembership, AcmeMembership>()
                    .UseEntity<Permission, AcmeGrant>();
            }

            /// <inheritdoc />
            public void Dispose()
            {
                Model.Dispose();
            }
        }

        /// <summary>A distribution's user, with a column of its own.</summary>
        public sealed class AcmeUser : User
        {
            [MaxLength(50)]
            public string? EmployeeNumber { get; set; }
        }

        /// <summary>A distribution's role.</summary>
        public sealed class AcmeRole : Role
        {
            [MaxLength(50)]
            public string? Department { get; set; }
        }

        /// <summary>A distribution's role membership; its own column is nullable, since Core seeds the default's rows.</summary>
        public sealed class AcmeMembership : RoleMembership
        {
            public bool? IsDelegated { get; set; }
        }

        /// <summary>A distribution's permission.</summary>
        public sealed class AcmeGrant : Permission
        {
            [MaxLength(50)]
            public string? Ticket { get; set; }
        }
    }
}
