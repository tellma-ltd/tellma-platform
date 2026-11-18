// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using System.Numerics;
using System.Reflection;
using System.Text.Json;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Assignments;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Entities;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Notifications;
using Tellma.Core.Abstractions.Settings;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.Crud.Details;
using Tellma.Core.Crud.Save;
using Tellma.Core.Data.Queries;

namespace Tellma.Core.Crud.Actions
{
    // An action is spec 0015 §11. Before any round trip: MaxIds, the type-level check of the action's
    // securable (§9.1), and the arguments, deserialised from JSON when they arrive as JSON and walked
    // for their attributes at Arguments.<Property> (§11.2 step 1). The built-in activate and
    // deactivate, when nothing validates actions, persist at once in one round trip (§11.1). Every
    // other action runs the general path: round trip 1 loads the targets and counts them under the
    // grant (§9.2) beside the validators' first loads; the validation hook, the components and the
    // method run over one ActionContext with their rounds; then the persist (§11.2 steps 2 to 5).
    /// <summary>One action of one stack, from its arguments to its answer.</summary>
    /// <typeparam name="TEntity">The stack's entity type.</typeparam>
    /// <typeparam name="TKey">The stack's key type.</typeparam>
    /// <param name="operation">The scope's write services.</param>
    /// <param name="stack">The stack.</param>
    /// <param name="behavior">The service's hooks, and the instance a declared action's method runs on.</param>
    /// <param name="descriptor">The action.</param>
    /// <param name="method">The action's method; null for a built-in action.</param>
    /// <param name="ids">The target ids, as sent.</param>
    /// <param name="arguments">The arguments: the action's arguments record, or its JSON; null for none.</param>
    /// <param name="options">What the action answers.</param>
    /// <param name="cancellationToken">Cancels the validation rounds; a persist already sent completes regardless.</param>
    internal sealed partial class ActionRun<TEntity, TKey>(
        SaveOperation operation,
        StackDescriptor stack,
        IEntityBehavior<TEntity, TKey> behavior,
        EntityActionDescriptor descriptor,
        MethodInfo? method,
        IReadOnlyList<TKey> ids,
        object? arguments,
        ActionOptions options,
        CancellationToken cancellationToken)
        : WriteRun(operation, stack.Resource, CrudOperationNames.Action)
        where TEntity : Entity<TKey>
        where TKey : struct, IBinaryInteger<TKey>
    {
        /// <summary>The path the arguments' errors are reported under.</summary>
        private const string ArgumentsProperty = "Arguments";

        /// <summary>The built-in action that deactivates rows; the other built-in activates them.</summary>
        private const string Deactivate = "deactivate";

        /// <summary>The stack.</summary>
        private readonly StackDescriptor _stack = stack;

        /// <summary>The service's hooks.</summary>
        private readonly IEntityBehavior<TEntity, TKey> _behavior = behavior;

        /// <summary>The action.</summary>
        private readonly EntityActionDescriptor _descriptor = descriptor;

        /// <summary>The action's method; null for a built-in action.</summary>
        private readonly MethodInfo? _method = method;

        /// <summary>What the action answers.</summary>
        private readonly ActionOptions _options = options;

        /// <summary>Cancels the validation rounds.</summary>
        private readonly CancellationToken _cancellationToken = cancellationToken;

        /// <summary>The stack's root entity.</summary>
        private readonly EntityMetadata _root = operation.Metadata.Get(typeof(TEntity));

        /// <summary>The action's errors: the arguments' and the validators'.</summary>
        private readonly ValidationErrors<TEntity> _errors = new();

        /// <summary>The target ids, each once, in request order.</summary>
        private readonly TKey[] _ids = DistinctIds(ids);

        /// <summary>The target ids as sent.</summary>
        private readonly IReadOnlyList<TKey> _sent = ids;

        /// <summary>The arguments as sent.</summary>
        private readonly object? _sentArguments = arguments;

        /// <summary>The arguments record; null for an action that takes none.</summary>
        private object? _arguments;

        /// <summary>The tenant's schema map.</summary>
        private QueryexSchemaMap _map = null!;

        /// <summary>The tables of the stack: the root's and every child collection's, at every depth.</summary>
        private IReadOnlySet<TableName> StackTables => field ??= StackTableSet.Of(_root);

        /// <summary>Whether the action answers a value of its own rather than the target ids.</summary>
        private bool OwnResult => _method is not null && ActionMethods.TryResult(_method.ReturnType, out Type? result) && result is not null;

        /// <summary>Runs the action.</summary>
        /// <returns>The method's own result, or the target ids and, when asked, the rows read back.</returns>
        public async Task<object?> RunAsync()
        {
            // --- Before any round trip: the ceiling, the type-level grant, the arguments ---
            if (_sent.Count > _stack.Limits.MaxIds)
            {
                throw new LimitExceededException(LimitNames.MaxIds, _sent.Count, _stack.Limits.MaxIds);
            }

            _ = await Services.Evaluator.RequireAsync(Resource, _descriptor.Action, _cancellationToken).ConfigureAwait(false);
            _arguments = Arguments();
            if (_options.ReturnEntities && !OwnResult)
            {
                DetailsPlanner.Admit(_stack, _options.Details ?? DetailsRequest.None);
            }

            MultilingualShape shape = Services.Accessor.Current.TenantSettings?.Shape
                ?? throw new InvalidOperationException("The tenant's settings are not loaded in this scope, so the action has no schema to compose under.");
            _map = Services.Schemas.Map(shape);

            // --- The built-in action that nothing validates: one round trip, its update alone ---
            if (_descriptor.IsBuiltIn && !HookOverrides.ValidatesActions(_behavior, Services.Services.GetServices<IEntityValidator<TEntity>>()))
            {
                return _ids.Length == 0 ? IdsOnly() : await PersistAsync().ConfigureAwait(false);
            }

            // --- The general path: round trip 1, the validators and the method, then the persist ---
            return await RunGeneralAsync().ConfigureAwait(false);
        }

        // Spec 0015 §11.2 step 1: arguments arrive as the arguments record (the web layer) or as JSON
        // (MCP, jobs), deserialised under the platform's JSON options; a shape failure is the caller's
        // 400. Either way the record's validation attributes are walked, each failure at
        // Arguments.<Property>.
        /// <summary>The arguments record, read from JSON when it arrives as JSON, and its attributes checked.</summary>
        /// <returns>The record; null for an action that takes none.</returns>
        /// <exception cref="BadRequestException">The JSON does not read as the action's arguments, or an action without arguments received some.</exception>
        /// <exception cref="ValidationException">A validation attribute of the arguments failed.</exception>
        /// <exception cref="InvalidOperationException">A service-level caller passed an object of another type.</exception>
        private object? Arguments()
        {
            // --- None sent: JSON null counts as none ---
            object? sent = _sentArguments is JsonElement { ValueKind: JsonValueKind.Null or JsonValueKind.Undefined } ? null : _sentArguments;
            Type? type = _descriptor.ArgumentsType;
            if (type is null)
            {
                return sent switch
                {
                    null => null,
                    JsonElement => throw new BadRequestException($"The action '{_descriptor.Name}' takes no arguments."),
                    _ => throw new InvalidOperationException($"The action '{_descriptor.Name}' of {Resource} takes no arguments, yet its caller passed a {sent.GetType().Name}."),
                };
            }

            // --- The record, as passed or read from JSON ---
            object record = sent switch
            {
                null => throw new BadRequestException($"The action '{_descriptor.Name}' takes arguments, and none were sent."),
                JsonElement json => Deserialize(json, type),
                _ when type.IsInstanceOfType(sent) => sent,
                _ => throw new InvalidOperationException($"The action '{_descriptor.Name}' of {Resource} takes a {type.Name}, yet its caller passed a {sent.GetType().Name}."),
            };

            // --- Its validation attributes, each failure under Arguments ---
            Services.Walker.CheckBody(record, ValidationPath.Root.Property(ArgumentsProperty), _errors);
            return _errors.HasErrors ? throw new ValidationException(_errors.Items) : record;
        }

        /// <summary>Reads the action's arguments record from JSON.</summary>
        /// <param name="json">The JSON.</param>
        /// <param name="type">The arguments record's type.</param>
        /// <returns>The record.</returns>
        /// <exception cref="BadRequestException">The JSON does not read as the record.</exception>
        private object Deserialize(JsonElement json, Type type)
        {
            try
            {
                return json.Deserialize(type, TellmaJson.Options)
                    ?? throw new BadRequestException($"The arguments of the action '{_descriptor.Name}' are null.");
            }
            catch (Exception failure) when (failure is JsonException or NotSupportedException or ArgumentException)
            {
                throw new BadRequestException($"The arguments of the action '{_descriptor.Name}' do not read as {type.Name}: {failure.Message}");
            }
        }

        /// <summary>The answer of an action without a result of its own and without a read-back: the target ids in request order.</summary>
        /// <returns>The answer.</returns>
        private EntitiesResult<TEntity> IdsOnly()
        {
            return new EntitiesResult<TEntity> { Ids = [.. _ids.Select(static id => long.CreateChecked(id))], Entities = [] };
        }

        /// <summary>The value a built-in action assigns to the rows' active flag.</summary>
        /// <returns>The assignment.</returns>
        private Dictionary<string, object?> BuiltInAssignment()
        {
            return new Dictionary<string, object?>(StringComparer.Ordinal) { [nameof(IActivatable.IsActive)] = !string.Equals(_descriptor.Name, Deactivate, StringComparison.Ordinal) };
        }

        /// <summary>Every child collection of an entity, at every depth, dotted, parents first.</summary>
        /// <param name="entity">The entity.</param>
        /// <param name="prefix">The dotted path to the entity's collections.</param>
        /// <returns>The paths.</returns>
        private static List<string> Collections(EntityMetadata entity, string prefix = "")
        {
            List<string> paths = [];
            foreach (ChildCollectionMetadata collection in entity.Children)
            {
                string path = prefix + collection.Property;
                paths.Add(path);
                paths.AddRange(Collections(collection.Child, path + "."));
            }

            return paths;
        }

        /// <summary>The context the action's persist participants see.</summary>
        /// <param name="batch">The participants' recording view.</param>
        /// <param name="affected">The batch-local table of the target ids.</param>
        /// <param name="targets">The target rows; empty on the one-round-trip path.</param>
        /// <returns>The context.</returns>
        private ActionPersistContext<TEntity> PersistContext(IDataBatch batch, SqlIdentifier affected, IReadOnlyList<TEntity> targets)
        {
            return new ActionPersistContext<TEntity>
            {
                Entities = targets,
                Context = Services.Accessor.Current,
                Batch = batch,
                AffectedIds = affected,
                Captures = [],
                Action = _descriptor.Name,
                Arguments = _arguments,
                Resource = Resource,
                Notifier = Services.Services.GetService<INotifier>(),
                AssignmentsService = Services.Services.GetService<IAssignments>(),
            };
        }
    }
}
