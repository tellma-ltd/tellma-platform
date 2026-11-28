// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.Access;
using Tellma.Core.Crud;
using Tellma.Core.Data;

namespace Tellma.Core.Users
{
    // Spec 0018 §3.5's preference bags. The caller's own bag is written in one Persist batch through
    // the guarded runner: spec 0014 §3.4's statement against the prologue's @tm_UserId, the caller's
    // tag moved by the epilogue (SqlOptions.ForCaller), then the bag read back. Another user's bag is
    // the administrative path under core.User × Preferences: a get in one Read batch under the
    // grant's filter, and set and delete as single-target entity actions whose method appends the
    // statement for the target and its read-back to the action's persist batch, so that round
    // trip's epilogue moves the target's tag. The items are checked in memory before any write;
    // the key cap is asserted inside the statement.
    public partial class UserService<TUser>
    {
        /// <summary>The name of the caller's own preference set.</summary>
        public const string MePreferencesSetAction = "me/preferences/set";

        /// <summary>The name of the caller's own preference delete.</summary>
        public const string MePreferencesDeleteAction = "me/preferences/delete";

        /// <summary>The name of an administrator's read of another user's preferences.</summary>
        public const string PreferencesGetAction = "preferences/get";

        /// <summary>The name of an administrator's set of another user's preferences.</summary>
        public const string PreferencesSetAction = "preferences/set";

        /// <summary>The name of an administrator's delete of another user's preferences.</summary>
        public const string PreferencesDeleteAction = "preferences/delete";

        /// <summary>The area the administrator's log names for a write of the preference bag.</summary>
        internal const string PreferencesArea = "preferences";

        /// <summary>The path of the arguments of an entity action, under which the arguments' errors land.</summary>
        private const string ArgumentsProperty = "Arguments";

