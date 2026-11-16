// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using System.Diagnostics.Metrics;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.Calendars;
using Tellma.Core.Crud.Validation;

namespace Tellma.Core.Tests.Crud.Validation
{
    /// <summary>
    ///     The scope's confirmations and a frame's warnings: seeded once, declared keys only, every key
    ///     confirmed under the system, every unconfirmed warning raised together in key order, and the
    ///     ordering rule of a validation outcome.
    /// </summary>
    public sealed class ConfirmationsTests : IDisposable
    {
        /// <summary>The user a user scope runs for.</summary>
        private const int UserId = 7;

        /// <summary>The composed validation model.</summary>
        private readonly ValidationRig _rig = new();

        /// <summary>The consent records.</summary>
        private readonly FakeLogger<Confirmations> _logger = new();

        /// <summary>A user scope's context.</summary>
        private static RequestContext UserScope { get; } = RequestContext.Tenantless(new GregorianCalendarSystem(), DateTimeOffset.UnixEpoch) with
        {
            Kind = PrincipalKind.User,
            UserId = UserId,
        };

        /// <summary>A system scope's context.</summary>
        private static RequestContext SystemScope { get; } = RequestContext.Tenantless(new GregorianCalendarSystem(), DateTimeOffset.UnixEpoch) with
        {
            Kind = PrincipalKind.System,
        };

        /// <inheritdoc />
        public void Dispose()
        {
            _rig.Dispose();
        }

        /// <summary>A stale stamp wins over errors, errors win over warnings, and warnings are raised only when nothing else is.</summary>
        [Fact]
        public void The_outcome_raises_a_stale_stamp_then_errors_then_warnings()
        {
            ConfirmationFrame frame = Scope(UserScope).Frame("vm.VmWidget", "save");
            frame.Require(ValidationModel.Backdated, [1]);
            ValidationErrors errors = new();
            errors.Add(ValidationPath.Root.Index(0).Property("Name"), ValidationCodes.Required);
            ConcurrencyException stale = new("Concurrency.Stale", [new ConcurrencyConflict(1, null, null, null, IsMissing: true)]);

            Assert.Same(stale, Assert.Throws<ConcurrencyException>(() => ValidationOutcome.Raise(stale, errors, frame)));
            Assert.Equal(errors.Items, Assert.Throws<ValidationException>(() => ValidationOutcome.Raise(null, errors, frame)).Errors);
            ConfirmationRequest warning = Assert.Single(Assert.Throws<ConfirmationRequiredException>(() => ValidationOutcome.Raise(null, new ValidationErrors(), frame)).Requests);
            Assert.Equal(ValidationModel.Backdated, warning.Key);
            Assert.Equal([1L], warning.Ids);

            ValidationOutcome.Raise(null, new ValidationErrors(), Scope(UserScope).Frame("vm.VmWidget", "save"));
        }

        /// <summary>Every unconfirmed warning surfaces in one exception, ordered by key, each key's in the order raised.</summary>
        [Fact]
        public void Unconfirmed_warnings_surface_together_in_key_order()
        {
            ConfirmationFrame frame = Scope(UserScope).Frame("vm.VmWidget", "save");
            Dictionary<string, object?> arguments = new(StringComparer.Ordinal) { ["date"] = "2026-01-31" };

            frame.Require(ValidationModel.Overdraft, [2]);
            frame.Require(ValidationModel.Backdated, [1], arguments);
            frame.Require(ValidationModel.Overdraft, [3, 4]);

            ConfirmationRequiredException raised = Assert.Throws<ConfirmationRequiredException>(() => ValidationOutcome.Raise(null, new ValidationErrors(), frame));
            Assert.Equal(
                [(ValidationModel.Backdated, "1"), (ValidationModel.Overdraft, "2"), (ValidationModel.Overdraft, "3,4")],
                raised.Requests.Select(static request => (request.Key, string.Join(',', request.Ids))));
            Assert.Equal("2026-01-31", raised.Requests[0].Arguments["date"]);
            Assert.Empty(raised.Requests[1].Arguments);
        }

