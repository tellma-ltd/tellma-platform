// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Logging;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Caching;
using Tellma.Core.Data;
using Tellma.Core.Data.Batches;
using Tellma.Core.Tenancy;

namespace Tellma.Core.Access
{
    // The runner is spec 0014 §7.1's Run with §7.6's failure modes, and spec 0013 §2.6's Rerun
    // policy: one recompose per call, from the fresh state the failed round trip returned.
    /// <summary>
    ///     Runs a batch on the scope's caller's behalf under the connect prologue's guard. A guard that
    ///     fails, a persist whose re-check finds a moved tag, and a read or validation whose re-run
    ///     input moved each cost one recompose; a second in the same call is refused as stale.
    /// </summary>
    /// <param name="connection">The scope's connection.</param>
    /// <param name="database">The scope's tenant database handle, which creates the batches.</param>
    /// <param name="holder">The scope's request context, which the initializers after the connection complete again after fresh settings.</param>
    /// <param name="initializers">The scope's request-context initializers.</param>
    /// <param name="metrics">The access instruments.</param>
    /// <param name="loggerFactory">Creates the access logger.</param>
    /// <param name="tagMetrics">The version-tag instruments, which meter an exhausted re-run; null in a composition without them.</param>
    /// <param name="recompositions">Receives the cause of every re-entered compose; null in a composition that counts none.</param>
    /// <param name="evaluator">The scope's evaluator, which decides the scope's required grants again before a recompose; null in a composition without one.</param>
    internal sealed class GuardedBatchRunner(
        IScopeConnection connection,
        ITenantDatabase database,
        IRequestContextHolder holder,
        IEnumerable<IRequestContextInitializer> initializers,
        AccessMetrics metrics,
        ILoggerFactory loggerFactory,
        VersionTagMetrics? tagMetrics = null,
        RecomposeLog? recompositions = null,
        AccessEvaluator? evaluator = null) : IGuardedBatchRunner
    {
        /// <summary>The access logger.</summary>
        private readonly ILogger _logger = (loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory))).CreateLogger(AccessEvents.Category);

        /// <inheritdoc />
        public async Task<TResult> RunAsync<TResult>(
            BatchPurpose purpose,
            Func<ConnectedUser, IDataBatch, ValueTask> compose,
            Func<ConnectedUser, BatchOutcome, TResult> read,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(compose);
            ArgumentNullException.ThrowIfNull(read);

            // A compose that may perform no I/O refuses the round trip before the connection is asked.
            ComposeFence.ThrowIfClosed();
            if (purpose == BatchPurpose.Maintenance || !Enum.IsDefined(purpose))
            {
                throw new ArgumentOutOfRangeException(nameof(purpose), purpose, "A guarded batch is a read, a validation or a persist; a maintenance batch carries no connect prologue.");
            }

            // --- The connected user: cached, or one prologue-only round trip ---
            ConnectedUser user = await connection.CurrentAsync(cancellationToken).ConfigureAwait(false);
            bool recomposed = false;
            while (true)
            {
                // --- A recompose runs under the fresh set: a grant the scope required and that set no
                // longer holds refuses the batch before it is composed again (spec 0015 §5.1) ---
                if (recomposed)
                {
                    evaluator?.EnsureRequiredHold(user);
                }

                // --- Compose: the prologue first, then the caller's statements ---
                IDataBatch batch = database.CreateBatch(purpose);
                ConnectPremises premises = connection.PremisesFor(user);
                BatchResult<ConnectResult> prologue = connection.Contribute(batch, premises);
                await compose(user, batch).ConfigureAwait(false);

                // --- Execute: a moved tag inside the persist transaction, or a moved re-run input, is one failure ---
                BatchOutcome outcome;
                try
                {
                    outcome = await batch.ExecuteAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (BatchAssertionFailedException refused) when (refused.Number == TellmaSqlErrors.CallerInvalid)
                {
                    // A deactivation committed between the prologue and the persist's transaction.
                    connection.Drop(user);
                    throw new TenantNotFoundException(holder.Current.RequireTenant().Id);
                }
                catch (BatchAssertionFailedException stale) when (stale.Number == TellmaSqlErrors.StaleVersionTag)
                {
                    // The guard passed before the re-check refused the persist: what the prologue read is
                    // applied and counted like any other round trip's.
                    if (connection.ReadOf(prologue) is ConnectResult readBefore)
                    {
                        user = connection.Apply(readBefore, user);
                    }

                    if (recomposed)
                    {
                        throw Thrash(user, purpose, new StaleContextException([]));
                    }

                    // The guard names no tag: a cold re-connect returns every fresh part.
                    recomposed = true;
                    recompositions?.Note(RecomposeCause.Guard);
                    user = await connection.ReconnectColdAsync(cancellationToken).ConfigureAwait(false);
                    await RefreshAsync(user, cancellationToken).ConfigureAwait(false);
                    holder.Current.ApplyCultures();
                    continue;
                }
                catch (StaleContextException stale)
                {
                    // A read or validation whose cached input moved: the guard passed and the body ran,
                    // so what the prologue read (the validated set, fresh rows or profile, the tags the
                    // executor's snapshot now holds) is applied before the batch is recomposed.
                    if (connection.ReadOf(prologue) is ConnectResult readBefore)
                    {
                        user = connection.Apply(readBefore, user);
                    }

                    if (recomposed)
                    {
                        throw Thrash(user, purpose, stale);
                    }

                    recomposed = true;
                    recompositions?.Note(Cause(stale.Dependencies));
                    continue;
                }

                // --- The prologue's result: applied whether or not the guard passed ---
                ConnectResult result = prologue.Value;
                user = connection.Apply(result, user);
                if (result.GuardPassed)
                {
                    return read(user, outcome);
                }

                // --- A failed guard: the fresh state rode the failed round trip, so recompose once ---
                if (recomposed)
                {
                    throw Thrash(user, purpose, new StaleContextException(Stale(premises, result)));
                }

                recomposed = true;
                recompositions?.Note(result.PermissionsStale || !result.SettingsStale ? RecomposeCause.Permissions : RecomposeCause.Settings);
                await RefreshAsync(user, cancellationToken).ConfigureAwait(false);

                // The thread cultures follow the refreshed context for the rest of this call: the
                // recomposed batch and the read render in it. A culture set inside the refresh would
                // not flow back here, so it is applied in this method.
                holder.Current.ApplyCultures();
            }
        }

        /// <summary>The cause a stale read or validation names: the permissions or settings tag when it lists one, else a list's.</summary>
        /// <param name="dependencies">The dependencies the refusal names.</param>
        /// <returns>The cause.</returns>
        private static RecomposeCause Cause(IReadOnlyList<VersionTagDependency> dependencies)
        {
            return dependencies.Any(static dependency => dependency.Name == VersionTagNames.Permissions) ? RecomposeCause.Permissions
                : dependencies.Any(static dependency => dependency.Name == VersionTagNames.Settings) ? RecomposeCause.Settings
                : RecomposeCause.Entity;
        }

        /// <summary>The <c>settings</c> tag a connected user's batch composes under.</summary>
        /// <param name="user">The connected user.</param>
        /// <returns>The tag; none when the tenant tags hold none.</returns>
        private static Guid SettingsTag(ConnectedUser user)
        {
            return user.TenantTags.TryGetValue(VersionTagNames.Settings, out Guid tag) ? tag : Guid.Empty;
        }

        /// <summary>The inputs a failed guard found stale, for the refusal: the permissions, the settings, or the user itself.</summary>
        /// <param name="premises">The premises the batch was composed under.</param>
        /// <param name="result">The prologue's result.</param>
        /// <returns>The stale inputs.</returns>
        private static List<VersionTagDependency> Stale(ConnectPremises premises, ConnectResult result)
        {
            (Guid? permissions, Guid settings) = premises switch
            {
                ConnectPremises.ForSubject subject => (subject.Expected.PermissionsTag, subject.Expected.SettingsTag),
                ConnectPremises.ForUser user => (user.Expected.PermissionsTag, user.Expected.SettingsTag),
                ConnectPremises.ForSystem system => (null, system.ExpectedSettingsTag),
                _ => (null, Guid.Empty),
            };

            List<VersionTagDependency> stale = [];
            if (result.SettingsStale)
            {
                stale.Add(new VersionTagDependency(VersionTagNames.Settings, new VersionTag(settings), VersionTagMismatchPolicy.Rerun));
            }

            // The permissions, also when the guard failed on the user alone: the set is the caller's.
            if (result.PermissionsStale || stale.Count == 0)
            {
                stale.Insert(0, new VersionTagDependency(VersionTagNames.Permissions, new VersionTag(permissions ?? Guid.Empty), VersionTagMismatchPolicy.Rerun));
            }

            return stale;
        }

        /// <summary>
        ///     Completes the context again when the round trip brought settings newer than the ones it
        ///     carries: the initializers after the connection re-run, so the recomposed batch sees the
        ///     new context. A context that carries no settings yet composes under the store's.
        /// </summary>
        /// <param name="user">The connected user the round trip returned, with the tenant's current tags.</param>
        /// <param name="cancellationToken">Cancels the initializers.</param>
        /// <returns>A task that completes when the context is complete again.</returns>
        private async Task RefreshAsync(ConnectedUser user, CancellationToken cancellationToken)
        {
            RequestContext context = holder.Current;
            if (context.Tenant is null || context.TenantSettings is not { } settings || settings.Tag.Value == SettingsTag(user))
            {
                return;
            }

            _ = await RequestContextInitialization.RunAsync(
                holder,
                initializers.Where(static initializer => initializer.Order > ConnectInitializer.ConnectOrder),
                context,
                connection.Inputs,
                cancellationToken).ConfigureAwait(false);
        }

        /// <summary>Counts, meters and logs a second failure in one call, and returns its refusal.</summary>
        /// <param name="user">The connected user.</param>
        /// <param name="purpose">The batch's purpose.</param>
        /// <param name="refusal">The refusal.</param>
        /// <returns>The refusal.</returns>
        private StaleContextException Thrash(ConnectedUser user, BatchPurpose purpose, StaleContextException refusal)
        {
            metrics.CountConnect(AccessTelemetryNames.ResultGuardThrash);
            tagMetrics?.RecordStale(VersionTagMetrics.ExhaustedPhase);
            AccessLog.GuardThrash(_logger, holder.Current.Tenant?.Id ?? 0, user.UserId, purpose.ToString());
            return refusal;
        }
    }
}
