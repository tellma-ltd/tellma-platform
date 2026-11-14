// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Tellma.Core.Abstractions.Composition;

namespace Tellma.Core.Abstractions.Tests.PublicApi
{
    /// <summary>
    ///     Renders the public surface of one namespace as text, one line per type and member, in a
    ///     deterministic order, so a change to the surface shows as a diff of the stored golden.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Everything a caller in another assembly can name is rendered: exported types, and their
    ///         public and protected members, with nullability, constraints, default values, constant
    ///         values and the attributes that change how a member is used (flags, obsolescence, editor
    ///         visibility, collection services, attribute usage). Internal and private members, explicit
    ///         interface implementations and compiler-generated names are not.
    ///     </para>
    ///     <para>
    ///         Types are rendered by their simple names, nested ones as <c>Outer.Inner</c>; tuples by
    ///         their element types and names. Reflection order is not stable, so types and members are sorted.
    ///     </para>
    /// </remarks>
    internal static class PublicApiRenderer
    {
        /// <summary>Every member of a type, declared on it, whatever its accessibility.</summary>
        private const BindingFlags Declared =
            BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        /// <summary>The C# keywords of the built-in types.</summary>
        private static readonly Dictionary<Type, string> Keywords = new()
        {
            [typeof(void)] = "void",
            [typeof(object)] = "object",
            [typeof(string)] = "string",
            [typeof(bool)] = "bool",
            [typeof(byte)] = "byte",
            [typeof(sbyte)] = "sbyte",
            [typeof(short)] = "short",
            [typeof(ushort)] = "ushort",
            [typeof(int)] = "int",
            [typeof(uint)] = "uint",
            [typeof(long)] = "long",
            [typeof(ulong)] = "ulong",
            [typeof(float)] = "float",
            [typeof(double)] = "double",
            [typeof(decimal)] = "decimal",
            [typeof(char)] = "char",
        };

        /// <summary>Renders the public surface of one namespace of an assembly.</summary>
        /// <param name="assembly">The assembly.</param>
        /// <param name="ns">The namespace; nested namespaces are not included.</param>
        /// <returns>The surface, one line per type and member, with LF line ends.</returns>
        public static string Render(Assembly assembly, string ns)
        {
            StringBuilder text = new();

            foreach (Type type in assembly.GetExportedTypes()
                .Where(type => type.Namespace == ns)
                .OrderBy(TypeName, StringComparer.Ordinal))
            {
                AppendType(text, type);
            }

            return text.ToString();
        }

        /// <summary>Every type a namespace's public surface names: bases, interfaces, member, parameter and constraint types, and their generic arguments.</summary>
        /// <param name="assembly">The assembly.</param>
        /// <param name="ns">The namespace.</param>
        /// <returns>The types, each generic one with its definition and arguments.</returns>
        public static IReadOnlySet<Type> ReferencedTypes(Assembly assembly, string ns)
        {
            HashSet<Type> found = [];
            foreach (Type type in assembly.GetExportedTypes().Where(type => type.Namespace == ns))
            {
                Collect(found, type.BaseType);
                foreach (Type face in type.GetInterfaces())
                {
                    Collect(found, face);
                }

                foreach (Type argument in type.IsGenericTypeDefinition ? type.GetGenericArguments() : [])
                {
                    Array.ForEach(argument.GetGenericParameterConstraints(), constraint => Collect(found, constraint));
                }

                foreach (MemberInfo member in type.GetMembers(Declared).Where(IsVisible))
                {
                    CollectMember(found, member);
                }
            }

            return found;
        }

        // --- Types ---

