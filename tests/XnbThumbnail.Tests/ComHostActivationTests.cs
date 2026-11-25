using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace XnbThumbnail.Tests
{
    /// <summary>
    /// Activates the handler the way dllhost.exe does, without the registry: load
    /// XnbThumbnail.comhost.dll, call its exported DllGetClassObject for the CLSID, create the
    /// object through IClassFactory, then Initialize and GetThumbnail, all through raw COM
    /// vtables rather than .NET's interop. This catches what the in-process tests can't: a
    /// comhost that fails to start the runtime from its runtimeconfig.json (a self-contained
    /// build fails here with 0x80008093), a missing deps.json or XnbCore.dll, or a CLSID that
    /// doesn't match.
    /// </summary>
    [TestClass]
    public sealed unsafe class ComHostActivationTests
    {
        private static string ComHostPath()
        {
            // Recorded at build time by XnbThumbnail.Tests.csproj's RecordComHostPath target.
            string path = typeof(ComHostActivationTests).Assembly
                .GetCustomAttributes<AssemblyMetadataAttribute>()
                .Single(a => a.Key == "XnbThumbnailComHost").Value;
            Assert.IsTrue(File.Exists(path), $"Built comhost not found at {path}.");
            return path;
        }

        // IUnknown: 0 QueryInterface, 1 AddRef, 2 Release.
        private static IntPtr Slot(IntPtr comObject, int index)
        {
            return (*(IntPtr**)comObject)[index];
        }

        private static int QueryInterface(IntPtr unk, Guid iid, out IntPtr result)
        {
            IntPtr r;
            int hr = ((delegate* unmanaged[Stdcall]<IntPtr, Guid*, IntPtr*, int>)Slot(unk, 0))(unk, &iid, &r);
            result = r;
            return hr;
        }

        private static void Release(IntPtr unk)
        {
            if (unk != IntPtr.Zero)
                ((delegate* unmanaged[Stdcall]<IntPtr, uint>)Slot(unk, 2))(unk);
        }

        [TestMethod]
        public void Color4x4ThroughDllGetClassObjectIsPixelExact()
        {
            IntPtr lib = NativeLibrary.Load(ComHostPath());
            var dllGetClassObject = (delegate* unmanaged[Stdcall]<Guid*, Guid*, IntPtr*, int>)
                NativeLibrary.GetExport(lib, "DllGetClassObject");

            byte[] xnb = Samples.Color4x4Xnb();
            IntPtr factory = IntPtr.Zero, init = IntPtr.Zero, thumb = IntPtr.Zero, stream = IntPtr.Zero, hbmp = IntPtr.Zero;
            try
            {
                Guid clsid = Native.ClsidXnbThumbnailProvider;
                Guid iidFactory = Native.IID_IClassFactory;
                int hr = dllGetClassObject(&clsid, &iidFactory, &factory);
                Assert.AreEqual(Native.S_OK, hr, $"DllGetClassObject returned 0x{hr:X8}.");

                // IClassFactory: 3 CreateInstance(IUnknown* outer, REFIID, void**).
                Guid iidInit = Native.IID_IInitializeWithStream;
                hr = ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, Guid*, IntPtr*, int>)Slot(factory, 3))(
                    factory, IntPtr.Zero, &iidInit, &init);
                Assert.AreEqual(Native.S_OK, hr, $"CreateInstance returned 0x{hr:X8}.");

                hr = QueryInterface(init, Native.IID_IThumbnailProvider, out thumb);
                Assert.AreEqual(Native.S_OK, hr, $"QueryInterface(IThumbnailProvider) returned 0x{hr:X8}.");

                // IInitializeWithStream: 3 Initialize(IStream*, DWORD grfMode).
                stream = Native.SHCreateMemStreamPtr(xnb, (uint)xnb.Length);
                Assert.AreNotEqual(IntPtr.Zero, stream, "SHCreateMemStream failed.");
                hr = ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, uint, int>)Slot(init, 3))(init, stream, 0 /* STGM_READ */);
                Assert.AreEqual(Native.S_OK, hr, $"Initialize returned 0x{hr:X8}.");

                // IThumbnailProvider: 3 GetThumbnail(UINT cx, HBITMAP*, WTS_ALPHATYPE*).
                uint alpha;
                hr = ((delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr*, uint*, int>)Slot(thumb, 3))(thumb, 4, &hbmp, &alpha);
                Assert.AreEqual(Native.S_OK, hr, $"GetThumbnail returned 0x{hr:X8}.");
                Assert.AreEqual((uint)WtsAlphaType.Argb, alpha);

                Dib dib = Dib.Read(hbmp);
                Assert.IsTrue(dib.IsDibSection);
                Assert.AreEqual(32, dib.BitsPerPixel);
                Assert.AreEqual(4, dib.Width);
                Assert.AreEqual(4, dib.Height);
                ThumbnailProviderTests.AssertPixels(Samples.Color4x4Rgba, dib);
            }
            finally
            {
                if (hbmp != IntPtr.Zero) Native.DeleteObject(hbmp);
                Release(stream);
                Release(thumb);
                Release(init);
                Release(factory);
                // The comhost stays loaded: the runtime it started into this process can't be unloaded.
            }
        }
    }
}
