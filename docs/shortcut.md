# 捷徑端（iPhone Shortcuts）

ClipSync 的 iPhone 端是 iOS「捷徑」，名稱 **ClipSync v3.0.2**，共 **78 個動作**。
本文件是從實際捷徑檔解析出來的，動作編號與捷徑 App 裡由上到下的順序一致（註解也算一個動作）。

- 分享連結：https://www.icloud.com/shortcuts/8205c3dfd71549cf9a78eb36c2d4ba07（README.md 第 13 行）
- 原始檔：[ClipSync.shortcut](ClipSync.shortcut)（未簽章 binary plist，由上面連結的 `shortcut` 欄位下載）
- 重新解析：`python scripts/dump_shortcut.py docs/ClipSync.shortcut`
- 更新捷徑後：重新下載檔案、覆蓋 `ClipSync.shortcut`、重跑解析，並更新本文件與 README 連結。

> 取得方式：`https://www.icloud.com/shortcuts/api/records/<連結ID>` 回傳 JSON，其中 `fields.shortcut.value.downloadURL`（把 `${f}` 換成任意檔名）就是未簽章的 plist。

## 變數

| 變數 | 設定處 | 用途 |
|---|---|---|
| `url` | 8、17 | Windows 伺服器位址（如 `http://192.168.x.x:913`） |
| `key` | 9、18 | 配對金鑰，放在 `X-ClipSync-Key` header |
| `InitDict` | 23 | 第一次 `/clipboard/meta` 的回應 |
| `lastKnownUpdatedAt` | 25、74 | 上一次已處理的伺服器 `updatedAt`（判斷電腦端有沒有新內容） |
| `lastUploadedText` | 27、39、72 | 上一次上傳／下載後的手機剪貼簿（判斷文字有沒有變） |
| `lastUploadedSize` | 29、48、73 | 上一次上傳／下載後的手機剪貼簿檔案大小（判斷檔案有沒有變） |
| `currentClip` | 32 | 本輪迴圈讀到的手機剪貼簿 |
| `Extension` | 34 | `currentClip` 的副檔名（純文字時為空） |
| `CurrentSize` | 45 | `currentClip` 的檔案大小 |
| `MetaDict` | 55 | 本輪 `/clipboard/meta` 回應 |
| `NewUpdatedAt` | 57 | 本輪伺服器 `updatedAt` |
| `RemoteType` | 59 | 本輪伺服器內容種類（`text` / `file`） |
| `RemoteFileName` | 61 | 伺服器檔名（**目前沒有任何動作使用**） |

## 動作逐行說明

### A. 初始化與讀取設定（1–19）

| # | 動作 | 內容 |
|---|---|---|
| 1 | 註解 | `init` |
| 2 | 註解 | `get host config` |
| 3 | 建立資料夾 | 在「我的iPhone」捷徑儲存位置建立 `ClipSync` |
| 4 | 取得檔案 | 「我的iPhone」的 `ClipSync` 資料夾下的 `clipsync-config.json`，找不到時**不報錯** |
| 5 | 如果 | 動作 4 的「檔案」**有任何值** |
| 6 | 取得辭典值 | 從「檔案」取 `url` |
| 7 | 取得辭典值 | 從「檔案」取 `key` |
| 8 | 設定變數 | `url` = 6 的結果 |
| 9 | 設定變數 | `key` = 7 的結果 |
| 10 | 否則 | （設定檔不存在 → 進入首次配對） |
| 11 | 掃描 QR／條碼 | 掃 Windows 托盤「ペアリング QR コードを表示」的 QR |
| 12 | 取得文字 | 從條碼取文字（JSON：`{"url":..., "key":...}`，見 `PairingQrCode.cs:12`） |
| 13 | 取得辭典 | 從文字解析成辭典 |
| 14 | 儲存檔案 | 存到與動作 4 相同的位置（「我的iPhone」的 `ClipSync/clipsync-config.json`），**覆寫**，不詢問位置 |
| 15 | 取得辭典值 | 取 `url` |
| 16 | 取得辭典值 | 取 `key` |
| 17 | 設定變數 | `url` |
| 18 | 設定變數 | `key` |
| 19 | 結束如果 | |

### B. 初始基準（20–29）— 首次同步修正就在這裡

| # | 動作 | 內容 |
|---|---|---|
| 20 | 註解 | `first get` |
| 21 | 取得 URL 內容 | `GET {url}/clipboard/meta`，header `X-ClipSync-Key: {key}` |
| 22 | 取得辭典 | 從 URL 內容 |
| 23 | 設定變數 | `InitDict` |
| 24 | 取得辭典值 | `InitDict` 的 `updatedAt` |
| 25 | 設定變數 | `lastKnownUpdatedAt`（先記下電腦端目前版本，所以第一輪不會誤判電腦有新內容） |
| 26 | 取得剪貼簿 | |
| 27 | 設定變數 | `lastUploadedText` = 目前手機剪貼簿（先記下基準，第一輪不會把舊內容當成新複製而上傳） |
| 28 | 取得詳細資料 | 剪貼簿的「檔案大小」 |
| 29 | 設定變數 | `lastUploadedSize` |

