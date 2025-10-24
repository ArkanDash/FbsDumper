using System.Text.RegularExpressions;
using Iced.Intel;
using ZLinq;

namespace FbsDumper.Instructions.Analyzers;

internal partial class X86Analyzer : IInstructionAnalyzer
{
    private const ulong CallerSideOffset = 0x28;
    private const ulong StackSlotSize = 8;
    private const int RegisterParamCount = 4;

    public List<InstructionsAnalyzer.CallInfo> AnalyzeCalls(List<InstructionWithAddress>? instructions)
    {
        var result = new List<InstructionsAnalyzer.CallInfo>();
        if (instructions == null || instructions.Count == 0) return result;

        var totalStackAllocation = AnalyzePrologue(instructions);
        var regState = new Dictionary<Register, ValueSource>();
        var stackState = new Dictionary<ulong, ValueSource>();
        ulong tick = 0;

        InitializeParameterRegisters(regState);

        foreach (var instr in instructions.AsValueEnumerable().Select(instrWithAddr => instrWithAddr.X86Instruction))
        {
            tick++;

            switch (instr.Mnemonic)
            {
                case Mnemonic.Mov or Mnemonic.Lea:
                    ProcessMoveOrLeaInstruction(instr, regState, stackState, totalStackAllocation, tick);
                    break;

                case Mnemonic.Xor:
                    ProcessXorInstruction(instr, regState, tick);
                    break;

                case Mnemonic.Call:
                    var call = ProcessCallInstruction(instr, regState);
                    result.Add(call);
                    break;
            }
        }

        return result;
    }

    [GeneratedRegex(@"param(\d+)")]
    private static partial Regex ParamNumberRegex();

    private static Register GetCanonicalRegister(Register reg)
    {
        return reg.GetFullRegister().GetFullRegister();
    }

    private static bool IsImmediateOperand(OpKind opKind)
    {
        return opKind is OpKind.Immediate32 or OpKind.Immediate64;
    }

    private static bool IsStackPointerSubtraction(Instruction instr)
    {
        return instr is { Mnemonic: Mnemonic.Sub, Op0Register: Register.RSP } &&
               IsImmediateOperand(instr.Op1Kind);
    }

    private static bool IsRegisterToRegisterMove(Instruction instr)
    {
        return instr.Mnemonic == Mnemonic.Mov &&
               instr is { Op0Kind: OpKind.Register, Op1Kind: OpKind.Register };
    }

    private static bool IsSelfXor(Instruction instr)
    {
        return instr is { Mnemonic: Mnemonic.Xor, Op0Kind: OpKind.Register } &&
               instr.Op0Register == instr.Op1Register;
    }

    private static bool IsStackMemoryAccess(Instruction instr)
    {
        return instr.MemoryBase == Register.RSP;
    }

    private static ulong AnalyzePrologue(List<InstructionWithAddress> instructions)
    {
        ulong allocation = 0;

        foreach (var instr in instructions.AsValueEnumerable().Select(instrWithAddr => instrWithAddr.X86Instruction))
            if (instr.Mnemonic == Mnemonic.Push)
                allocation += StackSlotSize;
            else if (IsStackPointerSubtraction(instr))
                allocation += instr.Immediate64;
            else if (!IsRegisterToRegisterMove(instr)) return allocation;

        return allocation;
    }

    private static void InitializeParameterRegisters(Dictionary<Register, ValueSource> regState)
    {
        Register[] paramRegisters = [Register.RCX, Register.RDX, Register.R8, Register.R9];

        for (var i = 0; i < paramRegisters.Length; i++)
            regState[paramRegisters[i]] = new ValueSource($"param{i + 1}", 0);
    }

    private static ValueSource? GetSourceFromRegister(Dictionary<Register, ValueSource> regState, Register register)
    {
        regState.TryGetValue(GetCanonicalRegister(register), out var source);
        return source;
    }

