/*
using Iced.Intel;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

public class InstructionsAnalyzer
{
	// Represents the known stack frame layout for this specific function.
	// It maps the memory offset from RSP to the logical argument index (`aN`).
	private static readonly Dictionary<ulong, int> StackOffsetToArgIndexMap = new()
	{
		{ 0x34, 13 }, { 0x38, 12 }, { 0x3C, 11 }, { 0x40, 7 },
		{ 0x44, 6 },  { 0xB0, 5 },  { 0xC8, 8 },  { 0xD0, 9 },
		{ 0xD8, 10 }
	};

	public class ArmCallInfo
	{
		public ulong Address;
		public string Target;
		public Dictionary<string, string> Args = new();
		public string? ArgSource;
		public int? ArgIndex;
	}

	private class RegValue
	{
		public string Value;
		public int Bits;
		public ulong Tick;
		public RegValue(string value, int bits, ulong tick)
		{
			Value = value;
			Bits = bits;
			Tick = tick;
		}
	}

	public List<ArmCallInfo> AnalyzeCalls(List<Instruction> instructions)
	{
		var result = new List<ArmCallInfo>();
		var regState = new Dictionary<Register, RegValue>();
		var regToParamIndex = new Dictionary<Register, int>();
		ulong tick = 0;

		foreach (var instr in instructions)
		{
			tick++;

			if (instr.Mnemonic == Mnemonic.Mov)
			{
				if (instr.Op0Kind == OpKind.Register)
				{
					var dest = instr.Op0Register;
					var bits = instr.Op0Register.IsGPR64() ? 64 : 32;

					if (instr.Op1Kind == OpKind.Immediate32 || instr.Op1Kind == OpKind.Immediate64)
					{
						regState[dest] = new RegValue($"0x{instr.Immediate32:X}", bits, tick);
					}
					else if (instr.Op1Kind == OpKind.Register)
					{
						var src = instr.Op1Register;
						regState[dest] = new RegValue(src.ToString(), bits, tick);

						if (src == Register.RCX) regToParamIndex[dest] = 1;
						else if (src == Register.RDX) regToParamIndex[dest] = 2;
						else if (src == Register.R8) regToParamIndex[dest] = 3;
						else if (src == Register.R9) regToParamIndex[dest] = 4;
						else if (regToParamIndex.TryGetValue(src, out var index))
							regToParamIndex[dest] = index;
					}
					else if (instr.Op1Kind == OpKind.Memory && instr.MemoryBase == Register.RSP)
					{
						regState[dest] = new RegValue($"[rsp+0x{instr.MemoryDisplacement64:X}]", bits, tick);
					}
				}
			}
			else if (instr.Mnemonic == Mnemonic.Xor && instr.Op0Kind == OpKind.Register && instr.Op0Register == instr.Op1Register)
			{
				regState[instr.Op0Register] = new RegValue("0x0", 32, tick);
			}
			else if (instr.Mnemonic == Mnemonic.Call)
			{
				var call = new ArmCallInfo
				{
					Address = instr.IP,
					Target = instr.NearBranchTarget != 0 ? $"0x{instr.NearBranchTarget:X}" : "<dynamic>"
				};

				if (TryGetLatest(regState, new[] { Register.EDX, Register.RDX }, out var edx))
					call.Args["EDX"] = edx.Value;

				if (TryGetLatest(regState, new[] { Register.R8, Register.R8D }, out var r8))
				{
					string r8Value = r8.Value;
					if (Enum.TryParse<Register>(r8Value, true, out var srcReg))
					{
						if (regToParamIndex.TryGetValue(srcReg, out var paramIdx))
							call.ArgSource = $"{srcReg} = param{paramIdx}";
						else if (regState.TryGetValue(srcReg, out var srcRegValue))
							call.ArgSource = $"{srcReg} = {srcRegValue.Value}";
						else call.ArgSource = srcReg.ToString();
					}
					else call.ArgSource = r8Value;

					if (call.ArgSource != null)
					{
						var memMatch = Regex.Match(call.ArgSource, @"\[rsp\+0x([a-fA-F0-9]+)\]");
						if (memMatch.Success)
						{
							var hexOffset = memMatch.Groups[1].Value;
							if (ulong.TryParse(hexOffset, NumberStyles.HexNumber, null, out var offset) &&
								StackOffsetToArgIndexMap.TryGetValue(offset, out var argIdx))
							{
								call.ArgIndex = argIdx;
							}
						}
						else
						{
							var paramMatch = Regex.Match(call.ArgSource, @"param(\d+)");
							if (paramMatch.Success)
							{
								call.ArgIndex = int.Parse(paramMatch.Groups[1].Value);
							}
							else if (call.ArgSource.StartsWith("0x") && int.TryParse(call.ArgSource.AsSpan(2), NumberStyles.HexNumber, null, out var immediateIndex))
							{
								call.ArgIndex = immediateIndex;
							}
							else if (Enum.TryParse<Register>(r8.Value, true, out var sourceRegister) &&
									 regToParamIndex.TryGetValue(sourceRegister, out var pIdx))
							{
								call.ArgIndex = pIdx;
							}
						}
					}
				}
				result.Add(call);
			}
		}
		return result;
	}

	private static bool TryGetLatest(Dictionary<Register, RegValue> state, Register[] regs, out RegValue latest)
	{
		latest = null!;
		ulong maxTick = 0;
		foreach (var reg in regs)
		{
			if (state.TryGetValue(reg, out var val) && val.Tick > maxTick)
			{
				latest = val;
				maxTick = val.Tick;
			}
		}
		return maxTick != 0;
	}
}
*/