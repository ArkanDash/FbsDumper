using Mono.Cecil;
using Mono.Cecil.Rocks;
using System.Text;
using static FbsDumper.MainApp;

namespace FbsDumper;

internal class TypeHelper
{
    private InstructionsParser instructionsResolver = new InstructionsParser(MainApp.GameAssemblyPath);

	public List<TypeDefinition> GetAllFlatBufferTypes(ModuleDefinition module, string baseTypeName)
    {
        List<TypeDefinition> ret = module.GetTypes().Where(t =>
            t.HasInterfaces &&
            t.Interfaces.Any(i => i.InterfaceType.FullName == baseTypeName)
			//&& t.Name == "AcademyFavorScheduleExcel"
		).ToList();

        if (!String.IsNullOrEmpty(MainApp.NameSpace2LookFor))
        {
            ret = ret.Where(t => t.Namespace == MainApp.NameSpace2LookFor).ToList();
        }

        // Dedupe
		ret = ret
	        .GroupBy(t => t.Name)
	        .Select(g => g.First())
	        .ToList();

		// todo: check nested types

		return ret;
    }

    public FlatTable? Type2Table(TypeDefinition targetType)
    {
        string typeName = targetType.Name;
        FlatTable ret = new FlatTable(typeName);

        //Console.WriteLine($"Dumping {typeName}");

        MethodDefinition? createMethod = targetType.Methods.FirstOrDefault(m =>
            m.Name == $"Create{typeName}" &&
            m.Parameters.Count > 1 &&
            m.Parameters.First().Name == "builder" &&
            m.IsStatic &&
            m.IsPublic
        );

        if (createMethod == null)
        {
            // Console.WriteLine($"[ERR] {targetType.FullName} does NOT contain a Create{typeName} function. Fields will be empty");
            ret.noCreate = true;
			return ret;
        }
        
        ProcessFields(ref ret, createMethod, targetType);

        return ret;
    }

    private void ProcessFields(ref FlatTable ret, MethodDefinition createMethod, TypeDefinition targetType)
    {
        Dictionary<int, ParameterDefinition> dict = new();

        try
        {
            dict = ParseCalls4CreateMethod(createMethod, targetType);
        }
        catch (Exception ex)
		{
			// method doesnt use parameter signatures directly, proceed to force dump
			ForceProcessFields(ref ret, createMethod, targetType);
            return;
        }

        dict = dict.OrderBy(t => t.Key).ToDictionary();

        foreach (KeyValuePair<int, ParameterDefinition> kvp in dict)
        {
            ParameterDefinition param = kvp.Value;
            TypeDefinition fieldType = param.ParameterType.Resolve();
            TypeReference fieldTypeRef = param.ParameterType;
            string fieldName = param.Name;

            if (fieldTypeRef is GenericInstanceType genericInstance)
            {
                // GenericInstanceType genericInstance = (GenericInstanceType)fieldTypeRef;
                fieldType = genericInstance.GenericArguments.First().Resolve();
                fieldTypeRef = genericInstance.GenericArguments.First();
            }

            FlatField field = new FlatField(fieldType, fieldName.Replace("_", "")); // needed for BA
            field.offset = kvp.Key;


			switch (fieldType.FullName)
            {
                case "FlatBuffers.StringOffset":
                    field.type = targetType.Module.TypeSystem.String.Resolve();
                    field.name = fieldName.EndsWith("Offset") ?
                                    new string(fieldName.SkipLast("Offset".Length).ToArray()) :
                                    fieldName;
                    field.name = field.name.Replace("_", ""); // needed for BA
                    break;
                case "FlatBuffers.VectorOffset":
                case "FlatBuffers.Offset":
                    string newFieldName = fieldName.EndsWith("Offset") ?
                                    new string(fieldName.SkipLast("Offset".Length).ToArray()) :
                                    fieldName;
                    newFieldName = newFieldName.Replace("_", ""); // needed for BA

                    MethodDefinition method = targetType.Methods.First(m =>
                        m.Name.ToLower() == newFieldName.ToLower()
                    );

                    TypeDefinition typeDefinition = method.ReturnType.Resolve();
                    field.isArray = fieldType.FullName == "FlatBuffers.VectorOffset";
                    fieldType = typeDefinition;
                    fieldTypeRef = method.ReturnType;

                    field.type = typeDefinition;
                    field.name = method.Name;
                    break;
                default:
                    break;

            }

            if (fieldTypeRef.IsGenericInstance)
            {
                GenericInstanceType newGenericInstance = (GenericInstanceType)fieldTypeRef;
                fieldType = newGenericInstance.GenericArguments.First().Resolve();
                fieldTypeRef = newGenericInstance.GenericArguments.First();
                field.type = fieldType;
            }

            if (field.type.IsEnum && !MainApp.flatEnumsToAdd.Contains(fieldType))
            {
                MainApp.flatEnumsToAdd.Add(fieldType);
            }

            ret.fields.Add(field);
        }
	}