        /// <summary>A confirmed key passes and records the consent; an unconfirmed one is raised; both are counted.</summary>
        [Fact]
        public void A_confirmed_warning_passes_and_records_the_consent()
        {
            using MetricCollector<long> counted = new(_rig.Services.GetRequiredService<IMeterFactory>(), CrudTelemetryNames.MeterName, CrudTelemetryNames.Confirmations);
            Confirmations scope = Scope(UserScope);
            scope.Seed([ValidationModel.Backdated, "Vm.NoLongerRaised", ValidationModel.Backdated]);
            ConfirmationFrame frame = scope.Frame("vm.VmWidget", "gl.Invoice:post");

            frame.Require(ValidationModel.Backdated, [4, 5]);
            frame.Require(ValidationModel.Overdraft);

            Assert.Equal([ValidationModel.Backdated], scope.Confirmed);
            Assert.Equal([ValidationModel.Backdated], frame.Confirmed);
            Assert.True(frame.IsConfirmed(ValidationModel.Backdated));
            Assert.False(frame.IsConfirmed(ValidationModel.Overdraft));
            Assert.Equal([ValidationModel.Overdraft], frame.Unconfirmed.Select(static request => request.Key));

            FakeLogRecord consent = Assert.Single(_logger.Collector.GetSnapshot());
            Assert.Equal(("ConfirmationAccepted", LogLevel.Information), (consent.Id.Name, consent.Level));
            Assert.Equal(
                "User 7 confirmed the warning Vm.Backdated raised by gl.Invoice:post on vm.VmWidget for the rows [4, 5].",
                consent.Message);

            Assert.Equal(
                [(ValidationModel.Backdated, CrudMetrics.ConfirmationConfirmed), (ValidationModel.Overdraft, CrudMetrics.ConfirmationRequired)],
                counted.GetMeasurementSnapshot().Select(static measurement => ((string)measurement.Tags[CrudTelemetryNames.KeyTag]!, (string)measurement.Tags[CrudTelemetryNames.OutcomeTag]!)));
        }

        /// <summary>Under the system every declared key counts as confirmed, and no consent is recorded.</summary>
        [Fact]
        public void Under_the_system_every_key_counts_as_confirmed()
        {
            Confirmations scope = Scope(SystemScope);
            ConfirmationFrame frame = scope.Frame("vm.VmWidget", "save");

            frame.Require(ValidationModel.Backdated, [1]);
            frame.Require(ValidationModel.Overdraft, [2]);

            Assert.True(scope.IsConfirmed(ValidationModel.Overdraft));
            Assert.Empty(scope.Confirmed);
            Assert.Empty(frame.Unconfirmed);
            Assert.Empty(_logger.Collector.GetSnapshot());
            _ = Assert.Throws<InvalidOperationException>(() => scope.IsConfirmed("Vm.Undeclared"));
        }

        /// <summary>A key nobody declared is an authoring bug, on the scope or a frame, and a scope is seeded once.</summary>
        [Fact]
        public void Undeclared_keys_and_a_second_seed_are_refused()
        {
            Confirmations scope = Scope(UserScope);
            ConfirmationFrame frame = scope.Frame("vm.VmWidget", "save");

            Assert.Contains(
                "contribution.Confirmation(\"Vm.Undeclared\")",
                Assert.Throws<InvalidOperationException>(() => frame.Require("Vm.Undeclared")).Message,
                StringComparison.Ordinal);
            _ = Assert.Throws<InvalidOperationException>(() => scope.IsConfirmed("Vm.Undeclared"));
            _ = Assert.Throws<ArgumentException>(() => frame.Require(" "));
            _ = Assert.Throws<InvalidOperationException>(() => scope.Require("Vm.Undeclared"));

            scope.Seed([]);
            _ = Assert.Throws<InvalidOperationException>(() => scope.Seed([ValidationModel.Backdated]));
            Assert.Empty(frame.Unconfirmed);
            Assert.Empty(scope.Unframed);
        }

