# ClipSync

Windows と iPhone の間でクリップボードをリアルタイムに同期する小さなツールです。テキスト・画像・任意のファイルに対応し、ローカルネットワーク経由で直接転送します。

## ダウンロード

### Windows

[最新の Release をダウンロード](https://github.com/vaz1306011/ClipSync/releases/latest)

### iPhone

[Shortcuts ショートカットを開く](https://www.icloud.com/shortcuts/f55c3e0c46c745fdbeb86d426d88a71f)

Safari でリンクを開き、「ショートカットを追加」をタップして保存します。下の QR コードを iPhone のカメラで読み取ってもリンクを開けます。

<img src="docs/shortcut-qr.png" alt="ショートカットのダウンロード用 QR コード" width="200">

## 使い方

1. 上記リンクから最新の Release から `ClipSync.WindowsApp.exe` をダウンロードして起動する（システムトレイに常駐する）
2. iPhone にショートカットを追加する
3. ショートカットを初めて実行すると QR コードのスキャンを求められるので、Windows のトレイメニューから「ペアリング QR コードを表示」を開いてスキャンする（サーバー URL とキーが自動保存され、以降は不要）
4. ショートカットはループ処理になっており、実行中は数秒おきに双方のクリップボードを比較して自動的に同期する
   - Windows でコピーした内容（テキスト・画像・任意のファイル）が iPhone 側に反映される
   - iPhone でコピーした内容も同様に Windows 側に反映される
5. 同期を止めたいときは、実行中の画面に表示されている停止ボタンをタップしてショートカットを終了する

注意事項：

- iPhone から送ったファイルは、iOS のクリップボードが元のファイル名を保持しないため、`clipboard.<拡張子>` という名前で Windows 側に届きます。
- iPhone と Windows は同じローカルネットワーク上にある必要があります。デフォルトのリッスンポートは `913` です。
- 初回起動時に `%AppData%\ClipSync\config.json` が自動生成され、ランダムなペアリングキーも作成されます。

### トレイメニュー

- 「設定」：同期する内容、認証コードの検出と通知、ポート番号と共有キーなどを変更（「一般」から初期値へのリセットも可能）
- 「ペアリング QR コードを表示」：Shortcuts などでスキャンしてペアリング
- 「設定を再読み込み」：設定ファイルを変更した後に反映

## 主な機能

- **QR コードによるワンタップペアリング**：トレイメニューから QR コードを表示でき、サーバー URL とキーを含む。Shortcuts でスキャンするだけで設定完了。
- **テキスト / 画像 / 任意のファイル**：クリップボードの内容は常に生のバイト列（raw bytes）で転送し、Base64 や JSON でラップしない。拡張子から MIME タイプを判定。
- **認証コードの通知**：`/otp` に SMS 本文（複数可）を送ると、まだ処理していないメッセージから認証コードを抽出する。テキスト入力中ならカーソルの近くに浮動ボタンを表示し、クリックするとコードを直接入力する（右クリックで閉じる）。入力中でなければ通知を出し、クリックしたときだけコピーする。どちらの場合も通知は Windows の通知センターに残り、後からクリックしてもコピーできる（現在のショートカットは未対応）。
- **キー認証**：すべての API 呼び出しは `X-ClipSync-Key` ヘッダーで認証される。キーは初回起動時に自動生成される。

## アーキテクチャ

プロジェクトは Windows アプリ 1 つで構成されています。

```
Windows（トレイ常駐アプリ、内蔵 HTTP サーバー）
   ▲
   │  数秒おきにクリップボードを比較・取得／送信
   ▼
iPhone（Shortcuts）
```

| コンポーネント          | パス                                               | 説明                                                                                                                        |
| ----------------------- | -------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------- |
| **ClipSync.WindowsApp** | [src/ClipSync.WindowsApp](src/ClipSync.WindowsApp) | Windows のシステムトレイ常駐アプリ。ASP.NET Core (Kestrel) サーバーを内蔵し、クリップボードの監視・提供、ペアリングを担当。 |

主な API：

| メソッド | パス              | 説明                                                                                                                                       |
| -------- | ----------------- | ------------------------------------------------------------------------------------------------------------------------------------------ |
| `POST`   | `/clipboard`      | テキストまたはファイルをアップロード（生のバイト列。ファイルの場合は `X-Content-Type: file`、`X-File-Name`、`X-Extension` ヘッダーを付与） |
| `GET`    | `/clipboard/meta` | 現在のクリップボードのメタ情報（種類・ファイル名・MIME・更新日時）を取得                                                                   |
| `GET`    | `/clipboard/data` | 現在のクリップボードの実体（生のバイト列）を取得                                                                                           |
| `POST`   | `/otp`            | SMS 本文（UTF-8 テキスト、64 KB まで。複数は `<<ClipSync:next>>` 区切り、新しい順）から認証コードを抽出して通知。処理済みは無視する        |

## 開発

- .NET SDK（`net10.0` / `net10.0-windows`。`dotnet --version` で確認可能）：<https://dotnet.microsoft.com/ja-jp/download>
- Windows 側は WinForms を使用するため Windows 上での実行が必須

ソースから実行する場合：

```bash
dotnet run --project src/ClipSync.WindowsApp
```

`v*` タグを push すると、GitHub Actions が自動的にビルドして Release を作成します。

## プログラムの調整方法

以下のファイルは、特に断りがなければ [src/ClipSync.WindowsApp](src/ClipSync.WindowsApp) からの相対パスです。

### 機能と実装場所

| 機能                                       | 主な実装場所                                                                  |
| ------------------------------------------ | ----------------------------------------------------------------------------- |
| 起動                                       | `Program.cs`                                                                  |
| トレイメニュー、各部品の接続、通知         | `TrayApplicationContext.cs`                                                   |
| Windows のクリップボードの監視・書き込み   | `ClipboardWatcher.cs`                                                         |
| HTTP API、キー認証、アップロード内容の判定 | `Server/ClipSyncServer.cs`                                                    |
| 同期中の内容の保持                         | `Server/ClipboardStore.cs`、`Server/ClipboardPayload.cs`                      |
| HEIC → PNG 変換                            | `Server/HeicConverter.cs`                                                     |
| 設定ファイル（`config.json`）              | `Server/AppConfig.cs`                                                         |
| 設定画面                                   | `SettingsWindow.xaml`、`SettingsWindow.xaml.cs`                               |
| ペアリング QR コード                       | `PairingQrCode.cs`（内容と画像）、`QrCodeForm.cs`（画面）                     |
| QR コードに入れる IP アドレスの選択        | `LocalNetworkInfo.cs`                                                         |
| 認証コードの抽出                           | `Server/OtpExtractor.cs`                                                      |
| 受信済みメッセージの除外                   | `Server/OtpInbox.cs`                                                          |
| 認証コードの浮動ボタン                     | `OtpPopupForm.cs`                                                             |
| 入力欄・カーソル位置の検出                 | `TextInputLocator.cs`                                                         |
| コードの直接入力                           | `KeyboardTyper.cs`                                                            |
| Win32 API の共通呼び出し                   | `NativeMethods.cs`                                                            |
| iOS アプリ向けのサイレントプッシュ         | `Server/PushNotificationService.cs`、`ApnsClient.cs`、`RelayPushClient.cs` 他 |

### よくある変更

| 変えたいこと                         | 変更する場所                                                                                                                 |
| ------------------------------------ | ---------------------------------------------------------------------------------------------------------------------------- |
| ポート番号（自分の環境だけ）         | `%AppData%\ClipSync\config.json` の `Port` を変更し、「設定を再読み込み」の後に再ペアリング                                  |
| ポート番号の初期値                   | `Server/AppConfig.cs` の `Port`（プロパティの初期値と `LoadOrCreate` の 2 か所）                                             |
| 対応する拡張子と MIME タイプ         | `MimeTypesByExtension`。`Server/ClipSyncServer.cs`（受信、`.` なし）と `ClipboardWatcher.cs`（送信、`.` あり）の両方にある   |
| API の追加                           | `Server/ClipSyncServer.cs` の `StartAsync`（キー認証はすべてのパスに適用される）                                             |
| トレイメニューの項目                 | `TrayApplicationContext.cs` のコンストラクター                                                                               |
| 認証コードとみなす条件               | キーワードと追加の正規表現（1 行に 1 ルール、上から順）は設定画面。桁数などは `Server/OtpExtractor.cs` の `CodeRegex`              |
| `/otp` の上限サイズ                  | `Server/ClipSyncServer.cs` の `MaxOtpBodyBytes`                                                                              |
| 同じメッセージを無視する時間         | `Server/OtpInbox.cs` の `Memory`                                                                                             |
| 浮動ボタンの表示時間・大きさ・見た目 | 表示時間は設定画面。他は `OtpPopupForm.cs` の `BaseWidth` / `BaseHeight`、コンストラクター（色）、`OnPaint`（文字・アイコン） |

ペアリング情報は iPhone 側の `ClipSync/clipsync-config.json` に保存されます。再ペアリングするには、このファイルを削除してからショートカットを実行してください。

### ショートカットの変更

ショートカットの全アクション、使用している変数、Windows 側との対応は [docs/shortcut.md](docs/shortcut.md)（中国語）にまとめています。

次の項目は両側で一致させる必要があります。片方を変えたら、もう片方も変更してください。

- API のパス（`/clipboard`、`/clipboard/meta`、`/clipboard/data`）
- ヘッダー名（`X-ClipSync-Key`、`X-Content-Type`、`X-Extension`）
- `/clipboard/meta` の項目名（`updatedAt`、`type`、`fileName`）と `type` の値（`text` / `file`）
- ペアリング QR コードの項目名（`url`、`key`）
- `/otp` の区切り文字（`OtpInbox.Separator`。現在のショートカットは `/otp` 未対応）

同期の間隔はショートカットだけで変更できます（ループ末尾の「待機」アクション）。

変更を公開する手順：

1. iPhone のショートカットアプリで編集し、共有メニューから新しい iCloud リンクをコピーする
2. この README の「iPhone」にあるリンクを差し替える（QR コード `docs/shortcut-qr.png` は、`main` に push すると GitHub Actions が更新する）
3. `https://www.icloud.com/shortcuts/api/records/<リンクの ID>` を開き、`fields.shortcut.value.downloadURL` の URL（`${f}` は任意のファイル名に置き換える）からダウンロードして `docs/ClipSync.shortcut` を上書きする
4. 内容を解析して、[docs/shortcut.md](docs/shortcut.md) を更新する

```bash
python3 scripts/dump_shortcut.py docs/ClipSync.shortcut
```
