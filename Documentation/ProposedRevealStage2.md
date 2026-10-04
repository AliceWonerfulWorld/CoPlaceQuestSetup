# Proposed 第2段階 — 一斉Reveal / Readonly Mini TierBoard

全登録Participantが全対象カードへ回答してから、全回答をParticipant別Readonly Boardとして表示する。Main共同配置・Proposed Final Agreement・最終確定・CSVは追加していない。

## ファイル

新規（各C#に `.meta` あり）:

- `Assets/Scripts/ProposedRevealCoordinator.cs`: 全員全カードの完了判定、共通Prepare / ACK / Reveal、確定スナップショット、再接続復元。
- `Assets/Scripts/ReadonlyParticipantBoardView.cs`: 回答からローカル閲覧専用UIを動的生成。
- `Assets/Scripts/RoundedPanelGraphic.cs`: テクスチャ不要の角丸背景・境界線。
- `Assets/Editor/ProposedRevealSetup.cs`: Scene参照の追加メニュー。
- `Assets/Editor/ProposedRevealVerification.cs`: 隔離した複数端末相当の回帰検証・画像出力。
- この文書、`ProposedRevealStage2.png`（2人のUIレンダー）、`ProposedRevealPlayMode.png`（実Play Mode）。

変更:

- `Assets/Scripts/CandidateSyncTest.cs`: Private方式の旧Marker / Statusを全フェーズで無効化。Revealの入力ロック、最後に送った本人の入力との照合。既存回答送信とDictionaryは維持。
- `Assets/Scripts/PrivateTierBoardController.cs`: Prepare中の短い入力ロック、Reveal後のPrivate非表示・操作禁止。F6/F7/F8とQ/W/E、1〜4/0は維持。
- `Assets/Scripts/ExperimentSessionManager.cs`: Reveal用固定キー3つを既存Session保護・Reset defaultsへ追加。Epoch / ACK設計は維持。
- `Assets/Scenes/QuestSetup.unity`: `ProposedReveal` と `ParticipantAnswerReveal` を追加・参照設定。Unity保存によるTMP設定の正規化も含む。

## 判定と共通Reveal

`ExperimentParticipantRegistry.ParticipantClientNos` の全員について、`CandidateSyncTest.AnswerCardIds` の全カードを `GetParticipantCandidates(clientNo)` から取得する。全て `Answered=true`、A〜Dの有効Tier、selectedAtありで初めて完了。Experimenterは一覧に含めない。

参加人数をP1/P2で固定しない。既存Registryの **Expected Participant Count** と登録人数が一致することも確認する。これにより必要な参加者の未接続・切断で早くRevealしない。3人の実験では全端末でExpected Participant Countを3に設定する。

1. 登録Participantの最小clientNoが、全員全カードの回答スナップショットをPrepareとして送信。
2. 各Participantが、受信済みの全回答とスナップショットを照合する。本人は最後に送信した入力とも照合し、古い受信エコーを確定しない。
3. 一致した端末だけ入力を一時ロックし、同じPrepare IDをACKする。この時点では回答内容を表示しない。
4. 全員のACKと現在の回答が一致したら、同じスナップショットを共通Revealとして送信。
5. 全端末がこの共通コミットを受信して `ExperimentState.Revealed` へ移行。全Mini Boardを作り終えてから一括表示する。

完了前の変更・取消は従来どおり可能。Prepareと競合した変更・取消はPrepareを破棄し、全端末を解除して再判定する。ACK待ちは15秒で送信担当が中断し、再試行する。全回答が成立し照合済みの短いPrepare期間だけ変更を止める。

全端末は同一コミットを公開契機とする。ネットワーク受信・描画による端末間の時間差は残るため、同一ミリ秒／同一描画フレームを保証する時計同期は追加していない。

**回答同期は既存candidate / answered / selectedAtのまま。** 新しい回答入力プロトコルは作っていない。Reveal用の共有値は公開する初期回答を固定するためのスナップショットであり、その後の受信変更では描画を変えない。

確認したNetSync API: `GetClientVariable`、`GetAllClientVariables`、`SetClientVariable`、`GetGlobalVariable`、`SetGlobalVariable`、`IsReady`。実Packageの1値1024文字上限に合わせ、RevealスナップショットをDeflate + Base64で圧縮。Session wrapper込みで上限を超えたら送信を拒否し、非公開を維持する。2人・3人の実データサイズを検証済み。

固定キーは `proposedRevealPrepare` / `proposedReveal`（Global）、`proposedRevealAck`（Client）。全て既存SessionVariableTransportを通し、Session ID / Epochの違うRevealを拒否する。Sessionが増えてもキー数は増えない。

## UI / Hierarchy

```text
ProposedReveal                         [ProposedRevealCoordinator]
ParticipantAnswerReveal                [ReadonlyParticipantBoardView]
└── ReadonlyMiniBoards                 [Reveal時だけ動的生成]
    ├── P1_InitialAnswer               [World-space Canvas]
    │   ├── Border / Surface           [角丸・半透明Dark / teal細線]
    │   ├── Eyebrow / Header / Identity [P1 INITIAL ANSWER / Participant / Client]
    │   ├── Tier_A
    │   │   ├── TierLabel
    │   │   └── Cards
    │   │       └── Card_01            [RawImage + CardName]
    │   ├── Tier_B
    │   ├── Tier_C
    │   ├── Tier_D
    │   └── Readonly                   [READ ONLY / answered枚数]
    ├── P2_InitialAnswer
    └── …
```

