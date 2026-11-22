// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Localization;
using Tellma.Core.Abstractions.Notifications;
using Tellma.Core.Abstractions.TableTypes;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.Access;
using Tellma.Core.Composition;
using Tellma.Core.Notifications;

namespace Tellma.Core.Users
{
    // Spec 0018 §3.3 and §3.5's notification surfaces of the users' stack. The invite raises the
    // core.user.added notice for every user it added, appended to the action's persist batch so it
    // commits with the write-back. A user sends themselves a test message through any channel but
    // the inbox, outside any batch. Another user's notification settings are spec 0021 §4.3's own
    // read and save, under core.User × Preferences: the get in one Read batch under the grant's
    // filter, the save a single-target entity action whose statement rides the action's persist
    // batch, so that round trip's epilogue moves the target's preferences tag.
    public partial class UserService<TUser>
    {
        /// <summary>The name of the caller's test message to themselves.</summary>
        public const string MeTestNotificationAction = "me/test-notification";

        /// <summary>The name of an administrator's read of another user's notification settings.</summary>
        public const string NotificationPreferencesGetAction = "notification-preferences/get";

        /// <summary>The name of an administrator's save of another user's notification settings.</summary>
        public const string NotificationPreferencesSaveAction = "notification-preferences/save";

        /// <summary>The notification type an invite raises for each user it added.</summary>
        internal const string UserAddedType = "core.user.added";

        /// <summary>The area the administrator's log names for a write of the notification settings.</summary>
        internal const string NotificationPreferencesArea = "notification-preferences";

