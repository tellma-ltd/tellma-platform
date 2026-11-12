// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Blobs;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Entities;
using Tellma.Core.Abstractions.Localization;
using Tellma.Core.Access;
using Tellma.Core.Data;

namespace Tellma.Core.Tests.Access
{
    /// <summary>
    ///     The access tables as every composition maps them: the entity metadata of users, roles,
    ///     memberships and permissions, the table shapes beyond the conventions, and the seeded rows.
    /// </summary>
    public class AccessModelTests(AccessModel model) : IClassFixture<AccessModel>
    {
        /// <summary>The two tag columns of a stamp row.</summary>
        private static readonly string[] StampTags = [nameof(UserStamp.PermissionsTag), nameof(UserStamp.PreferencesTag)];

        // --- Entity metadata ---

        [Theory]
        [InlineData(typeof(User), "core.User")]
        [InlineData(typeof(Role), "core.Role")]
        [InlineData(typeof(RoleMembership), "core.RoleMembership")]
        [InlineData(typeof(Permission), "core.Permission")]
        [InlineData(typeof(Blob), "core.Blob")]
        public void Every_access_entity_and_the_blob_have_their_query_names(Type entity, string name)
        {
            Assert.Equal(name, model.Metadata.Get(entity).Name);
        }

        [Fact]
        public void The_securable_resources_of_users_and_roles_are_their_entity_names()
        {
            Assert.Equal(CoreResources.User, model.Metadata.Get(typeof(User)).Name);
            Assert.Equal(CoreResources.Role, model.Metadata.Get(typeof(Role)).Name);
        }

        [Theory]
        [InlineData(typeof(User), nameof(User.Id), PropertyOwnership.ServerOwned)]
        [InlineData(typeof(User), nameof(User.Kind), PropertyOwnership.WriteOnce)]
        [InlineData(typeof(User), nameof(User.Subject), PropertyOwnership.ServerOwned)]
        [InlineData(typeof(User), nameof(User.Email), PropertyOwnership.Editable)]
        [InlineData(typeof(User), nameof(User.State), PropertyOwnership.DatabaseOwned)]
        [InlineData(typeof(User), nameof(User.InvitedAt), PropertyOwnership.ServerOwned)]
        [InlineData(typeof(User), nameof(User.InviteStatus), PropertyOwnership.ServerOwned)]
        [InlineData(typeof(User), nameof(User.LastInviteError), PropertyOwnership.ServerOwned)]
        [InlineData(typeof(User), nameof(User.JoinedAt), PropertyOwnership.ServerOwned)]
        [InlineData(typeof(User), nameof(User.Name), PropertyOwnership.Editable)]
        [InlineData(typeof(User), nameof(User.ImageId), PropertyOwnership.Editable)]
        [InlineData(typeof(User), nameof(User.SignatureId), PropertyOwnership.Editable)]
        [InlineData(typeof(User), nameof(User.PreferredLanguage), PropertyOwnership.Editable)]
        [InlineData(typeof(User), nameof(User.PreferredDigits), PropertyOwnership.Editable)]
        [InlineData(typeof(User), nameof(User.Gender), PropertyOwnership.Editable)]
        [InlineData(typeof(User), nameof(User.ContactMobile), PropertyOwnership.Editable)]
        [InlineData(typeof(User), nameof(User.IsActive), PropertyOwnership.ServerOwned)]
        [InlineData(typeof(User), nameof(User.CreatedById), PropertyOwnership.ServerOwned)]
        [InlineData(typeof(User), nameof(User.ModifiedAt), PropertyOwnership.ServerOwned)]
        [InlineData(typeof(Role), nameof(Role.Name), PropertyOwnership.Editable)]
        [InlineData(typeof(Role), nameof(Role.Code), PropertyOwnership.Editable)]
        [InlineData(typeof(Role), nameof(Role.IsPublic), PropertyOwnership.Editable)]
        [InlineData(typeof(Role), nameof(Role.IsActive), PropertyOwnership.ServerOwned)]
        [InlineData(typeof(RoleMembership), nameof(RoleMembership.UserId), PropertyOwnership.Editable)]
        [InlineData(typeof(RoleMembership), nameof(RoleMembership.RoleId), PropertyOwnership.Editable)]
        [InlineData(typeof(RoleMembership), nameof(RoleMembership.EndsAt), PropertyOwnership.Editable)]
        [InlineData(typeof(Permission), nameof(Permission.Filter), PropertyOwnership.Editable)]
        [InlineData(typeof(Permission), nameof(Permission.FilterQueryexVersion), PropertyOwnership.Derived)]
        public void Every_property_has_its_ownership(Type entity, string property, PropertyOwnership ownership)
        {
            Assert.Equal(ownership, Property(entity, property).Ownership);
        }