        /// <summary>Appends one type: its attributes, its declaration and its members.</summary>
        /// <param name="text">The text.</param>
        /// <param name="type">The type.</param>
        private static void AppendType(StringBuilder text, Type type)
        {
            foreach (string attribute in Attributes(type))
            {
                _ = text.Append(attribute).Append('\n');
            }

            _ = text.Append(Declaration(type)).Append('\n');

            // Members are sorted by their rendered line: reflection order differs between runtimes.
            IEnumerable<string> members = type.IsEnum
                ? EnumMembers(type)
                : typeof(Delegate).IsAssignableFrom(type)
                    ? []
                    : type.GetMembers(Declared).Where(IsVisible).SelectMany(Member);

            foreach (string member in members.Order(StringComparer.Ordinal))
            {
                _ = text.Append("    ").Append(member).Append('\n');
            }

            _ = text.Append('\n');
        }

        /// <summary>Renders a type's declaration line.</summary>
        /// <param name="type">The type.</param>
        /// <returns>The line.</returns>
        private static string Declaration(Type type)
        {
            StringBuilder line = new("public ");
            bool isRecord = type.GetMethod("<Clone>$", Declared) is not null || IsRecordStruct(type);

            if (type.IsInterface)
            {
                _ = line.Append("interface ");
            }
            else if (type.IsEnum)
            {
                _ = line.Append("enum ");
            }
            else if (typeof(Delegate).IsAssignableFrom(type))
            {
                MethodInfo invoke = type.GetMethod("Invoke")!;
                return line.Append("delegate ").Append(Name(invoke.ReturnType)).Append(' ').Append(TypeName(type))
                    .Append('(').Append(string.Join(", ", invoke.GetParameters().Select(parameter => Name(parameter.ParameterType)))).Append(')')
                    .ToString();
            }
            else if (type.IsValueType)
            {
                _ = line.Append(isRecord ? "record struct " : "struct ");
            }
            else
            {
                // A static class is abstract and sealed in metadata.
                _ = line.Append(type is { IsAbstract: true, IsSealed: true } ? "static " : type.IsAbstract ? "abstract " : type.IsSealed ? "sealed " : string.Empty);
                _ = line.Append(isRecord ? "record " : "class ");
            }

            _ = line.Append(TypeName(type));

            // Bases: the base class unless implicit, then every interface, sorted.
            List<string> bases = [];
            if (type.BaseType is { } baseType && baseType != typeof(object) && baseType != typeof(ValueType) && baseType != typeof(Enum))
            {
                bases.Add(Name(baseType));
            }

            if (type.IsEnum)
            {
                bases.Add(Name(Enum.GetUnderlyingType(type)));
            }
            else
            {
                bases.AddRange(type.GetInterfaces().Select(face => Name(face)).Order(StringComparer.Ordinal));
            }

            if (bases.Count > 0)
            {
                _ = line.Append(" : ").Append(string.Join(", ", bases));
            }

            return line.Append(Constraints(OwnGenericArguments(type))).ToString();
        }

        /// <summary>Whether a value type is a record struct.</summary>
        /// <param name="type">The type.</param>
        /// <returns>True for a record struct.</returns>
        private static bool IsRecordStruct(Type type)
        {
            return type.IsValueType && type.GetMethod("PrintMembers", Declared) is not null;
        }

        /// <summary>The members of an enum, by value.</summary>
        /// <param name="type">The enum.</param>
        /// <returns>The member lines.</returns>
        private static IEnumerable<string> EnumMembers(Type type)
        {
            return type.GetFields(BindingFlags.Public | BindingFlags.Static).Select(field => string.Create(
                CultureInfo.InvariantCulture,
                $"{field.Name} = {Convert.ToInt64(field.GetRawConstantValue(), CultureInfo.InvariantCulture)}"));
        }

        // --- Members ---

