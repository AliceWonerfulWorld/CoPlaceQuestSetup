# 実験ログ 第1段階

## 構成

`ExperimentManager` GameObjectに`ExperimentLogger`を追加。
既存のSession Reset開始／完了、Final AgreementのConfirmed通知と、追加したLobbyのStart受信通知を購読します。
`ExperimentCsvLogStore`だけがCSVを扱います。実験Scriptにはファイル保存処理を入れていません。
LoggerはNetSync変数・Role・State・配置を書き換えません。

保存するのはPC上のExperimenterだけです。Participant・モバイルPlayerは書き込みません。
Scene設定を再適用する場合: `Tools > Experiment > Configure Experiment Logger`。
既存の保存済みQuestSetupをPlay外で開いて実行してください。

## 保存場所

`Application.persistentDataPath/ExperimentLogs/<Session ID>/`

このProjectのmacOS Editorでは:
`/Users/alisunsun/Library/Application Support/DefaultCompany/CoPlaceQuestSetup/ExperimentLogs/`

各フォルダに`session.csv`（1行の概要）と`phase_events.csv`（イベント列）を作成します。
Consoleの`[Experiment Logger] CSV root:`とイベント保存メッセージに実際の絶対パスを出します。
New Session / Resetで過去のフォルダは削除しません。

## イベント

| Event Type | 記録の契機 |
|---|---|
| SESSION_CREATED | ExperimenterがSession IDを初めて観測した時 |
| SESSION_RESET_STARTED | 新Sessionのresetting Controlを観測した時 |
| SESSION_RESET_COMPLETED | `OnSessionStarted`＝ローカルのReset完了 |
| EXPERIMENT_STARTED | Lobbyが有効な共有Start recordを受信／復元した時。要求送信だけでは記録しない |
| BOARD_CONFIRMED | `FinalAgreementManager.OnBoardConfirmed`。再有効化時には保持済み結果も確認 |

現在のResetは新Session IDを発行する設計です。Reset開始／完了は新ID側に記録されます。
旧Sessionを終了した正確な時刻を示すイベントではありません。
第1段階では1 Sessionにつき1回の最終Board確定を想定します。

Conditionは**Start recordに確定したNormal / Proposedのみ**を採用します。
Start前は空欄で逐次保存し、Startを観測した時に同Sessionの既存行も確定値へ更新します。
StartされなかったSessionのConditionは空欄のままです。Reset時のModeを確定条件として扱いません。
PhaseイベントのParticipant IDは空欄です。`observer_client_id`はPCの接続番号で、研究上の被験者IDではありません。
Start時の登録Client番号は`participants_at_start`にセミコロン区切りで保存します。

## 時刻の意味

- `event_time_utc`: **Logger PCが通知・状態を観測したUTC時刻**。Questでの操作発生時刻やserver確定時刻ではありません。
- `session_elapsed_seconds`: PCがSessionを初観測してからの単調時計による秒数。Resetの準備時間も含みます。
- UTC時計が補正されても経過秒数は影響されません。Start→Confirmedの時間は同じ`observer_run_id`の経過秒数の差を使えます。
- Logger再起動／domain reload後に既存Sessionへ追記する場合、元の単調時計の基準は不明なので経過秒数を空欄にします。
- `observer_run_id`はLogger保存インスタンスの識別子です。端末時計を同期済みと仮定しません。
- `observation_kind`が`retained_snapshot`の行は、保持済み状態からの確認です。接続前の真の発生時刻を復元したものではありません。
- Startの`received_start_record`、その他の`notification`も共有状態の受信・復元を含み得ます。通知時刻を操作発生時刻とみなさないでください。
- Experimenter PC未接続中の履歴は収集できません。実験開始前からExperimenterとLoggerを稼働させてください。

## CSV・重複・エラー

UTF-8 BOM、CRLF、全フィールド引用符付きCSVです。日本語・カンマ・二重引用符・セル内改行を扱います。
数値はInvariantCulture（小数点`.`）です。ExcelからはUTF-8／カンマ区切りとして読み込めます。

Event IDはSession内で安定した値にし、保存済みCSVも読んで重複を除外します。
StartのIDは既存Start recordのIDを含めます。再通知でも最初の観測時刻を保持します。
各通知を即時保存し、テンポラリファイルのFlush後に原子的に置換します。
第1段階は少数イベントなので全行を書き戻します。高頻度操作ログの追加時は追記方式／バッファを検討してください。
`phase_events.csv`が正本で、`session.csv`は再生成できる概要です。2ファイル同時のトランザクションではありません。
保存途中に概要だけ失敗した場合、再試行／同一通知で正本から概要を修復します。
同じ出力先へ複数のExperimenterプロセスから同時書き込みする構成は対象外です。

保存失敗は実験処理へ例外を返さずConsoleへ出し、観測時刻を保持したキューを5秒間隔で再試行します。
無効化・終了時にも再試行します。ただし永続的なディスク障害中にアプリを終了すると未保存キューは失われます。
CSVのスキーマ不一致・破損は上書きせずエラーにします。実験前に保存先とConsoleを確認してください。

## 自動テストとサンプル

`Tools > Experiment > Verify Experiment Logger`

結果: `Library/ExperimentLoggerVerification/result.json`
テスト出力先は同JSONの`outputDirectory`。実運用ログと分離しています。
CSVの日本語・特殊文字、BOM、Culture、単調時計、再起動、重複、Condition確定、IO失敗と再試行を確認します。
さらにLogger付きの疑似3端末で既存Normal / Proposed / Role / Reset / Resultフローを実行し、Participantが保存しないことと全5イベントを検証します。
実機Quest・実通信・Excel GUI上での確認は別途必要です。

保存済みサンプル: `Documentation/LogSamples/Normal/`、`Documentation/LogSamples/Proposed/`。

## 第2段階

- Normal / Mainの配置変更、Proposed独立回答／取消、Submit、Reveal、Ready／解除などの操作イベント。
- Quest→PCの転送、送信元Session / Epoch / Participant / Event ID、ACK・再送・重複防止・遅延イベントの扱い。
- PC受信時刻とQuest単調時計による発生時刻を分け、必要なら時計差を推定。UTC一致は仮定しない。
- Client番号と研究用Participant IDの対応、課題ID・条件順のメタデータ。
- 高頻度書き込み、容量・保存失敗の運用表示、異常終了時の未送信イベント保全。

実行結果: Logger 29項目、Logger付き既存フロー71項目がPASS。Python標準csvでNormal / Proposedの4ファイルと特殊文字の往復を確認。詳細は`ExperimentLoggerVerification.json`。
