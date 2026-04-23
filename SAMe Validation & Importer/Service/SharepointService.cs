using Microsoft.Identity.Client;
using SAMe_VI.Object;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using File = System.IO.File;

namespace SAMe_VI.Service
{
    internal static class SharepointService
    {
        public static async Task<string> UploadFile(string folderPath, string fileName)
        {
            Sharepoint? sharepoint = Configuration.SharepointConfig;
            if (sharepoint == null)
            {
                return string.Empty;
            }

            if (string.IsNullOrWhiteSpace(fileName))
            {
                throw new ArgumentException("fileName is required.", nameof(fileName));
            }

            if (string.IsNullOrWhiteSpace(folderPath))
            {
                throw new ArgumentException("folderPath is required.", nameof(folderPath));
            }

            if (!File.Exists(fileName))
            {
                throw new FileNotFoundException("Local file not found.", fileName);
            }

            if (string.IsNullOrWhiteSpace(sharepoint.SiteURL))
            {
                throw new InvalidOperationException("SharePoint.SiteURL is missing.");
            }

            if (string.IsNullOrWhiteSpace(sharepoint.TenantID))
            {
                throw new InvalidOperationException("SharePoint.TenantID is missing.");
            }

            if (string.IsNullOrWhiteSpace(sharepoint.ClientID))
            {
                throw new InvalidOperationException("SharePoint.ClientID is missing.");
            }

            if (string.IsNullOrWhiteSpace(sharepoint.ClientSecret))
            {
                throw new InvalidOperationException("SharePoint.ClientSecret is missing.");
            }

            Uri siteUri = new Uri(sharepoint.SiteURL);
            string hostName = siteUri.Host;
            string sitePath = siteUri.AbsolutePath.TrimEnd('/');

            FolderMapping mapping = MapServerRelativeFolderToLibrary(folderPath, sitePath);

            IConfidentialClientApplication app = ConfidentialClientApplicationBuilder
                .Create(sharepoint.ClientID)
                .WithClientSecret(sharepoint.ClientSecret)
                .WithAuthority(new Uri("https://login.microsoftonline.com/" + sharepoint.TenantID))
                .Build();

            string[] scopes = new string[] { "https://graph.microsoft.com/.default" };
            AuthenticationResult authResult = await app.AcquireTokenForClient(scopes).ExecuteAsync().ConfigureAwait(false);

            HttpClientHandler handler = new HttpClientHandler();
            handler.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            handler.AllowAutoRedirect = true;

            using (HttpClient httpClient = new HttpClient(handler, disposeHandler: true))
            {
                httpClient.BaseAddress = new Uri("https://graph.microsoft.com/v1.0/");
                httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authResult.AccessToken);
                httpClient.DefaultRequestHeaders.Accept.Clear();
                httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                SiteInfo siteInfo = await GetSiteByPathAsync(httpClient, hostName, sitePath).ConfigureAwait(false);
                DriveInfo driveInfo = await ResolveDriveAsync(httpClient, siteInfo.Id, mapping.LibraryName).ConfigureAwait(false);

                string localName = Path.GetFileName(fileName);
                FileInfo localFileInfo = new FileInfo(fileName);

                string uploadPathWithinLibrary = BuildUploadPath(mapping.PathWithinLibrary, localName);

                GraphDriveItem uploadedItem;
                if (localFileInfo.Length <= 4L * 1024L * 1024L)
                {
                    uploadedItem = await UploadSmallFileAsync(httpClient, driveInfo.Id, uploadPathWithinLibrary, fileName).ConfigureAwait(false);
                }
                else
                {
                    uploadedItem = await UploadLargeFileAsync(httpClient, driveInfo.Id, uploadPathWithinLibrary, fileName).ConfigureAwait(false);
                }

                if (string.IsNullOrWhiteSpace(uploadedItem.WebUrl))
                {
                    return string.Empty;
                }

                Uri webUrl = new Uri(uploadedItem.WebUrl);
                return webUrl.AbsolutePath;
            }
        }

        private static string BuildUploadPath(string pathWithinLibrary, string fileName)
        {
            if (string.IsNullOrWhiteSpace(pathWithinLibrary))
            {
                return fileName;
            }

            string trimmed = pathWithinLibrary.Trim('/');
            return trimmed + "/" + fileName;
        }

