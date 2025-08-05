using Mono.Cecil;
using System.Text;
using System.Text.RegularExpressions;
using PowerArgs;

namespace FbsDumper;

public class MainApp
{
    private static string DummyAssemblyDir = "DummyDll";
	public static string LibIl2CppPath = "GameAssembly.dll"; // change it to the actual path
	private static string OutputFileName = "BlueArchive.fbs";
    private static string? CustomNameSpace = "FlatData"; // can also be String.Empty, "", or null to not specify namespace
    public static bool ForceSnakeCase = false;
	public static string? NameSpace2LookFor = null; // can also be MX.Data.Excel or FlatData to specify different namespaces
	private static string FlatBaseType = "FlatBuffers.IFlatbufferObject";
    public static FlatBufferBuilder flatBufferBuilder;
    public static List<TypeDefinition> flatEnumsToAdd = new List<TypeDefinition>(); // for GetAllFlatBufferTypes -> getting enums part

    public static void Main(string[] args)
    {
        var parsedArgs = Args.Parse<FbsDumperArgs>(args);

        DummyAssemblyDir = parsedArgs.DummyDll ?? "DummyDll";
        LibIl2CppPath = parsedArgs.GameAssembly ?? "GameAssembly.dll";
        OutputFileName = parsedArgs.OutputFile ?? "BlueArchive.fbs";
        CustomNameSpace = parsedArgs.Namespace ?? "FlatData";
        NameSpace2LookFor = parsedArgs.NamespaceToLookFor ?? null;
        ForceSnakeCase = parsedArgs.ForceSnakeCase;

        if (!Directory.Exists(parsedArgs.DummyDll))
        {
            Console.WriteLine($"[ERR] Dummy assembly directory '{parsedArgs.DummyDll}' not found.");
            Environment.Exit(1);
        }
        if (!File.Exists(parsedArgs.GameAssembly))
        {
            Console.WriteLine($"[ERR] GameAssembly path '{parsedArgs.GameAssembly}' not found.");
            Environment.Exit(1);
        }

        DefaultAssemblyResolver resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(DummyAssemblyDir);
        ReaderParameters readerParameters = new ReaderParameters();
        readerParameters.AssemblyResolver = resolver;
        Console.WriteLine("Reading game assemblies...");

        string blueArchiveDllPath = Path.Combine(DummyAssemblyDir, "BlueArchive.dll");
        if (!File.Exists(blueArchiveDllPath))
        {
            Console.WriteLine($"[ERR] BlueArchive.dll not found in '{DummyAssemblyDir}'.");
            Environment.Exit(1);
        }
        AssemblyDefinition asm = AssemblyDefinition.ReadAssembly(blueArchiveDllPath, readerParameters);

        string flatBuffersDllPath = Path.Combine(DummyAssemblyDir, "FlatBuffers.dll");
        if (!File.Exists(flatBuffersDllPath))
        {
            Console.WriteLine($"[ERR] FlatBuffers.dll not found in '{DummyAssemblyDir}'.");
            Environment.Exit(1);
        }
		AssemblyDefinition asmFBS = AssemblyDefinition.ReadAssembly(flatBuffersDllPath, readerParameters);

        flatBufferBuilder = new FlatBufferBuilder(asmFBS.MainModule);
        TypeHelper typeHelper = new TypeHelper();
		Console.WriteLine("Getting a list of types...");
		List<TypeDefinition> typeDefs = typeHelper.GetAllFlatBufferTypes(asm.MainModule, FlatBaseType);
        FlatSchema schema = new FlatSchema();
        int done = 0;
		foreach (TypeDefinition typeDef in typeDefs)
		{
            Console.Write($"Disassembling types ({done + 1}/{typeDefs.Count})...      \r");
            FlatTable? table = typeHelper.Type2Table(typeDef);
            if (table == null)
            {
                Console.WriteLine($"[ERR] Error dumping table for {typeDef.FullName}");
                continue;
            }
            schema.flatTables.Add(table);
            done++;
        }
		Console.WriteLine($"Adding enums...");
		foreach (TypeDefinition typeDef in flatEnumsToAdd)
        {
            FlatEnum? fEnum = TypeHelper.Type2Enum(typeDef);
            if (fEnum == null)
            {
                Console.WriteLine($"[ERR] Error dumping enum for {typeDef.FullName}");
                continue;
            }
            schema.flatEnums.Add(fEnum);
        }
		Console.WriteLine($"Writing schema to {OutputFileName}...");
        File.WriteAllText(OutputFileName, SchemaToString(schema));
        Console.WriteLine($"Done.");
	}

