using AsmArm64;
using FbsDumper.Helpers;
using Iced.Intel;
using Mono.Cecil;
using ZLinq;

namespace FbsDumper.Instructions;

internal class InstructionsParser
{
    private const ushort DosHeaderMz = 0x5A4D;
    private const uint ElfMagic = 0x464C457F;

    private const ushort ImageFileMachineI386 = 0x014c;
    private const ushort ImageFileMachineAmd64 = 0x8664;
    private const ushort ImageFileMachineArm64 = 0xAA64;
    private const ushort ImageFileMachineArmnt = 0x01c4;

    private const ushort Em386 = 0x0003;
    private const ushort EmX86 = 0x003E;
    private readonly ByteArrayCodeReader? _codeReader;
    private readonly byte[] _fileBytes;

    public InstructionsParser(string gameAssemblyPath)
    {
        _fileBytes = File.ReadAllBytes(gameAssemblyPath);
        Architecture = DetectArchitecture(gameAssemblyPath);
        _codeReader = Architecture == Architecture.X86 ? new ByteArrayCodeReader(_fileBytes) : null;
    }

    public Architecture Architecture { get; }

    private static Architecture DetectArchitecture(string gameAssemblyPath)
    {
        try
        {
            using var fileStream = new FileStream(gameAssemblyPath, FileMode.Open, FileAccess.Read);
            using var reader = new BinaryReader(fileStream);

            if (IsPeFile(reader)) return GetPeArchitecture(reader);

            return IsElfFile(reader) ? GetElfArchitecture(reader) : GetArchitectureFromFilename(gameAssemblyPath);
        }
        catch
        {
            return GetArchitectureFromFilename(gameAssemblyPath);
        }
    }

    private static Architecture GetArchitectureFromFilename(string gameAssemblyPath)
    {
        var fileName = Path.GetFileName(gameAssemblyPath).ToLowerInvariant();
        return fileName switch
        {
            "libil2cpp.so" => Architecture.Arm64,
            "gameassembly.dll" => Architecture.X86,
            _ when fileName.EndsWith(".so") => Architecture.Arm64,
            _ when fileName.EndsWith(".dll") => Architecture.X86,
            _ => Architecture.Arm64
        };
    }

    private static bool IsPeFile(BinaryReader reader)
    {
        reader.BaseStream.Seek(0, SeekOrigin.Begin);
        var dosHeader = reader.ReadUInt16();
        return dosHeader == DosHeaderMz;
    }

    private static bool IsElfFile(BinaryReader reader)
    {
        reader.BaseStream.Seek(0, SeekOrigin.Begin);
        var elfMagic = reader.ReadUInt32();
        return elfMagic == ElfMagic;
    }

    private static Architecture GetPeArchitecture(BinaryReader reader)
    {
        reader.BaseStream.Seek(0x3C, SeekOrigin.Begin);
        var peHeaderOffset = reader.ReadUInt32();

        reader.BaseStream.Seek(peHeaderOffset + 4, SeekOrigin.Begin);
        var machine = reader.ReadUInt16();

        return machine switch
        {
            ImageFileMachineI386 => Architecture.X86,
            ImageFileMachineAmd64 => Architecture.X86,
            ImageFileMachineArm64 => Architecture.Arm64,
            ImageFileMachineArmnt => Architecture.Arm64,
            _ => Architecture.X86
        };
    }

    private static Architecture GetElfArchitecture(BinaryReader reader)
    {
        reader.BaseStream.Seek(18, SeekOrigin.Begin);
        var machine = reader.ReadUInt16();

        return machine switch
        {
            EmX86 or Em386 => Architecture.X86,
            _ => Architecture.Arm64
        };
    }

    public List<InstructionWithAddress> GetInstructions(MethodDefinition targetMethod, bool debug = false)
    {
        var rva = GetMethodRva(targetMethod);
        if (rva != 0)
        {
            if (Architecture == Architecture.Arm64) return GetArmInstructions(rva, debug);

            var offset = GetMethodOffset(targetMethod);
            return GetX86Instructions(rva, offset, debug);
        }

        Log.Warning($"Invalid RVA or offset for method: {targetMethod.FullName}");
        return [];
    }

