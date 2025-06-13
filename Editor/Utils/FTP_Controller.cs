using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace FTP_Manager
{
    /// <summary>
    /// FTP account information class
    /// </summary>
    [Serializable]
    public class FTP_Account
    {
        public string host;
        public string username;
        public string password;
    }

    /// <summary>
    /// Enhanced FTP controller with connection pooling and error handling
    /// </summary>
    public static class FTP_Controller
    {
        // Configuration constants
        private const string DEFAULT_CONFIG_PATH = "Assets/FTPaccount.json";
        private const int TIMEOUT_MS = 30000;
        private const int BUFFER_SIZE = 81920; // 80KB buffer
        private const int MAX_CONCURRENT_CONNECTIONS = 5; // Conservative value
        private const int MAX_RETRY_ATTEMPTS = 3;
        private const int RETRY_DELAY_BASE_MS = 1000;

        static FTP_Controller()
        {
            // Configure global connection settings
            ServicePointManager.DefaultConnectionLimit = MAX_CONCURRENT_CONNECTIONS;
            ServicePointManager.Expect100Continue = false;
            ServicePointManager.UseNagleAlgorithm = false;
        }

        /// <summary>
        /// Uploads a local directory to FTP server
        /// </summary>
        public static void UploadDirectory(string localFolderPath, string remoteFolderPath,
                                         string host = null, string username = null, string password = null)
        {
            if (!Directory.Exists(localFolderPath))
                throw new DirectoryNotFoundException($"Local directory not found: {localFolderPath}");

            var account = GetAccountCredentials(host, username, password);
            string fullRemotePath = FormatFtpPath(account.host, remoteFolderPath);

            LogInfo($"Uploading folder: {Path.GetFileName(localFolderPath)}\nFrom: {localFolderPath}\nTo: {fullRemotePath}");

            try
            {
                EnsureRemoteDirectoryExists(fullRemotePath, account);
                UploadDirectoryContents(localFolderPath, fullRemotePath, account);
                LogInfo($"Successfully uploaded folder: {Path.GetFileName(localFolderPath)}");
            }
            catch (Exception ex)
            {
                LogError($"Failed to upload directory. Error: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Async version of directory upload
        /// </summary>
        public static async Task UploadDirectoryAsync(string localFolderPath, string remoteFolderPath,
                                                     string host = null, string username = null, string password = null)
        {
            await Task.Run(() => UploadDirectory(localFolderPath, remoteFolderPath, host, username, password));
        }

        /// <summary>
        /// Uploads a single file to FTP server
        /// </summary>
        public static void UploadFile(string filePath, string remotePath,
                                    string host = null, string username = null, string password = null)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"File not found: {filePath}");

            if (Path.GetExtension(filePath) == ".meta")
            {
                LogInfo($"Skipping .meta file: {filePath}");
                return;
            }

            var account = GetAccountCredentials(host, username, password);
            string fullRemotePath = FormatFtpPath(account.host, remotePath);
            string fileName = Path.GetFileName(filePath);
            string uploadUrl = CombineFtpPaths(fullRemotePath, fileName);

            LogInfo($"Uploading file: {fileName}\nFrom: {filePath}\nTo: {uploadUrl}");

            try
            {
                EnsureRemoteDirectoryExists(fullRemotePath, account);
                PerformFileUploadWithRetry(filePath, uploadUrl, account);
                LogInfo($"Successfully uploaded file: {fileName}");
            }
            catch (Exception ex)
            {
                LogError($"Failed to upload file: {fileName}. Error: {ex.Message}");
                throw;
            }
        }

        #region Core Implementation
        private static void UploadDirectoryContents(string localPath, string remotePath, FTP_Account account)
        {
            // Use semaphore to control concurrent connections
            using (var semaphore = new SemaphoreSlim(MAX_CONCURRENT_CONNECTIONS))
            {
                var uploadTasks = new List<Task>();

                // Process files with controlled parallelism
                foreach (string filePath in Directory.GetFiles(localPath))
                {
                    if (Path.GetExtension(filePath) == ".meta") continue;

                    semaphore.Wait();
                    uploadTasks.Add(Task.Run(() =>
                    {
                        try
                        {
                            string remoteFilePath = $"{remotePath.TrimEnd('/')}/{Path.GetFileName(filePath)}";
                            PerformFileUploadWithRetry(filePath, remoteFilePath, account);
                        }
                        catch (Exception ex)
                        {
                            LogError($"Failed to upload file {Path.GetFileName(filePath)}: {ex.Message}");
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    }));
                }

                // Wait for all file uploads to complete
                Task.WaitAll(uploadTasks.ToArray());

                // Process subdirectories sequentially
                foreach (string subDir in Directory.GetDirectories(localPath))
                {
                    string newRemotePath = $"{remotePath.TrimEnd('/')}/{Path.GetFileName(subDir)}/";
                    EnsureRemoteDirectoryExists(newRemotePath, account);
                    UploadDirectoryContents(subDir, newRemotePath, account);
                }
            }
        }

        private static void PerformFileUploadWithRetry(string localPath, string remoteUrl, FTP_Account account)
        {
            int attempt = 0;
            while (attempt < MAX_RETRY_ATTEMPTS)
            {
                try
                {
                    PerformFileUpload(localPath, remoteUrl, account);
                    return; // Success
                }
                catch (WebException ex) when (IsTransientError(ex))
                {
                    attempt++;
                    if (attempt >= MAX_RETRY_ATTEMPTS)
                    {
                        LogError($"Max retries ({MAX_RETRY_ATTEMPTS}) reached for {Path.GetFileName(localPath)}");
                        throw;
                    }

                    int delay = RETRY_DELAY_BASE_MS * attempt;
                    LogWarning($"Retry {attempt}/{MAX_RETRY_ATTEMPTS} in {delay}ms for {Path.GetFileName(localPath)}");
                    Thread.Sleep(delay);
                }
            }
        }

        private static bool IsTransientError(WebException ex)
        {
            if (ex.Response is FtpWebResponse response)
            {
                // Retry on these status codes
                return response.StatusCode == FtpStatusCode.ServiceNotAvailable ||
                       response.StatusCode == FtpStatusCode.ConnectionClosed ||
                       response.StatusCode == FtpStatusCode.CantOpenData;
            }
            return false;
        }

        private static void PerformFileUpload(string localPath, string remoteUrl, FTP_Account account)
        {
            FtpWebRequest request = CreateFtpRequest(remoteUrl, WebRequestMethods.Ftp.UploadFile, account);

            using (FileStream fileStream = new FileStream(localPath, FileMode.Open, FileAccess.Read, FileShare.Read, BUFFER_SIZE, true))
            using (Stream requestStream = request.GetRequestStream())
            {
                fileStream.CopyTo(requestStream, BUFFER_SIZE);
            }

            using (FtpWebResponse response = (FtpWebResponse)request.GetResponse())
            {
                if (response.StatusCode != FtpStatusCode.ClosingData)
                {
                    throw new WebException($"FTP upload failed with status: {response.StatusCode}");
                }
            }
        }

        private static void EnsureRemoteDirectoryExists(string directoryUrl, FTP_Account account)
        {
            directoryUrl = directoryUrl.TrimEnd('/') + "/";

            if (CheckRemoteDirectoryExists(directoryUrl, account))
                return;

            string parentDirectory = GetParentDirectoryUri(directoryUrl);
            if (!string.IsNullOrEmpty(parentDirectory))
            {
                EnsureRemoteDirectoryExists(parentDirectory, account);
            }

            try
            {
                FtpWebRequest request = CreateFtpRequest(directoryUrl, WebRequestMethods.Ftp.MakeDirectory, account);
                using (FtpWebResponse response = (FtpWebResponse)request.GetResponse())
                {
                    LogInfo($"Created remote directory: {directoryUrl}");
                }
            }
            catch (WebException ex) when ((ex.Response as FtpWebResponse)?.StatusCode == FtpStatusCode.ActionNotTakenFileUnavailable)
            {
                LogInfo($"Directory already exists: {directoryUrl}");
            }
            catch (Exception ex)
            {
                LogError($"Failed to create directory {directoryUrl}: {ex.Message}");
                throw;
            }
        }

        private static bool CheckRemoteDirectoryExists(string directoryUrl, FTP_Account account)
        {
            directoryUrl = directoryUrl.TrimEnd('/') + "/";

            try
            {
                FtpWebRequest request = CreateFtpRequest(directoryUrl, WebRequestMethods.Ftp.ListDirectory, account);
                using (FtpWebResponse response = (FtpWebResponse)request.GetResponse())
                {
                    return true;
                }
            }
            catch (WebException ex) when ((ex.Response as FtpWebResponse)?.StatusCode == FtpStatusCode.ActionNotTakenFileUnavailable)
            {
                return false;
            }
        }
        #endregion

        #region Helper Methods
        private static FTP_Account GetAccountCredentials(string host, string username, string password)
        {
            if (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password))
            {
                return new FTP_Account { host = host, username = username, password = password };
            }

            if (!File.Exists(DEFAULT_CONFIG_PATH))
                throw new FileNotFoundException($"FTP account config not found at: {DEFAULT_CONFIG_PATH}");

                string json = File.ReadAllText(DEFAULT_CONFIG_PATH);
                FTP_Account account = JsonUtility.FromJson<FTP_Account>(json);

                if (account == null)
                    throw new InvalidDataException("Invalid FTP account configuration");

                return account;
            }

        private static FtpWebRequest CreateFtpRequest(string url, string method, FTP_Account account)
        {
            if (!Uri.IsWellFormedUriString(url, UriKind.Absolute))
                throw new UriFormatException($"Invalid FTP URL: {url}");

            FtpWebRequest request = (FtpWebRequest)WebRequest.Create(url);
            request.Method = method;
            request.Credentials = new NetworkCredential(account.username, account.password);
            request.Timeout = TIMEOUT_MS;
            request.UsePassive = true;
            request.UseBinary = true;
            request.KeepAlive = false;
            request.ServicePoint.ConnectionLimit = MAX_CONCURRENT_CONNECTIONS;
            return request;
        }

        private static string FormatFtpPath(string host, string path)
        {
            path = path?.Trim('/') ?? string.Empty;
            return $"{host.TrimEnd('/')}/{path}";
        }

        private static string CombineFtpPaths(string basePath, string relativePath)
        {
            return $"{basePath.TrimEnd('/')}/{relativePath.TrimStart('/')}";
        }

        private static string GetParentDirectoryUri(string ftpUrl)
        {
            var uri = new Uri(ftpUrl);
            string path = uri.AbsolutePath.TrimEnd('/');

            if (string.IsNullOrEmpty(path) || path == "/")
                return null;

            int lastSlash = path.LastIndexOf('/');
            if (lastSlash < 0) return null;

            string parentPath = path.Substring(0, lastSlash);
            return new UriBuilder(uri.Scheme, uri.Host, uri.Port, parentPath).Uri.ToString();
        }

        private static void LogInfo(string message)
        {
            Debug.Log($"[FTP Manager] INFO: {message}");
        }

        private static void LogWarning(string message)
        {
            Debug.LogWarning($"[FTP Manager] WARNING: {message}");
        }

        private static void LogError(string message)
        {
            Debug.LogError($"[FTP Manager] ERROR: {message}");
        }
        #endregion

        #region Additional Features
        /// <summary>
        /// Gets the current connection count (for debugging)
        /// </summary>
        public static void LogConnectionStats()
        {
            Debug.Log($"[FTP Connection Stats] Current connections: {ServicePointManager.DefaultConnectionLimit}");
        }

        /// <summary>
        /// Configures the maximum concurrent connections
        /// </summary>
        public static void SetMaxConnections(int maxConnections)
        {
            if (maxConnections < 1 || maxConnections > 20)
                throw new ArgumentOutOfRangeException("Connection limit should be between 1-20");

            ServicePointManager.DefaultConnectionLimit = maxConnections;
            Debug.Log($"Set maximum concurrent connections to: {maxConnections}");
        }
        #endregion
    }
}