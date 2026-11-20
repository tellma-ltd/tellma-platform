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
        /// <summary>The table a save writes, whose user-level tag rule moves the caller's preferences tag.</summary>
        private static readonly FrozenSet<TableName> Written = new[] { NotificationStatements.Preferences }.ToFrozenSet();

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
                    found = batch.Sql(
                        $"SELECT [Type], [Channel], [Enabled] FROM [core].[NotificationPreferences] WHERE [UserId] = @tm_UserId;",
                        new SqlOptions(resultSets: 1));
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
                    SqlIdentifier rows = batch.Tvp(deviations);
                    _ = batch.Sql(
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
                    return ValueTask.CompletedTask;
                },
                static (_, outcome) => outcome,
                cancellationToken).ConfigureAwait(false);

            metrics.RecordPreferencesSaved();

            // The statements leave exactly the bound rows stored, so the answer is those rows.
            return Sorted(deviations.Select(static row => new NotificationPreference { Type = row.Type, Channel = row.Channel, Enabled = row.Enabled }));
        }

        /// <summary>The stored rows of the types and channels this instance registers; a removed one's rows are invisible.</summary>
        /// <param name="set">The read's result set: type, channel, enabled.</param>
        /// <returns>The preferences, by type and then channel.</returns>
        private NotificationPreference[] Known(QueryRowSet set)
        {
            string[] typeKeys = (string[])set.GetBuffer(0);
            string[] channelKeys = (string[])set.GetBuffer(1);
            bool[] enabled = (bool[])set.GetBuffer(2);
            return Sorted(Enumerable.Range(0, set.RowCount)
                .Where(row => types.Find(typeKeys[row]) is not null && channels.Find(channelKeys[row]) is not null)
                .Select(row => new NotificationPreference { Type = typeKeys[row], Channel = channelKeys[row], Enabled = enabled[row] }));
        }

        /// <summary>Orders preferences by type and then channel, ordinally, so both actions answer alike.</summary>
        /// <param name="preferences">The preferences.</param>
        /// <returns>The ordered list.</returns>
        private static NotificationPreference[] Sorted(IEnumerable<NotificationPreference> preferences)
        {
            return [.. preferences.OrderBy(static row => row.Type, StringComparer.Ordinal).ThenBy(static row => row.Channel, StringComparer.Ordinal)];
        }
    }

    /// <summary>The rules of a save of a user's notification preferences, which every path that saves them runs.</summary>
    internal static class NotificationPreferenceRules
    {
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
            ArgumentNullException.ThrowIfNull(preferences);
            ArgumentNullException.ThrowIfNull(types);
            ArgumentNullException.ThrowIfNull(channels);

            List<ValidationError> errors = [];
            List<NotificationPreferenceList> deviations = [];
            HashSet<(string Type, string Channel)> seen = [];
            for (int index = 0; index < preferences.Count; index++)
            {
                NotificationPreference? row = preferences[index];
                ValidationPath path = ValidationPath.Root.Index(index);

                // --- The type and the channel, each registered on this instance ---
                NotificationTypeDescriptor? type = row?.Type is string typeKey ? types.Find(typeKey) : null;
                NotificationChannelDescriptor? channel = row?.Channel is string channelKey ? channels.Find(channelKey) : null;
                if (type is null)
                {
                    errors.Add(Error(path.Property(nameof(NotificationPreference.Type)), NotificationValidationCodes.UnknownType, ("type", row?.Type)));
                }

                if (row is not null && channel is null)
                {
                    errors.Add(Error(path.Property(nameof(NotificationPreference.Channel)), NotificationValidationCodes.UnknownChannel, ("channel", row.Channel)));
                }

                if (row is null || type is null || channel is null)
                {
                    continue;
                }

                // --- Each pair once ---
                if (!seen.Add((type.Key, channel.Key)))
                {
                    errors.Add(Error(path.Property(nameof(NotificationPreference.Channel)), NotificationValidationCodes.DuplicatePreference, ("type", type.Key), ("channel", channel.Key)));
                    continue;
                }

                // --- The inbox of a type that cannot be muted stays on ---
                if (!row.Enabled && !type.Mutable && string.Equals(channel.Key, Composition.CoreFeature.InboxChannel, StringComparison.Ordinal))
                {
                    errors.Add(Error(path.Property(nameof(NotificationPreference.Enabled)), NotificationValidationCodes.CannotMute, ("type", type.Key)));
                    continue;
                }

                // --- A row equal to the channel's default is not stored ---
                if (row.Enabled != channel.DefaultEnabled)
                {
                    deviations.Add(new NotificationPreferenceList { Type = type.Key, Channel = channel.Key, Enabled = row.Enabled });
                }
            }

            return errors.Count > 0 ? throw new ValidationException(errors) : deviations;
        }

        /// <summary>A refusal at a path, with its arguments.</summary>
        /// <param name="path">The path.</param>
        /// <param name="code">The code.</param>
        /// <param name="arguments">The arguments.</param>
        /// <returns>The error.</returns>
        private static ValidationError Error(ValidationPath path, string code, params (string Name, object? Value)[] arguments)
        {
            return new ValidationError(path, code, arguments.ToDictionary(static argument => argument.Name, static argument => argument.Value, StringComparer.Ordinal));
        }
    }
}
