# Snap Range Control

A Cities: Skylines II mod that limits how far **guide lines** of existing roads can snap the road tool.

[日本語は下にあります](#日本語)

> **Note:** The Japanese text in this mod (settings screen and this README) was translated with the help of AI.

## Status

Experimental. Not yet tested in-game. Please try it on a test save first.

- Supported game version: **1.6.4f1** (other versions are untested)

## What it does

In the vanilla road tool, guide lines extend up to **512 m** from the ends of existing roads, and the cursor snaps to any of them within 8 m. So a new road often snaps to guide lines of roads that are far away.

This mod adds a **Guide line reach** slider. When the snapped point lies on a guide line farther than this distance from the guide line's origin (the road end), that snap is ignored and the snap is recomputed without guide lines.

- The default (512 m) is the same as the unmodded game.
- **Known limitation:** when a distant guide line would win, guide lines are skipped entirely for that position. A nearby guide line may then not snap there either (other snapping such as existing roads and straight direction still works).
- Only affects roads placed with the road tool. Other networks (rails, pipes, power lines, waterways) are unchanged.
- The 8 m snapping width and all other snapping options are unchanged.

## Settings

Options → Snap Range Control

| Setting | Description |
|---|---|
| Enable | Turn the mod's effect on or off. |
| Guide line reach | 32–512 m in 32 m steps. 512 m = vanilla. |
| Reset to defaults | Restore vanilla behaviour. |
| Key bindings | Increase / decrease the reach by 32 m, and toggle the mod. No keys are assigned by default. Keys work only while the road tool is selected. |
| Debug logging | Write guide line snap distances to `Logs/SnapRangeControl.log`. |

### In-game controls

While the road tool is selected, the tool options panel shows a **Guide line reach** section:

- Guide line icon: turn the mod's effect on or off.
- Arrow buttons: decrease / increase the reach by 32 m. The current value is shown between them.

## Save games

The mod stores only its own settings file. It never writes to save games, so removing it does not affect your saves.

## Compatibility

- If a game update changes the patched code, the mod disables itself and writes an error to `Logs/SnapRangeControl.log`. The game keeps running unmodified.
- Network Tools uses its own copy of the snapping code, so this mod does not affect its tools.
- Mods that also change road snapping (e.g. CustomRoadSnap) may interact with this mod.

## How it works

`NetToolSystem.SnapJob` is Burst compiled and cannot be patched. The mod adds a Harmony prefix/postfix to the managed `NetToolSystem.SnapControlPoints` method that schedules it. After the job finishes, it checks whether the snapped point lies on a guide line beyond the configured reach, and if so, runs the snap once more with guide lines disabled.

## Building

Requires the official Cities: Skylines II Modding Toolchain (installed from the game's Options → Modding) and Node.js 18 or later for the UI module (`ui/`, based on the toolchain's UI template).

```
cd ui
npm install
cd ..
dotnet build -c Release
```

`dotnet build` also builds the UI module (`npm run build` in `ui/`). The build deploys the mod to `%USERPROFILE%\AppData\LocalLow\Colossal Order\Cities Skylines II\Mods\SnapRangeControl`.

## License

MIT

---

## 日本語

> **注意:** このModの日本語（設定画面とこのREADME）は、AIを使って翻訳しています。

道路ツールで、既存の道路の**ガイドライン**へのスナップが効く距離を制限する、Cities: Skylines II 向けのModです。

### 状態

試験版です。まだゲーム内での確認が済んでいません。テスト用のセーブで試してください。

- 対応ゲームバージョン: **1.6.4f1**（ほかのバージョンは未確認）

### できること

バニラの道路ツールでは、既存の道路の端からガイドラインが最大 **512 m** 伸び、カーソルが 8 m 以内に近づくとスナップします。そのため、離れた道路のガイドラインにスナップしてしまうことがあります。

このModは「ガイドラインの到達距離」のスライダーを追加します。スナップ先のガイドラインが、その始点（道路の端）からこの距離より遠い場合、そのスナップを無視し、ガイドラインを使わずにスナップし直します。

- 既定値（512 m）は、Modなしと同じ動きです。
- **既知の制限:** 遠いガイドラインにスナップしそうな位置では、ガイドライン自体を使わずにスナップし直します。そのため、その位置では近くのガイドラインにもスナップしないことがあります（既存の道路や直線方向へのスナップは効きます）。
- 道路ツールで置く道路にだけ効きます。線路・水道管・電線・水路などは変わりません。
- 8 m のスナップ幅や、ほかのスナップ設定は変わりません。

### 設定

オプション → Snap Range Control

| 設定 | 内容 |
|---|---|
| 有効にする | Modの効果のオン/オフ |
| ガイドラインの到達距離 | 32〜512 m（32 m 刻み）。512 m でバニラと同じ |
| キー割り当て | 到達距離の増減（32 m ずつ）と、Modのオン/オフ。既定ではキーを割り当てていません。道路ツールを選んでいるときだけ効きます |
| 既定値に戻す | バニラと同じ動きに戻す |
| デバッグログ | スナップ距離を `Logs/SnapRangeControl.log` に書き出す |

### ゲーム内の操作

道路ツールを選ぶと、ツールオプション欄に「ガイドラインの到達距離」が出ます。

- ガイドラインのアイコン: Modの効果のオン/オフ
- 矢印ボタン: 到達距離を 32 m ずつ増減。間に今の値を表示します

### セーブデータ

Modが保存するのは自分の設定ファイルだけで、セーブデータには書き込みません。Modを外してもセーブデータは影響を受けません。

### 互換性

- ゲームのアップデートでパッチ対象が変わった場合、Modは自分を無効にして `Logs/SnapRangeControl.log` にエラーを書きます。ゲームはModなしの状態で動き続けます。
- Network Tools は独自のスナップ処理を持つため、そのツールにはこのModは効きません。
- 道路のスナップを変える他のMod（CustomRoadSnap など）とは、干渉する可能性があります。

### ライセンス

MIT
