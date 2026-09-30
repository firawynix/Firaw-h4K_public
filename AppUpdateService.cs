using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace FirawH4K;

/// <summary>
/// Opt-in desktop updater. Call with silent:true once at startup, while idle;
/// silent:false is a user-initiated tray check. Even startup asks before networking.
/// exitForUpdate must terminate the application, not hide its main window.
/// No timers, startup registrations, automatic installs, or automatic relaunches.
/// </summary>
internal sealed class AppUpdateService
{
    private const string Feed = "https://h4k.firawynix.com.br/downloads/release.json";
    private const string Signer = "668BEF56480FCA8EB1B0BC3B102A6A9CEC554CC4";
    private const long MaxInstallerBytes = 400L * 1024 * 1024;
    private static int checking;
    private static int startupAsked;

    public async Task CheckAsync(Window owner, bool silent, Func<bool> isBusy, Action exitForUpdate)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(isBusy);
        ArgumentNullException.ThrowIfNull(exitForUpdate);
        if (!owner.Dispatcher.CheckAccess())
        {
            await owner.Dispatcher.InvokeAsync(() => CheckAsync(owner, silent, isBusy, exitForUpdate)).Task.Unwrap();
            return;
        }
        if (Interlocked.CompareExchange(ref checking, 1, 0) != 0) return;
        string? staging = null;
        Process? helper = null;
        bool handedOff = false;
        bool downloadApproved = false;
        try
        {
            if (isBusy())
            {
                if (!silent) Tell(owner, "Pare a atividade do Firaw antes de procurar atualizações.");
                return;
            }
            if (silent && Interlocked.Exchange(ref startupAsked, 1) != 0) return;

            // Any package identity (including an unexpected API result) disables self-update.
            uint length = 0;
            if (GetCurrentPackageFullName(ref length, IntPtr.Zero) != 15700)
            {
                if (Ask(owner, "Esta edição recebe atualizações pela Microsoft Store. Abrir a Store?"))
                    Process.Start(new ProcessStartInfo("ms-windows-store://downloadsandupdates") { UseShellExecute = true });
                return;
            }
            if (silent && !Ask(owner, "Deseja verificar agora se há uma atualização do Firaw?")) return;
            if (isBusy()) return;

            using var handler = new HttpClientHandler
            {
                AllowAutoRedirect = false,
                AutomaticDecompression = DecompressionMethods.None,
                UseCookies = false,
                UseDefaultCredentials = false
            };
            using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Firaw-h4K-Updater/1.0");
            Release release;
            using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
                release = await ReadReleaseAsync(client, timeout.Token);
            var current = Normalize(Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(1, 0, 0, 0));
            if (release.Version <= current)
            {
                if (!silent && !isBusy()) Tell(owner, "O Firaw já está atualizado.");
                return;
            }
            if (isBusy() || !Ask(owner, $"A versão {release.Version} está disponível. Baixar a atualização?")) return;
            downloadApproved = true;
            if (isBusy()) return;

            string installDirectory = SafeLocalDirectory(AppContext.BaseDirectory);
            staging = CreatePrivateStaging();
            string installer = Path.Combine(staging, "update.exe");
            using (var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(15)))
                await DownloadAsync(client, release, installer, timeout.Token);

