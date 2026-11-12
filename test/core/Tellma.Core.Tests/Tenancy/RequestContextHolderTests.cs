// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using System.Buffers.Binary;
using System.Reflection;
using System.Reflection.Emit;
using Tellma.Core.Abstractions.Calendars;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Composition;
using Tellma.Core.Tenancy;

namespace Tellma.Core.Tests.Tenancy
{
    /// <summary>
    ///     The scoped holder of the request context: tenantless until bound, one instance per scope
    ///     for the reader and the writer, bound to one tenant for the scope's lifetime, and written by
    ///     the platform's two writers alone.
    /// </summary>
    public class RequestContextHolderTests
    {
        [Fact]
        public async Task A_scope_nothing_bound_reads_the_tenantless_context_stamped_once()
        {
            await using TenantContextHarness harness = await TenantContextHarness.CreateAsync();
            await using AsyncServiceScope scope = harness.Services.CreateAsyncScope();
            IRequestContextAccessor accessor = scope.ServiceProvider.GetRequiredService<IRequestContextAccessor>();

            RequestContext first = accessor.Current;
            harness.Time.Advance(TimeSpan.FromMinutes(5));
            RequestContext second = accessor.Current;

            // --- Tenantless, with the platform's defaults and the composed Gregorian system ---
            Assert.Null(first.Tenant);
            Assert.Equal(PrincipalKind.Anonymous, first.Kind);
            Assert.Null(first.UserId);
            Assert.Equal(("en", "en", "gc", "UTC", "UTC", "web"), (first.Language, first.Culture, first.Calendar, first.TimeZone, first.TenantTimeZone, first.Client));
            Assert.Same(harness.Services.GetRequiredService<ICalendarRegistry>()[CalendarCodes.Gregorian], first.CalendarSystem);

            // --- Stamped by the host's clock on the first read, and kept ---
            Assert.Equal(TenantContextHarness.Start, first.Now);
            Assert.Equal(DateOnly.FromDateTime(TenantContextHarness.Start.UtcDateTime), first.Today);
            Assert.Same(first, second);
        }

        [Fact]
        public async Task The_reader_and_the_writer_are_one_instance_per_scope()
        {
            await using TenantContextHarness harness = await TenantContextHarness.CreateAsync();
            await using AsyncServiceScope one = harness.Services.CreateAsyncScope();
            await using AsyncServiceScope other = harness.Services.CreateAsyncScope();

            IRequestContextHolder holder = one.ServiceProvider.GetRequiredService<IRequestContextHolder>();

            Assert.Same(holder, one.ServiceProvider.GetRequiredService<IRequestContextAccessor>());
            Assert.Same(holder, one.ServiceProvider.GetRequiredService<IRequestContextHolder>());
            Assert.NotSame(holder, other.ServiceProvider.GetRequiredService<IRequestContextHolder>());

            // Scoped: the root has no context to hand out.
            _ = Assert.Throws<InvalidOperationException>(harness.Services.GetRequiredService<IRequestContextAccessor>);
            _ = Assert.Throws<InvalidOperationException>(harness.Services.GetRequiredService<IRequestContextHolder>);
        }

        [Fact]
        public async Task A_context_set_in_one_scope_is_read_in_that_scope_alone()
        {
            await using TenantContextHarness harness = await TenantContextHarness.CreateAsync();
            await using AsyncServiceScope one = harness.Services.CreateAsyncScope();
            await using AsyncServiceScope other = harness.Services.CreateAsyncScope();
            IRequestContextHolder holder = one.ServiceProvider.GetRequiredService<IRequestContextHolder>();
            RequestContext bound = holder.Current with { Tenant = TenantContextHarness.Tenant(), Kind = PrincipalKind.User, Subject = "sub-1" };

            holder.Set(bound);

            Assert.Same(bound, one.ServiceProvider.GetRequiredService<IRequestContextAccessor>().Current);
            Assert.Null(other.ServiceProvider.GetRequiredService<IRequestContextAccessor>().Current.Tenant);
        }

