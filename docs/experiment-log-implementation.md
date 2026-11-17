# CoPlace 回答Snapshot保存・フェーズ別時間計測 実装報告

実施日：2026-11-17。監査レポート `experiment-log-audit.md` に基づく追加実装。既存の未コミットScene変更と監査レポートは保持した。commit／pushは今回行っていない。

## 1. 実装内容の概要

既存のExperimenter PCのLoggerと保存キューを拡張し、次を自動保存する。

- Normal／Proposedの確定済み全カード：`final_placements.csv`。
- Proposedの確定済みReveal Snapshot：`independent_answers.csv`。
- PCが観測したSubmit、Submit取消、Reveal、共有分類開始を既存`phase_events.csv`へ追加。
- Logger欠落・無効・保存失敗・不完全な必要Snapshotがある場合はResetを拒否。未確定の中断Resetは可能とし、旧Sessionに`SESSION_ABORTED`を記録する。

8枚・2名という固定値をアプリ処理に入れず、実際のCard ID一覧と確定Snapshotを使用する。既存のSession ID／Epochラッパー・同意ID・Reveal ID・Final Agreementを再利用し、新しい通信変数やカード操作ログは追加していない。

## 2. 変更ファイル一覧

| ファイル | 変更内容 |
|---|---|
| [Assets/Scripts/ExperimentLogger.cs](../Assets/Scripts/ExperimentLogger.cs) | 確定回答のコピー・キュー投入、Submit/Cancel観測、Reveal/共有開始購読、復元区別、保存前確認、中断イベント |
| [Assets/Scripts/ExperimentCsvLogStore.cs](../Assets/Scripts/ExperimentCsvLogStore.cs) | 回答2 CSV、不変保存、Session/Epoch/方式/セット検査。セット不整合を既存CSV書込前に検査 |
| [Assets/Scripts/ExperimentSessionManager.cs](../Assets/Scripts/ExperimentSessionManager.cs) | Logger欠落時も保存確認を迂回させない。Reset送信受付後に旧試行の中断を記録 |
| [Assets/Scripts/FinalAgreementManager.cs](../Assets/Scripts/FinalAgreementManager.cs) | 現在の対象Card ID一覧を読み取り専用で公開。確定の仕組みは変更なし |
| [Assets/Scripts/ProposedSharedPlacementController.cs](../Assets/Scripts/ProposedSharedPlacementController.cs) | 共有ボードが操作可能になったときのローカル通知を追加 |
| [Assets/Editor/AnswerLoggingVerification.cs](../Assets/Editor/AnswerLoggingVerification.cs)＋meta | 実Scene＋offline NetSync、1名/2名・A/B/Development・両方式と異常系の検証 |
| [Assets/Editor/AnswerSnapshotStoreVerification.cs](../Assets/Editor/AnswerSnapshotStoreVerification.cs)＋meta | 不変性、重複、Session/Epoch/セット不一致など保存単体検証 |
| [Assets/Editor/ProposedRevealVerification.cs](../Assets/Editor/ProposedRevealVerification.cs) | 共通endpoint fixtureでLoggerを隔離保存先に設定・有効化。fail-closed Resetに対応 |
| [Assets/Editor/ExperimentLoggerVerification.cs](../Assets/Editor/ExperimentLoggerVerification.cs) | 追加イベントを含めた回帰検証。既存CSVヘッダーは継続検証 |
| 本書、[experiment-log-verification.json](experiment-log-verification.json)、[experiment-log-samples](experiment-log-samples) | 検証結果と実生成したテストCSV |

IndependentAnswerSubmission、ProposedRevealCoordinator、ParticipantRegistry、Lobby、PilotCardSetController、SessionResetUIは既存の公開API／通知・UIを利用し、本番コードを変更していない。Sceneの既存変更は本作業に含めて作り直していない。Inspectorで新しい参照を配線する作業は不要。Loggerは同じSceneの既存コンポーネントを解決する。

## 3. 新規CSVの仕様

通常の保存先：

```text
Application.persistentDataPath/ExperimentLogs/<Session ID>/
  session.csv
  phase_events.csv
  card_set.csv
  final_placements.csv        # 確定した試行のみ
  independent_answers.csv     # ProposedでRevealした試行のみ
```

いずれもUTF-8 BOM、CRLF、全フィールドを引用符で囲む形式。`schema_version=1`。既存P/S/Cのヘッダー・列順は変更していない。phaseイベントの種類と、従来空だったparticipant_idに接続IDを明示する用途を追加した。

### final_placements.csv

