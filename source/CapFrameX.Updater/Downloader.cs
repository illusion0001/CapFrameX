using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace CapFrameX.Updater
{
    public class Downloader
    {
        private const string GITHUB_REPO = "https://api.github.com/repos/CXWorld/CapFrameX";

        public async Task<(string Filename, byte[] Content)> DownloadVersion(Version version, string type)
        {
            using (var httpClient = new HttpClient())
            {
                httpClient.DefaultRequestHeaders.UserAgent.Add(new System.Net.Http.Headers.ProductInfoHeaderValue("CX_Updater", Assembly.GetExecutingAssembly().GetName().Version.ToString()));
                httpClient.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));
                var release = await httpClient.GetAsync($"{GITHUB_REPO}/releases/tags/v{version}");

                if(!release.IsSuccessStatusCode)
                {
                    throw new Exception($"Version {version} not found", new Exception(await release.Content.ReadAsStringAsync()));
                }

                var releaseJson = JToken.Parse(await release.Content.ReadAsStringAsync());

                var releaseAssets = releaseJson["assets"].ToArray();

                var matchingReleaseByType = releaseAssets.FirstOrDefault(a => a.Value<string>("name").IndexOf(type, StringComparison.OrdinalIgnoreCase) >= 0);

                if(matchingReleaseByType is null)
                {
                    throw new Exception($"Type {type} not found in Release {version} assets");
                }

                var releaseAssetDownloadUrl = matchingReleaseByType.Value<string>("browser_download_url");

                Console.WriteLine($"Downloading from {releaseAssetDownloadUrl}");
                Console.WriteLine("Please wait");

                var fileStream = await httpClient.GetStreamAsync(releaseAssetDownloadUrl);
                using (var memoryStream = new MemoryStream())
                {
                    await fileStream.CopyToAsync(memoryStream);
                    return (matchingReleaseByType.Value<string>("name"), memoryStream.ToArray());
                }
            }
        }

        public async Task<string> DetermineLatestVersion()
        {
            using(var httpClient = new HttpClient())
            {
                httpClient.DefaultRequestHeaders.UserAgent.Add(new System.Net.Http.Headers.ProductInfoHeaderValue("CX_Updater", Assembly.GetExecutingAssembly().GetName().Version.ToString()));
                httpClient.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));

                var latestRelease = await httpClient.GetAsync($"{GITHUB_REPO}/releases/latest");

                var latestReleaseJson = JToken.Parse(await latestRelease.Content.ReadAsStringAsync());

                return latestReleaseJson.Value<string>("tag_name").Replace("v", string.Empty);
            }
        }
    }
}
