using Microsoft.Extensions.Configuration;

namespace ChatRagAppConfiguration;

public class ChatRagAppConfigurationInfo
{
    public static ConfigurationInfo GetChatRagAppConfigurationInfo()
    {
        var configuration = new ConfigurationBuilder()
            .AddUserSecrets<ChatRagAppConfigurationInfo>(optional: true)
            .AddEnvironmentVariables()
            .Build();

        return new ConfigurationInfo
        {
            GithubToken = GetValue(configuration, "GitHubAIModels_Token"),
            GithubEndpoint = GetValue(configuration, "GitHubAIModels_Url"),
            GithubModelName = GetValue(configuration, "GitHubAIModels_Model")
        };
    }

    private static string GetValue(IConfiguration configuration, params string[] keys)
    {
        foreach (var key in keys)
        {
            var value = configuration[key];
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return string.Empty;
    }
}

public class ConfigurationInfo
{
    public string GithubToken { get; set; } = string.Empty;
    public string GithubEndpoint { get; set; } = string.Empty;
    public string GithubModelName { get; set; } = string.Empty;
}