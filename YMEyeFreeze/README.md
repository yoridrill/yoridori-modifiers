# YM Eye Freeze

撮影時に VRChat の Eye Look による目線のキョロキョロとまばたきを一時停止し、目ボーンを初期状態で固定する NDMF ツールです。  
カメラ目線化やターゲット追従を行う機能ではありません。

## 使い方

1. AvatarRoot に `Yoridori Modifiers/YM Eye Freeze` を追加します。
2. アップロード後、Expression Menu の `Eye Freeze` を ON/OFF します。

ON の間は Eyes & Eyelid が Animation になり、左右の目ボーンがビルド時の初期 localRotation で固定されます。  
OFF にすると Eyes & Eyelid は Tracking に戻り、通常の Eye Look に戻ります。

## 設定

- `Menu Name`
  - Expression Menu に表示する名前です。初期値は `Eye Freeze` です。
- `Advanced/Parameter Name`
  - 内部 Expression Parameter 名です。初期値は `YM/EyeFreeze` です。
- `Advanced/Saved`
  - Expression Parameter を保存するかを指定します。初期値は ON です。
- `Advanced/Synced`
  - Expression Parameter を同期するかを指定します。初期値は ON です。

## 制限

- カメラ目線化ではありません。
- Eye Look 未設定のアバターは対象外です。
- `VRCAvatarDescriptor` の Eye Look に leftEye / rightEye が設定された、ボーン式 Eye Look のみ対象です。
- 手動目ボーン指定、BlendShape 目線、UV 目線には対応していません。

## ライセンス

このプロジェクトは MIT License で提供されています。詳細は [../LICENSE](../LICENSE) を参照してください。
