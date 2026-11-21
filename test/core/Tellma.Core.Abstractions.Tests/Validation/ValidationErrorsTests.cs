// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Validation;

namespace Tellma.Core.Abstractions.Tests.Validation
{
    /// <summary>
    ///     The errors a validation context collects: paths named by expression or by hand, every error
    ///     kept in order, and safe for the validators of one round to add to at once.
    /// </summary>
    public class ValidationErrorsTests
    {
        [Fact]
        public void A_fresh_list_has_no_errors()
        {
            ValidationErrors errors = new();

            Assert.False(errors.HasErrors);
            Assert.Empty(errors.Items);
        }

        [Fact]
        public void An_error_is_added_at_its_path_with_its_code_and_arguments()
        {
            ValidationErrors errors = new();

            errors.Add(ValidationPath.Root.Property("Ids").Index(2), ValidationCodes.FkInUse, new Dictionary<string, object?> { ["entity"] = "gl.Invoice" });
            errors.Add(ValidationPath.Root, ValidationCodes.TreeCycle);

            Assert.True(errors.HasErrors);
            Assert.Equal(
                [
                    new ValidationError(ValidationPath.Root.Property("Ids").Index(2), "Fk.InUse", new Dictionary<string, object?> { ["entity"] = "gl.Invoice" }),
                    new ValidationError(ValidationPath.Root, "Tree.Cycle", new Dictionary<string, object?>()),
                ],
                errors.Items);
        }

        [Fact]
        public void The_typed_view_names_the_member_from_the_expression()
        {
            ValidationErrors<Center> errors = new();

            errors.Add(0, x => x.ParentId, "Gl.Centers.ParentMustBeGrouping");
            errors.Add(1, x => (object)x.Code, ValidationCodes.Required);
            errors.Add(2, x => ((IActivatable)x).IsActive, ValidationCodes.WriteOnce);

            Assert.Equal(["[0].ParentId", "[1].Code", "[2].IsActive"], errors.Items.Select(static error => error.Path.ToString()));
        }

        [Fact]
        public void The_typed_view_refuses_an_expression_that_names_no_property_of_the_row()
        {
            ValidationErrors<Center> errors = new();

            _ = Assert.Throws<ArgumentException>(() => errors.Add(0, x => x.Parent!.Code, ValidationCodes.Required));
            _ = Assert.Throws<ArgumentException>(() => errors.Add(0, x => x.Describe(), ValidationCodes.Required));
            _ = Assert.Throws<ArgumentException>(() => errors.Add(0, x => x.Note, ValidationCodes.Required));
            _ = Assert.Throws<ArgumentException>(() => errors.Add(0, x => 5, ValidationCodes.Required));
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => errors.Add(-1, x => x.Code, ValidationCodes.Required));
            _ = Assert.Throws<ArgumentNullException>(() => errors.Add<int>(0, null!, ValidationCodes.Required));
            Assert.False(errors.HasErrors);
        }

        [Fact]
        public void An_error_keeps_its_arguments_as_they_were_when_added()
        {
            ValidationErrors errors = new();
            Dictionary<string, object?> arguments = new() { ["maxLength"] = 50 };

            errors.Add(ValidationPath.Root.Index(0).Property("Name"), ValidationCodes.MaxLength, arguments);
            arguments["maxLength"] = 10;

            Assert.Equal(50, errors.Items[0].Arguments["maxLength"]);
        }

        [Fact]
        public void A_snapshot_of_the_errors_does_not_change_with_later_additions()
        {
            ValidationErrors errors = new();
            errors.Add(ValidationPath.Root, ValidationCodes.TreeCycle);
            IReadOnlyList<ValidationError> snapshot = errors.Items;

            errors.Add(ValidationPath.Root, ValidationCodes.TreeTooDeep);

            Assert.Single(snapshot);
            Assert.Equal(2, errors.Items.Count);
        }

        [Fact]
        public async Task Validators_running_at_once_lose_no_error()
        {
            ValidationErrors errors = new();

            await Task.WhenAll(Enumerable.Range(0, 64).Select(validator => Task.Run(() =>
            {
                for (int i = 0; i < 100; i++)
                {
                    errors.Add(ValidationPath.Root.Index(validator), ValidationCodes.Required);
                }
            }, TestContext.Current.CancellationToken)));

            Assert.Equal(6400, errors.Items.Count);
        }

