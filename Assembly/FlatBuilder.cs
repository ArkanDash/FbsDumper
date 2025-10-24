using FbsDumper.Instructions;
using Mono.Cecil;
using ZLinq;

namespace FbsDumper.Assembly;

public class FlatBuilder
{
    public readonly long EndObject;
    public readonly Dictionary<long, MethodDefinition> Methods;
    public readonly long StartObject;

    public FlatBuilder(ModuleDefinition flatBuffersDllModule)
    {
        var flatBufferBuilderType = flatBuffersDllModule.GetType("FlatBuffers.FlatBufferBuilder");

        var methodsWithRva = flatBufferBuilderType.Methods
            .AsValueEnumerable()
            .Select(method => new { Method = method, Rva = InstructionsParser.GetMethodRva(method) })
            .ToArray();

        Methods = methodsWithRva.AsValueEnumerable().ToDictionary(x => x.Rva, x => x.Method);

        StartObject = methodsWithRva.AsValueEnumerable().First(x => x.Method.Name == "StartObject").Rva;
        EndObject = methodsWithRva.AsValueEnumerable().First(x => x.Method.Name == "EndObject").Rva;
    }
}