        [Fact]
        public async Task A_scope_is_bound_before_it_is_ever_read()
        {
            await using TenantContextHarness harness = await TenantContextHarness.CreateAsync();
            await using AsyncServiceScope scope = harness.Services.CreateAsyncScope();
            IRequestContextHolder holder = scope.ServiceProvider.GetRequiredService<IRequestContextHolder>();
            RequestContext bound = harness.Services.GetRequiredService<RequestContextDefaults>().Tenantless() with { Tenant = TenantContextHarness.Tenant() };

            holder.Set(bound);

            Assert.Same(bound, holder.Current);
        }

        [Fact]
        public async Task A_bound_scope_keeps_its_tenant_for_its_whole_lifetime()
        {
            await using TenantContextHarness harness = await TenantContextHarness.CreateAsync();
            await using AsyncServiceScope scope = harness.Services.CreateAsyncScope();
            IRequestContextHolder holder = scope.ServiceProvider.GetRequiredService<IRequestContextHolder>();
            RequestContext tenantless = holder.Current;
            RequestContext bound = tenantless with { Tenant = TenantContextHarness.Tenant(id: 17) };
            holder.Set(bound);

            // --- Another tenant, or none, is refused and changes nothing ---
            InvalidOperationException another = Assert.Throws<InvalidOperationException>(
                () => holder.Set(bound with { Tenant = TenantContextHarness.Tenant(id: 18) }));
            InvalidOperationException none = Assert.Throws<InvalidOperationException>(() => holder.Set(tenantless));

            Assert.Contains("bound to tenant 17 and cannot be rebound to tenant 18", another.Message, StringComparison.Ordinal);
            Assert.Contains("cannot be rebound to no tenant", none.Message, StringComparison.Ordinal);
            Assert.Same(bound, holder.Current);

            // --- The same tenant is completed freely, a refreshed descriptor included ---
            RequestContext completed = bound with { UserId = 5, Tenant = TenantContextHarness.Tenant(TenantState.ReadOnly, id: 17) };
            holder.Set(completed);
            Assert.Same(completed, holder.Current);
        }

        [Fact]
        public async Task A_bound_scope_keeps_its_principal_and_its_user_for_its_whole_lifetime()
        {
            await using TenantContextHarness harness = await TenantContextHarness.CreateAsync();
            await using AsyncServiceScope scope = harness.Services.CreateAsyncScope();
            IRequestContextHolder holder = scope.ServiceProvider.GetRequiredService<IRequestContextHolder>();
            RequestContext bound = holder.Current with { Tenant = TenantContextHarness.Tenant(id: 17), Kind = PrincipalKind.User, Subject = "sub-1" };
            holder.Set(bound);

            // --- Another kind or another subject is refused, in a text that names no subject ---
            InvalidOperationException kind = Assert.Throws<InvalidOperationException>(() => holder.Set(bound with { Kind = PrincipalKind.System }));
            InvalidOperationException subject = Assert.Throws<InvalidOperationException>(() => holder.Set(bound with { Subject = "sub-2" }));
            InvalidOperationException noSubject = Assert.Throws<InvalidOperationException>(() => holder.Set(bound with { Subject = null }));

            Assert.Contains("bound to a principal of kind User in tenant 17 and cannot be rebound to another principal", kind.Message, StringComparison.Ordinal);
            Assert.Equal(kind.Message, subject.Message);
            Assert.Equal(kind.Message, noSubject.Message);
            Assert.DoesNotContain("sub-", subject.Message, StringComparison.Ordinal);
            Assert.Same(bound, holder.Current);

            // --- The user is unknown until the connection binds it, then it is bound once ---
            RequestContext connected = bound with { UserId = 5 };
            holder.Set(connected);
            InvalidOperationException another = Assert.Throws<InvalidOperationException>(() => holder.Set(connected with { UserId = 6 }));
            InvalidOperationException none = Assert.Throws<InvalidOperationException>(() => holder.Set(connected with { UserId = null }));

            Assert.Contains("bound to user 5 of tenant 17 and cannot be rebound to user 6", another.Message, StringComparison.Ordinal);
            Assert.Contains("bound to user 5 of tenant 17 and cannot be rebound to no user", none.Message, StringComparison.Ordinal);
            Assert.Same(connected, holder.Current);

            // --- Every other member is completed freely ---
            RequestContext completed = connected with { Language = "ar", Assurance = new AuthenticationAssurance("mfa", null, null) };
            holder.Set(completed);
            Assert.Same(completed, holder.Current);
        }

