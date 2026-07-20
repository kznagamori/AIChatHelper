# settings.toml 設定ガイド

`settings.toml` は AIChatHelper の動作を変更するための設定ファイルです。アプリケーションの実行フォルダーに配置され、起動時に読み込まれます。

設定を変更した後は、原則としてアプリケーションを再起動してください。一部の状態設定はアプリが自動保存します。

## 基本構成

`settings.toml` は主に次のセクションで構成されます。

- `[[ChatSites]]`
- `[Config]`
- `[Config.EditorSettings]`
- `[Config.TemplateSettings]`
- `[Config.TabRestoreSettings]`
- `[Config.ExecuteAfterSendSettings]`
- `[[Config.ExecuteAfterSendSettings.ServiceExecutors]]`
- `[Config.UiState]`
- `[[Config.UiState.LeftPaneTabs]]`

## ChatSites

左ペインに追加できるチャットサービスを定義します。

```toml
[[ChatSites]]
Name = "ChatGPT"
Url = "https://chat.openai.com/"
```

項目:

- `Name`: 画面上に表示するサービス名。
- `Url`: WebView2 で開くチャットサービスの URL。

複数のサービスを追加する場合は、`[[ChatSites]]` ブロックを繰り返します。

```toml
[[ChatSites]]
Name = "Gemini"
Url = "https://gemini.google.com/app?hl=ja"

[[ChatSites]]
Name = "Claude"
Url = "https://claude.ai/new"
```

## Config

アプリ全体の基本設定です。

```toml
[Config]
InputSelectors = [
    '[contenteditable="true"][role="textbox"]',
    '[contenteditable="true"]',
    'textarea.w-full.resize-none',
    '#copilot-chat-textarea',
    'textarea',
]
```

### InputSelectors

`InputSelectors` は、チャットサービスの入力欄を探すための CSS セレクターです。

上から順に評価され、最初に見つかった要素へエディタ本文を反映します。チャットサービス側の画面構造が変わった場合は、このリストに新しいセレクターを追加します。

注意:

- セレクターの順番は優先順位です。
- 不正なセレクターは入力欄検出に失敗する原因になります。
- 入力欄が検出できない場合、エディタ本文は履歴保存・クリアされません。

## Config.EditorSettings

エディタと履歴・テンプレート選択時の挙動を設定します。

```toml
[Config.EditorSettings]
ConfirmTemplateOverwrite = false
ConfirmHistoryOverwrite = false
InsertTemplateTextOnClear = false
TemplateTextForEditor = """

"""
```

項目:

- `ConfirmTemplateOverwrite`: テンプレート適用時に現在のエディタ内容を破棄する確認を表示するか。
- `ConfirmHistoryOverwrite`: 履歴適用時に現在のエディタ内容を破棄する確認を表示するか。
- `InsertTemplateTextOnClear`: エディタクリア後に定型文を自動挿入するか。
- `TemplateTextForEditor`: `InsertTemplateTextOnClear = true` の場合に挿入する定型文。

複数行文字列は三重引用符で指定します。

```toml
TemplateTextForEditor = """
回答は、日本語で出力してください。

"""
```

## Config.TemplateSettings

テンプレートツリーで参照するフォルダーを設定します。

```toml
[Config.TemplateSettings]
TemplateDirectory = "template"
```

`TemplateDirectory` には次の形式を指定できます。

- 相対パス
- 絶対パス
- ドライブパス
- UNC パス

例:

```toml
TemplateDirectory = "template"
TemplateDirectory = "D:\\AIChatHelper\\template"
TemplateDirectory = "\\\\server\\share\\template"
```

ネットワークドライブや UNC パスを指定した場合、Windows の認証ダイアログが表示されることがあります。

## Config.TabRestoreSettings

左ペインのチャットタブを次回起動時に復元する方法を設定します。

```toml
[Config.TabRestoreSettings]
SaveAndRestoreTabUrls = false
AlwaysRestoreInitialTabs = false
```

項目:

- `SaveAndRestoreTabUrls`: 登録チャットサイト内で最後に表示していたページ URL を保存し、次回起動時に復元するか。
- `AlwaysRestoreInitialTabs`: 前回のタブ構成を使わず、`[[ChatSites]]` を登録順に各 1 タブずつ登録 URL で開くか。

両方が `true` の場合は `AlwaysRestoreInitialTabs` が優先されます。`[[ChatSites]]` の先頭タブが選択され、現在 URL は保存・復元されません。両方が `false` の場合は、前回のタブ構成を復元し、各タブを登録 URL から開く従来の動作になります。

