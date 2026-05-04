using Microsoft.Extensions.Configuration;
namespace ChatRagAppConfiguration;
public class ImageSearchConfigurationInfo
{
    public record ImageSearchConfig
{
    public int ImageResize { get; init; } = 64;
    public int SpatialGrid { get; init; } = 4;
    public string ActiveCLIPModel { get; init; } = "false";
}

    public static ImageSearchConfig GetImageSearchConfigurationInfo()
    {

        var configuration = new ConfigurationBuilder()
            .AddUserSecrets<ImageSearchConfigurationInfo>(optional: true)
            .AddEnvironmentVariables()
            .Build();

        return new ImageSearchConfig
        {
            ImageResize = int.Parse(GetValue(configuration, "ImageSearch_ImageResize") ?? "64"),
            SpatialGrid = int.Parse(GetValue(configuration, "ImageSearch_SpatialGrid") ?? "4"),
            ActiveCLIPModel = GetValue(configuration, "ImageSearch_ActiveCLIPModel") ?? "false"
        };
    }
     
    private static string? GetValue(IConfiguration configuration, params string[] keys)
    {
        foreach (var key in keys)
        {
            var value = configuration[key];
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

}

    