        [Fact]
        public void A_user_is_a_temporal_activatable_top_level_entity_keyed_naturally_by_email()
        {
            EntityMetadata users = model.Metadata.Get(typeof(User));

            Assert.True(users.IsTopLevel);
            Assert.True(users.IsTemporal);
            Assert.True(users.IsActivatable);
            Assert.Equal(nameof(User.Email), users.NaturalKey!.Name);
            Assert.True(Property(typeof(User), nameof(User.Email)).IsUnique);
            Assert.True(Property(typeof(User), nameof(User.Subject)).IsUnique);
            Assert.Equal(nameof(User.Name), Assert.Single(users.MultilingualGroups).Primary.Name);
        }

        [Fact]
        public void The_users_enums_are_stored_by_name_in_their_specified_widths()
        {
            Assert.Equal(
                [(nameof(User.Kind), 10), (nameof(User.State), 8), (nameof(User.InviteStatus), 9), (nameof(User.PreferredDigits), 8), (nameof(User.Gender), 8)],
                new[] { nameof(User.Kind), nameof(User.State), nameof(User.InviteStatus), nameof(User.PreferredDigits), nameof(User.Gender) }
                    .Select(name => (name, Property(typeof(User), name).MaxLength ?? 0)));
            Assert.Equal(Enum.GetNames<UserKind>(), Property(typeof(User), nameof(User.Kind)).EnumValues);
        }

        [Fact]
        public void The_four_string_preferences_draw_from_the_platforms_value_sets()
        {
            Assert.Equal(
                [CoreValueSets.Language, CoreValueSets.Calendar, CoreValueSets.TimeZone, CoreValueSets.Region],
                new[] { nameof(User.PreferredLanguage), nameof(User.PreferredCalendar), nameof(User.PreferredTimeZone), nameof(User.PreferredRegion) }
                    .Select(name => Property(typeof(User), name).ValueSet));
        }

        [Fact]
        public void The_image_and_the_signature_are_blob_references_any_member_may_read()
        {
            BlobReferenceMetadata[] blobs = [.. model.Metadata.Get(typeof(User)).BlobReferences];

            Assert.Equal(["user-image", "user-signature"], blobs.Select(static blob => blob.Kind));
            Assert.All(blobs, static blob => Assert.Equal(BlobReadAccess.AnyMember, blob.Policy.ReadAccess));
            Assert.Equal(BlobKindPolicy.Avatar.MaxSize, blobs[0].Policy.MaxSize);
            Assert.Equal(1_048_576, blobs[1].Policy.MaxSize);
        }

        [Fact]
        public void A_user_carries_its_memberships_and_a_role_its_permissions()
        {
            ChildCollectionMetadata memberships = Assert.Single(model.Metadata.Get(typeof(User)).Children);
            ChildCollectionMetadata permissions = Assert.Single(model.Metadata.Get(typeof(Role)).Children);

            Assert.Equal((nameof(User.RoleMemberships), typeof(RoleMembership), nameof(RoleMembership.UserId)), (memberships.Property, memberships.Child.ClrType, memberships.ParentKey.Name));
            Assert.Equal((nameof(Role.Permissions), typeof(Permission), nameof(Permission.RoleId)), (permissions.Property, permissions.Child.ClrType, permissions.ParentKey.Name));
        }

