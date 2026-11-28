// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Frozen;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Notifications;
using Tellma.Core.Abstractions.TableTypes;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.Crud;

namespace Tellma.Core.Notifications
{
    // The service is spec 0021 §4.3: the caller's stored deviations, and the save of the caller's
    // complete set, which stores the deviations from each channel's default alone and moves the
    // caller's PreferencesTag through the batch's epilogue (SqlOptions.ForCaller). A save is the
    // caller's own bookkeeping, which a read-only tenant still admits.
    /// <summary>The calling user's notification preferences: which channels deliver which types to them.</summary>
    /// <param name="runner">Runs every batch behind the connect prologue.</param>
    /// <param name="accessor">The scope's request context.</param>
    /// <param name="frames">The write frames the scope's persists are nested in.</param>
    /// <param name="types">The registered notification types.</param>
    /// <param name="channels">The registered channels.</param>
    /// <param name="metrics">The notifications' instruments.</param>
    [ApiRoute("notification-preferences")]
    internal sealed class NotificationPreferencesService(
        IGuardedBatchRunner runner,
        IRequestContextAccessor accessor,
        WriteFrames frames,
        INotificationTypeRegistry types,
        INotificationChannelRegistry channels,
        NotificationsMetrics metrics)
    {
        /// <summary>
        ///     The caller's stored preferences: the deviations from each channel's default, of the
        ///     types and channels this instance registers. A type and channel absent from the list
        ///     take the channel's default.
        /// </summary>
        /// <param name="cancellationToken">Cancels the read.</param>
        /// <returns>The preferences, by type and then channel.</returns>
        [ApiAction("get", MemberOnly = true, Idempotent = true, Mutation = false, Mcp = McpExposure.Hidden)]
        public async Task<IReadOnlyList<NotificationPreference>> GetAsync(CancellationToken cancellationToken = default)
        {
            BatchResult<RawResult>? found = null;
            return await runner.RunAsync(
                BatchPurpose.Read,
                (_, batch) =>
                {
                    found = NotificationPreferenceRules.Read(batch, userId: null);
                    return ValueTask.CompletedTask;
                },
                (_, _) => Known(found!.Value.ResultSets[0]),
                cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        ///     Saves the caller's complete set of preferences: a type and channel absent from the list
        ///     revert to the channel's default, and a row equal to the default is not stored.
        /// </summary>
        /// <param name="preferences">The caller's complete set.</param>
        /// <param name="cancellationToken">Cancels the save before its persist is sent.</param>
        /// <returns>The preferences as stored, as <see cref="GetAsync" /> answers them.</returns>
        /// <exception cref="BadRequestException">The request has no body.</exception>
        /// <exception cref="ValidationException">A row names an unknown type or channel, repeats a pair, or mutes a type that cannot be muted.</exception>
        /// <exception cref="TenantUnavailableException">The tenant is frozen.</exception>
        [ApiAction("save", MemberOnly = true, AllowWhenReadOnly = true, Mcp = McpExposure.Hidden)]
        public async Task<IReadOnlyList<NotificationPreference>> SaveAsync(IReadOnlyList<NotificationPreference> preferences, CancellationToken cancellationToken = default)
        {
            if (preferences is null)
            {
                throw new BadRequestException("The request has no body.");
            }

            // --- The validation, before anything is written; the deviations alone are bound ---
            IReadOnlyList<NotificationPreferenceList> deviations = NotificationPreferenceRules.Deviations(preferences, types, channels);

            // --- One Persist batch: the three statements, the caller's preferences tag moved by the epilogue ---
            PersistVerdict.Ensure(accessor.Current, mutation: true, allowWhenReadOnly: true, frames.Open);
            _ = await runner.RunAsync(
                BatchPurpose.Persist,
                (caller, batch) =>
                {
                    _ = NotificationPreferenceRules.Save(batch, userId: null, deviations);
                    return ValueTask.CompletedTask;
                },
                static (_, outcome) => outcome,
                cancellationToken).ConfigureAwait(false);

            metrics.RecordPreferencesSaved();

            // The statements leave exactly the bound rows stored, so the answer is those rows.
            return NotificationPreferenceRules.Stored(deviations);
        }

        /// <summary>The stored rows of the types and channels this instance registers; a removed one's rows are invisible.</summary>
        /// <param name="set">The read's result set: type, channel, enabled.</param>
        /// <returns>The preferences, by type and then channel.</returns>
        private NotificationPreference[] Known(QueryRowSet set)
        {
            return NotificationPreferenceRules.Known(set, types, channels);
        }
    }

    // Spec 0021 §4.3's rules and statements, shared with spec 0018 §3.5's administrative routes.
    // The caller's own rows are written against the connect prologue's @tm_UserId with
    // SqlOptions.ForCaller; another user's bind the target's id as a parameter and name it in a
    // one-row IdList, so the epilogue moves that user's PreferencesTag either way. Another user's set
    // is replaced under an update lock on that user's stamp row, taken first, so it serialises with
    // the owner's own save (whose re-check holds that row from the batch's head) and with any other
    // administrator's.
    /// <summary>The rules and statements of a user's notification preferences, which every path that reads or saves them runs.</summary>
    internal static class NotificationPreferenceRules
    {
        /// <summary>The table a save writes, whose user-level tag rule moves the user's preferences tag.</summary>
        public static readonly FrozenSet<TableName> Written = new[] { NotificationStatements.Preferences }.ToFrozenSet();

        /// <summary>
        ///     Validates a user's complete set and returns the rows to store: those that differ from
        ///     their channel's default.
        /// </summary>
        /// <param name="preferences">The complete set, as sent.</param>
        /// <param name="types">The registered notification types.</param>
        /// <param name="channels">The registered channels.</param>
        /// <returns>The deviations, in the order sent.</returns>
        /// <exception cref="ValidationException">
        ///     A row names an unknown type or channel, repeats an earlier row's type and channel, or
        ///     turns the inbox off for a type that cannot be muted.
        /// </exception>
        public static IReadOnlyList<NotificationPreferenceList> Deviations(
            IReadOnlyList<NotificationPreference?> preferences, INotificationTypeRegistry types, INotificationChannelRegistry channels)
        {
            ValidationErrors errors = new();
            IReadOnlyList<NotificationPreferenceList> deviations = Check(preferences, types, channels, errors, ValidationPath.Root);
            return errors.HasErrors ? throw new ValidationException(errors.Items) : deviations;
        }

        /// <summary>
        ///     Validates a user's complete set into a list of errors, each at its row under a path, and
        ///     returns the rows to store: those that differ from their channel's default.
        /// </summary>
        /// <param name="preferences">The complete set, as sent.</param>
        /// <param name="types">The registered notification types.</param>
        /// <param name="channels">The registered channels.</param>
        /// <param name="errors">Collects a row naming an unknown type or channel, repeating an earlier row's pair, or muting the inbox of a type that cannot be muted.</param>
        /// <param name="path">The path of the set, under which each row's errors land.</param>
        /// <returns>The deviations of the valid rows, in the order sent.</returns>
        public static IReadOnlyList<NotificationPreferenceList> Check(
            IReadOnlyList<NotificationPreference?> preferences,
            INotificationTypeRegistry types,
            INotificationChannelRegistry channels,
            ValidationErrors errors,
            ValidationPath path)
        {
            ArgumentNullException.ThrowIfNull(preferences);
            ArgumentNullException.ThrowIfNull(types);
            ArgumentNullException.ThrowIfNull(channels);
            ArgumentNullException.ThrowIfNull(errors);
            ArgumentNullException.ThrowIfNull(path);

            List<NotificationPreferenceList> deviations = [];
            HashSet<(string Type, string Channel)> seen = [];
            for (int index = 0; index < preferences.Count; index++)
            {
                NotificationPreference? row = preferences[index];
                ValidationPath at = path.Index(index);

                // --- The type and the channel, each registered on this instance ---
                NotificationTypeDescriptor? type = row?.Type is string typeKey ? types.Find(typeKey) : null;
                NotificationChannelDescriptor? channel = row?.Channel is string channelKey ? channels.Find(channelKey) : null;
                if (type is null)
                {
                    Add(errors, at.Property(nameof(NotificationPreference.Type)), NotificationValidationCodes.UnknownType, ("type", row?.Type));
                }

                if (row is not null && channel is null)
                {
                    Add(errors, at.Property(nameof(NotificationPreference.Channel)), NotificationValidationCodes.UnknownChannel, ("channel", row.Channel));
                }

                if (row is null || type is null || channel is null)
                {
                    continue;
                }

                // --- Each pair once ---
                if (!seen.Add((type.Key, channel.Key)))
                {
                    Add(errors, at.Property(nameof(NotificationPreference.Channel)), NotificationValidationCodes.DuplicatePreference, ("type", type.Key), ("channel", channel.Key));
                    continue;
                }

                // --- The inbox of a type that cannot be muted stays on ---
                if (!row.Enabled && !type.Mutable && string.Equals(channel.Key, Composition.CoreFeature.InboxChannel, StringComparison.Ordinal))
                {
                    Add(errors, at.Property(nameof(NotificationPreference.Enabled)), NotificationValidationCodes.CannotMute, ("type", type.Key));
                    continue;
                }

                // --- A row equal to the channel's default is not stored ---
                if (row.Enabled != channel.DefaultEnabled)
                {
                    deviations.Add(new NotificationPreferenceList { Type = type.Key, Channel = channel.Key, Enabled = row.Enabled });
                }
            }

            return deviations;
        }

        /// <summary>Appends the read of a user's stored rows: type, channel, enabled.</summary>
        /// <param name="batch">The batch, a Read batch.</param>
        /// <param name="userId">Another user, bound as a parameter; null for the caller, the prologue's <c>@tm_UserId</c>.</param>
        /// <returns>The read: one result set.</returns>
        public static BatchResult<RawResult> Read(IDataBatch batch, int? userId)
        {
            ArgumentNullException.ThrowIfNull(batch);
            SqlOptions options = new(idempotent: true, resultSets: 1);
            return userId is int id
                ? batch.Sql($"SELECT [Type], [Channel], [Enabled] FROM [core].[NotificationPreferences] WHERE [UserId] = {id};", options)
                : batch.Sql($"SELECT [Type], [Channel], [Enabled] FROM [core].[NotificationPreferences] WHERE [UserId] = @tm_UserId;", options);
        }

        /// <summary>
        ///     Appends the replacement of a user's stored rows by the deviations: a stored pair absent
        ///     from them deleted, a changed one updated, a new one inserted. The user's preferences tag
        ///     moves with the batch's epilogue.
        /// </summary>
        /// <param name="batch">The batch, a Persist batch.</param>
        /// <param name="userId">Another user, bound as a parameter; null for the caller, the prologue's <c>@tm_UserId</c>.</param>
        /// <param name="deviations">The rows to store, each pair once.</param>
        /// <returns>The statement, which reads nothing.</returns>
        public static BatchResult<RawResult> Save(IDataBatch batch, int? userId, IReadOnlyList<NotificationPreferenceList> deviations)
        {
            ArgumentNullException.ThrowIfNull(batch);
            ArgumentNullException.ThrowIfNull(deviations);
            SqlIdentifier rows = batch.Tvp(deviations);
            if (userId is int id)
            {
                // Another user's set: that user's stamp row is held under an update lock first.
                SqlIdentifier owner = batch.DeclareIdTable();
                return batch.Sql(
                    $"""
                    INSERT INTO {owner} ([Id]) SELECT [UserId] FROM [core].[UserStamps] WITH (UPDLOCK, ROWLOCK) WHERE [UserId] = {id};
                    DELETE P FROM [core].[NotificationPreferences] AS P
                    WHERE P.[UserId] = {id} AND NOT EXISTS (SELECT 1 FROM {rows} AS I WHERE I.[Type] = P.[Type] AND I.[Channel] = P.[Channel]);
                    UPDATE P SET P.[Enabled] = I.[Enabled]
                    FROM [core].[NotificationPreferences] AS P JOIN {rows} AS I ON I.[Type] = P.[Type] AND I.[Channel] = P.[Channel]
                    WHERE P.[UserId] = {id} AND P.[Enabled] <> I.[Enabled];
                    INSERT INTO [core].[NotificationPreferences] ([UserId], [Type], [Channel], [Enabled])
                    SELECT {id}, I.[Type], I.[Channel], I.[Enabled] FROM {rows} AS I
                    WHERE NOT EXISTS (SELECT 1 FROM [core].[NotificationPreferences] AS P WHERE P.[UserId] = {id} AND P.[Type] = I.[Type] AND P.[Channel] = I.[Channel]);
                    """,
                    new SqlOptions(Written, userIds: batch.Tvp([new IdList { Id = id }])));
            }

            return batch.Sql(
                $"""
                DELETE P FROM [core].[NotificationPreferences] AS P
                WHERE P.[UserId] = @tm_UserId AND NOT EXISTS (SELECT 1 FROM {rows} AS I WHERE I.[Type] = P.[Type] AND I.[Channel] = P.[Channel]);
                UPDATE P SET P.[Enabled] = I.[Enabled]
                FROM [core].[NotificationPreferences] AS P JOIN {rows} AS I ON I.[Type] = P.[Type] AND I.[Channel] = P.[Channel]
                WHERE P.[UserId] = @tm_UserId AND P.[Enabled] <> I.[Enabled];
                INSERT INTO [core].[NotificationPreferences] ([UserId], [Type], [Channel], [Enabled])
                SELECT @tm_UserId, I.[Type], I.[Channel], I.[Enabled] FROM {rows} AS I
                WHERE NOT EXISTS (SELECT 1 FROM [core].[NotificationPreferences] AS P WHERE P.[UserId] = @tm_UserId AND P.[Type] = I.[Type] AND P.[Channel] = I.[Channel]);
                """,
                SqlOptions.ForCaller(Written));
        }

        /// <summary>The stored rows of the types and channels this instance registers; a removed one's rows are invisible.</summary>
        /// <param name="set">The read's result set: type, channel, enabled.</param>
        /// <param name="types">The registered notification types.</param>
        /// <param name="channels">The registered channels.</param>
        /// <returns>The preferences, by type and then channel.</returns>
        public static NotificationPreference[] Known(QueryRowSet set, INotificationTypeRegistry types, INotificationChannelRegistry channels)
        {
            ArgumentNullException.ThrowIfNull(set);
            ArgumentNullException.ThrowIfNull(types);
            ArgumentNullException.ThrowIfNull(channels);
            string[] typeKeys = (string[])set.GetBuffer(0);
            string[] channelKeys = (string[])set.GetBuffer(1);
            bool[] enabled = (bool[])set.GetBuffer(2);
            return Sorted(Enumerable.Range(0, set.RowCount)
                .Where(row => types.Find(typeKeys[row]) is not null && channels.Find(channelKeys[row]) is not null)
                .Select(row => new NotificationPreference { Type = typeKeys[row], Channel = channelKeys[row], Enabled = enabled[row] }));
        }

        /// <summary>The rows a save stored, as a read answers them.</summary>
        /// <param name="deviations">The rows the save bound.</param>
        /// <returns>The preferences, by type and then channel.</returns>
        public static NotificationPreference[] Stored(IReadOnlyList<NotificationPreferenceList> deviations)
        {
            ArgumentNullException.ThrowIfNull(deviations);
            return Sorted(deviations.Select(static row => new NotificationPreference { Type = row.Type, Channel = row.Channel, Enabled = row.Enabled }));
        }

        /// <summary>Orders preferences by type and then channel, ordinally, so every path answers alike.</summary>
        /// <param name="preferences">The preferences.</param>
        /// <returns>The ordered list.</returns>
        private static NotificationPreference[] Sorted(IEnumerable<NotificationPreference> preferences)
        {
            return [.. preferences.OrderBy(static row => row.Type, StringComparer.Ordinal).ThenBy(static row => row.Channel, StringComparer.Ordinal)];
        }

        /// <summary>Collects a refusal at a path, with its arguments.</summary>
        /// <param name="errors">The errors.</param>
        /// <param name="path">The path.</param>
        /// <param name="code">The code.</param>
        /// <param name="arguments">The arguments.</param>
        private static void Add(ValidationErrors errors, ValidationPath path, string code, params (string Name, object? Value)[] arguments)
        {
            errors.Add(path, code, arguments.ToDictionary(static argument => argument.Name, static argument => argument.Value, StringComparer.Ordinal));
        }
    }
}
