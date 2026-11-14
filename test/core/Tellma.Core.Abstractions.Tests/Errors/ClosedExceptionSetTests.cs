// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Reflection;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Tests.Api;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.Queryex;

namespace Tellma.Core.Abstractions.Tests.Errors
{
    /// <summary>
    ///     The closed set of exceptions the edge maps by type: closed by construction, each member's
    ///     code and display arguments, and its refusal of a malformed outcome.
    /// </summary>
    public class ClosedExceptionSetTests
    {
        [Fact]
        public void Only_the_contract_assembly_can_extend_the_set_and_every_member_is_sealed()
        {
            Type baseType = typeof(TellmaException);

            Assert.True(baseType.IsAbstract);
            Assert.All(
                baseType.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public),
                static constructor => Assert.True(constructor.IsFamilyAndAssembly));

            Type[] members = [.. baseType.Assembly.GetTypes().Where(type => type.IsSubclassOf(baseType))];
            Assert.NotEmpty(members);
            Assert.All(members, static member => Assert.True(member.IsSealed && member.IsPublic, member.Name));
        }

        [Fact]
        public void A_validation_failure_carries_every_error()
        {
            ValidationError required = new(ValidationPath.Root.Index(0).Property("Name"), ValidationCodes.Required, Args(("property", "Name")));
            ValidationError cycle = new(ValidationPath.Root, ValidationCodes.TreeCycle, Args());

            ValidationException failure = new([required, cycle]);

            Assert.Equal("validation", failure.Code);
            Assert.Empty(failure.Arguments);
            Assert.Equal([required, cycle], failure.Errors);
            Assert.Equal("Validation failed with 2 error(s): Required at [0].Name, Tree.Cycle on the whole payload.", failure.Message);
        }

        [Fact]
        public void A_validation_failure_needs_an_error_and_holds_no_null()
        {
            _ = Assert.Throws<ArgumentNullException>(() => new ValidationException(null!));
            _ = Assert.Throws<ArgumentException>(() => new ValidationException([]));
            _ = Assert.Throws<ArgumentException>(() => new ValidationException([null!]));
        }

        [Fact]
        public void A_not_found_names_the_resource_and_the_ids_as_strings()
        {
            NotFoundException notFound = new("gl.Center", ["5", "7"]);

            Assert.Equal("not-found", notFound.Code);
            Assert.Equal("gl.Center", notFound.Arguments["resource"]);
            Assert.Equal(["5", "7"], (IReadOnlyList<string>)notFound.Arguments["ids"]!);
            Assert.Equal(["5", "7"], notFound.Ids);
            _ = Assert.Throws<ArgumentException>(() => new NotFoundException(" ", ["5"]));
            _ = Assert.Throws<ArgumentNullException>(() => new NotFoundException("gl.Center", null!));
        }

        [Fact]
        public void A_long_list_logs_ten_entries_and_a_count()
        {
            NotFoundException notFound = new("gl.Center", [.. Enumerable.Range(1, 25).Select(static id => id.ToString(System.Globalization.CultureInfo.InvariantCulture))]);

            Assert.Equal("No visible gl.Center with id 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 and 15 more.", notFound.Message);
        }

        [Theory]
        [InlineData(ForbiddenReason.NoGrant, null, "no-grant")]
        [InlineData(ForbiddenReason.OutsideFilter, null, "outside-filter")]
        [InlineData(ForbiddenReason.RestrictedPath, "Parent.Email", "restricted-path")]
        public void A_refusal_names_the_pair_and_why_in_wire_form(ForbiddenReason reason, string? path, string wireReason)
        {
            ForbiddenException forbidden = new("forbidden", "gl.Center", "Read", reason, path);

            Assert.Equal("forbidden", forbidden.Code);
            Assert.Equal((reason, path), (forbidden.Reason, forbidden.Path));
            Assert.Equal("gl.Center", forbidden.Arguments["resource"]);
            Assert.Equal("Read", forbidden.Arguments["action"]);
            Assert.Equal(wireReason, forbidden.Arguments["reason"]);
            Assert.Equal(path is not null, forbidden.Arguments.ContainsKey("path"));
        }