### C. 主迴圈（30–78）

| # | 動作 | 內容 |
|---|---|---|
| 30 | 註解 | `loop start` |
| 31 | 重複 | **99999 次**（`repeat.count`） |
| 32 | 設定變數 | `currentClip` = 剪貼簿 |
| 33 | 取得詳細資料 | `currentClip` 的「副檔名」 |
| 34 | 設定變數 | `Extension` |
| 35 | 註解 | `text` |
| 36 | 如果（符合**任一**） | `Extension` **是** `txt`，或 `Extension` **沒有任何值** → 視為文字 |
| 37 | └ 如果（符合**全部**） | `currentClip` **不是** `lastUploadedText`，且 `currentClip` **有任何值** |
| 38 | └└ 取得 URL 內容 | `POST {url}/clipboard`，body = `currentClip`（檔案本文），headers：`X-ClipSync-Key: {key}`、`X-Content-Type: text` |
| 39 | └└ 設定變數 | `lastUploadedText` = `currentClip` |
| 40 | └ 否則 | （空） |
| 41 | └ 結束如果 | |
| 42 | 註解 | `file` |
| 43 | 否則（36 的否則 = 檔案） | |
| 44 | └ 取得詳細資料 | `currentClip` 的「檔案大小」 |
| 45 | └ 設定變數 | `CurrentSize` |
| 46 | └ 如果 | `CurrentSize` **不是** `lastUploadedSize`（單位 B） |
| 47 | └└ 取得 URL 內容 | `POST {url}/clipboard`，body = `currentClip`，headers：`X-ClipSync-Key: {key}`、`X-Content-Type: file`、`X-Extension: {Extension}` |
| 48 | └└ 設定變數 | `lastUploadedSize` = `CurrentSize` |
| 49 | └ 否則 | （空） |
| 50 | └ 結束如果 | |
| 51 | 結束如果 | （36 的結束） |
| 52 | 註解 | `download` |
| 53 | 取得 URL 內容 | `GET {url}/clipboard/meta`，header `X-ClipSync-Key: {key}` |
| 54 | 取得辭典 | |
| 55 | 設定變數 | `MetaDict` |
| 56 | 取得辭典值 | `updatedAt` |
| 57 | 設定變數 | `NewUpdatedAt` |
| 58 | 取得辭典值 | `type` |
| 59 | 設定變數 | `RemoteType` |
| 60 | 取得辭典值 | `fileName` |
| 61 | 設定變數 | `RemoteFileName`（未使用） |
| 62 | 如果 | `NewUpdatedAt` **不是** `lastKnownUpdatedAt`（電腦端有新內容） |
| 63 | └ 註解 | `download file` |
| 64 | └ 取得 URL 內容 | `GET {url}/clipboard/data`，header `X-ClipSync-Key: {key}` |
| 65 | └ 如果 | `RemoteType` **是** `file` |
| 66 | └└ 設定剪貼簿 | 64 的 URL 內容（依回應 `Content-Type` 自動變成圖片／檔案） |
| 67 | └ 否則 | |
| 68 | └└ 註解 | `downalod text`（原文拼字） |
| 69 | └└ 設定剪貼簿 | 64 的 URL 內容（文字） |
| 70 | └ 結束如果 | |
| 71 | └ 取得剪貼簿 | 讀回剛寫入的內容 |
| 72 | └ 設定變數 | `lastUploadedText` = 剛寫入後的剪貼簿 |
| 73 | └ 設定變數 | `lastUploadedSize` = 剛寫入後的剪貼簿檔案大小 |
| 74 | └ 設定變數 | `lastKnownUpdatedAt` = `NewUpdatedAt` |
| 75 | 否則 | （空） |
| 76 | 結束如果 | |
| 77 | 等待 | 沒有設定秒數 → 捷徑預設 **1 秒** |
| 78 | 結束重複 | |

每輪順序固定：**先上傳（手機有新內容才傳）→ 再問電腦有沒有新內容（有才下載）→ 等 1 秒**。

## 捷徑 ↔ Windows 端對照

| 捷徑動作 | 呼叫 | Windows 端程式 |
|---|---|---|
| 11–13 | 配對 QR 內容 `{"url","key"}` | `PairingQrCode.cs:12` `BuildPayload` |
| 21、53 | `GET /clipboard/meta` | `ClipSyncServer.cs:181`；回傳 `ClipboardMetaDto(type, fileName, mimeType, updatedAt)`（`ClipSyncServer.cs:365`，camelCase JSON） |
| 38、47 | `POST /clipboard` | `ClipSyncServer.cs:90–156` |
| 64 | `GET /clipboard/data` | `ClipSyncServer.cs:187–206` |
| 所有請求的 `X-ClipSync-Key` | 驗證 | `ClipSyncServer.cs:78–88`、`IsAuthorized` `:280`（不符回 401） |