    private static ValueSource? GetSourceFromStackMemory(Dictionary<ulong, ValueSource> stackState, Instruction instr,
        ulong totalStackAllocation, ulong tick)
    {
        var offset = instr.MemoryDisplacement64;

        if (stackState.TryGetValue(offset, out var knownSource))
            return knownSource;

        var paramBaseOffset = totalStackAllocation + CallerSideOffset;
        if (offset < paramBaseOffset)
            return null;

        var paramNum = (offset - paramBaseOffset) / StackSlotSize - RegisterParamCount;
        return new ValueSource($"param{paramNum}", tick);
    }

    private static ValueSource GetSourceFromImmediate(Instruction instr, ulong tick)
    {
        return new ValueSource($"immediate:0x{instr.Immediate32:X}", tick);
    }

    private static ValueSource? DetermineValueSource(Instruction instr, Dictionary<Register, ValueSource> regState,
        Dictionary<ulong, ValueSource> stackState, ulong totalStackAllocation, ulong tick)
    {
        return instr.Op1Kind switch
        {
            OpKind.Register => GetSourceFromRegister(regState, instr.Op1Register),
            OpKind.Memory when IsStackMemoryAccess(instr) => GetSourceFromStackMemory(stackState, instr,
                totalStackAllocation, tick),
            var kind when IsImmediateOperand(kind) => GetSourceFromImmediate(instr, tick),
            _ => null
        };
    }

    private static void UpdateDestination(Instruction instr, ValueSource source,
        Dictionary<Register, ValueSource> regState,
        Dictionary<ulong, ValueSource> stackState, ulong tick)
    {
        var newSource = new ValueSource(source.Id, tick);

        switch (instr.Op0Kind)
        {
            case OpKind.Register:
                regState[GetCanonicalRegister(instr.Op0Register)] = newSource;
                break;
            case OpKind.Memory when IsStackMemoryAccess(instr):
                stackState[instr.MemoryDisplacement64] = newSource;
                break;
        }
    }

    private static void ProcessMoveOrLeaInstruction(Instruction instr, Dictionary<Register, ValueSource> regState,
        Dictionary<ulong, ValueSource> stackState, ulong totalStackAllocation, ulong tick)
    {
        var source = DetermineValueSource(instr, regState, stackState, totalStackAllocation, tick);
        if (source != null) UpdateDestination(instr, source, regState, stackState, tick);
    }

    private static void ProcessXorInstruction(Instruction instr, Dictionary<Register, ValueSource> regState, ulong tick)
    {
        if (IsSelfXor(instr)) regState[GetCanonicalRegister(instr.Op0Register)] = new ValueSource("immediate:0", tick);
    }

    private static void SetCallArgIndex(InstructionsAnalyzer.CallInfo call, string sourceId)
    {
        if (sourceId == "immediate:0")
        {
            call.ArgIndex = 0;
            return;
        }

        var match = ParamNumberRegex().Match(sourceId);
        if (match.Success) call.ArgIndex = int.Parse(match.Groups[1].Value);
    }

    private static InstructionsAnalyzer.CallInfo ProcessCallInstruction(Instruction instr,
        Dictionary<Register, ValueSource> regState)
    {
        var call = new InstructionsAnalyzer.CallInfo
        {
            Address = instr.IP,
            Target = instr.NearBranchTarget != 0 ? $"0x{instr.NearBranchTarget:X}" : "<dynamic>"
        };

        // Capture R8 for ArgIndex
        if (regState.TryGetValue(Register.R8, out var r8Source))
        {
            call.ArgSource = r8Source.Id;
            SetCallArgIndex(call, r8Source.Id);
        }

        // Capture EDX value
        if (regState.TryGetValue(Register.RDX, out var edxSource))
            call.EdxValue = edxSource.Id.Replace("immediate:", "");

        return call;
    }

    private class ValueSource(string id, ulong tick)
    {
        public string Id { get; } = id;
        public ulong Tick { get; } = tick;

        public override string ToString()
        {
            return Id;
        }
    }
}