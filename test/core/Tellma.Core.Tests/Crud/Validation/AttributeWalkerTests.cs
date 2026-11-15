// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.Crud.Validation;

namespace Tellma.Core.Tests.Crud.Validation
{
    /// <summary>
    ///     A save's first step: the payload's shape and ceilings, its ids, and every row's values
    ///     against its metadata facets and validation attributes, every failure reported at its path.
    /// </summary>
    public sealed class AttributeWalkerTests : IDisposable
    {
        /// <summary>The composed validation model.</summary>
        private readonly ValidationRig _rig = new();

        /// <summary>The walker.</summary>
        private readonly AttributeWalker _walker = new();

        /// <summary>Decimals against a <c>decimal(19,4)</c> column: at most four fractional and fifteen integer digits, never rounded to fit.</summary>
        /// <param name="text">The value, as invariant text.</param>
        /// <param name="fits">Whether it fits.</param>
        [Theory]
        [InlineData("1.2345", true)]
        [InlineData("1.23450", true)]
        [InlineData("-1.2345", true)]
        [InlineData("0.0001", true)]
        [InlineData("999999999999999.9999", true)]
        [InlineData("-999999999999999.9999", true)]
        [InlineData("0", true)]
        [InlineData("1.23451", false)]
        [InlineData("1.23455", false)]
        [InlineData("0.00001", false)]
        [InlineData("1000000000000000", false)]
        [InlineData("-1000000000000000.5", false)]
        public void A_decimal_is_held_to_the_columns_precision_and_scale_without_rounding(string text, bool fits)
        {
            ValidationErrors errors = Check(Widget(price: decimal.Parse(text, CultureInfo.InvariantCulture)));

            if (fits)
            {
                Assert.False(errors.HasErrors);
            }
            else
            {
                ValidationError error = Assert.Single(errors.Items);
                Assert.Equal(("[0].Price", ValidationCodes.Precision), (error.Path.ToString(), error.Code));
                Assert.Equal(19, error.Arguments["precision"]);
                Assert.Equal(4, error.Arguments["scale"]);
                Assert.Equal("Price", error.Arguments["property"]);
            }
        }

        /// <summary>Every failure of a row is reported, each at its property's path; an IValidatableObject is never consulted.</summary>
        [Fact]
        public void Every_failure_is_reported_at_its_path()
        {
            VmWidget widget = Widget(code: "TOO-LONG-CODE", name: null, quantity: 101, email: "not-an-email");

            ValidationErrors errors = Check(widget);

            Assert.Equal(
                [
                    ("[0].Code", ValidationCodes.MaxLength),
                    ("[0].Email", ValidationCodes.Invalid),
                    ("[0].Name", ValidationCodes.Required),
                    ("[0].Quantity", ValidationCodes.Range),
                ],
                Paths(errors));
            Assert.Equal(10, Find(errors, "[0].Code").Arguments["maxLength"]);
            Assert.Equal("EmailAddress", Find(errors, "[0].Email").Arguments["attribute"]);
            Assert.Equal((0, 100), ((int)Find(errors, "[0].Quantity").Arguments["minimum"]!, (int)Find(errors, "[0].Quantity").Arguments["maximum"]!));
            Assert.Equal("Name", Find(errors, "[0].Name").Arguments["property"]);
        }

        /// <summary>A string length that sets a column's maximum reports a value past it once, as MaxLength; its minimum is Validation.Invalid.</summary>
        [Fact]
        public void A_column_string_length_reports_one_failure()
        {
            string beyond = new string('a', 26) + "@x.co";

            ValidationError pastColumn = Assert.Single(Check(Widget(email: beyond)).Items);
            ValidationError tooShort = Assert.Single(Check(Widget(email: "a@b.c")).Items);

            Assert.Equal((ValidationCodes.MaxLength, 30), (pastColumn.Code, (int)pastColumn.Arguments["maxLength"]!));
            Assert.Equal((ValidationCodes.Invalid, "StringLength"), (tooShort.Code, (string)tooShort.Arguments["attribute"]!));
            Assert.Equal(30, _rig.Of<VmWidget>().Properties.Single(static property => property.Name == "Email").MaxLength);
        }

        /// <summary>A server-owned column is neither measured nor judged: the server resets it.</summary>
        [Fact]
        public void A_server_owned_column_is_not_judged()
        {
            VmWidget widget = Widget();
            widget.Stamp = "far-too-long-and-no-email";

            Assert.False(Check(widget).HasErrors);
        }