        [Fact]
        public async Task A_scope_bound_to_no_tenant_may_still_be_bound_to_any_principal()
        {
            // The rule starts with the tenant: a tenantless scope a front door read first is bound in
            // one write, whatever the tenantless context said of the principal.
            await using TenantContextHarness harness = await TenantContextHarness.CreateAsync();
            await using AsyncServiceScope scope = harness.Services.CreateAsyncScope();
            IRequestContextHolder holder = scope.ServiceProvider.GetRequiredService<IRequestContextHolder>();
            RequestContext tenantless = holder.Current;

            holder.Set(tenantless with { Kind = PrincipalKind.User, Subject = "sub-1" });
            RequestContext bound = tenantless with { Tenant = TenantContextHarness.Tenant(), Kind = PrincipalKind.ServiceAccount, Subject = "client-1", UserId = 9 };
            holder.Set(bound);

            Assert.Same(bound, holder.Current);
        }

        [Fact]
        public async Task The_holder_refuses_no_context()
        {
            await using TenantContextHarness harness = await TenantContextHarness.CreateAsync();
            await using AsyncServiceScope scope = harness.Services.CreateAsyncScope();

            _ = Assert.Throws<ArgumentNullException>(() => scope.ServiceProvider.GetRequiredService<IRequestContextHolder>().Set(null!));
        }

        [Fact]
        public void Only_the_two_writers_take_the_write_side_of_the_context()
        {
            // Named, not referenced: the web host's middleware is internal to it.
            const string TenantMiddleware = "Tellma.Core.AspNetCore.Tenancy.TenantMiddleware";

            // The context has exactly two writers: the access guard (with the tenant middleware in
            // front of it) and the tenant scope factory. Any other runtime type that stores, receives,
            // returns, resolves or calls the write side is a third writer in the making, so each one
            // found here has to be one of those two, the holder itself, or the code that registers and
            // shares them. The write side is public, so every platform assembly this suite ships with
            // is searched, not only the one that declares it: the web host's included, whose tenant
            // middleware binds the principal ahead of the guard.
            Type[] allowed =
            [
                typeof(IRequestContextHolder),
                typeof(RequestContextHolder),
                typeof(TenantAccessGuard),
                typeof(TenantScopeFactory),
                typeof(RequestContextInitialization),
                typeof(TenancyComposition),
            ];
            Assembly[] searched = PlatformAssemblies();

            string[] holders = [.. searched
                .SelectMany(static assembly => assembly.GetTypes())
                .Where(TakesTheWriteSide)
                .Select(TopLevel)
                .Distinct()
                .Select(static type => type.FullName!)
                .Order(StringComparer.Ordinal)];

            Assert.Contains(typeof(TellmaComposition).Assembly, searched);
            Assert.Contains(typeof(IRequestContextAccessor).Assembly, searched);
            Assert.Contains(searched, static assembly => assembly.GetName().Name == "Tellma.Core.AspNetCore");
            Assert.Equal(
                [.. allowed.Select(static type => type.FullName!).Append(TenantMiddleware).Order(StringComparer.Ordinal)],
                holders);
        }

        [Fact]
        public void The_search_for_writers_sees_a_write_made_without_a_local_a_field_or_a_parameter()
        {
            // The search is only as good as what it can see. Each of these takes the write side in a
            // shape that declares nothing of its type, and each has to be found.
            Assert.True(TakesTheWriteSide(typeof(ChainedWriter)));
            Assert.True(TakesTheWriteSide(typeof(UntypedResolver)));
            Assert.True(TakesTheWriteSide(typeof(CastingWriter)));
            Assert.False(TakesTheWriteSide(typeof(Reader)));
        }

