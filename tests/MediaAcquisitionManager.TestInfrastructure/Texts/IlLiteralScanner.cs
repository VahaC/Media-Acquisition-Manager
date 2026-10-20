using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Text;
using MediaAcquisitionManager.Core.Texts;
using Microsoft.Extensions.Logging;

namespace MediaAcquisitionManager.TestInfrastructure.Texts;

/// <summary>
/// MAM-47 (ADR 0042) — reads compiled IL to find where text written in code ends up: every string literal, followed
/// through the evaluation stack, locals and string building to what takes it — a log call, an exception, a field, a
/// return, an error helper. The convention tests judge the result: a sentence a person reads must come from the locale
/// files, so a literal sentence may only reach a log or an exception.
/// </summary>
/// <remarks>
/// <para>
/// Stack positions, not "the next call": in <c>Message.Of("k", ("name", value))</c> the literal <c>"name"</c> is followed by
/// a call that converts <c>value</c>, and only the stack says the literal goes into the tuple, not into that call.
/// </para>
/// <para>
/// String building is followed, not judged: <c>string.Concat</c>, <c>string.Format</c>, <c>string.Join</c>, the
/// interpolation handler (<c>$"…"</c>, whose pieces the compiler appends one by one) and the inline arrays a long
/// concatenation is built in pass their literals on, joined into one text with a <c>{}</c> per hole, so
/// <c>$"Couldn't reach {name} in time."</c> is judged as the sentence it is, not as two pieces. Branches are merged at
/// their join (a ternary or a switch expression offers each arm's text); a loop's back edge is not followed.
/// </para>
/// <para>
/// MAM-47 — it also keeps the declared type of what locals, arguments, fields and calls give, as far as it knows it, so
/// it can tell where a <see cref="MessageValue"/> is printed into a string (<c>"a" + message</c>,
/// <c>string.Format("{0}", message)</c>, <c>builder.Append(message)</c>), which compiles and writes the message's JSON;
/// and it lists every <c>const string</c>, whose text the compiler copies to each use, so a sentence kept in a constant
/// is judged in the type that declares it.
/// </para>
/// </remarks>
public static class IlLiteralScanner
{
    private const BindingFlags Declared =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    /// <summary>How many alternatives a text keeps through branches and joins before the rest are dropped.</summary>
    private const int MaximumAlternatives = 16;

    private const string Hole = "{}";

    private static readonly Dictionary<short, OpCode> OpCodesByValue = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(field => (OpCode)field.GetValue(null)!)
        .ToDictionary(opCode => opCode.Value);

    /// <summary>Every type of <paramref name="assembly"/>, nested and compiler-generated ones included.</summary>
    public static IlScan Scan(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        Type[] types;
        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            types = [.. exception.Types.OfType<Type>()];
        }