    private List<InstructionWithAddress> GetArmInstructions(long rva, bool debug = false)
    {
        var instructions = new List<InstructionWithAddress>();

        const int instrSize = 4;
        var currentOffset = rva;

        while (currentOffset + instrSize <= _fileBytes.Length)
        {
            var instrBytes = new byte[instrSize];
            Array.Copy(_fileBytes, currentOffset, instrBytes, 0, instrSize);

            var instrValue = BitConverter.ToUInt32(instrBytes, 0);

            try
            {
                var instr = Arm64Instruction.Decode(instrValue);
                var instrWithAddress = new InstructionWithAddress(instr, (ulong)currentOffset);
                instructions.Add(instrWithAddress);

                if (debug)
                {
                    var operandString = GetOperandString(instr);
                    Log.Global.LogInstruction((ulong)currentOffset, instr.Mnemonic.ToString().ToLower(), operandString);
                }

                currentOffset += instrSize;

                if (instr.Mnemonic.ToString().Equals("ret", StringComparison.CurrentCultureIgnoreCase))
                    break;
            }
            catch
            {
                break;
            }
        }

        return instructions;
    }

    private List<InstructionWithAddress> GetX86Instructions(long rva, long offset, bool debug = false)
    {
        if (_codeReader == null) return [];

        _codeReader.Position = (int)offset;
        var decoder = Decoder.Create(IntPtr.Size * 8, _codeReader);
        decoder.IP = (ulong)rva;
        var instructions = new List<InstructionWithAddress>();

        while (true)
        {
            var instruction = decoder.Decode();
            var instrWithAddress = new InstructionWithAddress(null, instruction.IP)
            {
                X86Instruction = instruction
            };
            instructions.Add(instrWithAddress);

            if (debug)
            {
                var instructionStr = instruction.ToString();
                Log.Global.LogInstruction(instruction.IP, instruction.Mnemonic.ToString().ToLower(), instructionStr);
            }

            if (instruction.Mnemonic == Mnemonic.Ret) break;
        }

        return instructions;
    }

    private static string GetOperandString(Arm64Instruction instruction)
    {
        return instruction.Operands.Count == 0
            ? string.Empty
            : instruction.Operands.AsValueEnumerable().Select(operand => operand.ToString()).JoinToString(", ");
    }

    public static long GetMethodRva(MethodDefinition method)
    {
        if (!method.HasCustomAttributes)
            return 0;

        var customAttr = method.CustomAttributes.FirstOrDefault(a => a.AttributeType.Name == "AddressAttribute");
        if (customAttr is not { HasFields: true })
            return 0;

        var argRva = customAttr.Fields.First(f => f.Name == "RVA");
        var rva = Convert.ToInt64(argRva.Argument.Value.ToString()?[2..], 16);
        return rva;
    }

    private static long GetMethodOffset(MethodDefinition method)
    {
        if (!method.HasCustomAttributes)
            return 0;

        var customAttr = method.CustomAttributes.FirstOrDefault(a => a.AttributeType.Name == "AddressAttribute");
        if (customAttr is not { HasFields: true })
            return 0;

        var argOffset = customAttr.Fields.First(f => f.Name == "Offset");
        var offset = Convert.ToInt64(argOffset.Argument.Value.ToString()?[2..], 16);
        return offset;
    }
}

internal record InstructionWithAddress(Arm64Instruction? Instruction, ulong Address)
{
    public Instruction X86Instruction { get; init; }

    public string Mnemonic => Instruction != null
        ? Instruction.Value.Mnemonic.ToString().ToLower()
        : X86Instruction.Mnemonic.ToString().ToLower();

    public string? Operand
    {
        get
        {
            if (Instruction == null) return X86Instruction.ToString();
            return Instruction.Value.Operands.Count == 0
                ? null
                : Instruction.Value.Operands.AsValueEnumerable().Select(operand => operand.ToString())
                    .JoinToString(", ");
        }
    }
}

public enum Architecture
{
    Arm64,
    X86
}