        /// <summary>Every platform assembly in the suite's output except the suite itself.</summary>
        /// <returns>The assemblies.</returns>
        private static Assembly[] PlatformAssemblies()
        {
            Assembly suite = typeof(RequestContextHolderTests).Assembly;
            return [.. Directory.EnumerateFiles(AppContext.BaseDirectory, "Tellma.*.dll")
                .Select(static file => Assembly.Load(new AssemblyName(Path.GetFileNameWithoutExtension(file))))
                .Where(assembly => assembly != suite)];
        }

        /// <summary>The top-level type a type is declared in, so a closure or a state machine counts as the type that owns it.</summary>
        /// <param name="type">The type.</param>
        /// <returns>The outermost declaring type.</returns>
        private static Type TopLevel(Type type)
        {
            while (type.DeclaringType is { } declaring)
            {
                type = declaring;
            }

            return type;
        }

        /// <summary>
        ///     Whether a type is the write side, or stores it, receives it, returns it, holds it in a
        ///     local, or has a method whose body names it: a call on it, a service resolved as it, a
        ///     cast to it.
        /// </summary>
        /// <param name="type">The type.</param>
        /// <returns>True when it does.</returns>
        private static bool TakesTheWriteSide(Type type)
        {
            const BindingFlags Declared = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

            if (IsWriteSide(type) || type.GetInterfaces().Any(IsWriteSide))
            {
                return true;
            }

            if (type.GetFields(Declared).Any(static field => IsWriteSide(field.FieldType)))
            {
                return true;
            }

            IEnumerable<MethodBase> methods = [.. type.GetMethods(Declared), .. type.GetConstructors(Declared)];
            return methods.Any(static method =>
                method.GetParameters().Any(static parameter => IsWriteSide(parameter.ParameterType))
                || (method is MethodInfo info && IsWriteSide(info.ReturnType))
                || BodyNamesTheWriteSide(method));
        }

        /// <summary>Whether a method's body declares a local of the write side or names it in an instruction.</summary>
        /// <param name="method">The method.</param>
        /// <returns>True when it does.</returns>
        private static bool BodyNamesTheWriteSide(MethodBase method)
        {
            if (method.GetMethodBody() is not { } body || body.GetILAsByteArray() is not { } il)
            {
                return false;
            }

            if (body.LocalVariables.Any(static local => IsWriteSide(local.LocalType)))
            {
                return true;
            }

            // A chained call such as GetRequiredService<T>().Set(context) declares no local, so the
            // instructions are read too: every member or type one names is resolved and tested.
            Type[]? typeArguments = method.DeclaringType is { IsGenericType: true } declaring ? declaring.GetGenericArguments() : null;
            Type[]? methodArguments = method.IsGenericMethod ? method.GetGenericArguments() : null;
            return MemberTokens(il).Any(token => method.Module.ResolveMember(token, typeArguments, methodArguments) switch
            {
                Type named => IsWriteSide(named),
                MethodBase called => IsWriteSide(called.DeclaringType)
                    || (called.IsGenericMethod && called.GetGenericArguments().Any(IsWriteSide))
                    || called.GetParameters().Any(static parameter => IsWriteSide(parameter.ParameterType))
                    || (called is MethodInfo info && IsWriteSide(info.ReturnType)),
                FieldInfo field => IsWriteSide(field.FieldType) || IsWriteSide(field.DeclaringType),
                _ => false,
            });
        }