        /// <summary>A rule on the class is judged on the whole row and reported at the row's path.</summary>
        [Fact]
        public void A_class_rule_is_reported_at_the_row()
        {
            ValidationError error = Assert.Single(Check(Widget(code: "SAME", name: "SAME")).Items);

            Assert.Equal(("[0]", ValidationCodes.Invalid, "VmCodeNotName"), (error.Path.ToString(), error.Code, (string)error.Arguments["attribute"]!));
            Assert.False(error.Arguments.ContainsKey("property"));
        }

        /// <summary>Children are walked at their own paths, and a child's parent key, which the nesting sets, is not judged.</summary>
        [Fact]
        public void Children_are_walked_at_their_paths_and_their_parent_key_is_left_alone()
        {
            VmWidget widget = Widget();
            widget.Lines = [new VmWidgetLine { Units = 1, Note = "too long" }, new VmWidgetLine { Units = 11 }];

            ValidationErrors errors = Check(widget);

            Assert.Equal(
                [("[0].Lines[0].Note", ValidationCodes.MaxLength), ("[0].Lines[1].Units", ValidationCodes.Range)],
                errors.Items.Select(static error => (error.Path.ToString(), error.Code)));
        }

        /// <summary>A request body is walked by its attributes, into nested objects and lists of them, a cycle visited once.</summary>
        [Fact]
        public void A_body_is_walked_into_nested_objects_and_lists()
        {
            VmBody body = new()
            {
                Title = null,
                Level = 9,
                Item = new VmBodyItem { Code = "ABCD" },
                Items = [new VmBodyItem { Code = "ok" }, new VmBodyItem { Code = "WXYZ" }],
            };
            body.Self = body;
            ValidationErrors errors = new();

            _walker.CheckBody(body, ValidationPath.Root.Property("Arguments"), errors);

            Assert.Equal(
                [
                    ("Arguments.Item.Code", ValidationCodes.MaxLength),
                    ("Arguments.Items[1].Code", ValidationCodes.MaxLength),
                    ("Arguments.Level", ValidationCodes.Range),
                    ("Arguments.Title", ValidationCodes.Required),
                ],
                Paths(errors));
        }

        /// <summary>Each attribute maps to its code: a length past any maximum is MaxLength, anything else Validation.Invalid naming the attribute.</summary>
        [Fact]
        public void Each_attribute_maps_to_its_code()
        {
            Assert.Equal(ValidationCodes.MaxLength, AttributeWalker.Failure(new StringLengthAttribute(3), "Code", "ABCD").Code);
            (string code, Dictionary<string, object?> arguments) = AttributeWalker.Failure(new StringLengthAttribute(3) { MinimumLength = 2 }, "Code", "A");
            Assert.Equal((ValidationCodes.Invalid, "StringLength"), (code, (string)arguments["attribute"]!));
            Assert.Equal(ValidationCodes.MaxLength, AttributeWalker.Failure(new LengthAttribute(1, 2), "Ids", new List<int> { 1, 2, 3 }).Code);
            Assert.Equal(ValidationCodes.Invalid, AttributeWalker.Failure(new LengthAttribute(2, 3), "Ids", new List<int> { 1 }).Code);
            (string range, Dictionary<string, object?> bounds) = AttributeWalker.Failure(new RangeAttribute(typeof(decimal), "0.5", "1.5"), "Rate", 2m);
            Assert.Equal((ValidationCodes.Range, "0.5", "1.5"), (range, bounds["minimum"], bounds["maximum"]));
            Assert.Equal(AttributeWalker.InvalidCode, AttributeWalker.Failure(new UrlAttribute(), "Site", "x").Code);
        }

        // ------------------------------------------------------------------
        // The payload's shape
        // ------------------------------------------------------------------

        /// <summary>An empty payload or a null row is malformed.</summary>
        [Fact]
        public void An_empty_payload_or_a_null_row_is_malformed()
        {
            VmWidget withNullLine = Widget();
            withNullLine.Lines = [null!];

            _ = Assert.Throws<BadRequestException>(() => Shape([]));
            _ = Assert.Throws<BadRequestException>(() => Shape([null!]));
            _ = Assert.Throws<BadRequestException>(() => Shape([withNullLine]));
        }