    private static string SchemaToString(FlatSchema schema)
    {
        StringBuilder sb = new StringBuilder();

        if (!string.IsNullOrEmpty(CustomNameSpace))
        {
            sb.AppendLine($"namespace {CustomNameSpace};\n");
        }

        foreach (FlatEnum flatEnum in schema.flatEnums)
        {
            sb.AppendLine(TableEnumToString(flatEnum));
        }

        foreach (FlatTable table in schema.flatTables)
        {
            sb.AppendLine(TableToString(table));
        }

        return sb.ToString();
    }

    private static string TableToString(FlatTable table)
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine($"table {table.tableName} {{");

        if (table.noCreate)
			sb.AppendLine("\t// No Create method");

		foreach (FlatField field in table.fields)
        {
            sb.AppendLine(TableFieldToString(field));
        }

        sb.AppendLine("}");

        return sb.ToString();
    }

    private static string TableEnumToString(FlatEnum fEnum)
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine($"enum {fEnum.enumName} : {SystemToStringType(fEnum.type)} {{");

        for (int i = 0; i < fEnum.fields.Count; i++)
        {
            FlatEnumField field = fEnum.fields[i];
            sb.AppendLine(TableEnumFieldToString(field, i == fEnum.fields.Count-1));
        }

        sb.AppendLine("}");

        return sb.ToString();
    }

    private static string TableEnumFieldToString(FlatEnumField field, bool isLast = false)
    {
        return $"\t{field.name} = {field.value}{(isLast ? "" : ",")}";
    }

    private static string TableFieldToString(FlatField field)
    {
        StringBuilder stringBuilder = new StringBuilder();
        stringBuilder.Append($"\t{(ForceSnakeCase ? CamelToSnake(field.name) : field.name)}: ");

        string fieldType = SystemToStringType(field.type);

        fieldType = field.isArray ? $"[{fieldType}]" : fieldType;

        stringBuilder.Append($"{fieldType}; // index 0x{field.offset:X}");

        return stringBuilder.ToString();
    }

    static string CamelToSnake(string camelStr)
    {
        bool isAllUppercase = camelStr.All(char.IsUpper); // Beebyte
        if (string.IsNullOrEmpty(camelStr) || isAllUppercase)
            return camelStr;
        return Regex.Replace(camelStr, @"(([a-z])(?=[A-Z][a-zA-Z])|([A-Z])(?=[A-Z][a-z]))", "$1_").ToLower();
    }

    public static string SystemToStringType(TypeDefinition field)
    {
        string fieldType = field.Name;

        switch (field.FullName)
        {
            // all system types to flatbuffer format

            case "System.String":
                fieldType = "string";
                break;
            case "System.Int16":
                fieldType = "short";
                break;
            case "System.UInt16":
                fieldType = "ushort";
                break;
            case "System.Int32":
                fieldType = "int";
                break;
            case "System.UInt32":
                fieldType = "uint";
                break;
            case "System.Int64":
                fieldType = "long";
                break;
            case "System.UInt64":
                fieldType = "ulong";
                break;
            case "System.Boolean":
                fieldType = "bool";
                break;
            case "System.Single":
                fieldType = "float";
                break;
            case "System.SByte":
                fieldType = "int8";
                break;
            case "System.Byte":
                fieldType = "uint8";
                break;
            default:
                if (fieldType.StartsWith("System."))
                {
                    Console.WriteLine($"[WARN] unknown system type {fieldType}");
                }
                break;
        }

        return fieldType;
    }
}

public class FlatBufferBuilder
{
    public long StartObject;
    public long EndObject;
    public Dictionary<long, MethodDefinition> methods;

    public FlatBufferBuilder(ModuleDefinition flatBuffersDllModule)
    {
        methods = new Dictionary<long, MethodDefinition>();
		TypeDefinition FlatBufferBuilderType = flatBuffersDllModule.GetType("FlatBuffers.FlatBufferBuilder");
        foreach (MethodDefinition method in FlatBufferBuilderType.Methods)
        {
            long rva = InstructionsParser.GetMethodRVA(method);
            {
                switch (method.Name)
                {
                    case "StartObject":
                        StartObject = rva;
                        break;
                    case "EndObject":
                        EndObject = rva;
                        break;
                    default:
                        // do nothing
                        break;
				}
            }
            methods.Add(rva, method);
        }
	}
}