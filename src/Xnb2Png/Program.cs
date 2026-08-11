using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using XnbCore;

namespace Xnb2Png
{
    internal static class Program
    {
        private const string Usage =
@"xnb2png - extract the Texture2D from an XNB file as a PNG

  xnb2png <file.xnb> [more.xnb ...]   convert next to the source file
  xnb2png -o <out.png> <file.xnb>     convert to an explicit path
  xnb2png --dir <folder>              convert every .xnb under a folder
  xnb2png --info <file.xnb>           print header details, write nothing

Options
  --premultiplied   keep the pipeline's premultiplied alpha (default: undo it)
  --overwrite       replace existing .png files (default: skip)
  --quiet           suppress per-file output
  --gui-errors      report failures in a message box (used by the shell verb)";

        private static bool _quiet;
        private static bool _guiErrors;

        private static int Main(string[] args)
        {
            if (args.Length == 0 || args[0] == "-h" || args[0] == "--help")
            {
                Console.WriteLine(Usage);
                return args.Length == 0 ? 2 : 0;
            }

            bool straight = true, overwrite = false, info = false, dirMode = false;
            string explicitOut = null;
            List<string> inputs = new List<string>();

            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                switch (a)
                {
                    case "--premultiplied": straight = false; break;
                    case "--overwrite": overwrite = true; break;
                    case "--quiet": _quiet = true; break;
                    case "--gui-errors": _guiErrors = true; break;
                    case "--info": info = true; break;
                    case "--dir": dirMode = true; break;
                    case "-o":
                        if (++i >= args.Length) return Fail("-o needs a path.");
                        explicitOut = args[i];
                        break;
                    default:
                        if (a.StartsWith("-")) return Fail("Unknown option: " + a);
                        inputs.Add(a);
                        break;
                }
            }

            if (inputs.Count == 0) return Fail("No input given.");

            List<string> files = new List<string>();
            if (dirMode)
            {
                foreach (string dir in inputs)
                {
                    if (!Directory.Exists(dir)) return Fail("Not a folder: " + dir);
                    files.AddRange(Directory.EnumerateFiles(dir, "*.xnb", SearchOption.AllDirectories));
                }
                if (files.Count == 0) return Fail("No .xnb files found.");
            }
            else
            {
                files.AddRange(inputs);
            }

            if (explicitOut != null && files.Count != 1)
                return Fail("-o only works with a single input file.");

            int ok = 0, skipped = 0;
            List<string> failures = new List<string>();

            foreach (string file in files)
            {
                try
                {
                    if (info)
                    {
                        PrintInfo(file);
                        ok++;
                        continue;
                    }

                    string outPath = explicitOut ?? Path.ChangeExtension(file, ".png");
                    if (!overwrite && File.Exists(outPath))
                    {
                        skipped++;
                        Log("skip  " + outPath + " (already exists)");
                        continue;
                    }

                    XnbImage img = XnbImage.Load(file);
                    byte[] pixels = straight ? img.ToStraightRgba() : img.PremultipliedRgba;
                    PngWriter.Write(outPath, img.Width, img.Height, pixels);

                    ok++;
                    Log(string.Format("ok    {0}  ({1}x{2}, {3}, {4})",
                        outPath, img.Width, img.Height, img.Format, img.Compression));
                }
                catch (Exception ex)
                {
                    failures.Add(Path.GetFileName(file) + ": " + ex.Message);
                    Log("FAIL  " + file + ": " + ex.Message);
                }
            }

            if (files.Count > 1)
                Log(string.Format("--- {0} converted, {1} skipped, {2} failed", ok, skipped, failures.Count));

            if (failures.Count > 0)
            {
                Report(string.Join(Environment.NewLine, failures.ToArray()));
                return 1;
            }
            return 0;
        }

        private static void PrintInfo(string file)
        {
            XnbImage img = XnbImage.Load(file);
            Console.WriteLine(file);
            Console.WriteLine("  platform     {0}", img.Platform);
            Console.WriteLine("  version      {0}{1}", img.Version, img.HiDef ? " (HiDef)" : " (Reach)");
            Console.WriteLine("  compression  {0}", img.Compression);
            Console.WriteLine("  format       {0}", img.Format);
            Console.WriteLine("  size         {0}x{1}", img.Width, img.Height);
            Console.WriteLine("  mip levels   {0}", img.MipLevels);
        }

        private static void Log(string message)
        {
            if (!_quiet) Console.WriteLine(message);
        }

        private static int Fail(string message)
        {
            Console.Error.WriteLine(message);
            Report(message);
            return 2;
        }

        private static void Report(string message)
        {
            if (!_guiErrors || !RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;
            try { MessageBoxW(IntPtr.Zero, message, "xnb2png", 0x10 /* MB_ICONERROR */); }
            catch { /* console-only build or non-Windows host */ }
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);
    }
}
