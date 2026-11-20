# CoPlace 実験者管理UI：現状調査・設計・実装計画

調査日：2026-11-19／基準コミット：`e076cd8`＋作業ツリーの現行Scene。**調査・設計のみ。アプリケーションコード・Scene・Prefab・設定・既存ログを変更していない。UnityのPlay停止／開始、Reset、ネットワーク操作、Setup／Verificationの実行もしていない。**

事実はソース／シリアライズ済みSceneの静的確認、問題の実機発生はユーザー報告、提案は将来の設計として区別する。保存済みSceneと稼働中・未保存のEditor状態が一致するとは限らない。今回の表示・実機挙動の再現確認は未実施。

## 1. 調査結果の概要

既存のuGUI、Session Reset、ログ保護を再利用して実現できる。ただし「画面にボタンを足すだけ」では、安全な次回設定と計時を保証できない。次の小規模な管理用API整備を先行する。

1. **現在／次回の設定を分ける。** Setは既に次回Resetで反映されるが、Expected Participant Countは現状随時配信、ModeはLobbyで即時同期という別仕様である。次回ドラフトの選択を直接これらの現在値へ書き込まない。
2. **同期完了／実験開始準備完了を分ける。** Resetは現在生存する全クライアントのACKを待つ。必要人数2なのにQuest1台しかいない状況でもReset自体は完了し得る。人数・各人の現Session ACK・設定一致・ログ／計時の準備まで揃って初めて「実験開始」を有効にする。
3. **Play再起動後の旧Sessionを明示する。** 復元だけで新規準備済みとしない。既存の継続試行を自動Resetせず、開始前の新規準備を明示的に要求する方針を推奨する。
4. **画面の所有元を1つにする。** 現行Sceneには、実行時生成と同名の`ExperimenterSessionReset`ルートが4個保存されている。いずれも表示用Canvas類だけで、Resetボタンの永続onClickは空。動的生成はこれらを再利用しない。古い完了／処理中メッセージの重なりや無効ボタンの原因になり得る。**静的に構造は確認済み、実機で原因を再現したわけではない。** 将来の実装時に対象を特定して除去し、単一の管理画面へ統合する。
5. **日本語文字列とフォントをセットで整備する。** Resetは既定のLiberationSans、他は用途別Static SDFを使う。新用語の文字は既存atlasに揃っていない。文字列の置換だけでは文字欠け・切れを招く。

日常の実験操作をUIへ移すことと、サーバーの配備・ビルド・開発デバッグを実験画面へ持ち込むことは分ける。サーバーの起動／停止まで主画面に常設する必要はない。

## 2. 現在のUI構成

### 2.1 対象Scene・Prefab・構築方法

ビルドSceneは`ProjectSettings/EditorBuildSettings.asset:7–10`の`Assets/Scenes/QuestSetup.unity`のみ。独立したタイトルSceneはない。Lobby内のCoPlace紹介がタイトルを兼ねる。

以下のScene行番号は**今回の作業ツリー**の値。構築用Editorコードは出所・再生成方法の根拠であり、将来それを再実行して既存UIを丸ごと作り直すという意味ではない。

| UI／SceneのGameObject | クラス・ファイル・行 | 表示条件／Role／Mode・Phase | 現機能・構築元 | 問題点／再利用要素 |
|---|---|---|---|---|
| 紹介・タイトル：`ExperimentLobby/LobbyContent/ParticipantIntroduction` | `ExperimentLobbyUI`、`Assets/Scripts/ExperimentLobbyUI.cs:6–8,20–44`。Scene:11944。`Assets/Editor/ExperimentLobbySetup.cs:43–63` | Lobby未開始。両Role／両方式。Reset中もLobby側に戻る | CoPlace見出し、共通課題、方式説明、待機、接続・人数。Editor生成後Scene保存、World Space | 独立タイトル画面はない。説明は再利用し、方式に有利な評価文言を足さない |
| Lobby実験者管理：`.../ExperimenterControls` | `ExperimentLobbyController`／`ExperimentLobbyUI`、`ExperimentLobbyController.cs:20–75,99–112`。Scene:11944/11970 | Experimenterのみ管理部分表示。開始前／Idle、方式変更は中央guardあり | Mode、New Session、Start、Session ID・現在状態。Setup:65–79 | Set・Epoch・ログ・各人ACKがない。開始後管理パネルが消える。RoundedPanelGraphic、ボタン色、フォント・説明を再利用 |
| 旧方式選択：`ExperimentModeCanvas` | `ExperimentModeUI.cs:5–17,54–57`。Scene:44157 | UpdateでCanvasを常に無効化。公共APIのMode変更はManagerに委譲 | Current Mode、Normal/ProposedボタンがSceneに残る | 本番経路で隠れる旧UI。新管理UIと二重表示しない。破棄前に参照／試験用途を確認 |
| 共有分類：`TierBoard/TierBoardCanvas`・カード群 | `NormalPlacementSync.cs:153–181`、`CardTierDetector.cs:177–210`、`NormalPlacementConfirmation.cs:8–18` | Start後のNormal、またはProposed共有分類。Participantが操作、Experimenterは閲覧 | A〜E・Unclassifiedと画像カード、配置、スナップ・ハイライト。Scene中心 | 管理画面を重ねてVR操作を遮らない。識別子A〜E／Card IDは変更しない |
| 独立回答：`PrivateTierBoard/LocalView` | `PrivateTierBoardController.cs:36–43,81–90,141–158`。Scene:3321 | Proposed、開始済みParticipant、未Reveal・非Reset。提出後ロック | 自分だけのカード配置／回答復元。Scene構築は`PrivateTierBoardSetup.cs` | 実験者のPC Stateだけで各人回答段階を判断できない。CanInteract・IsVisibleをそのまま利用 |
| Submit／取消：`PrivateTierBoard/LocalView/IndependentAnswerSubmitCanvas` | `IndependentAnswerSubmissionUI.cs:14–29`。Scene:35500。Setup:`IndependentAnswerSubmissionSetup.cs:24–78` | Private表示中Participant、CanSubmit／CanCancelと0.5秒抑止 | ボタン「この回答で確定」「確定を取り消す」、提出状態／待機 | 最終確定と「回答の提出」が混同される。英語Reveal混在。既存同意API・クリック抑止を維持 |
| 公開した初期回答：`ParticipantAnswerReveal/ReadonlyMiniBoards/Pn_InitialAnswer` | `ReadonlyParticipantBoardView.cs:33–100`。SceneのController:6734。`ProposedRevealCoordinator.cs:29–35` | 確定Revealがあり課題開始中。両Role、Proposed公開以降／Resultまで | SnapshotからWorld Space MiniBoardを実行時生成、全員分をまとめて表示。閲覧専用 | 英語見出しとClientNoがParticipantにも出る。P表示・読み取り専用・イベント再利用。ClientNo詳細は実験者へ限定する案 |
| 候補カード／旧マーカー：`CandidateMarkerManager`、`Assets/UI/CandidateCards/CandidateCard.prefab`、`Assets/Prefabs/Participant{1,2}Marker.prefab` | `CandidateMarkerManager.cs:20–46,51–115`／`CandidateCardView.cs:15–31`。Scene:23624 | CandidateSyncが本人／公開回答の条件で管理。Role別管理操作なし | Pラベル・色・画像・カードID、Prefabを使う補助表示 | 主な初期回答比較はReadonly MiniBoard。旧候補表示を二重に拡張しない。カード画像・色枠は再利用可能 |
| デバッグ回答表示：`CandidateStatusText` | `CandidateSyncTest.cs:388–404`。SceneのCandidateSync:44596、Text:46605 | Controllerの更新対象、内容は本人候補／公開結果。正確な実表示はCanvas・有効状態にも依存 | Self/Participant、Card/Tier、SelectedAt等の文字列 | 研究UIには不要な英語・時刻情報。まず本番非表示にできるか確認し、文字を全て参加者へ出す改修はしない |
| 最終同意：`TierBoard/FinalAgreementCanvas` | `FinalAgreementUI.cs:17–40,43–65`。Scene:18417。`FinalAgreementManager.cs:29–41,155–185` | 両方式の共有分類中、未Confirmed。両Roleに表示、ParticipantのみReady操作 | 各人Ready、合意／取消、人数、失敗理由、0.5秒抑止 | 英語中心。「This board is OK」等。Observerの操作不可は維持。ReadyとCSV保存済みは別状態として表示する |
| Result：`TierBoard/ExperimentResult/ResultContent` | `ExperimentResultUI.cs:8–17,30–55`。Scene:26510。`ExperimentResultSetup.cs:35–59` | Confirmedかつ非Reset。両Role、管理部分はExperimenter | 終了待機、Session ID、New Session/Reset。World Space Scene UI | 表示が確定してもCSV保存成功とは限らない。管理画面で保存状態を併記。Participant終了説明を再利用 |
| PC Reset：実行時`ExperimenterSessionReset` | `SessionResetUI.cs:17–65,90–108`、`ExperimentSessionManager.cs:294–300` | 非Mobile Experimenter。Mode/Phaseを問わず利用、Reset中は無効 | 右上Screen Space Overlay、確認、成功／失敗／切断表示。C#実行時生成 | 440×160領域・status 420×82、長文で縮小。進捗・対象IDが曖昧。未利用保存済みCanvasと競合し得る。既存確認・manager入口を再利用 |
| 接続／エラー／待機 | LobbyUI:30–38、ResultUI:45–48、ResetUI:49–65、FinalAgreementUI:33–36 | 各画面条件に依存 | 接続＝Network.IsReady、接続人数、StartBlockReason、LastFailure、LogSaveFailure | 全実験中に一覧できない。生の英語／例外文をそのまま表示。サーバー稼働と同期完了・参加者接続を分離すべき |

