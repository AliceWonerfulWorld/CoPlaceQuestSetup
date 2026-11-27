# CoPlace カード操作履歴の実装報告

## 操作経路と記録条件

共同ボードのカードは XR Interaction Toolkit の `selectEntered` と `selectExited` で動く。解放時にカード中心から Drop 先を解決し、スナップ点の確保と既存の参加資格・Ready・結果確定ガードを通った場合に `NormalPlacementSync.SendPlacement` が NetSync の `normalPlacement_<cardId>` を送る。送信が拒否された場合は元の配置に戻る。同期受信側の `ApplySyncedPlacement` は XR の解放処理を呼ばず、ローカル把持中の同期適用は保留される。

通常方式は Start 後、提案方式は独立回答の同時公開後に共同ボードが有効になる。独立回答は PrivateTierBoard 側の経路を使う。共同ボード初期化、結果の復元、Reset、Editor のキーボード操作は従来の配置値を送る。XR の実操作による有効な Drop のみ、既存のセッション付き配置値に UUID、Epoch、送信時 ClientNo、既存方式でハッシュした端末識別子を付ける。匿名 Participant ID は送信しない。

NetSync のグローバル変数通知には送信者引数がなく、送信 API の成功は送信待ちを含む。実験者 PC が現行セッションのタグ付き配置を受信し、共同分類が有効で結果確定前のとき、`CARD_PLACEMENT_COMMITTED` を記録する。ここでの「確定」は PC が Room の配置値更新を観測したことを指す。すべての端末でスナップした時刻や最終合意を指す値ではない。直前の Room 値を `from_tier` とするため、競合時に参加者端末が把持した瞬間の表示と異なることがある。

端末のハッシュを観測時点の ClientNo の端末情報と照合し、`Logger.Data.TryResolveParticipant` で確認済みの対応がある場合に限り匿名 ID を入れる。実験者自身と実験者と確認できた発生元を除外する。匿名 ID が解決できない場合も ClientNo と配置を保存する。受信時の照合結果をキューに固定するため、再接続後の対応表で過去行は補完されない。ClientNo が欠ける値は採用しない。タグは実験クライアントが生成する情報で、暗号署名付きの送信者証明ではない。

### 競合・再通知・未取得イベント

既存のローカル把持ガード、Room の更新順序と NetSync の送信集約を使う。カード全体の排他的なサーバー側把持ロックはこの配置経路にはない。両者の配置が順に Room 値へ反映された場合は両方を記録し、送信集約等で PC に届かなかった操作は行を生成しない。置き換えられた先行配置も、その時点の Room 更新として残る。UUID が同じ再通知・再試行は同じ行として扱う。異なる UUID で前後の Tier が一致する行は同一 Tier への再配置。ユニークなタグにより、同じ Tier でもワイヤ上は異なる値となり、NetSync の同値抑制の対象が変わる。既存の Tier 判定と合意の比較はデコードした Tier を使う。この変更の実 Room での挙動は Quest 2台で確認する。

把持、配置に至らない解放、無効な Drop、取消、送信拒否には Room での確定観測がないため、個別イベントは保存しない。最終合意用には参加者別の把持カード一覧がある。把持者の表示を検討する場合、共同分類中の補助表示に使える候補だが、ネットワーク上の排他所有権の証明には使えない。表示は今回追加していない。

## CSV と保存

`Logger.SessionDirectory(sessionId)/card_operations.csv` に保存する。既存の Test 日付別、Pilot／Main のペア・試行順別のフォルダを使用する。