| カラム | 型・意味 |
|---|---|
| schema_version | 整数文字列1 |
| session_id | Session GUID |
| session_epoch | Int64文字列 |
| condition | Normal／Proposed |
| card_set_id | A／B／Development |
| card_id | 研究カードIDまたは開発カードID |
| final_tier | A〜E |
| observed_time_utc | PCが確定を観測したDateTimeOffset UTCの`o`形式 |

FinalAgreementManagerの`IsBoardConfirmed`と`GetFinalPlacements()`を使い、対象Card ID一覧との完全一致を確認してからコピーする。全カードをCard ID順に1件ずつ保存する。8枚なら8行、Developmentは現在の3行となる。未確定配置は保存しない。

### independent_answers.csv

| カラム | 型・意味 |
|---|---|
| schema_version | 整数文字列1 |
| session_id / session_epoch | Session GUID／Int64文字列 |
| condition / card_set_id | Proposed／A・B・Development |
| participant_client_id | 通信ClientNoの整数文字列。研究用の永続Participant IDではない |
| card_id / initial_tier | Card ID／公開された初期Tier A〜E |
| selected_at_utc | 元の回答に入っている選択時刻。Quest等の回答元UTC、`o`形式 |
| observed_time_utc | PCが確定Revealを観測したUTC |
| reveal_snapshot_id | 確定RevealのID。重複／不整合判定と追跡用 |
| submission_id | その回答を確定した同意ID。最終有効Submitとの対応用 |

Coordinatorの`GetRevealedSnapshot()`から取得する。Session/Epoch、対象Card ID集合、各参加者の全回答・answered、Tier、同意ID、UTC選択時刻を確認する。2名×8枚は16行、1名×8枚は8行、1名×Developmentは3行。Reveal前の回答は保存せず、取消前の暫定SubmitもSnapshotにしない。

### 不変性と整合性

- 同じファイル・同じ回答内容の再保存はファイルを書き換えず、最初のobserved_time_utcを保持する。
- 回答・Session/Epoch・方式・セット・Reveal ID・Submit IDが異なれば不整合として拒否し、既存回答を上書きしない。
- 新規回答ファイルは一時ファイルをflush後、置換ではなくMoveで確定する。既存結果への上書きを行わない。
- 回答CSVの保存を、その回答に対応するphaseイベントの保存より先に行う。途中失敗時はObservationがキュー先頭に残り、同じコピーを再試行する。
- P/S/Cを含む全ファイル同時トランザクションではない。一部保存後の再試行を不変・重複抑止で扱う。既存のphaseとsummaryは従来どおり再構築される。

## 4. 追加イベントと記録タイミング

| イベント | 記録タイミング・ID |
|---|---|
| PARTICIPANT_SUBMITTED | PCでclient別のSubmit=trueと有効なProof/回答ハッシュを観測したとき。`Session:SUBMITTED:ClientNo:submissionId` |
| PARTICIPANT_SUBMISSION_CANCELLED | PCで既知のSubmitに対するSubmitted=falseを観測したとき。`Session:CANCELLED:ClientNo:submissionId` |
| ANSWERS_REVEALED | 確定Reveal通知を観測したとき。`Session:ANSWERS_REVEALED:revealId`。同じObservationに初期回答全件を付ける |
| SHARED_CLASSIFICATION_STARTED | Proposedの共有ボード準備完了markerを確認し、実際にMainの操作が可能になったとき。`Session:SHARED_CLASSIFICATION_STARTED:revealId` |
| SESSION_ABORTED | 未確定の進行中試行からReset要求が送信受付されたとき、旧Sessionへ記録。`Session:SESSION_ABORTED`。kindはreset_request_accepted |

Normalの共同分類開始は既存`EXPERIMENT_STARTED`を使用し、余分なSHAREDイベントは生成しない。BOARD_CONFIRMED、SESSION_CREATED、RESET_STARTED/COMPLETEDも継続する。

Submit／Cancel行の既存`participant_id`列は **`client:1`などの接頭辞付き通信ID** とする。永続研究IDと混同しない。CSVのClientNoと外部管理表を結合する。

Submit→Cancel→再Submitでは、再Submitに既存仕様どおり新しい同意GUIDが発行される。各GUIDと取消IDが別イベントになる。再受信やSnapshot再通知は同じイベントIDで抑止する。

時計は既存のPC `DateTimeOffset.UtcNow`と`Stopwatch`を使用。遅い保存・再試行が起きても、キュー投入時の観測時刻を維持する。`selected_at_utc`を他端末時刻やPC時刻から引いて所要時間を求めない。

### 接続復元の扱い

