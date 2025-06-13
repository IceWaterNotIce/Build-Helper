using System.IO;
using System.Net;
using System;
using UnityEngine;

namespace FTP_Manager
{
    /// <summary>
    /// FTP 账户信息类，用于存储主机、用户名和密码。
    /// </summary>
    public class FTP_Account
    {
        public string host;
        public string username;
        public string password;
    }

    /// <summary>
    /// FTP 控制器类，提供上传文件、目录以及管理 FTP 目录的方法。
    /// </summary>
    public class FTP_Controller
    {
        /// <summary>
        /// 上传本地文件夹到 FTP 服务器上的指定路径。
        /// </summary>
        /// <param name="localFolderPath">本地文件夹路径</param>
        /// <param name="RemoteFolderPath">FTP 服务器上的目标文件夹路径</param>
        /// <param name="host">FTP 主机（可选）</param>
        /// <param name="username">FTP 用户名（可选）</param>
        /// <param name="password">FTP 密码（可选）</param>
        public static void UploadDirectory(string localFolderPath, string RemoteFolderPath, string host = null, string username = null, string password = null)
        {
            // 如果未提供用户名或密码，则从配置文件加载
            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                string accountFilePath = "Assets/FTPaccount.json";
                if (!File.Exists(accountFilePath))
                {
                    UnityEngine.Debug.LogError("account.json not found");
                    return;
                }
                string json = File.ReadAllText(accountFilePath);
                FTP_Account account = JsonUtility.FromJson<FTP_Account>(json) ?? new FTP_Account();
                username = account.username;
                password = account.password;
                host = account.host;
            }

            Debug.Log($"Uploading Folder : {Path.GetFileName(localFolderPath)}\n {localFolderPath}\n To FTP server:\n {host}{RemoteFolderPath}");

            // 确保远程文件夹存在
            try
            {
                CreateFtpDirectory($"{host}{RemoteFolderPath}", username, password);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Failed to ensure remote folder exists: {RemoteFolderPath}. Error: {ex.Message}");
            }

            // 上传目录中的所有文件
            foreach (string filePath in Directory.GetFiles(localFolderPath))
            {
                // 跳过 .meta 文件
                if (Path.GetExtension(filePath) == ".meta")
                {
                    Debug.Log($"Skipping .meta file: {filePath}");
                    continue;
                }

                UploadFile(filePath, $"{host}{RemoteFolderPath}", username, password);
            }

            Debug.Log($"Uploaded  Folder : {Path.GetFileName(localFolderPath)}");

            // 递归上传子目录
            foreach (string directoryPath in Directory.GetDirectories(localFolderPath))
            {
                string newFtpUrl = $"{host}{RemoteFolderPath}{Path.GetFileName(directoryPath)}/";

                // 确保子目录在 FTP 服务器上存在
                try
                {
                    CreateFtpDirectory(newFtpUrl, username, password);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"Failed to ensure remote subdirectory exists: {newFtpUrl}. Error: {ex.Message}");
                }