        /// <summary>Whether a member is part of the public surface: public or protected, and not compiler-generated.</summary>
        /// <param name="member">The member.</param>
        /// <returns>True when visible.</returns>
        private static bool IsVisible(MemberInfo member)
        {
            // An unspeakable name (a record's clone method, a backing field) is the compiler's own.
            return !member.Name.StartsWith('<') && member switch
            {
                ConstructorInfo { IsStatic: true } => false,
                MethodBase method => !IsAccessor(method) && (method.IsPublic || method.IsFamily || method.IsFamilyOrAssembly),
                PropertyInfo property => property.GetAccessors(nonPublic: true).Any(IsVisibleAccessor),
                FieldInfo field => field.IsPublic || field.IsFamily || field.IsFamilyOrAssembly,
                EventInfo @event => @event.AddMethod is { } add && IsVisibleAccessor(add),
                _ => false,
            };
        }

        /// <summary>Whether a method is a property or event accessor rather than a member of its own.</summary>
        /// <param name="method">The method.</param>
        /// <returns>True for an accessor.</returns>
        private static bool IsAccessor(MethodBase method)
        {
            return method.IsSpecialName && !method.Name.StartsWith("op_", StringComparison.Ordinal) && method is not ConstructorInfo;
        }

        /// <summary>Whether an accessor is callable from another assembly.</summary>
        /// <param name="accessor">The accessor.</param>
        /// <returns>True when public or protected.</returns>
        private static bool IsVisibleAccessor(MethodInfo accessor)
        {
            return accessor.IsPublic || accessor.IsFamily || accessor.IsFamilyOrAssembly;
        }

        /// <summary>Renders one member, preceded by its rendered attributes.</summary>
        /// <param name="member">The member.</param>
        /// <returns>The member's lines.</returns>
        private static IEnumerable<string> Member(MemberInfo member)
        {
            string prefix = string.Concat(Attributes(member).Select(static attribute => attribute + " "));
            string line = member switch
            {
                ConstructorInfo constructor => Access(constructor) + "ctor(" + Parameters(constructor) + ")",
                MethodInfo method => Method(method),
                PropertyInfo property => Property(property),
                FieldInfo field => Field(field),
                EventInfo @event => "public event " + Name(@event.EventHandlerType!, Annotations.Of(@event)) + " " + @event.Name,
                _ => throw new InvalidOperationException("Unexpected member kind " + member.MemberType + "."),
            };

            return [prefix + line];
        }

        /// <summary>Renders a method.</summary>
        /// <param name="method">The method.</param>
        /// <returns>The line.</returns>
        private static string Method(MethodInfo method)
        {
            string generics = method.IsGenericMethodDefinition ? "<" + string.Join(", ", method.GetGenericArguments().Select(static argument => argument.Name)) + ">" : string.Empty;
            string returns = Name(method.ReturnType, Annotations.Of(method.ReturnParameter));
            return Access(method) + Modifiers(method) + returns + " " + method.Name + generics + "(" + Parameters(method) + ")"
                + Constraints(method.IsGenericMethodDefinition ? method.GetGenericArguments() : []);
        }

        /// <summary>Renders a property or an indexer.</summary>
        /// <param name="property">The property.</param>
        /// <returns>The line.</returns>
        private static string Property(PropertyInfo property)
        {
            MethodInfo accessor = property.GetAccessors(nonPublic: true).First(IsVisibleAccessor);
            string required = property.IsDefined(typeof(RequiredMemberAttribute)) ? "required " : string.Empty;
            ParameterInfo[] index = property.GetIndexParameters();
            string name = index.Length == 0 ? property.Name : "this[" + string.Join(", ", index.Select(parameter => Name(parameter.ParameterType, Annotations.Of(parameter)))) + "]";

            // Each visible accessor, with its own accessibility when it differs from the property's.
            List<string> accessors = [];
            if (property.GetMethod is { } getter && IsVisibleAccessor(getter))
            {
                accessors.Add(AccessorAccess(getter, accessor) + "get;");
            }

            if (property.SetMethod is { } setter && IsVisibleAccessor(setter))
            {
                bool init = setter.ReturnParameter.GetRequiredCustomModifiers().Contains(typeof(IsExternalInit));
                accessors.Add(AccessorAccess(setter, accessor) + (init ? "init;" : "set;"));
            }

            return Access(accessor) + Modifiers(accessor) + required + Name(property.PropertyType, Annotations.Of(property)) + " " + name
                + " { " + string.Join(" ", accessors) + " }";
        }

