<img width="1024" height="559" alt="image" src="https://github.com/user-attachments/assets/7c50c0c4-789d-4f03-ba4c-6f79380e16fd" />
(the icon By Gemini)

# CatMin Launcher 🐱⛏️

一個基於 C# WPF 與 WPF UI 打造的現代化、輕量級 Minecraft 啟動器。擁有流暢的毛玻璃介面、多版本管理、Modrinth 資源搜尋，以及一鍵安裝整合包等功能

![.NET](https://img.shields.io/badge/.NET-9.0-blue)
![WPF](https://img.shields.io/badge/WPF-UI-purple)
![License](https://img.shields.io/badge/License-MIT-green)

## 功能特色 (Features)

*   **現代化 UI**：基於 `WPF UI`  與 Mica 毛玻璃效果，支援暗黑模式、自訂壁紙與流暢的頁面切換動畫
*   **多帳號管理**：支援微軟正版 OAuth 登入，以及多個離線帳號的儲存與快速切換
*   **核心下載引擎**：基於 `CmlLib.Core`，支援原版遊戲、Fabric、Forge 的下載與自動安裝
*   **Modrinth 整合**：內建 Mod 搜尋引擎，可直接預覽圖片並下載 `.jar` 到 `mods` 資料夾
*   **一鍵整合包**：支援將 `.mrpack` (Modrinth) 檔案直接**拖拽**到視窗，自動解析並下載所有依賴模組
*   **版本隔離**：支援啟用版本隔離，讓不同版本的 Mod 互不干擾
*   **崩潰分析**：啟動失敗時，自動讀取最新的 `crash-reports` 或 `latest.log` 提示玩家可能的原因
*   **目錄自由**：支援自訂 Minecraft 根目錄，無縫接管官方啟動器或其他啟動器的資料夾（不複製檔案）

## 介面截圖


<img width="1250" height="780" alt="image" src="https://github.com/user-attachments/assets/6706f8be-419e-4ccd-9072-bb9fd513d5c4" />
<img width="1250" height="780" alt="image" src="https://github.com/user-attachments/assets/4bfa5426-4b24-44fb-9fe0-af8cbd296400" />
<img width="1250" height="780" alt="image" src="https://github.com/user-attachments/assets/21bd0460-6638-4e8f-b7be-81bf5f946b75" />
<img width="1250" height="780" alt="image" src="https://github.com/user-attachments/assets/55d32ff8-af6b-4906-b085-4ed37457213e" />
<img width="1250" height="780" alt="image" src="https://github.com/user-attachments/assets/06ca58fa-2214-45f6-82d5-717392bac2e1" />

## 如何使用

至
```
https://github.com/Wayne27304/CatMin-Launcher/releases 
```
下載安裝


## 已知錯誤與限制

目前版本存在以下已知問題，歡迎有興趣的開發者發起 PR 協助修復：

1.  **下載衝突 (`Already installing`)**：若在下載未完成時快速切換頁面並重複點擊下載，`CmlLib.Core` 會拋出異常。目前已加入 UI 防呆鎖，但底層尚未實作多執行緒任務佇列
2.  **僅支援 Modrinth 整合包**：目前僅支援解析拖拽進來的 `.mrpack` 檔案，尚未支援 CurseForge 的 `.zip` 整合包格式
3.  **Java 版本限制**：目前依賴 `CmlLib.Core` 自動尋找 Java，若系統中缺失對應的 Java（如 1.17+ 需要 Java 17），可能會啟動失敗。尚未實作「一鍵下載 Java」功能
4.  **UI 縮放問題**：在部分高 DPI (如 4K) 螢幕或特殊解析度下，部分控件可能會有輕微模糊（WPF 的已知限制）
5.  **下載進度條**：目前的下載進度是基於「檔案數量」而非「檔案大小」，因此大檔案下載時進度條可能會有跳躍感
6.  **部分按鈕未實作**：如「NeoForge / Quilt 安裝」目前僅保留 UI 入口，尚未串接對應的安裝邏輯

## 特別致謝

*   **deepseek**：本專案的誕生離不開 AI 的全程陪伴。從 C# 語法指導、UI 設計 (WPF UI)、API 串接 (CmlLib.Core、Modrinth)，到無數次的 Bug 修復與套件打包，皆由開發者與 AI 共同協作完成
*   [CmlLib.Core](https://github.com/CmlLib/CmlLib.Core) - 強大的 Minecraft 啟動核心庫
*   [WPF UI](https://github.com/lepoco/wpfui) - 微軟 Fluent Design 的 WPF 實作
*   [Modrinth API](https://docs.modrinth.com/) - 開放模組與整合包數據
*   [Inno Setup](https://jrsoftware.org/isinfo.php) - 安裝包製作工具

## 開源協議

本專案採用 [MIT License](LICENSE) 開源

你可以自由修改、分發或用於商業用途，但請保留原作者版權聲明。
