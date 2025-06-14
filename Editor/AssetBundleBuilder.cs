using UnityEditor;
using UnityEngine;
using System.Diagnostics;
using System.IO;
using System.Collections.Generic;
using UnityEditor.Build.Profile;


namespace BuildHelper
{
    // Add VersionConfig definition if missing
    [System.Serializable]
    public class VersionConfig
    {
        [System.Serializable]
        public class AssetBundleInfo
        {
            public string name;
            public string version;
            public string url;
        }

        public List<AssetBundleInfo> bundles = new List<AssetBundleInfo>();
    }

    public class AssetBundleBuilder
    {

        [MenuItem("Assets/Build AssetBundles")]
        public static void BuildAllAssetBundles()
        {

            // Get active build profile or use default platform
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

            // create directory if not exist
            if (!Directory.Exists("Assets/AssetBundles/" + platformName))
            {
                Directory.CreateDirectory("Assets/AssetBundles/" + platformName);
            }

            // filter current platform asset bundles
            string[] assetBundles = AssetDatabase.GetAllAssetBundleNames();
            List<string> filted_assetBundles = new List<string>();
            foreach (string assetBundle in assetBundles)
            {
                if (assetBundle.Contains(".all") || (activeProfile != null && assetBundle.Contains(activeProfile.name)))
                {
                    filted_assetBundles.Add(assetBundle);
                }
            }
            AssetBundleBuild[] buildMap = new AssetBundleBuild[filted_assetBundles.Count];

            for (int i = 0; i < filted_assetBundles.Count; i++)
            {
                buildMap[i].assetBundleName = filted_assetBundles[i];
                buildMap[i].assetNames = AssetDatabase.GetAssetPathsFromAssetBundle(filted_assetBundles[i]);
            }

            // build asset bundles
            BuildPipeline.BuildAssetBundles(
                "Assets/AssetBundles/" + (activeProfile != null ? activeProfile.name : platformName),
                buildMap,
                BuildAssetBundleOptions.None,
                EditorUserBuildSettings.activeBuildTarget
            );

            // update version.json and push to git
            UpdateVersionJson();
            CommitAndPushToGit();
            //
        }

        private static void OnAssetBundlesBuilt(string outputPath)
        {
            UnityEngine.Debug.Log("Asset Bundles built successfully at: " + outputPath);

            // Add your additional logic here
            // For example, you could:
            // - Move files
            // - Update an index
            // - Notify other systems
        }
        private static void UpdateVersionJson()
        {
            // Update the file path to the new location
            string accountFilePath = "Assets/FTPaccount.json";
            if (!File.Exists(accountFilePath))
            {
                UnityEngine.Debug.LogError($"FTP account file not found at {accountFilePath}. Please ensure the file exists and contains valid FTP account information.");
                return;
            }

            string accountjson = File.ReadAllText(accountFilePath);
            FTP_Account account = JsonUtility.FromJson<FTP_Account>(accountjson) ?? new FTP_Account();
            string ftpUrl = account.host + "AssetBundles/";

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

            // local version.json
            string versionFilePath = "Assets/AssetBundles/" + platformName + "/version.json";
            string versionDirectory = Path.GetDirectoryName(versionFilePath);

            // Ensure the directory exists
            if (!Directory.Exists(versionDirectory))
            {
                Directory.CreateDirectory(versionDirectory);
            }

            if (!File.Exists(versionFilePath))
            {
                File.Create(versionFilePath).Close();
                File.WriteAllText(versionFilePath, JsonUtility.ToJson(new VersionConfig()));
            }
            string json = File.ReadAllText(versionFilePath);
            VersionConfig versionData = JsonUtility.FromJson<VersionConfig>(json) ?? new VersionConfig();

            // filter current platform asset bundles
            string[] assetBundles = AssetDatabase.GetAllAssetBundleNames();
            List<string> filted_assetBundles = new List<string>();
            foreach (string assetBundle in assetBundles)
            {
                if (assetBundle.Contains(".all") || (BuildProfile.GetActiveBuildProfile() != null && assetBundle.Contains(platformName)))
                {
                    filted_assetBundles.Add(assetBundle);
                }
            }

            // Add new bundle to versionData.bundles
            if (versionData.bundles == null)
            {
                versionData.bundles = new List<VersionConfig.AssetBundleInfo>();
            }
            foreach (var bundle in filted_assetBundles)
            {
                if (versionData.bundles.Find(b => b.name == bundle) == null)
                {
                    versionData.bundles.Add(new VersionConfig.AssetBundleInfo
                    {
                        name = bundle,
                        version = "1.0",
                        url = ftpUrl + platformName + "/" + bundle
                    });
                }
            }

            // Remove deleted bundle from versionData.bundles
            foreach (var bundle in versionData.bundles)
            {
                if (filted_assetBundles.Find(b => b == bundle.name) == null)
                {
                    versionData.bundles.Remove(bundle);
                }
            }

            // Update version number
            foreach (var bundle in versionData.bundles)
            {
                //version format is 1.0
                bundle.version = (float.Parse(bundle.version) + 0.1f).ToString();
                UnityEngine.Debug.Log(bundle.name + " version updated to " + bundle.version);
            }

            File.WriteAllText(versionFilePath, JsonUtility.ToJson(versionData));
            UnityEngine.Debug.Log("Version.json updated");
        }


        private static void CommitAndPushToGit()
        {
            RunCommand("git add .");
            RunCommand("git commit -m \"Auto commit from Unity Asset Bundle Builder\"");
            RunCommand("git push origin main");

            UnityEngine.Debug.Log("Asset Bundles pushed to git");
        }
        private static void UploadToFTP()
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

            string localFolderPath = "Assets/AssetBundles/" + platformName + "/";
            string remoteFolderPath = "AssetBundles/" + platformName + "/";
            FTP_Controller.UploadDirectory(localFolderPath, remoteFolderPath);
            UnityEngine.Debug.Log("Asset Bundles uploaded to FTP");
        }

        private static void RunCommand(string command)
        {
            ProcessStartInfo processStartInfo = new ProcessStartInfo("cmd.exe", "/c " + command);
            processStartInfo.WorkingDirectory = Application.dataPath;
            processStartInfo.RedirectStandardOutput = true;
            processStartInfo.UseShellExecute = false;
            processStartInfo.CreateNoWindow = false;

            using (Process process = Process.Start(processStartInfo))
            {
                process.WaitForExit();
            }
        }

        [MenuItem("FTP/Upload AssetBundle to FTP")]
        public static void UploadAssetBundleToFTP()
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

            string localFolderPath = "Assets/AssetBundles/" + platformName + "/";
            string remoteFolderPath = "AssetBundles/" + platformName + "/";

            if (!Directory.Exists(localFolderPath))
            {
                UnityEngine.Debug.LogError($"AssetBundle folder does not exist: {localFolderPath}");
                return;
            }

            FTP_Controller.UploadDirectory(localFolderPath, remoteFolderPath);
            UnityEngine.Debug.Log("Asset Bundles uploaded to FTP.");
        }

    }
}