        /// <summary>Renders a field: a constant with its value, or a static or read-only field.</summary>
        /// <param name="field">The field.</param>
        /// <returns>The line.</returns>
        private static string Field(FieldInfo field)
        {
            string type = Name(field.FieldType, Annotations.Of(field));
            return field.IsLiteral
                ? Access(field) + "const " + type + " " + field.Name + " = " + Literal(field.GetRawConstantValue(), field.FieldType)
                : Access(field) + (field.IsStatic ? "static " : string.Empty) + (field.IsInitOnly ? "readonly " : string.Empty) + type + " " + field.Name;
        }

        /// <summary>A member's accessibility keyword and a space.</summary>
        /// <param name="member">The member: a method, constructor or field.</param>
        /// <returns>The keyword.</returns>
        private static string Access(MemberInfo member)
        {
            return member switch
            {
                MethodBase { IsPublic: true } or FieldInfo { IsPublic: true } => "public ",
                MethodBase { IsFamilyOrAssembly: true } or FieldInfo { IsFamilyOrAssembly: true } => "protected internal ",
                _ => "protected ",
            };
        }

        /// <summary>An accessor's accessibility when it differs from the property's.</summary>
        /// <param name="accessor">The accessor.</param>
        /// <param name="property">The accessor that sets the property's accessibility.</param>
        /// <returns>The keyword and a space, or nothing.</returns>
        private static string AccessorAccess(MethodInfo accessor, MethodInfo property)
        {
            return Access(accessor) == Access(property) ? string.Empty : Access(accessor);
        }

        /// <summary>A method's modifiers: static, abstract, virtual, override, sealed override.</summary>
        /// <param name="method">The method.</param>
        /// <returns>The modifiers, each followed by a space.</returns>
        private static string Modifiers(MethodInfo method)
        {
            if (method.IsStatic)
            {
                return "static ";
            }

            // An override's base definition is declared on another type.
            bool overrides = method.GetBaseDefinition().DeclaringType != method.DeclaringType;
            return method switch
            {
                { IsAbstract: true } => method.DeclaringType!.IsInterface ? string.Empty : "abstract ",
                _ when overrides => method.IsFinal ? "sealed override " : "override ",
                { IsVirtual: true, IsFinal: false } => method.DeclaringType!.IsInterface ? "default " : "virtual ",
                _ => string.Empty,
            };
        }

        /// <summary>Renders a method's parameters.</summary>
        /// <param name="method">The method or constructor.</param>
        /// <returns>The parameter list, without parentheses.</returns>
        private static string Parameters(MethodBase method)
        {
            bool extension = method.IsDefined(typeof(ExtensionAttribute));
            return string.Join(", ", method.GetParameters().Select((parameter, position) =>
            {
                string modifier = extension && position == 0 ? "this "
                    : parameter.IsDefined(typeof(ParamArrayAttribute)) ? "params "
                    : parameter.IsOut ? "out "
                    : parameter.IsIn ? "in "
                    : parameter.ParameterType.IsByRef ? "ref "
                    : string.Empty;
                string value = parameter.HasDefaultValue ? " = " + DefaultValue(parameter) : string.Empty;
                return modifier + Name(parameter.ParameterType, Annotations.Of(parameter)) + " " + parameter.Name + value;
            }));
        }

        /// <summary>Renders a parameter's default value.</summary>
        /// <param name="parameter">The parameter.</param>
        /// <returns>The value.</returns>
        private static string DefaultValue(ParameterInfo parameter)
        {
            Type type = Nullable.GetUnderlyingType(parameter.ParameterType) ?? parameter.ParameterType;
            return parameter.RawDefaultValue is null or DBNull
                ? type.IsValueType && Nullable.GetUnderlyingType(parameter.ParameterType) is null ? "default" : "null"
                : Literal(parameter.RawDefaultValue, type);
        }