Sceneの`EventSystem`は36927付近、XR/Mouse input有効は37015–37016。既存入力基盤を維持する。今回Play上での実クリック／Ray確認は行っていない。

### 2.2 保存されたReset UIの重複【確認済み】

Scene:2288、24605、47794、48217に同名ルートが4個あり、すべて`m_IsActive:1`。ルートの構成はRectTransform・Canvas・CanvasScaler・GraphicRaycasterのみ。ResetSessionボタンはScene:6325、22105、42796、48760で、対応Buttonの`m_OnClick.m_PersistentCalls.m_Calls`は空。確認ダイアログは4個とも保存時は非Activeである。

`SessionResetUI.Build()`は既存名を検索せず新規ルートを生成し、`Button.onClick.AddListener`で実行時購読する（82–108）。`canvasRoot`は非Serializeのprivate参照なので、Scene保存された上記の表示だけのルートとは結び付かない。HEADにも同名4個が存在し、今回の未コミット変更だけを原因とは断定しない。生成された経緯は確認不能。**古い「Reset complete」表示が実際の完了情報に見えるリスク**があるため、将来の実装で優先的に整理する。今回削除していない。

## 3. Inspector・Unity Tools依存箇所一覧

| 設定項目 | 現在の設定方法 | 管理クラス・根拠 | 適用タイミング | UI化の必要性 | 注意点 |
|---|---|---|---|---|---|
| 実験方式 | Lobbyボタン／Inspector初期値 | ExperimentManager:56–69,220–233、LobbyController:111–112 | LobbyでRoom送信・受信後に即時変更。Start後禁止 | 必須（既に有、次回化） | 今のボタンは次回ドラフトではない |
| NextSessionSetId | PilotCardSetsのnextSessionSet Inspector | PilotCardSetController.cs:16–18,30 | 次回ResetでControlへ取り込む | 必須 | 公開setterなし。管理用の検証付きAPI追加が必要 |
| ActiveSetId／CurrentCardSetId | 内部値、表示UIなし | PilotController:31,34–89、SessionManager:29 | 各端末がReset Controlを受けて適用 | 必須（読取表示） | 編集欄にしない。Nextと一致しない実験中は正常 |
| Expected Participant Count | ExperimentManager上Registry Inspector | ParticipantRegistry.cs:16,25–41,101–106,117–127 | local値を随時配信、最低ClientNoのExperimenterがauthority | 必須 | Sessionに固定されていない。next→applied化はAPIと同期側の変更を伴う |
| Role | localRole Inspector／AutoByPlatform | Registry:9–16,44–50,109–114 | Roleは随時公開 | 推奨（読取）、変更は任意 | Editor Auto＝Experimenter、Player Auto＝Participant。PC Playerは明示設定。本番中は切替不可が安全 |
| Room ID／Name | NetSync Prefab override／SetRoomId API | Scene:19118、package NetSyncManager.cs:24,444–485,567 | 接続初期化、SetRoomIdは接続を再構築 | 推奨（読取＋接続前設定） | 現Sceneはtest-room。Room変更はSession Resetの代わりにしない |
| サーバーアドレス | Prefab override＋Editor自動LAN IP | Scene:19122、NetSyncAutoServerAddress.cs:44–77,136以降 | macOS Editor起動／Play前／ビルド前 | 推奨（読取）、編集は任意 | 現保存値172.22.26.155は一時環境値。自動IPが手動値を再適用し得る |
| 通信用ポート | package内部private初期値 | NetSyncManager.cs:20–24 | Connect時 | 不要（基本固定） | dealer5555/transform5557/sub5556。現行で全てInspector変更可能ではない |
| 検出UDPポート | NetSync Inspector、公開ServerDiscoveryPort | package:63–65,579–582 | サーバー自動検出 | 任意 | 9999既定。サーバーと揃える。参加者向け画面へ不要 |
| サーバー起動／停止／再起動 | Tools/NetSync | NetSyncServerTools.cs:32–61,91–115 | Editor側プロセス制御 | 任意 | macOS Editor専用。Library/NetSyncServerに状態ファイル。外部起動サーバーを停止しない設計を維持 |
| Prepare Environment／uvxパス | Tools/NetSync | 同:118–140 | 環境準備 | 任意 | 実験主画面に入れず、事前運用／詳細障害対応に残す |
| 自動再接続／自動発見 | packageの内部ロジック | NetSyncManager.cs:1206–1286 | 接続／切断時 | 推奨（状態表示） | 接続再試行とReset再送を混同しない。新しい競合する接続ループは作らない |
| Offline mode | NetSync Inspector | package:59–60,613 | 起動時 | 任意 | 評価実験では不可。管理画面へ「オフライン検証中」警告、通常操作欄には出さない |
| Transform同期頻度／Battery sync | NetSync Inspector | package:61–67 | 通信更新 | 不要／任意 | 固定チューニング。必要ならバッテリーは読取運用情報に限定 |
| 初回接続のclient変数消去 | NetSync Inspector | package:67–68 | 初回接続 | 任意 | 研究SessionのReset機構と別。場当たり的に消去させない |
| Session ID／Epoch | manager内部生成 | SessionManager.cs:23,29–30,46–56,313 | Reset要求／Control受信 | 必須（読取）、編集不要 | 自由入力・巻戻しは禁止 |
| Session Reset | 右上／Lobby／Result、ContextMenu | SessionManager:294–326、ResetUI | 確認後。権限・保存確認あり | 必須（統合改善） | 既存機能を置換実装せず同じ入口へ統合 |
| ログ保存先 | persistentDataPath固定、検証用private override | ExperimentLogger.cs:21,30–33 | store初期生成時 | 必須（状態）、推奨（開く） | 任意パス入力は当面不要。ファイル移動・削除機能は付けない |
| Logger有効／参照 | Sceneコンポーネント、Tools Setup | Logger:36–46,198–208、ExperimentLoggerSetup.cs:11–26 | OnEnableから購読 | 推奨（稼働診断）、変更不要 | 無効化トグルを実験画面に置かない。欠落は開始／Resetをブロック |
| ログ再試行 | 自動5秒／次イベント／Reset前flush | Logger:73,214–227 | 保存失敗後 | 推奨（明示的再試行） | 既存Drainへの管理入口。新Sessionを発行する「Reset再試行」と分離 |
| Developmentカード | nextSessionSet=Development Inspector | PilotController:8,16–18,34–41 | Reset時 | 任意 | A/Bだけ通常表示。詳細の開発設定に隔離・警告 |
| 期待参加人数1／2 | 現Registry数値欄 | Registry | 現状随時 | 必須 | 1=動作確認、2=評価実験。アプリ内部は動的件数のまま |
| 現在の選択Card ID／キーボード | Manager Inspector、Editor入力 | ExperimentManager.cs:48–54、PrivateBoard:178–196、NormalPlacementSync.Editor.cs | 明示Participant override時の開発入力 | 不要 | 研究用カード選択UIとは別。PC管理者の回答操作を解禁しない |
| 提出／同意クリック間隔 | コード定数0.5秒 | SubmissionUI:14–29、FinalUI:13–21 | 各クリック | 不要 | 実験中に調整しない |
| Reveal準備タイムアウト | Coordinator Inspector preparationTimeout | ProposedRevealCoordinator.cs:22,86–105 | 準備中 | 任意（読取／詳細） | 通常設定から除外。公開を強制する復旧ボタンは不要 |
| Resetタイムアウト／送信再試行 | コード30秒／1秒 | SessionManager:125–126,238–250,276–278,316 | Reset中 | 不要（値変更）、必須（状態） | タイムアウトは中止・新GUID発行ではない |
| 全員Ready／最終確定 | Participantの既存UI | FinalUI:43以降、FinalManager:119–154 | 両方式共有分類 | 不要（管理操作化） | Experimenterの代理Ready／強制確定は提案しない |
| カード画像／manifest／Tier／スナップ寸法 | CSV・Asset・Scene・Setup | PilotCardSetSetup.cs、PilotCardFace.cs、TierZone.cs | 事前構成／ビルド | 不要 | 実験条件と研究ID対応を実験中に編集しない |
| Reveal／カード表示の配置・色・Pラベル | Inspector／Prefab／Setup | ReadonlyBoard:14–19、MarkerManager:26–46 | 表示生成 | 不要 | Quest向け読みやすさは開発で調整。個人識別は外部表、Pair入力UIは今回範囲外 |
| 日本語フォント／Canvas・参照 | Scene／Static SDF／Editor Setup | LobbySetup:84–103、ResultSetup:64–79 | ビルド前 | 不要（実験中設定） | フォント再生成は開発作業。Toolsを日常進行に要求しない |