        private static async Task<GraphDriveItem> UploadSmallFileAsync(HttpClient httpClient, string driveId, string uploadPathWithinLibrary, string localFilePath)
        {
            string escapedPath = EscapePathForDrive(uploadPathWithinLibrary);
            string requestUrl = "drives/" + Uri.EscapeDataString(driveId) + "/root:/" + escapedPath + ":/content";

            using (FileStream stream = new FileStream(localFilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (StreamContent content = new StreamContent(stream))
            using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Put, requestUrl))
            {
                content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                request.Content = content;

                using (HttpResponseMessage response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false))
                {
                    string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode)
                    {
                        ThrowGraphError(response, body);
                    }

                    GraphDriveItem item = JsonSerializer.Deserialize<GraphDriveItem>(body, JsonOptions()) ?? new GraphDriveItem();
                    return item;
                }
            }
        }

        private static async Task<GraphDriveItem> UploadLargeFileAsync(HttpClient httpClient, string driveId, string uploadPathWithinLibrary, string localFilePath)
        {
            string escapedPath = EscapePathForDrive(uploadPathWithinLibrary);
            string createSessionUrl = "drives/" + Uri.EscapeDataString(driveId) + "/root:/" + escapedPath + ":/createUploadSession";

            string payload =
                "{\"item\":{" +
                "\"@microsoft.graph.conflictBehavior\":\"replace\"" +
                "}}";

            using (HttpRequestMessage createRequest = new HttpRequestMessage(HttpMethod.Post, createSessionUrl))
            {
                createRequest.Content = new StringContent(payload, Encoding.UTF8, "application/json");

                using (HttpResponseMessage createResponse = await httpClient.SendAsync(createRequest, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false))
                {
                    string createBody = await createResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!createResponse.IsSuccessStatusCode)
                    {
                        ThrowGraphError(createResponse, createBody);
                    }

                    UploadSession session = JsonSerializer.Deserialize<UploadSession>(createBody, JsonOptions()) ?? new UploadSession();
                    if (string.IsNullOrWhiteSpace(session.UploadUrl))
                    {
                        throw new InvalidOperationException("Graph upload session did not return an uploadUrl.");
                    }

                    GraphDriveItem? completedItem = await UploadInChunksAsync(session.UploadUrl, localFilePath).ConfigureAwait(false);
                    if (completedItem == null)
                    {
                        throw new InvalidOperationException("Chunked upload did not return a completed DriveItem.");
                    }

                    return completedItem;
                }
            }
        }

        private static async Task<GraphDriveItem?> UploadInChunksAsync(string uploadUrl, string localFilePath)
        {
            FileInfo fileInfo = new FileInfo(localFilePath);
            long fileSize = fileInfo.Length;

            int chunkSize = 10 * 1024 * 1024;
            byte[] buffer = new byte[chunkSize];

            using (HttpClient uploadClient = new HttpClient())
            using (FileStream stream = new FileStream(localFilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                long offset = 0;
                while (offset < fileSize)
                {
                    int read = await stream.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
                    if (read <= 0)
                    {
                        break;
                    }

                    long start = offset;
                    long end = offset + read - 1;

                    using (ByteArrayContent chunkContent = new ByteArrayContent(buffer, 0, read))
                    using (HttpRequestMessage put = new HttpRequestMessage(HttpMethod.Put, uploadUrl))
                    {
                        chunkContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                        chunkContent.Headers.ContentRange = new ContentRangeHeaderValue(start, end, fileSize);

                        put.Content = chunkContent;

                        using (HttpResponseMessage response = await uploadClient.SendAsync(put, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false))
                        {
                            string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                            if (response.StatusCode == HttpStatusCode.Accepted)
                            {
                                offset += read;
                                continue;
                            }

                            if (response.IsSuccessStatusCode)
                            {
                                GraphDriveItem item = JsonSerializer.Deserialize<GraphDriveItem>(body, JsonOptions()) ?? new GraphDriveItem();
                                return item;
                            }

                            ThrowGraphError(response, body);
                        }
                    }

                    offset += read;
                }
            }

            return null;
        }

        private static FolderMapping MapServerRelativeFolderToLibrary(string serverRelativeFolder, string sitePath)
        {
            string normalizedFolder = serverRelativeFolder.Trim();
            if (!normalizedFolder.StartsWith("/", StringComparison.Ordinal))
            {
                normalizedFolder = "/" + normalizedFolder;
            }

            string normalizedSitePath = sitePath.TrimEnd('/');
            if (!normalizedSitePath.StartsWith("/", StringComparison.Ordinal))
            {
                normalizedSitePath = "/" + normalizedSitePath;
            }

            if (!normalizedFolder.StartsWith(normalizedSitePath + "/", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(normalizedFolder, normalizedSitePath, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Folder '" + serverRelativeFolder + "' is not under the SiteURL path '" + sitePath + "'.");
            }

            string remainder = normalizedFolder.Substring(normalizedSitePath.Length).TrimStart('/');
            if (string.IsNullOrWhiteSpace(remainder))
            {
                throw new InvalidOperationException("Folder path does not include a document library segment.");
            }

            int firstSlash = remainder.IndexOf('/', StringComparison.Ordinal);
            string libraryName = firstSlash < 0 ? remainder : remainder.Substring(0, firstSlash);

            string withinLibrary = firstSlash < 0 ? string.Empty : remainder.Substring(firstSlash + 1);
            withinLibrary = withinLibrary.Trim('/');

            return new FolderMapping(libraryName, withinLibrary);
        }

        private static async Task<SiteInfo> GetSiteByPathAsync(HttpClient httpClient, string hostName, string sitePath)
        {
            string encodedPath = Uri.EscapeDataString(sitePath).Replace("%2F", "/");
            string requestUrl = "sites/" + hostName + ":" + encodedPath + "?$select=id,webUrl";

            using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, requestUrl))
            using (HttpResponseMessage response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false))
            {
                string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    ThrowGraphError(response, body);
                }

                using (JsonDocument doc = JsonDocument.Parse(body))
                {
                    string id = ReadRequiredString(doc.RootElement, "id");
                    string webUrl = ReadOptionalString(doc.RootElement, "webUrl", string.Empty);
                    return new SiteInfo(id, webUrl);
                }
            }
        }

        private static async Task<DriveInfo> ResolveDriveAsync(HttpClient httpClient, string siteId, string libraryName)
        {
            string requestUrl = "sites/" + Uri.EscapeDataString(siteId) + "/drives?$select=id,name,webUrl";

            using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, requestUrl))
            using (HttpResponseMessage response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false))
            {
                string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    ThrowGraphError(response, body);
                }

                using (JsonDocument doc = JsonDocument.Parse(body))
                {
                    if (!doc.RootElement.TryGetProperty("value", out JsonElement drivesElement) || drivesElement.ValueKind != JsonValueKind.Array)
                    {
                        throw new InvalidOperationException("Graph drives response did not contain a value array.");
                    }

                    string normalizedLibrary = WebUtility.UrlDecode(libraryName).Trim();

                    foreach (JsonElement driveEl in drivesElement.EnumerateArray())
                    {
                        string id = ReadRequiredString(driveEl, "id");
                        string name = ReadOptionalString(driveEl, "name", string.Empty);
                        string webUrl = ReadOptionalString(driveEl, "webUrl", string.Empty);

                        bool nameMatch = string.Equals(name, normalizedLibrary, StringComparison.OrdinalIgnoreCase);
                        bool urlMatch = !string.IsNullOrWhiteSpace(webUrl) &&
                                        webUrl.IndexOf("/" + normalizedLibrary, StringComparison.OrdinalIgnoreCase) >= 0;

                        if (nameMatch || urlMatch)
                        {
                            return new DriveInfo(id, name, webUrl);
                        }
                    }

                    foreach (JsonElement driveEl in drivesElement.EnumerateArray())
                    {
                        string id = ReadRequiredString(driveEl, "id");
                        string name = ReadOptionalString(driveEl, "name", string.Empty);
                        string webUrl = ReadOptionalString(driveEl, "webUrl", string.Empty);

                        if (string.Equals(name, "Documents", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(name, "Shared Documents", StringComparison.OrdinalIgnoreCase))
                        {
                            return new DriveInfo(id, name, webUrl);
                        }
                    }

                    throw new InvalidOperationException("Could not resolve a drive for library '" + libraryName + "'.");
                }
            }
        }

        public static async Task<bool> DeleteFile(string sharepointFilePathOrUrl)
        {
            Sharepoint? sharepoint = Configuration.SharepointConfig;
            if (sharepoint == null)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(sharepointFilePathOrUrl))
            {
                throw new ArgumentException("sharepointFilePathOrUrl is required.", nameof(sharepointFilePathOrUrl));
            }

            if (string.IsNullOrWhiteSpace(sharepoint.SiteURL))
            {
                throw new InvalidOperationException("SharePoint.SiteURL is missing.");
            }

            if (string.IsNullOrWhiteSpace(sharepoint.TenantID))
            {
                throw new InvalidOperationException("SharePoint.TenantID is missing.");
            }

            if (string.IsNullOrWhiteSpace(sharepoint.ClientID))
            {
                throw new InvalidOperationException("SharePoint.ClientID is missing.");
            }

            if (string.IsNullOrWhiteSpace(sharepoint.ClientSecret))
            {
                throw new InvalidOperationException("SharePoint.ClientSecret is missing.");
            }

            string serverRelativePath = NormalizeToServerRelativePath(sharepointFilePathOrUrl);
            serverRelativePath = WebUtility.UrlDecode(serverRelativePath);

            if (serverRelativePath.EndsWith("/", StringComparison.Ordinal))
            {
                throw new ArgumentException("A file path is required (must not end with '/').", nameof(sharepointFilePathOrUrl));
            }

            Uri siteUri = new Uri(sharepoint.SiteURL);
            string hostName = siteUri.Host;
            string sitePath = siteUri.AbsolutePath.TrimEnd('/');

            FolderMapping mapping = MapServerRelativeFileToLibrary(serverRelativePath, sitePath);

            IConfidentialClientApplication app = ConfidentialClientApplicationBuilder
                .Create(sharepoint.ClientID)
                .WithClientSecret(sharepoint.ClientSecret)
                .WithAuthority(new Uri("https://login.microsoftonline.com/" + sharepoint.TenantID))
                .Build();

            string[] scopes = new string[] { "https://graph.microsoft.com/.default" };
            AuthenticationResult authResult = await app.AcquireTokenForClient(scopes).ExecuteAsync().ConfigureAwait(false);

            HttpClientHandler handler = new HttpClientHandler();
            handler.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            handler.AllowAutoRedirect = true;

            using (HttpClient httpClient = new HttpClient(handler, disposeHandler: true))
            {
                httpClient.BaseAddress = new Uri("https://graph.microsoft.com/v1.0/");
                httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authResult.AccessToken);
                httpClient.DefaultRequestHeaders.Accept.Clear();
                httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                SiteInfo siteInfo = await GetSiteByPathAsync(httpClient, hostName, sitePath).ConfigureAwait(false);
                DriveInfo driveInfo = await ResolveDriveAsync(httpClient, siteInfo.Id, mapping.LibraryName).ConfigureAwait(false);

                string? itemId = await TryGetDriveItemIdByPathAsync(httpClient, driveInfo.Id, mapping.PathWithinLibrary).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(itemId))
                {
                    return false;
                }

                await DeleteDriveItemAsync(httpClient, driveInfo.Id, itemId).ConfigureAwait(false);
                return true;
            }
        }

        private static string NormalizeToServerRelativePath(string input)
        {
            if (Uri.TryCreate(input, UriKind.Absolute, out Uri? uri))
            {
                return uri.AbsolutePath;
            }

            if (!input.StartsWith("/", StringComparison.Ordinal))
            {
                return "/" + input;
            }

            return input;
        }

        private static FolderMapping MapServerRelativeFileToLibrary(string serverRelativeFile, string sitePath)
        {
            string normalizedFile = serverRelativeFile.Trim();
            if (!normalizedFile.StartsWith("/", StringComparison.Ordinal))
            {
                normalizedFile = "/" + normalizedFile;
            }

            string normalizedSitePath = sitePath.TrimEnd('/');
            if (!normalizedSitePath.StartsWith("/", StringComparison.Ordinal))
            {
                normalizedSitePath = "/" + normalizedSitePath;
            }

            if (!normalizedFile.StartsWith(normalizedSitePath + "/", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("File '" + serverRelativeFile + "' is not under the SiteURL path '" + sitePath + "'.");
            }

            string remainder = normalizedFile.Substring(normalizedSitePath.Length).TrimStart('/');
            if (string.IsNullOrWhiteSpace(remainder))
            {
                throw new InvalidOperationException("File path does not include a document library segment.");
            }

            int firstSlash = remainder.IndexOf('/', StringComparison.Ordinal);
            if (firstSlash < 0)
            {
                throw new InvalidOperationException("File path does not include a path within the library.");
            }

            string libraryName = remainder.Substring(0, firstSlash);
            string withinLibrary = remainder.Substring(firstSlash + 1).Trim('/');

            if (string.IsNullOrWhiteSpace(withinLibrary))
            {
                throw new InvalidOperationException("File path does not include a file name.");
            }

            return new FolderMapping(libraryName, withinLibrary);
        }

        private static async Task<string?> TryGetDriveItemIdByPathAsync(HttpClient httpClient, string driveId, string pathWithinLibrary)
        {
            if (string.IsNullOrWhiteSpace(pathWithinLibrary))
            {
                return null;
            }

            string escapedPath = EscapePathForDrive(pathWithinLibrary);
            string requestUrl = "drives/" + Uri.EscapeDataString(driveId) + "/root:/" + escapedPath + "?$select=id";

            using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, requestUrl))
            using (HttpResponseMessage response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false))
            {
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    return null;
                }

                string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    ThrowGraphError(response, body);
                }

                using (JsonDocument doc = JsonDocument.Parse(body))
                {
                    string id = ReadRequiredString(doc.RootElement, "id");
                    return id;
                }
            }
        }

        private static async Task DeleteDriveItemAsync(HttpClient httpClient, string driveId, string itemId)
        {
            string requestUrl = "drives/" + Uri.EscapeDataString(driveId) + "/items/" + Uri.EscapeDataString(itemId);

            using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Delete, requestUrl))
            using (HttpResponseMessage response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false))
            {
                if (response.StatusCode == HttpStatusCode.NoContent)
                {
                    return;
                }

                string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    ThrowGraphError(response, body);
                }
            }
        }

        private static string EscapePathForDrive(string path)
        {
            string[] segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            StringBuilder sb = new StringBuilder();

            for (int i = 0; i < segments.Length; i++)
            {
                if (i > 0)
                {
                    sb.Append('/');
                }

                string segment = segments[i];
                string escaped = Uri.EscapeDataString(segment);
                sb.Append(escaped);
            }

            return sb.ToString();
        }

        private static void ThrowGraphError(HttpResponseMessage response, string responseBody)
        {
            throw new HttpRequestException("Graph request failed: " + (int)response.StatusCode + " " + response.StatusCode + " " + responseBody);
        }

        private static string ReadRequiredString(JsonElement element, string propertyName)
        {
            if (element.TryGetProperty(propertyName, out JsonElement prop) && prop.ValueKind == JsonValueKind.String)
            {
                string? value = prop.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            throw new InvalidOperationException("Missing required property '" + propertyName + "' in Graph response.");
        }

        private static string ReadOptionalString(JsonElement element, string propertyName, string fallback)
        {
            if (element.TryGetProperty(propertyName, out JsonElement prop) && prop.ValueKind == JsonValueKind.String)
            {
                return prop.GetString() ?? fallback;
            }

            return fallback;
        }

        private static JsonSerializerOptions JsonOptions()
        {
            JsonSerializerOptions options = new JsonSerializerOptions();
            options.PropertyNameCaseInsensitive = true;
            return options;
        }

        private sealed record SiteInfo(string Id, string WebUrl);

        private sealed record DriveInfo(string Id, string Name, string WebUrl);

        private sealed record FolderMapping(string LibraryName, string PathWithinLibrary);

        private sealed class UploadSession
        {
            public string? UploadUrl { get; set; }
        }

        private sealed class GraphDriveItem
        {
            public string? Id { get; set; }
            public string? Name { get; set; }
            public string? WebUrl { get; set; }
        }
    }
}