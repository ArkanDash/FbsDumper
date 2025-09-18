namespace FbsDumper.CLI;

public static class Args
{
    /// <summary>
    ///     FlatBuffer Schema Dumper
    /// </summary>
    /// <param name="dummyDll">-d, Specifies the dummy DLL directory.</param>
    /// <param name="gameAssembly">-a, Specifies the path to libil2cpp.so (ARM) or GameAssembly.dll (x86/x64). Leave empty to skip assembly analysis.</param>
    /// <param name="outputFile">-o, Specifies the output file.</param>
    /// <param name="namespace">-n, Specifies the flatdata namespace</param>
    /// <param name="forceSnakeCase">-s, Force snake case.</param>
    /// <param name="namespaceToLookFor">-nl, Specifies the namespace to look for</param>
    /// <param name="force">-f, Force processing using Add methods when no Create method exists.</param>
    /// <param name="verbose">-v, Enable verbose debug logging.</param>
    /// <param name="suppressWarnings">-sw, Suppress warning messages.</param>
    public static void Run(
        string dummyDll,
        string gameAssembly = "",
        string outputFile = "BlueArchive.fbs",
        string @namespace = "FlatData",
        bool forceSnakeCase = false,
        string? namespaceToLookFor = null,
        bool force = false,
        bool verbose = false,
        bool suppressWarnings = false)
    {
        Parser.Execute(dummyDll, gameAssembly, outputFile, @namespace, forceSnakeCase, namespaceToLookFor, force, verbose,
            suppressWarnings);
    }
}