| 列 | 意味 |
|---|---|
| schema_version | このファイルの仕様。現在は 1 |
| session_id, session_epoch | phase_events.csv と共通のセッション識別子と世代 |
| condition, card_set_id | 適用済み方式（Normal／Proposed）とカードセット |
| phase | shared_classification |
| event_id, operation_id | イベントの重複防止キーと XR 解放に付けた操作 UUID |
| event_type | CARD_PLACEMENT_COMMITTED |
| participant_id | 観測時点で確認済みの匿名 ID。未解決は空欄 |
| participant_client_id | 配置を送った端末の ClientNo |
| card_id | 既存のカード識別子 |
| from_tier, to_tier | PC で観測した直前と更新後の Tier。未分類は Unclassified。直前値が不明なら from_tier は空欄 |
| observed_time_utc | 実験者 PC の UTC 観測時刻 |
| session_elapsed_seconds | phase_events.csv と同じ Stopwatch 起点の秒。信頼できる起点がない場合は空欄 |
| observer_run_id | 実験者 PC の Logger 起動識別子 |

現行 Logger で準備し、ライブで Start を観測したセッションにヘッダーと `CARD_LOG_INITIALIZED` を保存する。ヘッダーのみは記録行 0 件を示す。旧セッションの復元で新規ファイルを作らない。提案方式を公開前に中断した場合は共同分類を開始していないので、ヘッダーのみでも共同分類の 0 回試行に含めない。実験者 PC の再接続時は保持値の再生と新規操作を区別できないため、その試行の観測を停止する。確定前に停止を検出した場合は `CARD_LOG_OBSERVATION_INTERRUPTED` を保存する。異常終了や送信集約により欠測した操作は CSV 件数から推定できない。

既存の FIFO、アトミック置換、保存エラー時の再試行、Reset 前の flush を使う。失敗した行はキュー先頭に残り、保存が済まなければ Reset を拒否する。再試行では既存のイベント ID を照合する。通常終了の保存チェックはキューとエラー状態も確認する。中断時は受信済み行を保持し、Reset／終了の flush を試みる。プロセス強制終了時のメモリ内キューの保全は保証されない。

## 録音との照合

録音を Start より前に開始し、冒頭で試行識別子を読み上げる。録音に入る同期合図を PC 側にも残す。例として、PC の `EXPERIMENT_STARTED` を観測した直後に合図を入れ、遅れと使用した PC の UTC を作業記録に残す。可能なら終了時にも同じ方法で合図を取り、時計のずれを確認する。分析時は `session_id`・`session_epoch`・`observer_run_id` を合わせ、合図から録音の相対位置に変換する。PC の観測時刻にはネットワークと処理遅延が含まれる。発話との前後関係が僅差の場合は会話内容と合わせて判定する。

## Quest の確認手順

1. 研究用 Room と分離した Room で Test として新規準備する。実験者 PC は Experimenter、Quest は Participant とし、接続と匿名 ID の対応を確認する。
2. Quest 1台では、Test の期待人数を設定可能な範囲で1台にする。開始できる通常方式で、未分類から Tier への Drop、別 Tier、同じ Tier、範囲外での解放を順に試す。ヘッダー、行数、ID、Tier、UTC、phase_events との照合を確認する。Reset 後は別ファイルになることを確認する。
3. Quest 2台では期待人数を2に戻し、通常方式と提案方式をそれぞれ新規セッションで実施する。提案方式は提出と同時公開の前後を分け、共同分類後だけ行が出ることを確認する。
4. A・B の順番操作、同じカードの同時操作、同一 Tier への再配置を行い、各端末の画面と PC の行を時系列で照合する。確認済み ID と未確認 ID を分け、再接続時に再確認が必要なことを検証する。
5. 最終同意で確定する試行と、途中で Reset する試行の両方で出力を確認する。旧セッションに行が増えないことと、録音の同期合図付近で対応を取れることを確認する。

## 変更ファイル

- `CardTierDetector.cs`、`NormalPlacementSync.cs`、`SessionVariableTransport.cs`（XR 解放と配置値へのタグ付け）
- `ExperimentLogger.cs`、`ExperimentCsvLogStore.cs`（観測・ID 照合・キュー・CSV）
- `ExperimentLoggerVerification.cs`、`ExperimentResultVerification.cs`（隔離した保存・同期検証）
- 本報告書

## 自動検証で生成した CSV と集計例

