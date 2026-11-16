// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections;
using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Reflection;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Validation;

namespace Tellma.Core.Crud.Validation
{
    // Spec 0015 §6.1 step 1: every property is checked against its metadata first (a string beyond
    // MaxLength, a decimal beyond Precision and Scale, never rounded), then every BCL
    // ValidationAttribute is evaluated from cached metadata, [MaxLength] skipped on a mapped property
    // whose metadata check already reported it, all failures reported, IValidatableObject never
    // consulted. §11.2 and §11.3 walk an action's arguments and an [ApiAction] body the same way.
    /// <summary>
    ///     Checks the values of a payload row or a request body against the column facets of its
    ///     metadata and the validation attributes of its class, reporting every failure at its path.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Each class's plan, the properties to check and how, is built once and cached. Only the
    ///         values a client may set are checked on an entity row: a server-owned, derived or
    ///         database-owned property is reset or ignored later, and a child's parent key is set by
    ///         the nesting, so none of them is judged here.
    ///     </para>
    ///     <para>
    ///         A failure maps to a validation code: <c>[Required]</c> to <c>Required</c>,
    ///         <c>[Range]</c> to <c>Range</c>, a maximum length to <c>MaxLength</c>, and every other
    ///         attribute to <c>Validation.Invalid</c> naming the attribute.
    ///     </para>
    /// </remarks>
    internal sealed class AttributeWalker
    {
        /// <summary>The code of a failed attribute no other code describes.</summary>
        public const string InvalidCode = ValidationCodes.Invalid;

        /// <summary>Each entity's plan, by its metadata.</summary>
        private readonly ConcurrentDictionary<EntityMetadata, Plan> _entities = new(ReferenceEqualityComparer.Instance);

        /// <summary>Each body class's plan.</summary>
        private readonly ConcurrentDictionary<Type, Plan> _bodies = new();

        /// <summary>
        ///     Checks one entity row: its metadata facets, then its attributes. The row's child
        ///     collections are not walked: a payload walk visits each child row with its own metadata.
        /// </summary>
        /// <param name="row">The row.</param>
        /// <param name="metadata">The metadata of the row's entity.</param>
        /// <param name="path">The row's path, such as <c>[3]</c> or <c>[3].Lines[1]</c>.</param>
        /// <param name="parentKey">The row's parent key when it is a nested child, which the nesting sets; null for a root.</param>
        /// <param name="errors">Takes every failure.</param>
        public void CheckRow(object row, EntityMetadata metadata, ValidationPath path, PropertyMetadata? parentKey, ValidationErrors errors)
        {
            ArgumentNullException.ThrowIfNull(row);
            ArgumentNullException.ThrowIfNull(metadata);
            ArgumentNullException.ThrowIfNull(path);
            ArgumentNullException.ThrowIfNull(errors);
            Plan plan = _entities.GetOrAdd(metadata, static entity => Plan.ForEntity(entity));
            plan.Check(row, path, parentKey?.Name, errors);
        }

        /// <summary>
        ///     Checks a request body, such as an action's arguments, by its attributes, recursing into
        ///     its nested objects and lists of objects.
        /// </summary>
        /// <param name="body">The body.</param>
        /// <param name="path">The path its members are reported under: <see cref="ValidationPath.Root" />, or <c>Arguments</c>.</param>
        /// <param name="errors">Takes every failure.</param>
        public void CheckBody(object body, ValidationPath path, ValidationErrors errors)
        {
            ArgumentNullException.ThrowIfNull(body);
            ArgumentNullException.ThrowIfNull(path);
            ArgumentNullException.ThrowIfNull(errors);
            Walk(body, path, errors, new HashSet<object>(ReferenceEqualityComparer.Instance));
        }

        /// <summary>The code and arguments of one failed attribute.</summary>
        /// <param name="attribute">The attribute.</param>
        /// <param name="property">The property's CLR name, or null for an attribute on the class.</param>
        /// <param name="value">The value it judged.</param>
        /// <returns>The code and its arguments.</returns>
        internal static (string Code, Dictionary<string, object?> Arguments) Failure(ValidationAttribute attribute, string? property, object? value)
        {
            Dictionary<string, object?> arguments = new(StringComparer.Ordinal);
            if (property is not null)
            {
                arguments["property"] = property;
            }

            // --- The attributes a platform code describes; a length beyond a maximum is MaxLength whichever declared it ---
            switch (attribute)
            {
                case RequiredAttribute:
                    return (ValidationCodes.Required, arguments);
                case RangeAttribute range:
                    arguments["minimum"] = Raw(range.Minimum);
                    arguments["maximum"] = Raw(range.Maximum);
                    return (ValidationCodes.Range, arguments);
                case MaxLengthAttribute maxLength:
                    arguments["maxLength"] = maxLength.Length;
                    return (ValidationCodes.MaxLength, arguments);
                case StringLengthAttribute stringLength when value is string text && text.Length > stringLength.MaximumLength:
                    arguments["maxLength"] = stringLength.MaximumLength;
                    return (ValidationCodes.MaxLength, arguments);
                case LengthAttribute length when LengthOf(value) is int count && count > length.MaximumLength:
                    arguments["maxLength"] = length.MaximumLength;
                    return (ValidationCodes.MaxLength, arguments);
                default:
                    arguments["attribute"] = AttributeName(attribute);
                    return (InvalidCode, arguments);
            }
        }