ネットワークのPrefab本体はPackageCache配下。Scene overrideだけで確認できない項目はpackageの既定値を確認したもので、稼働中の値を読み取ったという意味ではない。通常のScene/Prefabs/ScriptableObjectに独立した「セッション実験条件」AssetやPair ID設定UIは見つからなかった。

## 4. Session Resetの処理フロー

主要根拠：`Assets/Scripts/ExperimentSessionManager.cs`。要求のtrue返却は完了ではない。

| 段階 | 現処理・順序 | 現UIが読めるもの | 足りないもの／設計 |
|---|---|---|---|
| 1 要求 | ResetUIの確認→ResetSession→StartNewSession（UI:29–39、manager:302） | CanReset、IsConfirmationOpen | 要求元Session ID/Epochと確定ドラフトをUI commandへ保持 |
| 2 権限 | Subscribe、Experimenter、Network.IsReady、既存Reset/pending、参照、Unclassifiedを確認（304–309） | LastFailure／IsResettingSession | 失敗分類コード、操作理由の日本語表示 |
| 3 旧ログ確認 | Logger存在・TryFlushBeforeReset（310–311）。必要Snapshot捕捉→Drain（Logger:198–208） | Logger.LastError、OutputDirectory | pending数・保存中・必要ファイル保存済みのread-only状態。UI Updateからflushを呼ばない |
| 4 ID発行 | GUIDとEpoch+1（313）。Controlにmode/setを入れる | 受信後CurrentSessionId/SessionEpoch | pending要求IDはprivate、要求中の表示用ID未公開 |
| 5 設定確定 | nextSetをControlへコピー。**expectedCountは含まない**。modeは現在値（313） | CurrentCardSetId、SessionMode | applied settings版／期待人数／ドラフトとの一致情報 |
| 6 送信待ち | pendingSessionRequestId、IsResetting=true、Control送信（315–319）。未確定なら旧Sessionの中断を記録（320） | IsResettingSession、LastFailure | 「送信受付」と「Roomから通知受信」を区別する状態 |
| 7 各端末受信・適用 | OnGlobalはキュー、UpdateでReadControl。新IDならcache消去→Set適用→OnSessionResetStarted→ClearLocal/BuildDefaults（81–83,121–140,187–207） | OnSessionResetStarted(id)、HasResetControl | このイベント時点でカードSetは適用済みだが**ClearLocal完了前**。単独で「全初期化完了」と表示しない |
| 8 変数初期化 | 各clientのdefaults、ownerのglobalsを送信。受理されなければ1秒再試行（236–252） | 直接の公開progressなし | local/global defaults queuedはprivate。read-only enum／結果だけ公開 |
| 9 ACK送信 | 自分の全defaultsがechoで一致後、AckVariableに現IDを送る（255–261） | net.GetClientVariable(AckVariable,client)で取得可能 | 全体ACK一覧用APIなし。UIはmanagerの読取Snapshot経由が望ましい |
| 10 owner同期完了 | 全global echo一致、**全生存client（stealth含む）**のACK一致→IdVariable、active Control送信（263–271） | currentID、Control phaseはHasResetControlで一部判別 | requiredParticipantsと同期対象端末数は別。管理用GetResetProgressで分母と内訳を提示 |
| 11 ローカル完了／Lobby | 各端末はactiveかつ自分のdefaults/ACK条件後CompleteLocal→IsResetting=false、既存状態復元→OnSessionStarted（273,281–292） | OnSessionStarted(id)、IsResettingSession | **OnSessionStartedはReset完了通知**。課題開始は別のLobby.OnExperimentStarted。命名は維持して表示を誤らない |
| 12 次の課題開始許可 | Lobby未開始・ID/activatedAt・人数完全一致・Role・Idle・Mode同期がCanStartを構成（Lobby:54–75） | CanStart、StartBlockReason | 新規準備token／Logger準備／各Participant現ACK／設定版の一致は追加必要 |

### 障害・復旧の現挙動

- 保存失敗：新Controlを送らず拒否。UIに理由。保存先復旧後の明示的再試行が可能。Loggerは自動再試行もある。
- Control送信拒否：pendingを戻し失敗。同期完了扱いにしない。
- Control echo未着：pendingが残ってTryAdvance待機。30秒後に警告、操作はブロックしたまま。任意の新GUID再送機構はない。
- 各端末初期化例外：localResetFailed=true、以降その端末のTryAdvanceは停止（135、234）。同ID再受信だけでは新ID用初期化分岐へ入らない。**現在の公開APIだけでは安全な再初期化ボタンを作れない。** 別途、失敗段階に応じた限定復旧APIの設計・検証が必要。
- Participant切断：ACK対象は各回GetAliveClientsで求め直す。切断検知後はそのclientが分母から外れ得るのでReset完了しても必要人数不足でStart不可、があり得る。再接続clientは現Controlを復元し自分の初期化・ACKを行う。元の人を特定する安定IDはない。
- Reset ownerの実験者PCが切断した場合、activeへの移行を送るのはControl.ownerと同じClientNoだけ（263–271）。owner再接続時のClientNo維持・再選出はアプリ側で保証しておらず、owner不在で待機が続く可能性がある。進捗にはowner通信IDと接続状態も出す。単なるACK再試行では直らない場合は、未開始・旧保存完了・対象ID/Epoch固定を検証する明示的な再準備／owner移譲APIが別途必要。現行のguardをUIだけで外す復旧は提案しない。
- タイムアウト文「Reconnect peers or retry New Session」は、実際にはIsResetting/pending guardで新要求を拒否する場合がある。文言だけ「再実行」とせず、保存再試行／接続回復／復旧不能の分類を表示する。
- 旧データはSessionVariableTransportのSessionラッパー、Control Epoch、cache修復で排除する（manager:31–37,81–117,123、SessionVariableTransport.cs:9–24）。global experimentModeは例外的に非ラップ。今後の設定適用でもModeの一致確認を省かない。
- Reset中Startは既に拒否。完了後に新参加者が加入した場合、現在のStart条件はその人の現Session ACKを明示確認しない。Idle echoと他defaults ACKの間にStart可となる余地は静的に考えられるが、実機再現は未検証。追加gateで塞ぐ。

**提案：Reset進捗はmanagerから読み取る。** 別のReset処理をUIに実装しない。公開するのはRequestId/TargetSessionId、stage、local/global初期化結果、ACK対象の現在一覧と各値、ownerの接続、LastFailureCode/Detail、完了したID/Epoch。進捗％や架空の所要時間は出さない。同期対象が変わったことも文字で示す。

## 5. セッション設定の適用タイミング

### 5.1 現仕様【確認済み】

| 設定 | 編集できる時期／反映 | 現Sessionの扱い |
|---|---|---|
| Mode | LobbyかつIdle・全peer IdleのguardでSetMode送信、Room受信でCurrentMode更新（ExperimentManager:56–69,220–233）。Start後は拒否 | Control.modeはReset要求時の値だが、Reset後LobbyでMode変更できる。SessionModeと実際にStartしたModeが異なる場合がある。正式ログconditionはStartのMode |
| Set | Inspector nextSessionSetはいつでも変えられる。Reset要求時にその値をControlへコピー→各端末でApplySessionSet | ActiveSetId/CurrentCardSetIdは次のResetまで変わらない。変更予約が可能。Controllerに公開setterはない |
| Expected Count | Inspector変更をUpdateで配信。authorityは最低ClientNoのExperimenter。受信側はその現在値を参照 | Current／Nextの分離・Session/Epoch固定がない。Start、Reveal cohort、Finalの人数条件が実行中でも変化し得る |
| Role | Auto/overrideを随時配信。RoleによるParticipant一覧も変化 | 実験中のRole変更・人数変更は安全な条件変更UIとしては提供しない |
| Room／Server | 接続初期化またはRoom切替API・Editor IP更新 | 同じSessionの軽い条件変更ではない。詳細設定は未接続時のみが基本 |

