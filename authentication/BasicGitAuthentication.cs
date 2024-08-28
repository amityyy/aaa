using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Microsoft.CloudMine.Core.Collectors.Authentication;

namespace Microsoft.CloudMine.SourceCode.Collectors.Core.GitModel.Authentication
{
    public class BasicGitAuthentication : IGitAuthentication
    {
        private readonly BasicAuthSettings settings;

        public BasicGitAuthentication(BasicAuthSettings settings)
        {
            this.settings = settings;
        }

        public Task<IEnumerable<string>> GetAuthenticatedArgs()
        {
            // Convert identity and personal access token to a Base64-encoded string
            string authHeader = Convert.ToBase64String(
                Encoding.ASCII.GetBytes($"{this.settings.Identity}:{this.settings.PersonalAccessToken}")
            );

            // Return the authentication arguments as an IEnumerable<string>
            return Task.FromResult<IEnumerable<string>>(
                new List<string> { "-c", $"http.extraheader=Authorization: Basic {authHeader}" }
            );
        }
    }
}