using Iced.Intel;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

public class InstructionsAnalyzer
{
	public class ArmCallInfo
	{
		public ulong Address;
		public string Target;
		public string? ArgSource;
		public int? ArgIndex;
		public string? EdxValue; // New property to store the value of EDX
	}

	private class ValueSource
	{
		public string ID { get; }
		public ulong Tick { get; }
		public ValueSource(string id, ulong tick) { ID = id; Tick = tick; }
		public override string ToString() => ID;
	}

	private Register GetCanonicalRegister(Register reg)
	{
		return reg.GetFullRegister().GetFullRegister();
	}

	private ulong AnalyzePrologue(List<Instruction> instructions, out int prologueEndIndex)
	{
		ulong allocation = 0;
		prologueEndIndex = 0;
		foreach (var instr in instructions)
		{
			if (instr.Mnemonic == Mnemonic.Push)
			{
				allocation += 8;
			}
			else if (instr.Mnemonic == Mnemonic.Sub && instr.Op0Register == Register.RSP && (instr.Op1Kind == OpKind.Immediate32 || instr.Op1Kind == OpKind.Immediate64))
			{
				allocation += instr.Immediate64;
			}
			else if (instr.Mnemonic == Mnemonic.Mov && instr.Op0Kind == OpKind.Register && instr.Op1Kind == OpKind.Register)
			{
				// Continue
			}
			else
			{
				return allocation;
			}
			prologueEndIndex++;
		}
		return allocation;
	}

	public List<ArmCallInfo> AnalyzeCalls(List<Instruction> instructions)
	{
		var result = new List<ArmCallInfo>();
		if (instructions == null || instructions.Count == 0) return result;

		var totalStackAllocation = AnalyzePrologue(instructions, out int prologueEndIndex);

		var regState = new Dictionary<Register, ValueSource>();
		var stackState = new Dictionary<ulong, ValueSource>();
		ulong tick = 0;

		regState[Register.RCX] = new ValueSource("param1", tick);
		regState[Register.RDX] = new ValueSource("param2", tick);
		regState[Register.R8] = new ValueSource("param3", tick);
		regState[Register.R9] = new ValueSource("param4", tick);

		for (int i = 0; i < instructions.Count; i++)
		{
			var instr = instructions[i];
			tick++;

			ValueSource? source = null;
			if (instr.Mnemonic == Mnemonic.Mov || instr.Mnemonic == Mnemonic.Lea)
			{
				if (instr.Op1Kind == OpKind.Register)
				{
					regState.TryGetValue(GetCanonicalRegister(instr.Op1Register), out source);
				}
				else if (instr.Op1Kind == OpKind.Memory && instr.MemoryBase == Register.RSP)
				{
					var offset = instr.MemoryDisplacement64;
					if (stackState.TryGetValue(offset, out var knownSource))
					{
						source = knownSource;
					}
					else
					{
						const ulong callerSideOffset = 0x28;
						var paramBaseOffset = totalStackAllocation + callerSideOffset;
						if (offset >= paramBaseOffset)
						{
							var paramNum = ((offset - paramBaseOffset) / 8) - 4;
							source = new ValueSource($"param{paramNum}", tick);
						}
					}
				}
				// Handle immediate values, e.g., `mov edx, 8`
				else if (instr.Op1Kind == OpKind.Immediate32 || instr.Op1Kind == OpKind.Immediate64)
				{
					source = new ValueSource($"immediate:0x{instr.Immediate32:X}", tick);
				}

				if (source != null)
				{
					if (instr.Op0Kind == OpKind.Register)
					{
						regState[GetCanonicalRegister(instr.Op0Register)] = new ValueSource(source.ID, tick);
					}
					else if (instr.Op0Kind == OpKind.Memory && instr.MemoryBase == Register.RSP)
					{
						stackState[instr.MemoryDisplacement64] = new ValueSource(source.ID, tick);
					}
				}
			}
			else if (instr.Mnemonic == Mnemonic.Xor && instr.Op0Kind == OpKind.Register && instr.Op0Register == instr.Op1Register)
			{
				regState[GetCanonicalRegister(instr.Op0Register)] = new ValueSource("immediate:0", tick);
			}
			else if (instr.Mnemonic == Mnemonic.Call)
			{
				var call = new ArmCallInfo
				{
					Address = instr.IP,
					Target = instr.NearBranchTarget != 0 ? $"0x{instr.NearBranchTarget:X}" : "<dynamic>"
				};

				// Capture R8 for ArgIndex
				if (regState.TryGetValue(Register.R8, out var r8Source))
				{
					call.ArgSource = r8Source.ID;
					var match = Regex.Match(r8Source.ID, @"param(\d+)");
					if (match.Success)
					{
						call.ArgIndex = int.Parse(match.Groups[1].Value);
					}
					else if (r8Source.ID == "immediate:0")
					{
						call.ArgIndex = 0;
					}
				}

				// --- NEW: Capture EDX value ---
				if (regState.TryGetValue(Register.RDX, out var edxSource))
				{
					call.EdxValue = edxSource.ID.Replace("immediate:", "");
				}

				result.Add(call);
			}
		}
		return result;
	}
}