        return Scan(types);
    }

    /// <summary><paramref name="types"/> and every type nested in them.</summary>
    public static IlScan Scan(IEnumerable<Type> types)
    {
        ArgumentNullException.ThrowIfNull(types);
        var all = new HashSet<Type>();
        foreach (var type in types)
            AddWithNested(all, type);

        var uses = new List<LiteralUse>();
        var strings = new List<IlString>();
        var calls = new List<IlCall>();
        var printed = new List<IlCall>();
        var constants = new List<IlConstant>();
        var unresolved = new List<string>();
        var methodCount = 0;
        foreach (var type in all)
        {
            foreach (var field in type.GetFields(Declared))
            {
                if (field is { IsLiteral: true } && field.FieldType == typeof(string) && field.GetRawConstantValue() is string text)
                    constants.Add(new IlConstant(field, text));
            }

            foreach (var method in type.GetMethods(Declared).Cast<MethodBase>().Concat(type.GetConstructors(Declared)))
            {
                if (BodyOf(method) is not { } il)
                    continue;
                methodCount++;
                new MethodReader(method, il, uses, strings, calls, printed, unresolved).Read();
            }
        }

        return new IlScan(all, methodCount, uses, strings, calls, printed, constants, unresolved);
    }

    /// <summary>The top-level type that declares <paramref name="method"/>: a lambda's, a local function's or a state
    /// machine's outer type, which is where the source file is.</summary>
    public static Type OwnerOf(MethodBase method)
    {
        ArgumentNullException.ThrowIfNull(method);
        return OwnerOf(method.DeclaringType ?? throw new InvalidOperationException($"{method.Name} has no declaring type."));
    }

    /// <summary>The top-level type <paramref name="type"/> is, or is nested in: where the source file is.</summary>
    public static Type OwnerOf(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        while (type.DeclaringType is { } outer)
            type = outer;
        return type;
    }

    private static void AddWithNested(HashSet<Type> into, Type type)
    {
        if (!into.Add(type))
            return;
        foreach (var nested in type.GetNestedTypes(Declared))
            AddWithNested(into, nested);
    }

    private static byte[]? BodyOf(MethodBase method)
    {
        try
        {
            return method.GetMethodBody()?.GetILAsByteArray();
        }
        catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException or BadImageFormatException)
        {
            return null;
        }
    }

    /// <summary>What a stack slot, a local or an argument holds, as far as text written in code goes.</summary>
    /// <param name="Texts">The texts it may hold (one per branch it came through), holes written <c>{}</c>; empty when no
    /// literal reached it.</param>
    /// <param name="HasLiteral">Whether a literal is part of it — false for an interpolation handler before its first
    /// literal piece.</param>
    /// <param name="Elements">The items of an array or inline array being filled, in the order stored.</param>
    /// <param name="Address">The local or argument whose address this is (<c>ldloca</c>), by slot key.</param>
    /// <param name="ElementOf">The inline array (by slot key) one of whose elements this is the address of.</param>
    /// <param name="Type">The type the IL declares for it (a local's, an argument's, a field's, a call's result), when
    /// known.</param>
    private sealed record Value(
        IReadOnlyList<string> Texts,
        bool HasLiteral,
        List<Value>? Elements = null,
        int? Address = null,
        int? ElementOf = null,
        Type? Type = null)
    {
        public static readonly Value Unknown = new([], false);

        public static Value Literal(string text) => new([text], true);

        public static Value Builder() => new([""], false);

        public static Value Array() => new([], false, Elements: []);

        /// <summary>This value, known to be of <paramref name="type"/>.</summary>
        public Value As(Type? type) => type == Type ? this : this with { Type = type };

        /// <summary>Whether it is a message value, or an array holding one.</summary>
        public bool IsMessage => (Type is { } type && typeof(MessageValue).IsAssignableFrom(type))
            || (Elements is { } elements && elements.Any(element => element.IsMessage));

        public static Value Merge(Value first, Value second)
        {
            if (ReferenceEquals(first, second))
                return first;
            return new Value(
                [.. first.Texts.Concat(second.Texts).Distinct(StringComparer.Ordinal).Take(MaximumAlternatives)],
                first.HasLiteral || second.HasLiteral,
                first.Elements ?? second.Elements,
                first.Address == second.Address ? first.Address : null,
                first.ElementOf == second.ElementOf ? first.ElementOf : null,
                first.Type == second.Type ? first.Type : null);
        }

        /// <summary>The parts joined in order: every combination of their texts, a part with none written as a hole.</summary>
        public static Value Compose(IEnumerable<Value> parts, string separator = "")
        {
            IReadOnlyList<string> texts = [""];
            var hasLiteral = false;
            var first = true;
            foreach (var part in parts)
            {
                IReadOnlyList<string> options = part.Texts.Count > 0 ? part.Texts : [Hole];
                var glue = first ? "" : separator;
                texts = [.. texts.SelectMany(text => options.Select(option => text + glue + option)).Take(MaximumAlternatives)];
                hasLiteral |= part.HasLiteral;
                first = false;
            }

            return new Value(texts, hasLiteral);
        }
    }

    /// <summary>The evaluation stack and the locals and arguments at one point of a method.</summary>
    private sealed record State(List<Value> Stack, Dictionary<int, Value> Slots)
    {
        public State Copy() => new([.. Stack], new Dictionary<int, Value>(Slots));

        public static State Merge(State current, State saved)
        {
            // Verifiable IL has one stack depth at a join; if not, the branch's state wins.
            var stack = current.Stack.Count == saved.Stack.Count
                ? current.Stack.Zip(saved.Stack, Value.Merge).ToList()
                : [.. saved.Stack];
            var slots = new Dictionary<int, Value>(current.Slots);
            foreach (var (key, value) in saved.Slots)
                slots[key] = slots.TryGetValue(key, out var mine) ? Value.Merge(mine, value) : value;
            return new State(stack, slots);
        }
    }

    /// <summary>One method's IL, read once from start to end.</summary>
    private sealed class MethodReader(
        MethodBase method,
        byte[] il,
        List<LiteralUse> uses,
        List<IlString> strings,
        List<IlCall> calls,
        List<IlCall> printed,
        List<string> unresolved)
    {
        private readonly Dictionary<int, State> _saved = [];
        private readonly IList<LocalVariableInfo> _locals = method.GetMethodBody()?.LocalVariables ?? [];

        // A record's PrintMembers is the compiler's: it appends every property, a message too, for the debugger's view.
        private readonly bool _isGeneratedPrinter =
            method.Name == "PrintMembers" && method.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false);
        private readonly Type[]? _typeArguments = method.DeclaringType is { IsGenericType: true } type ? type.GetGenericArguments() : null;
        private readonly Type[]? _methodArguments = method.IsGenericMethod ? method.GetGenericArguments() : null;
        private State _state = new([], []);

        /// <summary>Where the instruction being run starts.</summary>
        private int _offset;

        private List<Value> Stack => _state.Stack;

        public void Read()
        {
            var handlers = HandlerStarts();
            var reachable = true;
            var position = 0;
            while (position < il.Length)
            {
                var offset = position;
                if (handlers.TryGetValue(offset, out var exceptionOnStack))
                {
                    _state = new State(exceptionOnStack ? [Value.Unknown] : [], new Dictionary<int, Value>(_state.Slots));
                    _saved.Remove(offset);
                }
                else if (_saved.Remove(offset, out var saved))
                {
                    _state = reachable ? State.Merge(_state, saved) : saved;
                }
                else if (!reachable)
                {
                    _state = new State([], new Dictionary<int, Value>(_state.Slots));
                }

                _offset = offset;
                var opCode = ReadOpCode(ref position);
                var operandStart = position;
                position += OperandSize(opCode, position);
                reachable = Execute(opCode, operandStart, position);
            }
        }

        private Dictionary<int, bool> HandlerStarts()
        {
            var starts = new Dictionary<int, bool>();
            foreach (var clause in method.GetMethodBody()!.ExceptionHandlingClauses)
            {
                switch (clause.Flags)
                {
                    case ExceptionHandlingClauseOptions.Clause:
                        starts[clause.HandlerOffset] = true;
                        break;
                    case ExceptionHandlingClauseOptions.Filter:
                        starts[clause.FilterOffset] = true;
                        starts[clause.HandlerOffset] = true;
                        break;
                    default:
                        starts[clause.HandlerOffset] = false;
                        break;
                }
            }

            return starts;
        }

        private OpCode ReadOpCode(ref int position)
        {
            var first = il[position++];
            return first == 0xFE
                ? OpCodesByValue[unchecked((short)(0xFE00 | il[position++]))]
                : OpCodesByValue[first];
        }

        private int OperandSize(OpCode opCode, int position) => opCode.OperandType switch
        {
            OperandType.InlineNone => 0,
            OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
            OperandType.InlineVar => 2,
            OperandType.InlineI8 or OperandType.InlineR => 8,
            OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(il, position),
            _ => 4,
        };

        /// <summary>Runs one instruction on the state; false when the next one isn't reached by falling through.</summary>
        private bool Execute(OpCode opCode, int operand, int next)
        {
            switch (opCode.Name)
            {
                case "ldstr":
                    var text = method.Module.ResolveString(Int32At(operand));
                    strings.Add(new IlString(method, text));
                    Push(Value.Literal(text));
                    return true;
                case "dup":
                    var top = Pop();
                    Push(top);
                    Push(top);
                    return true;
                case "pop":
                    Pop();
                    return true;
                case "box" or "unbox.any":
                    Push(Pop());
                    return true;
                case "castclass" or "isinst":
                    Push(Pop().As(TypeAt(operand)));
                    return true;
                case "ldfld":
                    Pop();
                    Push(Value.Unknown.As(FieldAt(operand)?.FieldType));
                    return true;
                case "ldsfld":
                    Push(Value.Unknown.As(FieldAt(operand)?.FieldType));
                    return true;
                case "newarr":
                    Pop();
                    Push(Value.Array());
                    return true;
                case "initobj":
                    if (Pop().Address is { } initialised)
                        _state.Slots[initialised] = Value.Array();
                    return true;
                case "call" or "callvirt" or "newobj":
                    Call(opCode, operand);
                    return true;
                case "calli":
                    unresolved.Add($"{OwnerOf(method).FullName}.{method.Name}: calli");
                    Stack.Clear();
                    return true;
                case "ret":
                    if (method is MethodInfo { ReturnType: var returnType } && returnType != typeof(void) && Stack.Count > 0)
                        Consume(Pop(), callee: null, "return");
                    return false;
                case "stfld":
                    var fieldValue = Pop();
                    Pop();
                    Consume(fieldValue, callee: null, FieldName(operand));
                    return true;
                case "stsfld":
                    Consume(Pop(), callee: null, FieldName(operand));
                    return true;
                case "stobj":
                    StoreIndirect();
                    return true;
                case "switch":
                    Pop();
                    var count = Int32At(operand);
                    for (var index = 0; index < count; index++)
                        Save(next + Int32At(operand + 4 + 4 * index));
                    return true;
            }

            if (opCode.Name!.StartsWith("stelem", StringComparison.Ordinal))
            {
                var element = Pop();
                Pop();
                if (Pop().Elements is { } elements)
                    elements.Add(element);
                else
                    Consume(element, callee: null, "array element");
                return true;
            }

            if (opCode.Name.StartsWith("stind", StringComparison.Ordinal))
            {
                StoreIndirect();
                return true;
            }

            if (SlotOf(opCode, operand) is { } slot)
                return Slot(opCode, slot);

            // Everything else by its declared stack behaviour: what it takes is gone, what it gives carries no literal. Not
            // judged as a use: a branch or a comparison only looks at a text (a null check of a dup'ed copy, whose other
            // copy goes on to its real consumer).
            var popped = PopCountOf(opCode);
            for (var index = 0; index < popped; index++)
                Pop();
            for (var index = 0; index < PushCountOf(opCode); index++)
                Push(Value.Unknown);

            switch (opCode.FlowControl)
            {
                case FlowControl.Branch:
                    if (opCode.Name.StartsWith("leave", StringComparison.Ordinal))
                        Stack.Clear();
                    Save(next + BranchOffset(opCode, operand));
                    return false;
                case FlowControl.Cond_Branch:
                    Save(next + BranchOffset(opCode, operand));
                    return true;
                case FlowControl.Return or FlowControl.Throw:
                    return false;
                default:
                    return true;
            }
        }

        // ── locals and arguments ────────────────────────────────────────────────────────────────────────────────────

        /// <summary>The slot key a local or argument instruction names: locals from 0, argument n as -(n + 1).</summary>
        private int? SlotOf(OpCode opCode, int operand)
        {
            var name = opCode.Name!;
            var isLocal = name.StartsWith("ldloc", StringComparison.Ordinal) || name.StartsWith("stloc", StringComparison.Ordinal);
            var isArgument = name.StartsWith("ldarg", StringComparison.Ordinal) || name.StartsWith("starg", StringComparison.Ordinal);
            if (!isLocal && !isArgument)
                return null;

            int index;
            if (name[^2] == '.' && char.IsDigit(name[^1]))
                index = name[^1] - '0';
            else if (opCode.OperandType == OperandType.ShortInlineVar)
                index = il[operand];
            else if (opCode.OperandType == OperandType.InlineVar)
                index = BitConverter.ToUInt16(il, operand);
            else
                return null;

            return isLocal ? index : -(index + 1);
        }

        private bool Slot(OpCode opCode, int slot)
        {
            var name = opCode.Name!;
            if (name.StartsWith("st", StringComparison.Ordinal))
                _state.Slots[slot] = Pop();
            else if (name is "ldloca" or "ldloca.s" or "ldarga" or "ldarga.s")
                Push(new Value([], false, Address: slot));
            else
                Push(_state.Slots.GetValueOrDefault(slot, Value.Unknown).As(TypeOfSlot(slot)));
            return true;
        }

        /// <summary>The declared type of a local (from 0) or an argument (-(n + 1)); none for a by-ref one.</summary>
        private Type? TypeOfSlot(int slot)
        {
            Type? type;
            if (slot >= 0)
            {
                type = _locals.FirstOrDefault(local => local.LocalIndex == slot)?.LocalType;
            }
            else
            {
                var index = -slot - 1;
                if (!method.IsStatic)
                    index--;
                var parameters = method.GetParameters();
                type = index < 0 ? method.DeclaringType : index < parameters.Length ? parameters[index].ParameterType : null;
            }

            return type is { IsByRef: true } ? null : type;
        }

        private void StoreIndirect()
        {
            var value = Pop();
            var address = Pop();
            if (address.ElementOf is { } inlineArray && _state.Slots.GetValueOrDefault(inlineArray)?.Elements is { } elements)
                elements.Add(value);
            else if (address.Address is { } slot)
                _state.Slots[slot] = value;
            else
                Consume(value, callee: null, "indirect store");
        }

        // ── calls ───────────────────────────────────────────────────────────────────────────────────────────────────

        private void Call(OpCode opCode, int operand)
        {
            MethodBase callee;
            try
            {
                callee = method.Module.ResolveMethod(Int32At(operand), _typeArguments, _methodArguments)!;
            }
            catch (Exception exception) when (exception is ArgumentException or BadImageFormatException or TypeLoadException or MissingMemberException)
            {
                unresolved.Add($"{OwnerOf(method).FullName}.{method.Name}: {exception.GetType().Name} {exception.Message}");
                Stack.Clear();
                return;
            }

            calls.Add(new IlCall(method, callee));
            var isNew = opCode == OpCodes.Newobj;
            var parameters = callee.GetParameters();
            var count = parameters.Length + (!isNew && !callee.IsStatic ? 1 : 0);
            var arguments = new Value[count];
            for (var index = count - 1; index >= 0; index--)
                arguments[index] = Pop();

            if (!_isGeneratedPrinter && Prints(callee, arguments))
                printed.Add(new IlCall(method, callee));

            var result = Interpret(callee, arguments, isNew);
            if (isNew)
                Push(result.As(callee.DeclaringType));
            else if (callee is MethodInfo { ReturnType: var returnType } && returnType != typeof(void))
                Push(result.As(returnType));
        }

        /// <summary>
        /// Whether the call writes a message into a string: its <c>ToString()</c> (what <c>"a" + message</c> compiles to),
        /// or a message handed to <c>string.Concat</c>, <c>Format</c> or <c>Join</c>, to a <c>StringBuilder</c>'s
        /// appending, or as an argument of a log call — each compiles, and each writes the message's JSON where a
        /// sentence was meant (MAM-47 WP12 review: a log formats its arguments with <c>ToString()</c>, so
        /// <c>logger.LogWarning("… {Reason}", reason)</c> logs JSON where <c>texts.English(reason)</c> logs English).
        /// </summary>
        private static bool Prints(MethodBase callee, Value[] arguments)
        {
            if (callee is { Name: "ToString", IsStatic: false } && callee.GetParameters().Length == 0)
                return arguments.Length > 0 && arguments[0].IsMessage;

            var builds = (callee.DeclaringType == typeof(string) && callee.Name is "Concat" or "Format" or "Join")
                || (callee.DeclaringType == typeof(StringBuilder) && callee.Name is "Append" or "AppendFormat" or "AppendJoin" or "Insert")
                || callee.DeclaringType == typeof(LoggerExtensions);
            if (!builds)
                return false;

            return arguments.Any(argument => argument.IsMessage)
                || (callee is MethodInfo { IsGenericMethod: true } generic
                    && generic.GetGenericArguments().Any(argument => typeof(MessageValue).IsAssignableFrom(argument)));
        }

        /// <summary>A call's result; a call that builds strings passes its literals on, any other takes them.</summary>
        private Value Interpret(MethodBase callee, Value[] arguments, bool isNew)
        {
            var type = callee.DeclaringType;
            var name = callee.Name;

            if (type == typeof(string))
            {
                switch (name)
                {
                    case "Concat":
                        return AsString(Value.Compose(arguments.SelectMany(Items)));
                    case "Format":
                        // The format is the sentence; a literal formatted into it is offered beside it.
                        return AsString(arguments.Aggregate(Value.Unknown, (all, argument) => argument.HasLiteral ? Value.Merge(all, argument) : all));
                    case "Join" when arguments.Length == 2:
                        return AsString(Value.Compose(Items(arguments[1]), arguments[0].Texts.FirstOrDefault() ?? Hole));
                    case "Create" when arguments.Length == 2 && arguments[1].Address is { } handler:
                        return AsString(_state.Slots.GetValueOrDefault(handler, Value.Unknown));
                    case "op_Implicit":
                        return arguments[0];
                }
            }

            if (type == typeof(MemoryExtensions) && name == "AsSpan" && arguments.Length == 1)
                return arguments[0];

            if (type == typeof(DefaultInterpolatedStringHandler))
            {
                if (isNew)
                    return Value.Builder();
                if (arguments[0].Address is { } handler)
                {
                    var built = _state.Slots.GetValueOrDefault(handler, Value.Builder());
                    switch (name)
                    {
                        case ".ctor":
                            _state.Slots[handler] = Value.Builder();
                            return Value.Unknown;
                        case "AppendLiteral" or "AppendFormatted":
                            _state.Slots[handler] = Value.Compose([built, arguments[1]]);
                            return Value.Unknown;
                        case "ToStringAndClear" or "ToString":
                            return AsString(built);
                    }
                }
            }

            // An inline array (a long concatenation's ReadOnlySpan<string>): its element addresses and its span.
            if (type is { Name: "<PrivateImplementationDetails>" } && arguments.Length > 0 && arguments[0].Address is { } inlineArray)
            {
                if (name.StartsWith("InlineArrayElementRef", StringComparison.Ordinal) || name.StartsWith("InlineArrayFirstElementRef", StringComparison.Ordinal))
                    return new Value([], false, ElementOf: inlineArray);
                if (name.StartsWith("InlineArrayAs", StringComparison.Ordinal))
                    return new Value([], false, Elements: _state.Slots.GetValueOrDefault(inlineArray)?.Elements);
            }

            var consumer = isNew ? $"new {TypeName(type)}" : $"{TypeName(type)}.{name}";
            foreach (var argument in arguments)
                Consume(argument, callee, consumer);
            return Value.Unknown;
        }

        /// <summary>An array's or a span's items, else the value itself.</summary>
        private IEnumerable<Value> Items(Value value)
        {
            if (value.Elements is { } elements)
                return elements;
            if (value.Address is { } slot && _state.Slots.GetValueOrDefault(slot)?.Elements is { } inline)
                return inline;
            return [value];
        }

        private static Value AsString(Value value) => value.HasLiteral ? new Value(value.Texts, true) : Value.Unknown;

        /// <summary>Records each text <paramref name="value"/> carries as taken by <paramref name="consumer"/>: its own, an
        /// array's items, or what a local whose address is passed holds (an interpolation handler).</summary>
        private void Consume(Value value, MethodBase? callee, string consumer)
        {
            if (value.HasLiteral)
            {
                foreach (var text in value.Texts)
                    uses.Add(new LiteralUse(method, text, callee, consumer));
            }

            if (value.Elements is { } elements)
            {
                foreach (var element in elements)
                    Consume(element, callee, consumer);
            }

            if (value.Address is { } slot && _state.Slots.GetValueOrDefault(slot) is { } held && !ReferenceEquals(held, value))
                Consume(held with { Address = null }, callee, consumer);
        }

        // ── the stack ───────────────────────────────────────────────────────────────────────────────────────────────

        private void Push(Value value) => Stack.Add(value);

        // An empty stack here means the scan lost track (a calli, an unresolved call): carry on with nothing known.
        private Value Pop()
        {
            if (Stack.Count == 0)
                return Value.Unknown;
            var value = Stack[^1];
            Stack.RemoveAt(Stack.Count - 1);
            return value;
        }

        private void Save(int target)
        {
            // A back edge (a loop) reaches code already read: not followed.
            if (target <= _offset || target >= il.Length)
                return;
            _saved[target] = _saved.TryGetValue(target, out var saved) ? State.Merge(saved, _state.Copy()) : _state.Copy();
        }

        private int Int32At(int position) => BitConverter.ToInt32(il, position);

        private int BranchOffset(OpCode opCode, int operand) =>
            opCode.OperandType == OperandType.ShortInlineBrTarget ? (sbyte)il[operand] : Int32At(operand);

        private FieldInfo? FieldAt(int operand)
        {
            try
            {
                return method.Module.ResolveField(Int32At(operand), _typeArguments, _methodArguments);
            }
            catch (Exception exception) when (exception is ArgumentException or BadImageFormatException or TypeLoadException or MissingMemberException)
            {
                return null;
            }
        }

        private Type? TypeAt(int operand)
        {
            try
            {
                return method.Module.ResolveType(Int32At(operand), _typeArguments, _methodArguments);
            }
            catch (Exception exception) when (exception is ArgumentException or BadImageFormatException or TypeLoadException)
            {
                return null;
            }
        }

        private string FieldName(int operand)
        {
            try
            {
                var field = method.Module.ResolveField(Int32At(operand), _typeArguments, _methodArguments);
                return field is null ? "field" : $"field {TypeName(field.DeclaringType)}.{field.Name}";
            }
            catch (Exception exception) when (exception is ArgumentException or BadImageFormatException or TypeLoadException or MissingMemberException)
            {
                return "field";
            }
        }

        // A generic type's name without its arity (List`1 → List); a nested type of a generic one has none.
        private static string TypeName(Type? type) =>
            type is null ? "?" : type.Name.IndexOf('`') is var tick and >= 0 ? type.Name[..tick] : type.Name;

        private static int PopCountOf(OpCode opCode) => opCode.StackBehaviourPop switch
        {
            StackBehaviour.Pop0 or StackBehaviour.Varpop => 0,
            StackBehaviour.Pop1 or StackBehaviour.Popi or StackBehaviour.Popref => 1,
            StackBehaviour.Pop1_pop1 or StackBehaviour.Popi_pop1 or StackBehaviour.Popi_popi or StackBehaviour.Popi_popi8
                or StackBehaviour.Popi_popr4 or StackBehaviour.Popi_popr8 or StackBehaviour.Popref_pop1 or StackBehaviour.Popref_popi => 2,
            _ => 3,
        };

        private static int PushCountOf(OpCode opCode) => opCode.StackBehaviourPush switch
        {
            StackBehaviour.Push0 or StackBehaviour.Varpush => 0,
            StackBehaviour.Push1_push1 => 2,
            _ => 1,
        };
    }
}
