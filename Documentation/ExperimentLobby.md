# CoPlace Experiment Lobby

QuestSetup内にWorld Spaceの開始前画面を追加。別Scene・追加パッケージは使用しない。

## ファイル

新規: `Assets/Scripts/ExperimentLobbyController.cs`、`ExperimentLobbyUI.cs`、`Assets/Editor/ExperimentLobbySetup.cs`、`ExperimentLobbyVerification.cs`、`Assets/Fonts/IndependentAnswer/LobbyJapanese SDF.asset`（各metaを含む）。日本語フォントは既存NotoSansJPを再利用。

変更: `QuestSetup.unity`、`ExperimentManager`、`ExperimentSessionManager`、`ExperimentModeUI`、`CandidateSyncTest`、`NormalPlacementSync`、`PrivateTierBoardController`、`ProposedRevealCoordinator`、`ProposedSharedPlacementController`、`ReadonlyParticipantBoardView`。既存検証の`PrivateTierBoardVerification`と`ProposedRevealVerification`は、開始後の既存フローを単独で検証するためLobby参照を解除。新しい統合検証はLobbyを含めて両方式を実行する。

## Hierarchy / 配置

```text
ExperimentLobby [Controller, UI]
└─ LobbyContent [World Space Canvas, 既存XR Raycaster]
   ├─ ParticipantIntroduction
   │  ├─ Title / Subtitle / Description
   │  ├─ CurrentCondition
   │  ├─ Participants / Connection
   │  └─ Waiting
   └─ ExperimenterControls
      ├─ NormalButton / ProposedButton
      ├─ NewSessionButton / SessionInformation
      └─ StartExperimentButton
```

位置は`(-0.07, 1.65, -1.5)`、Canvas Scaleは`0.001`。初期カメラの約2m前方に配置。RootのTransformで位置・大きさを調整できる。Participantパネルは幅1.48m、Experimenter操作は右側。角丸ダークパネル、細い境界線、teal、文字の階層を使用。[MetaのPanelガイド](https://developers.meta.com/vr/design/panels/)と[Typographyガイド](https://developers.meta.com/vr/design/styles_typography/)を参考にした。

Participantには共通説明・方式説明・人数・接続・待機だけを表示。ExperimenterControlsを非表示にし、公開Start/Mode/New SessionコールバックもRoleで拒否する。既存のMode管理CanvasもParticipantには表示しない。

## 開始・同期・Reset

`StartExperiment()`はExperimenter、接続済み、Reset完了、有効なSession ID/開始時刻、予定人数と登録Participant数の一致、全ParticipantのIdle、Mode同期済みを確認する。Experimenterは人数に含まない。New Sessionは既存`StartNewSession()`を呼び、Startで自動Resetしない。

Startは既存`SessionVariableTransport`でGlobal Variable `experimentLobbyStart`にSession ID / Epoch / Mode / nonceを送る。共有値の受信後に各端末がLobbyを閉じる（ネットワーク伝播分の遅延はある）。`ExperimentManager.CanRunExperiment`から既存のBoard表示・入力・Reveal・共有配置・Final Agreement有効条件を制御する。NetSync/Resetのデータ処理自体は停止しない。

Mode説明は`ExperimentManager.CurrentMode`から毎回更新。人数は`ExperimentParticipantRegistry.ParticipantClientNos`と`ExpectedParticipantCount`、接続は実NetSyncの`IsReady`から表示する。色に加え● Connected / ○ Waitingの文字を併記。

Reset開始でローカルStartを解除し、既存ResetのGlobal defaultsでもStart値を消去する。古いEpochのStartは拒否。同一Sessionの再接続ではGlobal Startを受信して既存の実験へ復帰する。Final Ready/Submit状態とは独立している。

## 検証

Unity内の実NetSync OfflineConnectionManagerを使い、2 Participant + 1 Experimenterの統合検証36項目が成功。Lobbyの表示分離、人数不足、管理操作拒否、Mode説明、日本語字形、Start、Normal共有配置/Final Agreement、Proposed Private→Submit→ACK Reveal→共有配置/Final Agreement、Reset、再接続、古いStart拒否を確認。

既存検証159項目も成功（Private25、Reveal75、共有配置45、Submit14）。Android定義・実パッケージ参照によるC#コンパイルも成功。実QuestのXRポインター/Grabとオンライン通信は、この変更では未確認。新しいAPKはまだ作成・インストールしていない。

[Participant画面](LobbyParticipant.png) / [Experimenter画面](LobbyExperimenter.png)

## Editor + Quest確認手順

1. 更新プロジェクトからQuest APKをビルド。EditorはExperimenter、QuestはParticipant。同じRoomに接続する。Editor+Quest各1台の構成では、ExperimenterのExpected Participant Countを1にする。この値は`experimentExpectedParticipantCount` Client VariableでParticipantへ同期され、人数表示と完了判定が同じ値を参照する。複数Experimenterの場合はClient Noが最小のExperimenterを参照。Experimenter不在時は各端末の既存設定を使用する。2 Participant実験ではExperimenterを別端末にして2 Participantを接続する。
2. 起動時にLobbyのみ表示され、Participantには管理ボタンがないことを確認。Main/Privateカードを操作できないことも確認する。
3. ExperimenterからNew Sessionを実行。Reset終了・人数一致後、Normal/Proposedを切り替え、両端末の説明が更新されることを確認。
4. Start Experimentで両端末のLobbyが閉じる。Normalでは共有配置/Ready、ProposedではPrivate回答/Submit→Reveal→共有配置/Readyの既存フローを確認。
5. New Sessionで両端末がLobbyへ戻り、前Sessionの回答/Reveal/Ready/Confirmedが残らないことを確認。
6. 同一Sessionへ再接続し、開始済みならLobby待機ではなく実験状態へ復帰することを確認。
