# CoPlace 実験ログ・CSV出力監査

監査日：2026-11-17（JST）／対象：`338b50fc4f05e41b27244b62844426e037bfb014`。調査・分析・報告のみ。アプリケーションコード・Scene・設定・既存CSV・既存検証結果は変更していない。

## 1. 監査結果の概要

**総合判定：CONDITIONAL GO。** 現在の実装は第1段階の「Experimenter PCが観測した開始・確定・Reset」の保存としては利用できる。しかし、候補指標すべてを取得する実験基盤には達していない。特に、**CSVに最終カード配置・独立回答・操作履歴がない**。Resetが保持するのは既に保存されたCSVであり、未実装の回答データを新たに保存する機能ではない。

- 条件を満たせば両方式の **Start→Board ConfirmedのPC観測所要時間** を比較できる。Proposedの独立回答／共同分類それぞれの時間は分離できない。
- 参加者別操作回数・操作偏り・再分類回数は算出できない。`participant_id`列の存在は、参加者別操作が保存されている証拠にならない。
- 独立回答一致率・独立回答と最終回答の一致率はCSVから算出できない。実行中には必要な回答があるがResetで消える。
- Session別フォルダ、イベントID重複抑止、旧Sessionの未保存キュー書き出し、Reset時の保存失敗ブロックはある。ただしキューはメモリのみで、クラッシュ時の完全保護ではない。
- Pair ID・安定した研究用Participant ID・実施順序は未実装。別紙の対応表で代替可能。
- **今回の読み取り時、実行中EditorのExpected Participant Countは1だった。** 動作確認設定と考えられるが、ペア実験前に2へ戻し、Quest 2台のRole登録・Start条件を確認する必要がある。変更はしていない。

以下では「取得・保存済み」は永続CSVに残るもの、「取得済みだが未保存」はメモリ／現在の同期変数に存在するものとする。Console出力やRoomの最新状態は研究用の永続操作ログとして扱わない。

## 2. 現在のログ収集・保存アーキテクチャ

### 2.1 調査範囲とコード索引

`Assets/Scripts`、`Assets/Editor`、`AgentScripts`、`Documentation`、カードキット、Scene・設定、ローカルPackageCacheの通信実装を検索した。`RecordEvent`、CSV／ファイルI/O、ID・時刻・Reset・Reveal・Readyの定義と呼び出し元を追跡した。生成物・サンプルの一般ログ出力を、別の実験CSV収集機構と混同していない。Pair/Groupを研究対象へ割り当てる実装は見つからなかった。

以降の根拠IDは次表を参照。行番号は監査コミットのもの。

