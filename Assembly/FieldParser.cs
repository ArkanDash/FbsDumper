using FbsDumper.CLI;
using Mono.Cecil;
using Mono.Cecil.Rocks;
using ZLinq;

namespace FbsDumper.Assembly;

public static class FieldParser
{
    public static TypeDefinition ProcessOffsets(TypeDefinition targetType, TypeDefinition fieldType, FlatField field,
        string fieldName, ref TypeReference fieldTypeRef)
    {
        switch (fieldType.FullName)
        {
            case "FlatBuffers.StringOffset":
                field.Type = targetType.Module.TypeSystem.String.Resolve();
                field.Name = fieldName.EndsWith("Offset")
                    ? new string([.. fieldName.SkipLast("Offset".Length)])
                    : fieldName;
                field.Name = TypeHelper.CleanFieldName(field.Name); // Needed for BA
                break;

            case "FlatBuffers.VectorOffset":
            case "FlatBuffers.Offset":
                var newFieldName = fieldName.EndsWith("Offset")
                    ? new string([.. fieldName.SkipLast("Offset".Length)])
                    : fieldName;
                newFieldName = TypeHelper.CleanFieldName(newFieldName); // Needed for BA

                var method = targetType.Methods.First(m =>
                    m.Name.Equals(newFieldName, StringComparison.CurrentCultureIgnoreCase)
                );

                var typeDefinition = method.ReturnType.Resolve();

                field.IsArray = fieldType.FullName == "FlatBuffers.VectorOffset";

                fieldType = typeDefinition;
                fieldTypeRef = method.ReturnType;

                field.Type = typeDefinition;
                field.Name = method.Name;
                break;
        }

        return fieldType;
    }

    private static TypeReference ProcessOffsetsByMethods(TypeDefinition targetType, TypeDefinition fieldType,
        FlatField field,
        string fieldName, TypeReference fieldTypeRef, MethodDefinition method)
    {
        switch (fieldType.FullName)
        {
            case "FlatBuffers.StringOffset":
                field.Type = targetType.Module.TypeSystem.String.Resolve();
                field.Name = fieldName.EndsWith("Offset")
                    ? new string([.. fieldName.SkipLast("Offset".Length)])
                    : fieldName;
                break;
            case "FlatBuffers.VectorOffset":
            case "FlatBuffers.Offset":
                var newFieldName = fieldName.EndsWith("Offset")
                    ? new string([.. fieldName.SkipLast("Offset".Length)])
                    : fieldName;
                newFieldName = TypeHelper.CleanFieldName(newFieldName); // Needed for BA

                if (fieldType.FullName == "FlatBuffers.VectorOffset")
                {
                    var startMethod = targetType.Methods.First(m => m.Name == $"Start{newFieldName}Vector");
                    fieldType = startMethod.Parameters[1].ParameterType.Resolve();
                    field.IsArray = true;
                }

                fieldTypeRef = fieldType;
                field.Type = fieldType;
                field.Name = method.Name;

                break;
        }

        return fieldTypeRef;
    }

    public static void ForceProcessFields(ref FlatTable ret, MethodDefinition createMethod, TypeDefinition targetType)
    {
        foreach (var (param, offset) in createMethod.Parameters.AsValueEnumerable().Skip(1)
                     .Select((p, i) => (p, i + 1)))
        {
            var fieldType = param.ParameterType.Resolve();
            var fieldTypeRef = param.ParameterType;
            var fieldName = param.Name;

            fieldTypeRef = ExtractGeneric(fieldTypeRef, ref fieldType);

            FlatField field = new(fieldType, TypeHelper.CleanFieldName(fieldName))
            {
                Offset = offset
            };

            fieldType = ProcessOffsets(targetType, fieldType, field, fieldName, ref fieldTypeRef);
            fieldType = SetGeneric(fieldTypeRef, fieldType, field);

            SaveEnum(field, fieldType);

            ret.Fields.Add(field);
        }
    }

    public static void ProcessFieldsByMethods(ref FlatTable ret, TypeDefinition targetType)
    {
        foreach (var method in targetType.GetMethods().AsValueEnumerable().Where(m =>
                     m.IsPublic && m.IsStatic && m.Name.StartsWith("Add") && m.HasParameters &&
                     m.Parameters.Count == 2 && m.Parameters.First().Name == "builder"))
        {
            var param = method.Parameters[1];

            var fieldType = param.ParameterType.Resolve();
            var fieldTypeRef = param.ParameterType;
            var fieldName = param.Name;

            fieldTypeRef = ExtractGeneric(fieldTypeRef, ref fieldType);
            FlatField field = new(fieldType, fieldName);

            fieldTypeRef = ProcessOffsetsByMethods(targetType, fieldType, field, fieldName, fieldTypeRef, method);
            SetGeneric(fieldTypeRef, fieldType, field);

            ret.Fields.Add(field);
        }
    }

    public static TypeReference ExtractGeneric(TypeReference fieldTypeRef, ref TypeDefinition fieldType)
    {
        if (fieldTypeRef is not GenericInstanceType genericInstance) return fieldTypeRef;
        fieldType = genericInstance.GenericArguments.First().Resolve();
        fieldTypeRef = genericInstance.GenericArguments.First();

        return fieldTypeRef;
    }

    public static TypeDefinition SetGeneric(TypeReference fieldTypeRef, TypeDefinition fieldType, FlatField field)
    {
        if (!fieldTypeRef.IsGenericInstance) return fieldType;

        var newGenericInstance = (GenericInstanceType)fieldTypeRef;
        fieldType = newGenericInstance.GenericArguments.First().Resolve();
        field.Type = fieldType;

        return fieldType;
    }

    public static void SaveEnum(FlatField field, TypeDefinition fieldType)
    {
        if (field.Type.IsEnum && !Parser.FlatEnumsToAdd.Contains(fieldType))
            Parser.FlatEnumsToAdd.Add(fieldType);
    }
}