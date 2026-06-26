# YM MToon → lilToon Converter

MToon 1.0 / 互換 MToon マテリアルを lilToon へ変換するための Unity + NDMF 向けツールです。

## 概要

このツールは以下を目的としています。

- Build 時に NDMF フェーズで自動変換
- Editor 上での非破壊 Preview
- lilToon 固有機能の一括設定
- 顔影調整
- Advanced での詳細ログと Preview 復旧機能

## 前提

- Unity Editor
- NDMF
- lilToon

## 使い方

1. 対象アバター配下に `Yoridori Modifiers/YM MToon to lilToon` を追加
2. 必要に応じて lilToon 固有機能の一括設定や顔影調整を有効化
3. `Preview` で変換後を確認（元オブジェクトは非破壊）
4. Build 時は NDMF plugin が自動適用

髪マテリアル結合、眉ステンシル、FakeShadow、輪郭線補正は `YM Hair Look Kit` を使用してください。

## Render Queue

- Opaque: `Geometry`
- Cutout: `AlphaTest`
- Transparent: **必ず `2460` 開始**
- Transparent は **元の MToon transparent 同士の相対順序のみ** を保持し、`2460 + rank` で**連番に詰めて再採番**
- VRoid などで `4000` 等の不適切な queue が入っていても、その絶対値は信用せず、lilToon/VRChat アバター運用向けに再構成
- この方針は **VRChat アバターで Focus が外れてボケる問題を避けるための仕様** であり、Unity 一般慣例より優先

### Render Queue ポリシー（変更禁止レベル）

- 本ツールは「Unity の一般的な queue 運用」ではなく、**VRChat でアバターに lilToon を使う実運用**を優先する。
- そのため Transparent queue は 2500/3000 帯へ分割しない。**2460 帯で密に採番**する。
- 将来の変更でも、このルールを崩す場合は「VRChat での実機検証結果」と「既存アバターへの影響評価」を必須とする。

## Inspector UI

- 上部: `Preview` ボタン（有効中は緑） / Preview 進捗表示 / 言語切り替え（日本語・英語）
- `顔マテリアル` の選択（顔影調整の基準マテリアル）
- `lilToon固有機能の一括設定`
  - 影を受け取る
  - 影の境界
  - 逆光ライト
  - 距離フェード
  - 輪郭線の Z Bias
- `特定部位への調整`
  - `顔の影を整える`
    - マスクタイプ
    - マスク
    - LOD
- `Advanced` 折りたたみ
  - Verbose Log
  - Reset Preview（保存済み Preview 復旧）

## 注意

- lilToon は UV スクロールのマスクに対応していないため、メインカラー 2nd での疑似再現となります

## ライセンス

このプロジェクトは MIT License で提供されています。詳細は [../LICENSE](../LICENSE) を参照してください。