        /// <summary>
        ///     A warning raised on the scope lands in its innermost open frame: an enlisted target
        ///     raising through its host's scope is raised by the host's outcome, together with the host's
        ///     own warnings and under the host's operation.
        /// </summary>
        [Fact]
        public void An_enlisted_targets_warning_is_raised_through_its_host()
        {
            Confirmations scope = Scope(UserScope);
            scope.Seed([ValidationModel.Backdated]);
            using ConfirmationFrame host = scope.Frame("vm.VmWidget", "gl.Invoice:post");

            // The host raises through its frame; the enlisted target through the scope it was handed.
            host.Require(ValidationModel.Overdraft, [1]);
            scope.Require(ValidationModel.Overdraft, [9]);
            scope.Require(ValidationModel.Backdated, [9]);

            ConfirmationRequiredException raised = Assert.Throws<ConfirmationRequiredException>(() => ValidationOutcome.Raise(null, new ValidationErrors(), host));
            Assert.Equal(
                [(ValidationModel.Overdraft, "1"), (ValidationModel.Overdraft, "9")],
                raised.Requests.Select(static request => (request.Key, string.Join(',', request.Ids))));
            Assert.Empty(scope.Unframed);
            Assert.Equal(
                "User 7 confirmed the warning Vm.Backdated raised by gl.Invoice:post on vm.VmWidget for the rows [9].",
                Assert.Single(_logger.Collector.GetSnapshot()).Message);
        }

        /// <summary>
        ///     Frames nest: the innermost open frame takes the scope's warnings, a disposed frame takes
        ///     none, and outside every frame an unconfirmed warning is kept on the scope while a
        ///     confirmed one records the consent.
        /// </summary>
        [Fact]
        public void The_innermost_open_frame_takes_the_scopes_warnings()
        {
            Confirmations scope = Scope(UserScope);
            scope.Seed([ValidationModel.Backdated]);
            ConfirmationFrame outer = scope.Frame("vm.VmWidget", "save");
            ConfirmationFrame inner = scope.Frame("vm.VmWidget", "gl.Invoice:post");

            scope.Require(ValidationModel.Overdraft, [1]);
            inner.Dispose();
            inner.Dispose();
            scope.Require(ValidationModel.Overdraft, [2]);
            outer.Dispose();
            scope.Require(ValidationModel.Overdraft, [3]);
            scope.Require(ValidationModel.Backdated, [4]);

            Assert.Equal([1L], Assert.Single(inner.Unconfirmed).Ids);
            Assert.Equal([2L], Assert.Single(outer.Unconfirmed).Ids);
            ConfirmationRequest unframed = Assert.Single(scope.Unframed);
            Assert.Equal((ValidationModel.Overdraft, 3L), (unframed.Key, Assert.Single(unframed.Ids)));
            FakeLogRecord consent = Assert.Single(_logger.Collector.GetSnapshot());
            Assert.Equal(("ConfirmationAcceptedOutsideFrame", LogLevel.Information), (consent.Id.Name, consent.Level));
            Assert.Equal("User 7 confirmed the warning Vm.Backdated, raised outside every write frame, for the rows [4].", consent.Message);
        }

        /// <summary>The composition registers one confirmations per scope, over the keys its features declared.</summary>
        [Fact]
        public void The_composition_registers_one_confirmations_per_scope()
        {
            using IServiceScope first = _rig.Services.CreateScope();
            using IServiceScope second = _rig.Services.CreateScope();

            Assert.Same(first.ServiceProvider.GetRequiredService<Confirmations>(), first.ServiceProvider.GetRequiredService<IConfirmations>());
            Assert.NotSame(first.ServiceProvider.GetRequiredService<Confirmations>(), second.ServiceProvider.GetRequiredService<Confirmations>());
            Assert.Equal(
                [ValidationModel.Backdated, ValidationModel.Overdraft],
                _rig.Services.GetRequiredService<DeclaredConfirmations>().Keys.Order(StringComparer.Ordinal));
        }

        /// <summary>A scope's confirmations over the model's declared keys.</summary>
        /// <param name="context">The scope's context.</param>
        /// <returns>The confirmations.</returns>
        private Confirmations Scope(RequestContext context)
        {
            return new Confirmations(
                _rig.Services.GetRequiredService<DeclaredConfirmations>(),
                new FixedContext(context),
                _rig.Services.GetRequiredService<CrudMetrics>(),
                _logger);
        }

        /// <summary>A request context accessor answering one context.</summary>
        /// <param name="context">The context.</param>
        private sealed class FixedContext(RequestContext context) : IRequestContextAccessor
        {
            /// <inheritdoc />
            public RequestContext Current => context;
        }
    }
}
