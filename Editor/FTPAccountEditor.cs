using UnityEditor;
using UnityEngine;
using System.IO;





namespace BuildHelper
{
    public class FTPAccountEditor : EditorWindow
    {
        private string host = "";
        private string username = "";
        private string password = "";
        private string accountFilePath = "Assets/FTPaccount.json";

        [MenuItem("FTP/FTP Account Edit")]
        public static void ShowWindow()
        {
            GetWindow<FTPAccountEditor>("FTP Account Editor");
        }

        private void OnEnable()
        {
            LoadAccount();
        }

        private void OnGUI()
        {
            GUILayout.Label("FTP Account Settings", EditorStyles.boldLabel);

            host = EditorGUILayout.TextField("Host", host);
            username = EditorGUILayout.TextField("Username", username);
            password = EditorGUILayout.PasswordField("Password", password);

            GUILayout.Space(10);

            if (GUILayout.Button("Save"))
            {
                SaveAccount();
            }

            if (GUILayout.Button("Load"))
            {
                LoadAccount();
            }

            GUILayout.Space(10);
            EditorGUILayout.HelpBox("Reminder: Ensure your FTP settings are correct and verified in your FTP GUI tool.", MessageType.Info);
        }

        private void SaveAccount()
        {
            FTP_Account account = new FTP_Account
            {
                host = host,
                username = username,
                password = password
            };

            string json = JsonUtility.ToJson(account, true);

            string directory = Path.GetDirectoryName(accountFilePath);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(accountFilePath, json);
            Debug.Log($"FTP account saved to {accountFilePath}");
        }

        private void LoadAccount()
        {
            if (File.Exists(accountFilePath))
            {
                string json = File.ReadAllText(accountFilePath);
                FTP_Account account = JsonUtility.FromJson<FTP_Account>(json);

                host = account.host;
                username = account.username;
                password = account.password;

                Debug.Log("FTP account loaded.");
            }
            else
            {
                Debug.LogWarning($"FTP account file not found at {accountFilePath}. A new file will be created upon saving.");
            }
        }
    }

}