    public Dictionary<int, ParameterDefinition> ParseCalls4CreateMethod(MethodDefinition createMethod, TypeDefinition targetType)
    {
        Dictionary<int, ParameterDefinition> ret = new Dictionary<int, ParameterDefinition>();
        Dictionary<long, MethodDefinition> typeMethods = new Dictionary<long, MethodDefinition>();

        foreach (MethodDefinition method in createMethod.Parameters[0].ParameterType.Resolve().GetMethods())
        {
            long rva = InstructionsParser.GetMethodRVA(method);
            typeMethods.Add(rva, method);
        }

		var instructions = instructionsResolver.GetInstructions(createMethod, false);

		InstructionsAnalyzer processer = new InstructionsAnalyzer();
		var calls = processer.AnalyzeCalls(instructions);
		bool hasStarted = false;
        int max = 0;
        int cur = 0;

        //StringBuilder sb = new StringBuilder();
        //foreach (var instr in instructions)
        //{
        //    sb.AppendLine($"{instr.IP:X} | {instr} -> {instr.Op0Kind} {instr.Op1Kind}");
        //}
        //File.WriteAllText("test.asm", sb.ToString());

        MethodDefinition endMethod = targetType.Methods.First(m => m.Name == $"End{targetType.Name}");
        long endMethodRVA = InstructionsParser.GetMethodRVA(endMethod);


		foreach (var call in calls)
		{
			// Console.WriteLine($"Call @ 0x{call.Address:X} -> {call.Target}");
            // Console.WriteLine($"  ArgIndex {call.ArgIndex}");
        }

        if (calls.All(c => c.ArgIndex == null || c.ArgIndex == 0))
        {
            return ret;
        }

		foreach (var call in calls)
		{
            long target = long.Parse(call.Target.Substring(2), System.Globalization.NumberStyles.HexNumber);
			switch (target)
            {
                case long addr when addr == flatBufferBuilder.StartObject:
                    hasStarted = true;
                    string arg1 = call.EdxValue!;
                    //Console.WriteLine($"EdxValue {arg1}");
					int cnt = arg1.StartsWith("0x") ? int.Parse(arg1.Substring(2), System.Globalization.NumberStyles.HexNumber) : 0;
                    max = cnt;
					//Console.WriteLine($"Has started, instance will have {cnt} fields");
                    break;
				case long addr when addr == flatBufferBuilder.EndObject:
					// Console.WriteLine($"Has ended");
					return ret;
                case long addr when addr == endMethodRVA:
					// Console.WriteLine($"Stop");
					return ret;
				default:
                    if (!hasStarted)
                    {
                        Console.WriteLine($"Skipping call for 0x{target:X} because StartObject hasn't been called yet");
                    }
                    if (!typeMethods.TryGetValue(target, out MethodDefinition? method) || method == null)
					{
						//Console.WriteLine($"Skipping call for 0x{target:X} because it's not part of FlatBufferBuilder");
						continue;
                    }
                    if (cur >= max)
                    {
						//Console.WriteLine($"Skipping call for 0x{target:X} because max amount of fields has been reached");
						continue;
					}
					string edx = call.EdxValue!;
                    int edxAsInt = edx.StartsWith("0x") ? int.Parse(edx.Substring(2), System.Globalization.NumberStyles.HexNumber) : 0;

                    //Console.WriteLine($"edx {edxAsInt} | index {call.ArgIndex} -1 | cnt {createMethod.Parameters.Count}");

                    //Console.WriteLine($"{edxAsInt} | {createMethod.Parameters[(int)call.ArgIndex-1]}");
					ret.Add(edxAsInt, createMethod.Parameters[(int)call.ArgIndex!-1]);
                    cur += 1;
					continue;
			}
		}

        return ret;
	}

