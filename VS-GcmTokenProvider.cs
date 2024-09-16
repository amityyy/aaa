using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell.Interop;

namespace Microsoft.VisualStudio.Debugger.Utilities.SourceLink
{
    internal class GcmTokenProvider : IAccessTokenProvider
    {
        private static string _gcmExePath;

        internal static string GcmExePath
        {
            get
            {
                if (_gcmExePath == null)
                {
                    // TODO: Currently this is hardcoded against the install root of VS, this should be driven off of a
                    // registry key provided by the Team Explorer team.
                    IVsShell shell = (IVsShell)ServiceProvider.GlobalProvider.GetService(typeof(SVsShell));
                    if (shell != null && shell.GetProperty((int)VSSPROPID.VSSPROPID_InstallDirectory, out object installDirObj) == 0 && installDirObj is string installDir)
                    {
                        string gcmCandidateDir = Path.Combine(installDir, "CommonExtensions", "Microsoft", "TeamFoundation", "Team Explorer", "Git", "mingw32", "Libexec", "git-core", "git-credential-manager.exe");
                        if (File.Exists(gcmCandidateDir))
                        {
                            _gcmExePath = gcmCandidateDir;
                        }
                    }
                }

                return _gcmExePath;
            }
        }

        public async Task<IAccessTokenResult> GetTokenAsync(Uri uri, bool isInteractive, CancellationToken cancellationToken)
        {
            Uri gcmTargetUrl = new Uri("https://github.com");
            if (uri.DnsSafeHost.EndsWith("raw.githubusercontent.com"))
                return null;

            string gcmExePath = await GetGcmExePath();
            if (string.IsNullOrWhiteSpace(gcmExePath) || !File.Exists(gcmExePath))
                return new ErrorAccessTokenResult("git-credential-manager.exe could not be found");

            bool succeeded = false;
            string username = null;
            string password = null;
            string error = null;

            await Task.Run(() =>
            {
                succeeded = InvokeGcm(gcmExePath, gcmTargetUrl, isInteractive, out username, out password, out error);
            }, cancellationToken);

            if (succeeded)
            {
                return new BasicAccessTokenResult(username, password);
            }
            else
            {
                return new ErrorAccessTokenResult(error);
            }
        }

        private bool InvokeGcm(string exePath, Uri uri, bool isInteractive, out string username, out string password, out string error)
        {
            username = null;
            password = null;
            error = null;

            try
            {
                ProcessStartInfo startInfo = new ProcessStartInfo(exePath, "get")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true
                };

                // TODO: This environment setting can be cleaned up
                string envIsInteractive = isInteractive ? "true" : "false";
                startInfo.EnvironmentVariables.Add("GCM_INTERACTIVE", envIsInteractive);

                Process gcmProcess = Process.Start(startInfo);
                gcmProcess.StandardInput.WriteLine($"protocol={uri.Scheme}");
                gcmProcess.StandardInput.WriteLine($"host={uri.Host}");

                // if not interactive, allow a 15-second timeout for GCM
                // if it is interactive, it will show UI, so use a huge timeout (24 hours)
                int timeout = isInteractive ? 24 * 60 * 60 : 15;
                gcmProcess.WaitForExit(timeout * 1000);

                if (!gcmProcess.HasExited)
                {
                    error = "GCM timeout";
                    gcmProcess.Kill();
                    return false;
                }
                else if (gcmProcess.ExitCode == 0)
                {
                    IReadOnlyDictionary<string, string> outputDictionary = ParseSuccessfulGcmOutput(gcmProcess.StandardOutput.ReadToEnd());
                    outputDictionary.TryGetValue("username", out username);
                    outputDictionary.TryGetValue("password", out password);

                    if (username == null || password == null)
                    {
                        // TODO: error string improvement
                        error = "Failed to get username and password";
                        return false;
                    }

                    return true;
                }
                else
                {
                    // TODO: error string improvement
                    error = $"GCM failed with exit code {gcmProcess.ExitCode}";
                    return false;
                }
            }
            catch (Exception e)
            {
                // TODO: error string improvement
                error = $"Unexpected error invoking GCM: {e.Message}";
                return false;
            }
        }

        private IReadOnlyDictionary<string, string> ParseSuccessfulGcmOutput(string output)
        {
            // Example GCM output:
            // protocol=https
            // host=github.com
            // path=
            // username=Personal Access Token
            // password=<PAT>
            Dictionary<string, string> outputDictionary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            using (StringReader reader = new StringReader(output))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    string[] tokens = line.Split('=');
                    string key = string.Empty;
                    string value = string.Empty;

                    if (tokens.Length >= 1)
                        key = tokens[0];

                    if (tokens.Length >= 2)
                        value = tokens[1];

                    outputDictionary[key] = value;
                }
            }

            return new ReadOnlyDictionary<string, string>(outputDictionary);
        }

        private async Task<string> GetGcmExePath()
        {
            if (_gcmExePath == null)
            {
                // TODO: Currently this is hardcoded against the install root of VS, this should be driven off of a
                // registry key provided by the Team Explorer team.
                IVsShell shell = await ServiceProvider.GetGlobalServiceAsync(typeof(SVsShell), true) as IVsShell;
                if (shell != null && shell.GetProperty((int)VSSPROPID.VSSPROPID_InstallDirectory, out object installDirObj) == 0 && installDirObj is string installDir)
                {
                    string gcmCandidateDir = Path.Combine(installDir, "CommonExtensions", "Microsoft", "TeamFoundation", "Team Explorer", "Git", "mingw32", "Libexec", "git-core", "git-credential-manager.exe");
                    if (File.Exists(gcmCandidateDir))
                    {
                        _gcmExePath = gcmCandidateDir;
                    }
                }
            }

            return _gcmExePath;
        }
    }
}