        /// <summary>
        ///     Sets keys of the caller's own preference bag in one round trip: each key's value updated
        ///     where it changed and inserted where absent, every other key kept. A read-only tenant
        ///     admits it.
        /// </summary>
        /// <param name="request">The preferences.</param>
        /// <param name="cancellationToken">Cancels the write.</param>
        /// <returns>The bag after the write and the caller's preferences tag.</returns>
        /// <exception cref="ValidationException">A key breaks the grammar or repeats, a value is too large, or the bag would hold too many keys.</exception>
        /// <exception cref="TenantUnavailableException">The tenant is frozen.</exception>
        [ApiAction(MePreferencesSetAction, MemberOnly = true, AllowWhenReadOnly = true)]
        public async Task<PreferencesResult> SetMyPreferencesAsync(UserPreferencesArguments request, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            UserPreferenceList[] items = Rows(request.Items);
            ValidationErrors errors = new();
            UserPreferenceStatements.Check(items, _dependencies.AccessOptions.MaxPreferenceValueBytes, errors, ValidationPath.Root.Property(nameof(UserPreferencesArguments.Items)));
            ThrowIfInvalid(errors);

            int maxKeys = _dependencies.AccessOptions.MaxPreferenceKeys;
            return await WriteOwnBagAsync(batch => UserPreferenceStatements.Set(batch, userId: null, items, maxKeys), cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        ///     Deletes keys from the caller's own preference bag in one round trip; a key the bag does
        ///     not hold is no error. A read-only tenant admits it.
        /// </summary>
        /// <param name="request">The keys.</param>
        /// <param name="cancellationToken">Cancels the write.</param>
        /// <returns>The bag after the write and the caller's preferences tag.</returns>
        /// <exception cref="ValidationException">A key breaks the grammar or repeats.</exception>
        /// <exception cref="TenantUnavailableException">The tenant is frozen.</exception>
        [ApiAction(MePreferencesDeleteAction, MemberOnly = true, AllowWhenReadOnly = true)]
        public async Task<PreferencesResult> DeleteMyPreferencesAsync(UserPreferenceKeysArguments request, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            IReadOnlyList<string> keys = request.Keys;
            ValidationErrors errors = new();
            UserPreferenceStatements.CheckKeys(keys, errors, ValidationPath.Root.Property(nameof(UserPreferenceKeysArguments.Keys)));
            ThrowIfInvalid(errors);

            return await WriteOwnBagAsync(batch => UserPreferenceStatements.Delete(batch, userId: null, keys), cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        ///     Reads another user's preference bag in one round trip, for a caller who holds the users'
        ///     <c>Preferences</c> grant; a user the grant's filter hides is not found.
        /// </summary>
        /// <param name="request">The user.</param>
        /// <param name="cancellationToken">Cancels the read.</param>
        /// <returns>The user's bag.</returns>
        /// <exception cref="NotFoundException">No user the grant's filter admits has the id.</exception>
        [ApiAction(PreferencesGetAction, Action = AccessActions.Preferences, Idempotent = true, Mutation = false)]
        public async Task<UserPreferencesResult> GetPreferencesAsync(UserPreferencesRequest request, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            int target = request.UserId;
            string[] missing = [target.ToString(CultureInfo.InvariantCulture)];
            if (target <= 0)
            {
                throw new NotFoundException(CoreResources.User, missing);
            }

            // --- One Read batch: the target under the grant's filter, and the target's bag ---
            BatchResult<EntityQueryResult<TUser>>? visible = null;
            BatchResult<RawResult>? bag = null;
            return await _dependencies.Runner.RunAsync(
                BatchPurpose.Read,
                (user, batch) =>
                {
                    visible = ComposeVisibleRead(user, batch, AccessActions.Preferences, [target], nameof(User.Id), "preferences");
                    bag = visible is null ? null : ComposeBagOf(batch, target, idempotent: true);
                    return ValueTask.CompletedTask;
                },
                (_, _) => visible is { Value.Entities.Count: 1 } && bag is { } read
                    ? new UserPreferencesResult(MeEnvelope.Bag(read.Value))
                    : throw new NotFoundException(CoreResources.User, missing),
                cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        ///     Sets keys of another user's preference bag, for a caller who holds the users'
        ///     <c>Preferences</c> grant on the target: each key's value updated where it changed and
        ///     inserted where absent, every other key kept. The target's preferences tag moves, so the
        ///     target's open tabs pick the change up. A read-only tenant admits it.
        /// </summary>
        /// <param name="context">The action's context, over one target.</param>
        /// <param name="arguments">The preferences.</param>
        /// <returns>The target's bag after the write.</returns>
        [EntityAction(PreferencesSetAction, Action = AccessActions.Preferences, SingleTarget = true, AllowWhenReadOnly = true)]
        public async Task<UserPreferencesResult> SetPreferencesAsync(ActionContext<TUser, int> context, UserPreferencesArguments arguments)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(arguments);
            int target = TargetOf(context);
            UserPreferenceList[] items = Rows(arguments.Items);
            int maxKeys = _dependencies.AccessOptions.MaxPreferenceKeys;
            return await WriteBagOfAsync(context, target, batch => UserPreferenceStatements.Set(batch, target, items, maxKeys)).ConfigureAwait(false);
        }

        /// <summary>
        ///     Deletes keys from another user's preference bag, for a caller who holds the users'
        ///     <c>Preferences</c> grant on the target; a key the bag does not hold is no error. The
        ///     target's preferences tag moves. A read-only tenant admits it.
        /// </summary>
        /// <param name="context">The action's context, over one target.</param>
        /// <param name="arguments">The keys.</param>
        /// <returns>The target's bag after the write.</returns>
        [EntityAction(PreferencesDeleteAction, Action = AccessActions.Preferences, SingleTarget = true, AllowWhenReadOnly = true)]
        public async Task<UserPreferencesResult> DeletePreferencesAsync(ActionContext<TUser, int> context, UserPreferenceKeysArguments arguments)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(arguments);
            int target = TargetOf(context);
            IReadOnlyList<string> keys = arguments.Keys;
            return await WriteBagOfAsync(context, target, batch => UserPreferenceStatements.Delete(batch, target, keys)).ConfigureAwait(false);
        }

        /// <summary>
        ///     The checks of an administrative preference write's arguments, before its persist: each
        ///     key's grammar, each value's size and no key named twice, each at the arguments' member.
        /// </summary>
        /// <param name="action">The action's name.</param>
        /// <param name="context">The action's context.</param>
        private void ValidatePreferenceArguments(string action, ActionContext<TUser, int> context)
        {
            ArgumentNullException.ThrowIfNull(action);
            ArgumentNullException.ThrowIfNull(context);

            // The arguments' own presence is the binding's; a null list never reaches the checks.
            ValidationPath arguments = ValidationPath.Root.Property(ArgumentsProperty);
            if (string.Equals(action, PreferencesSetAction, StringComparison.Ordinal) && context.Arguments is UserPreferencesArguments set)
            {
                UserPreferenceStatements.Check(Rows(set.Items), _dependencies.AccessOptions.MaxPreferenceValueBytes, context.Errors, arguments.Property(nameof(UserPreferencesArguments.Items)));
            }
            else if (string.Equals(action, PreferencesDeleteAction, StringComparison.Ordinal) && context.Arguments is UserPreferenceKeysArguments delete)
            {
                UserPreferenceStatements.CheckKeys(delete.Keys, context.Errors, arguments.Property(nameof(UserPreferenceKeysArguments.Keys)));
            }
        }

        /// <summary>
        ///     The rows of a set's items, as the bag's table type holds them; an absent item becomes a
        ///     row without a key or a value, which the checks refuse.
        /// </summary>
        /// <param name="items">The items.</param>
        /// <returns>The rows, in the items' order.</returns>
        internal static UserPreferenceList[] Rows(IReadOnlyList<KeyValueItem?> items)
        {
            ArgumentNullException.ThrowIfNull(items);
            return [.. items.Select(static item => new UserPreferenceList { Key = item?.Key!, Value = item?.Value! })];
        }

        /// <summary>
        ///     Writes the caller's own bag in one Persist batch: the statement, then the bag read back,
        ///     after the tenant's state admitted the write. The key cap's refusal becomes the
        ///     validation error it stands for.
        /// </summary>
        /// <param name="write">Appends the statement.</param>
        /// <param name="cancellationToken">Cancels the write.</param>
        /// <returns>The bag after the write and the caller's preferences tag.</returns>
        /// <exception cref="ValidationException">The bag would hold more keys than allowed.</exception>
        /// <exception cref="TenantUnavailableException">The tenant is frozen.</exception>
        private async Task<PreferencesResult> WriteOwnBagAsync(Func<IDataBatch, BatchResult<RawResult>> write, CancellationToken cancellationToken)
        {
            PersistVerdict.Ensure(_dependencies.Accessor.Current, mutation: true, allowWhenReadOnly: true, _dependencies.Frames.Open);
            BatchResult<RawResult>? bag = null;
            BatchOutcome outcome;
            try
            {
                outcome = await _dependencies.Runner.RunAsync(
                    BatchPurpose.Persist,
                    (caller, batch) =>
                    {
                        _ = write(batch);
                        bag = MeEnvelope.ComposeBag(batch, idempotent: false);
                        return ValueTask.CompletedTask;
                    },
                    static (_, outcome) => outcome,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (BatchAssertionFailedException refused) when (refused.Number == TellmaSqlErrors.Invariant && refused.Code == AccessCodes.TooManyPreferenceKeys)
            {
                // The cap, asserted inside the statement: the request's error, as the administrative
                // path's persist reports it.
                throw new ValidationException([new ValidationError(ValidationPath.Root, AccessCodes.TooManyPreferenceKeys, new Dictionary<string, object?>(StringComparer.Ordinal))]);
            }

            return new PreferencesResult(MeEnvelope.Bag(bag!.Value), VersionTagWire.Preferences(MeEnvelope.CallerTags(outcome.UserVersionTags).Preferences));
        }

        /// <summary>
        ///     Appends a write of another user's bag and its read-back to an administrative action's
        ///     persist batch, waits for the commit, logs the change and answers the bag.
        /// </summary>
        /// <param name="context">The action's context.</param>
        /// <param name="target">The user whose bag it writes.</param>
        /// <param name="write">Appends the statement.</param>
        /// <returns>The target's bag after the write.</returns>
        private async Task<UserPreferencesResult> WriteBagOfAsync(ActionContext<TUser, int> context, int target, Func<IDataBatch, BatchResult<RawResult>> write)
        {
            _ = write(context.Batch);
            BatchResult<RawResult> bag = ComposeBagOf(context.Batch, target, idempotent: false);
            await context.Persisted.ConfigureAwait(false);
            UsersLog.UserPreferencesChangedByAdministrator(_dependencies.Logger, target, PreferencesArea);
            return new UserPreferencesResult(MeEnvelope.Bag(bag.Value));
        }

        /// <summary>Appends the read of a user's preference bag, in key order.</summary>
        /// <param name="batch">The batch.</param>
        /// <param name="userId">The user.</param>
        /// <param name="idempotent">Whether the batch may re-run the read: true in a Read batch.</param>
        /// <returns>The read: one result set of keys and values.</returns>
        private static BatchResult<RawResult> ComposeBagOf(IDataBatch batch, int userId, bool idempotent)
        {
            return batch.Sql(
                $"SELECT [Key], [Value] FROM [core].[UserPreferences] WHERE [UserId] = {userId} ORDER BY [Key];",
                new SqlOptions(idempotent: idempotent, resultSets: 1));
        }

        /// <summary>The one target of a single-target action.</summary>
        /// <param name="context">The action's context.</param>
        /// <returns>The target's id.</returns>
        /// <exception cref="InvalidOperationException">The action names no target or more than one.</exception>
        private static int TargetOf(ActionContext<TUser, int> context)
        {
            return context.Ids is [int only]
                ? only
                : throw new InvalidOperationException("An administrative preference write targets exactly one user.");
        }

        /// <summary>Raises the errors collected, every one at once.</summary>
        /// <param name="errors">The errors.</param>
        /// <exception cref="ValidationException">An error was collected.</exception>
        private static void ThrowIfInvalid(ValidationErrors errors)
        {
            if (errors.HasErrors)
            {
                throw new ValidationException(errors.Items);
            }
        }
    }
}
