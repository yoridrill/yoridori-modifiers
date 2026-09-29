# YM Outline Extender

Main TextureのCutoutによって生じる内部境界へ線を追加する、ビルド専用のModifierです。通常のlilToon Outlineは保持します。アバター内に `Yoridori Modifiers/YM Outline Extender` を追加し、対象Materialをチェックしてください。

## 選択

未登録Materialだけを自動判定します。ON/OFFを手動変更した後は一覧更新・Inspector再表示・ビルドで上書きしません。同一Materialは一度だけ表示します。アバターから外れたMaterialの選択履歴は保持し、一覧からは隠します。

- lilToon: lilToon公式Helperで通常Cutoutバリエーションを判定。Multiは `_TransparentMode == 1`。いずれも `_OutlineWidth > 0`。
- MToon10: 既存Converterの `RenderTypeResolver` / `MToonToLilToonMapper.HasOutline` を利用。`_AlphaMode == 1`、Outline Mode有効、`_OutlineWidth > 0`（MToon10のoutlineWidthFactorに対応）。
- 旧MToon: 同じConverterの `_BlendMode` / Alpha Keyword / Outline Mode判定を利用。

MToonは別途YM MToon to lilToonで変換する必要があります。手動ONは可能ですが、ビルド時に最終Materialを再検証します。

## ビルド順・非破壊性

Resolvingで未登録Materialの初期選択を確定します。Transformingでは以下の依存関係を明示しています。

`Mesh Trimmer → TexTransTool Transforming → MToon to lilToon → Hair Look Kit → Outline Extender → TexTransTool Optimizing → YM Texture Finalizer → AAO / Quest Tools`

Hair Look Kit全体の後で最終RendererのMaterial・Main Textureを読みます。UVはそのまま使用し、Meshには触れません。Material追跡はNDMF ObjectRegistryを使用します。Hair Look Kitの多対一結合についてだけ、Coreの弱参照テーブルに全入力の出自を記録し、後続の派生Materialにも継承します。結合元に選択ON/OFFが混在する場合は、意図しない面へ適用しないよう結合Material全体を警告付きでスキップします。

Mesh TrimmerのFill Color合成は `a = src.a`、SolidifyはRGBのみを変更しています。本機能のための変更はありません。塗り足し・Atlas作成後のRGBを読み、元Texture・Material・Mesh・UVは変更しません。生成物はNDMFのAssetSaverへ渡す派生Assetで、画像ファイルの自動保存やAssetDatabaseへの独自書き出しは行いません。

## SDF

元Materialの実効境界 `MainTex.a = _Cutoff / _Color.a` から、Marching Squaresでサブテクセル位置の等値線を抽出します。鞍点はasymptotic deciderで接続を選びます。各線分の近傍だけを走査し、テクセル中心から等値線への最短ユークリッド距離を求めます。全画素から全体のBVHを繰り返し探索する処理はありません。Alphaの濃さ・AA幅を距離として使いません。Repeatは隣接周期領域も探索し、Clampは端のAlphaを延長します。

エンコードは `A = 0.5 + signedDistanceInBaseTexels / 16`（不透明側が正）。±8基底テクセルの範囲で距離を保持し、それ以遠は飽和します。この範囲は保存形式の上限であり、線幅の指定ではありません。元TextureのAlphaが実効Cutoffと交差する位置をSDFの距離0として生成TextureのAlpha 0.5へ写し、派生Materialだけを `_Cutoff = 0.5`、`_Color.a = 1` にします。元Textureと元Materialの値は変更しません。Mipごとに元MipのRGBとAlphaからSDFを作り、距離の単位を基底レベルに統一します。極小・サブテクセルの模様やMip間の形状変化は完全には再現できません。

生成直後は無圧縮の中間Textureとして後続処理へ渡します。OptimizingのYM Texture Finalizerが、TTTの最適化後にもAvatarから参照されているTextureだけをPCではBC7、AndroidではSDF向けのASTC 4x4で一度圧縮します。RGBは直前の処理結果を入力に使用し、元の色空間を維持します。読み取り不可Textureも、Editor専用Shaderによる明示LODのGPU読み出しを使用し、ImporterのRead/Write設定は変更しません。