        [Fact]
        public void A_code_is_required()
        {
            ValidationErrors errors = new();

            _ = Assert.Throws<ArgumentException>(() => errors.Add(ValidationPath.Root, " "));
            _ = Assert.Throws<ArgumentNullException>(() => errors.Add(null!, ValidationCodes.Required));
        }

        // --- The re-homing view an enlisted stack writes to ---

        [Fact]
        public void An_enlisted_stacks_view_keeps_its_own_paths_and_lands_each_error_on_the_hosts_path_naming_the_stack()
        {
            ValidationErrors host = new();
            ValidationErrors<Center> view = new(host, Under(ValidationPath.Root.Index(0).Property("Lines")), "gl.Center");

            view.Add(1, x => x.Code, ValidationCodes.Required, new Dictionary<string, object?> { ["max"] = 5 });

            // --- The view's own path for its participants; the host's for the caller ---
            Assert.Equal("[1].Code", Assert.Single(view.Items).Path.ToString());
            ValidationError rehomed = Assert.Single(host.Items);
            Assert.Equal(("[0].Lines[1].Code", ValidationCodes.Required), (rehomed.Path.ToString(), rehomed.Code));
            Assert.Equal(new Dictionary<string, object?> { ["max"] = 5, ["enlistedResource"] = "gl.Center" }, rehomed.Arguments);
        }

        [Fact]
        public void Without_a_path_map_every_error_lands_on_the_hosts_root()
        {
            ValidationErrors host = new();
            ValidationErrors view = new(host, map: null, "gl.Center");

            view.Add(ValidationPath.Root.Index(3).Property("Code"), ValidationCodes.Required);

            Assert.Equal(string.Empty, Assert.Single(host.Items).Path.ToString());
        }

        [Fact]
        public void A_view_over_a_view_composes_the_maps_innermost_first_and_keeps_the_innermost_stacks_name()
        {
            ValidationErrors host = new();
            ValidationErrors outer = new(host, Under(ValidationPath.Root.Index(0).Property("Notes")), "en.Note");
            ValidationErrors inner = new(outer, Under(ValidationPath.Root.Property("Tags")), "en.Tag");

            inner.Add(ValidationPath.Root.Index(2).Property("Label"), ValidationCodes.Required);

            // --- Each list holds the error at its own paths; the host alone holds it once ---
            Assert.Equal("[2].Label", Assert.Single(inner.Items).Path.ToString());
            Assert.Empty(outer.Items);
            ValidationError rehomed = Assert.Single(host.Items);
            Assert.Equal(("[0].Notes.Tags[2].Label", "en.Tag"), (rehomed.Path.ToString(), rehomed.Arguments["enlistedResource"]));
        }

        [Fact]
        public void A_path_map_that_answers_null_is_refused_and_leaves_both_lists_untouched()
        {
            ValidationErrors host = new();
            ValidationErrors view = new(host, static _ => null!, "gl.Center");

            InvalidOperationException refused = Assert.Throws<InvalidOperationException>(() => view.Add(ValidationPath.Root.Index(0), ValidationCodes.Required));

            Assert.Contains("returned null", refused.Message, StringComparison.Ordinal);
            Assert.False(view.HasErrors);
            Assert.False(host.HasErrors);
        }

        /// <summary>A path map that prefixes every path of the enlisted payload.</summary>
        /// <param name="prefix">The host's path the enlisted payload sits under.</param>
        /// <returns>The map.</returns>
        private static Func<ValidationPath, ValidationPath> Under(ValidationPath prefix)
        {
            return path => new ValidationPath([.. prefix.Segments, .. path.Segments]);
        }

        /// <summary>A capability an entity declares through an interface.</summary>
        private interface IActivatable
        {
            /// <summary>Whether the row is active.</summary>
            bool IsActive { get; }
        }

        /// <summary>A payload entity.</summary>
        private sealed class Center : IActivatable
        {
            /// <summary>A field, which no path names.</summary>
            public string Note = string.Empty;

            /// <summary>The parent's id.</summary>
            public int? ParentId { get; set; }

            /// <summary>The parent row.</summary>
            public Center? Parent { get; set; }

            /// <summary>The code.</summary>
            public string Code { get; set; } = string.Empty;

            /// <summary>Whether the row is active, implemented explicitly.</summary>
            bool IActivatable.IsActive => true;

            /// <summary>A method, which no path names.</summary>
            /// <returns>The code.</returns>
            public string Describe()
            {
                return Code;
            }
        }
    }
}