        /// <summary>Renders a constant value.</summary>
        /// <param name="value">The raw value.</param>
        /// <param name="type">The value's declared type.</param>
        /// <returns>The value as C#.</returns>
        private static string Literal(object? value, Type type)
        {
            return value switch
            {
                null => "null",
                string text => "\"" + text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"",
                bool flag => flag ? "true" : "false",
                char character => "'" + character + "'",
                // A flags combination renders as its members joined, as C# would spell it.
                _ when type.IsEnum => string.Join(
                    " | ",
                    Enum.ToObject(type, value).ToString()!.Split(", ").Select(member => Name(type) + "." + member)),
                IFormattable number => number.ToString(null, CultureInfo.InvariantCulture),
                _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
            };
        }

        /// <summary>Renders the constraints of generic parameters.</summary>
        /// <param name="arguments">The generic parameters.</param>
        /// <returns>The where clauses, each preceded by a space; nothing when unconstrained.</returns>
        private static string Constraints(IEnumerable<Type> arguments)
        {
            StringBuilder clauses = new();
            foreach (Type argument in arguments)
            {
                List<string> parts = [];
                GenericParameterAttributes flags = argument.GenericParameterAttributes;

                // A struct constraint is recorded as a value-type constraint plus a default constructor and an implicit ValueType base.
                bool isStruct = flags.HasFlag(GenericParameterAttributes.NotNullableValueTypeConstraint);
                if (isStruct)
                {
                    parts.Add("struct");
                }
                else if (flags.HasFlag(GenericParameterAttributes.ReferenceTypeConstraint))
                {
                    parts.Add(Annotations.IsNullable(argument) is true ? "class?" : "class");
                }

                Type[] types = argument.GetGenericParameterConstraints();
                parts.AddRange(types
                    .Where(constraint => !(isStruct && constraint == typeof(ValueType)))
                    .Select(constraint => Name(constraint))
                    .Order(StringComparer.Ordinal));

                // notnull is recorded only as the parameter's own annotation, so it shows when nothing else constrains it.
                if (parts.Count == 0 && types.Length == 0 && Annotations.IsNullable(argument) is false)
                {
                    parts.Add("notnull");
                }

                if (!isStruct && flags.HasFlag(GenericParameterAttributes.DefaultConstructorConstraint))
                {
                    parts.Add("new()");
                }

                if (parts.Count > 0)
                {
                    _ = clauses.Append(" where ").Append(argument.Name).Append(" : ").Append(string.Join(", ", parts));
                }
            }

            return clauses.ToString();
        }

        // --- Attributes ---

        /// <summary>The attributes of a type or member that change how it is used, rendered as C#.</summary>
        /// <param name="target">The type or member.</param>
        /// <returns>The attributes, sorted.</returns>
        private static List<string> Attributes(MemberInfo target)
        {
            List<string> found = [];
            if (target.GetCustomAttribute<FlagsAttribute>() is not null)
            {
                found.Add("[Flags]");
            }

            if (target.GetCustomAttribute<ObsoleteAttribute>() is { } obsolete && !target.IsDefined(typeof(CompilerFeatureRequiredAttribute)))
            {
                found.Add("[Obsolete(" + Literal(obsolete.Message, typeof(string)) + (obsolete.IsError ? ", error: true" : string.Empty) + ")]");
            }

            if (target.GetCustomAttribute<EditorBrowsableAttribute>() is { } browsable)
            {
                found.Add("[EditorBrowsable(EditorBrowsableState." + browsable.State + ")]");
            }

            if (target.GetCustomAttribute<CollectionServiceAttribute>(inherit: false) is not null)
            {
                found.Add("[CollectionService]");
            }

            if (target is Type type && typeof(Attribute).IsAssignableFrom(type) && type.GetCustomAttribute<AttributeUsageAttribute>() is { } usage)
            {
                found.Add("[AttributeUsage(" + Literal(usage.ValidOn, typeof(AttributeTargets))
                    + ", AllowMultiple = " + Literal(usage.AllowMultiple, typeof(bool)) + ", Inherited = " + Literal(usage.Inherited, typeof(bool)) + ")]");
            }

            found.Sort(StringComparer.Ordinal);
            return found;
        }

