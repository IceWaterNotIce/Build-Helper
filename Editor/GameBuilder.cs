using UnityEngine;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using System.Diagnostics;
using System.IO;
using System;
using UnityEditor.Build.Profile;
using System.Collections.Generic;
using System.Net;
using UnityEditor.Callbacks;
using FTP_Manager;

[InitializeOnLoad]
public class GameBuilder : IPreprocessBuildWithReport
{
    public int callbackOrder => 0;
    public void OnPreprocessBuild(BuildReport report)
    {
        SetPlatformVar();
        UpdateVersion();
        CommitAndPushToGit(BuildProfile.GetActiveBuildProfile().name, PlayerSettings.bundleVersion);
    }

    [PostProcessBuild]
    public static void OnPostprocessBuild(BuildTarget target, string path)
    {
        UnityEngine.Debug.Log("Build completed");
    }


    [MenuItem("Build/Upload to FTP")]
    public static void UploadToFTP()
    {
        // 從 editor/account.json 獲取帳戶信息
        string accountFilePath = "Assets/Editor/Utils/FTPaccount.json";
        if (!File.Exists(accountFilePath))
        {
            UnityEngine.Debug.LogError("FTP account file not found.");
            return;
        }

        string json = File.ReadAllText(accountFilePath);
        FTP_Account account = JsonUtility.FromJson<FTP_Account>(json) ?? new FTP_Account();
        string localFolderPath = "Builds/" + BuildProfile.GetActiveBuildProfile().name + "/";
        string ftpUrl = BuildProfile.GetActiveBuildProfile().name + "/";

        if (!Directory.Exists(localFolderPath))
        {
            UnityEngine.Debug.Log("Game build folder does not exist.");
            return;
        }

        // 開始遞歸上傳文件和文件夾
        try
        {
            UnityEngine.Debug.Log("ftpurl: " + ftpUrl);
            FTP_Controller.UploadDirectory(localFolderPath, ftpUrl, account.username, account.password);
            UnityEngine.Debug.Log("Game files uploaded to FTP");
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.Log($"Error: {ex.Message}");
        }
    }

    
    private void SetPlatformVar()
    {
        // load txt from resources
        string platform = BuildProfile.GetActiveBuildProfile().name;
        string platformFilePath = Path.Combine(Application.dataPath, "Resources", "platform.txt");
        File.WriteAllText(platformFilePath, platform);
    }

    private static void UpdateVersion()
    {
        var activeProfile = BuildProfile.GetActiveBuildProfile();
        var currentBuildTarget = activeProfile.name;
        UnityEngine.Debug.Log("Current Build Target: " + currentBuildTarget);
        string versionFilePath = Path.Combine(Application.streamingAssetsPath, "version.json");
        if (File.Exists(versionFilePath))
        {
            string json = File.ReadAllText(versionFilePath);
            VersionConfig versionData = JsonUtility.FromJson<VersionConfig>(json);
            foreach (var platform in versionData.platforms)
            {
                if (platform.name == currentBuildTarget)
                {
                    string[] versionInfoParts = PlayerSettings.bundleVersion.Split('.');
                    if (versionInfoParts.Length == 3)
                    {
                        if (int.TryParse(versionInfoParts[2], out int patchVersion))
                        {
                            patchVersion++;
                            platform.latestVersion = $"{versionInfoParts[0]}.{versionInfoParts[1]}.{patchVersion}";
                            UnityEngine.Debug.Log($"Version updated to {platform.latestVersion}");
                        }
                        else
                        {
                            UnityEngine.Debug.LogError("Patch version is not a number.");
                        }
                    }
                    else
                    {
                        UnityEngine.Debug.LogError("Version format is not correct. It should be like 1.0.0");
                    }

                    File.WriteAllText(versionFilePath, JsonUtility.ToJson(versionData));
                    break;
                }
            }


        }

        string[] versionParts = PlayerSettings.bundleVersion.Split('.');
        if (versionParts.Length == 3)
        {
            if (int.TryParse(versionParts[2], out int patchVersion))
            {
                patchVersion++;
                PlayerSettings.bundleVersion = $"{versionParts[0]}.{versionParts[1]}.{patchVersion}";
                UnityEngine.Debug.Log($"Version updated to {PlayerSettings.bundleVersion}");
            }
            else
            {
                UnityEngine.Debug.LogError("Patch version is not a number.");
            }
        }
        else
        {
            UnityEngine.Debug.LogError("Version format is not correct. It should be like 1.0.0");
        }
    }

    private static void CommitAndPushToGit(string platform, string versionParts)
    {
        RunGitCommand("git add .");
        RunGitCommand("git commit -m \"Auto commit from Unity Builder. \"");
        RunGitCommand("git tag -a " + platform + "v" + versionParts + " -m \"Auto tag from Unity Builder. \"");
        RunGitCommand("git push origin main");
        RunGitCommand("git push origin v" + versionParts);

        UnityEngine.Debug.Log("Git commit and push done");
    }

    private static void RunGitCommand(string command)
    {
        ProcessStartInfo processStartInfo = new ProcessStartInfo("cmd.exe", "/c " + command);
        processStartInfo.WorkingDirectory = Application.dataPath;
        processStartInfo.RedirectStandardOutput = true;
        processStartInfo.UseShellExecute = false;
        processStartInfo.CreateNoWindow = false;

        using (Process process = Process.Start(processStartInfo))
        {
            process.WaitForExit();
            UnityEngine.Debug.Log(process.StandardOutput.ReadToEnd());
        }
    }



    [System.Serializable]
    public class VersionConfig
    {
        [System.Serializable]
        public class VersionInfo
        {
            public string name;
            public string latestVersion;
            public string downloadURL;
        }
        public List<VersionInfo> platforms;
    }
}