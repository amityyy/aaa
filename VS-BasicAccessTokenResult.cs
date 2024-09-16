using System;
using System.Text;

namespace Microsoft.VisualStudio.Debugger.Utilities.SourceLink
{
    internal class BasicAccessTokenResult : IAccessTokenResult
    {
        public string AccessTokenType => "Basic";

        public string AccessToken { get; protected set; }

        public string Error => null;

        public readonly string Username;
        public readonly string Password;

        public BasicAccessTokenResult(string username, string password)
        {
            // Username can be empty in the case of password being a personal access token,
            // but neither can be null
            if (username == null)
            {
                throw new ArgumentNullException(nameof(username));
            }

            if (password == null)
            {
                throw new ArgumentNullException(nameof(password));
            }

            if (string.IsNullOrWhiteSpace(password))
            {
                throw new ArgumentException("Password cannot be empty or whitespace.", nameof(password));
            }

            Username = username;
            Password = password;
            AccessToken = GetBase64EncodedString($"{Username}:{Password}");
        }

        public virtual void Accept(Uri uri)
        {
            // TODO: Subclass this for GCM
        }

        public virtual void Reject(Uri uri)
        {
            // TODO: Subclass this for GCM
        }

        protected string GetBase64EncodedString(string input)
        {
            return Convert.ToBase64String(Encoding.ASCII.GetBytes(input));
        }
    }
}
