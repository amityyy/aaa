using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.CloudMine.AzureDevOps.Collectors.Authentication;
using Microsoft.CloudMine.Core.Auditing;
using Microsoft.CloudMine.Core.Collectors.Authentication;
using Microsoft.CloudMine.Core.Telemetry;
using Microsoft.CloudMine.SourceCode.Collectors.Core.Settings;

namespace Microsoft.CloudMine.SourceCode.Collectors.Core.GitModel.Authentication
{
    public class BearerTokenGitAuthentication : BearerTokenAuthentication, IGitAuthentication
    {
        public BearerTokenGitAuthentication(
            ITelemetryClient telemetryClient,
            IAuditLogger auditLogger,
            BearerTokenAuthenticationSettings settings)
            : base(telemetryClient, auditLogger, settings, SourceCodeAzureCredentialProvider.GetDeploymentCredential())
        {
        }

        public async Task<IEnumerable<string>> GetAuthenticatedArgs()
        {
            // Obtain the bearer token asynchronously
            string bearerToken = await this.GetAuthorizationHeaderAsync().ConfigureAwait(false);
            
            // Return the authentication arguments with the bearer token
            return new List<string> { "-c", $"http.extraheader=Authorization: Bearer {bearerToken}" };
        }
    }
}
