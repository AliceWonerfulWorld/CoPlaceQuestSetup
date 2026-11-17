# Experimenter Session Reset UI

PC画面右上の `Reset Session` は、Lobby・分類中・最終結果表示中に利用できます。`ExperimentSessionManager.Start` で実行時に生成するため、Sceneへの手動追加やToolsのSetup実行は不要です。既存のEventSystem（Mouse入力有効）とuGUIを利用します。Quest／Participantには表示しません。

## 操作

1. PCをExperimenterとして同じRoomへ接続する（EditorのAutoByPlatformはExperimenter）。
2. セットを変更する場合、PCの `PilotCardSetController > Next Session Set` をA/Bに設定する。これは次のResetで適用される既存仕様です。
3. 右上の `Reset Session` を押し、中央の確認画面で `Reset Session` または `Cancel` を選ぶ。既存Lobbyの `New Session`、Resultの `New Session / Reset` からも同じ確認画面を開けます。
4. Reset中はボタンが無効になります。全接続端末のACK後に `Reset complete` が表示され、Lobbyの待機状態へ戻ります。
5. LobbyでNormal／Proposedを選択し、参加者の準備完了後にStartを押します。

確認を取り消した場合、カードやSessionは変更しません。確認中にRole・接続・Session ID／Epochが変わった場合も取り消します。

## 既存Resetとの関係・データ

UIから既存の `ResetSession() -> StartNewSession()` を呼びます。新しいGUID／Epoch、全端末へのControl通知、既存ローカル初期化、同期変数の初期値、全生存クライアントのACK確認を再利用し、接続やサーバーは再起動しません。Session ID／Epochで古い配置・Startなどを除外する既存方式は変更していません。

共有配置、独立回答、Submit、Reveal、Ready、Confirmed、Result、ドラッグ、ハイライト／プレビューは既存Reset経路で解除します。次の開始は自動実行せず、LobbyのStartを待ちます。

Reset前にExperimentLoggerが現在の状態を取得し、未保存キューを旧Session IDのまま書き出します。失敗時はResetを開始せず、UIにCSVエラーを表示します。保存先を復旧して再度Resetしてください。保存済みCSV・確定イベントは保持し、新Sessionのログは別フォルダになります。ログは既存の第1段階の内容を維持し、詳細な最終配置ログの新規実装は含みません。

Reset開始後に切断・ACK待ち・保存エラーがあればUIに表示します。30秒のACK待ち警告でも操作はロックしたままです。切断端末を同じRoomに再接続し、ACK完了を待ちます。進行中のResetを重複実行するAPI呼び出しも拒否します。

## 検証（2026-11-17）

Unity 6000.3.17f1 EditorのPreviewSceneとオフライン通信で確認しました。実ネットワーク・Quest実機検証ではありません。

- `ExperimentLoggerVerification.Run()`：29項目成功。CSV分離・保存失敗／復旧・既存ログ仕様。
- 上記から呼ぶ `ExperimentResultVerification`：78項目成功。Normal／Proposedの開始→確定→UI Reset→次の開始、キャンセル・連打・Participant拒否・CSV失敗によるReset中止など。
- `PilotCardSetVerification.Run()`：427項目成功。A/B各8枚とDevelopment、両方式、実際のButtonイベントからのReset、全端末ACK、セット／方式切り替え、過去Session CSVのバイト単位保持。
- `PrivateAnswerCardPoseVerification.Run()`：19項目成功、上記Pilot検証を通じたUnity Error/Exception/Assert 0件。前回の回転修正を含む回帰確認。
- コンパイル成功、`git diff --check`成功。
- `SessionResetConfirmation.png`：隔離したEditor PreviewSceneで描画し目視確認。画像化のためCanvasのみScreen Space Cameraに一時変更。製品側はScreen Space Overlay。

保存先：`SessionResetVerification.json`、`PilotCardSetVerification.json`、`PrivateAnswerCardPoseVerification.json`。ログ試験の意図的なIO失敗はConsoleにErrorを出しますが、テストで期待されるものです。

### 実機での追加確認（未検証）

PC＋Quest 3を2台、同じサーバー／Roomへ接続した状態で以下を確認してください。

- NormalとProposedで8枚を分類・確定後、PCのマウスでResetし、2台ともResult・Submit・Reveal・Readyが解除されること。
- Normal→Proposed、Proposed→Normal、A→Bの次試行を、Questやサーバーを再起動せず開始できること。
- カードを掴んだ途中のResetで掴み・ハイライトが残らず、Unclassifiedへ戻ること。
- ParticipantにはReset UIが出ないこと、PCの確認キャンセルでは結果が保持されること。
- ACK待ちでQuestを切断・再接続した際の進捗表示と完了、旧Session CSVの保持。

## 今回のファイル

- `Assets/Scripts/SessionResetUI.cs`（新規、対応meta）：PC専用ボタン・確認・状態表示。
- `Assets/Scripts/ExperimentSessionManager.cs`：UI生成・入口、重複防止、ログ保存確認。
- `Assets/Scripts/ExperimentLogger.cs`：Reset前の安全な書き出し。
- `Assets/Scripts/ExperimentLobbyController.cs`、`ExperimentResultUI.cs`：既存ボタンを確認へ接続。
- `Assets/Editor/ExperimentResultVerification.cs`、`PilotCardSetVerification.cs`：UI／ログ保護の回帰検証。旧3枚のfixtureはDevelopmentを明示。
- `Documentation/PilotCardSetVerification.json`、`SessionResetVerification.json`、`SessionResetConfirmation.png`、本書：結果・手順。
- 前回から未コミットの `PrivateAnswerCardPoseVerification.cs`／結果JSONは、Pilot検証の項目数を固定しない表記へ更新。`PrivateAnswerCard.cs`と`ProjectSettings.asset`の既存変更は保持しています。