作業ツリーの保存Sceneでは`nextSessionSet:1`＝A（19875付近）、`currentMode:1`＝Proposed（45745付近）、`expectedParticipantCount:1`・`localRole:0`＝Auto（45780付近）。これらは保存初期値であり、Room同期後の稼働中の適用値を示すものではない。ActiveSetIdのコード初期値Developmentも、過去Session復元後の値の証明ではない。

### 5.2 推奨するCurrent／Draft／Appliedの関係【設計案】

- **Draft（次回）**：Mode/Set/人数の3値。UI選択はPCのローカルドラフトだけを変更する。実行中も予約可能。「次回の準備時に適用」と常時表示する。
- **Applied（現在）**：新Session準備要求でドラフトをコピーし、ID/Epochと結び付けて同期する。Mode/Set/expectedCountをControlに束ね、各端末で同じ条件を適用する。現在の条件は読取専用。
- **Dirty／未適用**：準備後・Start前にドラフトを変えた場合、現在との差を示す。誤適用防止を優先し、**再準備までStartを無効**にする案を推奨。実験中に次回ドラフトを変えることは、現在の実験進行／ログに影響させない。
- **準備済みtoken**：このPC／Logger観測runで自分が要求し、対応するID/Epoch・設定版のReset完了を観測した証明。新runへは持ち越さない。受信した古いactive Controlだけで発行しない。

最小拡張は既存managerの要求引数／Controlへ設定Snapshotを加え、既存Resetの同じ段階で適用すること。UIがprivate fieldをreflectionで変更する構成は採用しない。Modeは旧試行がactiveな間に既存SetModeを呼んでもguardで拒否されるため、**先に旧ログ保存→新Control受信／Lobby解除→Reset内の設定適用→既存Mode同期→各端末ACK**の順とする。`ResetExperiment`は現在RestoreRoomModeを呼ぶだけ（ExperimentManager:265–271）なので、ここを含めて新Control.modeを正とする準備用適用経路を整理する。実験中のSetMode guardは残す。

expectedCountをControlへ追加する際も既存Role／expectedCountキーは原則維持し、RegistryがSession適用値を返す経路と次回ドラフトのsetterを分ける。単にInspector用のsetterをUIへ公開すると実行中に変わってしまう。旧Controlに新フィールドがない場合は勝手に1名／2名へ推定せず、旧構成の復元表示＋新規準備要求にする。PCとQuestの同一対応ビルドへの更新が必要。

### 5.3 人数変更とACK

- 1→2：次回ドラフトを2名へ変更しても、進行中の1名試行は1名のまま。新規準備後は接続中端末のReset同期が終わっても「必要参加者待ち 1/2」。2人目が接続し、現Session ACKとRole・Idleが揃うまでStart不可。
- 2→1：次回を1名にしても接続中の2名を自動的に先頭1名へ切り捨てない。2/1なら「参加者が1名多く接続しています」。不要端末を切断する等、明示的に1名へ揃える。管理UIでParticipantを強制退室／Experimenter化する機能は今回必須にしない。
- ACK対象は引き続き接続中の全対応client（実験者を含む）とし、必要Participant人数は開始条件の別値。**ACK 2/2＝Participant 2/2ではない。** 表示は「参加者初期化1/2、実験者初期化1/1、現在の全同期対象2/2」など役割と分母を明示する。
- 参加者の差し替えや切断後の同一試行再開は、単に人数が再び2になれば同じ人と判断しない。最小の本実験運用は中断・新規準備。厳密なcohort固定／差し替え許可は§14で決め、今回のUI化に隠れて実験仕様を変えない。

## 6. 問題点と発生原因

| 優先度 | 問題・根拠 | 原因／影響 | 対応方針 |
|---|---|---|---|
| P0 | Sceneの表示用Resetルート4個と動的生成が共存（§2.2） | 古い完了文字が状態に見え、無効ボタン／前面Graphicが操作を遮る可能性 | 所有元の単一化・対象限定のScene整理を実装第一段階に含める。実機の真因かは追加確認 |
| P0 | Play再起動後、旧準備済みIDでStartできる条件が残る（Lobby:64–75、CsvStore:121–125） | 既存SessionのCSVがあると新storeは信頼できる単調originを持たず、新イベントelapsedが空になる | 明示的新規準備＋run/session readiness gate。継続中の実験は表示・回収のみ、自動Resetしない |
| P1 | CountがSession固定でない（Registry:25–41,101–106） | 次回操作のつもりで現在のReveal／合意／Start条件を変更する | DraftとAppliedを分離しResetで固定 |
| P1 | StartはLogger準備・保存失敗・計時origin・各人ACKを確認しない（Lobby:54–75） | Reset後の新ログ失敗や復元Sessionでも開始できる余地 | 中央のStartBlockReason／CanStartを強化し、UI/API双方に適用 |
| P1 | Modeは即時、Setは予約、Countは随時 | 同じ「次回設定」として見せるだけでは異なる動作をする | Controlに束ねる準備トランザクションと設定版一致確認 |
| P1 | Reset完了の表現が要求元UIのメッセージだけ（ResetUI:46–49） | 別入口・再起動ではメッセージの連続性がない。何のIDが完了したか不明 | managerの完了ID/Epochを主情報として表示。要求中と受信済みを区別 |
| P1 | CSV状態がLastErrorのみ（Logger:30–34,198–228） | エラー空を「全回答保存済み」と断定できない。pending数／必要Snapshot状態が公開されていない | read-only SaveStatus APIを追加。Final Confirmedと保存成功を別表示 |
| P1 | ACK待ち・初期化失敗・タイムアウトが同じ長文 | 再要求をguardが拒否する状態で「New Session再試行」と案内する | 理由コードと段階別復旧。進行中に新GUIDを発行しない |
| P2 | UIに英語・Enum・生の例外・技術情報混在 | 参加者がSubmitを最終確定と誤解、説明や実験者操作に迷い | 表示層だけ日本語化、詳細はPCへ折り畳み |
| P2 | Static SDFに新用語が不足、ResetはLatin既定font | 文字欠け・縮小・ボタン文字切れ | 日本語font適用と全表示文字の静的検査、実機で1〜1.5mの確認 |
| P2 | 管理がWorld SpaceとPC Overlayに分散 | PC操作とVR操作の必要情報が混ざる | PCに単一管理画面、Participantは操作に必要な既存VR UIのみ |

P0はUI／運用の重大度分類であり、今回実機で全ての故障を再現したという意味ではない。

## 7. Experimenter向け管理UIの設計案

### 7.1 構成

PC専用uGUI Screen Space Overlayの管理画面1つ。既存の暗い背景、青緑Accent、角丸パネル、白文字を継承する。通常画面でボード全体を覆わず、右側の管理領域＋必要なら折り畳み。Participantでは管理Canvasを生成／有効化しない。確認中のみPCの操作をmodalで遮断する。World Spaceの参加者操作は既存UIを利用する。

- **現在のセッション**：通常方式／提案方式、適用済Set、必要人数、Session ID・Epoch、意味に応じた現在フェーズ、接続と初期化の一覧、保存状態、計時状態、課題進捗。
- **次回セッションの設定**：通常方式／提案方式、A／B、1人（動作確認）／2人（評価実験）。進行中もここだけは編集できる。「現在の実験には反映されません」を表示。Developmentは折り畳みの開発設定。
- **操作**：「新規セッションの準備」「実験開始」「実験中断…」「ログ保存先を開く」。常設する障害操作は「保存を再試行」と接続状態の確認。サーバー停止・強制公開・強制確定は入れない。
- **処理状況**：新規要求の対象ID、実際の段階、Participant ACK一覧、実験者を含む全端末ACK数、問題と次の操作。

Session IDは短縮表示＋全文コピー／詳細を推奨。ログフォルダにはユーザーのホームディレクトリを含むため、PCのみ表示。ClientNoは「通信ID」と明示し、研究用Participant IDではないことを詳細に注記する。Participant画面にSession ID・Epoch・パス・エラーstackは出さない。

### 7.2 データ源と追加APIの最小範囲