TTTは初期化時にShader Assetを走査し、Shader名の `lilToon` / `Outline` から標準の完全なlilToon Texture Usage Informationを自動登録します。本Shaderはその命名条件を満たすため、TTT向けの部分定義を別途登録しません。AAOがインストールされている場合は、AAO自身のバージョン対応済みlilToon Shader Informationへ委譲し、Main・Outline・Emission・MatCapなど全TextureのUV、Scale / Offset、頂点利用情報をCustom Shaderへ引き継ぎます。Main Textureだけの部分定義で既存情報を上書きしません。AAO連携AssemblyはVersion Defineで分離され、TTT・AAOとも必須依存ではありません。

SDF生成前に、最終Rendererの各Materialスロットが実際に使用するMesh UV0三角形をTexture空間へラスタライズします。その使用領域内に存在するAlpha等値線だけを距離源とするため、メッシュ外にある別のAlpha境界が透明なシルエット付近へ線を発生させません。キャッシュはビルド単位の `(最終Texture参照, 実効Cutoff, 最終UV使用領域)` です。色や線幅だけが違い、UV使用領域も同じ場合はTextureを共有します。GPU上のTexture容量は2048²・全Mipで約5.3 MiB、4096²で約21.3 MiBです。Texture名・解像度・Mip・処理段階を進捗バーに表示し、SDF生成中はキャンセルできます。

## Shaderと幅

