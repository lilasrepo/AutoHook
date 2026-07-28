# AutoHook（繁中移植版 · TC13） / Traditional-Chinese Port

> 讓釣魚變得沒那麼無聊（或更無聊）。<br>
> Makes fishing less (or more) boring.

**繁體中文**：這是 **[AutoHook](https://github.com/PunishXIV/AutoHook)** 的繁體中文客戶端移植版，對應 **FFXIV 7.20 / yanmucorp Dalamud API13（.NET 9）**。本專案僅做相容性移植，**非官方、非原作維護**；所有原始功能與設計著作權歸原作者 **Det**。

**English**: A Traditional-Chinese-client port of **[AutoHook](https://github.com/PunishXIV/AutoHook)** targeting **FFXIV 7.20 / yanmucorp Dalamud API13 (.NET 9)**. Compatibility port only — **unofficial and not maintained by the original author**. All original work © **Det**.

---

## 這是什麼 / About

自動幫你提勾上鉤的魚。可依魚種、釣餌與擬餌設定不同的提勾策略，並支援自動換餌、精準採集等情境，長時間釣魚不必盯著螢幕。

Hooks fish for you. Configure per-bait/per-fish hooking strategies, with auto-recast, mooch and precision-gathering support so you can fish hands-free.

## 安裝 / Installation

**繁體中文**
1. 使用 **XIVTCLauncher** 啟動繁體中文客戶端。
2. 遊戲內輸入 `/xlsettings` → 切到 **Experimental** 分頁 → **Custom Plugin Repositories（自訂插件庫）**。
3. 貼上下列網址並按 **+** 儲存：
   ```
   https://raw.githubusercontent.com/lilasrepo/DalamudPlugins/main/pluginmaster.json
   ```
4. 輸入 `/xlplugins`，搜尋 **AutoHook (TC13)** → 安裝 → 啟用。

**English**
1. Launch the Traditional-Chinese client with **XIVTCLauncher**.
2. In-game, type `/xlsettings` → **Experimental** tab → **Custom Plugin Repositories**.
3. Add this URL and save with **+**:
   ```
   https://raw.githubusercontent.com/lilasrepo/DalamudPlugins/main/pluginmaster.json
   ```
4. Type `/xlplugins`, search **AutoHook (TC13)** → Install → Enable.

## 對應版本 / Compatibility

| 項目 / Item | 版本 / Version |
|---|---|
| 遊戲 / Game | FFXIV 7.20（繁中客戶端 / TC client） |
| Dalamud | yanmucorp API13（.NET 9） |
| 移植自上游 / Ported from upstream | v4.3.4.1 |

## 原作與授權 / Credits & License

本專案 fork 自 **[PunishXIV/AutoHook](https://github.com/PunishXIV/AutoHook)**，授權沿用上游；所有原始功能著作權歸 **Det**。<br>
Forked from **[PunishXIV/AutoHook](https://github.com/PunishXIV/AutoHook)**. License follows upstream; all original work © **Det**.

## 免責聲明 / Disclaimer

第三方插件，使用風險自負。**移植相關問題請回報到本 repo 的 Issues，請勿打擾上游原作者。**<br>
Third-party plugin — use at your own risk. **For port-specific issues please open an Issue here; do not contact the upstream author.**