        /// <summary>The name an attribute is written under, without its <c>Attribute</c> suffix.</summary>
        /// <param name="attribute">The attribute.</param>
        /// <returns>The name, such as <c>EmailAddress</c>.</returns>
        private static string AttributeName(ValidationAttribute attribute)
        {
            const string Suffix = "Attribute";
            string name = attribute.GetType().Name;
            return name.Length > Suffix.Length && name.EndsWith(Suffix, StringComparison.Ordinal) ? name[..^Suffix.Length] : name;
        }

        /// <summary>A range bound as a raw argument: a number stays a number, anything else is its invariant text.</summary>
        /// <param name="bound">The bound.</param>
        /// <returns>The argument.</returns>
        private static object? Raw(object? bound)
        {
            return bound switch
            {
                null or int or long or double or decimal or float or short or byte => bound,
                IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
                _ => bound.ToString(),
            };
        }

        /// <summary>The length a length attribute measures: a string's characters or a collection's count.</summary>
        /// <param name="value">The value.</param>
        /// <returns>The length, or null for a value with none.</returns>
        private static int? LengthOf(object? value)
        {
            return value switch
            {
                string text => text.Length,
                ICollection collection => collection.Count,
                _ => null,
            };
        }

        /// <summary>Checks one body object and recurses into its nested objects, each visited once.</summary>
        /// <param name="body">The object.</param>
        /// <param name="path">Its path.</param>
        /// <param name="errors">Takes every failure.</param>
        /// <param name="visited">The objects already walked, so a cycle ends.</param>
        private void Walk(object body, ValidationPath path, ValidationErrors errors, HashSet<object> visited)
        {
            if (!visited.Add(body))
            {
                return;
            }

            Plan plan = _bodies.GetOrAdd(body.GetType(), static type => Plan.ForBody(type));
            plan.Check(body, path, skip: null, errors);

            // --- Nested objects, and lists of them, are walked under their member's path ---
            foreach (PropertyInfo nested in plan.Nested)
            {
                object? value = nested.GetValue(body);
                ValidationPath member = path.Property(nested.Name);
                if (value is string or null)
                {
                    continue;
                }

                if (value is IEnumerable items)
                {
                    int index = 0;
                    foreach (object? item in items)
                    {
                        if (item is not null && Plan.IsWalkable(item.GetType()))
                        {
                            Walk(item, member.Index(index), errors, visited);
                        }

                        index++;
                    }
                }
                else if (Plan.IsWalkable(value.GetType()))
                {
                    Walk(value, member, errors, visited);
                }
            }
        }

        /// <summary>One class's checks: its columns' facets, its properties' attributes, its own attributes, and its nested members.</summary>
        private sealed class Plan
        {
            /// <summary>Creates a plan.</summary>
            /// <param name="facets">The column facets to check.</param>
            /// <param name="properties">The properties whose attributes to evaluate.</param>
            /// <param name="classAttributes">The attributes on the class itself.</param>
            /// <param name="nested">The members a body walk recurses into.</param>
            private Plan(Facet[] facets, Rule[] properties, ValidationAttribute[] classAttributes, PropertyInfo[] nested)
            {
                Facets = facets;
                Properties = properties;
                ClassAttributes = classAttributes;
                Nested = nested;
            }

            /// <summary>The column facets to check.</summary>
            public Facet[] Facets { get; }

            /// <summary>The properties whose attributes to evaluate.</summary>
            public Rule[] Properties { get; }

            /// <summary>The attributes on the class itself.</summary>
            public ValidationAttribute[] ClassAttributes { get; }

            /// <summary>The members a body walk recurses into.</summary>
            public PropertyInfo[] Nested { get; }

            /// <summary>Whether a body walk recurses into objects of a class: the application's own classes, not the framework's.</summary>
            /// <param name="type">The class.</param>
            /// <returns>True when it does.</returns>
            public static bool IsWalkable(Type type)
            {
                return type.IsClass && type != typeof(string) && !type.IsArray
                    && !(type.Namespace is string space && (space == "System" || space.StartsWith("System.", StringComparison.Ordinal) || space.StartsWith("Microsoft.", StringComparison.Ordinal)));
            }