| 表示／操作 | 再利用するもの | 追加が必要なもの（提案、未実装） |
|---|---|---|
| 現在条件 | CurrentCardSetId、ActiveSetId、CurrentMode、StartedMode、Epoch | AppliedSettings(mode/set/count/revision)。Control.modeと実際のModeの二重解釈を解消 |
| Phase | IsExperimentStarted、HasRevealed、shared.IsActive、IsBoardConfirmed | 表示用変換。Idleを一律「待機」にしない（Normal active時は共同分類、Proposed未公開は独立回答） |
| 各参加者状態 | Registry.ParticipantClientNos、Network.IsReady、各Role・現Session ACK、Submit状態、Ready API | 既知clientの接続履歴を持つread-only view。消えた人を即一覧から消さず切断として表示。架空の人名を作らない |
| 課題進捗 | CardIds／現在Tier、公開Snapshot、valid submission、IsParticipantReady | 必要枚数に対する配置済数／提出人数のread-only集約。実験者だけが見る情報をParticipantへ漏らさない |
| Reset進捗 | managerの既存private段階とイベント | ResetProgressの公開。UIは別の状態判定／Resetを再実装しない |
| 保存状態 | LastError、OutputDirectory、pending、確定Snapshotの書込結果 | SaveStatus：configured/writer、pending件数、保存中、最終成功、現Sessionの必要Final/Initial保存済、原因コード。キュー0と必要回答有無を合わせる |
| 計時 | store.runId、origin、Logger.liveSession/baseline | ObserverRunId、MonotonicOriginAvailable、continuity／復元状態、準備tokenのread-only情報。既存のprivate実装をUIがreflectionで読む設計にしない |
| 保存先を開く | Logger.OutputDirectory | PC限定のフォルダopen command。既知パスのみ、任意コマンド／ファイル削除は不可。開けなければパスコピー |
| 設定選択 | Pilotの列挙型、Mode、ExpectedCount既存同期 | ローカルDraftと検証付き準備要求。既存setter直呼びで現在値を変えない |
| 中断 | 同じReset入口＋MarkResetAccepted/SESSION_ABORTED | 破壊範囲と次回設定を示す確認。新しい独立した「中断Reset」機構は作らない |

「保存済み」は、そのSessionで存在すべきデータを確認した結果とする。開始前／未公開ならInitialは「対象外／まだ公開されていません」、未確定ならFinalは「未確定・保存対象なし」。ファイルがないだけでエラーにも成功にも決め付けない。

### 7.3 根拠のある進捗表示

| 文言 | 根拠／意味 |
|---|---|
| 前回の実験データを確認・保存しています | 実際にTryFlushBeforeResetを実行するcommand段階。同期処理なので表示は一瞬または次frameまで描画されない場合がある |
| 新しいセッションの通知を送信しています | pending requestがあり、対象Controlのecho未観測 |
| 実験条件とカードを適用しています | ReadControlの新ID適用処理中。終了後は「適用済み」にする |
| 接続端末の初期化を待機中 | defaults echo／全alive ACKが未完了。実際の一覧を表示 |
| 接続中端末の同期完了・必要参加者待ち | manager完了だが指定人数不足、人数超過、新加入ACK未完等 |
| セッションの準備完了 | 現IDの完了＋設定一致＋正確な人数／Role／ACK＋Logger／計時＋新規準備tokenが全て成立 |
| 準備に失敗しました／同期の完了を確認できません | 送信拒否・ローカル初期化例外・ACKタイムアウト・切断を区別 |

同期的なSave/Applyを長く見せるための演出や、仮の進捗％・「80%」は入れない。UI repaintを必要とする場合はcommandのbusy表示→次frameで既存要求を実行する程度にし、処理の意味を変えない。

## 8. 画面ワイヤーフレーム

以下は設計案。数値・IDは例であり実Sessionデータではない。PC幅に合わせて一覧と詳細を折り畳む。色だけでなく必ず状態名・原因文を示す。

### 通常時／開始前

```text
┌ CoPlace 実験管理 ─────────────────────────────────────┐
│ [接続済み・同期完了]  Room: test-room       [接続の詳細] │
├ 現在のセッション ───────────┬ 次回セッションの設定 ──────┤
│ 通常方式 / セットA / 必要2人 │ 方式 [通常方式][提案方式]  │
│ ID: 12ab…90cd [コピー]      │ セット [A][B]             │
│ Epoch: 8  状態: 開始前      │ 人数 [1人 動作確認][2人 評価]│
│ 参加者1 通信ID1 接続済/初期化済│ 次回準備時に適用されます  │
│ 参加者2 通信ID2 接続済/初期化済│ [新規セッションの準備]   │
│ 接続人数 2/2  全端末ACK 3/3 │ 現在との差: なし           │
│ CSV: 必要データ保存済み     │                            │
│ 計時: この観測runで準備済み │                            │
├ 状況: セッションの準備完了 ┴───────────────────────────┤
│ [実験開始]    [実験中断… 無効]   [ログ保存先を開く]      │
└───────────────────────────────────────────────────────┘
```

### Reset中

```text
現在: 新セッション 34cd…ab12 / Epoch9 / 提案方式 / B / 2人
処理: 接続端末の初期化を待機中
前回データ保存: 完了     条件適用: 完了
参加者1: ACK受信済み     参加者2: 初期化待ち
実験者: ACK受信済み      全同期対象: 2/3
[実験開始 無効] [新規準備 無効] [接続の詳細] [ログ保存先を開く]
※完了するまで新しい要求を送らない
```

参加者2が切断された場合：「接続中端末の同期は完了しました／参加者2は未接続／必要人数1/2、開始できません」。全端末ACK2/2だけを緑の「準備完了」にしない。

### 実験終了時

```text
状態: 最終結果が確定しました
現在: 提案方式 / B / 2人 / ID 34cd…ab12
回答: 初期回答16件 保存済み / 最終回答8件 保存済み
フェーズイベント: 保存済み / 未保存0件
計時: 通常観測              [保存先を開く]
次回: 通常方式 / A / 2人    [新規セッションの準備]
[実験開始 無効] [中断 無効]  ※参加者画面は終了待機を維持
```

件数は動的。開発用3枚・1名なら初期3件／最終3件。確定後に保存処理が残る場合は「結果は確定済み／CSVを保存中」と分離する。

### エラー／過去Session復元時

```text
[保存失敗] 前回の回答データを保存できません。新規準備できません。
未保存: 2件   対象: 現Session   原因: 保存先へ書き込めません
[保存を再試行] [保存先を開く] [詳細をコピー]
[実験開始 無効] [新規準備 無効]   ※既存CSVは削除しない

または

[過去のセッションを復元しています] ID 12ab…90cd / Epoch8
計時: 現在の観測runでは継続性を確認できません
[実験開始 無効] [状態・保存済み結果を確認]
[新規セッションの準備…]  ※継続中なら中断確認を要求。自動Resetしない
```

確認ダイアログは旧ID・現在状態・保存確認・次回mode/set/countを表示する。進行中は「実験を中断し、次の待機セッションを準備します」と明記。確定済みなら「保存済み結果は保持します」。Cancelは「戻る」または「準備を取り消す」とし、提出取消と区別する。

## 9. 日本語化対象一覧

### 9.1 表示文字列と配置元

C#識別子・Enum・通信キー・CSV列／イベント名は変更しない。表示層に小さな日本語辞書／enum変換を置く案を推奨する。日本語1言語のためだけにLocalization packageや大規模なテーブル基盤を必須にしない。