        // --- Type names ---

        /// <summary>A type's simple name with its own generic parameters: <c>Outer.Inner&lt;T&gt;</c>.</summary>
        /// <param name="type">The type.</param>
        /// <returns>The name.</returns>
        private static string TypeName(Type type)
        {
            string name = StripArity(type.Name);
            IReadOnlyList<Type> own = OwnGenericArguments(type);
            string generics = own.Count == 0 ? string.Empty : "<" + string.Join(", ", own.Select(static argument => argument.Name)) + ">";
            return (type.DeclaringType is { } outer ? TypeName(outer) + "." : string.Empty) + name + generics;
        }

        /// <summary>The generic parameters a type declares itself, not those of its declaring type.</summary>
        /// <param name="type">The type.</param>
        /// <returns>The parameters.</returns>
        private static IReadOnlyList<Type> OwnGenericArguments(Type type)
        {
            int inherited = type.DeclaringType?.GetGenericArguments().Length ?? 0;
            return [.. type.GetGenericArguments().Skip(inherited)];
        }

        /// <summary>Renders a type as C# names it, with the nullable annotations its declaration wrote.</summary>
        /// <param name="type">The type.</param>
        /// <param name="annotations">The declaration's annotations, read in step with the walk; null for none.</param>
        /// <returns>The name.</returns>
        private static string Name(Type type, Annotations? annotations = null)
        {
            annotations ??= Annotations.Oblivious();
            if (type.IsByRef)
            {
                return Name(type.GetElementType()!, annotations);
            }

            if (Nullable.GetUnderlyingType(type) is { } underlying)
            {
                return Name(underlying, annotations) + "?";
            }

            // The compiler writes one annotation per reference type and type parameter, in pre-order,
            // and a placeholder for a generic value type; a non-generic value type takes none.
            string mark = string.Empty;
            if (!type.IsValueType)
            {
                mark = annotations.Next() ? "?" : string.Empty;
            }
            else if (type.IsGenericType)
            {
                annotations.Skip();
            }

            if (type.IsArray)
            {
                return Name(type.GetElementType()!, annotations) + "[" + new string(',', type.GetArrayRank() - 1) + "]" + mark;
            }

            if (Keywords.TryGetValue(type, out string? keyword))
            {
                return keyword + mark;
            }

            if (type.IsGenericParameter)
            {
                return type.Name + mark;
            }

            if (!type.IsGenericType)
            {
                return TypeName(type) + mark;
            }

            // A tuple: its element names come first in the declaration's name list, then its elements'.
            Type[] arguments = type.GetGenericArguments();
            if (type.GetGenericTypeDefinition().FullName?.StartsWith("System.ValueTuple`", StringComparison.Ordinal) is true)
            {
                string?[] labels = annotations.TupleNames(arguments.Length);
                string[] elements = [.. arguments.Select((argument, position) =>
                    Name(argument, annotations) + (labels[position] is { } label ? " " + label : string.Empty))];
                return "(" + string.Join(", ", elements) + ")" + mark;
            }

            // Any other generic type, closed or open: its arguments in order, each with its own annotations.
            string[] names = [.. arguments.Select(argument => Name(argument, annotations))];

            string outer = type.DeclaringType is { } declaring ? StripArity(declaring.Name) + "." : string.Empty;
            return outer + StripArity(type.Name) + "<" + string.Join(", ", names) + ">" + mark;
        }

