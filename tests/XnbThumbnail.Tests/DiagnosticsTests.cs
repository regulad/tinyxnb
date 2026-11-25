using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tinyxnb;

namespace XnbThumbnail.Tests
{
    /// <summary>
    /// The handler's diagnostics (src/Diagnostics, PRIVACY.md), all offline: a build without the
    /// DSN, as every build but a tagged release is, starts nothing and still makes thumbnails;
    /// DO_NOT_TRACK turns diagnostics off; and what a report could carry is scrubbed.
    /// </summary>
    [TestClass]
    public sealed class DiagnosticsTests
    {
        private const string SomeDsn = "https://public@o0.ingest.sentry.io/0";

        [TestMethod]
        public void TestBuildsHaveNoDsn()
        {
            Assert.IsTrue(string.IsNullOrEmpty(SentryDiagnostics.Dsn),
                "Only release.yml's tagged build may compile the DSN in (-p:TinyxnbRelease=true).");
        }

        [TestMethod]
        public void ThumbnailsWorkWithoutSentry()
        {
            IntPtr hbmp;
            WtsAlphaType alpha;
            int hr = ThumbnailProviderTests.Thumbnail(Samples.Color4x4Xnb(), 4, out hbmp, out alpha);
            try
            {
                Assert.AreEqual(Native.S_OK, hr, $"GetThumbnail returned 0x{hr:X8}.");
            }
            finally
            {
                Native.DeleteObject(hbmp);
            }

            Assert.IsFalse(SentryDiagnostics.Started, "A build without the DSN must not start Sentry.");
            Assert.IsNull(SentryDiagnostics.Start());
        }

        [TestMethod]
        public void DoNotTrackTurnsDiagnosticsOff()
        {
            Assert.IsTrue(SentryDiagnostics.ShouldStart(SomeDsn, null));
            Assert.IsTrue(SentryDiagnostics.ShouldStart(SomeDsn, ""));

            // Empty, 0 and false, in any case and trimmed, leave diagnostics on, as Warlock reads them.
            foreach (string value in new[] { "0", "false", "FALSE", " 0 ", "  " })
                Assert.IsTrue(SentryDiagnostics.ShouldStart(SomeDsn, value), $"DO_NOT_TRACK={value}");

            // Anything else turns them off.
            foreach (string value in new[] { "1", "true", "yes", " 1 " })
                Assert.IsFalse(SentryDiagnostics.ShouldStart(SomeDsn, value), $"DO_NOT_TRACK={value}");

            Assert.IsFalse(SentryDiagnostics.ShouldStart("", null), "No DSN, nothing to start.");
            Assert.IsFalse(SentryDiagnostics.ShouldStart(null, null), "No DSN, nothing to start.");
        }

        [TestMethod]
        public void StartReadsDoNotTrack()
        {
            string before = Environment.GetEnvironmentVariable("DO_NOT_TRACK");
            Environment.SetEnvironmentVariable("DO_NOT_TRACK", "1");
            try
            {
                Assert.IsNull(SentryDiagnostics.Start());
                Assert.IsFalse(SentryDiagnostics.Started);
            }
            finally
            {
                Environment.SetEnvironmentVariable("DO_NOT_TRACK", before);
            }
        }

        [TestMethod]
        public void RedactRemovesPaths()
        {
            Assert.AreEqual("Could not find file '<path>'.",
                SentryDiagnostics.Redact(@"Could not find file 'C:\Users\someone\Game\Content\a b.xnb'."));
            Assert.AreEqual("Access to the path '<path>' is denied.",
                SentryDiagnostics.Redact(@"Access to the path '\\server\share\a.xnb' is denied."));
            Assert.AreEqual("Could not find a part of the path '<path>'.",
                SentryDiagnostics.Redact("Could not find a part of the path 'D:/games/x.xnb'."));
        }

        [TestMethod]
        public void RedactKeepsWhatIdentifiesNoOne()
        {
            const string message = "Unsupported surface format: 23.";
            Assert.AreEqual(message, SentryDiagnostics.Redact(message));
            Assert.IsNull(SentryDiagnostics.Redact(null));
        }

        [TestMethod]
        public void RedactRemovesThisUsersAndComputersNames()
        {
            string user = Environment.GetEnvironmentVariable("USERNAME");
            string host = Environment.GetEnvironmentVariable("COMPUTERNAME");
            if (string.IsNullOrEmpty(user) || user.Length < 2 || string.IsNullOrEmpty(host) || host.Length < 2)
                Assert.Inconclusive("USERNAME or COMPUTERNAME is unset or too short to redact.");

            string redacted = SentryDiagnostics.Redact($"{user.ToUpperInvariant()} on {host.ToLowerInvariant()}");
            Assert.IsFalse(redacted.Contains(user, StringComparison.OrdinalIgnoreCase), redacted);
            Assert.IsFalse(redacted.Contains(host, StringComparison.OrdinalIgnoreCase), redacted);
            Assert.IsTrue(redacted.Contains("<host>", StringComparison.Ordinal), redacted);
        }

        [TestMethod]
        public void RunIdIsRandomAndAlone()
        {
            Guid id;
            Assert.IsTrue(Guid.TryParseExact(SentryDiagnostics.RunId, "N", out id));
            Assert.AreNotEqual(Guid.Empty, id);
            Assert.IsTrue(SentryDiagnostics.Release.StartsWith("tinyxnb@", StringComparison.Ordinal));
        }
    }
}