- 通常の継続観測は`observation_kind=notification`。
- Loggerの再有効化、過去のStartを持つSessionへの再接続／復元は`retained_snapshot`。既に保存されている行の元のkind／時刻は重複処理で変更しない。
- 作業中にPCの接続が失われた場合、そのSessionの以降の観測を保守的に復元扱いにする。新しいReset、またはStart markerのない準備済みSessionの接続baselineを確認してからの新Startで通常観測へ戻る。
- 復元で回答を回収することはできても、元のReveal／Submit時刻を復元したとは扱わない。

## 5. 実際に出力されたCSVサンプル

サンプルは**今回の最終オフライン検証で実生成されたCSVのコピー**である。推定出力ではなく、テスト用の回答・時刻・IDであり、研究参加者の実測結果ではない。

- [Normalサンプル](experiment-log-samples/Normal/session.csv)：Set A・2名、最終8件。初期回答CSVはない。
- [Proposedサンプル](experiment-log-samples/Proposed/session.csv)：Set A・2名、初期16件・最終8件、phaseイベント11件（Submit 3回・Cancel 1回を含む）。
- [Proposedの初期回答](experiment-log-samples/Proposed/independent_answers.csv)
- [Proposedの最終回答](experiment-log-samples/Proposed/final_placements.csv)
- [Proposedのイベント](experiment-log-samples/Proposed/phase_events.csv)

生成元の絶対パスと検証レポートは `experiment-log-verification.json` に記録した。

実ファイル `final_placements.csv` の先頭2データ行：

```csv
"schema_version","session_id","session_epoch","condition","card_set_id","card_id","final_tier","observed_time_utc"
"1","5d2f59b0c37f4c2ba82e517098a29935","2","Proposed","A","A01","E","2026-11-17T14:25:08.6690920+00:00"
"1","5d2f59b0c37f4c2ba82e517098a29935","2","Proposed","A","A02","E","2026-11-17T14:25:08.6690920+00:00"
```

実ファイル `independent_answers.csv` の先頭2データ行：

```csv
"schema_version","session_id","session_epoch","condition","card_set_id","participant_client_id","card_id","initial_tier","selected_at_utc","observed_time_utc","reveal_snapshot_id","submission_id"
"1","5d2f59b0c37f4c2ba82e517098a29935","2","Proposed","A","1","A01","A","2026-11-17T14:25:08.4090600Z","2026-11-17T14:25:08.5295860+00:00","46dd13f4dd494288b4fa601d590009e5","17a03072e1ff45e4b2e23d8c8c4f3cf5"
"1","5d2f59b0c37f4c2ba82e517098a29935","2","Proposed","A","1","A02","A","2026-11-17T14:25:08.4115580Z","2026-11-17T14:25:08.5295860+00:00","46dd13f4dd494288b4fa601d590009e5","17a03072e1ff45e4b2e23d8c8c4f3cf5"
```

## 6. 評価指標の算出方法

同じSession、同じobserver_run_idで、必要な境界が`notification`かつelapsedが空でない正常終了試行を原則とする。`SESSION_ABORTED`や接続中断のある試行は別扱いにする。elapsedの基準はSessionの初回観測であり、課題Startではないので必ず差分を取る。

| 指標 | 計算 |
|---|---|
| 全体作業時間 | `BOARD_CONFIRMED.elapsed - EXPERIMENT_STARTED.elapsed` |
| 独立回答・公開まで | Proposedの`ANSWERS_REVEALED.elapsed - EXPERIMENT_STARTED.elapsed` |
| 参加者別独立回答時間 | 公開Snapshotのsubmission_idに対応する`PARTICIPANT_SUBMITTED.elapsed - EXPERIMENT_STARTED.elapsed` |
| Normal共同分類時間 | Start→BOARD_CONFIRMED |
| Proposed共同分類時間 | `BOARD_CONFIRMED.elapsed - SHARED_CLASSIFICATION_STARTED.elapsed` |
| 独立回答間一致率 | Proposedで同じCard IDの初期Tierが一致する枚数÷対象カード数 |
| 独立回答と最終回答の一致率 | 各参加者について同じCard IDのinitial_tier＝final_tierの枚数÷対象カード数 |

各人の最終有効Submitは、単純な「CSVで最後のSubmit」ではなく、初期回答CSVの`submission_id`とevent_idを対応付ける。結合キーはSession ID、ClientNo、Submit ID。イベントIDは`<session>:SUBMITTED:<participant_client_id>:<submission_id>`で組み立てられる。初期回答と最終回答はSession ID／Epoch／Card IDで結合し、Mode・Setの一致も確認する。

