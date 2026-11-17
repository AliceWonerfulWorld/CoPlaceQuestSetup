# CoPlace 予備実験カードセット

実装日: 2026-11-12。既存の `QuestSetup` に組み込み済み。初期値は既存の料理カード3枚 (`Development`)。

カード表示は同日の視認性改善で54×22cm・4列×2行・大きなTMP名へ更新した。最新の寸法・表示仕様・実機確認手順は [視認性改善報告](PilotCardReadability.md) を参照。

## セット切り替え手順

1. `Assets/Scenes/QuestSetup.unity` を開く。
2. 実験者PCでHierarchyの **PilotCardSets** を選ぶ。
3. Inspectorの **Pilot Card Set Controller > Next Session Set** を `A` または `B` にする。料理カードは `Development`。
4. Experimenterとして接続し、**New Session / Reset** を押す。選択はこの操作で新しいSessionに取り込まれる。Play前の設定も、Play中の設定も可能。
5. Reset完了・参加者2台の接続を待ち、Normal／Proposedを選択してStartする。
6. 次の試行では、Next Session Setを変更してから再びNew Session / Resetを押す。

**Inspector変更だけでは現在のSessionのセットは変わらない。必ずResetしてからStartする。** 参加者側Inspectorの設定は採用されない。参加者2台と実験者PCは、今回変更した同じシーン／コードを含むビルドを使用する。PCでPlay終了後も選択を保存する場合は、Play前に設定してSceneを保存する。

反映値はConsoleの `[Pilot Cards] Session set A/B/Development` またはログの `card_set.csv` で確認できる。参加者向け課題説明は `coplace_pilot_kit/participant_task_instructions.md` をそのまま共通説明に使う。今回は課題説明全文を表示する新UIは追加していない。

Editorの参加者入力を許可した既存テスト設定では、F1〜F8でカード、1〜5でA〜E、0でUnclassifiedを指定できる。Developmentの従来のF6〜F8／Q・W・E操作も残す（Normalでは従来どおりF6〜F8）。

## 調査結果と実装

| 対象 | 既存構造／今回の対応 |
| --- | --- |
| カード管理 | 共有カードはGameObject名 `Card_01` 等の文字列がID。RendererのMaterialで画像を管理。PrivateAnswerCardは別途cardIdを保持。数値IDへの変換は不要だった。 |
| 生成・配置 | 配置済みSceneオブジェクト、XRGrabInteractable、CardTierDetector／PrivateAnswerCard、TierZoneの固定SnapPointを使用。新しい生成基盤は導入せず、共有・個人カードを各8スロットへ拡張した。 |
| Normal | `normalPlacement_<Card ID>` と配置revisionを既存NetSync Global Variableで同期。今回も同じ経路でA01〜A08／B01〜B08を直接使用。 |
| Proposed | `candidate_`／`answered_`／`selectedAt_` のClient Variable、Submitの回答ハッシュ、全カードのPrepare→ACK→Reveal、共有ボード初期化、最終合意をそのまま使用。対象カード一覧だけ選択セットに差し替える。 |
| Tier | A〜E＋Unclassified。回答、Submit、Reveal、最終確定の検証にEを追加。各Tierに8枚全てを置けるID固定スロットを用意し、到着順による端末間の配置差を防ぐ。 |
| Reset | 既存Session制御JSONに `cardSetId` を追加。各端末はResetガード下でセットを適用し、既存状態消去・初期値作成を行ってからACKする。過去カードIDの変数も既存Reset処理が消去する。 |
| セット固定 | Session ID／epochと一緒にセットを保持。同じSession IDで異なるセットの制御は拒否。遅延した過去Sessionデータは既存epoch／Sessionエンベロープで排除する。 |
| PC／Quest共有 | 画像と日本語名は同じビルド内のシーン資産として保持し、ネットワークではセットID・カードID・回答／配置を共有。画像自体の転送は行わない。 |
| ログ | Session ID・方式・フェーズCSVの既存列は変更しない。観測イベントにセットIDを保持し、同じSessionフォルダにUTF-8 BOM付き `card_set.csv` (`session_id,card_set_id`) を追加。詳細操作ログは追加していない。 |

manifestのID・日本語名・画像を取り込み、`type_for_balance_only` は参加者向けデータへ取り込んでいない。料理の元画像・Materialは変更／削除せず保持。Pilotの元PNGは保持し、現在は写真領域だけを元の比率でUV表示する。名前は写真から分離したTMP表示に更新した。Revealの日本語名には専用の静的NotoSansJP SDFを追加した。

セットのカードIDはA01〜A08／B01〜B08として同期変数・Submit・Reveal・最終配置にも一貫して使用する。固定キーは全セット合計19カード分であり、試行ごとに新しいカードキーを作らない。

## 検証結果

### 自動検証・Editor描画

