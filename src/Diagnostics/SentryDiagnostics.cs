using System;
using System.Reflection;
using System.Text.RegularExpressions;
using Sentry;
using Sentry.Protocol;

namespace Tinyxnb
{
    /// <summary>
    /// What the thumbnail handler and xnb2png share for their diagnostics, which PRIVACY.md
    /// describes: crash reports, one session per run for release health, and one transaction
    /// per thumbnail or conversion as the usage count. Compiled into both from
    /// Diagnostics.props; XnbCore stays free of NuGet packages.
    /// </summary>
    /// <remarks>
    /// Modelled on regulad/Warlock's <c>warlock-diagnostics</c>: the DSN is in the source, but
    /// only a release build (<c>-p:TinyxnbRelease=true</c>, which only release.yml passes, and
    /// only for a tag) compiles it in, so a build from source or for a test sends nothing.
    ///
    /// Nothing that identifies anyone is sent: no user, machine or file names, no paths of the
    /// user's files, no file contents and no IP address. A run is identified only by
    /// <see cref="RunId"/>, made at each start and never stored. Every capture sets it on the
    /// event itself, before the SDK's enricher runs, because the SDK otherwise falls back to an
    /// installation ID that it stores on disk or takes from a network adapter's MAC address
    /// (sentry-dotnet 6.12.1, <c>Enricher.Apply</c> and <c>InstallationIdHelper</c>).
    /// </remarks>
    internal static class SentryDiagnostics
    {
        /// <summary>tinyxnb's project's DSN in a release build, none (<c>""</c>) in any other.</summary>
        internal const string Dsn =
#if TINYXNB_RELEASE
            // Project regulad/tinyxnb. Public by nature: it lets a client send, nothing more.
            "https://5d1daead2ee49eb247de3373c17e78ff@o4506961510596608.ingest.us.sentry.io/4512494078328832";
#else
            "";
#endif

        /// <summary>
        /// The one ID of this process's run, random and never persisted, so that a crash and the
        /// thumbnails around it can be told apart from another run's, and nothing more.
        /// </summary>
        internal static readonly string RunId = Guid.NewGuid().ToString("N");

        /// <summary>The version release.yml built with (<c>-p:Version</c>), from Diagnostics.props.</summary>
        internal static string Version
        {
            get
            {
                foreach (AssemblyMetadataAttribute a in typeof(SentryDiagnostics).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>())
                    if (a.Key == "TinyxnbVersion") return a.Value;
                return "0.0.0";
            }
        }

        /// <summary><c>tinyxnb@&lt;version&gt;</c>, as release.yml names the release in Sentry.</summary>
        internal static string Release => "tinyxnb@" + Version;

        /// <summary>The environment every event and session names.</summary>
        internal const string EnvironmentName = "production";

        /// <summary>Whether Sentry is running in this process: started, and not turned off.</summary>
        internal static bool Started { get; private set; }