        /// <summary>
        ///     Sends the caller a test message through a notification channel, so they learn whether
        ///     it reaches them. The inbox cannot be tested; a channel this host sends nothing through
        ///     answers that it is not configured.
        /// </summary>
        /// <param name="request">The channel.</param>
        /// <param name="cancellationToken">Cancels the send.</param>
        /// <returns>Whether the message went out, or why not.</returns>
        /// <exception cref="BadRequestException">The request has no body.</exception>
        /// <exception cref="ValidationException">The channel is the inbox, or no channel has the key.</exception>
        /// <exception cref="DependencyUnavailableException">The channel's transport failed or refused the message.</exception>
        [ApiAction(MeTestNotificationAction, MemberOnly = true)]
        public async Task<TestNotificationResult> SendTestNotificationAsync(TestNotificationRequest request, CancellationToken cancellationToken = default)
        {
            if (request is null)
            {
                throw new BadRequestException("The request has no body.");
            }

            // --- The channel: never the inbox, and one this instance registers ---
            string key = request.Channel;
            ValidationPath channelPath = ValidationPath.Root.Property(nameof(TestNotificationRequest.Channel));
            if (string.Equals(key, CoreFeature.InboxChannel, StringComparison.Ordinal))
            {
                throw new ValidationException([new ValidationError(channelPath, AccessCodes.ChannelNotTestable, AccessRefusals.Arguments(("channel", key)))]);
            }

            if (_dependencies.NotificationChannels.Find(key) is null)
            {
                throw new ValidationException([new ValidationError(channelPath, NotificationValidationCodes.UnknownChannel, AccessRefusals.Arguments(("channel", key)))]);
            }

            // --- A registered channel this host has no sender for cannot send ---
            if (_dependencies.ChannelSender(key) is not INotificationChannel sender)
            {
                _dependencies.Metrics.RecordTestNotification(key, UsersTelemetryNames.TestNotificationNotConfigured);
                return TestNotificationResult.NotConfigured;
            }

            // --- The caller, confirmed: no round trip within the fast-deny window, else a
            // prologue-only one, which refuses a deactivated caller before anything is sent ---
            ConnectedUser caller = await _dependencies.Connection.CurrentAsync(cancellationToken).ConfigureAwait(false);
            if (_dependencies.Connection.IsPrologueDue(caller))
            {
                _ = await _dependencies.Runner.RunAsync(
                    BatchPurpose.Read,
                    static (_, _) => ValueTask.CompletedTask,
                    static (confirmed, _) => confirmed,
                    cancellationToken).ConfigureAwait(false);
            }

            // --- The send, counted by its outcome ---
            TestNotificationResult result;
            try
            {
                result = await sender.SendTestAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (DependencyUnavailableException)
            {
                _dependencies.Metrics.RecordTestNotification(key, UsersTelemetryNames.TestNotificationUnavailable);
                throw;
            }

            _dependencies.Metrics.RecordTestNotification(key, result switch
            {
                TestNotificationResult.Sent => UsersTelemetryNames.TestNotificationSent,
                TestNotificationResult.NoContactAddress => UsersTelemetryNames.TestNotificationNoContactAddress,
                TestNotificationResult.NotConfigured => UsersTelemetryNames.TestNotificationNotConfigured,
                _ => throw new InvalidOperationException($"The channel '{key}' answered an unknown test result {result}."),
            });
            return result;
        }

        /// <summary>
        ///     Reads another user's notification settings in one round trip, for a caller who holds the
        ///     users' <c>Preferences</c> grant: the stored deviations from each channel's default, of
        ///     the types and channels this instance registers. A user the grant's filter hides is not
        ///     found.
        /// </summary>
        /// <param name="request">The user.</param>
        /// <param name="cancellationToken">Cancels the read.</param>
        /// <returns>The user's settings, by type and then channel.</returns>
        /// <exception cref="NotFoundException">No user the grant's filter admits has the id.</exception>
        /// <exception cref="ValidationException">The user is not an individual.</exception>
        [ApiAction(NotificationPreferencesGetAction, Action = AccessActions.Preferences, Idempotent = true, Mutation = false)]
        public async Task<IReadOnlyList<NotificationPreference>> GetNotificationPreferencesAsync(UserPreferencesRequest request, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            int target = request.UserId;
            string[] missing = [target.ToString(CultureInfo.InvariantCulture)];
            if (target <= 0)
            {
                throw new NotFoundException(CoreResources.User, missing);
            }

            // --- One Read batch: the target under the grant's filter, and the target's rows ---
            BatchResult<EntityQueryResult<TUser>>? visible = null;
            BatchResult<RawResult>? rows = null;
            (TUser user, QueryRowSet set) = await _dependencies.Runner.RunAsync(
                BatchPurpose.Read,
                (caller, batch) =>
                {
                    visible = ComposeVisibleRead(caller, batch, AccessActions.Preferences, [target], nameof(User.Id) + "," + nameof(User.Kind), "notification preferences");
                    rows = visible is null ? null : NotificationPreferenceRules.Read(batch, target);
                    return ValueTask.CompletedTask;
                },
                (_, _) => visible is { Value.Entities: [TUser only] } && rows is { } read
                    ? (only, read.Value.ResultSets[0])
                    : throw new NotFoundException(CoreResources.User, missing),
                cancellationToken).ConfigureAwait(false);

            // --- Notification settings are an individual's ---
            return user.Kind == UserKind.Individual
                ? NotificationPreferenceRules.Known(set, _dependencies.NotificationTypes, _dependencies.NotificationChannels)
                : throw new ValidationException([new ValidationError(
                    ValidationPath.Root.Property(nameof(UserPreferencesRequest.UserId)),
                    AccessCodes.NotIndividual,
                    new Dictionary<string, object?>(StringComparer.Ordinal))]);
        }

        /// <summary>
        ///     Saves another user's complete set of notification settings, for a caller who holds the
        ///     users' <c>Preferences</c> grant on the target: a type and channel absent from the set
        ///     revert to the channel's default, and a row equal to the default is not stored. The
        ///     target's preferences tag moves, so the target's open tabs pick the change up. A
        ///     read-only tenant admits it.
        /// </summary>
        /// <param name="context">The action's context, over one target.</param>
        /// <param name="arguments">The target's complete set.</param>
        /// <returns>The target's settings as stored, by type and then channel.</returns>
        [EntityAction(NotificationPreferencesSaveAction, Action = AccessActions.Preferences, SingleTarget = true, AllowWhenReadOnly = true)]
        public async Task<IReadOnlyList<NotificationPreference>> SaveNotificationPreferencesAsync(ActionContext<TUser, int> context, NotificationPreferencesArguments arguments)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(arguments);
            int target = TargetOf(context);

            // The action's validation reported every refusal at its argument; a refused action never
            // persists, so the rows are computed here without raising them again.
            IReadOnlyList<NotificationPreferenceList> deviations = NotificationPreferenceRules.Check(
                arguments.Preferences, _dependencies.NotificationTypes, _dependencies.NotificationChannels, new ValidationErrors(), ValidationPath.Root);

            // --- The statement for the target, on the action's persist batch ---
            _ = NotificationPreferenceRules.Save(context.Batch, target, deviations);
            await context.Persisted.ConfigureAwait(false);
            UsersLog.UserPreferencesChangedByAdministrator(_dependencies.Logger, target, NotificationPreferencesArea);

            // The statement leaves exactly the bound rows stored, so the answer is those rows.
            return NotificationPreferenceRules.Stored(deviations);
        }