| 対象／現表示（代表的な全グループ） | 配置元・行 | 改善案／必要作業 |
|---|---|---|
| CoPlace、EXPERIMENT INTRODUCTION、共同VR空間… | LobbySetup:45–48、Scene:27587/34374/15754 | CoPlaceは維持、補助見出し「実験の説明」。本文は既存の公平な説明を利用 |
| NORMAL / 共同分類、PROPOSED / 個別回答→比較・共同分類 | LobbyUI:27–28、LobbySetup:51–52 | 「通常方式／共同分類」「提案方式／独立回答→回答の公開→共同分類」。優劣を表す語を足さない |
| EX­PERIMENTER、Normal / 共同分類、Proposed / 個別回答、New Session、Start Experiment | LobbySetup:67–74、Scene:43949/20633/35311/29672/3461 | 「実験者」「通常方式」「提案方式」「新規セッションの準備」「実験開始」。現行buttonsを次回ドラフトへ束ねる |
| Participants n/m Connected、CONNECTION、Connected、Waiting | LobbyUI:30–35、LobbySetup:55–59 | 「参加者 n/m人 接続済み」「接続状態」「接続済み／同期を待機中／未接続」。TCP接続だけで「準備完了」にしない |
| Session ID、Resetting、Idle/Answering等Enum名、未作成 | LobbyUI:37–39、LobbySetup:73、ResultUI:45 | 「セッションID」「準備中」。Enumは文脈変換。Idleは開始前／通常共同分類／独立未操作を分ける |
| 「実験者の開始を…」「参加者不足」「Role登録同期」「New Sessionで…」 | LobbyController.StartBlockReason:54–72 | 「実験者が開始するまでお待ちください」「参加者の接続を待っています（1/2人）」「実験者の登録を同期しています」「新規セッションの準備が必要です」等。内部Role語を見せない |
| ModeChangeBlockReason英語 | ExperimentManager:60–67 | 管理画面で「実験開始後は現在の方式を変更できません」等へ。Consoleとprotocolは維持 |
| Current Mode: Normal/Proposed、旧ボタン名 | ExperimentModeUI:54–57、Scene:21370/27935/35881 | 本番非表示を維持。残す開発表示は「現在の方式」。非表示を理由に新画面へコピーしない |
| この回答で確定／確定を取り消す | SubmissionUI:19 | 「回答を提出」「提出を取り消す」。Finalと区別 |
| 回答確定済み／相手の回答を待っています…／回答確定を取り消しています… | SubmissionUI:20–23 | 「提出済み／他の参加者の提出を待っています」「提出を取り消しています」。「相手」は1名動作確認に不適切なので人数依存しない表現 |
| 内容を確認して確定してください／一斉Revealを準備 | 同:22–23 | 「内容を確認して回答を提出してください」「全員の回答を公開する準備をしています」。公開ボタンを新設せず既存全員Submit→公開を維持 |
| INDEPENDENT ANSWER / REVEALED、Pn INITIAL ANSWER、Participant n · Client x、READ ONLY / n/n ANSWERED | ReadonlyParticipantBoardView:75–78,100 | 「公開された独立回答」「参加者nの独立回答」「閲覧専用／n枚回答済み」。通信IDはPC詳細へ。P1等の識別表示は必要なら併記 |
| Self / Participant / Answered / SelectedAt | CandidateSyncTest:393,402、CandidateStatusText | 原則本番デバッグ非表示。必要な管理情報だけ集約し、Questに技術詳細を出さない |
| P1/P2ラベル・候補Card ID | CandidateMarkerManager:26–30、CandidateCardView:15–24、候補／marker Prefab | 「参加者1（あなた）」等の表示。Card ID・P識別子の研究上の対応を崩さない。色を唯一の区別にしない |
| FINAL AGREEMENT／FINAL BOARD CONFIRMED、This board is OK、Cancel Ready、Board Confirmed | FinalAgreementUI:22–24 | 「最終結果への同意」「最終結果が確定しました」「この配置に同意する」「同意を取り消す」「確定済み」 |
| Participants、(You)、Ready、Waiting、Experimenter: view only | FinalAgreementUI:25–35 | 「参加者」「あなた」「同意済み／同期待ち／未同意」「実験者は閲覧のみ」。Ready pendingを確定同意済みにしない |
| 未分類／同期／掴み中／Ready送信失敗／容量上限の英語 | FinalAgreementManager:137,149,173–183,315→FinalUI:36 | 「未分類のカードをすべてA〜Eへ配置してください」「カードを離してください」「配置の同期を待っています」「同意を送信できません」「結果を同期できません。実験者へお知らせください」 |
| FINAL RESULT、Mode: … / Confirmed、Session ID、New Session / Reset | ResultUI:8,45、ResultSetup:48–59 | 「最終結果」「方式／確定済み」「セッションID」「新規セッションの準備」。Participantには終了待機だけ |
| Reset Session?、Reset Session、Cancel、Saved CSV logs will be kept… | SessionResetUI:28,34–38,65,99–107 | 「新規セッションを準備しますか」「準備する」「戻る」「保存済みCSVは保持します」。進行中は「中断して準備」を明示 |
| Resetting…、Reset complete…、Reset pending…、disconnected、Session or connection changed | 同:46–56 | 「接続端末の初期化を待っています」「同期完了／参加者待ち」「準備完了」「未接続です」「接続または対象セッションが変わったため取り消しました」。状態取得根拠を優先 |
| CSV save failed、manager.LastFailure、Logger.LastError／OS例外全文 | ResetUI:37,52,63、SessionManager:244,251,269,278,305–319、Logger:200–227 | PCへ原因分類＋再試行案、原文は詳細／コピー。Participantは「実験者の対応をお待ちください」。生のパス／stackをQuestへ出さない |
| Next Session SetのTooltip／Role overrideの長い英語 | PilotController:16、Registry:14、package Inspector tooltip | 日常操作をUIへ移すと依存低下。新UIの補助説明は日本語。packageの全Inspector文言翻訳は不要 |
| Unclassified、Card ID、Tier、画像のイベント名 | Scene TierBoard、PilotCardFace:27–43 | 未分類の見出しだけ「未分類」へ。Tier A〜E／Card IDは維持。課題カードの内容・評価／正解は変更しない |

補足：カード面は`PilotCardFace.Configure/Populate`で動的Canvasを生成する。配置予定枠は`TierZone.cs:108以降`で動的生成され、文字の管理UIではない。Revealは`ProposedRevealCoordinator.cs:56以降`の全員提出・準備／commit経路で自動実行される。手動Revealボタンは既存の実験仕様にないため、新管理画面にも追加しない。

### 9.2 用語・文脈の統一

基本語：Normal＝通常方式、Proposed＝提案方式、Experimenter＝実験者、Participant＝参加者、Session＝セッション、New Session/Reset＝新規セッションの準備、Start Experiment＝実験開始、Card Set＝カードセット、Submit＝回答を提出、Cancel Submission＝提出を取り消す、Reveal＝回答の公開、Independent Answer＝独立回答、Shared Classification＝共同分類、Final Agreement＝最終結果への同意、Confirmed＝確定済み、Final Result＝最終結果、Waiting＝待機中、Connected＝接続済み、Disconnected＝未接続。

「待機中」だけでは、人数待ち・ACK待ち・他者提出待ちを区別できないので理由を後ろに付ける。Resetは技術操作名として詳細に併記可能だが主ボタンに重複した英日名は不要。Submitを「確定」と呼ばず、同意・最終確定と分ける。IDやファイル名は翻訳しない。

### 9.3 フォント・改行・Quest可読性【静的確認＋検証計画】

- `Assets/TextMesh Pro/Resources/TMP Settings.asset:27,36`は既定LiberationSans SDF、global fallbackは空。ResetUI:78はこの既定font。日本語へ差し替える際はCJK対応assetを明示する必要がある。
- `Assets/Fonts/IndependentAnswer/{LobbyJapanese,SubmitJapanese,ResultJapanese} SDF.asset`、`Assets/PilotCards/PilotJapanese SDF.asset`は用途別の文字集合を持つStatic atlas。fallbackも空の設定を確認した。生成元はNotoSansJP.ttf。
- シリアライズ済み`m_CharacterTable/m_Unicode`をPythonで読み、提案する用語群と照合した。文字数はLobby180、Submit48、Result99、Pilot156、LiberationSans250。例としてLobbyにも「通・常・提・案・出・存・敗」等が不足、Submitには「提・出」等が不足する。**実際にTMP/Questで表示した検証ではなく、現在のfont内コードポイントの静的照合**である。
- 実装時は確定した全表示文字列（ボタン・失敗文・数字・句読点・「あなた」等）を先に列挙し、必要なStatic SDFを更新。CJK全文字の巨大atlasや新packageを無条件に追加しない。既存カード用文字集合も保持する。
- Lobbyは横幅532の管理ボタンで34pt・高さ118、Startは174（LobbySetup:70–75,130）。Resetは230×52／23pt（ResetUI:84–87）、statusは最大20→最小14へ自動縮小。日本語化で「新規セッションの準備」をそのまま同幅へ入れず、PC側幅を確保する。ID・長いOSパスは折り畳み／コピーへ。
- VRは既存World Space scaleと配置を基準に1〜1.5mから読めることを実機確認し、文字数増を安易なfont縮小で吸収しない。提出／同意の長いボタンは2行も許容し、Ray／Direct判定と重ならない余白を検証する。
- 通常方式・提案方式の説明は手順だけを中立に記述する。「公平」「意見を反映しやすい」等の誘導評価を追加しない。

## 10. 操作ミス防止・権限制御の設計

### 10.1 共通のStart判定

中央の`ExperimentLobbyController.CanStart/StartBlockReason`を唯一の最終判定とし、PCボタン無効化だけに依存しない。既存のRole・接続・Idle・Mode guardを維持し、次をAND条件として追加する。

1. 実験者Roleが登録同期済み、接続／initial sync済み。
2. 対象Sessionが有効でReset中／要求中でない。現ID/Epochの完了を確認。
3. AppliedSettingsと各端末の設定が一致し、次回ドラフトとの未適用差がない（推奨方針）。
4. Participantが必要人数と**完全一致**。全員のRole・現Session ACK・Idle・必要データ初期化が確認済み。
5. Logger存在・有効・PC書込可、保存失敗なし・必要保存処理未完なし。準備時の保存先チェックは将来の書込成功を保証しないため、実験中の失敗通知も維持。
6. このLogger観測runで新規準備したID/Epochに対するtokenがあり、単調時計のorigin／計時継続性が確認できる。
7. Start要求が既にpending／開始済みでない。

