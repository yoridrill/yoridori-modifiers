# Yoridori Modifiers

Yoridori Modifiers は、VRChat アバター向けの非破壊 NDMF ツール集です。  
Unity 2022.3 / VCC / ALCOM で作成した VRChat Avatars Project での利用を想定しています。

## 含まれるツール

- `YM Arm Patch`
  - 肩、前腕、親指の見た目を補正します。
  - 通常追加時はニュートラルな初期値です。
  - VRoid 向けには右クリックメニューのプリセットを使うと、衣装に合わせた設定で追加できます。
- `YM Mesh Trimmer`
  - 透明テクスチャのアルファを元に、不要なポリゴンをビルド時に削除します。
  - Android / iOS 向けの Preview では Toon Standard 相当の表示に切り替えて確認できます。
  - テクスチャの透明余白の塗り足しにも対応します。
- `YM MToon to lilToon`
  - MToon 1.0 / 互換 MToon マテリアルを lilToon へ変換します。
  - lilToon 固有機能の一括設定や顔影調整を必要に応じて有効化できます。
- `YM Hair Look Kit`
  - 髪マテリアルの結合、眉ステンシル、FakeShadow、輪郭線補正を非破壊で適用します。
  - `YM MToon to lilToon` と同じアバターにある場合は、変換後の lilToon マテリアルに対して処理できます。
- `YM Eye Freeze`
  - Exメニューに Eye Look と Blink を一時停止し、目ボーンを初期状態で固定するモードを追加します。
  - カメラ目線化やターゲット追従は行いません。
- `YM Facial Mapper`
  - テキストで指定した Shape Key をハンドサインに合わせて非破壊で適用します。
  - Eyelid-L / Eyelid-R / Viseme の排他と、まばたき・口パク停止に対応します。
- `YM VRoid Skirt Refine`
  - VRoid のワンピース、スカート、ロングコート裾まわりをビルド時に非破壊で整えるためのツールです。
  - ワンピースの先端側延長、ロングコートの根本側追加、PhysBone統合、Rotation Constraint、PhysBoneCollider、ウェイト再配分に対応しています。

## 導入

VCC / ALCOM で導入できます。  
https://yoridrill.github.io/vpm-repos/redirect.html

必要な主な依存関係は以下です。

- VRChat SDK Avatars
- NDMF
- lilToon

## 使い方

各コンポーネントは Add Component から追加できます。

- `Yoridori Modifiers/YM Arm Patch`
- `Yoridori Modifiers/YM Mesh Trimmer`
- `Yoridori Modifiers/YM MToon to lilToon`
- `Yoridori Modifiers/YM Hair Look Kit`
- `Yoridori Modifiers/YM Eye Freeze`
- `Yoridori Modifiers/YM Facial Mapper`
- `Yoridori Modifiers/YM VRoid Skirt Refine`

VRoid アバターでは、Hierarchy のアバターを右クリックして以下のメニューを使うと、各ツールをまとめた GameObject を追加できます。

- `Yoridori Modifiers/Create YM Components Object for VRoid/Long Sleeves`
- `Yoridori Modifiers/Create YM Components Object for VRoid/Short Sleeves`
- `Yoridori Modifiers/Create YM Components Object for VRoid/Kimono`
- `Yoridori Modifiers/Create YM Components Object for VRoid/(via VRM 0.0) Long Sleeves`
- `Yoridori Modifiers/Create YM Components Object for VRoid/(via VRM 0.0) Short Sleeves`
- `Yoridori Modifiers/Create YM Components Object for VRoid/(via VRM 0.0) Kimono`

個別に追加したい場合は、`Yoridori Modifiers/Add Component with VRoid Defaults` から選択できます。

`YM Mesh Trimmer` は Android/iOS 用と Windows 用の2つが追加され、Windows 用は広めのマスク設定になります。
`YM VRoid Skirt Refine` は `Yoridori Modifiers/Add Component with VRoid Defaults/YM VRoid Skirt Refine` から追加できます。Preset は Settings をまとめて変更するためのもので、ビルド時は Settings の値を参照して処理します。

## Preview

Preview 対応ツールの Inspector 上部には `Preview` ボタンがあります。  
Preview 中はシーン上の元モデルをできるだけ壊さないように一時オブジェクトで表示します。

Preview 表示が残ったり、モデルが見えなくなった場合は、各ツールの `Advanced` 内にある `Reset Preview` を押してください。  
Yoridori Modifiers のどの `Reset Preview` からでも、各ツールの Preview 復旧がまとめて実行されます。

同一アバター上では同時 Preview を制限する場合があります。  
特に AnimationMode を使う `YM Arm Patch` は、シーン上で同時に1つだけ Preview できます。

## 詳細

各ツールの詳しい説明は以下を参照してください。

- [YM Arm Patch](./YMArmPatch/README.md)
- [YM Mesh Trimmer](./YMMeshTrimmer/README.md)
- [YM MToon to lilToon](./YMMToonToLilToon/README.md)
- [YM Hair Look Kit](./YMHairLookKit/README.md)
- [YM Eye Freeze](./YMEyeFreeze/README.md)
- [YM Facial Mapper](./YMFacialMapper/README.md)
- [YM VRoid Skirt Refine](./YMVRoidSkirtRefine/README.md)

ビルド時は、`YM Arm Patch`、`YM VRoid Skirt Refine`、`YM Mesh Trimmer`、`YM MToon to lilToon`、`YM Hair Look Kit`、`YM Eye Freeze`、`YM Facial Mapper` の順で処理します。

## ライセンス

MIT License です。詳細は [LICENSE](./LICENSE) を参照してください。
