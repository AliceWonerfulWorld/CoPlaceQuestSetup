# Proposed Private Answer — 第1段階

Proposed の独立回答を Main TierBoard からローカル専用の Private TierBoard へ移した。同期するのは既存の `candidate_<cardId>` / `answered_<cardId>` / `selectedAt_<cardId>` のみ。Private Board の Transform、カードの物理位置、表示は同期しない。

## ファイルと Scene

新規:

- `Assets/Scripts/PrivateTierBoardController.cs` / `.meta`: 本人の表示、入力条件、Editor 入力、本人の回答復元、Session イベント処理。
- `Assets/Scripts/PrivateAnswerCard.cs` / `.meta`: ローカル XR Grab、Release、Tier 判定、Snap。
- `Assets/Editor/PrivateTierBoardSetup.cs` / `.meta`: 明示的な Scene 作成と読みやすさ調整のメニュー。自動実行・実行時生成はしない。
- `Assets/Editor/PrivateTierBoardVerification.cs` / `.meta`: 保存済み Scene の隔離コピーと NetSync の実 Offline transport を使う回帰検証。
- この文書と `Documentation/PrivateAnswerStage1.png`: 実装報告と実際の Play Mode 画像。

変更:

- `Assets/Scenes/QuestSetup.unity`: Private Board、5 Tier、3 Private Card、参照を追加。Unity による Scene 保存でオブジェクトの記載順も変わった。
- `Assets/Scripts/CandidateSyncTest.cs`: データ API、送信成否 API、Private 入力フェーズの制御、旧キーボード入力の撤去、Private 中の既存 Reveal 表示抑止。
- `Assets/Scripts/CardTierDetector.cs`: Main カードを Normal 専用に限定。Proposed の candidate 送信を撤去。

`ExperimentManager`、`ParticipantRegistry`、`NormalPlacementSync`、`FinalAgreementManager`、`ExperimentSessionManager`、`SessionVariableTransport`、`TierZone` の既存実装は変更していない。

```text
PrivateTierBoard                  [PrivateTierBoardController]
└── LocalView
    ├── PrivateBoardView          [Main の表示を複製]
    │   ├── TierBoardCanvas       [PRIVATE ANSWER / 説明 / A〜D / Unclassified]
    │   ├── Tier_A                [独立した TierZone / 3 SnapPoints]
    │   ├── Tier_B
    │   ├── Tier_C
    │   ├── Tier_D
    │   └── Unclassified
    ├── PrivateCard_01            [cardId = Card_01]
    ├── PrivateCard_02            [cardId = Card_02]
    └── PrivateCard_03            [cardId = Card_03]
```

Private Card は Main の画像・MeshRenderer・Collider・Rigidbody・XRGrabInteractable 設定を再利用した別オブジェクト。CardTierDetector、Visual Scripting、NetSync コンポーネントは持たない。各カードに名前表示用 Canvas と `PrivateAnswerCard` を付けた。Main カードへの位置参照・移動処理は持たない。

仮配置は PrivateTierBoard の Position `(0, 1.25, 1.25)`、Scale `(0.6, 0.6, 0.6)`。位置・回転・Scale は root の Inspector で調整できる。Tier と SnapPoint、カード、説明文の参照も Inspector で調整できる。

## 入力・表示・データ

全3枚を最初から表示し、任意順に回答・変更・取消できる。各カードの `cardId` を直接データ API へ渡す。Private 入力は `CurrentCardId` を参照せず、最初の回答によって CurrentCardId を変更しない。

操作条件はすべて満たす必要がある:

- Play 中の有効な Private Controller。
- Local Role が Participant、Mode が Proposed。
- State が Idle / Answering / WaitingForAnswers。
- NetSync Ready、ローカル Participant の登録完了。
- Session Reset 中でない。

Revealed / Discussion / Confirmed は本人の回答表示を残して読み取り専用にする。Experimenter は Private Board を表示しない。Normal では Private 表示・入力を無効にする。

Proposed 中は Main の Renderer / Canvas / Collider をローカルで無効にする。Main の同期・Session Reset・Final Agreement コンポーネントは動かしたままにする。Normal へ戻ると元の表示・Collider 状態を復元する。Main カードの XR selection filter も Normal 専用なので、Private 入力用に誤操作できない。

