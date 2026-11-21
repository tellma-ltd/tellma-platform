// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Validation;

namespace Tellma.Core.Abstractions.Tests.Errors
{
    /// <summary>Coded errors and warnings are immutable values that compare by their content.</summary>
    public class CodedErrorTests
    {
        [Fact]
        public void Two_errors_built_apart_from_equal_parts_are_equal()
        {
            ValidationError first = new(ValidationPath.Root.Index(0).Property("Name"), ValidationCodes.MaxLength, Args(("property", "Name"), ("maxLength", 50)));
            ValidationError second = new(ValidationPath.Root.Index(0).Property("Name"), ValidationCodes.MaxLength, Args(("maxLength", 50), ("property", "Name")));

            Assert.Equal(first, second);
            Assert.Equal(first.GetHashCode(), second.GetHashCode());
        }

        [Fact]
        public void Errors_differing_in_path_code_or_an_argument_are_not_equal()
        {
            ValidationError error = new(ValidationPath.Root.Index(0), ValidationCodes.Required, Args(("property", "Name")));

            Assert.NotEqual(error, new ValidationError(ValidationPath.Root.Index(1), ValidationCodes.Required, Args(("property", "Name"))));
            Assert.NotEqual(error, new ValidationError(ValidationPath.Root.Index(0), ValidationCodes.Unique, Args(("property", "Name"))));
            Assert.NotEqual(error, new ValidationError(ValidationPath.Root.Index(0), ValidationCodes.Required, Args(("property", "Code"))));
            Assert.NotEqual(error, new ValidationError(ValidationPath.Root.Index(0), ValidationCodes.Required, Args()));
        }

        [Fact]
        public void An_error_keeps_a_read_only_copy_of_its_arguments()
        {
            Dictionary<string, object?> arguments = Args(("property", "Name"));
            ValidationError error = new(ValidationPath.Root, ValidationCodes.Required, arguments);

            arguments["property"] = "Code";

            Assert.Equal("Name", error.Arguments["property"]);
            _ = Assert.Throws<NotSupportedException>(() => ((IDictionary<string, object?>)error.Arguments)["property"] = "Code");
        }

        [Fact]
        public void An_error_needs_a_path_a_code_and_an_argument_bag()
        {
            _ = Assert.Throws<ArgumentNullException>(() => new ValidationError(null!, ValidationCodes.Required, Args()));
            _ = Assert.Throws<ArgumentException>(() => new ValidationError(ValidationPath.Root, " ", Args()));
            _ = Assert.Throws<ArgumentNullException>(() => new ValidationError(ValidationPath.Root, ValidationCodes.Required, null!));
        }

        [Fact]
        public void A_warning_compares_by_key_ids_and_arguments()
        {
            ConfirmationRequest first = new("Inventory.BackdatedRecost", [5, 6], Args(("count", 1240)));

            Assert.Equal(first, new ConfirmationRequest("Inventory.BackdatedRecost", [5, 6], Args(("count", 1240))));
            Assert.NotEqual(first, new ConfirmationRequest("Inventory.BackdatedRecost", [6, 5], Args(("count", 1240))));
            Assert.NotEqual(first, new ConfirmationRequest("Sales.OpenOrders", [5, 6], Args(("count", 1240))));
            Assert.NotEqual(first, new ConfirmationRequest("Inventory.BackdatedRecost", [5, 6], Args(("count", 1))));
        }

        [Fact]
        public void A_warning_keeps_copies_of_its_ids_and_arguments()
        {
            long[] ids = [5];
            ConfirmationRequest warning = new("Inventory.BackdatedRecost", ids, Args());

            ids[0] = 9;

            Assert.Equal([5L], warning.Ids);
            _ = Assert.Throws<ArgumentException>(() => new ConfirmationRequest("", [], Args()));
            _ = Assert.Throws<ArgumentNullException>(() => new ConfirmationRequest("Inventory.BackdatedRecost", null!, Args()));
            _ = Assert.Throws<ArgumentNullException>(() => new ConfirmationRequest("Inventory.BackdatedRecost", [], null!));
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
