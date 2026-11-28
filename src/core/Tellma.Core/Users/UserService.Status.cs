// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Identity;
using Tellma.Core.Abstractions.TableTypes;
using Tellma.Core.Access;
using Tellma.Core.Queryex;

namespace Tellma.Core.Users
{
    // Spec 0018 §3.4: one guarded read of the requested users under the caller's core.User × Read
    // decision (ids the caller cannot see are omitted, as get-by-ids omits them, so the answer is no
    // existence oracle), then one delivery call for the subjects whose invitation email the identity
    // server raised; no writes.
    public partial class UserService<TUser>
    {
        /// <summary>The columns the invitation status reads.</summary>
        private const string StatusSelect =
            nameof(User.Id) + "," + nameof(User.State) + "," + nameof(User.Subject) + "," + nameof(User.InviteStatus) + "," + nameof(User.LastInviteError);

        /// <summary>
        ///     Reads how far each requested user's invitation got: the stored state, the identity
        ///     server's last answer and refusal, and the delivery of the invitation email. A user the
        ///     caller cannot read is omitted.
        /// </summary>
        /// <param name="request">The users' ids.</param>
        /// <param name="cancellationToken">Cancels the read and the delivery call.</param>
        /// <returns>The visible users' statuses, in request order, each once.</returns>
        /// <exception cref="LimitExceededException">More ids than a request may carry.</exception>
        [ApiAction("invitation-status", Action = AccessActions.Read, Idempotent = true, Mutation = false)]
        public async Task<IReadOnlyList<InvitationStatus>> GetInvitationStatusAsync(IdsRequest request, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(request.Ids);
            int max = Descriptor.Limits.MaxIds;
            if (request.Ids.Count > max)
            {
                throw new LimitExceededException(LimitNames.MaxIds, request.Ids.Count, max);
            }

            // --- The requested ids, each once; one outside the key's range names no user ---
            int[] ids = [.. request.Ids.Where(static id => id is > 0 and <= int.MaxValue).Select(static id => (int)id).Distinct()];
            if (ids.Length == 0)
            {
                return [];
            }

            // --- One guarded read under the caller's read decision ---
            BatchResult<EntityQueryResult<TUser>>? read = null;
            IReadOnlyList<TUser> rows = await _dependencies.Runner.RunAsync(
                BatchPurpose.Read,
                (user, batch) =>
                {
                    read = ComposeVisibleRead(user, batch, AccessActions.Read, ids, StatusSelect, "invitation status");
                    return ValueTask.CompletedTask;
                },
                (_, _) => read?.Value.Entities ?? [],
                cancellationToken).ConfigureAwait(false);

            // --- One delivery call for the invitations the identity server emailed ---
            var byId = rows.ToDictionary(static user => user.Id);
            List<TUser> emailed = [.. ids.Where(byId.ContainsKey).Select(id => byId[id]).Where(static user =>
                user.State == UserState.Invited && user.InviteStatus != InviteStatus.Active && !string.IsNullOrEmpty(user.Subject))];
            Dictionary<string, IdentityDeliveryStatus> deliveries = new(StringComparer.Ordinal);
            if (emailed.Count > 0)
            {
                string[] subjects = [.. emailed.Select(static user => user.Subject!).Distinct(StringComparer.Ordinal)];
                IReadOnlyList<IdentityDeliveryStatus> answered = await _dependencies.Identity
                    .GetInvitationDeliveryAsync(subjects, cancellationToken).ConfigureAwait(false);

                // A prefix leaves the unanswered subjects without a delivery; an answer is kept only
                // for the subject it was asked for.
                for (int index = 0; index < answered.Count && index < subjects.Length; index++)
                {
                    if (string.Equals(answered[index].Subject, subjects[index], StringComparison.Ordinal))
                    {
                        deliveries[subjects[index]] = answered[index];
                    }
                }
            }

            // --- The answer, in request order ---
            return [.. ids.Where(byId.ContainsKey).Select(id => Status(byId[id], deliveries))];
        }

        /// <summary>
        ///     One user's invitation status. A new user raised nothing; an account that already held a
        ///     credential needed no email, which the delivery reports as not found; a joined user's
        ///     delivery is moot; any other invited user carries the identity server's answer.
        /// </summary>
        /// <param name="user">The user, as read.</param>
        /// <param name="deliveries">The identity server's answers, by subject.</param>
        /// <returns>The status.</returns>
        internal static InvitationStatus Status(User user, IReadOnlyDictionary<string, IdentityDeliveryStatus> deliveries)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(deliveries);
            IdentityDeliveryStatus? delivery = user.State == UserState.New ? null
                : user.InviteStatus == InviteStatus.Active ? (user.Subject is string subject ? new IdentityDeliveryStatus(subject, IdentityDeliveryState.NotFound, false, null, null, null) : null)
                : user.State == UserState.Joined ? null
                : user.Subject is string invited ? deliveries.GetValueOrDefault(invited)
                : null;
            return new InvitationStatus(user.Id, user.State, user.InviteStatus, user.LastInviteError, delivery);
        }

        /// <summary>
        ///     Composes a read of the requested users that the caller's decision on an action of the
        ///     users admits, in id order; nothing when it denies.
        /// </summary>
        /// <param name="user">The connected caller.</param>
        /// <param name="batch">The batch.</param>
        /// <param name="action">The action whose decision filters the users, such as <c>Read</c>.</param>
        /// <param name="ids">The requested ids.</param>
        /// <param name="select">The columns read.</param>
        /// <param name="purpose">What the read is for, which a filter too complex to compile names.</param>
        /// <returns>The read; null when the caller holds no grant of the action.</returns>
        /// <exception cref="InvalidOperationException">The caller's filter exceeds the compile ceilings.</exception>
        private BatchResult<EntityQueryResult<TUser>>? ComposeVisibleRead(ConnectedUser user, IDataBatch batch, string action, IReadOnlyList<int> ids, string select, string purpose)
        {
            AccessDecision decision = _dependencies.Securables.Find(CoreResources.User, action) is SecurableDescriptor securable
                ? _dependencies.Evaluator.DecideFrom(user.Access, securable, _dependencies.Time.GetUtcNow())
                : _dependencies.Evaluator.Unknown(CoreResources.User, action);
            if (!decision.IsAllowed)
            {
                return null;
            }

            SqlIdentifier keys = batch.Tvp([.. ids.Select(static id => new IdList { Id = id })]);
            try
            {
                return batch.Query(new EntityQuery<TUser>
                {
                    Select = select,
                    Filter = decision.Filter,
                    Restrictions = [new KeySetRestriction(nameof(User.Id), keys.Name)],
                    OrderBy = nameof(User.Id),
                });
            }
            catch (ArgumentException tooComplex) when (decision.Outcome == AccessOutcome.Filtered)
            {
                // An administrator's configuration problem: a plain error, never a silent denial.
                string involved = string.Join(", ", decision.Grants.OfType<RoleAccessGrant>().Select(static grant => grant.RoleId).Distinct().Order());
                AccessLog.FilterTooComplex(_dependencies.Logger, tooComplex, user.Access.TenantId, user.UserId, decision.Resource, decision.Action, involved);
                throw new InvalidOperationException($"The caller's filter on the users exceeded the compile ceilings, so the {purpose} cannot be read.", tooComplex);
            }
        }
    }
}