現在 URL の保存・復元には次の制限があります。

- 登録 URL と同じ `http` / `https` スキームであること。
- 登録 URL と同じホスト、またはその DNS サブドメインであること。
- ユーザー情報を含まず、8,192 文字以内であること。
- 外部リンクや別ドメインの認証ページは保存しないこと。

例えば、登録 URL が `https://chat.openai.com/` の場合、別ドメインである `https://chatgpt.com/` のページ URL は保存対象外です。`chatgpt.com` 内のページを復元する場合は、`[[ChatSites]]` の URL 自体を `https://chatgpt.com/` に変更してください。

URL のパスやクエリには会話 ID などが含まれる場合があります。`SaveAndRestoreTabUrls = true` にすると、対象 URL は `settings.toml` の `CurrentUrl` に平文で保存されます。また、機能を無効にした直後の最初の保存では、直前の内容を保持する `settings.toml.bak` に URL が残る場合があります。その後の設定保存でバックアップも上書きされます。

## Config.ExecuteAfterSendSettings

右ペインの「送信後実行」機能を設定します。

```toml
[Config.ExecuteAfterSendSettings]
DefaultEnabled = false
ExecutionTimeoutMs = 3000
PostInputDelayMs = 100
RetryIntervalMs = 100
EnableDomAnalysisLog = true
UnsupportedServiceBehavior = "InputOnly"
```

項目:

- `DefaultEnabled`: 起動時の「送信後実行」チェック状態。
- `ExecutionTimeoutMs`: 実行ボタンを探す最大待ち時間。
- `PostInputDelayMs`: 入力欄へ本文を反映してから実行ボタン探索を始めるまでの待ち時間。
- `RetryIntervalMs`: 実行ボタン探索のリトライ間隔。
- `EnableDomAnalysisLog`: 実行前の DOM 候補情報を Debug ログへ出力するか。プロンプト本文は出力しません。
- `UnsupportedServiceBehavior`: 未対応サービスでチェックがオンの場合の挙動。

`UnsupportedServiceBehavior` の値:

- `InputOnly`: 入力欄への反映だけ行う。
- `ShowWarning`: 入力欄への反映だけ行い、警告を表示する。

## Config.ExecuteAfterSendSettings.ServiceExecutors

サービスごとの送信・実行ボタン候補を設定します。

```toml
[[Config.ExecuteAfterSendSettings.ServiceExecutors]]
ServiceName = "ChatGPT"
UrlPatterns = ["chat.openai.com", "chatgpt.com"]
SubmitButtonSelectors = [
    'button[data-testid="send-button"]',
    'button[data-testid="composer-submit-button"]',
    'button[aria-label*="Send"]',
    'button[aria-label*="送信"]',
]
KeyboardFallback = "None"
```

項目:

- `ServiceName`: サービス名。ログや識別に使います。
- `UrlPatterns`: 対象サービス判定に使う URL 文字列。
- `SubmitButtonSelectors`: 実行ボタン候補の CSS セレクター。
- `KeyboardFallback`: ボタンが見つからない場合のキーボードフォールバック。

`KeyboardFallback` の値:

- `None`: フォールバックしない。
- `Enter`: 入力欄に Enter 相当のキーイベントを送る。
- `CtrlEnter`: 入力欄に Ctrl+Enter 相当のキーイベントを送る。

注意:

- `SubmitButtonSelectors` はサービス側 UI の変更で壊れることがあります。
- 無効な CSS セレクターはスキップされます。
- `:has()` は Chromium の対応状況に依存します。
- 実行成功は、クリックまたはフォールバック操作を試みたことを示します。AI 応答の開始や完了は判定しません。

## Config.UiState

アプリが現在状態を保存するためのセクションです。通常は手動編集不要です。

```toml
[Config.UiState]
ActiveLeftTabIndex = 0
PaneDisplayMode = "TwoPane"
RightPaneSelectedTab = "History"
WindowWidth = 1600
WindowHeight = 800
WindowLeft = 100
WindowTop = 100
RestoreWindowPosition = false
IsDarkTheme = true
ExecuteAfterSend = false
```

項目:

- `ActiveLeftTabIndex`: 左ペインで最後にアクティブだったタブ位置。
- `PaneDisplayMode`: アプリ全体のペイン表示。`TwoPane`、`LeftPane`、`RightPane` のいずれか。
- `RightPaneSelectedTab`: 右ペインの選択タブ。`History` または `Template`。
- `WindowWidth`: アプリケーションウィンドウの幅。
- `WindowHeight`: アプリケーションウィンドウの高さ。
- `WindowLeft`: アプリケーションウィンドウの左位置。`RestoreWindowPosition = true` の場合だけ復元に使います。
- `WindowTop`: アプリケーションウィンドウの上位置。`RestoreWindowPosition = true` の場合だけ復元に使います。
- `RestoreWindowPosition`: 起動時に `WindowLeft` / `WindowTop` を復元するか。
- `IsDarkTheme`: テーマ状態。`true` はダーク、`false` はライト。
- `ExecuteAfterSend`: 「送信後実行」の現在チェック状態。

### Config.UiState.LeftPaneTabs

左ペインに表示しているタブを保存するための配列です。通常はアプリが自動更新します。

```toml
[[Config.UiState.LeftPaneTabs]]
SiteName = "ChatGPT"
Url = "https://chat.openai.com/"
DisplayName = "ChatGPT"
CurrentUrl = "https://chat.openai.com/c/example"
```

項目:

- `SiteName`: 元になったチャットサイト名。
- `Url`: 元になったチャットサイト URL。
- `DisplayName`: タブ見出し。
- `CurrentUrl`: 最後に表示していた検証済み URL。URL 保存・復元が有効な場合だけ出力されます。

復元時は、現在の `[[ChatSites]]` に一致するタブだけが作成されます。保存済み登録 URL が現在の `ChatSites` に存在しない場合、そのタブはスキップされます。`CurrentUrl` が不正または許可ドメイン外の場合は、元の `Url` から開きます。

## 例

```toml
[[ChatSites]]
Name = "ChatGPT"
Url = "https://chat.openai.com/"

[[ChatSites]]
Name = "Gemini"
Url = "https://gemini.google.com/app?hl=ja"

[[ChatSites]]
Name = "Claude"
Url = "https://claude.ai/new"

[Config]
InputSelectors = [
    '[contenteditable="true"][role="textbox"]',
    '[contenteditable="true"]',
    'textarea',
]

[Config.EditorSettings]
ConfirmTemplateOverwrite = false
ConfirmHistoryOverwrite = false
InsertTemplateTextOnClear = false
TemplateTextForEditor = """

"""

[Config.TemplateSettings]
TemplateDirectory = "template"

[Config.TabRestoreSettings]
SaveAndRestoreTabUrls = false
AlwaysRestoreInitialTabs = false

[Config.ExecuteAfterSendSettings]
DefaultEnabled = false
ExecutionTimeoutMs = 3000
PostInputDelayMs = 100
RetryIntervalMs = 100
EnableDomAnalysisLog = true
UnsupportedServiceBehavior = "InputOnly"

[[Config.ExecuteAfterSendSettings.ServiceExecutors]]
ServiceName = "ChatGPT"
UrlPatterns = ["chat.openai.com", "chatgpt.com"]
SubmitButtonSelectors = [
    'button[data-testid="send-button"]',
    'button[data-testid="composer-submit-button"]',
    'button[aria-label*="Send"]',
    'button[aria-label*="送信"]',
]
KeyboardFallback = "None"
```

## トラブルシュート

### 起動時に設定読み込みでエラーになる

TOML の構文が壊れている可能性があります。文字列の閉じ忘れ、配列末尾、引用符の種類を確認してください。

### チャット入力欄へ反映されない

`InputSelectors` が対象サービスの入力欄に一致していない可能性があります。ブラウザーの開発者ツールなどで入力欄の CSS セレクターを確認し、優先順位が高い位置へ追加してください。

### 送信後実行でボタンが押されない

対象サービスの実行ボタン DOM が変わった可能性があります。`EnableDomAnalysisLog = true` にして Debug ログを確認し、`SubmitButtonSelectors` を調整してください。

### テンプレートが表示されない

`TemplateDirectory` のパスが存在するか確認してください。ネットワークパスの場合は Windows 側で接続・認証できる必要があります。

### 前回開いていたチャットページが復元されない

`SaveAndRestoreTabUrls = true` になっていることと、`AlwaysRestoreInitialTabs = false` になっていることを確認してください。現在ページが登録 URL と別のドメイン、別のスキーム、または 8,192 文字を超える URL の場合は保存されず、登録 URL から開きます。