        /// <summary>Removes a generic type name's arity suffix.</summary>
        /// <param name="name">The metadata name, such as <c>List`1</c>.</param>
        /// <returns>The name without its suffix.</returns>
        private static string StripArity(string name)
        {
            int tick = name.IndexOf('`', StringComparison.Ordinal);
            return tick < 0 ? name : name[..tick];
        }

        // --- Nullable annotations ---

        /// <summary>
        ///     Reads one declaration's nullable annotations as the compiler wrote them, a position at a
        ///     time, so a type renders as declared.
        /// </summary>
        /// <remarks>
        ///     The runtime's nullability reader substitutes a type parameter's own constraint for an
        ///     unannotated use of it, which would render every unconstrained <c>T</c> as <c>T?</c>; the
        ///     declared annotation is read here instead.
        /// </remarks>
        private sealed class Annotations
        {
            /// <summary>The value of an annotated position.</summary>
            private const byte Annotated = 2;

            /// <summary>The per-position annotations; null when the declaration carries none.</summary>
            private readonly IReadOnlyList<byte>? positions;

            /// <summary>The annotation of every position the declaration does not annotate itself.</summary>
            private readonly byte fallback;

            /// <summary>The next position.</summary>
            private int index;

            /// <summary>The declaration's tuple element names, in order; null when it names none.</summary>
            private readonly IReadOnlyList<string?>? tupleNames;

            /// <summary>The next tuple element name.</summary>
            private int tupleIndex;

            /// <summary>Creates a reader.</summary>
            /// <param name="attributes">The declaration's attributes.</param>
            /// <param name="scope">The innermost member whose annotation context applies; null for none.</param>
            private Annotations(IEnumerable<CustomAttributeData> attributes, MemberInfo? scope)
            {
                positions = Bytes(attributes, "System.Runtime.CompilerServices.NullableAttribute");
                fallback = Context(scope);
                tupleNames = attributes
                    .FirstOrDefault(candidate => candidate.AttributeType == typeof(TupleElementNamesAttribute))?
                    .ConstructorArguments[0].Value is IReadOnlyCollection<CustomAttributeTypedArgument> labels
                    ? [.. labels.Select(static label => (string?)label.Value)]
                    : null;
            }

            /// <summary>A reader of a declaration that annotates nothing.</summary>
            /// <returns>The reader.</returns>
            public static Annotations Oblivious()
            {
                return new([], null);
            }

            /// <summary>A reader of a property's type.</summary>
            /// <param name="property">The property.</param>
            /// <returns>The reader.</returns>
            public static Annotations Of(PropertyInfo property)
            {
                return new(property.CustomAttributes, property.DeclaringType);
            }

            /// <summary>A reader of a parameter's or return value's type.</summary>
            /// <param name="parameter">The parameter.</param>
            /// <returns>The reader.</returns>
            public static Annotations Of(ParameterInfo parameter)
            {
                return new(parameter.CustomAttributes, parameter.Member);
            }

            /// <summary>A reader of a field's type.</summary>
            /// <param name="field">The field.</param>
            /// <returns>The reader.</returns>
            public static Annotations Of(FieldInfo field)
            {
                return new(field.CustomAttributes, field.DeclaringType);
            }

            /// <summary>A reader of an event's handler type.</summary>
            /// <param name="event">The event.</param>
            /// <returns>The reader.</returns>
            public static Annotations Of(EventInfo @event)
            {
                return new(@event.CustomAttributes, @event.DeclaringType);
            }

            /// <summary>Whether a generic parameter is declared nullable: <c>class?</c>, or unconstrained.</summary>
            /// <param name="parameter">The generic parameter.</param>
            /// <returns>True when nullable, false when not, null when oblivious.</returns>
            public static bool? IsNullable(Type parameter)
            {
                byte value = Bytes(parameter.CustomAttributes, "System.Runtime.CompilerServices.NullableAttribute") is [byte own]
                    ? own
                    : Context((MemberInfo?)parameter.DeclaringMethod ?? parameter.DeclaringType);
                return value == 0 ? null : value == Annotated;
            }

