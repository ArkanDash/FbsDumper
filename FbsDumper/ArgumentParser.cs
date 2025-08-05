using PowerArgs;

namespace FbsDumper;

class FbsDumperArgs
{
    [ArgRequired]
    [ArgShortcut("d")]
    [ArgDescription("Specifies the dummy DLL.")]
    public string? DummyDll { get; set; }

    [ArgRequired]
    [ArgShortcut("a")]
    [ArgDescription("Specifies the path to GameAssembly.dll.")]
    public string? GameAssembly { get; set; }

    [ArgShortcut("o")]
    [ArgDescription("Specifies the output directory.")]
    public string? OutputFile { get; set; }

    [ArgShortcut("n")]
    [ArgDescription("Specifies the flatdata namespace")]
    public string? Namespace { get; set; }

    [ArgShortcut("s")]
    [ArgDefaultValue(false)]
    [ArgDescription("Force snake case.")]
    public bool ForceSnakeCase { get; set; }

    [ArgShortcut("nl")]
    [ArgDescription("Specifies the namespace to look for")]
    public string? NamespaceToLookFor { get; set; }
}