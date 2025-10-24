using System.Buffers;
using System.Text.RegularExpressions;
using FbsDumper.Assembly;
using FbsDumper.Helpers;
using FbsDumper.Instructions;
using Mono.Cecil;
using Utf8StringInterpolation;
using ZLinq;

namespace FbsDumper.CLI;

public static partial class Parser
{
    private static string _dummyAssemblyDir = "DummyDll";
    public static string GameAssemblyPath = "libil2cpp.so";
    private static string _outputFileName = "BlueArchive.fbs";
    private static string? _customNameSpace = "FlatData";
    private static bool _forceSnakeCase;
    public static string? NameSpace2LookFor;
    private static readonly string FlatBaseType = "FlatBuffers.IFlatbufferObject";
    public static FlatBuilder FlatBufferBuilder = null!;
    public static readonly List<TypeDefinition> FlatEnumsToAdd = [];
    public static bool SuppressWarnings;
    public static bool NoAsmProcessing;
    public static bool Force;

    private static readonly Dictionary<string, string> TypeMap = new()
    {
        ["System.String"] = "string",
        ["System.Int16"] = "short",
        ["System.UInt16"] = "ushort",
        ["System.Int32"] = "int",
        ["System.UInt32"] = "uint",
        ["System.Int64"] = "long",
        ["System.UInt64"] = "ulong",
        ["System.Boolean"] = "bool",
        ["System.Single"] = "float",
        ["System.SByte"] = "int8",
        ["System.Byte"] = "uint8"
    };

    public static void Execute(string dummyDll, string gameAssembly, string outputFile, string nameSpace,
        bool forceSnakeCase, string? namespaceToLookFor, bool force, bool verbose, bool suppressWarnings)
    {
        if (verbose) Log.EnableDebugLogging();

        SuppressWarnings = suppressWarnings;

        _dummyAssemblyDir = dummyDll;
        GameAssemblyPath = gameAssembly;
        _outputFileName = outputFile;
        _customNameSpace = nameSpace;
        NameSpace2LookFor = namespaceToLookFor;
        _forceSnakeCase = forceSnakeCase;
        Force = force;

        if (!Directory.Exists(_dummyAssemblyDir))
        {
            Log.Global.LogDummyDirNotFound(_dummyAssemblyDir);
            Log.Error("Please provide a valid path using -dummydll or -d.");
            Log.Shutdown();
            Environment.Exit(1);
        }

        if (string.IsNullOrEmpty(GameAssemblyPath))
        {
            Log.Info("No game assembly provided. Skipping assembly analysis.");
            NoAsmProcessing = true;
        }
        else if (!File.Exists(GameAssemblyPath))
        {
            Log.Global.LogGameAssemblyNotFound(GameAssemblyPath);
            Log.Error("Please provide a valid path using -gameassembly or -a.");
            Log.Shutdown();
            Environment.Exit(1);
        }

        var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(_dummyAssemblyDir);
        var readerParameters = new ReaderParameters
        {
            AssemblyResolver = resolver
        };
        Log.Info("Reading game assemblies...");

        var blueArchiveDllPath = Path.Combine(_dummyAssemblyDir, "BlueArchive.dll");
        if (!File.Exists(blueArchiveDllPath))
        {
            Log.Global.LogFileNotFound("BlueArchive.dll", _dummyAssemblyDir);
            Log.Shutdown();
            Environment.Exit(1);
        }

        var asm = AssemblyDefinition.ReadAssembly(blueArchiveDllPath, readerParameters);

        var flatBuffersDllPath = Path.Combine(_dummyAssemblyDir, "FlatBuffers.dll");
        if (!File.Exists(flatBuffersDllPath))
        {
            Log.Global.LogFileNotFound("FlatBuffers.dll", _dummyAssemblyDir);
            Log.Shutdown();
            Environment.Exit(1);
        }

        var asmFbs = AssemblyDefinition.ReadAssembly(flatBuffersDllPath, readerParameters);

        FlatBufferBuilder = new FlatBuilder(asmFbs.MainModule);

        var architecture = NoAsmProcessing ? Architecture.X86 : TypeHelper.DetectArchitecture(GameAssemblyPath);
        var typeParser = TypeHelper.GetTypeParser(architecture);

        Log.Info(NoAsmProcessing ? "Using no assembly analysis mode" : $"Detected architecture: {architecture}");
        Log.Info("Getting a list of types...");

        var typeDefs = TypeHelper.GetAllFlatBufferTypes(asm.MainModule, FlatBaseType);

        FlatSchema schema = new();

        var done = 0;

        foreach (var typeDef in typeDefs)
        {
            Log.Global.LogProgress(done + 1, typeDefs.Count);
            var table = TypeHelper.TypeToTable(typeParser, typeDef);

            schema.FlatTables.Add(table);
            done += 1;
        }

        Log.Info("Adding enums...");
        foreach (var fEnum in FlatEnumsToAdd.AsValueEnumerable().Select(TypeHelper.TypeToEnum))
            schema.FlatEnums.Add(fEnum);

        Log.Info($"Writing schema to {_outputFileName}...");

        WriteSchema(_outputFileName, schema);

        Log.Info("Done.");
    }

