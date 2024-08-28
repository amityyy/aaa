using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Azure.Core;
using Microsoft.Identity.Client;
using Microsoft.CloudMine.SourceCode.Collectors.Core.Settings;
using Microsoft.CloudMine.Core.Collectors.Model;

namespace Microsoft.CloudMine.SourceCode.Collectors.Core.GitModel.Authentication
{
    public class FederatedCredentialGitAuthentication : IGitAuthentication
    {
        private readonly IConfidentialClientApplication app;
        private readonly TimeProvider timeProvider;
        private AuthenticationResult? authenticationResult;

        public FederatedCredentialGitAuthentication(string applicationId, TimeProvider? timeProvider = null)
        {
            this.timeProvider = timeProvider ?? TimeProvider.System;
            this.app = SourceCodeAzureCredentialProvider.GetConfidentialClientApplication(applicationId);
        }

        public async Task<IEnumerable<string>> GetAuthenticatedArgs()
        {
            var tokenRequestContext = new TokenRequestContext(new[] { Constants.AzureDevOpsResourceId });

            // Check if the authentication result is null or expired
            if (authenticationResult == null || authenticationResult.ExpiresOn <= timeProvider.GetUtcNow())
            {
                authenticationResult = await app.AcquireTokenForClient(tokenRequestContext.Scopes)
                    .WithSendX5C(true)
                    .ExecuteAsync()
                    .ConfigureAwait(false);
            }

            // Return the authentication arguments with the access token
            return new List<string>
            {
                "-c",
                $"http.extraheader=Authorization: Bearer {authenticationResult.AccessToken}"
            };
        }
    }
}