                // 递归上传子目录
                UploadDirectory(directoryPath, newFtpUrl, username, password);
            }
        }

        /// <summary>
        /// 上传单个文件到 FTP 服务器。
        /// </summary>
        /// <param name="filePath">本地文件路径</param>
        /// <param name="ftpUrl">FTP 服务器上的目标路径</param>
        /// <param name="username">FTP 用户名</param>
        /// <param name="password">FTP 密码</param>
        public static void UploadFile(string filePath, string ftpUrl, string username, string password)
        {
            string fileName = Path.GetFileName(filePath);
            string uploadUrl = $"{ftpUrl}{fileName}";

            Debug.Log($"Uploading file : {fileName}\n {filePath}\n To FTP server:\n {uploadUrl}");

            // 确保目录在 FTP 服务器上存在
            string directoryUrl = ftpUrl.TrimEnd('/');
            try
            {
                CreateFtpDirectory(directoryUrl, username, password);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Failed to ensure directory exists: {directoryUrl}. Error: {ex.Message}");
            }

            try
            {
                FtpWebRequest request = (FtpWebRequest)WebRequest.Create(uploadUrl);
                request.Method = WebRequestMethods.Ftp.UploadFile;
                request.Credentials = new NetworkCredential(username, password);

                using (FileStream fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                using (Stream requestStream = request.GetRequestStream())
                {
                    fileStream.CopyTo(requestStream);
                }

                Debug.Log($"Uploaded file : {fileName}");
            }
            catch (WebException ex)
            {
                Debug.LogError($"Failed to upload file: {fileName}. Error: {ex.Message}");
            }
        }

        /// <summary>
        /// 在 FTP 服务器上创建目录（如果不存在）。
        /// </summary>
        /// <param name="ftpUrl">要创建的 FTP 目录 URL</param>
        /// <param name="username">FTP 用户名</param>
        /// <param name="password">FTP 密码</param>
        public static void CreateFtpDirectory(string ftpUrl, string username, string password)
        {
            string parentDirectory = GetParentDirectoryUri(ftpUrl);
            if (!string.IsNullOrEmpty(parentDirectory))
            {
                // 检查父目录是否存在
                if (!CheckRemoteDirectoryExists(parentDirectory, username, password))
                {
                    Debug.Log($"Parent directory does not exist: {parentDirectory}. Checking higher-level directories...");
                    // 递归确保父目录存在
                    CreateFtpDirectory(parentDirectory, username, password);
                }
            }

            Debug.Log($"Attempting to create directory: {ftpUrl}");
            try
            {
                FtpWebRequest request = (FtpWebRequest)WebRequest.Create(ftpUrl);
                request.Method = WebRequestMethods.Ftp.MakeDirectory;
                request.Credentials = new NetworkCredential(username, password);

                using (FtpWebResponse response = (FtpWebResponse)request.GetResponse())
                {
                    Debug.Log($"Directory created: {ftpUrl}");
                }
            }
            catch (WebException ex)
            {
                if (ex.Response is FtpWebResponse ftpResponse && ftpResponse.StatusCode == FtpStatusCode.ActionNotTakenFileUnavailable)
                {
                    Debug.Log($"Directory already exists: {ftpUrl}");
                }
                else
                {
                    Debug.LogWarning($"Failed to create directory: {ftpUrl}. Error: {ex.Message}");
                    throw;
                }
            }
        }

        /// <summary>
        /// 获取 FTP 目录的父目录 URI。
        /// </summary>
        /// <param name="ftpUrl">FTP 目录 URL</param>
        /// <returns>父目录的 URI，如果没有父目录则返回 null</returns>
        private static string GetParentDirectoryUri(string ftpUrl)
        {
            Uri uri = new Uri(ftpUrl);
            string path = uri.AbsolutePath;
            if (path.EndsWith("/"))
            {
                path = path.Substring(0, path.Length - 1);
            }
            int lastSlashIndex = path.LastIndexOf('/');
            if (lastSlashIndex >= 0)
            {
                string parentPath = path.Substring(0, lastSlashIndex + 1);
                return new Uri(uri.Scheme + "://" + uri.Host + parentPath).ToString();
            }
            else
            {
                return null;
            }
        }

        /// <summary>
        /// 检查 FTP 服务器上的目录是否存在。
        /// </summary>
        /// <param name="directoryUrl">FTP 目录 URL</param>
        /// <param name="username">FTP 用户名</param>
        /// <param name="password">FTP 密码</param>
        /// <returns>如果目录存在则返回 true，否则返回 false</returns>
        private static bool CheckRemoteDirectoryExists(string directoryUrl, string username, string password)
        {
            Debug.Log($"Checking if directory exists: {directoryUrl}");
            try
            {
                FtpWebRequest request = (FtpWebRequest)WebRequest.Create(directoryUrl);
                request.Method = WebRequestMethods.Ftp.ListDirectory;
                request.Credentials = new NetworkCredential(username, password);

                using (FtpWebResponse response = (FtpWebResponse)request.GetResponse())
                using (Stream responseStream = response.GetResponseStream())
                using (StreamReader reader = new StreamReader(responseStream))
                {
                    reader.ReadToEnd();
                }
                return true;
            }
            catch (WebException ex)
            {
                if (ex.Response is FtpWebResponse ftpResponse && ftpResponse.StatusCode == FtpStatusCode.ActionNotTakenFileUnavailable)
                {
                    return false;
                }
                Debug.LogWarning($"Error checking if directory exists: {directoryUrl}. Error: {ex.Message}");
                throw;
            }
        }
    }
}