保存層の隔離テストが生成した Normal のサンプル。ID と UTC はテスト値。

```csv
"schema_version","session_id","session_epoch","condition","card_set_id","phase","event_id","operation_id","event_type","participant_id","participant_client_id","card_id","from_tier","to_tier","observed_time_utc","session_elapsed_seconds","observer_run_id"
"1","Normal-operations","1","Normal","A","shared_classification","operation-2:CARD_PLACEMENT_COMMITTED","operation-2","CARD_PLACEMENT_COMMITTED","P01","1","A01","Unclassified","A","2026-10-15T00:00:02.0000000+00:00","2.000000","operations-run"
"1","Normal-operations","1","Normal","A","shared_classification","operation-3:CARD_PLACEMENT_COMMITTED","operation-3","CARD_PLACEMENT_COMMITTED","P02","2","A01","A","B","2026-10-15T00:00:03.0000000+00:00","3.000000","operations-run"
"1","Normal-operations","1","Normal","A","shared_classification","operation-4:CARD_PLACEMENT_COMMITTED","operation-4","CARD_PLACEMENT_COMMITTED","","3","A01","B","B","2026-10-15T00:00:04.0000000+00:00","4.000000","operations-run"
```

`shared_classification` の確定行は3件。P01 と P02 はそれぞれ配置確定1件・Tier変更1件で、匿名 ID 未解決が1件ある。全3件を分母とした構成比は各区分 1/3、確認済み2件だけを分母とした P01 と P02 の比は各 1/2。A01 の履歴は Unclassified → A → B → B で、Tier変更2件、同一 Tier への再配置1件。Tier変更集計では `from_tier` が空欄の行を判定不能として別計上する。把持回数は取得対象外。方式比較には共同分類だけを用い、未観測操作や欠損試行を0回に置換しない。操作回数は意見検討率を判定する補助情報であり、発言量・影響力・意見検討の有無の代用にはならない。

## 自動検証・残存課題

Unity 6000.3.17f1 の Editor プレビューとオフライン NetSync 端末を使用。保存先は Library 下の実行別ディレクトリ。接続済みの実機は ADB 上で1台確認した。ヘッドセットでの XR 入力、2台の同時操作、実 Room の配信順序、同一 Tier の配信と端末再接続時の ID 照合は未検証。

実験データ管理の回帰検証は Main Normal の完了判定で停止した。Editor ログには既存の Board 結果が NetSync の 1024 バイト上限を超えたと記録されている。この試験は従来のタグなし配置を使い、失敗箇所は最終合意結果の送信。この制限は本変更で修正していないため、本実験前に8枚カードの結果確定を確認する必要がある。

| 検証 | 結果 |
|---|---|
| ExperimentLoggerVerification | 44項目成功。両方式の PC 観測、隔離 CSV、旧形式への後付け防止、0件ヘッダー、Tier変更・同一 Tier・匿名 ID 未解決、書き込み失敗時の既存行保持と再試行、重複防止、セッション分離 |
| ExperimentResultVerification | 78項目成功。オフライン端末での通常／提案フロー、独立回答、同時公開、共同分類、最終合意、Role、Reset、時間・回答ログ |
| ExperimentDataManagementVerification | 114項目通過後に Main Normal 完了で失敗 |
| ParticipantUIVerification | 136項目通過後に通常方式カードセット A の最終合意／結果画面で失敗 |
| ExperimenterUISimplificationVerification | 24項目通過後に Pilot Normal の結果保存／次試行案内で失敗 |

参加者 UI と実験者 UI も、タグなし配置を使う8枚試行の結果確定で停止した。先に通過した検証と区別し、8枚試行の完了を合格扱いにしていない。カード操作固有の競合・取消・PC 観測中断・Reset 拒否は個別の実 Room 試験が残る。研究保存先の210ファイルは最終回帰実行の前後で SHA-256 がすべて一致した。作業開始時から存在したシーンと ProjectSettings の未コミット差分も保持した。