        [Fact]
        public void Only_a_restricted_path_refusal_names_a_path()
        {
            _ = Assert.Throws<ArgumentException>(() => new ForbiddenException("forbidden", "gl.Center", "Read", ForbiddenReason.RestrictedPath));
            _ = Assert.Throws<ArgumentException>(() => new ForbiddenException("forbidden", "gl.Center", "Read", ForbiddenReason.NoGrant, "Parent.Email"));
            _ = Assert.Throws<ArgumentException>(() => new ForbiddenException("forbidden", "gl.Center", "Read", (ForbiddenReason)9));
            _ = Assert.Throws<ArgumentException>(() => new ForbiddenException("forbidden", "", "Read", ForbiddenReason.NoGrant));
            _ = Assert.Throws<ArgumentException>(() => new ForbiddenException(" ", "gl.Center", "Read", ForbiddenReason.NoGrant));
        }

        [Fact]
        public void A_refusal_may_name_no_action()
        {
            ForbiddenException forbidden = new("forbidden", "core.Settings.General", null, ForbiddenReason.NoGrant);

            Assert.Null(forbidden.Action);
            Assert.True(forbidden.Arguments.ContainsKey("action"));
            Assert.Null(forbidden.Arguments["action"]);
        }

        [Fact]
        public void A_concurrency_conflict_lists_every_changed_row()
        {
            ConcurrencyConflict changed = new(5, "2026-08-30T12:02:10.0000000+00:00", 3, "Sara", IsMissing: false);
            ConcurrencyConflict missing = new(6, null, null, null, IsMissing: true);

            ConcurrencyException conflict = new("concurrency-conflict", [changed, missing]);

            Assert.Equal("concurrency-conflict", conflict.Code);
            Assert.Equal([changed, missing], conflict.Conflicts);
            Assert.Empty(conflict.Arguments);
            _ = Assert.Throws<ArgumentException>(() => new ConcurrencyException("concurrency-conflict", []));
        }

        [Fact]
        public void A_confirmation_is_required_for_every_unconfirmed_warning()
        {
            ConfirmationRequest recost = new("Inventory.BackdatedRecost", [5], Args(("count", 1240)));

            ConfirmationRequiredException required = new([recost]);

            Assert.Equal("confirmation-required", required.Code);
            Assert.Equal([recost], required.Requests);
            _ = Assert.Throws<ArgumentException>(() => new ConfirmationRequiredException([]));
            _ = Assert.Throws<ArgumentException>(() => new ConfirmationRequiredException([null!]));
        }

        [Fact]
        public void Count_and_limit_refusals_carry_their_numbers()
        {
            CountMismatchException mismatch = new(12, 13);
            LimitExceededException limit = new(LimitNames.MaxTake, 10_001, 10_000);

            Assert.Equal("count-mismatch", mismatch.Code);
            Assert.Equal((12, 13), (mismatch.Arguments["expected"], mismatch.Arguments["actual"]));
            Assert.Equal((12, 13), (mismatch.Expected, mismatch.Actual));
            Assert.Equal("limit-exceeded", limit.Code);
            Assert.Equal(("MaxTake", 10_001, 10_000), (limit.Arguments["limit"], limit.Arguments["actual"], limit.Arguments["maximum"]));
            Assert.Equal(("MaxTake", 10_001, 10_000), (limit.Limit, limit.Actual, limit.Maximum));
        }

        [Fact]
        public void A_limit_refusal_validates_before_it_renders_its_message()
        {
            // A null name is the argument's own refusal, never a failure inside the message.
            _ = Assert.Throws<ArgumentNullException>(() => new LimitExceededException(null!, 1, 0));
            _ = Assert.Throws<ArgumentException>(() => new LimitExceededException(" ", 1, 0));
        }

        [Fact]
        public void An_invalid_query_carries_every_diagnostic()
        {
            QueryexDiagnostic diagnostic = new("QX3001", new QueryexSpan(4, 6), "Filter", [new("property", "Nmae")]);

            InvalidQueryException invalid = new([diagnostic]);

            Assert.Equal("query-invalid", invalid.Code);
            Assert.Equal([diagnostic], invalid.Diagnostics);
            Assert.Equal("The query is invalid with 1 diagnostic(s): QX3001 at Filter.", invalid.Message);
            _ = Assert.Throws<ArgumentException>(() => new InvalidQueryException([]));
        }

