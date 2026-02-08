using System;

namespace PhantomRender
{
    public class Class1
    {
#if NETFRAMEWORK
        // Código específico para .NET Framework (Seguro)
        public string GetVersion() => ".NET Framework";
#elif NETCOREAPP
        // Código específico para .NET Core / .NET 5+ (Optimizado/Unsafe)
        public unsafe string GetVersion() => ".NET 9.0 (Modern)";
#endif
    }
}
