using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace ScoutCore
{
    public class PATHelper
    {
        private static readonly PATHelper _patHelper = new PATHelper();
        private readonly ILogger _log;

        public static PATHelper Instance
        {
            get { return _patHelper; }
        }

        public PATHelper()
        {
            _log = Logging.Loggers.CreateLogger<PATHelper>();
        }

        /// <summary>
        /// Gets the user's PAT to use Azure DevOps APIs.
        /// Currently, there are two places relying on the PAT. If we get the PAT (meaning the user opens Scout from the enlistment),
        /// (1) the "Share Query" button will be enabled and (2) we load all shared queries from the OneDrive client repository.
        /// </summary>
        /// <returns>User's PAT</returns>
        public virtual async Task<string> GetPAT()
        {
            _log.LogDebug("Getting the user PAT.");
            string pat = string.Empty;

            try
            {
                var util = new AuthenticationUtilities();
                pat = await util.GetAccessToken(AuthenticationUtilities.AuthenticationTargets.AzureDevOpsService, null);

                if (string.IsNullOrWhiteSpace(pat))
                {
                    _log.LogDebug("No PAT returned from GetAccessToken().");
                }
                else
                {
                    _log.LogDebug("Successfully got the user's PAT.");
                }
            }
            catch (Exception ex)
            {
                _log.LogError($"Failed to get the PAT from GetAccessToken(). Exception: {ex}");
            }

            return pat;
        }
    }
}