        [Fact]
        public void A_bad_request_carries_its_detail_and_arguments()
        {
            BadRequestException plain = new(null);
            BadRequestException named = new("The argument 'q' is not a date.", Args(("parameter", "q")));

            Assert.Equal(("bad-request", null), (plain.Code, plain.Detail));
            Assert.Empty(plain.Arguments);
            Assert.Equal("The argument 'q' is not a date.", named.Detail);
            Assert.Equal("q", named.Arguments["parameter"]);
        }

        [Fact]
        public void Individual_required_and_dependency_unavailable_carry_their_codes()
        {
            InvalidOperationException cause = new("connection lost");
            DependencyUnavailableException unavailable = new("database", TimeSpan.FromSeconds(1), cause);

            Assert.Equal("individual-required", new IndividualRequiredException().Code);
            Assert.Equal("dependency-unavailable", unavailable.Code);
            Assert.Equal("database", unavailable.Arguments["dependency"]);
            Assert.Equal(TimeSpan.FromSeconds(1), unavailable.RetryAfter);
            Assert.Same(cause, unavailable.InnerException);
            Assert.Null(new DependencyUnavailableException("email", null, null).RetryAfter);
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => new DependencyUnavailableException("database", TimeSpan.FromSeconds(-1), null));
            _ = Assert.Throws<ArgumentException>(() => new DependencyUnavailableException("", null, null));
        }

        [Fact]
        public void A_partial_failure_carries_what_succeeded_and_what_failed()
        {
            string[] results = ["invited 5"];

            PartialFailureException partial = new("partial-failure", results, ["6", "7"]);

            Assert.Equal("partial-failure", partial.Code);
            Assert.Same(results, partial.Results);
            Assert.Equal(["6", "7"], partial.Failed);
            _ = Assert.Throws<ArgumentNullException>(() => new PartialFailureException("partial-failure", null!, []));
            _ = Assert.Throws<ArgumentException>(() => new PartialFailureException("partial-failure", results, [null!]));
        }

        [Fact]
        public void The_display_arguments_are_a_copy_nobody_can_change()
        {
            Dictionary<string, object?> arguments = new() { ["parameter"] = "q" };
            BadRequestException refusal = new("detail", arguments);

            arguments["parameter"] = "changed";

            Assert.Equal("q", refusal.Arguments["parameter"]);
            _ = Assert.Throws<NotSupportedException>(() => ((IDictionary<string, object?>)refusal.Arguments).Add("x", 1));
        }

        [Fact]
        public void The_limit_names_are_pinned()
        {
            Assert.Equal(
                new Dictionary<string, string>
                {
                    [nameof(LimitNames.MaxTake)] = "MaxTake",
                    [nameof(LimitNames.SkipWindow)] = "skipWindow",
                    [nameof(LimitNames.MaxSaveCount)] = "MaxSaveCount",
                    [nameof(LimitNames.MaxRowsPerSave)] = "MaxRowsPerSave",
                    [nameof(LimitNames.MaxChildren)] = "MaxChildren",
                    [nameof(LimitNames.MaxIds)] = "MaxIds",
                    [nameof(LimitNames.MaxDeleteByQueryRows)] = "MaxDeleteByQueryRows",
                    [nameof(LimitNames.MaxSearchLength)] = "MaxSearchLength",
                    [nameof(LimitNames.MaxExtras)] = "MaxExtras",
                    [nameof(LimitNames.Entities)] = "entities",
                    [nameof(LimitNames.Body)] = "body",
                },
                WireNameTests.Constants(typeof(LimitNames)));
        }

        /// <summary>Builds an argument bag.</summary>
        /// <param name="pairs">The named values.</param>
        /// <returns>The bag.</returns>
        private static Dictionary<string, object?> Args(params (string Name, object? Value)[] pairs)
        {
            return pairs.ToDictionary(static pair => pair.Name, static pair => pair.Value);
        }
    }
}