        [Fact]
        public void A_membership_navigates_to_its_user_and_role_and_a_blob_to_its_uploader()
        {
            Assert.Equal(
                [("User", "core.User"), ("Role", "core.Role")],
                model.Metadata.Get(typeof(RoleMembership)).References.Select(static reference => (reference.Navigation, reference.Target.Name)));
            Assert.Equal(
                [("CreatedBy", "core.User")],
                model.Metadata.Get(typeof(Blob)).References.Select(static reference => (reference.Navigation, reference.Target.Name)));
        }

        [Fact]
        public void The_stamps_and_the_preferences_are_tables_but_not_entities()
        {
            Assert.DoesNotContain(model.Metadata.All, static entity => entity.Table.Name is "UserStamps" or "UserPreferences");
        }

        // --- Table shapes beyond the conventions ---

        [Fact]
        public void The_users_state_is_a_stored_computed_column_over_the_evidence()
        {
            IProperty state = model.Table("Users").FindProperty(nameof(User.State))!;

            Assert.Equal(UserConfiguration<User>.StateSql, state.GetComputedColumnSql());
            Assert.True(state.GetIsStored());
            Assert.Equal("varchar(8)", state.GetColumnType());
            Assert.False(state.IsColumnNullable());
        }

        [Fact]
        public void The_subject_is_compared_byte_for_byte_and_its_index_covers_the_connect_seek()
        {
            IEntityType users = model.Table("Users");
            IIndex subject = users.GetIndexes().Single(static index => index.GetDatabaseName() == "UX_Users_Subject");

            Assert.Equal("Latin1_General_100_BIN2", users.FindProperty(nameof(User.Subject))!.GetCollation());
            Assert.Equal((true, "[Subject] IS NOT NULL"), (subject.IsUnique, subject.GetFilter()));
            Assert.Equal([nameof(User.Kind), nameof(User.IsActive), nameof(User.State)], subject.GetIncludeProperties());
        }

        [Theory]
        [InlineData("Users", new[] { "CK_Users_Id", "CK_Users_IndividualEvidence", "CK_Users_KindEmail", "CK_Users_ServiceEvidence", "CK_Users_SystemHasNoSubject", "CK_Users_SystemIsRowOne" })]
        [InlineData("Roles", new[] { "CK_Roles_Id" })]
        [InlineData("RoleMemberships", new[] { "CK_RoleMemberships_Id", "CK_RoleMemberships_Window" })]
        [InlineData("Permissions", new[] { "CK_Permissions_FilterVersion", "CK_Permissions_Id", "CK_Permissions_WildcardHasNoFilter" })]
        [InlineData("UserStamps", new string[0])]
        [InlineData("UserPreferences", new string[0])]
        public void Every_table_has_exactly_its_named_checks(string table, string[] checks)
        {
            Assert.Equal(checks, model.Table(table).GetCheckConstraints().Select(static check => check.Name).Order(StringComparer.Ordinal));
        }

        [Theory]
        [InlineData("Users", new[] { "IX_Users_IsActive_Name", "UX_Users_Email", "UX_Users_ImageId", "UX_Users_SignatureId", "UX_Users_Subject" })]
        [InlineData("Roles", new[] { "IX_Roles_IsPublic", "UX_Roles_Code", "UX_Roles_Name", "UX_Roles_Name2", "UX_Roles_Name3" })]
        [InlineData("RoleMemberships", new[] { "IX_RoleMemberships_RoleId_UserId", "IX_RoleMemberships_UserId_RoleId" })]
        [InlineData("Permissions", new[] { "IX_Permissions_RoleId" })]
        [InlineData("UserStamps", new string[0])]
        [InlineData("UserPreferences", new string[0])]
        [InlineData("Blobs", new[] { "IX_Blobs_Expiry", "IX_Blobs_StagedBy", "UX_Blobs_StorageKey" })]
        public void Every_table_has_exactly_its_named_indexes(string table, string[] indexes)
        {
            Assert.Equal(indexes, model.Table(table).GetIndexes().Select(static index => index.GetDatabaseName()).Order(StringComparer.Ordinal));
        }

