# Technics Control

Technics EAH-AZ80 を Windows PC から操作・確認するためのデスクトップアプリです（WinUI 3）。

## 主な機能

- **タスクトレイ常駐 & クイックパネル**: トレイアイコンの左クリックでバッテリー残量（左右・ケース）、接続コーデック、外音コントロール（NC / トランスペアレント / アテンション / OFF）と強さを素早く確認・切り替え
- **メインウィンドウ**:
  - **ホーム**: バッテリー残量、接続中コーデック、外音コントロールの状態表示
  - **外音コントロール**: モード切り替えと円形ダイヤルによる強さ（1〜100）の調整
  - **サウンドモード**: プリセット（Direct / Bass+ / Super Bass+ / Vocal / Treble+ / Dynamic / Custom）の切り替え
  - **設定**: マルチポイント台数、接続中の機器一覧、タッチセンサー割り当て、装着検知、オートパワーオフ、LED 点滅、接続モード / LDAC、NC 最適化、アンビエントモード設定、通話ノイズ抑制、本体色、製品情報（FW バージョン・使用時間・充電エラー）、テーマ、自動起動

## プロジェクト構成

- `TechnicsControl.slnx`
  - `src/TechnicsControl.App`: WinUI 3 デスクトップアプリ（`TechnicsControl.exe`）
  - `src/TechnicsControl.Core`: Airoha RACE プロトコル（RFCOMM）および Technics コマンド通信ライブラリ
  - `src/TechnicsControl.Cli`: 動作確認・ダンプ用 CLI（`technics.exe`）
  - `tests/TechnicsControl.Core.Tests`: ユニットテスト
  - `tools/AssetExtractor`: 公式 Android アプリ APK から画像・アイコンを抽出するツール
  - `docs/protocol.md`: RACE プロトコル仕様と実機解析メモ

## ビルドと実行

```powershell
# テスト実行
dotnet test TechnicsControl.slnx

# アプリのビルド・実行 (x64)
dotnet run --project src/TechnicsControl.App/TechnicsControl.App.csproj -p:Platform=x64
```