        /// <summary>A payload beyond a ceiling is refused at once, naming the ceiling, the count and the maximum.</summary>
        [Fact]
        public void A_payload_beyond_a_ceiling_is_refused()
        {
            StackLimits limits = StackLimits.Default with { MaxSaveCount = 2, MaxRowsPerSave = 5 };
            VmWidget crowded = Widget();
            crowded.Lines = [new VmWidgetLine { Units = 1 }, new VmWidgetLine { Units = 1 }, new VmWidgetLine { Units = 1 }, new VmWidgetLine { Units = 1 }];
            VmWidget two = Widget();
            two.Lines = [new VmWidgetLine { Units = 1 }, new VmWidgetLine { Units = 1 }];

            LimitExceededException count = Assert.Throws<LimitExceededException>(() => Shape([Widget(), Widget(), Widget()], limits));
            LimitExceededException children = Assert.Throws<LimitExceededException>(() => Shape([crowded], limits));
            LimitExceededException rows = Assert.Throws<LimitExceededException>(() => Shape([two, two], limits));

            Assert.Equal((LimitNames.MaxSaveCount, 3, 2), (count.Limit, count.Actual, count.Maximum));
            Assert.Equal((LimitNames.MaxChildren, 4, 3), (children.Limit, children.Actual, children.Maximum));
            Assert.Equal((LimitNames.MaxRowsPerSave, 6, 5), (rows.Limit, rows.Actual, rows.Maximum));
        }

        /// <summary>A repeated non-zero id of one entity type is reported at every repeat; new rows (0) may repeat, and types do not mix.</summary>
        [Fact]
        public void A_repeated_id_of_one_entity_type_is_reported()
        {
            VmWidget[] widgets = [Widget(id: 0), Widget(id: 0), Widget(id: -1), Widget(id: -1), Widget(id: 8), Widget(id: 8), Widget(id: 8)];
            widgets[0].Lines = [new VmWidgetLine { Id = 8, Units = 1 }];
            widgets[2].Lines = [new VmWidgetLine { Id = 8, Units = 1 }];

            ValidationErrors errors = new();
            SaveShape.Check(widgets, _rig.Of<VmWidget>(), StackLimits.Default, _walker, errors);

            Assert.Equal(
                [("[2].Lines[0].Id", 8L), ("[3].Id", -1L), ("[5].Id", 8L), ("[6].Id", 8L)],
                errors.Items.Select(static error => (error.Path.ToString(), (long)error.Arguments["id"]!)).OrderBy(static error => error.Item1, StringComparer.Ordinal));
            Assert.All(errors.Items, static error => Assert.Equal(ValidationCodes.EntityDuplicateId, error.Code));
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _rig.Dispose();
        }

        /// <summary>A valid widget with the given values.</summary>
        /// <param name="id">The id.</param>
        /// <param name="code">The code.</param>
        /// <param name="name">The name.</param>
        /// <param name="quantity">The quantity.</param>
        /// <param name="email">The e-mail address.</param>
        /// <param name="price">The price.</param>
        /// <returns>The widget.</returns>
        private static VmWidget Widget(int id = 0, string code = "W1", string? name = "Widget", int quantity = 1, string? email = null, decimal? price = null)
        {
            return new VmWidget { Id = id, Code = code, Name = name, Quantity = quantity, Email = email, Price = price };
        }

        /// <summary>The path and code of every error, in the ordinal order of their paths.</summary>
        /// <param name="errors">The errors.</param>
        /// <returns>The paths and codes.</returns>
        private static IEnumerable<(string Path, string Code)> Paths(ValidationErrors errors)
        {
            return errors.Items.Select(static error => (error.Path.ToString(), error.Code)).OrderBy(static error => error.Item1, StringComparer.Ordinal);
        }

        /// <summary>The one error at a path.</summary>
        /// <param name="errors">The errors.</param>
        /// <param name="path">The path's bracket form.</param>
        /// <returns>The error.</returns>
        private static ValidationError Find(ValidationErrors errors, string path)
        {
            return Assert.Single(errors.Items, error => error.Path.ToString() == path);
        }

        /// <summary>Checks a one-widget payload's shape and values.</summary>
        /// <param name="widget">The widget.</param>
        /// <returns>The failures.</returns>
        private ValidationErrors Check(VmWidget widget)
        {
            ValidationErrors errors = new();
            SaveShape.Check([widget], _rig.Of<VmWidget>(), StackLimits.Default, _walker, errors);
            return errors;
        }

        /// <summary>Checks a payload's shape and values, under the given ceilings.</summary>
        /// <param name="widgets">The payload.</param>
        /// <param name="limits">The ceilings; the defaults when null.</param>
        /// <returns>The failures.</returns>
        private ValidationErrors Shape(IReadOnlyList<VmWidget> widgets, StackLimits? limits = null)
        {
            ValidationErrors errors = new();
            SaveShape.Check(widgets, _rig.Of<VmWidget>(), limits ?? StackLimits.Default, _walker, errors);
            return errors;
        }
    }
}