Private の配置は `CandidateSyncTest.TrySetCandidate(cardId, tier, out submitted)` に接続する。A〜D の場合、従来と同様に candidate と UTC `DateTime.UtcNow.ToString("o")` を保存し、その後 answered=true を送る。Unclassified は answered=false、candidate=Unclassified、selectedAt="" とする。SessionVariableTransport の Session ID 付き形式と固定キーを維持した。

送信が受理されたら本人の Private カードを Snap する。受信エコーの3変数が現在のローカル入力と一致するまで、過去の受信値によって新しいローカル配置を戻さない。部分的な candidate / answered / selectedAt は復元対象にしない。Participant 別 Dictionary は既存の受信処理が引き続き更新する。

Private の view は **常に LocalClientNo の回答だけ**を読む。他 Participant の Tier を表示するコード経路を持たない。独立回答中は旧 CandidateMarker と CandidateStatusText を空にし、切断中もこの表示抑止を維持する。通常のデバッグ用 NetSync 受信ログや内部データ API は回答データを保持するため、秘匿性の対象は Participant 向けの実験表示であり、ネットワーク通信自体の秘匿化ではない。

## State と既存 Reveal

第1段階では `usePrivateAnswerBoard=true`。旧処理が1枚の回答完了で自動 Reveal して残りのカード入力を止めるため、Private の独立回答中だけ旧自動 Reveal を停止する。

本人の回答が0枚なら Idle、一部なら Answering、3枚そろえば WaitingForAnswers。この状態でも変更・取消できる。これは本人の入力進捗であり、他 Participant の回答完了や Tier を表示しない。

明示的に既存 State を Revealed へ移した場合は Private を操作不可にし、既存のカード単位 Reveal 処理が動く。Revealed を Answering に自動的に戻さない。新しい Mini Board、全員全カードの一斉 Reveal、最終共同配置、Proposed Final Agreement は実装していない。

## Session Reset・再接続

Controller は既存の `OnSessionResetStarted` / `OnSessionStarted` を購読する。Reset 開始で保持中の Private カードを安全に離し、3枚とも Unclassified、ローカルの保留入力と復元キャッシュを消去する。回答変数の初期化は既存 SessionManager の defaults / ACK / Session ID / Epoch 処理に任せ、Private 側から二重に初期値を送らない。

NetSync Ready に戻ったとき、ローカルの未確認入力を破棄し、サーバーから復元した本人の Client Variables を読み直す。SessionVariableTransport が現在の Session に合うデータだけを読む。

インストール済み NetSync 0.17.4 の実装を確認した:

- `GetClientVariable(name, clientNo)` と `GetAllClientVariables(clientNo)` が実際に存在する。
- Client Variable 受信は、含まれる clientNo ごとの完全な snapshot としてキャッシュを更新する。
- Python server は Client Variables を Room / Device ID 単位で保存し、Device ID と clientNo の対応を使って再配信する。同じ Device ID の再接続では保持した回答を取得できる。
- Device ID の期限切れ、空 Room の期限切れ、明示的な Client Variable clear、サーバー再起動で保持データを失うことがある。その場合のディスク保存や別経路への回答再送は追加していない。
- `_clearClientNetworkVariablesOnStart` の既存設定・既存 API の挙動は変えていない。

## 次段階のデータ API

```csharp
IReadOnlyList<string> CandidateSyncTest.AnswerCardIds
int CandidateSyncTest.LocalClientNo
bool CandidateSyncTest.IsDataReady
bool CandidateSyncTest.TryGetParticipantAnswer(int clientNo, string cardId, out ParticipantAnswer answer)
IReadOnlyDictionary<string, ParticipantAnswer> CandidateSyncTest.GetParticipantCandidates(int clientNo)
```

`ParticipantAnswer` は CardId / Tier / Answered / SelectedAt の読み取り専用 snapshot。返す Dictionary は新しく作るため、内部 Dictionary を外部から書き換えられない。次段階では ParticipantRegistry の `ParticipantClientNos` とこの API を組み合わせられる。**他者 snapshot を view に使うのは Reveal の公開条件成立後に限定すること。**

## 検証結果

