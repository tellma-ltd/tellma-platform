// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using System.Reflection;
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
        public async Task The_holder_refuses_no_context()
        {
            await using TenantContextHarness harness = await TenantContextHarness.CreateAsync();
            await using AsyncServiceScope scope = harness.Services.CreateAsyncScope();

            _ = Assert.Throws<ArgumentNullException>(() => scope.ServiceProvider.GetRequiredService<IRequestContextHolder>().Set(null!));
        }

        [Fact]
        public void Only_the_two_writers_take_the_write_side_of_the_context()
        {
            // The context has exactly two writers: the access guard (with the tenant middleware in
            // front of it) and the tenant scope factory. Any other runtime type that stores, receives
            // or returns the write side is a third writer in the making, so each one found here has to
            // be one of those two, the holder itself, or the code that registers and shares them.
            Type[] allowed =
            [
                typeof(IRequestContextHolder),
                typeof(RequestContextHolder),
                typeof(TenantAccessGuard),
                typeof(TenantScopeFactory),
                typeof(RequestContextInitialization),
                typeof(TenancyComposition),
            ];

            string[] holders = [.. typeof(TellmaComposition).Assembly.GetTypes()
                .Where(TakesTheWriteSide)
                .Select(TopLevel)
                .Distinct()
                .Select(static type => type.Name)
                .Order(StringComparer.Ordinal)];

            Assert.Equal([.. allowed.Select(static type => type.Name).Order(StringComparer.Ordinal)], holders);
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

        /// <summary>Whether a type is the write side, or stores, receives, returns or holds it in a local.</summary>
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
                || (method.GetMethodBody()?.LocalVariables.Any(static local => IsWriteSide(local.LocalType)) ?? false));
        }

        /// <summary>Whether a type is the holder's interface or its implementation, or a generic type over either.</summary>
        /// <param name="type">The type.</param>
        /// <returns>True when it is.</returns>
        private static bool IsWriteSide(Type type)
        {
            return type == typeof(IRequestContextHolder)
                || type == typeof(RequestContextHolder)
                || (type.IsGenericType && type.GetGenericArguments().Any(IsWriteSide))
                || (type.HasElementType && IsWriteSide(type.GetElementType()!));
        }
    }
}