UIは無効理由を1つの主要メッセージと詳細チェックリストで示す。mode/set/countの選択ボタンをParticipantへ表示しないだけでなく、設定要求API／準備APIでもRoleを確認する。既存通信が信頼するRoleを強化する認証基盤の全面導入は今回のUI実装範囲にしない。

### 10.2 Play再起動・復元Sessionの扱い

| 案 | 評価 |
|---|---|
| 新規Session準備を実験開始前に必須 | **推奨。** 明示操作・現run tokenで旧origin問題を避けやすい。準備の完了を復元activeと区別する必要がある |
| 既存Session警告のみ | 単調elapsedが空になる状態でもユーザーが開始できる。計時を主指標とする本実験には弱い。閲覧／開発用の限定運用なら可 |
| Logger runとSession整合でStart制御 | **上の推奨案と併用。** tokenだけでなく実際のorigin／復元状態も確認する。run IDをCSV上の研究IDとして扱わない |

Loggerの`Kind`／liveSessionはprivate、store.runIdとoriginsもprivateで、現在はStartへ公開していない。新規準備なしに古いCSVがあるSessionを開くとoriginがnullになる（CsvStore:121–125）。「Start markerがないから今から始めればよい」と判断するだけでは、同じSessionの準備ログが前runに保存されていた場合の問題を解消できない。

**自動Resetは禁止。** 進行中の復元試行は「継続中のセッションを復元／計時の継続性を確認できません」と表示し、既存の状態復元・回答回収・参加者の既存操作を無断で破棄しない。新しい実験のStartだけを制限し、結果確認／保存復旧／明示中断を提供する。中断確認後に既存Resetで次へ進む。既存のデータを上書きしてelapsedを後付け補完しない。

### 10.3 操作ごとの安全条件

| 操作ミス | 防止策 |
|---|---|
| 人数不足／人数超過Start | exact count＋各人ACK/Role/Idle、人数超過は任意の1人を勝手に除外しない |
| Reset中Start／二重準備 | managerのpending/IsResettingを再利用。command即時lock、確認時の旧ID/Epoch照合 |
| 現在の条件変更 | 編集はDraftのみ。Appliedは読取専用、開始後の既存guardを維持 |
| 保存失敗のReset | TryFlushBeforeResetのfail-closed維持。Loggerがない／無効でも拒否。UIがReset専用の抜け道を持たない |
| 過去Sessionで新試行 | 新規準備tokenとLogger run/origin確認。SessionIDがあるだけでStart不可 |
| Set／人数の選択未反映 | 現在／次回欄の差分表示、Start前dirtyなら再準備を要求 |
| Participantの管理操作 | 管理Canvas非表示＋API Role guard。Scriptの直接呼出しでも拒否 |
| 結果消失 | Snapshot保存確認→既存Reset。保存済みCSVを削除・修正する管理ボタンは設けない |
| 復旧で状態破壊 | 保存再試行とネットワーク復旧を別操作にする。強制Reveal・代理Submit・代理Ready・途中Epoch巻戻しは禁止 |

## 11. 推奨する実装順序

各段階はレビュー可能な独立変更にする。実装は今回行っていない。

| 作業 | 優先度・方法／再利用 | 依存関係 | 主なリスク | テスト方法／実機必要性 |
|---|---|---|---|---|
| W1 UI所有元整理と読取状態API | P0。既存manager／Logger／RegistryからResetProgress・SaveStatusを公開。4個の表示用Canvasを対象限定で整理、管理画面1個を担保 | 最初。既存参照を調べてからcleanup | 間違ったScene削除、無関係なCanvas破損、購読重複 | Serialized scene inventory、同名複数／callback検査。Editor表示＋Questで管理UI非表示確認 |
| W2 現在Session表示・Reset状態 | P0/P1。既存Control/完了イベント・ACKを読む日本語status view。％なし | W1 | OnSessionStartedを課題開始と誤解、動的ACK分母誤認 | 遅延echo・ACK・Participant/owner切断・timeoutをofflineで。Quest1台＋PCで可、2台で全員確認 |
| W3 設定ドラフトと適用Snapshot | P1。mode/set/countをローカルDraftとControlのAppliedへ分離。既存Resetの適用段階を利用 | W1。UI選択前にAPI仕様を確定 | 旧ビルド非互換、Mode同期競合、Countの現在値を誤更新 | 旧Control復元、新旧mode・A/B・1/2の全遷移、未適用差分。Quest1台＋offline2名、実機2台 |
| W4 新規準備・Start安全性 | P0/P1。既存StartBlockReasonにprepared token・Logger origin・全員ACK・設定一致を追加 | W2/W3とLogger read API | 再起動後に常に開始不能／逆に旧Session開始、継続試行の誤Reset | Player/Editor再起動・Logger再作成・復元と新規の境界を検証。1台で起動跨ぎ、2台でlate join |
| W5 PC設定選択UI | P1。方式・A/B・1/2の選択、dirty表示。「準備する」でのみW3へ送る | W3/W4 | UIだけ更新され実値と不一致、選択中にpeerが変化 | API拒否・ボタン連打・確認Cancel・現在値不変。PC+1台とoffline2名 |
| W6 保存状態・保存先open | P1/P2。既存queueとLastError、OutputDirectoryをread-only表示。検証付きDrain再試行入口 | W1/W4 | pollingでSnapshot再保存を繰り返す、任意パス／OS依存 | I/O不能→復旧、missingLogger、必要CSV欠損、フォルダopen失敗。保存はPC中心、実機通信の観測も確認 |
| W7 中断・復旧導線 | P1。進行中は確認→既存Reset/SESSION_ABORTED。エラー段階ごとの案内 | W2/W4/W6 | 保存エラーを無視する強制進行、pending中の新GUID競合 | 未確定／公開後／Confirmedからの中断、保存失敗、初期化失敗。2台復旧は実機必須 |
| W8 実験者日本語化 | P2。共通表示辞書、管理画面・Reset/Lobby/Resultの文言・font更新 | W1/W2と用語確定 | 生の例外が残る、短いbuttonで文字切れ | 全文字font coverage、長いSession/パス/エラー、PC複数解像度。PC表示実査 |
| W9 Participant日本語化 | P2。提出・公開・最終同意・終了／待機、debug情報整理 | W8の用語／font | 提出が最終確定に見える、研究説明を誘導、サイズ縮小 | UI状態別文字列・glyph・Ray/Direct・1〜1.5m。Quest1台で可、2台同時公開 |
| W10 接続詳細の最小整備 | P2/P3。Room/IP/接続・同期状態を読取、必要なら接続前だけ編集 | W4。運用形態の決定後 | RuntimeでEditorサーバー機能を呼ぶ、進行中Room変更 | 未接続／再接続／異なるRoom／Player role。実LAN・Quest2台は最終確認 |
| W11 自動回帰・本番手順 | 各段階で実施。既存Verificationを拡張し、専用rootでCSV保護検証 | W1以降継続、最終全段階 | 古いテストの固定文言／5イベント仮定、テストが本番ログを使用 | §13。旧正常フローを保ち、新条件の拒否も検証。最後に実Quest2台連続試行 |

サーバー起動・停止UIは既存Editor専用処理と別要件になる。まずは試験当日の事前起動を運用とし、Runtimeサーバー管理サービスの新設を今回の実装の必須条件にしない。

## 12. 変更予定ファイル一覧