Card数を8で固定せず、ファイル内の対象Card ID集合の一致を確認して分母を決める。欠損時に黙って分母を減らさない。匿名研究IDとClientNoの対応は外部管理表を使用する。操作回数、発言量、意見反映度をこのCSVから直接推定する機能は追加していない。

## 7. Session Resetとログ保存の連携

1. 既存の確認UIからmanager.ResetSession→StartNewSessionを呼ぶ。
2. ExperimenterのmanagerはLoggerの存在を要求する。Loggerが無効・未設定・非PC書込者でも拒否する。
3. Loggerは確定済みFinal、存在する確定Reveal、現在のphase状態をもう一度取得する。Proposedの必要コンポーネント／確定Reveal欠落、Session/Epoch不一致、不完全回答は失敗とする。
4. 既存キューをDrainし、必要回答CSVを含めて全件保存できた場合だけReset制御を送信する。保存失敗は既存LastError／Reset UIに出る。
5. 未確定の進行中試行ならReset要求の送信受付後に旧SessionへSESSION_ABORTEDを記録する。未確定のFinalや、未公開のInitialを作ることはしない。開始前Lobbyの単なる準備Resetは中断試行にしない。
6. 新GUID／Epochで既存Resetを実行する。キューの各Observationは作成時のSession ID・Epoch・回答コピーを持つため、旧回答を新Sessionへ書かない。

保存先を復旧したら同じResetを再度試すか、Loggerの自動再試行を待つ。保存済み回答と現在の確定Snapshotが矛盾する場合は自動上書きしない。実験者が元データを退避・確認する必要がある。

中断イベントは「Reset送信受付」を意味し、全端末ACK完了を意味しない。中断イベント自体や新Sessionのログでその後I/O障害が起きた場合もキューに保持して通知する。既存構造どおり、将来の全書込成功をReset開始前に保証することはできない。

## 8. 自動テストの結果

最終版でUnity Editor 6000.3.17f1、PreviewScene、offline NetSyncにより以下を実行し、結果JSONのpassedを確認した。実Quest・実Room試験ではない。

| 検証 | 結果 | 範囲 |
|---|---|---|
| AnswerLoggingVerification | **366項目成功** | 1名/2名×A/B/Development×Normal/Proposed、実コンポーネントで開始・回答・Submit取消/再Submit・Reveal・共有分類・確定・Reset、各CSV件数・値、フェーズ時刻順、Submit ID結合、旧CSVバイト保持、中断、Logger無効/欠落、保存失敗/復旧、Session/Epoch不一致、cold復元のkindと回答保存 |
| AnswerSnapshotStoreVerification | **18項目成功** | 不変回答、同一通知の時刻/バイト保持、異なる回答/Reveal ID/Set/Epoch/Session/方式/未確定/不完全行列/不正時刻の拒否、2×8と1×3、BOM |
| ExperimentLoggerVerification | **29項目成功** | 既存CSV・単調時間・文字コード・重複・再起動・I/O復旧と新イベント込みの統合 |
| ExperimentResultVerification（上記から実行） | **78項目成功** | 既存Normal/Proposed、Result、確認・連打・取消・Role・Reset、保存失敗拒否 |
| PilotCardSetVerification（下記から実行） | **427項目成功** | A/B/Development、両方式、全員ACK、切り替え、最終確定、CSV保持 |
| PrivateAnswerCardPoseVerification | **19項目成功** | 初期化・取消・Reset、Pilotフローを通じた予期しないUnity Error/Exception/Assert 0 |

結果は[experiment-log-verification.json](experiment-log-verification.json)に保存した。件数には別テストから呼び出す検証が含まれるため、単純合計を独立ケース数とはしない。

検証は毎回`Library/AnswerLoggingVerification/<GUID>`、`Library/AnswerSnapshotStoreVerification/<GUID>`等の専用出力先を使う。既存fixtureもLoggerに隔離rootを設定する。本番用persistentDataPathのCSVに対して障害試験や削除はしていない。最終検証前後で本番側の既存CSV 18ファイルのSHA-256が一致した。

開発中、最初の長いeval呼び出しが5秒のAPI待ちでタイムアウトしたが、実処理は完了し生成JSONで成功を確認した。以降は明示タイムアウトを指定し、最終の一括検証はCLI応答・JSONとも成功した。最初の保存単体検証では、期待するInvalidDataExceptionを検証コードが捕捉できず失敗し、捕捉を訂正して全18項目を再実行した。意図的なI/O失敗試験はConsoleへCSV save failedを出す。これを通常実験の保存失敗と混同しないこと。