`Hidden/yoridrill/lilToonOutlineExtender/CutoutOutline` は [lilToon Custom Shader機構](https://lilxyzw.github.io/lilToon/ja_JP/dev/custom_shader_format.html) の `.lilcontainer` と小さなHookのみで構成します。末尾をlilToonのバリエーション命名規則に合わせ、Material InspectorがCutout・Outlineを正しく判定できるようにしています。Outline Extender実行時点でOutlineが無効、またはOutline Widthが0の最終Materialは、手動選択されていても警告してスキップします。lilToon本体の変更・コピー・フォークはありません。

既存Main Texture SampleのAlphaを利用し、追加のTexture Sampleはありません。SDFの `ddx` / `ddy` から画面上の距離勾配を直接求め、SDF距離をその勾配長で割ってTexture解像度・Tiling・UV密度・遠近による圧縮を補正します。UV Jacobianの逆変換は使用しないため、面が真横に近づいた場合も特異行列による方向・太さの発散は起こりません。画面距離の `fwidth` をAA幅として本体・線外周をそれぞれ `smoothstep` し、その差だけを線として合成します。三角形・UV境界をまたぐ非局所的な微分で離れた場所へAAが漏れないようAA幅を1px相当に制限し、保存範囲端まで飽和したSDFからは線を生成しません。さらに面が真横へ近づくほど、±8テクセルの保存範囲のうち線に使用できる領域を面の正対度の二乗に応じて境界から最小1テクセルまで狭めます。真横では太さの完全な維持よりトゲの抑制を優先し、線が細くなることを許容します。ゼロ境界は常に残します。目標幅はlilToonと同じ `_OutlineWidth * 0.01` および `_OutlineFixWidth` のカメラ距離補正から求め、オブジェクトScaleとProjectionで画面幅へ変換します。独立した固定ピクセル幅設定はありません。

境界の透明側だけCutoutを通過させて線を描き、元の不透明部分は覆いません。`_OutlineColor` と `_OutlineEnableLighting` を利用してFog・Distance Fade前に合成します。Inspectorの「太さ比率」で、最終lilToon Outline Widthを基準にCutout境界線だけを0～2倍へ調整できます。既定値1は通常Outlineと同じ基準です。±8テクセルを超える非常に太い線は保存範囲に制限されます。通常の輪郭押し出しと完全同一ではなく、非一様Scale・極端な斜視・Outlineのテクスチャ色やLit設定では見た目に差が出ます。`_OutlineWidthMask`・頂点色による幅補正は内部境界には反映しません。通常のlilToon Outlineパスに元々あるWidth Maskサンプルは維持します。

## 初版の対象範囲

標準フルlilToon Cutout / Multi Cutoutを対象とします。Lite / Fur / Tessellation / Optional / 他製品のCustom Shaderは安全にスキップします。

以下も警告付きでスキップします。

- Alpha Mask、Main 2nd/3rdのAlpha変更、Dissolve、Dither。
- Parallax / POM、UDIM Discard、AudioLinkによるレイヤー変更。
- Cutoffが0以下、Main Color Alpha以上、Main Color Alphaが0以下。
- Texture2D以外、Point Filter、Mirror / MirrorOnce。
- AnimatorでMaterial設定・Main Texture・AlphaやMaterial差し替えを変更するRenderer（初版はMaterialプロパティアニメーション全体を保守的に除外）。

対応外構成はInspectorで事前表示し、ビルド時にも最終結果を検証します。ビルド中の警告はUnity Consoleに表示します。Shader未検出・コンパイルエラー時も元の最終Materialを維持します。

## Inspector

Hair Look Kitと同じPreviewボタン・状態表示、右端の言語切替、見出し・区切り線・余白、折りたたみMaterial一覧、16pxチェックボックスとObjectField、Advancedを使用します。対応外Materialは行ごとに警告を並べず、機能単位で理由をまとめて表示します。変換予定のMToonには不要な警告を出しません。

Previewは元アバターを複製し、必要な場合だけ `YM MToon to lilToon → YM Outline Extender` の順で適用します。Hair Look Kitは実行しません。元Textureと同じ解像度を維持し、設定変更によるPreview再構築では元Textureの内容とCutoffが同じSDFを再利用します。Previewを終了するとキャッシュを破棄します。元RendererはPreview中だけ非表示にし、Preview終了時に復元します。

## 検証結果

Unity 2022.3.22f1 / macOSのEditorで、SDF・Inspector・Preview・Registry・Finalizerを含む43件のEditMode Test通過を確認しています。2048px / 4096pxの完了時間上限、画面微分AA、89.5度の側面表示、UV三角形境界での飽和SDFノイズ抑止、最終UV領域外のAlpha境界除外、太さ比率、Mesh TrimmerのAlpha維持、SDF後のRGB維持、参照中Textureだけの一回圧縮、派生関係、PC / Androidの実圧縮形式、Normal Mapの遅延パッキングも検証対象です。

`midori-lemon` の元解像度PreviewではUV使用領域を含む初回9.45秒、設定変更による同一入力の再構築はSDFキャッシュにより0.12秒でした。初回は高解像度SDF生成時間が支配的です。SDFの輪郭抽出・距離ラスタライズ・符号化をCPU並列化し、直近の合成ベンチマークでは2048pxを0.94秒、4096pxを3.92秒で完了しています。

実アバターでも `Assets/lemon/New Scene.unity` の `midori-lemon` を一時複製し、NDMFの `AvatarProcessor.ManualProcessAvatar`（永続AssetSaverを使用する通常ビルド経路）を実行しました。11対象Materialの処理、共通Finalizer、AAOまで **23.44秒** で完了し、Outline Extenderから最終的に参照されるMain Texture 10個すべてでBC7形式とAsset保存を確認しています。Outline Extenderなし／ありの差分Bakeでは、意図したMain Texture・Cutoff・YM設定以外のMaterial状態差分はなく、表面内部78,681画素の描画差分は0でした。元Material・Texture・MeshのJSONと永続Asset依存HashもBake前後で一致し、元Sceneは保存・変更していません。

## 実装ファイル

追加ファイル（各ファイル・ディレクトリのUnity `.meta` を含む）:

- `Runtime/YMOutlineExtenderComponent.cs`、`Runtime/YoridoriModifiers.OutlineExtender.Runtime.asmdef`
- `Editor/OutlineMaterialUtility.cs`、`Editor/YMOutlineExtenderComponentEditor.cs`
- `Editor/YMOutlineExtenderNdmfPlugin.cs`、`Editor/YoridoriModifiers.OutlineExtender.Editor.asmdef`
- `Editor/OutlineSdfGenerator.cs`、`Editor/OutlineSdfCache.cs`、`Editor/ReadMainMip.shader`
- `Shaders/OutlineExtender.lilcontainer`、`Shaders/OutlineExtender.lilblock`
- この `README.md`
- `../Core/Editor/YMTextureRegistry.cs`、`../Core/Editor/YMTextureFinalizerNdmfPlugin.cs`
- `Editor/Integrations/AAO`: AAOの完全なlilToon Shader Informationを再利用するOptional連携。TTTはShader名による標準lilToon判定を使用します。

既存ファイルの変更:

- `Core/Editor/NdmfObjectRegistry.cs`: 結合元の出自を保持する補助情報。
- `YMHairLookKit/Editor/HairMaterialMerger.cs`: 結合入力の出自登録1行。
- `YMMToonToLilToon/Runtime/MToonToLilToonConversion.cs`: 既存 `HasOutline` の公開化のみ。判定内容は変更なし。
- パッケージ直下の `README.md`: 機能・追加メニュー・処理位置の案内。