        /// <summary>
        /// Whether a <c>DO_NOT_TRACK</c> of <paramref name="value"/> asks for no tracking: set to
        /// anything but empty, <c>0</c> or <c>false</c> (in any case), once trimmed, as Warlock's
        /// <c>do_not_track</c> reads it.
        /// </summary>
        internal static bool DoNotTrack(string value)
        {
            string v = value?.Trim();
            return !string.IsNullOrEmpty(v) && v != "0" && !string.Equals(v, "false", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Whether to start Sentry, with this build's DSN and this <c>DO_NOT_TRACK</c>.</summary>
        internal static bool ShouldStart(string dsn, string doNotTrack) =>
            !string.IsNullOrEmpty(dsn) && !DoNotTrack(doNotTrack);

        /// <summary>
        /// Starts Sentry for this process, unless this build has no DSN or <c>DO_NOT_TRACK</c>
        /// is set. Returns what flushes and stops it when disposed, or null when it didn't start.
        /// Never throws: diagnostics must never break a thumbnail or a conversion.
        /// </summary>
        /// <remarks>
        /// Every integration that hooks the process is off: the handler runs inside
        /// <c>dllhost.exe</c>, a process tinyxnb doesn't own, so it captures only what its own
        /// code catches, and xnb2png catches its own failures in <c>Main</c> instead. The SDK's
        /// static hub belongs to the copy of Sentry.dll in tinyxnb's own load context: .NET's
        /// COM host loads each component into an isolated AssemblyLoadContext.
        /// </remarks>
        internal static IDisposable Start()
        {
            try
            {
                if (Started || !ShouldStart(Dsn, Environment.GetEnvironmentVariable("DO_NOT_TRACK")))
                    return null;

                SentryOptions options = new SentryOptions
                {
                    Dsn = Dsn,
                    Release = Release,
                    Environment = EnvironmentName,

                    // No PII: no user or machine name, and no IP address inferred from the request.
                    SendDefaultPii = false,
                    IsEnvironmentUser = false,
                    ServerName = "",

                    // Nothing on disk: no offline cache and no stored installation ID. The SDK's
                    // own sessions stay off, as they would name the installation ID, which falls
                    // back to a hash of the network adapters or the machine's name (sentry-dotnet
                    // 6.12.1, GlobalSessionManager.StartSession): StartSession sends this run's
                    // own, named by RunId.
                    CacheDirectoryPath = null,
                    DisableFileWrite = true,
                    AutoSessionTracking = false,

                    // Each capture carries its own scope; nothing relies on an ambient one.
                    IsGlobalModeEnabled = false,

                    // Every thumbnail and conversion is the usage count, so none is sampled out.
                    TracesSampleRate = 1.0,

                    // Neither program records breadcrumbs, and which other assemblies share the
                    // process is none of our business.
                    MaxBreadcrumbs = 0,
                    ReportAssembliesMode = ReportAssembliesMode.None,
                    DetectStartupTime = StartupTimeDetectionMode.None,
                };

                options.DisableAppDomainUnhandledExceptionCapture();
                options.DisableUnobservedTaskExceptionCapture();
                options.DisableAppDomainProcessExitFlush();
                options.DisableDiagnosticSourceIntegration();
                options.DisableWinUiUnhandledExceptionIntegration();
                options.DisableSystemDiagnosticsMetricsIntegration();

                options.SetBeforeBreadcrumb(_ => null);
                options.SetBeforeSend(Scrub);
                options.SetBeforeSendTransaction(Scrub);

                IDisposable sdk = SentrySdk.Init(options);
                Started = true;
                return sdk;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Starts a transaction, the usage count, or returns null when Sentry isn't running.
        /// </summary>
        internal static ITransactionTracer StartTransaction(string name, string operation)
        {
            if (!Started) return null;
            try
            {
                ITransactionTracer transaction = SentrySdk.StartTransaction(name, operation);
                transaction.User = new SentryUser { Id = RunId };
                return transaction;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Finishes <paramref name="transaction"/>, if any, with <paramref name="status"/>.</summary>
        internal static void Finish(ITransactionTracer transaction, SpanStatus status)
        {
            if (transaction == null) return;
            try { transaction.Finish(status); }
            catch { /* never let diagnostics fail the caller */ }
        }

        /// <summary>
        /// Reports <paramref name="exception"/>, if Sentry is running. <paramref name="handled"/>
        /// is false for one that ends the process.
        /// </summary>
        internal static void Capture(Exception exception, bool handled = true)
        {
            if (!Started) return;
            try
            {
                SentrySdk.CaptureException(exception, handled, !handled, scope => scope.User = new SentryUser { Id = RunId });
                UpdateSession(errored: true, endStatus: null);
            }
            catch
            {
                /* never let diagnostics fail the caller */
            }
        }

        private static readonly object SessionLock = new object();

        /// <summary>This run's session as last sent, or null before <see cref="StartSession"/>.</summary>
        private static SessionUpdate _session;

        /// <summary>
        /// Starts this run's session, once, if Sentry is running: what release health counts, as
        /// one healthy, errored or crashed run. It is named by <see cref="RunId"/> alone.
        /// </summary>
        internal static void StartSession()
        {
            if (!Started) return;
            try
            {
                lock (SessionLock)
                {
                    if (_session != null) return;
                    _session = FirstUpdate(DateTimeOffset.UtcNow);
                    SentrySdk.CaptureSession(_session);
                }
            }
            catch
            {
                /* never let diagnostics fail the caller */
            }
        }

        /// <summary>
        /// Ends this run's session with <paramref name="status"/>: <c>Exited</c> for a run that
        /// finished, <c>Crashed</c> for one ended by an exception. The thumbnail handler's never
        /// ends, as nothing tells it when dllhost.exe exits.
        /// </summary>
        internal static void EndSession(SessionEndStatus status) => UpdateSession(errored: false, endStatus: status);

        private static void UpdateSession(bool errored, SessionEndStatus? endStatus)
        {
            lock (SessionLock)
            {
                if (_session == null || _session.EndStatus != null) return;
                _session = NextUpdate(_session, errored, endStatus, DateTimeOffset.UtcNow);
                SentrySdk.CaptureSession(_session);
            }
        }

        /// <summary>A session's first update: new, named by <see cref="RunId"/>, and healthy.</summary>
        internal static SessionUpdate FirstUpdate(DateTimeOffset now) =>
            new SessionUpdate(SentryId.Create(), RunId, now, Release, EnvironmentName,
                ipAddress: null, userAgent: null, errorCount: 0, isInitial: true,
                timestamp: now, sequenceNumber: 0, endStatus: null);

        /// <summary>
        /// The update after <paramref name="previous"/>: one more error when
        /// <paramref name="errored"/>, and ended when <paramref name="endStatus"/> is set.
        /// </summary>
        internal static SessionUpdate NextUpdate(SessionUpdate previous, bool errored, SessionEndStatus? endStatus, DateTimeOffset now) =>
            new SessionUpdate(previous.Id, RunId, previous.StartTimestamp, Release, EnvironmentName,
                ipAddress: null, userAgent: null, errorCount: previous.ErrorCount + (errored ? 1 : 0),
                isInitial: false, timestamp: now, sequenceNumber: previous.SequenceNumber + 1, endStatus: endStatus);

        /// <summary>Waits up to <paramref name="timeout"/> for what's queued to be sent.</summary>
        internal static void Flush(TimeSpan timeout)
        {
            if (!Started) return;
            try { SentrySdk.Flush(timeout); }
            catch { /* never let diagnostics fail the caller */ }
        }

        /// <summary>
        /// The last word on each event: this run's ID as its only user field, no server name,
        /// messages without paths or names, and loaded modules by file name only (their paths
        /// can name the user's profile folder).
        /// </summary>
        private static SentryEvent Scrub(SentryEvent e)
        {
            e.User = new SentryUser { Id = RunId };
            e.ServerName = null;
            ScrubTimes(e.Contexts);

            if (e.Message != null)
            {
                e.Message.Message = Redact(e.Message.Message);
                e.Message.Formatted = Redact(e.Message.Formatted);
            }

            if (e.SentryExceptions != null)
                foreach (SentryException ex in e.SentryExceptions)
                    ex.Value = Redact(ex.Value);

            if (e.DebugImages != null)
                foreach (DebugImage image in e.DebugImages)
                    image.CodeFile = FileName(image.CodeFile);

            return e;
        }

        /// <summary>A transaction's last word: this run's ID as its only user field.</summary>
        private static SentryTransaction Scrub(SentryTransaction t)
        {
            t.User = new SentryUser { Id = RunId };
            ScrubTimes(t.Contexts);
            return t;
        }

        /// <summary>
        /// When this computer booted and this process started: together they could tie one
        /// run's reports to another's, which only <see cref="RunId"/> may do.
        /// </summary>
        private static void ScrubTimes(SentryContexts contexts)
        {
            if (contexts == null) return;
            contexts.Device.BootTime = null;
            contexts.App.StartTime = null;
        }

        /// <summary>
        /// Absolute Windows paths, drive-letter or UNC, up to the quote, angle bracket or line end
        /// that closes them, as .NET's I/O exceptions quote them.
        /// </summary>
        private static readonly Regex WindowsPath = new Regex(
            @"(?:\b[A-Za-z]:[\\/]|\\\\(?=[^\\\s]))[^'""\r\n<>|]*",
            RegexOptions.CultureInvariant);

        /// <summary>
        /// <paramref name="text"/> without what could identify anyone: absolute paths, and this
        /// user's and this computer's names.
        /// </summary>
        internal static string Redact(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;

            text = WindowsPath.Replace(text, "<path>");
            // The computer's name first: it often contains the user's.
            text = ReplaceName(text, Environment.GetEnvironmentVariable("COMPUTERNAME"), "<host>");
            text = ReplaceName(text, Environment.GetEnvironmentVariable("USERNAME"), "<user>");
            return text;
        }

        private static string ReplaceName(string text, string name, string with)
        {
            name = name?.Trim().TrimEnd('$');
            if (string.IsNullOrEmpty(name) || name.Length < 2) return text;
            return Regex.Replace(text, Regex.Escape(name), with, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        /// <summary>A path's last component.</summary>
        private static string FileName(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            int slash = path.LastIndexOfAny(new[] { '\\', '/' });
            return slash < 0 ? path : path.Substring(slash + 1);
        }
    }
}
