// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Access;
using Tellma.Core.Composition;
using Tellma.Core.Data.Batches;
using Tellma.Core.Tenancy;

namespace Tellma.Core.Provisioning
{
    /// <summary>
    ///     Runs a tenant's provisioning steps: every contributed step whose version is ahead of the
    ///     one the tenant database records, in order, each in a system tenant scope of its own under a
    ///     root activity named after it, and records each completion in <c>dbo.__TellmaProvisioning</c>.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The first step that fails stops the run: later steps may build on it, and the next
    ///         migration retries it, because nothing was recorded for it.
    ///     </para>
    ///     <para>
    ///         A completion is recorded after the step's own work committed, in a transaction of its
    ///         own, so a crash or a lost connection between the two runs the step again next time.
    ///         That is safe because a step is idempotent: running it twice leaves what running it once
    ///         does.
    ///     </para>
    ///     <para>
    ///         The administrator bootstrap is the one step the record does not schedule, because its
    ///         work is the administrator a run names, not the tenant's schema: a run that names none
    ///         neither runs nor records it, so it is never recorded as done when it made nobody an
    ///         administrator, and a run that names one runs it whatever the tenant records, which is
    ///         how a later run gives a provisioned tenant its administrator.
    ///     </para>
    ///     <para>
    ///         The tenant database must already list the running model's schema fingerprint, because
    ///         every batch a step sends checks it first.
    ///     </para>
    /// </remarks>
    /// <param name="services">The root services, whose scope creates each step outside any tenant to read its name, order and version.</param>
    /// <param name="scopes">Opens each step's tenant scope.</param>
    /// <param name="connections">Opens the tenant database for the step record.</param>
    /// <param name="record">The realised composition, which lists the steps it runs, each with its factory.</param>
    /// <param name="versions">The running platform version, recorded with each completion.</param>
    /// <param name="logger">The logger.</param>
    internal sealed class ProvisioningStepRunner(
        IServiceProvider services,
        ITenantScopeFactory scopes,
        ITenantConnectionFactory connections,
        TellmaCompositionRecord record,
        DeploymentVersions versions,
        ILogger<ProvisioningStepRunner> logger)
    {
        /// <summary>The subject of the system principal every step runs as.</summary>
        public const string SystemSubject = "system";

        /// <summary>The client name of the system principal every step runs as.</summary>
        public const string SystemClient = "worker";

        /// <summary>
        ///     The snapshot of the system principal the migrator's tenant work runs as: kind
        ///     <see cref="PrincipalKind.System" />, subject <c>system</c>, the system user, client
        ///     <c>worker</c> and no trace parent.
        /// </summary>
        /// <param name="tenantId">The tenant.</param>
        /// <returns>The snapshot.</returns>
        public static RequestContextSnapshot SystemSnapshot(int tenantId)
        {
            return new RequestContextSnapshot(tenantId, PrincipalKind.System, SystemSubject, WellKnownIds.SystemUserId, SystemClient, TraceParent: null);
        }

        /// <summary>
        ///     The steps a run that names no administrator, such as a migration, would run on a tenant:
        ///     those with no record or a recorded version below their own, in order.
        /// </summary>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="cancellationToken">Cancels the read.</param>
        /// <returns>The due steps' names and versions, in the order they run.</returns>
        public async Task<IReadOnlyList<(string Name, int Version)>> DueAsync(int tenantId, CancellationToken cancellationToken)
        {
            IReadOnlyDictionary<string, int> recorded = await ReadRecordedAsync(tenantId, cancellationToken).ConfigureAwait(false);
            return [.. Due(await DescribeAsync().ConfigureAwait(false), recorded, namesAdministrator: false).Select(static step => (step.Name, step.Version))];
        }

        /// <summary>
        ///     Runs every due step of a tenant, in order, and records each completion. The administrator
        ///     bootstrap is due exactly when the run names an administrator.
        /// </summary>
        /// <param name="tenant">The tenant, as the registry describes it.</param>
        /// <param name="isNew">Whether the tenant is being provisioned.</param>
        /// <param name="adminSubject">The first administrator's subject, in Development only.</param>
        /// <param name="adminEmail">The first administrator's email address, or null.</param>
        /// <param name="beforeEachStep">
        ///     Runs before each due step starts, and fails the run by throwing; the migrator confirms
        ///     there that it still holds the tenant's lock. Null for none.
        /// </param>
        /// <param name="cancellationToken">Cancels between steps; a running step completes.</param>
        /// <returns>The names of the steps that ran, in order.</returns>
        /// <exception cref="Exception">A step failed; the steps after it did not run and nothing was recorded for it.</exception>
        public async Task<IReadOnlyList<string>> RunAsync(
            TenantDescriptor tenant,
            bool isNew,
            string? adminSubject,
            string? adminEmail,
            Func<CancellationToken, Task>? beforeEachStep,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(tenant);

            // --- What is due: the record first, then each step's own name, order and version ---
            IReadOnlyDictionary<string, int> recorded = await ReadRecordedAsync(tenant.Id, cancellationToken).ConfigureAwait(false);
            List<StepDescription> steps = await DescribeAsync().ConfigureAwait(false);
            bool namesAdministrator = !string.IsNullOrWhiteSpace(adminEmail);

            // A tenant no run ever bootstrapped has no individual administrator; a run that cannot
            // give it one says so, because nothing else would.
            if (!namesAdministrator && steps.Any(step => step.ForAdministrator && !recorded.ContainsKey(step.Name)))
            {
                ProvisioningLog.AdministratorNotBootstrapped(logger, tenant.Id);
            }

            return await RunInOrderAsync(tenant, Due(steps, recorded, namesAdministrator), isNew, adminSubject, adminEmail, beforeEachStep, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        ///     Runs the administrator bootstrap alone on a tenant that is already provisioned, whatever
        ///     the tenant records, and records its completion; the tenant's other steps are left to
        ///     its migration.
        /// </summary>
        /// <param name="tenant">The tenant, as the registry describes it.</param>
        /// <param name="adminSubject">The administrator's subject, in Development only.</param>
        /// <param name="adminEmail">The administrator's email address.</param>
        /// <param name="beforeEachStep">Runs before the step starts, and fails the run by throwing. Null for none.</param>
        /// <param name="cancellationToken">Cancels before the step; a running step completes.</param>
        /// <returns>The names of the steps that ran.</returns>
        /// <exception cref="ArgumentException">The email address is blank.</exception>
        /// <exception cref="Exception">The step failed, and nothing was recorded for it.</exception>
        public async Task<IReadOnlyList<string>> BootstrapAsync(
            TenantDescriptor tenant,
            string? adminSubject,
            string adminEmail,
            Func<CancellationToken, Task>? beforeEachStep,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(tenant);
            ArgumentException.ThrowIfNullOrWhiteSpace(adminEmail);

            IEnumerable<StepDescription> steps = Ordered((await DescribeAsync().ConfigureAwait(false)).Where(static step => step.ForAdministrator));
            return await RunInOrderAsync(tenant, steps, isNew: false, adminSubject, adminEmail, beforeEachStep, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>Runs the given steps of a tenant, in the order given, and records each completion.</summary>
        /// <param name="tenant">The tenant.</param>
        /// <param name="steps">The steps, in order.</param>
        /// <param name="isNew">Whether the tenant is being provisioned.</param>
        /// <param name="adminSubject">The first administrator's subject, or null.</param>
        /// <param name="adminEmail">The first administrator's email address, or null.</param>
        /// <param name="beforeEachStep">Runs before each step starts, and fails the run by throwing. Null for none.</param>
        /// <param name="cancellationToken">Cancels between steps.</param>
        /// <returns>The names of the steps that ran, in order.</returns>
        private async Task<IReadOnlyList<string>> RunInOrderAsync(
            TenantDescriptor tenant,
            IEnumerable<StepDescription> steps,
            bool isNew,
            string? adminSubject,
            string? adminEmail,
            Func<CancellationToken, Task>? beforeEachStep,
            CancellationToken cancellationToken)
        {
            List<string> completed = [];
            foreach (StepDescription step in steps)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (beforeEachStep is not null)
                {
                    await beforeEachStep(cancellationToken).ConfigureAwait(false);
                }

                try
                {
                    await RunStepAsync(tenant, step, isNew, adminSubject, adminEmail, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
                {
                    ProvisioningLog.StepFailed(logger, tenant.Id, step.Name, exception);
                    throw;
                }

                // --- The completion, recorded once the step's work committed; a crash in between re-runs the idempotent step ---
                SqlConnection connection = await connections.OpenAsync(tenant.Id, cancellationToken).ConfigureAwait(false);
                await using (connection.ConfigureAwait(false))
                {
                    await ProvisioningRecords.RecordAsync(connection, step.Name, step.Version, versions.PlatformVersion, cancellationToken).ConfigureAwait(false);
                }

                ProvisioningLog.StepCompleted(logger, tenant.Id, step.Name, step.Version);
                completed.Add(step.Name);
            }

            return completed;
        }

        /// <summary>
        ///     The due steps among those described, in order: the administrator bootstrap when the run
        ///     names an administrator, and every other step with no record or a recorded version below
        ///     its own.
        /// </summary>
        /// <param name="steps">Every contributed step.</param>
        /// <param name="recorded">The completed version of each recorded step.</param>
        /// <param name="namesAdministrator">Whether the run names an administrator.</param>
        /// <returns>The due steps.</returns>
        private static IEnumerable<StepDescription> Due(IEnumerable<StepDescription> steps, IReadOnlyDictionary<string, int> recorded, bool namesAdministrator)
        {
            return Ordered(steps.Where(step => step.ForAdministrator
                ? namesAdministrator
                : !recorded.TryGetValue(step.Name, out int version) || step.Version > version));
        }

        /// <summary>Steps in the order they run: by order, then by name.</summary>
        /// <param name="steps">The steps.</param>
        /// <returns>The steps, ordered.</returns>
        private static IEnumerable<StepDescription> Ordered(IEnumerable<StepDescription> steps)
        {
            return steps
                .OrderBy(static step => step.Order)
                .ThenBy(static step => step.Name, StringComparer.Ordinal);
        }

        /// <summary>Reads the tenant database's record of completed steps.</summary>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="cancellationToken">Cancels the read.</param>
        /// <returns>The completed version of each recorded step.</returns>
        private async Task<IReadOnlyDictionary<string, int>> ReadRecordedAsync(int tenantId, CancellationToken cancellationToken)
        {
            SqlConnection connection = await connections.OpenAsync(tenantId, cancellationToken).ConfigureAwait(false);
            await using (connection.ConfigureAwait(false))
            {
                return await ProvisioningRecords.ReadAsync(connection, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        ///     Every registered step's name, order and version, read from an instance its factory
        ///     created outside any tenant, as the startup gate created it when it validated them.
        /// </summary>
        /// <returns>One description per registered step: a class may stand behind several.</returns>
        private async Task<List<StepDescription>> DescribeAsync()
        {
            AsyncServiceScope scope = services.CreateAsyncScope();
            await using (scope.ConfigureAwait(false))
            {
                return [.. record.ProvisioningSteps.Select(registration =>
                {
                    ITenantProvisioningStep step = registration.Create(scope.ServiceProvider);
                    return new StepDescription(registration, step.Name, step.Order, step.Version, step is BootstrapAdministratorStep);
                })];
            }
        }

        /// <summary>
        ///     Runs one step: a root activity named after it with no parent, the step's own system
        ///     scope opened inside it, its frame opened there, then the step created from that scope by
        ///     its registration's factory, so no other step is created.
        /// </summary>
        /// <param name="tenant">The tenant.</param>
        /// <param name="step">The step's description.</param>
        /// <param name="isNew">Whether the tenant is being provisioned.</param>
        /// <param name="adminSubject">The first administrator's subject, or null.</param>
        /// <param name="adminEmail">The first administrator's email address, or null.</param>
        /// <param name="cancellationToken">Cancels the scope's creation.</param>
        /// <returns>A task that completes when the step completed.</returns>
        private async Task RunStepAsync(
            TenantDescriptor tenant, StepDescription step, bool isNew, string? adminSubject, string? adminEmail, CancellationToken cancellationToken)
        {
            // --- The root activity: whatever the caller was tracing, a step starts a trace of its own ---
            Activity? previous = Activity.Current;
            Activity.Current = null;
            try
            {
                using Activity? activity = DataExecutor.ActivitySource.StartActivity(step.Name, ActivityKind.Internal);
                _ = activity?.SetTag(DataTelemetryNames.TenantIdAttribute, tenant.Id);

                // --- The step's scope, its frame, then the step itself ---
                TenantScope scope = await scopes.CreateScopeAsync(SystemSnapshot(tenant.Id), allowNonActive: true, cancellationToken).ConfigureAwait(false);
                await using (scope.ConfigureAwait(false))
                {
                    scope.Services.GetService<IProvisioningFrameOpener>()?.Open(scope.Services, step.Name);
                    ITenantProvisioningStep instance = step.Registration.Create(scope.Services);
                    await instance.RunAsync(new TenantProvisioningContext(tenant, scope.Services, isNew, adminSubject, adminEmail)).ConfigureAwait(false);
                }
            }
            finally
            {
                Activity.Current = previous;
            }
        }

        /// <summary>One registered step as its instance describes itself.</summary>
        /// <param name="Registration">The registration, whose factory creates the step.</param>
        /// <param name="Name">The step's name.</param>
        /// <param name="Order">The step's order.</param>
        /// <param name="Version">The step's version.</param>
        /// <param name="ForAdministrator">Whether the step is the administrator bootstrap, which the run's administrator schedules rather than the record.</param>
        private sealed record StepDescription(ProvisioningStepRegistration Registration, string Name, int Order, int Version, bool ForAdministrator);
    }
}
