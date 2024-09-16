namespace DataStoreAccess.GitCore.Commands
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Security;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    using DataStoreAccess.Core.FileSearch;
    using DataStoreAccess.GitCore.GitUtilities;
    using DataStoreAccess.GitCore.Utilities;
    using DataStoreAccess.Security;
    using DataStoreAccess.Utility;

    using Microsoft;

    public class GitCommandHandler : IGitCommandHandler
    {
        public struct TreeObject
        {
            public string ObjectType;
            public string ObjectID;
            public string ObjectPath;
        }

        public struct CommitObject
        {
            public DateTime CommitDate;
            public string CommitMessage;
            public string CommitSHA;
        }

        private static readonly string gitPath = GitUtility.GetGitPath();
        private readonly IProcessExecutor processExecutor;

        public GitCommandHandler(IProcessExecutor processExecutor)
        {
            Requires.NotNull(processExecutor, nameof(processExecutor));
            this.processExecutor = processExecutor;
        }

        #region Public methods

        /// <summary>
        /// Stages changes
        /// </summary>
        /// <param name="currentWorkingDirectory">Current Working Directory</param>
        /// <param name="pathSpec">Path of dir that specifies list of files. If null, stage all changes.</param>
        /// <returns></returns>
        public async Task AddAsync(
            string currentWorkingDirectory,
            CancellationToken cancellationToken = default,
            string pathSpec = null)
        {
            pathSpec = pathSpec == null ? "-A" : AddSpaceCharProtection(pathSpec);

            await ExecuteNonQuery(
                currentWorkingDirectory,
                $"add {pathSpec}",
                cancellationToken,
                Constants.LongRunningTimeout).ConfigureAwait(false);
        }

        /// <summary>
        /// Clone a repo and checkout the specified branch
        /// </summary>
        /// <param name="currentWorkingDirectory"></param>
        /// <param name="url">Repo Url</param>
        /// <param name="branch">Branch to checkout after clone</param>
        /// <returns></returns>
        public async Task BranchClone(
            string currentWorkingDirectory,
            string branch,
            string url,
            CancellationToken cancellationToken = default)
        {
            Requires.NotNullOrWhiteSpace(currentWorkingDirectory, nameof(currentWorkingDirectory));
            Requires.NotNullOrWhiteSpace(branch, nameof(branch));
            Requires.NotNullOrWhiteSpace(url, nameof(url));

            var path = AddSpaceCharProtection(currentWorkingDirectory.Replace("\\", "/"));

            await ExecuteNonQuery(
                currentWorkingDirectory,
                $"clone -b {branch} {url} {path}",
                cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Clones a repo and specific branch to a given path with sparse checkout
        /// </summary>
        /// <param name="currentWorkingDirectory">Current Working Directory</param>
        /// <param name="branch">Branch to checkout</param>
        /// <param name="url">GitHub Url</param>
        /// <param name="path">path to clone to</param>
        /// <param name="matchPatterns">Match patterns for sparse checkout to use</param>
        /// <returns></returns>
        public async Task BranchCloneWithSparseCheckout(
            string currentWorkingDirectory,
            string branch,
            string url,
            string path,
            ICollection<MatchPattern> matchPatterns,
            SecureString personalAccessToken = default,
            CancellationToken cancellationToken = default)
        {
            Requires.NotNullOrWhiteSpace(currentWorkingDirectory, nameof(currentWorkingDirectory));
            Requires.NotNullOrWhiteSpace(branch, nameof(branch));
            Requires.NotNullOrWhiteSpace(url, nameof(url));
            Requires.NotNullEmptyOrNullElements(matchPatterns, nameof(matchPatterns));

            path = AddSpaceCharProtection(path.Replace("\\", "/"));

            await ExecuteNonQuery(
                currentWorkingDirectory,
                $"clone --no-checkout {url} {path}",
                cancellationToken,
                Constants.LongRunningTimeout).ConfigureAwait(false);

            await SetUpSparseCheckout(currentWorkingDirectory, matchPatterns, cancellationToken);

            await ExecuteNonQuery(currentWorkingDirectory, $"checkout {branch}", cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Checks out a branch with sparse checkout
        /// </summary>
        /// <param name="currentWorkingDirectory">Current working directory</param>
        /// <param name="create">Indicates if branch should be created</param>
        /// <param name="branch">Branch name to checkout.</param>
        /// <param name="orphan">Indicates if branch can be an orphan branch or not</param>
        /// <param name="matchPatterns">Match patterns for sparse checkout to use</param>
        /// <returns></returns>
        public async Task CheckoutBranchWithSparseCheckout(
            string currentWorkingDirectory,
            bool create,
            string branch,
            ICollection<MatchPattern> matchPatterns,
            CancellationToken cancellationToken = default,
            bool orphan = false)
        {
            Requires.NotNullEmptyOrNullElements(matchPatterns, nameof(matchPatterns));

            await SetUpSparseCheckout(currentWorkingDirectory, matchPatterns, cancellationToken);

            await ExecuteNonQuery(
                currentWorkingDirectory,
                $"checkout {(orphan ? "--orphan" : string.Empty) } {(create ? "-b" : string.Empty)} {branch}",
                cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Checks out a commit
        /// </summary>
        /// <param name="currentWorkingDirectory">Current working directory</param>
        /// <param name="commit">The commit SHA ID as a string</param>
        /// <returns>The <see cref="Task"/></returns>
        public async Task CheckoutCommit(
            string currentWorkingDirectory,
            string commit,
            CancellationToken cancellationToken = default)
        {
            Requires.NotNullOrWhiteSpace(commit, nameof(commit));

            await ExecuteNonQuery(currentWorkingDirectory, $"checkout {commit}", cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Checks out a commit with sparse checkout
        /// </summary>
        /// <param name="currentWorkingDirectory">Current working directory</param>
        /// <param name="commit">The commit SHA ID as a string</param>
        /// <param name="matchPatterns">Match patterns for sparse checkout to use</param>
        /// <returns>The <see cref="Task"/></returns>
        public async Task CheckoutCommitWithSparseCheckout(
            string currentWorkingDirectory,
            string commit,
            ICollection<MatchPattern> matchPatterns,
            CancellationToken cancellationToken = default)
        {
            Requires.NotNullOrWhiteSpace(commit, nameof(commit));
            Requires.NotNullEmptyOrNullElements(matchPatterns, nameof(matchPatterns));

            await SetUpSparseCheckout(currentWorkingDirectory, matchPatterns, cancellationToken).ConfigureAwait(false);

            await ExecuteNonQuery(currentWorkingDirectory, $"checkout {commit}", cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Force recursive clean of files in the working tree
        /// </summary>
        /// <param name="currentWorkingDirectory">Current Working Directory</param>
        /// <returns></returns>
        public async Task Clean(string currentWorkingDirectory, CancellationToken cancellationToken = default)
        {
            await ExecuteNonQuery(currentWorkingDirectory, $"rm -fr .", cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Clones a repo to a given path
        /// </summary>
        /// <param name="currentWorkingDirectory">Current Working Directory</param>
        /// <param name="url">GitHub Url</param>
        /// <param name="path">Path to clone to</param>
        /// <returns></returns>
        public async Task Clone(
            string currentWorkingDirectory,
            string url,
            string path,
            CancellationToken cancellationToken = default)
        {
            Requires.NotNullOrWhiteSpace(url, nameof(url));
            Requires.NotNullOrWhiteSpace(path, nameof(path));

            path = path.Replace("\\", "/");

            await ExecuteNonQuery(
                currentWorkingDirectory,
                $"clone {url} {path}",
                cancellationToken,
                Constants.LongRunningTimeout).ConfigureAwait(false);
        }

        /// <summary>
        /// Clones a repo to a given path with sparse checkout
        /// </summary>
        /// <param name="currentWorkingDirectory">Current Working Directory</param>
        /// <param name="url">GitHub Url</param>
        /// <param name="path">Path to clone to</param>
        /// <param name="matchPatterns">Match patterns for sparse checkout to use</param>
        /// <returns></returns>
        public async Task CloneWithSparseCheckout(
            string currentWorkingDirectory,
            string url,
            string path,
            ICollection<MatchPattern> matchPatterns,
            CancellationToken cancellationToken = default)
        {
            Requires.NotNullOrWhiteSpace(url, nameof(url));
            Requires.NotNullOrWhiteSpace(path, nameof(path));
            Requires.NotNullEmptyOrNullElements(matchPatterns, nameof(matchPatterns));

            path = AddSpaceCharProtection(path.Replace("\\", "/"));

            await ExecuteNonQuery(
                currentWorkingDirectory,
                $"clone --no-checkout {url} {path}",
                cancellationToken,
                Constants.LongRunningTimeout).ConfigureAwait(false);

            await SetUpSparseCheckout(currentWorkingDirectory, matchPatterns, cancellationToken);

            await ExecuteNonQuery(currentWorkingDirectory, $"checkout master", cancellationToken, Constants.LongRunningTimeout);
        }

        /// <summary>
        /// Commit changes
        /// </summary>
        /// <param name="currentWorkingDirectory">Current Working Directory</param>
        /// <param name="commitMessage">Commit Message</param>
        /// <param name="allowEmpty">Indicates if this commit can be empty or not</param>
        /// <returns></returns>
        public async Task Commit(
            string currentWorkingDirectory,
            string commitMessage,
            CancellationToken cancellationToken = default,
            bool allowEmpty = false)
        {
            Requires.NotNullOrWhiteSpace(commitMessage, nameof(commitMessage));

            await ExecuteNonQuery(
                currentWorkingDirectory,
                $"commit -m \"{commitMessage.Replace("\"", "")}\"" + (allowEmpty ? "--allow-empty" : String.Empty),
                cancellationToken,
                Constants.LongRunningTimeout).ConfigureAwait(false);
        }

        /// <summary>
        /// Deletes local branch
        /// </summary>
        /// <param name="currentWorkingDirectory">Current Working Directory</param>
        /// <param name="branchName">Branch to delete</param>
        /// <param name="hard">Indicates if delete is a hard delete</param>
        /// <returns></returns>
        public async Task DeleteLocalBranch(
            string currentWorkingDirectory, string branchName, CancellationToken cancellationToken = default, bool hard = false)
        {
            Requires.NotNullOrWhiteSpace(branchName, nameof(branchName));

            await ExecuteNonQuery(
                currentWorkingDirectory,
                $"branch {(hard ? "-D" : "-d")} {branchName}",
                cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Deletes remote branch.
        /// </summary>
        /// <param name="currentWorkingDirectory">Current Working Directory</param>
        /// <param name="branchName">Branch to delete</param>
        /// <returns></returns>
        public async Task DeleteRemoteBranch(
            string currentWorkingDirectory,
            string branchName,
            CancellationToken cancellationToken = default)
        {
            Requires.NotNullOrWhiteSpace(branchName, nameof(branchName));

            await ExecuteNonQuery(
                currentWorkingDirectory,
                $"push origin --delete {branchName}",
                cancellationToken);
        }

        /// <summary>
        /// Fetch branches and refs from origin or remote if specified
        /// </summary>
        /// <param name="currentWorkingDirectory">Current working directory</param>
        /// <param name="remote">Remote branch to fetch, origin if not specified</param>
        /// <returns></returns>
        public async Task Fetch(
            string currentWorkingDirectory,
            CancellationToken cancellationToken = default,
            string remote = null)
        {
            await ExecuteNonQuery(
                currentWorkingDirectory,
                $"fetch {remote ?? String.Empty}",
                cancellationToken,
                Constants.LongRunningTimeout).ConfigureAwait(false);
        }

        /// <summary>
        /// Get branch that points to current HEAD
        /// </summary>
        /// <param name="currentWorkingDirectory">Current Working Directory</param>
        /// <returns>Branch name</returns>
        public async Task<IReadOnlyList<string>> GetBranches(
            string currentWorkingDirectory,
            CancellationToken cancellationToken = default)
        {
            return await ExecuteQuery(
                currentWorkingDirectory,
                "branch",
                cancellationToken,
                s =>
                {
                    var result = new List<string>();
                    var lines = s.Split(Constants.NewLineSplitOptions, StringSplitOptions.RemoveEmptyEntries);
                    if (lines.Length <= 0) return result;
                    foreach (var line in lines)
                    {
                        var tabLines = line.Split(Constants.TabSplitOptions, StringSplitOptions.RemoveEmptyEntries);
                        //Current HEAD branch
                        var isHeadBranch = tabLines.Length == 2 && tabLines[0].Contains(@"*");
                        result.Add(isHeadBranch ? tabLines[1] : tabLines[0]);
                    };
                    return result;
                }).ConfigureAwait(false);
        }

        /// <summary>
        /// Get branch that points to current HEAD
        /// </summary>
        /// <param name="currentWorkingDirectory">Current Working Directory</param>
        /// <returns>Branch name</returns>
        public async Task<string> GetCurrentBranch(string currentWorkingDirectory, CancellationToken cancellationToken = default)
        {
            return await ExecuteQuery(
                currentWorkingDirectory,
                "branch",
                cancellationToken,
                s =>
                {
                    var lines = s.Split(Constants.NewLineSplitOptions, StringSplitOptions.RemoveEmptyEntries);

                    if (lines.Length <= 0)
                    {
                        return null;
                    }

                    foreach (var line in lines)
                    {
                        var tabLines = line.Split(Constants.TabSplitOptions);

                        if (tabLines.Length == 2 && tabLines[0].Contains(@"*"))
                        {
                            return tabLines[1];
                        }
                    };

                    return null;
                }).ConfigureAwait(false);
        }

        /// <summary>
        /// Get remote url
        /// </summary>
        /// <param name="currentWorkingDirectory">Current Working Directory</param>
        /// <returns></returns>
        public async Task<string> GetRemoteUrl(string currentWorkingDirectory, CancellationToken cancellationToken = default)
        {
            return await ExecuteQuery(
                currentWorkingDirectory,
                "remote get-url origin",
                cancellationToken,
                x => x).ConfigureAwait(false);
        }

        /// <summary>
        /// Indicates whether conversions from LF to CRLF should be performed while checking-out/commiting files
        /// </summary>
        /// <param name="currentWorkingDirectory">Current Working Directory</param>
        /// <param name="autoCRLF">Indicates whether this option should be turned on or not</param>
        /// <returns></returns>
        public async Task GitAutoCRLF(
            string currentWorkingDirectory,
            CancellationToken cancellationToken = default,
            bool autoCRLF = false)
        {
            await ExecuteNonQuery(
                currentWorkingDirectory,
                $"config --global core.autocrlf {autoCRLF}",
                cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Tells Git to detect renames
        /// </summary>
        /// <param name="currentWorkingDirectory">Current Working Directory</param>
        /// <param name="diffRenames">Indicates whether diff renames should be detected</param>
        /// <returns></returns>
        public async Task GitDiffRenames(
            string currentWorkingDirectory,
            CancellationToken cancellationToken = default,
            bool diffRenames = false)
        {
            await ExecuteNonQuery(
                currentWorkingDirectory,
                $"config --global diff.renames {diffRenames}",
                cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Display UTF8 characters in filenames
        /// </summary>
        /// <param name="currentWorkingDirectory">Current Working Directory</param>
        /// <param name="quotePath">Indicates whether quotepath should turned on or not</param>
        /// <returns></returns>
        public async Task GitQuotePath(
            string currentWorkingDirectory,
            CancellationToken cancellationToken = default,
            bool quotePath = false)
        {
            await ExecuteNonQuery(
                currentWorkingDirectory,
                $"config --global core.quotepath {quotePath}",
                cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Get the SHA1 hash for HEAD revision
        /// </summary>
        /// <param name="currentWorkingDirectory">Current Working Directory</param>
        /// <returns></returns>
        public async Task<string> HeadRevision(string currentWorkingDirectory, CancellationToken cancellationToken = default)
        {
            return await ExecuteQuery<string>(
                currentWorkingDirectory,
                "rev-parse HEAD",
                cancellationToken,
                s => s).ConfigureAwait(false);
        }

        /// <summary>
        /// Initialize a new repo with a commit
        /// </summary>
        /// <param name="currentWorkingDirectory">Current Working Directory</param>
        /// <param name="initCommitMessage">Optional init message parameter</param>
        /// <returns></returns>
        public async Task Init(string currentWorkingDirectory, CancellationToken cancellationToken = default)
        {
            await ExecuteNonQuery(currentWorkingDirectory, "init", cancellationToken);
            await ExecuteNonQuery(currentWorkingDirectory, @"commit -m ""Init Commit"" --allow-empty", cancellationToken);
        }

        /// <summary>
        /// Check if there are changes in the stage area
        /// </summary>
        /// <param name="currentWorkingDirectory">Current Working Directory</param>
        /// <returns></returns>
        public async Task<bool> IsStaged(string currentWorkingDirectory, CancellationToken cancellationToken = default)
        {
            return await ExecuteQuery(
                currentWorkingDirectory,
                "diff --cached --stat",
                cancellationToken,
                x =>
                {
                    var parsedOutput = x.Split(Constants.NewLineSplitOptions, StringSplitOptions.RemoveEmptyEntries);
                    return parsedOutput.Length > 0;
                }).ConfigureAwait(false);
        }

        /// <summary>
        /// Returns true if working directory is a valid git repository
        /// </summary>
        /// <param name="workingDirectory">The working directory path</param>
        /// <returns>true if working directory is a valid git repository</returns>
        public async Task<bool> IsValidGitRepository(string workingDirectory, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(workingDirectory))
            {
                return false;
            }

            try
            {
                return await ExecuteQuery(
                    workingDirectory,
                    "rev-parse --is-inside-work-tree",
                    cancellationToken,
                    x =>
                    {
                        var parsedOutput = x.Split(Constants.NewLineSplitOptions, StringSplitOptions.RemoveEmptyEntries);
                        return parsedOutput.Length > 0;
                    }).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                if (e.Message.Contains("not a git repository"))
                {
                    return false;
                }

                throw;
            }
        }

        /// <summary>
        /// Get a list of commits in the current working directory
        /// </summary>
        /// <param name="currentWorkingDirectory">Current Working Directory</param>
        /// <param name="count">Optional count of the lines to be read in the command output</param>
        /// <param name="filePath">Optional path of the file for which commits are retrieved</param>
        /// <param name="sinceBranch">Optional branch name to filter commits since head of this branch</param>
        /// <param name="untilBranch">Optional branch name to filter commits until head of this branch</param>
        /// <returns>The list of <see cref="CommitObject"/></returns>
        public async Task<IReadOnlyList<CommitObject>> ListCommits(
            string currentWorkingDirectory,
            CancellationToken cancellationToken = default,
            int? count = null,
            string filePath = null,
            string sinceBranch = null,
            string untilBranch = null)
        {
            Verify.Operation(
                !(sinceBranch == null ^ untilBranch == null),
                $"Please provide neither or both {nameof(sinceBranch)} and {nameof(untilBranch)}");

            var branchFilter = sinceBranch == null ? string.Empty : sinceBranch + ".." + untilBranch;
            var formatter = string.Join("|", "%H", "%an", "%ae", "%cn", "%ce", "%ci", "%s");
            var arguments = new StringBuilder();
            arguments.Append($@"--no-pager log {branchFilter} --format=""{formatter}""");

            if (count != null)
            {
                arguments.Append($"-{count}");
            }

            if (!string.IsNullOrEmpty(filePath))
            {
                arguments.Append($@" -- ""{filePath}""");
            }

            return await ExecuteQuery(
                currentWorkingDirectory,
                arguments.ToString(),
                cancellationToken,
                s =>
                {
                    var commitObjectsList = new List<CommitObject>();
                    var lines = s.Split(Constants.NewLineSplitOptions, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var line in lines)
                    {
                        var splitLine = line.Split('|');
                        if (splitLine.Length >= 7)
                        {
                            commitObjectsList.Add(new CommitObject
                            {
                                CommitDate = Convert.ToDateTime(splitLine[5]),
                                CommitMessage = splitLine.Length > 7 ? string.Join("|", splitLine.Skip(6)) : splitLine[6],
                                CommitSHA = splitLine[0]
                            });
                        }
                    }

                    return commitObjectsList;
                }).ConfigureAwait(false);
        }

        /// <summary>
        /// List deleted files path between two commits
        /// </summary>
        /// <param name="currentWorkingDirectory">Current Working Directory</param>
        /// <param name="startCommit">Start commit ID</param>
        /// <param name="endCommit">End commit ID</param>
        /// <returns></returns>
        public async Task<IReadOnlyList<string>> ListDeletedFiles(
            string currentWorkingDirectory,
            string startCommitId,
            CancellationToken cancellationToken = default,
            string endCommitId = null)
        {
            Requires.NotNullOrWhiteSpace(startCommitId, nameof(startCommitId));

            return await ExecuteQuery(
                currentWorkingDirectory,
                $"diff --name-status --diff-filter=D {startCommitId} {endCommitId ?? "HEAD"}",
                cancellationToken,
                s =>
                {
                    var result = new List<string>();
                    if (string.IsNullOrEmpty(s))
                    {
                        return result;
                    }

                    var deletedFilesWithState = s.Split(Constants.NewLineSplitOptions, StringSplitOptions.RemoveEmptyEntries);
                    if (!deletedFilesWithState.Any())
                    {
                        return result;
                    }

                    foreach (var deletedFileWithState in deletedFilesWithState.Where(c => !string.IsNullOrEmpty(c)))
                    {
                        var deletedFileParts = deletedFileWithState.Split(Constants.TabSplitOptions);
                        result.Add(deletedFileParts[1]);
                    }

                    return result;
                }).ConfigureAwait(false);
        }

        /// <summary>
        /// Return all files modified in a commit
        /// </summary>
        /// <param name="currentWorkingDirectory">Current Working Directory</param>
        /// <param name="commit">Commit ID</param>
        /// <returns></returns>
        public async Task<IReadOnlyList<string>> ListFilesInCommit(
            string currentWorkingDirectory,
            string commitId,
            CancellationToken cancellationToken = default)
        {
            Requires.NotNullOrWhiteSpace(commitId, nameof(commitId));

            return await ExecuteQuery(
                currentWorkingDirectory,
                $"diff-tree --no-commit-id --name-only -r {commitId}",
                cancellationToken,
                s => s.Split(Constants.NewLineSplitOptions, StringSplitOptions.RemoveEmptyEntries))
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Get list of commits by following parent links of given commit or HEAD
        /// </summary>
        /// <param name="currentWorkingDirectory">Current Working Directory</param>
        /// <param name="revision"></param>
        /// <param name="maxTake">Max number of commits to take, if not specified then all are take</param>
        /// <returns></returns>
        public async Task<IReadOnlyList<string>> ListOrderedCommits(
            string currentWorkingDirectory,
            CancellationToken cancellationToken = default,
            string revision = null,
            int? maxTake = null)
        {
            return await ExecuteQuery(
                currentWorkingDirectory,
                $"rev-list {revision ?? "HEAD"} {(maxTake != null ? $"--max-take = {maxTake}" : string.Empty)}",
                cancellationToken,
                s =>
                {
                    var result = s.Split(Constants.NewLineSplitOptions, StringSplitOptions.RemoveEmptyEntries);
                    return result;
                }).ConfigureAwait(false);
        }

        /// <summary>
        /// Generates a list of files that have been renamed
        /// </summary>
        /// <param name="currentWorkingDirectory">Current Working Directory</param>
        /// <param name="startCommitId">Start commit ID</param>
        /// <param name="endCommitId">End commit ID</param>
        /// <param name="onlyCaseChange">Indicates whether this call is only a case change and those diff files should be excluded</param>
        /// <returns></returns>
        public async Task<IReadOnlyDictionary<string, string>> ListRenamedFiles(
            string currentWorkingDirectory,
            string startCommitId,
            string endCommitId,
            CancellationToken cancellationToken = default,
            bool onlyCaseChange = false)
        {
            Requires.NotNullOrWhiteSpace(currentWorkingDirectory, nameof(currentWorkingDirectory));

            Requires.NotNullOrWhiteSpace(startCommitId, nameof(startCommitId));
            Requires.NotNullOrWhiteSpace(endCommitId, nameof(endCommitId));

            return await ExecuteQuery(
                currentWorkingDirectory,
                $"diff -M --name-status {startCommitId} {endCommitId}",
                cancellationToken,
                s =>
                {
                    var renamedFiles = new Dictionary<string, string>();
                    var lines = s.Split(Constants.NewLineSplitOptions, StringSplitOptions.RemoveEmptyEntries);
                    if (lines.Length == 0)
                    {
                        return renamedFiles;
                    }

                    foreach (var line in lines)
                    {
                        var segments = line.Split(Constants.TabSplitOptions).ToArray();
                        if (segments.Length < 3)
                        {
                            continue;
                        }

                        var fromPath = segments[1].Replace("/", @"\");
                        var toPath = segments[2].Replace("/", @"\");

                        if (!onlyCaseChange || fromPath.Equals(toPath, StringComparison.OrdinalIgnoreCase))
                        {
                            renamedFiles[toPath] = fromPath;
                        }
                    }

                    return renamedFiles;
                }).ConfigureAwait(false);
        }

        /// <summary>
        /// Returns a list of TreeEntry objects for each commit in branch, if commit not specified
        /// </summary>
        /// <param name="currentWorkingDirectory">Current Working Directory</param>
        /// <param name="commit">Specified commit</param>
        /// <returns></returns>
        public async Task<List<TreeObject>> ListTree(
            string currentWorkingDirectory,
            CancellationToken cancellationToken = default,
            string commit = null)
        {
            return await ExecuteQuery(
                currentWorkingDirectory,
                $"ls-tree --full-tree -r {commit ?? "HEAD"}",
                cancellationToken,
                s =>
                {
                    var result = new List<TreeObject>();
                    if (string.IsNullOrWhiteSpace(s))
                    {
                        return result;
                    }

                    var commits = s.Split(Constants.NewLineSplitOptions, StringSplitOptions.RemoveEmptyEntries);
                    if (commits.Length == 0)
                    {
                        return result;
                    }

                    foreach (var c in commits.Where(c => !string.IsNullOrWhiteSpace(c)))
                    {
                        var parts = c.Split(Constants.TabSplitOptions, StringSplitOptions.RemoveEmptyEntries);
                        result.Add(new TreeObject
                        {
                            ObjectType = parts[1],
                            ObjectID = parts[2],
                            ObjectPath = string.Join(" ", parts.Skip(3)).Replace("/", @"\")
                        });
                    }

                    return result;
                }).ConfigureAwait(false);
        }

        /// <summary>
        /// Prune all unreachable objects from the object database
        /// </summary>
        /// <param name="currentWorkingDirectory">Current working directory</param>
        /// <returns></returns>
        public async Task Prune(string currentWorkingDirectory)
        {
            await ExecuteNonQuery(currentWorkingDirectory, $"prune").ConfigureAwait(false);
        }

        /// <summary>
        /// Pull objects from current branch's remote or other remote if specified
        /// </summary>
        /// <param name="currentWorkingDirectory">Current working dir</param>
        /// <param name="remote">Remote to pull from</param>
        /// <returns></returns>
        public async Task Pull(
            string currentWorkingDirectory,
            CancellationToken cancellationToken = default,
            string remote = null)
        {
            await ExecuteNonQuery(
                currentWorkingDirectory,
                $"pull {remote ?? string.Empty}",
                cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Push changes to origin or remote if specified
        /// </summary>
        /// <param name="currentWorkingDirectory">Current Working Directory</param>
        /// <param name="branch">Branch to push changes to</param>
        /// <param name="remote">Remote to push changes to if specified, origin if not</param>
        /// <returns></returns>
        public async Task Push(
            string currentWorkingDirectory,
            CancellationToken cancellationToken = default,
            string branch = null,
            string remote = null)
        {
            await ExecuteNonQuery(
                currentWorkingDirectory,
                $"push -u {remote ?? "origin"} {branch}",
                cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Reset branch to remote branch
        /// </summary>
        /// <param name="currentWorkingDirectory">Current working directory</param>
        /// <param name="branch">Remote branch to reset current branch to</param>
        /// <returns></returns>
        public async Task ResetBranch(
            string currentWorkingDirectory,
            string branch,
            CancellationToken cancellationToken = default)
        {
            Requires.NotNullOrWhiteSpace(branch, nameof(branch));

            await ExecuteNonQuery(
                currentWorkingDirectory,
                $"reset --hard origin/{branch}",
                cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Reset current working directory
        /// </summary>
        /// <param name="currentWorkingDirectory">Current working directory</param>
        /// <returns></returns>
        public async Task Reset(
            string currentWorkingDirectory,
            CancellationToken cancellationToken = default)
        {
            await ExecuteNonQuery(
                currentWorkingDirectory,
                $"reset --hard",
                cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Reset branch to commit
        /// </summary>
        /// <param name="currentWorkingDirectory">Current working directory</param>
        /// <param name="commitId">CommitID to reset branch to</param>
        /// <returns></returns>
        public async Task ResetToCommit(
            string currentWorkingDirectory,
            string commitId,
            CancellationToken cancellationToken = default)
        {
            Requires.NotNullOrWhiteSpace(commitId, nameof(commitId));

            await ExecuteNonQuery(
                currentWorkingDirectory,
                $"reset --hard {commitId}",
                cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Revert existing <paramref name="commitId"/> to theirs.
        /// </summary>
        /// <param name="currentWorkingDirectory">Current Working Directory</param>
        /// <param name="commit">Commit ID</param>
        /// <returns></returns>
        public async Task Revert(string currentWorkingDirectory, string commitId, CancellationToken cancellationToken = default)
        {
            Requires.NotNullOrWhiteSpace(commitId, nameof(commitId));

            await ExecuteNonQuery(
                currentWorkingDirectory,
                $"revert --no-edit -X theirs {commitId}",
                cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Set upstream to a remote branch
        /// </summary>
        /// <param name="currentWorkingDirectory">Current Working Directory</param>
        /// <param name="branchName">Branch name for which upstream has to be set</param>
        /// <param name="remote">Remote name, origin if left out</param>
        /// <returns></returns>
        public async Task SetRemoteTrack(
            string currentWorkingDirectory,
            string branchName,
            CancellationToken cancellationToken = default,
            string remote = null)
        {
            Requires.NotNullOrWhiteSpace(branchName, nameof(branchName));

            await ExecuteNonQuery(
                currentWorkingDirectory,
                $"branch --set-upstream-to={remote ?? "origin"}/{branchName} {branchName}",
                cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Set the remote's url
        /// </summary>
        /// <param name="currentWorkingDirectory">working directory</param>
        /// <param name="newRemoteUrl">remote url</param>
        /// <param name="cancellationToken">the cancellation</param>
        /// <returns>Task</returns>
        public async Task SetRemoteUrl(
            string currentWorkingDirectory,
            string newRemoteUrl,
            CancellationToken cancellationToken = default)
        {
            Requires.NotNullOrWhiteSpace(newRemoteUrl, nameof(newRemoteUrl));

            await ExecuteNonQuery(
                currentWorkingDirectory,
                $"remote set-url origin {newRemoteUrl}",
                cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Sets the user name and email git config at the current working directory
        /// </summary>
        /// <param name="currentWorkingDirectory">the directory of the repo</param>
        /// <param name="username">the username to set</param>
        /// <param name="email">the email to set</param>
        public async Task SetUserAndEmail(string currentWorkingDirectory, string username, string email, CancellationToken cancellationToken = default)
        {
            await ExecuteNonQuery(currentWorkingDirectory, $"config user.name {username}", cancellationToken).ConfigureAwait(false);
            await ExecuteNonQuery(currentWorkingDirectory, $"config user.email {email}", cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// switches to a branch
        /// </summary>
        /// <param name="currentWorkingDirectory">Current working directory</param>
        /// <param name="create">Indicates if branch should be created</param>
        /// <param name="branch">Branch name to switch to.</param>
        /// <param name="orphan">Indicates if branch can be an orphan branch or not</param>
        /// <returns></returns>
        public async Task SwitchBranch(
            string currentWorkingDirectory,
            bool create,
            string branch,
            CancellationToken cancellationToken = default,
            bool orphan = false)
        {
            await ExecuteNonQuery(
                currentWorkingDirectory,
                $"switch {(orphan ? "--orphan" : string.Empty) } {(create ? "-c" : string.Empty)} {branch}",
                cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Public method to update or set up the sparse checkout configs
        /// </summary>
        /// <param name="currentWorkingDirectory">Current working directory</param>
        /// <param name="matchPatterns">Match patterns for sparse checkout to use</param>
        /// <returns>The <see cref="Task"/></returns>
        public async Task UpdateSparseCheckout(
            string currentWorkingDirectory,
            ICollection<MatchPattern> matchPatterns,
            CancellationToken cancellationToken = default)
        {
            await SetUpSparseCheckout(currentWorkingDirectory, matchPatterns, cancellationToken);
        }

        public async Task StoreGitCredentialsAsync(
            string currentWorkingDirectory,
            string username,
            string password,
            Uri repoUri,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(currentWorkingDirectory))
            {
                throw new ArgumentException(
                    $"'{nameof(currentWorkingDirectory)}' cannot be null or whitespace",
                    nameof(currentWorkingDirectory));
            }

            if (string.IsNullOrWhiteSpace(username))
            {
                throw new ArgumentException($"'{nameof(username)}' cannot be null or whitespace", nameof(username));
            }

            if (string.IsNullOrWhiteSpace(password))
            {
                throw new ArgumentException($"'{nameof(password)}' cannot be null or whitespace", nameof(password));
            }

            if (repoUri is null)
            {
                throw new ArgumentNullException(nameof(repoUri));
            }

            var standardInput = $"protocol=https\nhost={repoUri.Host}\npath=" +
                $"{repoUri.PathAndQuery.TrimStart('/')}\nusername={username}\npassword={password}\n\n";

            var commandLineArgs = "credential-manager-core store";

            await ExecuteNonQuery(
                currentWorkingDirectory,
                commandLineArgs,
                cancellationToken,
                standardInput: standardInput).ConfigureAwait(false);
        }

        #endregion

        #region Internal methods

        /// <summary>
        /// Enclose a path with single quotations.
        /// </summary>
        /// <param name="path">A directory path or a file path</param>
        /// <returns>
        /// A path enclosed with single quoations if the path contains one or more space characters.
        /// </returns>
        internal string AddSpaceCharProtection(string path)
        {
            // No action needed
            if (string.IsNullOrWhiteSpace(path) || !path.Contains(' '))
            {
                return path;
            }

            // The path has one or more quote chars. We assume it's already protected.
            if (path.Contains('\"') || path.Contains('\''))
            {
                return path;
            }

            return $"\"{path.Trim()}\"";
        }

        /// <summary>
        /// Execute commands that don't "return" a value
        /// </summary>
        /// <param name="currentWorkingDirectory">Current Working Directory</param>
        /// <param name="commandLineArgs">Commands to run alongwith arguments</param>
        /// <param name="timespan">Timeout after which the git process should be killed</param>
        /// <returns></returns>
        internal async Task ExecuteNonQuery(
            string currentWorkingDirectory,
            string commandLineArgs,
            CancellationToken cancellationToken = default,
            TimeSpan? timespan = null,
            string standardInput = default)
        {
            Requires.NotNullOrWhiteSpace(commandLineArgs, nameof(commandLineArgs));

            await ExecuteGitCommand(
                currentWorkingDirectory,
                commandLineArgs,
                timespan,
                x => x,
                cancellationToken,
                standardInput).ConfigureAwait(false);
        }

        #endregion

        #region Private methods

        private async Task<T> ExecuteGitCommand<T>(
            string currentWorkingDirectory,
            string commandLineArgs,
            TimeSpan? timeSpan,
            Func<string, T> parser,
            CancellationToken cancellationToken = default,
            string standardInput = default)
        {
            Requires.NotNullOrWhiteSpace(currentWorkingDirectory, nameof(currentWorkingDirectory));
            var workingDirectory = currentWorkingDirectory;

            Verify.Operation(gitPath != null, "Couldn't find path to git.exe");
            Verify.Operation(
                Directory.Exists(currentWorkingDirectory),
                $"Working Directory not found: {currentWorkingDirectory}");

            var startInfo = new ProcessStartInfo
            {
                FileName = gitPath,
                WorkingDirectory = workingDirectory,
                Arguments = commandLineArgs,
                CreateNoWindow = false,
                UseShellExecute = false,
                Verb = "runas",
                RedirectStandardOutput = true,
                RedirectStandardInput = true,
                RedirectStandardError = true
            };

            var result = await processExecutor.ExecuteProcessAsync(startInfo, cancellationToken, input: standardInput);

            if (result.ExitCode != 0)
            {
                throw new Exception($"'\"{gitPath}\"' git command failed in directory " +
                    $"{currentWorkingDirectory} with exit code {result.ExitCode}. \nError: {result.StandardError}" +
                    $" \nOutput: {result.StandardOutput}");
            }

            return parser(result.StandardOutput);
        }

        /// <summary>
        /// Execute commands that return values after being processed by a specified delegate
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="currentWorkingDirectory">Current Working Directory</param>
        /// <param name="commandLineArgs">Commands to run alongwith arguments</param>
        /// <param name="parser">Specified function to parse the command line output</param>
        /// <param name="timespan">Timeout after which the git process should be killed</param>
        /// <returns></returns>
        private async Task<T> ExecuteQuery<T>(
            string currentWorkingDirectory,
            string commandLineArgs,
            CancellationToken cancellationToken = default,
            Func<string, T> parser = null,
            TimeSpan? timespan = null)
        {
            return await ExecuteGitCommand(
                currentWorkingDirectory,
                commandLineArgs,
                timespan,
                parser,
                cancellationToken).ConfigureAwait(false);
        }

        private async Task SetGlobalGitConfig(
            string currentWorkingDirectory,
            string configArgumentsString,
            CancellationToken cancellationToken = default)
        {
            await ExecuteNonQuery(currentWorkingDirectory, $"config --global {configArgumentsString}", cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Sets up the sparse checkout configs
        /// </summary>
        /// <param name="currentWorkingDirectory">Current working directory</param>
        /// <param name="matchPatterns">Match patterns for sparse checkout to use</param>
        /// <returns>The <see cref="Task"/></returns>
        private async Task SetUpSparseCheckout(
            string currentWorkingDirectory,
            ICollection<MatchPattern> matchPatterns,
            CancellationToken cancellationToken = default)
        {
            await ExecuteNonQuery(
                currentWorkingDirectory,
                $"sparse-checkout init",
                cancellationToken,
                Constants.LongRunningTimeout).ConfigureAwait(false);

            var uniqueMatchPatterns = matchPatterns
                .Select(mp =>
                {
                    var rawPattern = MatchPattern.GetRelPathFromPattern(mp.Pattern).Trim();
                    var sparseCheckoutPattern = mp.Pattern.StartsWith("!") ? "!" + rawPattern : rawPattern;
                    sparseCheckoutPattern = sparseCheckoutPattern.Trim(new[] { '\\' });
                    sparseCheckoutPattern = sparseCheckoutPattern.Replace("\\", "/");
                    sparseCheckoutPattern = this.AddSpaceCharProtection(sparseCheckoutPattern);

                    return sparseCheckoutPattern;
                })
                .Distinct()
                .ToList();

            // if all patterns are excludes, we need a wildcard to check out anything not excluded
            if (matchPatterns.Where(mp => mp.Pattern.StartsWith("!")).Any() &&
                !matchPatterns.Where(mp => !mp.Pattern.StartsWith("!")).Any())
            {
                uniqueMatchPatterns.Insert(0, "/*");
            }

            var matchPatternsList = string.Join(" ", uniqueMatchPatterns);

            await ExecuteNonQuery(
                currentWorkingDirectory,
                $"sparse-checkout set {matchPatternsList}",
                cancellationToken,
                Constants.LongRunningTimeout);
        }

        #endregion
    }
}

