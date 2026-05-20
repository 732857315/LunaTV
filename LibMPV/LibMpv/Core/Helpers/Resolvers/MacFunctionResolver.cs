// ReSharper disable IdentifierTypo
// ReSharper disable InconsistentNaming

namespace HanumanInstitute.LibMpv.Core;

public class MacFunctionResolver : FunctionResolverBase
{
    private const string Libdl = "libdl";
    private const int RTLD_LAZY = 0x001;
    private const int RTLD_NOW = 0x002;
    private const int RTLD_GLOBAL = 0x0100;
    private const int RTLD_LOCAL = 0x0000;


    protected override string GetNativeLibraryName(string libraryName, int version)
    {
        return $"{libraryName}.{version}.dylib";
    }

    protected override string[] GetSearchPaths()
    {
        return new[] { MpvApi.RootPath };
    }

    protected override IntPtr LoadNativeLibrary(string libraryName)
    {
        return dlopen(libraryName, RTLD_NOW | RTLD_GLOBAL);
    }

    protected override IntPtr FindFunctionPointer(IntPtr nativeLibraryHandle, string functionName)
    {
        return dlsym(nativeLibraryHandle, functionName);
    }

    [DllImport(Libdl)]
    public static extern IntPtr dlsym(IntPtr handle, string symbol);

    [DllImport(Libdl)]
    public static extern IntPtr dlopen(string fileName, int flag);
}