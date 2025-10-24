using System.Globalization;
using FbsDumper.Assembly.TypeParsers;
using FbsDumper.CLI;
using FbsDumper.Helpers;
using FbsDumper.Instructions;
using Mono.Cecil;
using Mono.Cecil.Rocks;
using ZLinq;

namespace FbsDumper.Assembly;

internal static class TypeHelper
{
    public static readonly InstructionsParser InstructionsResolver = new(Parser.GameAssemblyPath);

    public static ITypeParser GetTypeParser(Architecture architecture)
    {
        return architecture switch
        {
            Architecture.Arm64 => new ArmTypeParser(),
            Architecture.X86 => new X86TypeParser(),
            _ => throw new ArgumentException($"Unsupported architecture: {architecture}")
        };
    }

    public static string CleanFieldName(string fieldName)
    {
        return fieldName.Replace("_", "");
    }

    public static Architecture DetectArchitecture(string gameAssemblyPath)
    {
        var instructionsParser = new InstructionsParser(gameAssemblyPath);
        return instructionsParser.Architecture;
    }

    public static List<TypeDefinition> GetAllFlatBufferTypes(ModuleDefinition module, string baseTypeName)
    {
        List<TypeDefinition> ret =
        [
            .. module.GetTypes().AsValueEnumerable().Where(t =>
                t.HasInterfaces &&
                t.Interfaces.Any(i => i.InterfaceType.FullName == baseTypeName)
            ).ToArray()
        ];

        if (!string.IsNullOrEmpty(Parser.NameSpace2LookFor))
            ret = [.. ret.AsValueEnumerable().Where(t => t.Namespace == Parser.NameSpace2LookFor).ToArray()];

        // Dedupe
        ret = [..ret.AsValueEnumerable().DistinctBy(t => t.Name).ToArray()];

        return ret;
    }

    public static FlatTable TypeToTable(ITypeParser typeParser, TypeDefinition targetType)
    {
        var typeName = targetType.Name;
        var ret = new FlatTable(typeName);

        var createMethod = targetType.Methods.FirstOrDefault(m =>
            m.Name == $"Create{typeName}" &&
            m.Parameters.Count > 1 &&
            m.Parameters.First().Name == "builder" &&
            m is { IsStatic: true, IsPublic: true }
        );

        if (Parser.NoAsmProcessing)
        {
            if (createMethod == null)
                return Parser.Force
                    ? ProcessWithForceMethod(ref ret, targetType)
                    : ProcessWithoutCreateMethod(ret, targetType);

            FieldParser.ForceProcessFields(ref ret, createMethod, targetType);
            return ret;
        }

        if (createMethod == null)
            return ProcessWithoutCreateMethod(ret, targetType);

        typeParser.ProcessFields(ref ret, createMethod, targetType);
        return ret;
    }

    private static FlatTable ProcessWithForceMethod(ref FlatTable ret, TypeDefinition targetType)
    {
        FieldParser.ProcessFieldsByMethods(ref ret, targetType);
        return ret;
    }

    private static FlatTable ProcessWithoutCreateMethod(FlatTable ret, TypeDefinition targetType)
    {
        Log.Warning($"{targetType.FullName} does NOT contain a Create{targetType.Name} function. Fields will be empty");
        ret.NoCreate = true;
        return ret;
    }

    public static FlatEnum TypeToEnum(TypeDefinition typeDef)
    {
        var retType = typeDef.GetEnumUnderlyingType().Resolve();
        var ret = new FlatEnum(retType, typeDef.Name);

        foreach (var fieldDef in typeDef.Fields.AsValueEnumerable().Where(f => f.HasConstant))
        {
            var enumField = new FlatEnumField(fieldDef.Name, Convert.ToInt64(fieldDef.Constant));
            ret.Fields.Add(enumField);
        }

        return ret;
    }

    public static bool TryParseTarget(string target, out long result)
    {
        result = 0;

        if (target.StartsWith("0x"))
        {
            var targetHex = target[2..];
            return !string.IsNullOrEmpty(targetHex) &&
                   long.TryParse(targetHex, NumberStyles.HexNumber, null, out result);
        }

        if (!target.StartsWith('#')) return false;
        var targetDecimal = target[1..];
        return !string.IsNullOrEmpty(targetDecimal) &&
               long.TryParse(targetDecimal, NumberStyles.Integer, null, out result);
    }

    public static List<InstructionsAnalyzer.CallInfo> GetAnalyzedCalls(MethodDefinition createMethod)
    {
        var instructions = InstructionsResolver.GetInstructions(createMethod);
        var analyzer = InstructionsAnalyzer.GetAnalyzer(InstructionsResolver.Architecture);
        return analyzer.AnalyzeCalls(instructions);
    }

    public static long GetEndMethodRva(TypeDefinition targetType)
    {
        var endMethod = targetType.Methods.First(m => m.Name == $"End{targetType.Name}");
        return InstructionsParser.GetMethodRva(endMethod);
    }
}

internal interface ITypeParser
{
    void ProcessFields(ref FlatTable ret, MethodDefinition createMethod, TypeDefinition targetType);
}