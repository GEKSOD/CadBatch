using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;

namespace CadBatch.Pdf
{
    // A plug-in cannot supply binding redirects through the host's exe.config.
    // Resolve compatible servicing versions only for our own bundled PDF assemblies.
    internal sealed class FrameworkPdfDependencies : IDisposable
    {
        private static readonly HashSet<string> Bundled=new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "PdfSharp","PdfSharp.Shared","PdfSharp.System","PdfSharp.Cryptography","PdfSharp.WPFonts",
            "Microsoft.Extensions.Logging.Abstractions","Microsoft.Extensions.DependencyInjection.Abstractions",
            "Microsoft.Bcl.AsyncInterfaces","System.Buffers","System.Memory","System.Numerics.Vectors",
            "System.Runtime.CompilerServices.Unsafe","System.Security.Cryptography.Pkcs","System.Threading.Tasks.Extensions"
        };
        private readonly string directory=Path.GetDirectoryName(typeof(FrameworkPdfDependencies).Assembly.Location)!;
        private readonly bool active;
        public FrameworkPdfDependencies()
        {
            active=RuntimeInformation.FrameworkDescription.StartsWith(".NET Framework",StringComparison.OrdinalIgnoreCase);
            if(active)AppDomain.CurrentDomain.AssemblyResolve+=Resolve;
        }
        private Assembly? Resolve(object sender,ResolveEventArgs args)
        {
            // Framework reflection/type loading can report a null requesting assembly.
            // The handler is attached only for the duration of this PDF operation.
            if(args.RequestingAssembly!=null)
            {
                string requester=args.RequestingAssembly.GetName().Name??"";
                if(requester!="CadBatch.Pdf" && !Bundled.Contains(requester))return null;
                if(!string.Equals(Path.GetDirectoryName(args.RequestingAssembly.Location),directory,StringComparison.OrdinalIgnoreCase))return null;
            }
            var requested=new AssemblyName(args.Name);
            if(requested.Name==null || !Bundled.Contains(requested.Name))return null;
            string candidate=Path.Combine(directory,requested.Name+".dll");
            if(!File.Exists(candidate))return null;
            var actual=AssemblyName.GetAssemblyName(candidate);
            if(!string.Equals(actual.Name,requested.Name,StringComparison.OrdinalIgnoreCase) ||
                !(actual.GetPublicKeyToken()??Array.Empty<byte>()).SequenceEqual(requested.GetPublicKeyToken()??Array.Empty<byte>()) ||
                actual.Version==null || requested.Version==null || actual.Version.Major!=requested.Version.Major ||
                actual.Version.Minor!=requested.Version.Minor || actual.Version<requested.Version)return null;
            return Assembly.LoadFrom(candidate);
        }
        public void Dispose() {if(active)AppDomain.CurrentDomain.AssemblyResolve-=Resolve;}
    }
}