        [Fact]
        public void The_permissions_parent_key_index_covers_the_load_by_role()
        {
            IIndex index = Assert.Single(model.Table("Permissions").GetIndexes());

            Assert.Equal(
                [nameof(Permission.Resource), nameof(Permission.Action), nameof(Permission.Filter), nameof(Permission.FilterQueryexVersion)],
                index.GetIncludeProperties());
        }

        [Theory]
        [InlineData("Users", "FK_Users_CreatedById", "Users", DeleteBehavior.NoAction)]
        [InlineData("Users", "FK_Users_ImageId", "Blobs", DeleteBehavior.NoAction)]
        [InlineData("Users", "FK_Users_SignatureId", "Blobs", DeleteBehavior.NoAction)]
        [InlineData("RoleMemberships", "FK_RoleMemberships_UserId", "Users", DeleteBehavior.NoAction)]
        [InlineData("RoleMemberships", "FK_RoleMemberships_RoleId", "Roles", DeleteBehavior.NoAction)]
        [InlineData("Permissions", "FK_Permissions_RoleId", "Roles", DeleteBehavior.NoAction)]
        [InlineData("UserStamps", "FK_UserStamps_UserId", "Users", DeleteBehavior.Cascade)]
        [InlineData("UserPreferences", "FK_UserPreferences_UserId", "Users", DeleteBehavior.Cascade)]
        [InlineData("Blobs", "FK_Blobs_CreatedById", "Users", DeleteBehavior.NoAction)]
        public void Every_foreign_key_is_named_and_only_the_users_bookkeeping_cascades(string table, string name, string principal, DeleteBehavior onDelete)
        {
            IForeignKey foreignKey = model.Table(table).GetForeignKeys().Single(foreignKey => foreignKey.GetConstraintName() == name);

            Assert.Equal((principal, onDelete), (foreignKey.PrincipalEntityType.GetTableName(), foreignKey.DeleteBehavior));
        }

        [Fact]
        public void The_stamps_are_one_row_per_user_with_named_defaults_and_read_only_in_the_query_language()
        {
            IEntityType stamps = model.Table("UserStamps");

            Assert.True(Assert.Single(stamps.GetForeignKeys()).IsUnique);
            Assert.Equal(UserStampConfiguration.QueryexName, stamps.FindAnnotation(TellmaAnnotationNames.QueryexReadOnlyEntity)?.Value);
            Assert.Equal(
                [("PermissionsTag", "NEWID()", "DF_UserStamps_PermissionsTag"), ("PreferencesTag", "NEWID()", "DF_UserStamps_PreferencesTag")],
                StampTags.Select(name => (name, stamps.FindProperty(name)!.GetDefaultValueSql(), stamps.FindProperty(name)!.GetDefaultConstraintName())));
            Assert.Equal(0L, stamps.FindProperty(nameof(UserStamp.InboxVersion))!.GetDefaultValue());
            Assert.False(stamps.IsTemporal());
        }

        [Fact]
        public void The_preferences_are_keyed_by_user_and_ascii_key_and_written_through_their_table_type()
        {
            IEntityType preferences = model.Table("UserPreferences");

            Assert.Equal([nameof(UserPreference.UserId), nameof(UserPreference.Key)], preferences.FindPrimaryKey()!.Properties.Select(static property => property.Name));
            Assert.Equal("varchar(128)", preferences.FindProperty(nameof(UserPreference.Key))!.GetColumnType());
            Assert.Equal("nvarchar(max)", preferences.FindProperty(nameof(UserPreference.Value))!.GetColumnType());
            Assert.Contains(typeof(UserPreferenceList), model.Definition.TableTypes);
        }

        // --- Seeds ---