### 兩邊必須一致的約定

| 約定 | 捷徑端 | Windows 端 | 改一邊要改另一邊 |
|---|---|---|---|
| 認證 header 名稱 | `X-ClipSync-Key`（21、38、47、53、64） | `AuthHeader` `ClipSyncServer.cs:25` | 捷徑 5 個請求都要改 |
| 內容種類 header | `X-Content-Type: text`／`file`（38、47） | `ContentTypeHeader` `:26`、判斷 `:111、:115`（只有 `"file"` 走檔案，其他都當文字） | 38、47 |
| 副檔名 header | `X-Extension`（47） | `ExtensionHeader` `:28`；決定 MIME `:140` 與檔名 `clipboard.<副檔名>` `:139` | 47 |
| 檔名 header | 捷徑**沒有送** `X-File-Name` | `FileNameHeader` `:27`（有送才覆蓋預設檔名） | 若想自訂檔名，需在 47 加 header |
| meta 欄位名 | 讀 `updatedAt`、`type`、`fileName`（24/56、58、60） | `ClipboardMetaDto` `:365` | 56、58、60 的鍵名 |
| meta `type` 值 | 與 `"file"` 比較（65） | `ToMetaDto` `:290`（只會是 `"file"` 或 `"text"`） | 65 |
| 端點路徑 | `/clipboard/meta`、`/clipboard`、`/clipboard/data` | `MapGet/MapPost` `:90、:181、:187` | 21、38、47、53、64 |
| 下載回應 | 66、69 直接把 URL 內容設為剪貼簿，靠 `Content-Type` 判斷類型 | 文字 `text/plain; charset=utf-8`（`:193`）；檔案 `payload.MimeType`（`:198`） | MIME 表在 `:34–48`，沒列到的副檔名會變 `application/octet-stream` |
| 配對 QR | 12 取文字、13 解析、15/16 取 `url`/`key` | `PairingQrCode.BuildPayload` JSON 鍵名 `url`、`key` | 6、7、15、16 的鍵名 |
| 輪詢間隔 | 77 等待（預設 1 秒） | 無對應 | 只在捷徑端調 |
| port | 由 QR 的 `url` 帶入 | `AppConfig` 的 Port（預設 913） | 不用改捷徑，重新配對即可 |

## Windows 端已有、捷徑尚未使用的端點

`POST /otp`（`ClipSyncServer.cs:160`）：header `X-ClipSync-Key`，body 為 SMS 全文（UTF-8 純文字，上限 8 KB）。Windows 端用 `OtpExtractor` 抽出驗證碼後跳通知，點擊才複製；抓不到驗證碼也回 204。

## 觀察到的細節（不是已確認的 bug，改邏輯前先知道）

- 38、47、53、64 的 `WFJSONValues`（含 `clipboard-image.jepg` 的拼字）是舊版 Base64/JSON 時代的殘留；因為請求本文類型是「檔案」（`WFHTTPBodyType=File`，使用 `currentClip`），實際上不會送出。Windows 端已在 `dda7482` 改為 raw bytes。
- 53、64 這兩個 GET 動作也帶著 `WFRequestVariable={currentClip}` 與本文類型「檔案」，GET 通常不會送 body，但若伺服器行為異常可先檢查這裡。
- 36 的判斷用「副檔名」：副檔名是 `txt` 或空 → 文字；其他（如 `png`、`heic`、`pdf`）→ 檔案。圖片從相簿複製時副檔名通常有值，所以走檔案分支。
- 文字上傳的判斷看「內容有沒有變」（37），檔案則看「大小有沒有變」（46）；兩個不同大小但不同內容的檔案連續複製不會被偵測到，大小相同的檔案也不會重傳。
- 66 與 69 的動作完全相同，檔案與文字分支目前沒有差別。
- 61 的 `RemoteFileName` 沒有被使用。

## 連結沿革（新到舊）

| 日期 | commit | 連結 ID | 備註 |
|---|---|---|---|
| 2026-11-25 | — | `8205c3dfd71549cf9a78eb36c2d4ba07` | 目前版本，名稱仍是 ClipSync v3.0.2；設定檔的讀取與儲存統一為「我的iPhone」同一位置 |
| 2026-10-08 | `6d5aa97` | `53703918479549869180d8c095528262` | 讀取在「我的iPhone」、儲存在 iCloud Drive |
| 2026-10-05 前 | — | `8fd10748b3ca494eaf19d899d190c8c3` | |
| 2026-10-03 | `de8026d` | — | 「初回同期の上書き修正版」：修正首次同步時手機內容覆蓋電腦剛複製內容的問題（B 段的初始基準應是這次的修正，但尚未與舊版比對確認） |
| 2026-10-02 | — | `9f1789c0cf96416c…`、`249ea8c75f0445c3a05a082…` | README 早期版本（ID 尾碼已截斷） |