            helper = StartHelper();
            // Fixed launcher code; every varying value travels as data over redirected stdin.
            using (var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2)))
            {
                await helper.StandardInput.WriteLineAsync(Convert.ToBase64String(Encoding.UTF8.GetBytes(HelperSource))).WaitAsync(timeout.Token);
                await helper.StandardInput.WriteLineAsync(Encode(installer)).WaitAsync(timeout.Token);
                await helper.StandardInput.WriteLineAsync(release.Sha256).WaitAsync(timeout.Token);
                await helper.StandardInput.WriteLineAsync(release.Size.ToString(CultureInfo.InvariantCulture)).WaitAsync(timeout.Token);
                await helper.StandardInput.WriteLineAsync(Environment.ProcessId.ToString(CultureInfo.InvariantCulture)).WaitAsync(timeout.Token);
                using var self = Process.GetCurrentProcess();
                await helper.StandardInput.WriteLineAsync(self.StartTime.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture)).WaitAsync(timeout.Token);
                await helper.StandardInput.WriteLineAsync(Encode(installDirectory)).WaitAsync(timeout.Token);
                await helper.StandardInput.FlushAsync(timeout.Token);
                if (await helper.StandardOutput.ReadLineAsync(timeout.Token) != "READY")
                    throw new InvalidDataException("Installer verification failed.");
            }
            if (isBusy() || !Ask(owner, "A atualização foi verificada. Fechar o Firaw e instalar agora?")) return;
            // No await between this last busy check, authorization and the exit callback.
            if (isBusy()) return;
            helper.StandardInput.WriteLine("INSTALL");
            helper.StandardInput.Flush();
            exitForUpdate();
            handedOff = true;
        }
        catch (Exception)
        {
            // Startup connectivity failures are quiet; a user-approved download is interactive.
            if ((!silent || downloadApproved) && !owner.Dispatcher.HasShutdownStarted)
                Tell(owner, "Não foi possível concluir a atualização com segurança. Tente novamente mais tarde.");
        }
        finally
        {
            if (!handedOff && helper != null)
            {
                try
                {
                    if (!helper.HasExited) helper.Kill();
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    await helper.WaitForExitAsync(timeout.Token);
                }
                catch { /* No authorization survives a live parent: helper times out closed. */ }
            }
            helper?.Dispose();
            if (!handedOff && staging != null)
            {
                try { File.Delete(Path.Combine(staging, "update.exe")); Directory.Delete(staging); }
                catch { /* Only this attempt's known file and empty directory may be removed. */ }
            }
            if (!handedOff) Volatile.Write(ref checking, 0);
        }
    }

    private sealed record Release(Version Version, Uri Url, long Size, string Sha256);

    private static async Task<Release> ReadReleaseAsync(HttpClient client, CancellationToken cancellation)
    {
        using var response = await GetAsync(client, new Uri(Feed), cancellation);
        const int maxManifest = 64 * 1024;
        if (response.Content.Headers.ContentLength > maxManifest) throw new InvalidDataException();
        await using var input = await response.Content.ReadAsStreamAsync(cancellation);
        using var bytes = new MemoryStream();
        var buffer = new byte[8192];
        int count;
        while ((count = await input.ReadAsync(buffer, cancellation)) != 0)
        {
            if (bytes.Length + count > maxManifest) throw new InvalidDataException();
            bytes.Write(buffer, 0, count);
        }
        using var document = JsonDocument.Parse(bytes.ToArray(), new JsonDocumentOptions { MaxDepth = 8 });
        var root = document.RootElement;
        RejectDuplicates(root);
        string versionText = root.GetProperty("version").GetString() ?? "";
        if (versionText.Length > 40 || !versionText.All(c => char.IsAsciiDigit(c) || c == '.') ||
            !Version.TryParse(versionText, out var version)) throw new InvalidDataException();
        string architecture = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.X86 => "x86",
            _ => throw new PlatformNotSupportedException()
        };
        var package = root.GetProperty("architectures").GetProperty(architecture);
        long size = package.GetProperty("size").GetInt64();
        string hash = package.GetProperty("sha256").GetString() ?? "";
        string pin = package.GetProperty("signerThumbprint").GetString() ?? "";
        if (size <= 0 || size > MaxInstallerBytes || hash.Length != 64 || !hash.All(Uri.IsHexDigit) ||
            !string.Equals(pin, Signer, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException();
        var url = AllowedUrl(package.GetProperty("url").GetString() ?? "");
        if (!url.AbsolutePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException();
        return new Release(Normalize(version), url, size, hash);
    }

    private static void RejectDuplicates(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) return;
        var names = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in element.EnumerateObject())
        {
            if (!names.Add(property.Name)) throw new InvalidDataException();
            RejectDuplicates(property.Value);
        }
    }

    private static Version Normalize(Version value) => new(value.Major, value.Minor, Math.Max(0, value.Build), Math.Max(0, value.Revision));

    private static Uri AllowedUrl(string value)
    {
        // Reject encoded separators/dot segments, credentials, alternate ports and URL ambiguities.
        if (value.Length > 2048 || value.Any(c => c <= ' ' || c >= 127 || c is '\\' or '%') ||
            !Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(uri.Host, "h4k.firawynix.com.br", StringComparison.OrdinalIgnoreCase) ||
            uri.Port != 443 || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0 ||
            !uri.AbsolutePath.StartsWith("/downloads/", StringComparison.Ordinal) ||
            value.Split('/').Any(part => part is "." or "..")) throw new InvalidDataException();
        return uri;
    }

    private static async Task<HttpResponseMessage> GetAsync(HttpClient client, Uri url, CancellationToken cancellation)
    {
        AllowedUrl(url.AbsoluteUri);
        var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellation);
        // Reject ALL redirects, including on-host ones, and partial/error responses.
        if (response.StatusCode != HttpStatusCode.OK || response.Content.Headers.ContentEncoding.Count != 0)
        {
            response.Dispose();
            throw new HttpRequestException("Unexpected update response.");
        }
        return response;
    }

    private static async Task DownloadAsync(HttpClient client, Release release, string path, CancellationToken cancellation)
    {
        using var response = await GetAsync(client, release.Url, cancellation);
        if (response.Content.Headers.ContentLength is long length && length != release.Size) throw new InvalidDataException();
        await using var input = await response.Content.ReadAsStreamAsync(cancellation);
        await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        long total = 0;
        int count;
        while ((count = await input.ReadAsync(buffer, cancellation)) != 0)
        {
            total += count;
            if (total > release.Size || total > MaxInstallerBytes) throw new InvalidDataException();
            hash.AppendData(buffer, 0, count);
            await output.WriteAsync(buffer.AsMemory(0, count), cancellation);
        }
        if (total != release.Size || !CryptographicOperations.FixedTimeEquals(hash.GetHashAndReset(), Convert.FromHexString(release.Sha256)))
            throw new InvalidDataException();
    }

    private static string SafeLocalDirectory(string path)
    {
        string full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
        if (full.Length < 4 || full.Length > 220 || full[1] != ':' || full[2] != '\\' ||
            !char.IsAsciiLetter(full[0]) || full.Any(c => c < ' ' || c == '"') || full[3..].Contains(':'))
            throw new InvalidDataException();
        for (DirectoryInfo? directory = new(full); directory != null; directory = directory.Parent)
            if (!directory.Exists || (directory.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException();
        return full;
    }

    private static string CreatePrivateStaging()
    {
        string parent = SafeLocalDirectory(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        string path = Path.Combine(parent, "Firaw-h4K-update-" + Guid.NewGuid().ToString("N"));
        using var identity = WindowsIdentity.GetCurrent();
        var user = identity.User ?? throw new InvalidOperationException();
        var security = new DirectorySecurity();
        security.SetOwner(user);
        security.SetAccessRuleProtection(true, false);
        foreach (var sid in new[] { user, new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null) })
            security.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        new DirectoryInfo(path).Create(security);
        return SafeLocalDirectory(path);
    }

    private static string Encode(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value));

    private static Process StartHelper()
    {
        string system = Environment.GetFolderPath(Environment.SpecialFolder.System);
        string powershell = Path.Combine(SafeLocalDirectory(system), "WindowsPowerShell", "v1.0", "powershell.exe");
        if (!File.Exists(powershell)) throw new FileNotFoundException();
        // No PATH lookup, downloaded helper, profile, script file, command interpolation or elevation.
        const string launcher = "$ErrorActionPreference='Stop'; try { $s=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String([Console]::ReadLine())); Add-Type -TypeDefinition $s -ErrorAction Stop; [FirawUpdateHelper]::Run() } catch { exit 1 }";
        var start = new ProcessStartInfo(powershell)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = system,
            RedirectStandardInput = true,
            RedirectStandardOutput = true
        };
        foreach (string argument in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(launcher)) })
            start.ArgumentList.Add(argument);
        return Process.Start(start) ?? throw new InvalidOperationException();
    }

    private static bool Ask(Window owner, string text) => System.Windows.MessageBox.Show(owner, text, "Atualização do Firaw",
        MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;

    private static void Tell(Window owner, string text) => System.Windows.MessageBox.Show(owner, text, "Atualização do Firaw",
        MessageBoxButton.OK, MessageBoxImage.Information);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int GetCurrentPackageFullName(ref uint packageFullNameLength, IntPtr packageFullName);

    // C# 5 compatible: executed by the OS-supplied Windows PowerShell, without extra dependencies.
    // WinVerifyTrust checks the PE digest AND the cryptographic signer. We never treat an
    // arbitrary PowerShell NotTrusted status, embedded certificate, or manifest pin as proof.
    private const string HelperSource = """
        using System;
        using System.Diagnostics;
        using System.Globalization;
        using System.IO;
        using System.Runtime.InteropServices;
        using System.Security.Cryptography;
        using System.Security.Cryptography.X509Certificates;
        using System.Text;

        public static class FirawUpdateHelper
        {
            const string Pin = "668BEF56480FCA8EB1B0BC3B102A6A9CEC554CC4";
            const int UntrustedRoot = unchecked((int)0x800B0109);

            public static void Run()
            {
                string path = Decode(Console.ReadLine());
                string hash = Console.ReadLine();
                long size = long.Parse(Console.ReadLine(), CultureInfo.InvariantCulture);
                int pid = int.Parse(Console.ReadLine(), CultureInfo.InvariantCulture);
                long started = long.Parse(Console.ReadLine(), CultureInfo.InvariantCulture);
                string destination = Decode(Console.ReadLine());
                ValidatePath(path, false);
                ValidatePath(destination, true);
                if (size <= 0 || size > 400L * 1024 * 1024 || hash.Length != 64) throw new InvalidDataException();
                // Retain a real process handle before READY: PID reuse cannot authorize installation.
                using (Process parent = Process.GetProcessById(pid))
                {
                    IntPtr handle = parent.Handle;
                    if (parent.StartTime.ToUniversalTime().Ticks != started || parent.HasExited) throw new InvalidDataException();
                    // Deny write/delete/replacement throughout verification, consent and installation.
                    using (FileStream file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        Verify(path, file, hash, size);
                        Console.WriteLine("READY");
                        Console.Out.Flush();
                        if (Console.ReadLine() != "INSTALL") return;
                        if (!parent.WaitForExit(120000)) return; // Timeout/error NEVER means the app exited.
                        ValidatePath(path, false);
                        ValidatePath(destination, true);
                        Verify(path, file, hash, size);
                        // NSIS requires /D= last and unquoted, even for a directory containing spaces.
                        // No command shell interprets this string. ValidatePath rejects quotes/controls.
                        ProcessStartInfo start = new ProcessStartInfo(path, "/S /D=" + destination);
                        start.UseShellExecute = false;
                        start.CreateNoWindow = true;
                        start.WindowStyle = ProcessWindowStyle.Hidden;
                        start.WorkingDirectory = Path.GetDirectoryName(path);
                        using (Process installer = Process.Start(start))
                        {
                            if (installer == null) throw new IOException();
                            installer.WaitForExit();
                        }
                    }
                }
                try { File.Delete(path); Directory.Delete(Path.GetDirectoryName(path)); } catch { }
            }

            static string Decode(string text) { return Encoding.UTF8.GetString(Convert.FromBase64String(text)); }

            static void ValidatePath(string path, bool directory)
            {
                if (path.Length < 4 || path.Length > 260 || path[1] != ':' || path[2] != '\\' ||
                    !char.IsLetter(path[0]) || path.Substring(3).Contains(":")) throw new InvalidDataException();
                foreach (char c in path) if (c < 32 || c == '"') throw new InvalidDataException();
                if (!string.Equals(Path.GetFullPath(path), path, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException();
                if (!directory && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException();
                for (DirectoryInfo d = new DirectoryInfo(directory ? path : Path.GetDirectoryName(path)); d != null; d = d.Parent)
                    if (!d.Exists || (d.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException();
            }

            static void Verify(string path, FileStream file, string expected, long size)
            {
                if (file.Length != size) throw new InvalidDataException();
                file.Position = 0;
                using (SHA256 sha = SHA256.Create())
                    if (!string.Equals(BitConverter.ToString(sha.ComputeHash(file)).Replace("-", ""), expected, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException();

                Guid action = new Guid("00AAC56B-CD44-11D0-8CC2-00C04FC295EE");
                FileInfoNative info = new FileInfoNative();
                info.Size = (uint)Marshal.SizeOf(typeof(FileInfoNative));
                info.Path = Marshal.StringToCoTaskMemUni(path);
                info.Handle = file.SafeFileHandle.DangerousGetHandle();
                IntPtr infoPointer = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(FileInfoNative)));
                Marshal.StructureToPtr(info, infoPointer, false);
                TrustData data = new TrustData();
                data.Size = (uint)Marshal.SizeOf(typeof(TrustData));
                data.UiChoice = 2;
                data.UnionChoice = 1;
                data.File = infoPointer;
                data.StateAction = 1;
                // Explicit private trust anchor has no CA revocation service; never fetch certificate URLs.
                data.Flags = 0x10 | 0x1000 | 0x2000;
                try
                {
                    int result = WinVerifyTrust(new IntPtr(-1), ref action, ref data);
                    // A narrow root exception, NOT TRUST_E_SUBJECT_NOT_TRUSTED or a broad NotTrusted status.
                    if (result != 0 && result != UntrustedRoot) throw new CryptographicException(result);
                    IntPtr provider = WTHelperProvDataFromStateData(data.State);
                    if (provider == IntPtr.Zero) throw new CryptographicException();
                    IntPtr signerPointer = WTHelperGetProvSignerFromChain(provider, 0, false, 0);
                    if (signerPointer == IntPtr.Zero) throw new CryptographicException();
                    ProviderSigner signer = (ProviderSigner)Marshal.PtrToStructure(signerPointer, typeof(ProviderSigner));
                    if (signer.CertCount != 1 || signer.Certs == IntPtr.Zero || signer.SignerInfo == IntPtr.Zero ||
                        (signer.Error != 0 && signer.Error != UntrustedRoot)) throw new CryptographicException();
                    ProviderCertificate entry = (ProviderCertificate)Marshal.PtrToStructure(signer.Certs, typeof(ProviderCertificate));
                    ProviderData providerData = (ProviderData)Marshal.PtrToStructure(provider, typeof(ProviderData));
                    VerifySignature signature = new VerifySignature();
                    signature.Size = (uint)Marshal.SizeOf(typeof(VerifySignature));
                    signature.SignerType = 2; // CMSG_VERIFY_SIGNER_CERT, primary signer index zero.
                    signature.Signer = entry.Certificate;
                    // Verify the cryptographic CMS signature independently of certificate trust policy.
                    // A forged/tampered signature cannot ride along with an untrusted-root exception.
                    if (entry.Certificate == IntPtr.Zero || providerData.Message == IntPtr.Zero ||
                        !CryptMsgControl(providerData.Message, 0, 19, ref signature)) throw new CryptographicException();
                    using (X509Certificate2 certificate = new X509Certificate2(entry.Certificate))
                    {
                        if (!string.Equals(certificate.Thumbprint, Pin, StringComparison.OrdinalIgnoreCase)) throw new CryptographicException();
                        // Only the pinned, currently valid self-signed code-signing leaf may be its own root.
                        using (X509Chain chain = new X509Chain())
                        {
                            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                            chain.ChainPolicy.VerificationFlags = X509VerificationFlags.AllowUnknownCertificateAuthority;
                            chain.ChainPolicy.ApplicationPolicy.Add(new Oid("1.3.6.1.5.5.7.3.3"));
                            if (!chain.Build(certificate) || chain.ChainElements.Count != 1) throw new CryptographicException();
                            foreach (X509ChainStatus status in chain.ChainStatus)
                                if ((status.Status & ~X509ChainStatusFlags.UntrustedRoot) != 0) throw new CryptographicException();
                        }
                    }
                    // Independently require the Windows PE digest check to succeed with no trust exception.
                    data.StateAction = 2;
                    WinVerifyTrust(new IntPtr(-1), ref action, ref data);
                    data.State = IntPtr.Zero;
                    data.StateAction = 1;
                    data.Flags |= 0x200; // WTD_HASH_ONLY_FLAG; full signature validation already ran above.
                    int digestResult = WinVerifyTrust(new IntPtr(-1), ref action, ref data);
                    if (digestResult != 0) throw new CryptographicException(digestResult);
                }
                finally
                {
                    data.StateAction = 2;
                    WinVerifyTrust(new IntPtr(-1), ref action, ref data);
                    Marshal.FreeHGlobal(infoPointer);
                    Marshal.FreeCoTaskMem(info.Path);
                }
            }

            [StructLayout(LayoutKind.Sequential)]
            struct FileInfoNative { public uint Size; public IntPtr Path, Handle, Subject; }
            [StructLayout(LayoutKind.Sequential)]
            struct TrustData
            {
                public uint Size;
                public IntPtr Policy, Sip;
                public uint UiChoice, Revocation, UnionChoice;
                public IntPtr File;
                public uint StateAction;
                public IntPtr State, Url;
                public uint Flags, UiContext;
                public IntPtr SignatureSettings;
            }
            [StructLayout(LayoutKind.Sequential)]
            struct ProviderSigner
            {
                public uint Size;
                public System.Runtime.InteropServices.ComTypes.FILETIME VerifyAsOf;
                public uint CertCount;
                public IntPtr Certs;
                public uint SignerType;
                public IntPtr SignerInfo;
                public int Error;
                public uint CounterSignerCount;
                public IntPtr CounterSigners, Chain;
            }
            // Only the prefix is read; native alignment includes padding before the certificate pointer.
            [StructLayout(LayoutKind.Sequential)]
            struct ProviderCertificate { public uint Size; public IntPtr Certificate; }
            [StructLayout(LayoutKind.Sequential)]
            struct ProviderData
            {
                public uint Size;
                public IntPtr Trust;
                public int Opened;
                public IntPtr Window, Action, Provider;
                public uint Error, SecuritySettings, PolicySettings;
                public IntPtr Functions;
                public uint ErrorCount;
                public IntPtr Errors;
                public uint StoreCount;
                public IntPtr Stores;
                public uint Encoding;
                public IntPtr Message;
            }
            [StructLayout(LayoutKind.Sequential)]
            struct VerifySignature
            {
                public uint Size;
                public IntPtr Provider;
                public uint SignerIndex, SignerType;
                public IntPtr Signer;
            }
            [DllImport("crypt32.dll", ExactSpelling = true, SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            static extern bool CryptMsgControl(IntPtr message, uint flags, uint control, ref VerifySignature parameters);
            [DllImport("wintrust.dll", ExactSpelling = true)]
            static extern int WinVerifyTrust(IntPtr window, ref Guid action, ref TrustData data);
            [DllImport("wintrust.dll", ExactSpelling = true)]
            static extern IntPtr WTHelperProvDataFromStateData(IntPtr state);
            [DllImport("wintrust.dll", ExactSpelling = true)]
            static extern IntPtr WTHelperGetProvSignerFromChain(IntPtr provider, uint signer, [MarshalAs(UnmanagedType.Bool)] bool counter, uint counterSigner);
        }
        """;
}