            /// <summary>Reads the next position.</summary>
            /// <returns>True when the position is annotated nullable.</returns>
            public bool Next()
            {
                byte value = positions switch
                {
                    null => fallback,
                    { Count: 1 } => positions[0],
                    _ => index < positions.Count ? positions[index] : fallback,
                };

                index++;
                return value == Annotated;
            }

            /// <summary>Reads the element names of the next tuple.</summary>
            /// <param name="count">The tuple's element count.</param>
            /// <returns>The names, null for an unnamed element.</returns>
            public string?[] TupleNames(int count)
            {
                string?[] labels = new string?[count];
                for (int position = 0; position < count; position++, tupleIndex++)
                {
                    labels[position] = tupleNames is not null && tupleIndex < tupleNames.Count ? tupleNames[tupleIndex] : null;
                }

                return labels;
            }

            /// <summary>Passes over a placeholder position.</summary>
            public void Skip()
            {
                index++;
            }

            /// <summary>The annotation context in force for a member: its own, else its declaring types'.</summary>
            /// <param name="scope">The member; null for none.</param>
            /// <returns>The context's annotation; 0 when oblivious.</returns>
            private static byte Context(MemberInfo? scope)
            {
                for (MemberInfo? member = scope; member is not null; member = member.DeclaringType)
                {
                    if (Bytes(member.CustomAttributes, "System.Runtime.CompilerServices.NullableContextAttribute") is [byte context])
                    {
                        return context;
                    }
                }

                return 0;
            }

            /// <summary>The byte or bytes an annotation attribute carries.</summary>
            /// <param name="attributes">The attributes to search.</param>
            /// <param name="name">The attribute's full name.</param>
            /// <returns>The bytes; null when the attribute is absent.</returns>
            private static IReadOnlyList<byte>? Bytes(IEnumerable<CustomAttributeData> attributes, string name)
            {
                CustomAttributeData? attribute = attributes.FirstOrDefault(candidate => candidate.AttributeType.FullName == name);
                return attribute?.ConstructorArguments[0].Value switch
                {
                    null => null,
                    byte single => [single],
                    IReadOnlyCollection<CustomAttributeTypedArgument> many => [.. many.Select(static item => (byte)item.Value!)],
                    _ => null,
                };
            }
        }

        // --- Referenced types ---

        /// <summary>Collects the types one member names.</summary>
        /// <param name="found">The types found so far.</param>
        /// <param name="member">The member.</param>
        private static void CollectMember(HashSet<Type> found, MemberInfo member)
        {
            switch (member)
            {
                case MethodBase method:
                    if (method is MethodInfo info)
                    {
                        Collect(found, info.ReturnType);
                    }

                    foreach (ParameterInfo parameter in method.GetParameters())
                    {
                        Collect(found, parameter.ParameterType);
                    }

                    break;
                case PropertyInfo property:
                    Collect(found, property.PropertyType);
                    break;
                case FieldInfo field:
                    Collect(found, field.FieldType);
                    break;
                case EventInfo @event:
                    Collect(found, @event.EventHandlerType);
                    break;
                default:
                    break;
            }

            foreach (CustomAttributeData attribute in member.CustomAttributes)
            {
                Collect(found, attribute.AttributeType);
            }
        }

        /// <summary>Collects a type and every type it is built from.</summary>
        /// <param name="found">The types found so far.</param>
        /// <param name="type">The type; null for none.</param>
        private static void Collect(HashSet<Type> found, Type? type)
        {
            if (type is null || type.IsGenericParameter || !found.Add(type))
            {
                return;
            }

            if (type.HasElementType)
            {
                Collect(found, type.GetElementType());
            }

            if (type.IsGenericType)
            {
                Collect(found, type.GetGenericTypeDefinition());
                foreach (Type argument in type.GetGenericArguments())
                {
                    Collect(found, argument);
                }
            }
        }
    }
}