| 根拠 | ファイル・クラス | メソッド／行番号 | 確認内容 |
|---|---|---|---|
| E1 | [ExperimentLogger.cs](../Assets/Scripts/ExperimentLogger.cs#L12)／ExperimentLogger | OnEnable 28–36、OnResetStarted 61–73、RecordStart 81–84、OnBoardConfirmed 86–91、CaptureSnapshot 93–107 | 保存する5種類のイベント。最終配置引数`placements`は書き出していない |
| E2 | 同上 | RecordEvent 112–125、TryFlushBeforeReset 128–133、Drain 134–153、OnDisable/OnApplicationQuit 45–57 | 観測時刻・メモリキュー・再試行・終了時の最終試行 |
| E3 | [ExperimentCsvLogStore.cs](../Assets/Scripts/ExperimentCsvLogStore.cs#L10)／ExperimentCsvLogStore | Observation 12–20、列定義22–32、Record 47–105、Validate 108–117、WriteAtomic 120–130、ReadCsv 134–157 | 3 CSV、ID重複抑止、BOM、原子的な単一ファイル置換 |
| E4 | [ExperimentSessionManager.cs](../Assets/Scripts/ExperimentSessionManager.cs#L106)／ExperimentSessionManager | ReadControl 106–140、ClearLocal 142–149、BuildDefaults 151–183、TryAdvance 231–274、CompleteLocal 281–293、StartNewSession 302–321、ResetSession 323 | 新GUID／Epoch、セット適用、初期化、全端末ACK、旧ログ保存確認 |
| E5 | [ExperimentParticipantRegistry.cs](../Assets/Scripts/ExperimentParticipantRegistry.cs#L9)／ExperimentParticipantRegistry | EffectiveRole 44–50、PublishRole 109–114、RefreshParticipants 137–155 | 通信ClientNoとRoleによる参加者一覧。研究用の個人ID登録ではない |
| E6 | [ExperimentLobbyController.cs](../Assets/Scripts/ExperimentLobbyController.cs#L13)／ExperimentLobbyController | StartRecord 13、Update 79–98、StartExperiment 99–107、NewSessionFromUI 110 | Start marker受信による開始通知。StartRecordに実行時刻フィールドなし |
| E7 | [ExperimentManager.cs](../Assets/Scripts/ExperimentManager.cs#L25)／ExperimentManager | Mode/State 25–38、CurrentMode/State 52–54、ModeChangeBlockReason 56–69 | Modeと現在Phase。全遷移の永続記録ではない |
| E8 | [CandidateSyncTest.cs](../Assets/Scripts/CandidateSyncTest.cs#L29)／CandidateSyncTest | ParticipantAnswer 29–35、TryGetParticipantAnswer 37–52、TrySetCandidate 160–176、TryCancelCandidate 180–190、ResetLocalSession 378–387 | participant別candidate/answered/selectedAt。時刻は回答元端末のUTC、最新値のみ |
| E9 | [IndependentAnswerSubmission.cs](../Assets/Scripts/IndependentAnswerSubmission.cs#L17)／IndependentAnswerSubmission | Proof 17、AnswerHash 40–52、GetSubmissionId 56–65、TrySubmit/TryCancel 67–82、Flush 93–106 | Submit GUIDと回答ハッシュ・現在の同意状態。Submit時刻・取消履歴なし |
| E10 | [ProposedRevealCoordinator.cs](../Assets/Scripts/ProposedRevealCoordinator.cs#L23)／ProposedRevealCoordinator | Answer/Participant/Snapshot 23–25、GetRevealedSnapshot 34、ClearLocal 51–54、LateUpdate 67–75、BuildCompleteSnapshot 144–166、Read 184以降 | 全員の独立回答SnapshotとSession/Epoch、回答時刻を保持。Reveal実行時刻・CSV保存なし |
| E11 | [ProposedSharedPlacementController.cs](../Assets/Scripts/ProposedSharedPlacementController.cs#L43)／ProposedSharedPlacementController | Update 43–84 | Reveal後の共有ボード準備・Discussionへの遷移。開始時刻の永続化なし |
| E12 | [FinalAgreementManager.cs](../Assets/Scripts/FinalAgreementManager.cs#L43)／FinalAgreementManager | BoardRecord 43–52、LateUpdate 119–154、IsParticipantReady/ToggleLocalReady 155以降、PreparePlacementChange 190以降、RestoreBoard 280–322、ResetLocalSession 324–329 | 最終配置・participants・Ready証明。最小ClientNoのParticipantが自動確定を発行 |
| E13 | [NormalPlacementSync.cs](../Assets/Scripts/NormalPlacementSync.cs#L144)／NormalPlacementSync | ResetLocalSession 144–150、SendPlacement 153–181、OnGlobalVariableChanged 184以降 | 最新共有分類を同期。送信ConsoleにClientNoはあるが、CSV操作イベントなし |
| E14 | [CardTierDetector.cs](../Assets/Scripts/CardTierDetector.cs#L177)／CardTierDetector | OnReleased 177–197、ResetSessionPlacement 199以降 | 無効／キャンセルの復帰、成功配置。送信前に権限・Tier確認 |
| E15 | [PrivateAnswerCard.cs](../Assets/Scripts/PrivateAnswerCard.cs#L47)／PrivateAnswerCard | CancelDrag 47–55、OnGrabbed 56–59、UpdateDragFeedback 61–70、OnReleased 72–77、ApplyPlacement 90以降 | Private操作・プレビュー・キャンセル。操作CSVなし |
| E16 | [SessionVariableTransport.cs](../Assets/Scripts/SessionVariableTransport.cs#L7)／SessionVariableTransport | Encode/Decode/Get/Set 9–24 | 固定キーの値をSession IDで包む。過去イベント蓄積・配送ACK付きログ転送ではない |
| E17 | [PilotCardSetController.cs](../Assets/Scripts/PilotCardSetController.cs#L17)／PilotCardSetController | NextSessionSetId/ActiveSetId 30–31、ApplySessionSet 34以降 | 次回選択と現在セットを分離。カードID・画像の対応を適用 |
| E18 | [SessionResetUI.cs](../Assets/Scripts/SessionResetUI.cs#L17)／SessionResetUI | CanReset/RequestConfirmation 17–27、ConfirmReset 29–39、Refresh 42–65 | PC実験者限定、確認、即時ロック、保存失敗表示 |
| E19 | [ExperimentResultUI.cs](../Assets/Scripts/ExperimentResultUI.cs#L30)／ExperimentResultUI | OnReset 30、Refresh 33–48、NewSessionFromUI 50–55 | Confirmed画面とReset要求。結果保存処理ではない |
| E20 | [ExperimentLoggerSetup.cs](../Assets/Editor/ExperimentLoggerSetup.cs#L11)／ExperimentLoggerSetup | Run 11–26 | LoggerをExperimentManagerのGameObjectへ配置・参照設定 |
| E21 | [NetSyncServerTools.cs](../Assets/Editor/NetSyncServerTools.cs#L91)／NetSyncServerTools | StopServer/RestartServer 91–115 | サーバープロセスの停止／再起動。Session Reset・CSVエクスポートとは別処理 |

通信基盤は`Library/PackageCache/com.styly.styly-netsync@8ee92598896b/Runtime/NetSyncManager.cs:566,1107`でClientNoを公開・受領する。`Runtime/Internal Scripts/NetworkVariableManager.cs:141–145,256–260,464–473,550–553`は最新値キャッシュ変更通知を行う。アプリケーションにはClientNoを人の安定IDへ変換する永続対応表がない。Room名やネットワークの接続グループは、研究用Pair IDの代替として記録されていない。

### 2.2 データの流れ

1. Quest Participantが共有配置／独立回答／Submit／ReadyをNetSyncへ送信する。
2. 既存コントローラーが最新同期状態からStart・Reveal・Confirmedなどを復元する。
3. **Experimenter PCのLoggerだけ**がSession通知・LobbyのStart通知・Board Confirmed通知を購読し、5種類の観測イベントを生成する（E1）。Revealやカード操作を購読してはいない。
4. メモリキューにSession ID・Epoch・PC時刻を付け、CsvLogStoreへ保存する（E2）。
5. Resetは旧キューの保存確認後、新Sessionを通知する。Roomとローカルの回答状態を初期化する（E4）。

サーバー側で全操作のイベントストリームを集める方式ではない。PCは受信者／観測者であり、すべてのParticipant操作がCSVに統合されるわけではない。

## 3. 取得可能なデータ一覧

CSV保存箇所の略号：**P**=`phase_events.csv`、**S**=`session.csv`、**C**=`card_set.csv`。`—`は永続保存なし。表のNormal／Proposed欄は指定された実装状況の分類。

### 3.1 識別情報

| データ項目 | Normal | Proposed | CSV出力 | 保存箇所 | 備考 |
|---|---|---|---|---|---|
| Session ID／Epoch | 取得・保存済み | 取得・保存済み | あり | P/S/C（EpochはP/S） | GUID、EpochはRoom内制御。全世界／全ペア通し番号ではない。E3/E4 |
| 研究用Participant ID | 未実装 | 未実装 | 空列のみ | P.participant_id | 全本番呼び出しは空文字。E1/E2 |
| 通信Participant ClientNo | 部分的に取得 | 部分的に取得 | 開始時一覧のみ | P/S.participants_at_start | `1;2`など。別Sessionで同じ人を保証しない。E5/E6 |
| Pair ID／Group ID | 未実装 | 未実装 | なし | — | 同一ペアの2試行を自動結合できない |
| Role | 取得済みだが未保存 | 取得済みだが未保存 | 明示Role列なし | client変数experimentRole | observer ID・参加者一覧から役割の一部は推定可能。Role変更履歴なし。E5 |
| Mode | 取得・保存済み | 取得・保存済み | あり | P/S.condition | 実験Startで確定し過去行にも反映。開始前は空。E1/E3 |
| Card Set | 取得・保存済み | 取得・保存済み | あり | C.card_set_id | A/B/Development。画像・manifestの版は保存しない。E3/E17 |
| 実施順序／割付グループ | 未実装 | 未実装 | なし | — | UTCで観測順の推定は可能だがカウンターバランスの試行番号ではない |
| 実験実施日時 | 取得・保存済み | 取得・保存済み | あり | P.event_time_utc/S各時刻 | PC観測日時。実験予約日・参加者現地日付の独立フィールドはない |

### 3.2 時間

| データ項目 | Normal | Proposed | CSV出力 | 保存箇所 | 備考 |
|---|---|---|---|---|---|
| Session準備開始時刻 | 部分的に取得 | 部分的に取得 | あり | P.SESSION_CREATED/RESET_STARTED、S | 新SessionをPCが観測した時刻。Control.startedAt自体はCSVへ転記しない |
| 分類作業開始時刻 | 部分的に取得 | 部分的に取得 | あり | P.EXPERIMENT_STARTED/S | Start観測時刻。最初にカードに触れた時刻ではない |
| 独立回答開始時刻 | 対象外 | 他のログから算出可能 | 専用列なし | P.EXPERIMENT_STARTED | Proposedの共通課題開始の代理値。個人の初回操作時刻は未保存 |
| 参加者ごとのSubmit時刻 | 対象外 | 未実装 | なし | — | Proofに時刻がない。selectedAtはSubmit時刻ではない。E9 |
| Reveal時刻 | 対象外 | 未実装 | なし | — | Reveal状態／Snapshotは取得するが、その発生時刻を保持・保存しない。E10 |
| 共同分類開始時刻 | 他のログから算出可能 | 未実装 | Normalのみ代理値 | P.EXPERIMENT_STARTED | NormalはStartを定義にすれば利用可。Proposedは共有ボード有効化時刻なし。E11 |
| 参加者ごとのReady／Agreement時刻 | 未実装 | 未実装 | なし | — | Readyの現状態と証明はあるが時刻・履歴はない。E12 |
| 全員合意成立時刻 | 部分的に取得 | 部分的に取得 | あり | P.BOARD_CONFIRMED/S | 合意による自動確定のPC観測時刻を代理にできる。別の「合意成立」イベントではない |
| Final Confirm時刻 | 部分的に取得 | 部分的に取得 | あり | P.BOARD_CONFIRMED/S | 確定データのPC受信観測。発行元での正確な時刻はない |
| セッション終了時刻 | 部分的に取得 | 部分的に取得 | 専用イベントなし | P.BOARD_CONFIRMEDで代替 | 正常な課題終了を確定と定義した場合のみ。中断終了は記録しない |
| Session Reset時刻 | 部分的に取得 | 部分的に取得 | あり | P.RESET_STARTED/COMPLETED、S | **リセット先の新Sessionに記録**。旧Session終了・中断の明示記録ではない。E1/E4 |

### 3.3 カード操作

| データ項目 | Normal | Proposed | CSV出力 | 保存箇所 | 備考 |
|---|---|---|---|---|---|
| 操作した参加者 | 部分的に取得 | 部分的に取得 | なし | 送信元Console／独立回答clientキー | Normalの共有Tier値に操作者フィールドなし。Proposed独立回答の持ち主は識別できるが履歴なし |
| 操作対象Card ID | 取得済みだが未保存 | 取得済みだが未保存 | なし | 同期キー／カードコンポーネント | Pにcard_id列なし。E8/E13 |
| 操作時刻 | 未実装 | 部分的に取得 | なし | Proposed.selectedAt_* | Proposedの最新独立回答の選択時刻のみ。共有操作時刻なし |
| 操作前の分類先 | 部分的に取得 | 部分的に取得 | なし | 受信callback oldValue／一時状態 | 完全な履歴として残らない。取消や再接続も値変更になり得る |
| 操作後の分類先 | 取得済みだが未保存 | 取得済みだが未保存 | なし | normalPlacement_*／candidate_* | 最新値のみ |
| カード取得 | 部分的に取得 | 部分的に取得 | なし | XR選択／Held状態 | 操作中状態として保持。永続取得イベントなし |
| ドラッグ移動 | 部分的に取得 | 部分的に取得 | なし | Transform/XR | 軌跡・フレーム列を収集しない |
| カード配置 | 取得済みだが未保存 | 取得済みだが未保存 | なし | 最新配置・回答 | 成功ごとのイベント列はない |
| 再分類 | 部分的に取得 | 部分的に取得 | なし | 配置revision／最新値上書き | GUIDは回数や順序を表さない。A→B→Aも復元不能 |
| ドラッグキャンセル／無効ドロップ | 部分的に取得 | 部分的に取得 | なし | Release引数／復帰処理 | 処理はあるが履歴として未収集。E14/E15 |
| 参加者別操作回数 | 未実装 | 未実装 | なし | — | 現在CSVから正確な集計は不可 |

### 3.4 Proposed固有

| データ項目 | Normal | Proposed | CSV出力 | 保存箇所 | 備考 |
|---|---|---|---|---|---|
| 参加者別独立回答／各カードTier | 対象外 | 取得済みだが未保存 | なし | candidate/answered/selectedAt、Reveal Snapshot | Resetで破棄。E8/E10 |
| Submit時点の回答Snapshot | 対象外 | 部分的に取得 | なし | Submit Proof id/hash＋ロック中の回答 | Submitごとの独立した回答配列の保存ではない。SHAハッシュから回答は復元不能。E9 |
| Submit／Cancel履歴 | 対象外 | 未実装 | なし | 現同意状態だけ同期 | 現在値とConsoleのみ。時系列履歴なし |
| Reveal時点の回答 | 対象外 | 取得済みだが未保存 | なし | GetRevealedSnapshot() | 全員のカード別回答を含む。Resetで消える。E10 |
| Reveal後のカード移動履歴 | 対象外 | 未実装 | なし | 共有ボードの最新状態のみ | Normalと同じ共有同期経路。E11/E13 |
| 独立回答から最終回答への変化 | 対象外 | 取得済みだが未保存 | なし | RevealとFinalのRAM Snapshot | 実行中は比較可能。保存後CSVだけからは不可 |

### 3.5 最終結果

| データ項目 | Normal | Proposed | CSV出力 | 保存箇所 | 備考 |
|---|---|---|---|---|---|
| 全カードの最終分類先 | 取得済みだが未保存 | 取得済みだが未保存 | **なし** | FinalAgreement.finalPlacements／BoardRecord | OnBoardConfirmedの引数をLoggerは利用しない。E1/E12 |
| 最終確定時刻 | 部分的に取得 | 部分的に取得 | あり | P/S | PC観測値のみ |
| 参加者別Ready／Agreement状態 | 取得済みだが未保存 | 取得済みだが未保存 | なし | finalReady／BoardRecord.participants | CSVの参加者一覧は開始時点であり、個々のReady履歴ではない |
| 最終確定を行った参加者 | 部分的に取得 | 部分的に取得 | 明示列なし | 発行者はparticipants最小ClientNo | プロトコル上の発行担当。最後にReadyした人・最終案を決めた人ではない。E12:131–153 |
| 正常終了／中断状態 | 部分的に取得 | 部分的に取得 | 明示列なし | Confirmed有無を代理にする | Confirmedがない理由は未完・中断・保存欠損のいずれとも断定できない |

## 4. CSV出力仕様とサンプル

### 4.1 保存先・タイミング

通常の保存先は `Application.persistentDataPath/ExperimentLogs/<Session ID>/`（E2:24–26）。今回Editorから読み取った実パスは `/Users/alisunsun/Library/Application Support/DefaultCompany/CoPlaceQuestSetup/ExperimentLogs`。Windowsなどのパスは端末ごとのpersistentDataPathを確認すること。

| ファイル名 | 役割 | 行数 |
|---|---|---|
| phase_events.csv | 観測イベントの正本 | ヘッダー＋イベント。正常開始・確定まで通常5イベント |
| session.csv | phase_eventsから再構築する概要 | ヘッダー＋1行 |
| card_set.csv | 既存スキーマを変えずにセットを記録する追加ファイル | ヘッダー＋1行 |

Questは`!Application.isMobilePlatform`条件で書き出し対象外。PCでもExperimenter以外は書き出さない。**Standalone PCのAutoByPlatformはEditorと異なりParticipantになる**ため、PCビルドを実験者に使う場合は既存Role設定を明示する必要がある（E5:49–50）。

自動保存：通知を受けた都度Record→Drainを同期実行し、ファイルをflushする。手動の汎用「Save CSV」操作はない。Reset前・コンポーネント無効化・終了時にも書き出しを試行する。試行終了を待ってまとめて出力する方式ではない。

同じSessionを再保存するとイベントIDで重複を抑止し、CSV全体を一時ファイルから置換する。追記専用／不変アーカイブではない。Start受信後には過去行のconditionを補完するため、同Sessionの既存ファイル内容が更新される。正常な別Session Resetで旧フォルダを削除する処理はない。

### 4.2 phase_events.csv（schema_version=1）

| カラム | 型・形式 | 意味 |
|---|---|---|
| schema_version | 整数文字列、現在1 | スキーマ版 |
| session_id | 文字列、通常32桁GUID（ハイフンなし） | Session識別子 |
| condition | 空／Normal／Proposed | Startで確認された方式 |
| event_type | 文字列 | SESSION_CREATED、SESSION_RESET_STARTED、SESSION_RESET_COMPLETED、EXPERIMENT_STARTED、BOARD_CONFIRMED |
| event_time_utc | ISO 8601 round-trip文字列 | PCが通知／状態を観測したUTC。DateTimeOffset.UtcNowの`o`、通常`+00:00` |
| session_elapsed_seconds | 不変カルチャ小数、6桁／空 | 最初にそのSessionを観測してからのローカルStopwatch秒。**課題開始からの秒数ではない** |
| participant_id | 文字列、通常空 | 将来の操作Adapter向け。現行フェーズイベントは参加者に帰属させない |
| event_id | 文字列 | Session＋イベント種別、StartはさらにStart GUID。Session内の重複判定キー |
| session_epoch | Int64文字列 | 同期Sessionの世代 |
| observer_client_id | 整数の文字列表現／空 | 保存したExperimenterの通信ClientNo |
| observer_run_id | GUID文字列 | CsvLogStoreインスタンスごとのID。機械ID／研究参加者IDではない |
| observation_kind | 文字列 | notification、received_reset_control、received_start_record、retained_snapshotなど。テストstore直呼びではliveもある |
| time_basis | 固定文字列 | `experimenter_observation_utc; elapsed=local_monotonic_since_first_session_observation` |
| participants_at_start | セミコロン区切りClientNo／空 | Start時点の参加者一覧。通常Start行のみ値を持つ |

### 4.3 session.csv

| カラム | 型・意味 |
|---|---|
| schema_version | 整数文字列1 |
| session_id | Session文字列 |
| condition | 空／Normal／Proposed |
| session_epoch | 最初のイベントのEpoch、Int64文字列 |
| first_observed_utc | 最初の行のUTC観測時刻。元のSession生成時刻とは限らない |
| experiment_started_observed_utc | 最初のEXPERIMENT_STARTEDのUTC／空 |
| board_confirmed_observed_utc | 最初のBOARD_CONFIRMEDのUTC／空 |
| reset_started_observed_utc | 最初のSESSION_RESET_STARTEDのUTC／空 |
| reset_completed_observed_utc | 最初のSESSION_RESET_COMPLETEDのUTC／空 |
| participants_at_start | Start行のClientNo一覧／空 |
| time_basis | 上記固定文字列 |

`card_set.csv`は`session_id`（文字列）、`card_set_id`（A/B/Developmentの文字列）の2列。通常のLoggerは全イベントに現在セットIDを添える。store直呼びでCardSetIdを空にすればsidecarは作られない。

共通仕様：UTF-8 BOM、レコード区切りCRLF、全フィールドをダブルクォート、内部クォートを二重化。埋め込み改行・カンマ・日本語を扱う（E3:119–157）。数値はCSV上の文字列であり、読込側で型変換する。

### 4.4 今回実際に生成したCSVの抜粋

以下は**コードから推定した架空の出力ではなく、今回の隔離単体検証が生成したファイル**からStartとConfirmedの2行を抜き出したもの。日時・Session名・IDはテストfixtureの合成値で、実験参加者の測定データではない。本番のGUID形式とは異なる。

元ファイル：`/private/tmp/coplace-log-audit-z4_7dp_d/unit/store/normal-session/phase_events.csv`。

```csv
"schema_version","session_id","condition","event_type","event_time_utc","session_elapsed_seconds","participant_id","event_id","session_epoch","observer_client_id","observer_run_id","observation_kind","time_basis","participants_at_start"
"1","normal-session","Normal","EXPERIMENT_STARTED","2026-10-15T00:00:05.0000000+00:00","5.000000","","normal-session:EXPERIMENT_STARTED","1","99","run-1","live","experimenter_observation_utc; elapsed=local_monotonic_since_first_session_observation","1;2"
"1","normal-session","Normal","BOARD_CONFIRMED","2026-10-15T00:00:45.0000000+00:00","45.000000","","normal-session:BOARD_CONFIRMED","1","99","run-1","live","experimenter_observation_utc; elapsed=local_monotonic_since_first_session_observation",""
```

同ファイルには後続の文字列・時計・再起動テスト行も存在するが、通常の実験がそれらを記録する意味ではない。上の同一run例なら作業時間は45−5=40秒。

追加検証で実生成された別Sessionのsidecar（`/private/tmp/coplace-log-audit-z4_7dp_d/sidecar/audit-set-A/card_set.csv`）：

```csv
"session_id","card_set_id"
"audit-set-A","A"
```

リポジトリにも`Documentation/LogSamples/{Normal,Proposed}/{phase_events,session}.csv`の既存テスト出力がある。今回Python標準csv.readerで既存Normalサンプルを読み、ヘッダー＋5行、実CRLF、BOMを確認した。こちらは過去の検証fixtureであり、本監査で実機から取得したデータではない。今回、稼働PCの実保存先にSession CSVが存在することも読み取り確認したが、Quest 2名の評価実験が成立した証拠にはしていない。

## 5. 評価指標の算出可否

「可能」は開始・確定をPCが継続観測し、同じ信頼できるrunのelapsedが残った場合。実際に参加者が動き始めた時刻とは区別する。

| 評価指標 | 算出可否 | 使用するログ | 算出方法 | 不足データ・注意点 |
|---|---|---|---|---|
| 実験全体の所要時間（Normal／Proposed比較） | **条件付き可** | P.EXPERIMENT_STARTED、BOARD_CONFIRMED | 同一Session・同一observer runでelapsed_confirmed−elapsed_start | Start→確定の課題時間。説明・アンケート・準備時間は含まない。再接続後の過去状態観測は真の開始ではない |
| 共同分類に要した時間（両方式比較） | Normalのみ条件付き可、比較不可 | Normalは上記2イベント | NormalはStart→確定を共同分類と定義可能 | Proposedの共有ボード操作開始／Reveal時刻が未保存。単に総時間から差し引くことはできない |
| 独立回答に要した時間（Proposedのみ） | 不可 | Startだけ存在 | 共通開始→各人Submit、または全員Submitまで、という定義が必要 | 個別Submit・Revealの時刻なし。最新selectedAtの最大値はSubmitを表さない |
| 参加者ごとのカード操作回数（両方式） | 不可 | 対応ログなし | 成功した手動配置イベントを参加者・Session別に集計する定義などが必要 | 取得／移動／有効配置／同Tier再配置のどれを数えるか未定。Actorと成功履歴がない |
| 参加者間の操作回数の偏り（両方式） | 不可 | 同上 | 例：絶対差、各人割合など | 操作回数がないため計算不能。操作権限・待ち時間も影響する |
| カードの再分類回数（両方式） | 不可 | 同上 | 有効な確定操作でbefore Tier≠after Tierを数える等 | 最新Tierとrevisionから過去の回数を復元できない。初回配置・Unclassified復帰の扱いも定義が必要 |
| 独立回答間の一致率（Proposedのみ） | CSVから不可、実行中RAMなら可 | Reveal Snapshot（未保存） | 同じCard IDのTierが一致した枚数÷8 | 2名×8枚の公開時回答を保存する必要。欠損時分母・除外を事前定義 |
| 独立回答と最終回答の一致率（Proposedのみ） | CSVから不可、実行中RAMなら可 | Reveal Snapshot＋FinalPlacements（両方未保存） | 各人について初期Tier＝最終Tierの枚数÷8 | 初期・最終の両Snapshotが必要。合意結果との一致は因果的な影響量そのものではない |

「自分の意見の反映」「他者理解」「参加感」「納得感」「操作性」「他者影響」「認知負担」はアンケート等の別データにSession／Pairの対応付けを行う。**操作回数だけから意見の反映度・発言量・議論への貢献度を直接測定したとは判断しない。** 音声や発言内容を測る必要があるかは別の研究設計判断であり、本監査は自動音声収集の追加を必須としない。

時間分析時は`condition`が空、Start/Confirmedが不足、observer_run_idが不一致、elapsedが空、負の差分、観測中断ありの試行を要確認にする。UTC差はPC時計補正の影響を受けるため、同runの単調時間差を優先する。retained_snapshot行を除外しても、再接続でStart markerを初めて受けたケースが`received_start_record`になる可能性は残る（E6/E1）。observation_kindだけで継続観測を証明できない。

## 6. Session Resetとログ保護の評価

### 6.1 保存済みデータと未保存状態の区別

| 確認事項 | 評価・根拠 |
|---|---|
| Reset前CSVの保持 | 通常の新GUID Resetは旧フォルダを削除しない。E3/E4。過去検証でバイト一致を確認済みだが今回は統合テスト未再実行 |
| 同Sessionの再保存 | 既存CSV全体を置換する。Event ID重複は行追加しないが、summary再構築やcondition補完は行う。単純に「一切上書きしない」とは言えない |
| 未保存キュー | Reset前にCaptureSnapshot＋Drainし、pending.Count=0かつLastError空を確認。失敗なら新Controlを送らない（E2:128–132、E4:310–311） |
| メモリの独立／最終回答 | **CSVに送るAdapter自体がないので、上記flushでは保存できない**。Resetで破棄される。E1:86–91、E10:51–54、E12:324–329 |
| 新Session ID／Epoch | GUID新規、既存Epoch＋1。サーバー／Room初期化を跨ぐグローバルな連番ではない。E4:313 |
| 旧データ混入対策 | 値のSessionラッパー、Controlの古いEpoch拒否、SnapshotのSession/Epoch、キャッシュ初期化、ACK。通常経路は分離される。E4/E10/E16 |
| Normal→Proposed | 別Sessionのconditionに実際のStart方式を保存。Reset作成時modeと次のLobby選択は異なり得るため、分析にはCSV conditionを使う |
| A→B | ResetがNextSessionSetをControlへ固定し、Cを別フォルダへ保存。実験中Inspector変更では現在セットを変えない |
| 保存失敗後のReset | 検出済みの旧キュー失敗なら拒否。**Loggerが欠落した場合はnullを許容するため阻止しない**（E4:310–311）。今回の稼働SceneではLogger有効・manager同居を読取確認 |
| Reset中の新Sessionログ失敗 | Reset開始前の検査で将来の書込成功まで保証しない。新Session通知後の保存失敗はキュー保持・UI表示されるがACK進行は独立。次の課題開始もLogger失敗を条件としていない |
| 二重実行 | ローカルUI即時ロック＋manager IsResetting/pending guard。保存ID単位で重複抑止。複数PCの別Experimenterが同時Resetする分散排他までは保証しない |
| プロセス強制終了 | pendingはRAM。OnQuit/OnDisableで再試行するがI/O不能や強制終了を超えて持ち越す永続ジャーナルはない |

### 6.2 指定の連続実験シナリオ

以下は**静的解析＋過去のオフライン統合テスト結果に基づく評価**。今回稼働中Sessionへ実際のResetを送ってはいない。

| 手順 | 保存／分離の見込み | 失われる・不足するもの |
|---|---|---|
| 1 Normal・A開始 | 新Session S1にReset関連、Start、C=A、condition=Normal | Pair・安定Participant ID・順序なし |
| 2 8枚分類 | 共有Tierは同期される | 操作者・配置履歴・回数はCSVにない |
| 3–4 最終確定・CSV保存 | 自動でS1のBOARD_CONFIRMEDとsummaryを保存。独立したSave操作は不要 | 全8枚の最終TierはCSVにない |
| 5 Reset | S1のpendingをflush。S2新規でReset開始／完了を記録。S1のCSVは保持 | S1のRAM最終配置が消える。旧Sessionを終了したResetのリンク／中断理由なし |
| 6 Proposed・B開始 | **このReset前にNextSessionSet=Bにする必要がある**。LobbyでProposedを選びStartしS2に保存 | AのままReset完了後にInspectorだけBへ変えてもそのS2はBにならない。別Resetが必要 |
| 7 独立回答・Submit | client別最新回答・同意状態を同期 | 独立回答CSV・Submit時刻／履歴なし |
| 8 Reveal | 全員の初期回答Snapshotが取得される | Snapshot永続化・Reveal時刻なし |
| 9–10 共同分類・確定・CSV | S2のBOARD_CONFIRMED、condition=Proposed、C=Bを保存 | 共同分類時間・再分類履歴・最終Tierは保存されない |
| 11 Reset | S2の既存CSV保持、次Session S3準備 | S2の初期／最終回答はRAMから消える |
| 12 次ペア開始 | 新GUIDなら別フォルダで保存可能 | ペア交代・研究ID・counterbalance順序を自動認識しない。手動対応表が必要 |

**結論：セッションごとのフェーズCSVは独立保存できる設計だが、現在のCSVだけでは各セッションの回答内容を独立分析できない。**「保存成功」と「研究指標に必要な全データが保存された」は別条件である。

## 7. データの信頼性に関する問題

### 7.1 操作者・イベントの意味

- Proposedの独立回答はclient別変数なので所有者を区別できる。ただしClientNoは人の永続IDではなく、P1/P2表示も現在の参加者並び順から付ける（E5、E8、E10:157）。再接続で同じ番号・同じ表示になる保証を研究データ管理として置いていない。
- Normal／Reveal後の共有Tier値にはauthorがない。受信callbackのoldValue/newValueやrevisionを数えても、他人の操作、再接続復元、Reset、修復送信を区別して各人へ帰属できない。
- 現行のPは操作を記録しないため、プレビューを「操作回数に誤加算」する実装もまだない。将来コールバックを単純に数えると誤集計する。記録場所は有効なローカル操作が受理された箇所とし、同期適用・プレビュー・初期化を除外する必要がある（E13–E16）。
- 無効Tier・領域外・権限なし・cancelは復帰／拒否されるが、その失敗・取消はCSVにない。成功配置回数だけを指標にする場合も、キャンセルを別指標にする場合も定義が必要。
- 既存ConsoleにはCandidate Send、Normal Placement Send、Card Drop、Board Confirmedなどがあるが、全QuestのConsoleをPCのCSVへ集約していない。Editor.logは試験出力・再送・復元を含み、カードごとの確定Consoleを研究用Snapshotの代替にするのは不適切。

### 7.2 重複・順序・時刻

- storeは同じSession内のevent_id一致で重複を排除する。再通知・再起動後もCSVを読んで抑止する。IDが違う同一意味イベントは自動統合しない。BOARD_CONFIRMEDはSession単位固定、StartはStartRecord GUID付き（E1/E3）。
- 重複は最初の観測行を保持する。最初が遅延復元なら、後から正しい発生時刻に修正する機構ではない。重複検査はpayload全体やEpoch整合性まで検証しない。
- Pの順番はPCが記録した順番。ネットワーク遅延・同フレームの状態更新・再接続Snapshotにより、Questで起きた操作順と同じとは限らない。全端末共通のsource sequence番号はない。
- PのUTCはPC時計なので、P内部の時刻比較にQuest時計ずれは直接入らない。一方、Proposed.selectedAtは各QuestのUTC。将来これをPC時刻や他Quest時刻と引き算する場合は時計ずれ対策が必要。現行実装にオフセット推定・時刻同期の記録は見当たらない。
- Stopwatchは時計補正に強いが、PCプロセス／store再生成を跨ぐoriginは復元しない。既存Sessionを開き直したstoreは新規行のelapsedを空にする（E3:61–65）。CSVの桁数が6桁でも実測精度がマイクロ秒という意味ではない。

### 7.3 切断・障害・複数保存主体

- 最新のStart/ConfirmedがRoomに残っていれば再接続で検出できる場合がある。ただしPCが不在の間のSubmit→Cancelや配置の中間状態は履歴転送されない。確定後に他PCがResetしてRoom結果を消した場合、後から入ったPCは旧試行を回収できない。
- CaptureSnapshotの補完時刻は復元した時刻であり、元のStart／確定時刻ではない。`retained_snapshot`は分析上要注意。ネットワーク状態の回復と計時データの回復は同義ではない。
- 1ファイルのtmp→Replace/Moveはあるが、P→S→Cの3ファイル同時トランザクションではない。summary失敗は同イベント再試行で修復する。電源断の全プラットフォーム保証やディレクトリfsyncまでは確認していない。
- **今回の追加検証では、同Sessionの異なるセットをstoreへ直接渡すと、PとSが先に更新されてからCの矛盾検査で例外になることを確認した。** 通常のSession controllerは同IDでセット変更を拒否するので通常操作の再現障害ではないが、保存失敗＝一切書かれていないという保証はない（E3:85–103、E4:117）。
- 同じ保存先に複数writerが同時Recordする場合、固定名`.tmp`の競合／全体読込後置換によるlost updateが起き得る。プロセス間ロック・全CSV排他・単一PCのwriterリースはない。これは静的に推定したリスクであり、同時writer障害を今回実機再現したわけではない。運用を実験者PC1台に限定すれば大幅に回避できる。
- 既存の保存失敗はキュー保持・5秒後再試行だが、後続イベントのRecordもDrainするため、必ず5秒間隔だけで再試行するわけではない。先頭の不正／不整合イベントが残ると後続も止まる。

## 8. 実験データの紐付け方法

大規模な改修をせず、**外部の試行対応表1枚**を使う方法を推奨する。アプリCSVを編集して個人情報を混入させず、Session IDでjoinする。

提案する対応表の列（未実装の運用案）：

```text
pair_id, participant_1_id, participant_2_id, trial_order, allocation_group,
session_id, mode, card_set_id, p1_client_no, p2_client_no,
started_date_local, status, exclusion_reason, questionnaire_key, notes
```

- pair_id=`PAIR001`、研究用個人ID=`P001/P002`などの匿名IDを使用。氏名との対応はアクセスを分けた別管理にする。
- 実験者がSession準備後／Start後にSession ID、実際のMode・Set、通信ClientNoと人の対応を転記・照合する。再接続でClientNoが変化した場合は追記する。
- 1行1試行。3行目以降の中断・再試行も別Sessionとして残し、予定順序と実際の試行番号を区別する。Epochから実施順序を復元しない。
- アンケートには同じ研究用Participant ID、pair_id、trial_orderまたはSession IDを付ける。実験全体の順番だけでは紐付けない。

| 割付グループ | trial_order=1 | trial_order=2 |
|---|---|---|
| 1 | Normal・A | Proposed・B |
| 2 | Proposed・A | Normal・B |
| 3 | Normal・B | Proposed・A |
| 4 | Proposed・B | Normal・A |

これは提案する管理法であり、現行CSVがこの表を自動生成するとは報告しない。カウンターバランスはModeとSetを分けて記録し、集計時に片方だけの効果として扱わない。カード画像／manifestの版・アプリコミット・ビルド版も実験日の管理票に添えると、IDの意味を後から検証できる。

## 9. 問題点・不足機能の優先度一覧

P0はデータ消失／破損に直結する条件付きリスクを含む。全候補指標の実装を一律に必須とはしない。「小」は少数のAdapter／出力／設定確認、「中」は送信・重複抑止・検証を伴う変更の見込みであり、工数の確約ではない。

| ID／優先度 | 概要・発生条件 | 研究への影響 | 根拠 | 推奨対応・規模 | 必要性／手動代替 |
|---|---|---|---|---|---|
| R1 P0（回答を分析する場合） | 回答Snapshotを保存せずReset | 独立／最終回答が回収不能になる | E1:86–91、E10:51–54、E12:324–329 | 最終確定時にFinal、ProposedのReveal時に全初期回答をSession別へ保存。小〜中 | 回答比較を採用するなら実験前必須。暫定的にReset前の全カード結果・各人初期回答を撮影／手記し二重確認。ただし16初期回答＋8最終の手作業負担あり |
| R2 P0（I/O障害継続＋終了） | pendingがメモリのみ、保存不能状態で終了／クラッシュ | 未保存イベント消失 | E2:19,45–57,134–153 | 保存状態を確認して実験停止・保存先復旧・バックアップ。永続キューは必要なら追加、中 | 最低限の運用必須。終了前に未保存0・CSV実在確認。強制終了は手動では完全保証不可 |
| R3 P0（構成不備／複数writer時） | Logger欠落でもReset可能、または同じ出力先に複数writer | 無記録のまま開始／競合による欠損 | E4:310–311、E3:120–130 | Logger有効・参照・保存可を開始前確認、writer PCを1台へ。厳密なfail-closedは小、分散排他は中 | 今回Loggerの配置は正常。事前点検で代替可。分散writer機構の全面追加は不要 |
| R4 P1 | Submit／Reveal／共同分類開始時刻なし | Proposedのフェーズ時間を分けられない | E1、E9–E11 | 採用する区切りだけPC観測イベントを追加。source時刻と区別。小〜中 | フェーズ時間を主指標にするなら必要。PC録画＋手動コーディングも可 |
| R5 P1 | 操作者付き成功操作履歴なし | 操作回数・偏り・再分類回数が算出不能 | E8/E13–E16 | 成功したローカル操作のActor・Card・before/after・ID・時刻を集約する場合は中 | 操作指標を採用する場合のみ必要。視点・人を識別できる録画で手動集計可能だが負担大 |
| R6 P1（時間を主指標にする場合） | PC切断／再接続・再起動で時刻が復元観測へ置換 | 真の作業時間と異なる短時間・長時間を得る | E1:93–106、E3:61–65、E6 | 継続観測を必須化、中断試行を再試行／除外。必要ならStart/Confirmedのsource時刻記録、小〜中 | 障害試行の運用基準と実機検証が必要。録画時刻で代替可 |
| R7 P2 | Pair／安定Participant／実施順序なし | 方式間の対応付け・counterbalanceを誤る | E2/E5 | §8の外部対応表。コード変更不要 | 本実験前に運用必須。手動で代替可能 |
| R8 P2 | 正常終了／中断理由・旧Session→次Sessionリンクなし | 欠損を中断と誤認、再試行の除外困難 | E1/E4 | 管理票にcompleted/aborted/retryと理由を記入。将来終了イベント追加は小 | 手動代替可。空Confirmedを自動的に中断としない |
| R9 P2 | 現在Expected Participant Count=1、PC PlayerのAuto Role注意 | ペアでない試行や保存者不在で実施 | E5:25–50、今回読取 | 本番前に2・Quest Participant・PC Experimenterを点検。設定確認のみ | 必須確認。今回は変更していない |
| R10 P2 | C整合性検査がP/S保存後、3ファイル非トランザクション | 異常構成時に一部ファイルだけ更新 | E3:85–103、追加隔離検証 | 解析前に3ファイル整合性を検査。必要時は書込前のC検査へ移動、小 | 通常controllerが防ぐため直ちに全面改修不要。手動検査可 |
| R11 P3 | アプリ／カードmanifest版・ログ一覧・任意バックアップUIなし | 再現性・回収作業の利便性 | E3列定義、E17 | 管理票へコミット／ビルド記録、Sessionフォルダをバックアップ | 手動代替可。今回の前提では新UI不要 |

## 10. テスト・検証結果

### 10.1 今回実行したもの

Unity 6000.3.17f1の稼働Editorで、SessionはPlay Mode中だった。**ユーザーのPlayを停止・Resetせず**、既存テスト全体が固定出力先を上書きすることを避けた。アプリコードを変更せず、`unity command eval`で既存`ExperimentLoggerVerification.cs:22–76`の保存クラス単体部分を取り出し、出力rootだけ一時フォルダに置いて実行した。これはフル統合Runの実行ではない。最初の監査用eval組み立てはコンパイル失敗し、試験コード実行前に停止した。組み立て訂正後の実行は成功した。

保存先：`/private/tmp/coplace-log-audit-z4_7dp_d/`（一時フォルダであり、長期保存保証はない。実験CSVではない）。

| 実行内容 | 結果・範囲 |
|---|---|
| 既存保存単体検証16項目 | **16/16成功**。ファイル生成、Start前空condition、Startでの方式補完、6桁不変カルチャ秒、同ID重複抑止、summary整合、和文／引用／改行、BOM、時計補正に依存しないelapsed、再起動重複抑止、再起動後elapsed空、summary修復、新Session分離、パストラバーサル拒否、方式変更拒否、I/O失敗復旧 |
| 追加の隔離store検証 | sidecar Aを実生成、同ID再保存の行抑止とPバイト保持を確認。異なるSet Bを同Sessionへ渡して例外になること、および例外前にPへ新イベントが入る非トランザクション性を確認。正常経路のSet変更テストではない |
| Python標準CSVパーサで出力を独立読取 | 今回生成したnormal-sessionのPはヘッダー＋8テストイベント、next-sessionのSは独立Sessionの1概要行。既存サンプルも正常に読取 |
| 既存成果物の保護 | 監査開始時点のDocumentationと既存Logger/Result/Pilot検証領域のCSV/JSON **344ファイルをSHA-256比較し、変更0** |
| 稼働設定の読み取り | Logger有効、ExperimentManagerと同じGameObject、Role=Experimenter、ExpectedParticipantCount=1、実ログ保存先を確認。実験の開始・操作・Resetは行っていない |

この単体試験はEvent IDの重複・Session別フォルダ・ファイル形式の保証を確認する。Quest起点操作の配送欠損・再接続・Resetの全端末ACKは確認しない。

### 10.2 保存済みの過去テスト結果（今回再実行していない）

| テスト | コード | 保存済み結果 | 本監査での扱い |
|---|---|---|---|
| Logger全体 | Assets/Editor/ExperimentLoggerVerification.cs:14–113 | Documentation/ExperimentLoggerVerification.json、SessionResetVerification.json内に29項目PASS | 過去結果とテスト内容を照合。今回の16項目と混同しない |
| Result／Reset統合 | Assets/Editor/ExperimentResultVerification.cs:20–247、特に164–204 | SessionResetVerification.json内に78項目PASS | 両方式、取消・連打・Participant拒否・I/O失敗によるReset中止を含む過去のオフライン結果 |
| Pilotセット統合 | Assets/Editor/PilotCardSetVerification.cs:59–128 | Documentation/PilotCardSetVerification.jsonで427項目PASS | A/B/Development×両方式、全員ACK、旧CSVバイト保持、独立回答・Reveal・確定。指定のNormal A→Proposed B→次ペアをそのまま今回実行した結果ではない |
| 回転／初期化回帰 | Assets/Editor/PrivateAnswerCardPoseVerification.cs:18以降 | SessionResetVerification.json内に19項目PASS、Error 0 | 主にPoseとPilot連携。研究ログの網羅性を示すテストではない |

関連する`IndependentAnswerSubmissionVerification.cs`、`ProposedRevealVerification.cs`、`ProposedSharedPlacementVerification.cs`、`ExperimentLobbyVerification.cs`等も存在するが、今回実行していない。これらのScene fixtureには旧3枚など個別前提があり、今回の監査で一律の成功扱いにはしていない。

**今回未実行／確認不能：** フルLogger/Result/Pilot統合Run、実Roomでの2台Quest配送、同時writerの競合再現、強制終了中の保存耐性、PC・Questの時刻ずれ測定、指定連続試行の実機走行。稼働中Sessionと既存成果物を保護するためフル統合Runを控えた。Unity Editorが止まった保守時間帯に、別出力先／隔離プロジェクトで行うのが望ましい。

監査完了時、作業ツリーに`Assets/Scenes/QuestSetup.unity`の変更を検出した。今回の監査はScene変更／保存APIを呼び出しておらず、この変更の出所・意図は確認していない。変更は復元・破棄せず保持した。全体の`git diff --check`はこのSceneの空白を指摘したが、監査レポート自体には末尾空白を追加していない。

## 11. 本実験開始前に最低限必要な修正

本項は推奨であり、**今回実装していない**。

1. **まず採用する定量指標を決定する。** 全体所要時間＋主観評価だけを採用する場合と、回答一致率／フェーズ時間／操作回数を採用する場合では必須の修正が異なる。
2. 回答を研究資料に使うなら、Reset前に確実に残す。最小の実装候補はFinal Agreement受信時の全カードFinal Snapshotと、Proposed Reveal時のparticipant別Initial Snapshotの追加保存。Session ID、Card ID、Participantの研究ID対応、Tier、観測時刻を付ける。詳細ドラッグ軌跡の実装は不要。暫定手動記録でもよいが、全回答の記録確認後にResetする手順が必要。
3. フェーズ時間を主要比較にするなら、個人Submit、Revealまたは共有ボード有効化のうち採用する区切りだけを保存する。計時の定義は両方式で事前固定する。
4. 操作回数を採用する場合だけ、Actor付き成功イベントの収集・送達／重複抑止・回数定義を用意する。現状CSVの変数更新数を操作数として流用しない。採用しないなら大規模な操作ロギングは本実験の前提にしない。
5. 外部対応表と保存確認手順、PC継続観測・中断除外ルール、writer PC1台、本番参加者数2・Role点検を決める。これはアプリ改修なしで実施できる。
6. Quest 2台で次項を完了する。保存失敗の警告が残っている状態や、新SessionのCSVが作られていない状態では開始しない。実施後にSessionフォルダを別媒体等へコピーし、取得した指標データの有無を確認する。

## 12. Quest 3実機での確認チェックリスト

**すべて本監査では未検証。** PC画面の見た目／オフラインテストの成功を代用しない。

- [ ] PC Experimenter 1台、Quest Participant 2台、Expected Participant Count=2、同じRoomで接続。参加者2名の研究IDとClientNoを対応表へ記録。
- [ ] Normal・A開始でS1の3 CSVを確認。Start時Mode/Setと対応表が一致する。
- [ ] 各人が8枚を操作し確定。PのBOARD_CONFIRMEDが1行、Sの確定時刻が存在する。CSVには最終Tierがない現状を踏まえ、採用する結果保存方法で全8枚を別途確認。
- [ ] Reset確認をCancelしたときS1の状態を保持。Confirmの連打でEpoch／Sessionを重複生成しない。
- [ ] Reset前にBを選択し、ResetでS2へ。2台のReady・Submit・Reveal・Confirmed・掴み・ハイライト・Resultが消える。旧CSVの内容を退避コピーと比較。
- [ ] Proposedを選びStart。両人の独立回答、Submit取消→再Submit、同時Reveal、共同分類、Final Confirmが正常。採用するInitial／Finalデータが保存されていることをReset前に確認。
- [ ] S2にcondition=Proposed、C=B、S1とは別GUID。S1のNormal/Aログが残る。2回目Reset後に別ペアの対応表行を用意し、以前の個人IDを使い回さない。
- [ ] Proposed→Normal、B→Aを含む割付グループの残りも確認。Epochは人／順序の代替にしない。
- [ ] ParticipantからResetできない。PCの表示で進行／成功／失敗が分かる。
- [ ] テスト用の別保存先で保存不能・復旧を試す。旧未保存キューがあるとResetを拒否し、復旧後に重複なく保存する。実験本番CSVを障害試験に使わない。
- [ ] Start前・分類中・確定直前のPC切断／再接続、Quest切断／再接続を別のテストSessionで確認。回復可否と**時刻の妥当性**を別々に評価し、中断試行を適切に除外できる。
- [ ] PC再起動でelapsedが空になるケースを確認し、継続runと誤って計算しない。録画等のバックアップと突合。
- [ ] 実験終了後にCSVをExcel等で読み、BOM／和文／列数／ID／mode／set／Start・Confirmの1行性、必要回答データ、対応表joinを確認。

## 13. 本実験開始可否の総合判定

**CONDITIONAL GO：現状のまま無条件に本実験へ進むことは推奨しない。**

根拠は、Session・方式・セット・PC観測の開始／確定イベントの保存経路には基本的な保護と単体検証がある一方、候補の主要定量指標の多くに必要なデータが永続化されていないためである。特に、Reset後に回答を回収できると誤解して進めると、試行をやり直せない形で回答データを失う。

次の条件が満たされれば、評価指標を限定した本実験は可能と判断する。

- 必須指標と時間境界を決め、取得できない候補指標を計画から外すか、最小の追加保存／手動記録で補う。
- 必要な回答データをReset前に永続保存する手段を決め、ペア・参加者・試行順の対応表を準備する。
- PCログの継続観測と保存確認、障害時中断／再試行、単一writer運用を定める。
- 参加者数2・Roleを確認し、実Quest 2台で連続試行と保存データの回収を通す。

全体所要時間＋アンケートに限定する場合は、詳細操作ログを全面実装する必要はない。ただし、現在のCSVだけで操作回数・フェーズ別時間・初期／最終一致率も評価できるという計画のままでは、その評価部分は実施不可である。アプリが動作すること、ログ保存が成功すること、研究上必要なデータが揃うことを別々の完了条件として扱う。