            /// <summary>The plan of an entity row: the client-owned columns' facets and attributes, and the other members' attributes.</summary>
            /// <param name="metadata">The entity's metadata.</param>
            /// <returns>The plan.</returns>
            public static Plan ForEntity(EntityMetadata metadata)
            {
                // --- The columns a client sets; the others are reset, ignored or written by the database ---
                var mapped = metadata.Properties.ToDictionary(static property => property.Name, StringComparer.Ordinal);
                static bool ClientOwned(PropertyMetadata property)
                {
                    return property.Ownership is PropertyOwnership.Editable or PropertyOwnership.WriteOnce;
                }

                List<Facet> facets = [];
                foreach (PropertyMetadata property in metadata.Properties.Where(ClientOwned))
                {
                    Type type = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
                    if (type == typeof(string) && property.MaxLength is int maxLength)
                    {
                        facets.Add(new Facet(property.Name, property.Getter, FacetKind.MaxLength, maxLength, 0));
                    }
                    else if (type == typeof(decimal) && property.Precision is int precision && property.Scale is int scale)
                    {
                        facets.Add(new Facet(property.Name, property.Getter, FacetKind.Precision, precision, scale));
                    }
                }

                // --- Every member's attributes, a mapped column's only when a client sets it ---
                List<Rule> rules = [];
                foreach (PropertyInfo member in Members(metadata.ClrType))
                {
                    PropertyMetadata? column = mapped.GetValueOrDefault(member.Name);
                    if (column is not null && !ClientOwned(column))
                    {
                        continue;
                    }

                    // A mapped column's maximum length is its metadata's, which the facet check reported.
                    ValidationAttribute[] attributes = [.. member.GetCustomAttributes<ValidationAttribute>(inherit: true)
                        .Where(attribute => !(column is not null && attribute is MaxLengthAttribute))];
                    if (attributes.Length > 0)
                    {
                        Func<object, object?> getter = column is not null ? column.Getter : member.GetValue;
                        rules.Add(new Rule(member.Name, getter, attributes, column?.MaxLength));
                    }
                }

                return new Plan([.. facets], [.. rules], ClassAttributesOf(metadata.ClrType), []);
            }

            /// <summary>The plan of a body class: every member's attributes, and the members that may hold nested objects.</summary>
            /// <param name="type">The class.</param>
            /// <returns>The plan.</returns>
            public static Plan ForBody(Type type)
            {
                List<Rule> rules = [];
                List<PropertyInfo> nested = [];
                foreach (PropertyInfo member in Members(type))
                {
                    ValidationAttribute[] attributes = [.. member.GetCustomAttributes<ValidationAttribute>(inherit: true)];
                    if (attributes.Length > 0)
                    {
                        rules.Add(new Rule(member.Name, member.GetValue, attributes, ColumnMaxLength: null));
                    }

                    // A member that may hold an object of the application's, or a list of them.
                    Type declared = member.PropertyType;
                    Type? element = declared.IsArray ? declared.GetElementType() : declared.IsGenericType && typeof(IEnumerable).IsAssignableFrom(declared) ? declared.GetGenericArguments()[^1] : null;
                    if (declared == typeof(object) || IsWalkable(declared) || (element is not null && (element == typeof(object) || IsWalkable(element))))
                    {
                        nested.Add(member);
                    }
                }

                return new Plan([], [.. rules], ClassAttributesOf(type), [.. nested]);
            }