- Unity 6000.3.17f1でコンパイルし、実際のSceneを開いて設定・保存した。
- `Tools > Experiment > Verify Pilot Card Sets`: **409項目成功**。実際のコンポーネントとNetSyncのoffline transportを使い、参加者2端末＋実験者1端末の配信を模擬した。
- A／B各8枚とDevelopment3枚、それぞれNormal／Proposedの計6試行を確認。
- 全カードを同じTier Eへ置いた際の8スロット、端末間配置同期、最終確定を確認。
- 未回答1枚がある場合のSubmit拒否、片方だけSubmitした場合の公開防止、提出後の変更拒否、同時Reveal、全画像の表示、公開後の共同分類を確認。
- Resetによる回答・配置・Submit・Reveal・最終確定の消去、別セットの適用、参加者Inspector設定の無視、実験中の設定変更無視、再接続時のセット／最終状態保持を確認。
- 各試行の `card_set.csv` と既存phase CSV列の保持を確認。
- 既存 `PrivateTierBoardVerification`: **25項目成功**。現在必要なSession作成をテスト準備に追加し、Reset初期値・6ゾーン・非表示予備カードの期待値を更新した。
- 別途、16枚全てのmanifest ID・日本語名・SceneのTexture GUID・Material GUID・元画像とのSHA-256一致を検証。
- 既存 `ExperimentLoggerVerification`: **29項目成功**。内部で実行する既存 `ExperimentResultVerification` も **71項目成功**（Normal／ProposedのDevelopment回帰を含む）。
- A/BのEditorレンダリングを確認し、日本語フォント不足とテキスト高さによる欠けを修正。メイン／個人／Revealの画像対応を検証した。

結果: `Documentation/PilotCardSetVerification.json`、`Documentation/PilotCardAssetAudit.json`、`Documentation/PilotRegressionVerification.json`。Editor描画は `Library/PilotCardVerification/`。既存ログ検証の今回の結果は `Library/ExperimentLoggerVerification/result.json` と `Library/ExperimentResultVerification/result.json`。

再実行はUnityの `Tools > Experiment > Verify Pilot Card Sets`。初回導入処理 `Install Pilot Card Sets` は組み込み済みのため再実行不要（重複導入を拒否する）。元manifestや画像を後から変更しても自動反映はしないため、研究用データを変更する場合はScene内カタログとの再照合が必要。

### Quest実機／実ネットワーク

**Quest 3実機・実ネットワークでの検証は未検証。Android APKビルドも今回実行していない。** Editor内のoffline transport試験は実機検証の代替ではない。

実験前に、Quest 3×2台＋Experimenter PCで以下を確認する。

1. 同じRoomでA01〜A08／B01〜B08の画像・ID・表示名が一致する。
2. Normalで8枚の掴み・移動・遠距離ドロップ、A〜E全Tierへの配置、相手側反映、両者の最終合意が動く。
3. Proposedで他者の独立回答が公開前に見えず、8枚Submit後に両者同じRevealが表示され、共同分類・確定できる。
4. 確定後にA→B→AでResetし、前回の画像／配置／回答／公開／確定が残らない。再接続でも同じ試行を復元する。
5. 8枚横並び時の画像内日本語、コントローラの掴みやすさ、左右のRevealボードの可読性を確認する。カードは8枚を収容するため縮小している。
6. Experimenter PCにSession別の既存CSVと `card_set.csv` が保存される。

## 変更ファイル一覧

追加（付随するUnity `.meta` も追加）:

- `Assets/Scripts/PilotCardSetController.cs`
- `Assets/Editor/PilotCardSetSetup.cs`
- `Assets/Editor/PilotCardSetVerification.cs`
- `Assets/PilotCards/`: PNG16枚、Material16個、`PilotJapanese SDF.asset`
- `Documentation/PilotCardSets.md`
- `Documentation/PilotCardSetVerification.json`
- `Documentation/PilotCardAssetAudit.json`
- `Documentation/PilotRegressionVerification.json`

変更:

- `Assets/Scenes/QuestSetup.unity`
- `Assets/Scripts/ExperimentSessionManager.cs`
- `Assets/Scripts/CandidateSyncTest.cs`
- `Assets/Scripts/NormalPlacementSync.cs`
- `Assets/Scripts/NormalPlacementSync.Editor.cs`
- `Assets/Scripts/PrivateTierBoardController.cs`
- `Assets/Scripts/PrivateAnswerCard.cs`
- `Assets/Scripts/TierZone.cs`
- `Assets/Scripts/IndependentAnswerSubmission.cs`
- `Assets/Scripts/ProposedRevealCoordinator.cs`
- `Assets/Scripts/FinalAgreementManager.cs`
- `Assets/Scripts/ReadonlyParticipantBoardView.cs`
- `Assets/Scripts/ExperimentLobbyUI.cs`
- `Assets/Scripts/ExperimentLogger.cs`（作業開始時から未追跡のログ実装へセットIDを追加）
- `Assets/Scripts/ExperimentCsvLogStore.cs`（同上、既存CSV形式を維持して追加CSVを保存）
- `Assets/Editor/ProposedRevealVerification.cs`（既存Development検証が非表示の予備スロットを対象に数えないよう調整）
- `Assets/Editor/PrivateTierBoardVerification.cs`（Development3枚＋A〜E／Unclassifiedの現構成に検証条件を更新）

開始時点に存在した未コミットのLobby／Result検証／ログ用ファイル・資料・カードキットは破棄していない。Sceneの既存変更を含めUnity Editor経由で保存したため、Unityによるシリアライズ順序の変更も差分に含まれる。コミット・APK配布は行っていない。

## 残る制限

実機での読みやすさ・操作感・実ネットワーク同期、およびAPKビルドは未検証。現在確認済みの自動テストに失敗はない。セットは専用UIではなく実験者PCのInspectorで指定するため、運用時は「選択→Reset完了→方式選択→Start」の順序を守る。セット間の難度やカード理解度の評価は実装検証に含めていない。