- Editor / Android / Editor tools を Unity 6.3 の実際の参照・define でコンパイル: エラー・警告なし。
- `Tools > Experiment > Verify Private TierBoard`: 25項目 PASS。実コードと NetSync の Offline transport を使用し、開いている Scene と稼働 Room を変更しない。
- 隔離検証で確認: 3枚の自由順回答、別 Participant の異なる snapshot、本人だけの位置復元、旧 Marker/Text の非表示、変更・取消・再回答、CurrentCardId 非依存、Main 不変、Reveal 等の状態拒否、Experimenter 非表示、Normal 復元、Session Epoch と回答と view の一体 Reset。
- Normal の回帰検証: 3枚の共有配置、受信反映、Ready、Final Agreement、Board Confirmed が PASS。
- 実 Editor＋NetSync: Q/1、W/2、E/3 で本人の A/B/C と answered=true、UTC selectedAt、Private の Snap 位置を確認。Main の物理位置と CurrentTier は入力前後で不変。
- 実 Editor の停止・再接続: 同じ Room / Session / 本人 clientNo で A/B/C と同じ selectedAt を復元。
- 実 Editor Keyboard の 0: Unclassified、answered=false、selectedAt=""、再度 1 で回答可能。
- 実 Editor Play で XRInteractionManager の SelectEnter / SelectExit を実行: selection 成立、Release 後 selection 解除、Private Card_03 の Tier D への Snap と candidate 送信を確認。
- Play Mode のフレーム進行と Console を確認。今回の Play の Console errors=0。過去の Pipeline timeout ログは別時刻の既存ログ。
- 検証用の本人の回答は Unclassified に戻し、Room Mode は検証開始前の Normal へ戻した。共有 Main の配置は変更していない。
- **Quest を装着しての手による Grab、更新版 Editor＋更新版 Quest の2台同時回答は未確認。下記手順で確認する必要がある。**

Quest 用 Development APK: `Builds/PrivateAnswerStage1.apk`。Android Build は成功、errors=0、warnings=21（既存の XR / AR 等のビルド警告を含む）。APK は実機へまだインストールしていない。

## Editor＋Quest の確認手順

1. 更新した QuestSetup を Quest 3 用に Build / Install する。Editor と Quest を同じサーバー・Room・Session に接続する。
2. Editor の ExperimentParticipantRegistry の Local Role を Participant、Expected Participant Count を2にする。Quest も Participant とする。Scene の既存設定は維持している。
3. 全員 Idle の状態で Mode UI から Proposed へ切り替える。既存 Session の状態が進んでいる場合は、Experimenter が New Session を実行してから始める。Local Role が Participant のままでは既存仕様どおり New Session は拒否される。
4. Editor の **Game view にフォーカス**する。F6=Card_01、F7=Card_02、F8=Card_03（Q/W/E も使用可能）。1=A、2=B、3=C、4=D、0=Unclassified。Scene view の Q/W/E は Unity 自身のツールショートカットなので Game view を使う。
5. Editor は Q→1、W→2、E→3。Quest は Private のカードを実際に Grab し、01→D、02→C、03→A の領域で Release。全カードを任意順に操作できる。
6. 各端末に自分の Private だけが表示され、Main と旧 Candidate Marker/Text が見えず、相手の Tier が漏れないことを確認する。
7. `GetParticipantCandidates(clientNo)` またはデバッグ時の既存 Dictionary で、両者の異なるデータを確認する。Participant 向けの表示にはこのデバッグ結果を出さない。
8. 1枚を別 Tier へ変更してから Unclassified へ戻す。answered=false、selectedAt="" と再回答を確認する。
9. 同じ Session で片方を再接続し、本人の回答位置が復元されることを確認する。
10. State を明示的に Revealed / Discussion / Confirmed にすると Private が操作不可になることを確認する。新しい Reveal UI はまだ存在しない。
11. Editor の Role を一時的に Experimenter に変更して New Session を実行し、双方の3枚と3変数が初期化されることを確認する。その後 Participant へ戻す。
12. 全員 Idle で Normal へ切り替える。Private が消え、Main の双方向配置、変更・取消、双方の Ready、Final Agreement が従来どおり動作することを確認する。

ログは `[Private Answer]` / `[Private Answer Changed]` / `[Private Answer Cancelled]` / `[Private Board Restore]` / `[Private Answer Rejected]`。毎フレームのログと CSV は追加していない。