	public static FlatEnum Type2Enum(TypeDefinition typeDef)
    {
        TypeDefinition retType = typeDef.GetEnumUnderlyingType().Resolve();
        FlatEnum ret = new FlatEnum(retType, typeDef.Name);

        foreach (FieldDefinition fieldDef in typeDef.Fields.Where(f => f.HasConstant))
        {
            FlatEnumField enumField = new FlatEnumField(fieldDef.Name, Convert.ToInt64(fieldDef.Constant));
            ret.fields.Add(enumField);
        }

        return ret;
    }

	private static void ForceProcessFields(ref FlatTable ret, MethodDefinition createMethod, TypeDefinition targetType)
	{
        int index = 0; // dirty af
		foreach (ParameterDefinition param in createMethod.Parameters.Skip(1))
		{
			TypeDefinition fieldType = param.ParameterType.Resolve();
			TypeReference fieldTypeRef = param.ParameterType;
			string fieldName = param.Name;

			if (fieldTypeRef is GenericInstanceType genericInstance)
			{
				// GenericInstanceType genericInstance = (GenericInstanceType)fieldTypeRef;
				fieldType = genericInstance.GenericArguments.First().Resolve();
				fieldTypeRef = genericInstance.GenericArguments.First();
			}

			FlatField field = new FlatField(fieldType, fieldName.Replace("_", "")); // needed for BA
            index += 1;

			switch (fieldType.FullName)
			{
				case "FlatBuffers.StringOffset":
					field.type = targetType.Module.TypeSystem.String.Resolve();
					field.name = fieldName.EndsWith("Offset") ?
									new string(fieldName.SkipLast("Offset".Length).ToArray()) :
									fieldName;
					field.name = field.name.Replace("_", ""); // needed for BA
					break;
				case "FlatBuffers.VectorOffset":
				case "FlatBuffers.Offset":
					string newFieldName = fieldName.EndsWith("Offset") ?
									new string(fieldName.SkipLast("Offset".Length).ToArray()) :
									fieldName;
					newFieldName = newFieldName.Replace("_", ""); // needed for BA

					MethodDefinition method = targetType.Methods.First(m =>
						m.Name.ToLower() == newFieldName.ToLower()
					);
					TypeDefinition typeDefinition = method.ReturnType.Resolve();
					field.isArray = fieldType.FullName == "FlatBuffers.VectorOffset";
					fieldType = typeDefinition;
					fieldTypeRef = method.ReturnType;

					field.type = typeDefinition;
					field.name = method.Name;
					break;
				default:
					break;

			}

			if (fieldTypeRef.IsGenericInstance)
			{
				GenericInstanceType newGenericInstance = (GenericInstanceType)fieldTypeRef;
				fieldType = newGenericInstance.GenericArguments.First().Resolve();
				fieldTypeRef = newGenericInstance.GenericArguments.First();
				field.type = fieldType;
			}

			if (field.type.IsEnum && !MainApp.flatEnumsToAdd.Contains(fieldType))
			{
				MainApp.flatEnumsToAdd.Add(fieldType);
			}

            field.offset = index;

			ret.fields.Add(field);
		}
	}

}
