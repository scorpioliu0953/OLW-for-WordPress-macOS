# OLW for WordPress - macOS 版

專為 WordPress 打造的桌面部落格編輯器，macOS 原生應用程式。

## 功能特色

- 透過 WordPress REST API 連線（需要 WordPress 5.6 以上版本）
- 使用「應用程式密碼」安全認證
- 文章撰寫與編輯（支援 HTML）
- 文章分類與標籤管理
- 圖片上傳
- 草稿 / 發布 / 排程 / 私密文章
- 支援 Intel 與 Apple Silicon Mac

## 下載

| 版本 | 下載連結 |
|------|---------|
| Intel Mac (x86_64) | [OLWforWordPress-x64.dmg](https://github.com/scorpioliu0953/OLW-for-WordPress-macOS/releases/latest/download/OLWforWordPress-x64.dmg) |
| Apple Silicon (M1/M2/M3/M4) | [OLWforWordPress-arm64.dmg](https://github.com/scorpioliu0953/OLW-for-WordPress-macOS/releases/latest/download/OLWforWordPress-arm64.dmg) |

## 使用方式

1. 下載對應您 Mac 架構的 DMG 檔案
2. **首次下載需解除 macOS 安全限制**（因為應用程式未經 Apple 簽署）：
   ```bash
   xattr -rd com.apple.quarantine ~/Downloads/OLWforWordPress-arm64.dmg
   ```
   Intel 版請將檔名改為 `OLWforWordPress-x64.dmg`
3. 開啟 DMG，將「OLW for WordPress」拖曳至「應用程式」資料夾
4. 如果仍顯示「已損毀」，請再對應用程式執行：
   ```bash
   xattr -rd com.apple.quarantine "/Applications/OLW for WordPress.app"
   ```
5. 首次開啟時輸入 WordPress 網站網址、使用者名稱及應用程式密碼
6. 連線成功後即可開始撰寫文章

### 如何取得應用程式密碼

1. 登入 WordPress 後台
2. 前往「使用者」→「個人資料」
3. 捲動到「應用程式密碼」區塊
4. 輸入名稱（例如「OLW」）後點選「新增應用程式密碼」
5. 複製產生的密碼

## 技術架構

- .NET 8 + Avalonia UI 11
- WordPress REST API (`/wp-json/wp/v2/`)
- 跨平台 MVVM 架構

## 相關專案

- [OLW for WordPress (Windows 版)](https://github.com/scorpioliu0953/OLW-for-WordPress) - Windows 桌面版

## 授權

MIT License