        /// <summary>The metadata token of every member or type an IL body names, in order.</summary>
        /// <param name="il">The body's instructions.</param>
        /// <returns>The tokens.</returns>
        private static IEnumerable<int> MemberTokens(byte[] il)
        {
            int position = 0;
            while (position < il.Length)
            {
                // --- The opcode: one byte, or two when the first is the 0xFE prefix ---
                int value = il[position++];
                if (value == 0xFE)
                {
                    value = 0xFE00 | il[position++];
                }

                OperandType operand = OpCodesByValue[value].OperandType;

                // --- Its operand: a token to yield, a jump table, or bytes to skip ---
                if (operand is OperandType.InlineMethod or OperandType.InlineField or OperandType.InlineType or OperandType.InlineTok)
                {
                    yield return BinaryPrimitives.ReadInt32LittleEndian(il.AsSpan(position));
                    position += sizeof(int);
                }
                else if (operand == OperandType.InlineSwitch)
                {
                    int targets = BinaryPrimitives.ReadInt32LittleEndian(il.AsSpan(position));
                    position += sizeof(int) * (1 + targets);
                }
                else
                {
                    // An operand kind this table does not know fails the search loudly, never quietly.
                    position += OperandSizes[operand];
                }
            }
        }

        /// <summary>Whether a type is the holder's interface or its implementation, or a generic type, an array or a reference over either.</summary>
        /// <param name="type">The type.</param>
        /// <returns>True when it is.</returns>
        private static bool IsWriteSide(Type? type)
        {
            return type is not null
                && (type == typeof(IRequestContextHolder)
                    || type == typeof(RequestContextHolder)
                    || (type.IsGenericType && type.GetGenericArguments().Any(IsWriteSide))
                    || (type.HasElementType && IsWriteSide(type.GetElementType())));
        }

        /// <summary>Every IL opcode by its encoded value.</summary>
        private static readonly Dictionary<int, OpCode> OpCodesByValue = typeof(OpCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(static field => field.FieldType == typeof(OpCode))
            .Select(static field => (OpCode)field.GetValue(null)!)
            .ToDictionary(static code => (int)unchecked((ushort)code.Value));

        /// <summary>The operand size in bytes of every operand kind that names no member and is no jump table.</summary>
        private static readonly Dictionary<OperandType, int> OperandSizes = new()
        {
            [OperandType.InlineNone] = 0,
            [OperandType.ShortInlineBrTarget] = 1,
            [OperandType.ShortInlineI] = 1,
            [OperandType.ShortInlineVar] = 1,
            [OperandType.InlineVar] = 2,
            [OperandType.InlineBrTarget] = 4,
            [OperandType.InlineI] = 4,
            [OperandType.InlineSig] = 4,
            [OperandType.InlineString] = 4,
            [OperandType.ShortInlineR] = 4,
            [OperandType.InlineI8] = 8,
            [OperandType.InlineR] = 8,
        };

        /// <summary>Writes through a chained call: no local, field or parameter of the write side.</summary>
        private static class ChainedWriter
        {
            /// <summary>Resolves the write side and sets it in one expression.</summary>
            /// <param name="services">The scope's services.</param>
            /// <param name="context">The context.</param>
            public static void Write(IServiceProvider services, RequestContext context)
            {
                services.GetRequiredService<IRequestContextHolder>().Set(context);
            }
        }

        /// <summary>Resolves the write side by its type object, typed as nothing but an object.</summary>
        private static class UntypedResolver
        {
            /// <summary>Resolves the write side untyped.</summary>
            /// <param name="services">The scope's services.</param>
            /// <returns>The write side, as an object.</returns>
            public static object Resolve(IServiceProvider services)
            {
                return services.GetRequiredService(typeof(IRequestContextHolder));
            }
        }

        /// <summary>Reaches the write side by casting the read side.</summary>
        private static class CastingWriter
        {
            /// <summary>Whether the read side is the write side.</summary>
            /// <param name="accessor">The read side.</param>
            /// <returns>True when it is.</returns>
            public static bool IsWriter(IRequestContextAccessor accessor)
            {
                return accessor is IRequestContextHolder;
            }
        }

        /// <summary>Reads the context, as every service does.</summary>
        private static class Reader
        {
            /// <summary>Reads the scope's context.</summary>
            /// <param name="services">The scope's services.</param>
            /// <returns>The context.</returns>
            public static RequestContext Read(IServiceProvider services)
            {
                return services.GetRequiredService<IRequestContextAccessor>().Current;
            }
        }
    }
}
