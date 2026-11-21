// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.Tests.Access;
using Tellma.Core.Users;

namespace Tellma.Core.Tests.Users
{
    /// <summary>
    ///     The users' service hooks: a save normalises its rows and checks each new or changed locale
    ///     member and contact detail; the invite and issue-credentials actions check each target's
    ///     kind, activity and progress; the system user refuses every ability on its record.
    /// </summary>
    public sealed class UserHooksTests : IDisposable
    {
        /// <summary>The service.</summary>
        private readonly UsersKit _kit = new();

        /// <inheritdoc />
        public void Dispose()
        {
            _kit.Dispose();
        }

        [Fact]
        public async Task A_save_normalises_each_row()
        {
            User row = new() { Kind = UserKind.Individual, Name = " Ada ", Email = " Ada@Example.com ", PreferredLanguage = "AR" };
            SaveContext<User> context = AccessRulesHarness.Save(new RulesLoader(), UsersKit.CallerId, (row, null));

            await _kit.Hooks.PreprocessAsync(context);

            Assert.Equal("Ada", row.Name);
            Assert.Equal("ada@example.com", row.Email);
            Assert.Equal("ar", row.PreferredLanguage);
        }

        [Fact]
        public async Task A_new_row_with_unoffered_or_malformed_members_reports_each_at_its_member()
        {
            User row = new()
            {
                Kind = UserKind.Individual,
                Name = "Ada",
                Email = "ada@example.com",
                PreferredLanguage = "fr",
                PreferredCalendar = "xx",
                PreferredTimeZone = "Mars/Olympus",
                PreferredRegion = "ZZ",
                ContactEmail = "Ada <ada@example.com>",
                ContactMobile = "0501234567",
            };
            SaveContext<User> context = AccessRulesHarness.Save(new RulesLoader(), UsersKit.CallerId, (row, null));

            await _kit.Hooks.ValidateAsync(context);

            Assert.Equal(
                [
                    ("[0].PreferredLanguage", AccessCodes.LanguageNotOffered),
                    ("[0].PreferredCalendar", AccessCodes.CalendarUnknown),
                    ("[0].PreferredTimeZone", AccessCodes.TimeZoneUnknown),
                    ("[0].PreferredRegion", AccessCodes.RegionNotOffered),
                    ("[0].ContactEmail", AccessCodes.ContactEmailInvalid),
                    ("[0].ContactMobile", AccessCodes.MobileInvalid),
                ],
                AccessRulesHarness.Errors(context.Errors));
        }

        [Fact]
        public async Task A_new_row_with_offered_and_well_formed_members_reports_nothing()
        {
            User row = new()
            {
                Kind = UserKind.Individual,
                Name = "Ada",
                Email = "ada@example.com",
                PreferredLanguage = "ar",
                PreferredCalendar = "uq",
                PreferredTimeZone = "Asia/Dubai",
                PreferredRegion = "SA",
                ContactEmail = "ada@example.com",
                ContactMobile = "+971501234567",
            };
            SaveContext<User> context = AccessRulesHarness.Save(new RulesLoader(), UsersKit.CallerId, (row, null));

            await _kit.Hooks.ValidateAsync(context);

            Assert.False(context.Errors.HasErrors);
        }

        [Fact]
        public async Task A_stored_value_no_longer_offered_is_left_alone_until_it_changes()
        {
            User before = new() { Id = 7, Kind = UserKind.Individual, Name = "Ada", Email = "ada@example.com", PreferredLanguage = "fr", ContactMobile = "0501234567" };
            User unchanged = new() { Id = 7, Kind = UserKind.Individual, Name = "Ada L.", Email = "ada@example.com", PreferredLanguage = "fr", ContactMobile = "0501234567" };
            User changed = new() { Id = 7, Kind = UserKind.Individual, Name = "Ada", Email = "ada@example.com", PreferredLanguage = "de", ContactMobile = "0501234567" };
            SaveContext<User> context = AccessRulesHarness.Save(new RulesLoader(), UsersKit.CallerId, (unchanged, before), (changed, before));

            await _kit.Hooks.ValidateAsync(context);

            Assert.Equal([("[1].PreferredLanguage", AccessCodes.LanguageNotOffered)], AccessRulesHarness.Errors(context.Errors));
        }

        [Fact]
        public async Task The_invite_takes_active_individuals_who_have_not_joined_each_with_an_email()
        {
            User service = UsersKit.ServiceAccount(3, null);
            User inactive = UsersKit.Individual(4, "Bob");
            inactive.IsActive = false;
            User joined = UsersKit.Individual(5, "Cy");
            joined.State = UserState.Joined;
            User emailless = UsersKit.Individual(6, "Di");
            emailless.Email = null;
            User invited = UsersKit.Individual(7, "Ed");
            invited.State = UserState.Invited;
            ActionContext<User, int> context = AccessRulesHarness.Action(new RulesLoader(), UsersKit.CallerId, [3, 4, 5, 6, 7], service, inactive, joined, emailless, invited);

            await _kit.Hooks.ValidateActionAsync(UserService<User>.InviteAction, context);

            Assert.Equal(
                [
                    ("Ids[0]", AccessCodes.NotIndividual),
                    ("Ids[1]", AccessCodes.Inactive),
                    ("Ids[2]", AccessCodes.AlreadyJoined),
                    ("Ids[3]", ValidationCodes.Required),
                ],
                AccessRulesHarness.Errors(context.Errors));
        }

        [Fact]
        public async Task The_issue_credentials_action_takes_an_active_service_account()
        {
            User individual = UsersKit.Individual(3, "Ada");
            ActionContext<User, int> context = AccessRulesHarness.Action(new RulesLoader(), UsersKit.CallerId, [3], individual);

            await _kit.Hooks.ValidateActionAsync(UserService<User>.IssueCredentialsAction, context);

            Assert.Equal([("Ids[0]", AccessCodes.NotService)], AccessRulesHarness.Errors(context.Errors));

            User inactive = UsersKit.ServiceAccount(4, "svc-1");
            inactive.IsActive = false;
            context = AccessRulesHarness.Action(new RulesLoader(), UsersKit.CallerId, [4], inactive);

            await _kit.Hooks.ValidateActionAsync(UserService<User>.IssueCredentialsAction, context);

            Assert.Equal([("Ids[0]", AccessCodes.Inactive)], AccessRulesHarness.Errors(context.Errors));
        }

        [Fact]
        public async Task The_system_user_is_left_to_the_access_rules_and_refuses_every_ability_on_its_record()
        {
            User system = UsersKit.ServiceAccount(WellKnownIds.SystemUserId, null);
            ActionContext<User, int> context = AccessRulesHarness.Action(new RulesLoader(), UsersKit.CallerId, [WellKnownIds.SystemUserId], system);

            await _kit.Hooks.ValidateActionAsync(UserService<User>.InviteAction, context);

            Assert.False(context.Errors.HasErrors);
            Assert.Equal(AccessCodes.SystemUserImmutable, _kit.Hooks.RefuseOnRecord(system, "Update"));
            Assert.Null(_kit.Hooks.RefuseOnRecord(UsersKit.Individual(9, "Ada"), "Update"));
        }

        [Fact]
        public async Task Other_actions_take_no_rules_of_the_service()
        {
            User inactive = UsersKit.Individual(4, "Bob");
            inactive.IsActive = false;
            ActionContext<User, int> context = AccessRulesHarness.Action(new RulesLoader(), UsersKit.CallerId, [4], inactive);

            await _kit.Hooks.ValidateActionAsync("activate", context);

            Assert.False(context.Errors.HasErrors);
        }
    }
}