        [Fact]
        public void Every_seeded_row_lies_in_the_reserved_band()
        {
            // Every seeded table's key is an int column: the entity's Id, or a stamp's UserId.
            List<(string Table, int Key)> seeded = [];
            foreach (IEntityType entityType in model.DesignModel.GetEntityTypes())
            {
                IProperty key = entityType.FindPrimaryKey()!.Properties[0];
                seeded.AddRange(entityType.GetSeedData().Select(row => (entityType.GetTableName()!, (int)row[key.Name]!)));
            }

            Assert.Equal(
                [("Permissions", 1), ("RoleMemberships", 1), ("Roles", 1), ("UserStamps", 1), ("Users", 1)],
                seeded.OrderBy(static row => row.Table, StringComparer.Ordinal));
            Assert.All(seeded, static row => Assert.InRange(row.Key, 1, WellKnownIds.ReservedIdBandEnd));
        }

        [Fact]
        public void The_system_user_is_seeded_joined_active_and_self_authored()
        {
            IDictionary<string, object?> user = Assert.Single(model.Table("Users").GetSeedData());
            DateTimeOffset seededAt = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

            Assert.Equal(WellKnownIds.SystemUserId, user[nameof(User.Id)]);
            Assert.Equal(UserKind.System, user[nameof(User.Kind)]);
            Assert.Equal("System", user[nameof(User.Name)]);
            Assert.Equal(true, user[nameof(User.IsActive)]);
            Assert.Equal(seededAt, user[nameof(User.JoinedAt)]);
            Assert.Null(Value(user, nameof(User.Subject)));
            Assert.Null(Value(user, nameof(User.Email)));
            Assert.Equal(
                [WellKnownIds.SystemUserId, WellKnownIds.SystemUserId, seededAt, seededAt],
                new[] { nameof(User.CreatedById), nameof(User.ModifiedById), nameof(User.CreatedAt), nameof(User.ModifiedAt) }.Select(name => user[name]));
        }

        [Fact]
        public void The_administrator_role_grants_everything_to_the_system_user_permanently()
        {
            IDictionary<string, object?> role = Assert.Single(model.Table("Roles").GetSeedData());
            IDictionary<string, object?> permission = Assert.Single(model.Table("Permissions").GetSeedData());
            IDictionary<string, object?> membership = Assert.Single(model.Table("RoleMemberships").GetSeedData());

            Assert.Equal((WellKnownIds.AdministratorRoleId, "Administrator", "Administrator", false, true), (role["Id"], role["Name"], role["Code"], role["IsPublic"], role["IsActive"]));
            Assert.Equal((WellKnownIds.AdministratorPermissionId, WellKnownIds.AdministratorRoleId, "*", "*"), (permission["Id"], permission["RoleId"], permission["Resource"], permission["Action"]));
            Assert.Null(Value(permission, "Filter"));
            Assert.Equal((WellKnownIds.SystemAdministratorMembershipId, WellKnownIds.SystemUserId, WellKnownIds.AdministratorRoleId), (membership["Id"], membership["UserId"], membership["RoleId"]));
            Assert.Null(Value(membership, "StartsAt"));
            Assert.Null(Value(membership, "EndsAt"));
        }

        [Fact]
        public void The_system_users_stamp_row_is_seeded_with_fixed_tags()
        {
            IDictionary<string, object?> stamp = Assert.Single(model.Table("UserStamps").GetSeedData());

            Assert.Equal(
                (WellKnownIds.SystemUserId, new Guid("00000000-0000-0000-0000-000000000001"), new Guid("00000000-0000-0000-0000-000000000002")),
                (stamp["UserId"], stamp["PermissionsTag"], stamp["PreferencesTag"]));
        }

        /// <summary>A seeded row's value of a column, null when the row leaves the column out.</summary>
        /// <param name="row">The seeded row.</param>
        /// <param name="column">The column.</param>
        /// <returns>The value.</returns>
        private static object? Value(IDictionary<string, object?> row, string column)
        {
            return row.TryGetValue(column, out object? value) ? value : null;
        }

        /// <summary>A property's metadata.</summary>
        /// <param name="entity">The entity class.</param>
        /// <param name="property">The property.</param>
        /// <returns>The metadata.</returns>
        private PropertyMetadata Property(Type entity, string property)
        {
            return model.Metadata.Get(entity).Properties.Single(candidate => candidate.Name == property);
        }
    }
}