    private static void WriteSchema(string fileName, FlatSchema schema)
    {
        using var buffer = Utf8String.CreateWriter(out var stringWriter);

        if (!string.IsNullOrEmpty(_customNameSpace))
            stringWriter.AppendFormat($"namespace {_customNameSpace};\n\n");

        foreach (var flatEnum in schema.FlatEnums)
        {
            WriteTableEnum(ref stringWriter, flatEnum);
            stringWriter.AppendLine();
        }

        foreach (var flatTable in schema.FlatTables)
        {
            WriteTable(ref stringWriter, flatTable);
            stringWriter.AppendLine();
        }

        stringWriter.Flush();

        File.WriteAllBytes(fileName, buffer.ToArray());
    }

    private static void WriteTable<TBufferWriter>(ref Utf8StringWriter<TBufferWriter> writer, FlatTable table)
        where TBufferWriter : IBufferWriter<byte>
    {
        writer.AppendFormat($"table {table.TableName} {{\n");

        if (table.NoCreate) writer.AppendLiteral("\t// No Create method\n");

        foreach (var tableField in table.Fields)
            WriteTableField(ref writer, tableField);

        writer.AppendLiteral("}\n");
    }

    private static void WriteTableEnum<TBufferWriter>(ref Utf8StringWriter<TBufferWriter> writer, FlatEnum fEnum)
        where TBufferWriter : IBufferWriter<byte>
    {
        var enumTypeName = SystemToStringType(fEnum.Type);
        writer.AppendFormat($"enum {fEnum.EnumName} : {enumTypeName} {{\n");

        for (var i = 0; i < fEnum.Fields.Count; i++)
        {
            var field = fEnum.Fields[i];
            var isLast = i == fEnum.Fields.Count - 1;
            writer.AppendFormat($"\t{field.Name} = {field.Value}{(isLast ? "" : ",")}\n");
        }

        writer.AppendLiteral("}\n");
    }

    private static void WriteTableField<TBufferWriter>(ref Utf8StringWriter<TBufferWriter> writer, FlatField field)
        where TBufferWriter : IBufferWriter<byte>
    {
        var fieldName = _forceSnakeCase ? CamelToSnake(field.Name) : field.Name;
        var fieldType = SystemToStringType(field.Type);

        if (field.IsArray) fieldType = $"[{fieldType}]";

        writer.AppendFormat($"\t{fieldName}: {fieldType}; // index 0x{field.Offset:X}\n");
    }

    private static string CamelToSnake(string camelStr)
    {
        var isAllUppercase = camelStr.All(char.IsUpper);
        if (string.IsNullOrEmpty(camelStr) || isAllUppercase)
            return camelStr;
        return CamelToSnakeRegex().Replace(camelStr, "$1_").ToLower();
    }

    private static string SystemToStringType(TypeDefinition field)
    {
        var fullName = field.FullName;
        if (TypeMap.TryGetValue(fullName, out var type)) return type;

        var name = field.Name;
        if (name.StartsWith("System.")) Log.Global.LogUnknownSystemType(name);

        return name;
    }

    [GeneratedRegex(@"(([a-z])(?=[A-Z][a-zA-Z])|([A-Z])(?=[A-Z][a-z]))")]
    private static partial Regex CamelToSnakeRegex();
}