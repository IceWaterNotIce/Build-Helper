# Unity Build Helper Package

## 簡介

Unity Build Helper Package 是一個用於簡化 Unity 專案建置和資產管理的工具包。它提供了以下功能：

- 自動化資產包建置 (`AssetBundleBuilder`)。
- FTP 上傳功能，用於將資產包和遊戲建置結果上傳到遠端伺服器。
- 版本管理工具，幫助維護版本資訊 (`version.json`)。
- Git 集成，用於自動提交和推送更改。

## 功能

### 1. 資產包建置 (`AssetBundleBuilder`)

- 支援根據當前平台或自定義建置設定檔進行資產包建置。
- 自動更新 `version.json` 文件，記錄資產包的版本和下載 URL。
- 支援將資產包上傳到 FTP 伺服器。

### 2. FTP 帳戶管理 (`FTPAccountEditor`)

- 提供簡單的 GUI 界面，用於管理 FTP 帳戶資訊。
- 支援保存和加載 FTP 帳戶設定。

### 3. 遊戲建置 (`GameBuilder`)

- 自動化遊戲建置流程，包括版本更新和平台變數設置。
- 支援將遊戲建置結果上傳到 FTP 伺服器。
- 集成 Git，自動提交和推送版本標籤。

## 安裝

1. 將此 Package 複製到 Unity 專案的 `Assets/_Packages` 資料夾中。
2. 確保 `FTPaccount.json` 文件已添加到 `.gitignore`，以避免敏感資訊被提交到版本控制系統。

## 使用方法

### 資產包建置

1. 在 Unity 編輯器中，導航到 `Assets > Build AssetBundles`。
2. 根據當前平台或設定檔進行資產包建置。
3. 資產包將存儲在 `Assets/AssetBundles/<PlatformName>` 資料夾中。

### FTP 帳戶管理

1. 在 Unity 編輯器中，導航到 `Build > FTP Account Edit`。
2. 填寫 FTP 帳戶資訊（Host、Username、Password）。
3. 點擊 `Save` 按鈕保存設定。

### 遊戲建置

1. 在 Unity 編輯器中，導航到 `Build > Upload to FTP`。
2. 確保遊戲建置結果存儲在 `Builds/<PlatformName>` 資料夾中。
3. 點擊按鈕將遊戲建置結果上傳到 FTP。

## 注意事項

- 確保 `FTPaccount.json` 文件已正確配置，並且不會被提交到版本控制系統。
- 使用前請檢查 FTP 帳戶設定是否正確。
- 確保 Git 已正確配置，並且當前分支為 `main`。

## 聯絡方式

如有任何問題，請聯繫開發者或提交 Issue。