| ファイル（候補） | 予定する責務／対応作業 |
|---|---|
| `Assets/Scripts/ExperimentSessionManager.cs` | 既存Resetのread-only進捗、AppliedSettings、要求対象ID、完了token、限定復旧状態。W1–W4/W7 |
| `Assets/Scripts/ExperimentParticipantRegistry.cs` | 現Session expectedCountと次回ドラフト分離、登録済み接続一覧の読取。Role guard維持。W1/W3 |
| `Assets/Scripts/PilotCardSetController.cs` | Next選択の検証付きAPI、適用済/次回の読み取り。W3/W5 |
| `Assets/Scripts/ExperimentManager.cs` | 新準備のMode適用と既存RoomModeの整合、表示用enumの分離。既存実験中変更禁止を保持。W3 |
| `Assets/Scripts/ExperimentLobbyController.cs` | CanStartの単一安全判定、旧Session／設定／ログ／ACK／token gate。W4 |
| `Assets/Scripts/ExperimentLogger.cs` | SaveStatus、保存再試行入口、計時継続性／現在run／必要Snapshotのread-only情報。W1/W4/W6 |
| `Assets/Scripts/ExperimentCsvLogStore.cs` | 必要最小限のrun/origin状態公開のみを想定。CSVヘッダー変更／過去CSV補正は不要。W4 |
| `Assets/Scripts/SessionResetUI.cs` | 単一管理画面からの確認・要求・statusへ統合。重複Build防止・所有権明確化。W1/W2/W7/W8 |
| 新規 `ExperimenterControlPanel.cs` 等（名称仮） | PC向けView／command接続。Reset/ログの業務ロジックはmanagerに残す。W2/W5/W6 |
| 新規 `ExperimentSessionSettings.cs`／`ExperimentUIText.cs` 等（名称仮） | 小さな設定DTO／ドラフト、表示辞書・enum/理由コード変換。W3/W8。大きな独立フレームワークにしない |
| `ExperimentLobbyUI.cs`／`ExperimentResultUI.cs` | 管理側の重複ボタンを共通commandへ、Participant向け説明・待機維持、日本語化。W2/W8/W9 |
| `IndependentAnswerSubmissionUI.cs`／`FinalAgreementUI.cs`／`ReadonlyParticipantBoardView.cs` | 日本語表示・サイズ・フォント、通信ID詳細をPCへ。W9 |
| `FinalAgreementManager.cs` | UIへ渡す失敗理由の分類を必要なら追加。合意判定・CSV／ネットワーク値は維持。W9 |
| `CandidateSyncTest.cs`／`CandidateCardView.cs`／`CandidateMarkerManager.cs` | 本番debug表示整理とPラベルの表示変換が必要な範囲だけ。W9 |
| `Assets/Scenes/QuestSetup.unity`／必要なら管理UI Prefab | 保存された4個の孤立Reset Canvasを対象特定して除去、単一管理UIの配置・参照更新。既存未コミット変更と競合しないよう差分確認。W1/W5 |
| `Assets/Fonts/IndependentAnswer/*SDF.asset`等 | 日本語必要文字の追加、管理UIのfont明示設定。W8/W9 |
| `Assets/Editor/ExperimentLobbySetup.cs`／`ExperimentResultSetup.cs`／`IndependentAnswerSubmissionSetup.cs`等 | 再生成で旧英語や旧fontが復活しないよう生成定義を同期。全面再実行は不要 |
| 既存Verification群＋新規管理UI検証 | 新API・日本語・1/2名・設定・run・ACK・保存を回帰検証。W11 |

`SessionVariableTransport`の既存キーやCSV・カードIDをリネームする計画はない。packageのNetSync本体／サーバーSupervisorをUI化だけのために改変する計画もない。接続前設定やserver lifecycleを拡張するかは別判断。

## 13. テスト計画

### 今回実施したこと／実施していないこと

実施：ソース・Setup・Scene・Prefab参照の読み取り、SceneのGameObject／script GUID対応の静的抽出、135個のTMP text componentの抽出、4個のResetルート／空の永続onClick確認、fontのUnicode集合照合、設定値とコード上の適用順序追跡。これらは**静的検査**である。

未実施：Unityでのクリック・画面／フォント描画、Play状態照会・開始／停止、Session Reset、ネットワーク通信、既存Verification再実行、Quest1台／2台実機。保存済み`docs/experiment-log-verification.json`には既存の回答366・store18・Logger29・Result78・Pilot427・Pose19の結果があるが、これは過去の検証結果であり、今回のUI設計が実装・検証された証拠にしない。

### 実装後の検証マトリクス

| 検証対象 | 自動／Editor・offline | Quest1台＋PC | Quest2台＋PC |
|---|---|---|---|
| UI所有元・Role | Sceneに孤立Resetルートなし、実行時管理Canvas1個、Participant管理API拒否、hidden管理情報のraycast非干渉 | 管理UIがQuestへ出ない、PC mouse有効 | 両Questとも非表示 |
| 設定Current/Next | Normal/Proposed×A/B×人数1/2、実験中Draft変更で現在不変、Resetに原子的適用、旧Control復元拒否 | 1名で両方式・A→BをInspectorなしで反復 | 2名で適用値完全一致、Mode同期と同時Start |
| ACKとStart | request受付≠echo≠local reset≠all ACK≠人数準備。重複クリック、late join、切断で分母変更、各人ACK前Start拒否 | ACK表示の進行、1/2なら開始不可 | 1人だけACKを遅らせる／切断／再接続、全員完了後のみ開始 |
| 1→2／2→1 | 全遷移。余剰Participantを暗黙除外しない。live trial Count不変 | 2名設定の不足表示（1台では開始しない） | 2→1の余剰表示、明示切断後のみ1名Start、次に2名へ |
| run／復元 | store origin null、old active/prepared Control、retain Start/Confirmed、Logger再作成、token不一致でStart拒否。自動Resetが0回 | Play/アプリ再起動、明示新準備後elapsedあり。進行中復元は勝手にリセットされない | 一方だけ再接続した際の同意／回答保護と本番中断手順 |
| CSV保護・保存UI | queue/必要Final/Initial不足、Logger欠落/無効、I/O失敗→再試行、未確定中断、旧CSVバイト一致。status pollingで再保存しない | 実PC保存先・open・コピー、8件／1人初期8件 | Proposed初期16件＋最終8件、全phaseと時刻差、2名対応表 |
| 状態表示 | Normal active Idleを共同分類、Proposed pre-Revealを独立回答、確定と保存完了を区別、rawError詳細のみ | 通常／準備中／Result／エラー画面、PC複数解像度 | 各Participantの提出・同意・切断を正しく区別 |
| 日本語・操作性 | 確定文字列HasCharacters、全状態で文字切れ／改行、Setup再生成でも文字復活しない | Quest1〜1.5m、Ray/Directで提出/取消/同意、長文エラーはPCだけ | 同時公開・終了待機の両端末可読性、誘導文言がない |
| 回帰 | ExperimentLobby/Result/Logger、AnswerLogging/Store、Pilot/Pose等のVerificationを新gateへ適合して実行 | Normal→Proposed→Normalの連続動作 | 実LANで複数試行、遅延／切断とCSV回収 |

offlineで2名の構造・同期順を検証できても、2台Questの通信・Ray・視認性の最終確認を成功扱いしない。テスト出力は専用のGUID付きroot、本番のpersistentDataPathログへ障害を起こさない。

## 14. 実装前に決定が必要な事項

| 決める事項 | 推奨する初期方針 | 理由／代替 |
|---|---|---|
| PCの実行形態 | 当面Editor実験者。Standaloneも使うならRole設定とserver起動運用を明示 | Auto RoleがPlayerではParticipant。Editor ToolsのRuntime移植は別範囲 |
| 旧Sessionの開始許可 | 本実験では新規準備を必須、Logger run／origin整合でgate | 警告だけで継続Startするのは開発モード限定候補。実験中の自動Resetはしない |
| 準備後に次回設定を変えた場合 | 現在は変更しない。Start前dirtyなら再準備必須 | 「現在設定で開始を選べる」案も可能だが操作誤認が増える。まず簡潔な安全側 |
| 次回Countの範囲 | 通常UIは1（動作確認）／2（評価実験） | 内部可変件数は維持。3以上や開発カードは詳細へ隔離 |
| 切断・人数差し替えの運用 | 本実験の途中差し替えは中断→明示新規準備 | 人数が同じでも同じ人とは限らない。厳密cohort固定／継続再開は追加設計と検証が必要 |
| 準備の表示完了条件 | all live ACKの内部完了と、正確な人数＋条件＋Logger＋tokenの準備完了を分ける | ACK分母を必要人数と誤読させない。必要人数不足なら状態は「同期完了・参加者待ち」 |
| 中断ボタンの意味 | 「実験中断して次の待機Sessionを準備」の確認→既存Reset | 現状にPause/Stopだけの独立状態はない。新しい実験仕様を暗黙導入しない |
| 自動復旧の範囲 | 保存再試行・既存再接続は可、公開／確定／強制Resetは不可 | localResetFailedの限定復旧を追加するなら専用の失敗条件・再初期化保証を先に決める |
| 表示用の日本語・フォント | §9用語＋用途別Static SDF更新、UI辞書を小規模導入 | 複数言語が必要になるまでは大きなLocalization導入不要 |
| ネットワーク設定UI | 主画面は読取、必要なら未接続時だけ詳細設定 | server起動・停止は試験前の運用。Room変更をSession準備の代わりにしない |
| 管理画面の配置／古いCanvas整理 | PC専用1個、既存World Space Participant UI継承、孤立4個を対象特定して除去 | Scene未コミット変更の内容を実装時に再確認。今回何も削除していない |

推奨する最初の実装範囲は **W1〜W6と基本日本語表示**。Participant操作の日本語化・復旧導線・実機2台回帰をその後に続ける。サーバー管理、Pair ID入力、アンケート、実験自動進行、詳細操作収集は付随追加しない。最終的な目標は、実験者が「次回設定→準備→状態確認→開始→結果と保存確認→次回準備」を同じPC画面で行え、既存のReset／ログ保護／Role制御を維持できることである。
