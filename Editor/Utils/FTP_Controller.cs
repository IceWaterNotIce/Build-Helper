using System;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using UnityEngine;

namespace FTP_Manager
{
    /// <summary>
    /// FTP account information class (sealed for security)
    /// </summary>
    [Serializable]
    public class FTP_Account
    {
        public string host; // 修改為 public
        public string username; // 修改為 public
        public string password; // 修改為 public
    }

    /// <summary>
    /// FTP controller with enhanced security and error handling
    /// </summary>
    public static class FTP_Controller
    {
        private const string DEFAULT_CONFIG_PATH = "Assets/FTPaccount.json";
        private const int TIMEOUT_MS = 30000;
        private const int BUFFER_SIZE = 81920; // 80KB buffer

        /// <summary>
        /// Uploads a local directory to FTP server (async version available)
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
                PerformFileUpload(filePath, uploadUrl, account);
                LogInfo($"Successfully uploaded file: {fileName}");
            }
            catch (Exception ex)
            {
                LogError($"Failed to upload file: {fileName}. Error: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Async version of file upload
        /// </summary>
        public static async Task UploadFileAsync(string filePath, string remotePath,
                                               string host = null, string username = null, string password = null)
        {
            await Task.Run(() => UploadFile(filePath, remotePath, host, username, password));
        }

        #region Core Implementation
        private static void UploadDirectoryContents(string localPath, string remotePath, FTP_Account account)
        {
            // Upload files in parallel with thread-safe error handling
            Parallel.ForEach(Directory.GetFiles(localPath), filePath =>
            {
                if (Path.GetExtension(filePath) != ".meta")
                {
                    try
                    {
                        string fileName = Path.GetFileName(filePath);
                        string fileRemotePath = CombineFtpPaths(remotePath, fileName);
                        PerformFileUpload(filePath, fileRemotePath, account);
                    }
                    catch (Exception ex)
                    {
                        LogError($"Failed to upload file {Path.GetFileName(filePath)}: {ex.Message}");
                    }
                }
            });

            // Process subdirectories sequentially
            foreach (string subDir in Directory.GetDirectories(localPath))
            {
                string newRemotePath = CombineFtpPaths(remotePath, Path.GetFileName(subDir));
                EnsureRemoteDirectoryExists(newRemotePath, account);
                UploadDirectoryContents(subDir, newRemotePath, account);
            }
        }

        private static void PerformFileUpload(string localPath, string remoteUrl, FTP_Account account)
        {
            FtpWebRequest request = CreateFtpRequest(remoteUrl, WebRequestMethods.Ftp.UploadFile, account);

            using (FileStream fileStream = new FileStream(localPath, FileMode.Open, FileAccess.Read, FileShare.Read, BUFFER_SIZE, true))
            using (Stream requestStream = request.GetRequestStream())
            {
                fileStream.CopyTo(requestStream, BUFFER_SIZE);
            }

            // Verify upload completed successfully
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
        }

        private static bool CheckRemoteDirectoryExists(string directoryUrl, FTP_Account account)
        {
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
            request.UsePassive = true; // Better compatibility with firewalls
            request.KeepAlive = false; // Prevent connection pooling issues
            request.UseBinary = true;

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

            int lastSlash = path.LastIndexOf('/');
            if (lastSlash <= 0) return null;

            string parentPath = path.Substring(0, lastSlash + 1);
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
    }
}