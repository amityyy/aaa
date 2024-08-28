using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Azure.Core;
using Microsoft.CloudMine.SourceCode.Collectors.Core.Settings;
using Microsoft.CloudMine.SourceCode.Collectors.Core.Model;

namespace Microsoft.CloudMine.SourceCode.Collectors.Core.GitModel.Authentication
{
    public class DefaultAzureGitAuthentication : IGitAuthentication
    {
        private readonly TokenCredential credential;
        private readonly TimeProvider timeProvider;
        private AccessToken? accessToken;

        public DefaultAzureGitAuthentication(TimeProvider? timeProvider = null)
        {
            this.credential = SourceCodeAzureCredentialProvider.GetDeploymentCredential();
            this.timeProvider = timeProvider ?? TimeProvider.System;
        }

        public async Task<IEnumerable<string>> GetAuthenticatedArgs()
        {
            // Check if the access token is missing or expired
            if (!accessToken.HasValue || accessToken.Value.ExpiresOn.UtcDateTime <= timeProvider.GetUtcNow())
            {
                accessToken = await credential.GetTokenAsync(
                    new TokenRequestContext(new[] { Constants.AzureDevOpsResourceId }), 
                    new CancellationToken()
                );
            }

            // Return the authentication arguments with the access token
            return new List<string>
            {
                "-c",
                $"http.extraheader=Authorization: Bearer {accessToken.Value.Token}"
            };
        }
    }
}
