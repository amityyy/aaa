using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CloudMine.Core.Telemetry;
using Microsoft.CloudMine.Core.Utility;
using Microsoft.CloudMine.SourceCode.Collectors.Core.GitModel.Authentication;
using Microsoft.CloudMine.SourceCode.Collectors.Core.GitModel.Builders;
using Microsoft.CloudMine.SourceCode.Collectors.Core.GitModel.Exceptions;
using Microsoft.CloudMine.SourceCode.Collectors.Core.Settings;
using Constants = Microsoft.CloudMine.Core.Collectors.Model.Constants;

namespace Microsoft.CloudMine.SourceCode.Collectors.Core.GitModel
{
    public class GitClient : IGitClient
    {
        // For formatting details, see: https://git-scm.com/docs/pretty-formats
        internal const string GitEndTag = "#ENDGITPARSING#";
        private const string GitShowFormatString = $"%H%n%T%n%P%n%an%n%ae%n%at%n%cn%n%Ce%n%c%n%s%n%b%n{GitEndTag}";
        private const string GitLsTreeFormatString = "%(objectmode) %(objecttype) %(objectname) %(objectsize) %(path)";
        private const string GitCommitLiteString = "gH %P";
        private const string GitTagFormatString = $"%(objectname)%0a%(refname:short)%0a%(creatordate)%0a%(taggeremail)%0a%(subject)%0a{GitEndTag}";

        private readonly IGitAuthentication authentication;
        private readonly ITelemetryClient telemetryClient;
        private readonly ProcessRunner processRunner;

        public string? WorkingDirectory { get; set; }

        public GitClient(IGitAuthentication authentication, ITelemetryClient telemetryClient, string? workingDirectory = null)
        {
            this.telemetryClient = telemetryClient;
            this.authentication = authentication;
            this.processRunner = new ProcessRunner();
            this.WorkingDirectory = workingDirectory;
        }

        public async Task CloneAsync(string location, string url)
        {
            IEnumerable<string> auth = await this.authentication.GetAuthenticatedArgs().ConfigureAwait(false);
            List<string> args = new List<string> { "clone", url, location };
            ReportGitCommand(args);

            IProcess process = processRunner.Create("git");
            ProcessResult result = await processRunner.ExecuteAsync(process, auth.Concat(args)).ConfigureAwait(false);

            bool success = result.ExitCode == 0;
            this.telemetryClient.TrackEvent("GitCloneResult", new Dictionary<string, string>
            {
                { "ExitCode", result.ExitCode.ToString() },
                { "StdOut", result.Stdout },
                { "StdErr", result.StdErr },
                { "Success", success.ToString() },
                { "Url", url },
                { "Location", location }
            });

            if (result.StdErr.Contains("TF401019"))
            {
                throw new RepositoryNotFoundException();
            }

            if (!success)
            {
                throw new InvalidOperationException($"Failed to clone from {url} to {location}. Exit code: {result.ExitCode}. StdErr: {result.StdErr}");
            }
        }

        public async Task StreamObjectsAsync(string refName, Func<GitObject, Task<bool>> objectConsumer, CancellationToken cancellationToken = default)
        {
            List<string> args = new List<string> { "ls-tree", "-r", refName };
            ReportGitCommand(args);

            IProcess process = processRunner.Create("git", WorkingDirectory);
            Task<bool> stdOutConsumer(string? line)
            {
                if (TryParseTreeObject(line, out GitObject? gitObject))
                {
                    return objectConsumer(gitObject);
                }

                return Task.FromResult(false);
            }

            Task<bool> stdErrConsumer(string? line)
            {
                ReportErrorLine(line, args);
                return Task.FromResult(true);
            }

            int exitCode = await processRunner.ExecuteStreamedAsync(process, args, stdOutConsumer, stdErrConsumer, cancellationToken).ConfigureAwait(false);

            if (exitCode != ExitCodes.Okay)
            {
                throw new InvalidOperationException($"Failed to execute git command with args: '{string.Join(", ", args)}'. Exit code: {exitCode}");
            }
        }

        internal static bool TryParseTreeObject(string? line, [NotNullWhen(true)] out GitObject? gitObject)
        {
            gitObject = null;

            if (string.IsNullOrWhiteSpace(line))
            {
                return false;
            }

            // From the official documentation: https://git-scm.com/docs/git-ls-tree
            // %(objectmode) %(objecttype) %(objectname)%x09%(path)
            // %x09 is the character code for tab.
            string[] restAndPath = line.Split('\t', 2);
            if (restAndPath.Length != 2)
            {
                throw new Exception($"Unexpected git tree object line: {line}");
            }

            string path = restAndPath[1];
            string[] parts = restAndPath[0].Split(' ', 3);
            if (parts.Length != 3)
            {
                throw new Exception($"Unexpected git tree object line: {line}");
            }

            string type = parts[1];
            string objectId = parts[2];

            gitObject = new GitObject
            {
                Id = objectId,
                Type = type,
                Path = path
            };

            return true;
        }
        
    }
}