            /// <summary>Checks one object, recording every failure.</summary>
            /// <param name="instance">The object.</param>
            /// <param name="path">Its path.</param>
            /// <param name="skip">A member not to check, such as a child's parent key; null for none.</param>
            /// <param name="errors">Takes every failure.</param>
            public void Check(object instance, ValidationPath path, string? skip, ValidationErrors errors)
            {
                // --- The column facets, from the metadata ---
                foreach (Facet facet in Facets)
                {
                    if (string.Equals(facet.Property, skip, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    facet.Check(facet.Getter(instance), path.Property(facet.Property), errors);
                }

                // --- Each member's attributes, every failure reported ---
                foreach (Rule rule in Properties)
                {
                    if (string.Equals(rule.Property, skip, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    object? value = rule.Getter(instance);
                    ValidationContext context = new(instance, rule.Property, serviceProvider: null, items: null) { MemberName = rule.Property };
                    foreach (ValidationAttribute attribute in rule.Attributes)
                    {
                        // A length beyond a mapped column's maximum is the facet check's to report.
                        if (attribute is StringLengthAttribute && value is string text && rule.ColumnMaxLength is int column && text.Length > column)
                        {
                            continue;
                        }

                        if (attribute.GetValidationResult(value, context) is not null)
                        {
                            (string code, Dictionary<string, object?> arguments) = Failure(attribute, rule.Property, value);
                            errors.Add(path.Property(rule.Property), code, arguments);
                        }
                    }
                }

                // --- The class's own attributes, judged on the whole object ---
                if (ClassAttributes.Length > 0)
                {
                    ValidationContext context = new(instance, instance.GetType().Name, serviceProvider: null, items: null);
                    foreach (ValidationAttribute attribute in ClassAttributes)
                    {
                        if (attribute.GetValidationResult(instance, context) is not null)
                        {
                            (string code, Dictionary<string, object?> arguments) = Failure(attribute, property: null, instance);
                            errors.Add(path, code, arguments);
                        }
                    }
                }
            }

            /// <summary>The public readable instance properties of a class, indexers aside.</summary>
            /// <param name="type">The class.</param>
            /// <returns>The properties.</returns>
            private static IEnumerable<PropertyInfo> Members(Type type)
            {
                return type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Where(static member => member.CanRead && member.GetIndexParameters().Length == 0 && member.GetMethod is { IsPublic: true });
            }

            /// <summary>The validation attributes on a class itself.</summary>
            /// <param name="type">The class.</param>
            /// <returns>The attributes.</returns>
            private static ValidationAttribute[] ClassAttributesOf(Type type)
            {
                return [.. type.GetCustomAttributes<ValidationAttribute>(inherit: true)];
            }
        }

        /// <summary>What a column facet checks.</summary>
        private enum FacetKind
        {
            /// <summary>A string's length against the column's.</summary>
            MaxLength,

            /// <summary>A decimal's digits against the column's precision and scale.</summary>
            Precision,
        }

        /// <summary>One column facet of an entity, checked against its value.</summary>
        /// <param name="Property">The property's CLR name.</param>
        /// <param name="Getter">Reads the property.</param>
        /// <param name="Kind">What the facet checks.</param>
        /// <param name="Limit">The column's length, or its precision.</param>
        /// <param name="Scale">The column's scale, for a precision.</param>
        private sealed record Facet(string Property, Func<object, object?> Getter, FacetKind Kind, int Limit, int Scale)
        {
            /// <summary>Checks a value, recording a failure at its path.</summary>
            /// <param name="value">The value.</param>
            /// <param name="path">The property's path.</param>
            /// <param name="errors">Takes the failure.</param>
            public void Check(object? value, ValidationPath path, ValidationErrors errors)
            {
                if (Kind == FacetKind.MaxLength && value is string text && text.Length > Limit)
                {
                    errors.Add(path, ValidationCodes.MaxLength, new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["property"] = Property,
                        ["maxLength"] = Limit,
                    });
                }
                else if (Kind == FacetKind.Precision && value is decimal number && !Fits(number, Limit, Scale))
                {
                    errors.Add(path, ValidationCodes.Precision, new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["property"] = Property,
                        ["precision"] = Limit,
                        ["scale"] = Scale,
                    });
                }
            }

            /// <summary>
            ///     Whether a decimal fits a column without rounding: no more significant fractional
            ///     digits than the scale, and no more integer digits than the precision less the scale.
            /// </summary>
            /// <param name="number">The value.</param>
            /// <param name="precision">The column's precision.</param>
            /// <param name="scale">The column's scale.</param>
            /// <returns>True when it fits.</returns>
            internal static bool Fits(decimal number, int precision, int scale)
            {
                // The invariant text never uses an exponent, so its digits are the value's digits;
                // trailing fractional zeros are not significant (1.2300 stores as 1.23 unchanged).
                string text = Math.Abs(number).ToString(CultureInfo.InvariantCulture);
                int point = text.IndexOf('.', StringComparison.Ordinal);
                string integer = point < 0 ? text : text[..point];
                string fraction = point < 0 ? string.Empty : text[(point + 1)..].TrimEnd('0');
                int integerDigits = integer.TrimStart('0').Length;
                return fraction.Length <= scale && integerDigits <= precision - scale;
            }
        }

        /// <summary>One member's attributes.</summary>
        /// <param name="Property">The member's CLR name.</param>
        /// <param name="Getter">Reads the member.</param>
        /// <param name="Attributes">Its validation attributes, in declaration order.</param>
        /// <param name="ColumnMaxLength">The length of the column it maps to, or null for an unmapped member or an unsized column.</param>
        private sealed record Rule(string Property, Func<object, object?> Getter, ValidationAttribute[] Attributes, int? ColumnMaxLength);
    }
}