カード画像は既存CandidateMarkerManagerのCardImageBindingと同じTexture、名称はCard IDを再利用。InspectorのCard Visualsで表示名も変更できる。色だけに依存せずP番号・Participant番号・Client番号で識別する。

2人は横並び。3人以上は同じ生成処理でGridに増やす。位置・回転・Scaleは `ParticipantAnswerReveal` のTransform、列数・間隔・Board Size・Canvas ScaleはViewのInspectorで調整可能。初期位置 `(0, 1.45, 1.6)`、1枚のサイズ約 `0.792m × 0.924m`、横の隙間約 `0.121m`。

Collider、XR Grab、Selectable、GraphicRaycaster、Network Transformは持たない。全GraphicはraycastTarget=false。Privateオブジェクトを共有・複製しない。

Reveal前はMini Boardを生成せず、旧Candidate Marker / Statusも空。Privateは本人の回答だけを表示。Reveal後はPrivateを非表示・操作禁止、Mainは引き続き非表示・操作禁止のまま。ExperimenterもReveal成立後のReadonly比較Boardを閲覧できるが、Privateを使わず完了判定対象にもならない。

## Reset / 再接続

既存 `OnSessionResetStarted` で確定・準備中スナップショットを破棄し、Mini Boardを即非表示にして削除する。Session defaultsで3固定キー、既存の回答変数・ExperimentStateを初期化する。Privateは前段階のReset処理でUnclassifiedへ戻る。遅延した前SessionのRevealも再表示しない。

同一Sessionに再接続した端末は、サーバーに残った共通Revealから同じ初期回答を復元する。再接続時に相手が不在でも、Reveal済みのParticipant一覧と回答は確定スナップショットから再構築する。NetSyncサーバーがRoomデータを破棄した場合の永続化は追加していない。

## 検証

- 隔離2端末・3端末相当: **58項目PASS**。インストール済みNetSyncのOffline transportと実コンポーネントを使用し、端末間配送のみ検証コードで再現。
- 未回答、取消、秘匿、Prepare / ACK、全員共通Reveal、P1=A/B/C・P2=D/B/Aの配置、Readonly、画像6枚、Main不変、再接続、Session Reset、前Sessionの遅延通知、3人Grid、Experimenter除外、変更と完了の競合、ACKタイムアウト・再試行を確認。
- 前段階・Normalの回帰検証: **25項目PASS**。Private Grab入力処理と既存Normal共有配置、Ready、Final Agreement、Board Confirmedを維持。
- Editor / Android / Editor toolsのコンパイル確認。
- 実Offline Play Mode（保存Sceneのコピー・Expected Count=1）: 全回答から自動Revealed、Mini Board生成、Private操作不可、フレーム進行（22572→51192）、Console errors=0を確認。元のSceneへ戻し、テストSceneを削除。稼働Roomは変更していない。
- **更新版Editor＋Questの実2台同時Reveal・Quest装着時のUI読みやすさは未確認。** 以下の実機手順で確認する。

検証メニュー: `Tools > Experiment > Verify Proposed Readonly Reveal`。結果は `Library/ProposedRevealVerification/result.json`、2人のレンダーは同ディレクトリの `ReadonlyBoards.png`。Normal等の回帰は `Verify Private TierBoard`。

Quest用Development APK: `Builds/ProposedRevealStage2.apk`。Android Build成功（errors=0、warnings=22）。QuestのUSB接続が現在ないため、実機インストールは未実施。

## Editor＋Quest 実機確認手順

1. 第2段階のQuestSetupをQuestへBuild / Installし、Editorも更新コードを使用する。両方を同じNetSyncサーバー / Roomへ接続する。旧第1段階APKはReveal ACKを送らないので混在させない。
2. EditorのLocal Role=Participant、Quest=Participant、両端末のExpected Participant Count=2にする。ExperimenterがNew Sessionを実行し、Proposed / Idleから始める。
3. EditorのGame viewで F6→1、F7→2、F8→3（01=A、02=B、03=C）。Q/W/Eも使用可能。
4. QuestはPrivate CardをGrab / Releaseして01=D、02=Bへ回答し、03をUnclassifiedに残す。この間、相手のTier・Marker・Status・Mini Boardが表示されないことを両端末で確認。
5. Editorで01をUnclassifiedへ取消し、Questで03=Aへ回答する。この状態でもRevealされないことを確認。
6. EditorでF6→1へ再回答。全員ACK後、両端末にP1/P2のReadonly Boardが一括表示され、A/B/CとD/B/Aが正しいことを確認。P番号はclientNoの昇順なのでEditorがP2になる場合は端末を読み替える。
7. キーボード・GrabでPrivate回答を変更できず、Mini Boardも操作できず、Mainが共同編集可能になっていないことを確認。
8. 同じSessionで片方を再接続し、確定した両ParticipantのMini Boardが復元されることを確認。
9. ExperimenterがNew Sessionを実行。両端末でMini Boardが消え、Private3枚がUnclassified、answered=false、selectedAt空、State=Idleになることを確認。Experimenter役は別端末でもEditorの一時的Role変更でもよい。
10. Normalへ戻して共有配置とFinal Agreementの実機回帰を確認。3人テストでは全端末のExpected Participant Countを3にし、3人目の全回答まで非公開・3枚のBoardが表示されることを確認する。
