using Mono.Cecil;
using Iced.Intel;
using System.Diagnostics;
using System.Net;
using System.Globalization;
using System.Text.RegularExpressions;

namespace FbsDumper;

internal class InstructionsParser
{
	string gameAssemblyPath;
	byte[] fileBytes;
	ByteArrayCodeReader codeReader;

	public InstructionsParser(string _gameAssemblyPath)
	{
		gameAssemblyPath = _gameAssemblyPath;
		fileBytes = File.ReadAllBytes(gameAssemblyPath);
		codeReader = new ByteArrayCodeReader(fileBytes);
	}

	public List<Instruction> GetInstructions(MethodDefinition targetMethod, bool debug = false)
	{
		long rva = GetMethodRVA(targetMethod);
		long offset = GetMethodOffset(targetMethod);
		if (rva == 0)
		{
			Console.WriteLine($"[!] Invalid RVA or offset for method: {targetMethod.FullName}");
			return new List<Instruction>();
		}

		return GetInstructions(rva, offset, debug);
	}

	public List<Instruction> GetInstructions(long RVA, long Offset, bool debug = false)
	{
		codeReader.Position = (int)Offset;
		var decoder = Iced.Intel.Decoder.Create(IntPtr.Size * 8, codeReader);
		decoder.IP = (ulong)RVA;
		var instructions = new List<Instruction>();

		//Console.WriteLine($"{codeReader.Position:X} {decoder.IP:X}");

		while (true)
		{
			var instruction = decoder.Decode();
			if (debug)
			{
				string instructionStr = instruction.ToString();

				Console.WriteLine($"{instruction.IP} | {instructionStr}");
			}

			instructions.Add(instruction);

			if (instruction.Mnemonic == Mnemonic.Ret)
			{
				break;
			}
		}
			
		return instructions;
	}


	public static long GetMethodRVA(MethodDefinition method)
	{
		if (!method.HasCustomAttributes)
			return 0;

		var customAttr = method.CustomAttributes.FirstOrDefault(a => a.AttributeType.Name == "AddressAttribute");
		if (customAttr == null || !customAttr.HasFields)
			return 0;

		var argRVA = customAttr.Fields.First(f => f.Name == "RVA");
		long rva = Convert.ToInt64(argRVA.Argument.Value.ToString()?.Substring(2), 16);
		return rva;
	}

	public static long GetMethodOffset(MethodDefinition method)
	{
		if (!method.HasCustomAttributes)
			return 0;

		var customAttr = method.CustomAttributes.FirstOrDefault(a => a.AttributeType.Name == "AddressAttribute");
		if (customAttr == null || !customAttr.HasFields)
			return 0;

		var argRVA = customAttr.Fields.First(f => f.Name == "Offset");
		long rva = Convert.ToInt64(argRVA.Argument.Value.ToString()?.Substring(2), 16);
		return rva;
	}
}
