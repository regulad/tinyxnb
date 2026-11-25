using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Win32;

namespace XnbThumbnail.Tests
{
    /// <summary>
    /// The real Explorer path: the Shell finds the handler through its registration, and its
    /// thumbnail cache runs it out of process in dllhost.exe. This needs the handler installed
    /// (install.ps1) first, so these tests are excluded from a plain run by category, and are
    /// inconclusive when the handler isn't registered (failed, with TINYXNB_REQUIRE_SHELL=1):
    /// <code>
    ///   dotnet test tests/XnbThumbnail.Tests -c Release --filter "TestCategory!=Shell"
    ///   ./install.ps1                    (or the staged package's)
    ///   dotnet test tests/XnbThumbnail.Tests -c Release --filter "TestCategory=Shell"
    ///   ./uninstall.ps1 -RemoveFiles
    /// </code>
    /// From an elevated process (High integrity), COM ignores per-user class registrations, so
    /// there the handler must be installed with <c>-Scope Machine</c>, as release.yml does.
    /// </summary>
    [STATestClass]
    public sealed class ShellTests
    {
        private const int Edge = 64;

        [TestMethod]
        [TestCategory("Shell")]
        public void ShellExtractsTheThumbnail()
        {
            string clsidKey = $@"CLSID\{{{XnbThumbnailProvider.ClsidString}}}\InprocServer32";
            using (RegistryKey key = Registry.ClassesRoot.OpenSubKey(clsidKey))
            {
                if (key == null)
                {
                    const string message = "The handler isn't registered; run install.ps1 first.";
                    // CI sets this, so that a registration COM can't see fails instead of skipping.
                    if (Environment.GetEnvironmentVariable("TINYXNB_REQUIRE_SHELL") == "1")
                        Assert.Fail(message);
                    Assert.Inconclusive(message);
                }
            }

            DirectoryInfo dir = Directory.CreateTempSubdirectory("tinyxnb-");
            try
            {
                string path = Path.Combine(dir.FullName, "quadrants.xnb");
                File.WriteAllBytes(path, Samples.QuadrantsXnb(Edge));

                Guid iid = Native.IID_IShellItemImageFactory;
                Native.IShellItemImageFactory factory;
                int hr = Native.SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out factory);
                Assert.AreEqual(Native.S_OK, hr, $"SHCreateItemFromParsingName returned 0x{hr:X8}.");

                IntPtr hbmp;
                try
                {
                    // THUMBNAILONLY: fail rather than fall back to the file type's icon, which is
                    // what a missing or broken handler would otherwise silently produce.
                    hr = factory.GetImage(new Native.SIZE { cx = Edge, cy = Edge }, Native.SIIGBF.ThumbnailOnly, out hbmp);
                }
                finally
                {
                    Marshal.ReleaseComObject(factory);
                }
                Assert.AreEqual(Native.S_OK, hr, $"IShellItemImageFactory.GetImage returned 0x{hr:X8}.");
                AssertRanOutOfProcess();

                try
                {
                    Dib dib = Dib.Read(hbmp);
                    Assert.AreEqual(32, dib.BitsPerPixel);
                    // The Shell may scale the handler's bitmap; it must stay square and fit.
                    Assert.AreEqual(dib.Width, dib.Height, $"Thumbnail is {dib.Width}x{dib.Height}.");
                    Assert.IsTrue(dib.Width > 0 && dib.Width <= Edge, $"Thumbnail is {dib.Width}x{dib.Height}.");

                    int q = dib.Width / 4;
                    foreach (bool top in new[] { true, false })
                    {
                        foreach (bool left in new[] { true, false })
                        {
                            int x = left ? q : 3 * q, y = top ? q : 3 * q;
                            var want = Samples.QuadrantColors[Samples.Quadrant(left, top)];
                            var got = dib.Pixel(x, y);
                            string at = $"({x}, {y}): want rgb{want}, got bgra{got}";
                            Assert.IsTrue(Math.Abs(got.R - want.R) <= 2, at);
                            Assert.IsTrue(Math.Abs(got.G - want.G) <= 2, at);
                            Assert.IsTrue(Math.Abs(got.B - want.B) <= 2, at);
                        }
                    }
                }
                finally
                {
                    Native.DeleteObject(hbmp);
                }
            }
            finally
            {
                dir.Delete(true);
            }
        }

        /// <summary>
        /// The thumbnail must have come from the handler in a dllhost.exe surrogate, as Explorer
        /// gets it, not from this process loading the comhost itself.
        /// </summary>
        private static void AssertRanOutOfProcess()
        {
            const string comhost = "XnbThumbnail.comhost.dll";

            foreach (ProcessModule m in Process.GetCurrentProcess().Modules)
                Assert.IsFalse(string.Equals(comhost, m.ModuleName, StringComparison.OrdinalIgnoreCase),
                               "The Shell loaded the handler into the test process.");

            foreach (Process p in Process.GetProcessesByName("dllhost"))
            {
                try
                {
                    foreach (ProcessModule m in p.Modules)
                    {
                        if (string.Equals(m.ModuleName, comhost, StringComparison.OrdinalIgnoreCase))
                            return;
                    }
                }
                catch (Exception e) when (e is Win32Exception || e is InvalidOperationException)
                {
                    // Another user's surrogate, or one that just exited.
                }
                finally
                {
                    p.Dispose();
                }
            }
            Assert.Fail($"No dllhost.exe has {comhost} loaded.");
        }
    }
}