## 9. 既存機能への影響

- Card ID、Tier ID、回答／配置同期値、Submit／Cancelの同意方式、Reveal準備／ACK、Final Agreementの確定条件を変更していない。
- Final Agreementの読み取りプロパティと、共有開始時のローカル通知を追加した。通信パケット／固定キーを増やしていない。
- NormalはStart基準の共同分類を維持し、ProposedはReveal後の既存Main操作権限を維持する。
- Resetの条件を「必要ログを保存できるLoggerが存在すること」に強化した。古い独立テストfixtureでLoggerを設定しない構成はResetを拒否するため、共通fixtureの構成を更新した。
- 既存3 CSVのヘッダーは維持するが、新しいevent_typeとclient:接頭辞を解析側が許容する必要がある。旧解析が「1試行5イベント固定」と仮定している場合は更新が必要。
- 以前の試行で失われた回答や時刻を遡って生成するものではない。旧ログだけから初期／最終回答を復元できない。

## 10. Quest 1台で確認可能な項目

実機テストは今回未実施。PCをExperimenter、QuestをParticipantとし、開発時だけExpected Participant Count=1で次を確認できる。

1. Set AのNormalで8枚を分類・確定し、PC側にfinal_placements.csvが8件保存される。独立回答CSVが生成されない。
2. Reset前にSet Bを指定し、Reset→Proposed開始。8枚を回答してSubmit／可能ならReveal前に取消・再Submitを行う。1名構成はRevealが速いため取消確認は2名offline試験でも補う。
3. Revealでindependent_answers.csvが8件、共有分類開始でSHAREDイベントが保存される。最終確定後に初期回答CSVの内容が変わらない。
4. Reset後も旧フォルダが残り、Normalへの切り替え・次の開始ができる。未確定の中断Resetで存在しないFinalを要求しない。
5. PCの実保存先、イベントのkind、Session／Epoch／Mode／Set、Submit ID結合、時刻差を確認する。

開発用1名設定をそのまま本実験へ持ち込まない。実験者PCをStandaloneビルドにする場合は、AutoByPlatformがParticipantになる既存仕様に注意し、明示的にExperimenterへ設定する。

## 11. Quest 2台で確認が必要な項目

以下は**未検証**。本実験前にPC＋Quest 2台・Expected Participant Count=2で確認する。

- 2名それぞれの8回答がclient別に対応し、16件の初期回答が保存される。
- 一方だけSubmitしても公開されず、取消／再Submitと全員Submitを経て1つの確定Revealになる。
- 同時公開後にSHAREDイベントが記録され、全員のReadyによるFinal CSVが8件で一致する。
- Normal A→Proposed B、および逆順／別セットの連続試行で旧ファイルが保持される。
- 実通信の再送・遅延・切断／再接続で重複行を作らず、復元の時刻をリアルタイム計測と混同しない。
- 外部の匿名Participant ID対応表に両QuestのClientNoを記入し、再接続後の番号変化を確認する。
- 実験者が使うPCでCSV書込・回収・別媒体へのバックアップを確認する。障害試験は専用のテストSession／保存先で実施する。

## 12. 残存する問題・制約

- PCが観測していない過去のSubmit→Cancel→再Submitの全履歴を、最新状態だけから完全復元することはできない。今回の実装は既存NetSyncの値変更通知を観測し、新しい永続Questイベント配送キューは導入していない。復元された最新同意／Snapshotはretained扱いにし、PC継続観測のない試行を通常の時間分析に入れない。
- PC観測時刻にはネットワーク遅延・フレーム処理の遅れが含まれる。selected_atは回答元端末の時計であり、端末間UTC差分は計時に使用しない。
- 保存キューは既存同様RAM。プロセス強制終了・電源断・保存不能状態で終了した場合、未保存イベントの完全回収は保証しない。警告中は本実験を進めず、復旧・保存確認を優先する。
- phase/summary/sidecarはプロセス間の単一writer排他までは備えない。実験者PCを1台に限定する。回答ファイルは既存結果の置換を拒否するが、複数writer運用を推奨するものではない。
- 異なる内容の同Session回答が保存済みの場合、Resetをブロックする。自動修復で研究データを上書きしない設計なので、退避・調査が必要。
- Pair ID・匿名研究ID・実施順序は外部管理表で対応する。操作回数・軌跡・発言・アンケートUIなど、今回対象外の機能は実装していない。
- 自動検証と実機検証を区別する。コード上・offline試験では必要データの保存と算出経路を確認できたが、Quest 1台／2台の実通信での最終確認は未検証である。
