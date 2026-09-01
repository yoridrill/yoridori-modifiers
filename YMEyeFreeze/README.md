# YM Eye Freeze

撮影時に VRChat の Eye Look による目線のキョロキョロを抑え、目ボーンを初期状態へ戻す NDMF ツールです。
カメラ目線化やターゲット追従を行う機能ではありません。

## 使い方

1. AvatarRoot に `Yoridori Modifiers/YM Eye Freeze` を追加します。
2. アップロード後、Expression Menu の `Eye Freeze` を ON/OFF します。

ON の間は VRChat Rotation Constraint が有効になり、左右の目ボーンが Head 配下の固定ターゲットの回転へ戻ります。
OFF にすると Constraint が無効になり、通常の Eye Look に戻ります。

## 設定

- `Menu Name`
  - Expression Menu に表示する名前です。初期値は `Eye Freeze` です。
- `Advanced/Parameter Name`
  - 内部 Expression Parameter 名です。初期値は `YM/EyeFreeze` です。
- `Advanced/Saved`
  - Expression Parameter を保存するかを指定します。初期値は ON です。
- `Advanced/Synced`
  - Expression Parameter を同期するかを指定します。初期値は ON です。

## NDMFツールとの併用

- NDMF Parameter Providerを通じて、Modular Avatarなどがビルド時に追加するExpression Parametersを含む推定容量を確認し、上限を超える場合だけInspectorへ警告を表示します。
- Parameterのリマップが設定されている場合は、NDMFから解決したビルド時の名前をAnimator・Expression Parameters・メニューで共通して使用します。
- ルートメニューが8枠を超える場合は、`More`サブメニューを作成してEye Freezeを追加します。
- 同名ParameterがBool以外の場合は、型が不一致のままビルドせずNDMF Build Reportへエラーを表示します。

## 制限

- カメラ目線化ではありません。
- まばたきの停止は行いません。
- Eye Look 未設定のアバターは対象外です。
- `VRCAvatarDescriptor` の Eye Look に leftEye / rightEye が設定された、ボーン式 Eye Look のみ対象です。
- 手動目ボーン指定、BlendShape 目線、UV 目線には対応していません。

## ライセンス

このプロジェクトは MIT License で提供されています。詳細は [../LICENSE](../LICENSE) を参照してください。
