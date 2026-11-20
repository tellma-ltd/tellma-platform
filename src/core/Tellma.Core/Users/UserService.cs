// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Localization;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.Access;

namespace Tellma.Core.Users
{
    // The service is spec 0018 §3.1's UserService: the hooks of §3.2 here, the invite action of §3.3,
    // the invitation status of §3.4, the issue-credentials action of §3.8 and the post-commit side
    // effects of §3.6 in the partial files beside it. The access rules of spec 0014 §8.4 are a
    // separate component, so a distribution leaf cannot drop them.
    /// <summary>
    ///     The users' stack service: the standard operations over the tenant's users and their role
    ///     memberships, closed over the distribution's user leaf, plus inviting users through the
    ///     identity server, issuing service accounts' client credentials, and reading how far each
    ///     invitation got. The access rules that keep the users' writes safe run beside it as a
    ///     component of the stack.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A save normalises the users' emails, names and locale members, then checks the locale
    ///         members against what the deployment offers and the contact details against their
    ///         formats. A leaf that overrides a hook calls this one to keep it.
    ///     </para>
    ///     <para>
    ///         The service declares that it enlists writes of its own stack: a user's self-service
    ///         writes run under the stack's authority through the caller's frame.
    ///     </para>
    /// </remarks>
    /// <typeparam name="TUser">The stack's user leaf.</typeparam>
    /// <param name="factory">The pipeline factory the scope resolves.</param>
    /// <param name="dependencies">What the service needs from the platform beyond its pipeline.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public partial class UserService<TUser>(IEntityPipelineFactory factory, UserServiceDependencies dependencies) : EntityService<TUser>(factory), IEnlists<TUser>
        where TUser : User
    {
        /// <summary>The name of the invite action.</summary>
        public const string InviteAction = "invite";

        /// <summary>The name of the issue-credentials action.</summary>
        public const string IssueCredentialsAction = "issue-credentials";

        /// <summary>The name of the built-in action that activates users.</summary>
        private const string ActivateAction = "activate";

        /// <summary>The name of the built-in action that deactivates users.</summary>
        private const string DeactivateAction = "deactivate";

        /// <summary>What the service needs from the platform beyond its pipeline.</summary>
        private readonly UserServiceDependencies _dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));

        /// <summary>Preprocesses a save: the base's preprocessing, then each user's emails, names and locale members normalised.</summary>
        /// <param name="context">The save's context; its rows have no final ids yet.</param>
        /// <returns>A task that completes when the payload is preprocessed.</returns>
        protected override async Task PreprocessAsync(SaveContext<TUser> context)
        {
            ArgumentNullException.ThrowIfNull(context);
            await base.PreprocessAsync(context).ConfigureAwait(false);
            foreach (TUser user in context.Entities)
            {
                UserRules.Preprocess(user);
            }
        }

        /// <summary>
        ///     Validates a save: the base's rules, then each new or changed locale member against what
        ///     the deployment offers and each new or changed contact detail against its format. A
        ///     stored value the deployment no longer offers is left alone until it changes.
        /// </summary>
        /// <param name="context">The save's context.</param>
        /// <returns>A task that completes when the rules ran.</returns>
        protected override async Task ValidateAsync(SaveContext<TUser> context)
        {
            ArgumentNullException.ThrowIfNull(context);
            await base.ValidateAsync(context).ConfigureAwait(false);

            // --- The before images, which tell a changed value from a stored one ---
            await context.Loader.LoadAsync().ConfigureAwait(false);
            for (int index = 0; index < context.Entities.Count; index++)
            {
                TUser user = context.Entities[index];

                // --- The locale members: the language and the region the catalogue offers, the
                // calendar and the time zone their value sets hold (the pipeline stores the set's
                // spelling afterwards, and reports nothing more at a path with an error here) ---
                if (user.PreferredLanguage is string language && Checks(context, index, static row => row.PreferredLanguage)
                    && !_dependencies.Languages.IsOffered(language))
                {
                    context.Errors.Add(index, static row => row.PreferredLanguage, AccessCodes.LanguageNotOffered);
                }

                if (user.PreferredCalendar is string calendar && Checks(context, index, static row => row.PreferredCalendar)
                    && !UserRules.InSet(_dependencies.ValueSets, CoreValueSets.Calendar, calendar))
                {
                    context.Errors.Add(index, static row => row.PreferredCalendar, AccessCodes.CalendarUnknown);
                }

                if (user.PreferredTimeZone is string zone && Checks(context, index, static row => row.PreferredTimeZone)
                    && !UserRules.InSet(_dependencies.ValueSets, CoreValueSets.TimeZone, zone))
                {
                    context.Errors.Add(index, static row => row.PreferredTimeZone, AccessCodes.TimeZoneUnknown);
                }

                if (user.PreferredRegion is string region && Checks(context, index, static row => row.PreferredRegion)
                    && !_dependencies.Languages.IsRegionOffered(region))
                {
                    context.Errors.Add(index, static row => row.PreferredRegion, AccessCodes.RegionNotOffered);
                }

                // --- The contact details: one plain address, and an E.164 number ---
                if (user.ContactEmail is string email && Checks(context, index, static row => row.ContactEmail)
                    && !UserRules.IsContactEmail(email))
                {
                    context.Errors.Add(index, static row => row.ContactEmail, AccessCodes.ContactEmailInvalid);
                }

                if (user.ContactMobile is string mobile && Checks(context, index, static row => row.ContactMobile)
                    && !UserRules.IsMobile(mobile))
                {
                    context.Errors.Add(index, static row => row.ContactMobile, AccessCodes.MobileInvalid);
                }
            }
        }

        /// <summary>
        ///     Validates the service's own actions per target: <c>invite</c> takes active individuals who
        ///     have not joined yet, each with an email; <c>issue-credentials</c> takes an active service
        ///     account. The system user is the access rules' to refuse.
        /// </summary>
        /// <param name="action">The action's name.</param>
        /// <param name="context">The action's context.</param>
        /// <returns>A task that completes when the rules ran.</returns>
        protected override async Task ValidateActionAsync(string action, ActionContext<TUser, int> context)
        {
            ArgumentNullException.ThrowIfNull(action);
            ArgumentNullException.ThrowIfNull(context);
            await base.ValidateActionAsync(action, context).ConfigureAwait(false);

            bool invite = string.Equals(action, InviteAction, StringComparison.Ordinal);
            bool credentials = string.Equals(action, IssueCredentialsAction, StringComparison.Ordinal);
            if (!invite && !credentials)
            {
                return;
            }

            // --- The targets load with the first round ---
            await context.Loader.LoadAsync().ConfigureAwait(false);
            for (int index = 0; index < context.Entities.Count; index++)
            {
                TUser user = context.Entities[index];
                if (user.Id == WellKnownIds.SystemUserId)
                {
                    continue;
                }

                ValidationPath at = AccessRefusals.Target(context.Ids, user.Id, index);
                if (invite)
                {
                    ValidateInvitee(context.Errors, at, user);
                }
                else
                {
                    ValidateServiceAccount(context.Errors, at, user);
                }
            }
        }

        /// <summary>Why a row refuses an ability: the system user refuses every one.</summary>
        /// <param name="row">The row.</param>
        /// <param name="ability">The ability.</param>
        /// <returns>The reason's resource key, or null.</returns>
        protected override string? RefuseOnRecord(TUser row, string ability)
        {
            ArgumentNullException.ThrowIfNull(row);
            return row.Id == WellKnownIds.SystemUserId ? AccessCodes.SystemUserImmutable : base.RefuseOnRecord(row, ability);
        }

        /// <summary>The invite's rules for one target.</summary>
        /// <param name="errors">The action's errors.</param>
        /// <param name="at">The target's path.</param>
        /// <param name="user">The target.</param>
        internal static void ValidateInvitee(ValidationErrors errors, ValidationPath at, User user)
        {
            ArgumentNullException.ThrowIfNull(errors);
            ArgumentNullException.ThrowIfNull(user);
            if (user.Kind != UserKind.Individual)
            {
                errors.Add(at, AccessCodes.NotIndividual);
            }
            else if (string.IsNullOrWhiteSpace(user.Email))
            {
                // An individual always has an email; one without cannot be invited, whatever made it so.
                errors.Add(at, ValidationCodes.Required, AccessRefusals.Arguments(("property", nameof(User.Email))));
            }

            if (!user.IsActive)
            {
                errors.Add(at, AccessCodes.Inactive);
            }

            if (user.State == UserState.Joined)
            {
                errors.Add(at, AccessCodes.AlreadyJoined);
            }
        }

        /// <summary>The issue-credentials action's rules for its target.</summary>
        /// <param name="errors">The action's errors.</param>
        /// <param name="at">The target's path.</param>
        /// <param name="user">The target.</param>
        internal static void ValidateServiceAccount(ValidationErrors errors, ValidationPath at, User user)
        {
            ArgumentNullException.ThrowIfNull(errors);
            ArgumentNullException.ThrowIfNull(user);
            if (user.Kind != UserKind.Service)
            {
                errors.Add(at, AccessCodes.NotService);
            }

            if (!user.IsActive)
            {
                errors.Add(at, AccessCodes.Inactive);
            }
        }

        /// <summary>Whether a save row's member needs checking: the row is new, or the member changed.</summary>
        /// <typeparam name="TValue">The member's type.</typeparam>
        /// <param name="context">The save's context.</param>
        /// <param name="index">The payload row.</param>
        /// <param name="member">Reads the member.</param>
        /// <returns>True when it needs checking.</returns>
        private static bool Checks<TValue>(SaveContext<TUser> context, int index, Func<TUser, TValue> member)
        {
            return context.IsNew(index) || context.Changed(index, member);
        }

        /// <summary>
        ///     Waits for the action to fail with the validation errors its rounds collected: the persist
        ///     never runs, so the wait rethrows what ended the action.
        /// </summary>
        /// <param name="context">The action's context.</param>
        /// <returns>Never returns normally.</returns>
        /// <exception cref="InvalidOperationException">The action persisted although its validation failed.</exception>
        private static async Task FailValidatedAsync(ActionContext<TUser, int> context)
        {
            await context.Persisted.ConfigureAwait(false);
            throw new InvalidOperationException("The action persisted although its validation reported errors.");
        }
    }
}