        /// <summary>
        ///     The checks of an administrative save of another user's notification settings, before its
        ///     persist: each row's type and channel registered, each pair once, and the inbox of a type
        ///     that cannot be muted kept on, each at the arguments' member.
        /// </summary>
        /// <param name="action">The action's name.</param>
        /// <param name="context">The action's context.</param>
        private void ValidateNotificationPreferenceArguments(string action, ActionContext<TUser, int> context)
        {
            // The arguments' own presence is the binding's; a null set never reaches the checks.
            if (string.Equals(action, NotificationPreferencesSaveAction, StringComparison.Ordinal) && context.Arguments is NotificationPreferencesArguments save)
            {
                _ = NotificationPreferenceRules.Check(
                    save.Preferences,
                    _dependencies.NotificationTypes,
                    _dependencies.NotificationChannels,
                    context.Errors,
                    ValidationPath.Root.Property(ArgumentsProperty).Property(nameof(NotificationPreferencesArguments.Preferences)));
            }
        }

        /// <summary>
        ///     Appends the <c>core.user.added</c> notice of the users an invite added to the action's
        ///     persist batch, so it commits with the write-back: each user is told who added them to
        ///     which tenant, once, however often they are re-invited before they read it.
        /// </summary>
        /// <param name="context">The action's context.</param>
        /// <param name="added">The users the invite added.</param>
        /// <param name="actorUserId">The caller who invited; null for the system.</param>
        /// <param name="tenantName">The tenant's name in each content language.</param>
        /// <param name="actorName">The caller's name in each content language.</param>
        private void NotifyAdded(ActionContext<TUser, int> context, IReadOnlyList<int> added, int? actorUserId, LocalizedText tenantName, LocalizedText actorName)
        {
            // The arguments are identical for every recipient, so one request names them all, in
            // as many requests as the notifier's recipient cap needs.
            UserAddedArguments arguments = new(tenantName, actorName);
            int chunk = _dependencies.NotificationsOptions.MaxRecipientsPerRequest;
            foreach (int[] recipients in added.Chunk(chunk))
            {
                context.Notify(new NotificationRequest(
                    UserAddedType,
                    recipients,
                    arguments,
                    TargetResource: null,
                    TargetId: null,
                    actorUserId,
                    DedupKey: UserAddedType));
            }
        }
    }

    /// <summary>The arguments of the <c>core.user.added</c> notice, stored as <c>{ tenantName, actorName }</c>.</summary>
    /// <param name="TenantName">The tenant's name in each content language.</param>
    /// <param name="ActorName">The name of the user who added the recipient, in each content language.</param>
    internal sealed record UserAddedArguments(LocalizedText TenantName, LocalizedText ActorName);
}
