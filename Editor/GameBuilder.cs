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
        string platformName;
        var activeProfile = BuildProfile.GetActiveBuildProfile();

        if (activeProfile == null)
        {
            // Fallback to current active build target
            platformName = EditorUserBuildSettings.activeBuildTarget.ToString();
            UnityEngine.Debug.LogWarning("No active build profile found, using current build target: " + platformName);
        }
        else
        {
            platformName = activeProfile.name;
        }
        CommitAndPushToGit(platformName, PlayerSettings.bundleVersion);
    }

    [PostProcessBuild]
    public static void OnPostprocessBuild(BuildTarget target, string path)
    {
        UnityEngine.Debug.Log("Build completed");
    }

    [MenuItem("FTP/Upload Build to FTP")]
    public static void UploadToFTP()
    {
        string platformName;
        var activeProfile = BuildProfile.GetActiveBuildProfile();

        if (activeProfile == null)
        {
            // Fallback to current active build target
            platformName = EditorUserBuildSettings.activeBuildTarget.ToString();
            UnityEngine.Debug.LogWarning("No active build profile found, using current build target: " + platformName);
        }
        else
        {
            platformName = activeProfile.name;
        }

        string localFolderPath = "Builds/" + platformName + "/";
        string remoteFolderPath = "Builds/" + platformName + "/";

        if (!Directory.Exists(localFolderPath))
        {
            UnityEngine.Debug.LogError($"Builds folder does not exist: {localFolderPath}");
            return;
        }

        FTP_Controller.UploadDirectory(localFolderPath, remoteFolderPath);
        UnityEngine.Debug.Log("Builds uploaded to FTP.");
    }

    private void SetPlatformVar()
    {
        // Load txt from resources
        string platformName;
        var activeProfile = BuildProfile.GetActiveBuildProfile();

        if (activeProfile == null)
        {
            // Fallback to current active build target
            platformName = EditorUserBuildSettings.activeBuildTarget.ToString();
            UnityEngine.Debug.LogWarning("No active build profile found, using current build target: " + platformName);
        }
        else
        {
            platformName = activeProfile.name;
        }
        
        string platformFilePath = Path.Combine(Application.dataPath, "Resources", "platform.txt");
        File.WriteAllText(platformFilePath, platformName);
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