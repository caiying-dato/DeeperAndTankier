# 愈深愈肉（Deeper and Tankier）

杀戮尖塔 2（Slay the Spire 2）模组：**怪物血量随爬塔层数增加**——爬得越深，怪物越肉。

[![游戏版本](https://img.shields.io/badge/游戏-v0.111.0-blue)](#)
[![版本](https://img.shields.io/github/v/release/caiying-dato/DeeperAndTankier?label=%E7%89%88%E6%9C%AC&color=green)](../../releases)
[![前置](https://img.shields.io/badge/前置-CYLib%20v0.1.3%2B-orange)](#安装)
[![语言](https://img.shields.io/badge/语言-简体中文%20%7C%20English-green)](#)
[![许可](https://img.shields.io/badge/许可-MIT-yellow)](LICENSE)

开发源码、构建脚本与完整技术文档：[https://github.com/caiying-dato/DeeperAndTankier](https://github.com/caiying-dato/DeeperAndTankier)

- 模组 ID：`DeeperAndTankier`
- 版本：0.1.3
- 适用游戏版本：v0.111.0（`min_game_version: 0.111.0`）
- 依赖：CYLib v0.1.3+（配置平台，见 [CYLib 仓库](https://github.com/caiying-dato/CYLib)）
- `affects_gameplay: true`（影响玩法；联机时所有玩家需安装同版本模组）

---

## 安装

### 方式一：下载发布包（推荐）

到本仓库的 [**Releases**](../../releases) 页面下载最新的 `DeeperAndTankier-vX.Y.Z.zip`，
解压后把里面的 **`DeeperAndTankier` 文件夹**放进《杀戮尖塔 2》安装目录的 `mods\` 下：

1. 在 Steam 里**右键游戏 → 管理 → 浏览本地文件**，打开《杀戮尖塔 2》的安装目录。
2. 把压缩包里的 `DeeperAndTankier` 文件夹解压/拖进这个安装目录的 `mods\` 里。

   ```
   Slay the Spire 2\
     mods\
       DeeperAndTankier\
         DeeperAndTankier.dll
         DeeperAndTankier.json
   ```

3. **前置模组**：先装 [CYLib](https://github.com/caiying-dato/CYLib/releases)（`v0.1.3`+，配置平台）。
4. **完全重启游戏**，在模组列表里启用 **愈深愈肉（Deeper and Tankier）**，开新局生效。

> ⚠️ **那一层 `DeeperAndTankier\` 必须保留**：直接把包里的文件倒进 `mods\` 里，
> 模组管理器看不到它，而且哪里都不会报错。发布包**不含前置模组**。
>
> GitHub 上的包与局域网共享架上同名版本**内容完全一致**，只是文件名去掉了中文与时间戳
> （`愈深愈肉(DeeperAndTankier)-vX.Y.Z-<日期>-<时间>.zip` → `DeeperAndTankier-vX.Y.Z.zip`）。

### 方式二：Steam 创意工坊

订阅 [**愈深愈肉 —— Deeper and Tankier**（工坊 ID 3812015038）](https://steamcommunity.com/sharedfiles/filedetails/?id=3812015038)，
由 Steam 自动安装与更新。

---

## 一、公式

```
增幅   = 成长系数 × 难度 × 层数
血量倍率 = 1 + 增幅
```

- **成长系数**：默认 **0.001**，可在 CYLib「模组配置」里调整（千分比计，
  配置值 1 = 0.001，见下文「五、配置（CYLib）」）
- **难度** = 本局的进阶等级 A（`RunState.AscensionLevel`，A10 → 10）
- **层数** = 本局已爬的**总层数**（`RunState.TotalFloor`，跨幕累计，就是结算界面
  “爬塔层数 / floors climbed”那个数）

> **注意：层数 ≠ 幕数。**
> 幕数是 `CurrentActIndex`（第几幕），本模组不用它；
> 也不用幕内层数 `ActFloor`（每幕从 1 重新数）。
> 本模组用的是全局累计层数，越深越肉，跨幕持续增长。

### 示例（默认成长系数 0.001）

| 场景 | 计算 | 血量倍率 |
|---|---|---|
| A10，第 16 层 | 0.001 × 10 × 16 = 16% | ×1.16 |
| A20，第 50 层 | 0.001 × 20 × 50 = 100% | ×2.00 |
| A0（无进阶） | — | ×1.00（不加成） |
| 任意难度，第 0 层 | — | ×1.00（不加成） |

血量换算后**向下取整**（与原版多人血量缩放的取整方式一致，联机各端结果必定相同）。

---

## 二、多人模式

原版多人已有一个人数/幕数血量机制：

```
原版多人血量 = 原始血量 × 人数 × 按幕数系数
按幕数系数：幕一 1.1 ／ 幕二 1.2 ／ 幕三 Boss 1.3，其余 1.2
（见 Creature.ScaleHpForMultiplayer / MultiplayerScalingModel.GetMultiplayerScaling）
```

本模组的倍率**乘在其结果之上**，两者互不覆盖、纯乘算共存：

```
最终血量 = 原始血量 × 人数 × 按幕数系数 × (1 + 成长系数 × 难度 × 层数)
```

例：**A10、第 16 层、2 人、幕一** → `原始血量 × 2 × 1.1 × 1.16`，
即在原版多人血量基础上再 +16%。

联机适配说明：

- 公式只读取共享的 `RunState`（层数、难度），不读任何本地/随机状态；
- 战斗中动态刷出的怪物（孵化、复活、分节）走同一条换算路径，同样加成、同样确定。

### 参数决议：配置不一致怎么办（含 >4 人模组联机）

**人数（任意 N，包括模组联机的 5 人以上）**：原版 `×人数` 是线性项，任意人数都成立；
本模组倍率与人数无关，两层机制纯乘算组合：

```
最终血量 = 原始血量 × N × 按幕数系数 × (1 + 成长系数 × 难度 × 层数)
```

**成长系数（唯一的本地参数）**：联机战斗在各端独立确定性模拟，
`ChecksumTracker` 对完整战斗状态（含怪物血量）做校验和互比，各端系数不同会直接导致状态发散。
DAT 的决议是**房主权威 + 开局同步 + 整局冻结**：

1. **握手先把关**：游戏按 `id-version` 强制比对 gameplay 模组清单（不一致 ModMismatch 拒连），
   所以联机各方必然是同版本 DAT——这也是自定义同步消息安全的前提（消息 ID 空间必然一致）；
2. **开局同步**：RunStarted 时房主广播自己的系数，客户端向房主请求；房主收到请求会再广播一次
   （幂等，覆盖中途加入 / 漏收场景），日志会提示「已同步房主成长系数 X‰（本地配置为 Y‰，以房主为准）」；
3. **整局冻结**：系数一局只采纳一次——局中在 CYLib 里改配置**不影响本局**（防止各端生效时机不同
   而发散），从下一局开始生效；单人局不受影响，保持即时生效；
4. 尚未收到房主系数的瞬间暂用本地配置并打警告（可靠传输下窗口极小，收到即自动切换）。

实现见 `src/Net/DatConfigSync.cs`（消息与同步状态机）与 `src/Patches/NetSyncPatches.cs`
（RunManager.InitializeShared 挂接，最早可靠注册时机）。

> 原版注意事项：多人模式下 `MultiplayerScalingModel.GetMultiplayerScaling` 对第 4 幕及以上
> （actIndex > 2）会抛异常——原版多人的幕数系数表只有三幕，与本模组无关，模组加幕的多人局会踩到。
> 回放（Replay）会按录制端当时的本地配置重算刷怪血量，建议回放前保持配置一致。

---

## 三、实现：挂钩点

游戏里所有「怪物原始血量 → 最终血量」的换算都经过
`MegaCrit.Sts2.Core.Entities.Creatures.Creature` 的两个方法，本模组各挂一个 Harmony Postfix：

| 补丁 | 目标 | 作用 |
|---|---|---|
| 挂钩点 1 | `Creature.ScaleHpForMultiplayer`（静态） | 在换算结果上乘层数倍率。覆盖：多人刷怪缩放、煎蛋孵化、实验体复活、千足虫分节血量、事件文本血量预览 |
| 挂钩点 2 | `Creature.ScaleMonsterHpForMultiplayer`（实例） | 补齐**单人模式**常规刷怪（原版此方法在 `playerCount == 1` 时是空操作，根本不走挂钩点 1）。多人模式下此补丁直接跳过，防止重复计算 |
| 挂钩点 3 | `PowerCmd.Apply<HardToKillPower>`（泛型命令） | 敌人 buff 适配：把「难以杀灭」施加层数（原版固定 9）改写为配置值，见「六、敌人Buff适配」 |

每只怪物出生时两个补丁**有且只有一个**生效，倍率只乘一次：

- 单人：挂钩点 2 → `原始血量 × (1 + 成长系数 × 难度 × 层数)`
- 多人：挂钩点 1 → `原始血量 × 人数 × 按幕数系数 × (1 + 成长系数 × 难度 × 层数)`

只加成**怪物**（`Creature.Monster != null`），玩家血量不受影响。
图鉴/怪物预览不加成（与原版预览不乘多人系数的行为一致）。

其他实现细节：

- **关键陷阱（曾导致首版失效）**：`CombatState.CreateCreature` 里 `AttachCreature`
  （给怪物绑定 `CombatState`）发生在 `ScaleMonsterHpForMultiplayer` **之后**，
  所以挂钩点 2 的 Postfix 执行时 `__instance.CombatState` 是 `null`。
  首版从 `CombatState.RunState` 取层数/难度拿到 null、倍率算成 1，静默不加成。
  现在一律走 `HpScaling.CurrentRunState()` 全局查找（`RunManager`），与怪物是否已绑定战斗无关。
- 层数与难度取自 `IRunState.TotalFloor` / `IRunState.AscensionLevel`；
  查不到 RunState 或 `NullRunState`（测试/预览场景）倍率为 1，安全无副作用。
- `RunState` 通过 `RunManager.RunStarted` 事件缓存 + `DebugOnlyGetState()`（反射调用，
  不硬引用，防游戏更新移除该方法）双通道获取。
- 血量写入走原版 `SetMaxHpInternal` / `SetCurrentHpInternal`，自带 999999999 上限保护。
- 日志写入 `%APPDATA%\SlayTheSpire2\logs\DeeperAndTankier.log`（每只怪物一行，含层数/难度/倍率）。

---

## 四、构建与安装

工程在 `src/`，用 .NET 9 SDK（本机为 SDK 10 亦可）构建：

```powershell
dotnet build src\DeeperAndTankier.csproj -c Release
```

构建后**自动安装**到 `C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2\mods\DeeperAndTankier\`
（不想自动安装加 `-p:AutoInstall=false`；换盘/换机器改 csproj 顶部的 `Sts2GameDir`）。

> **本仓库不含 CYLib，但编译期需要它。** csproj 默认按「与本仓库同级」去找
> `<本仓库的父目录>\CYLib\dist\CYLib\CYLib.dll`，所以从源码构建前请：
>
> ```powershell
> git clone https://github.com/caiying-dato/CYLib   # 放在本仓库的同级目录
> cd CYLib; .\deploy.cmd                            # 生成 dist\CYLib\CYLib.dll
> ```
>
> CYLib 在别处时用 `-p:CyLibDir=D:\path\to\CYLib` 指定。找不到时构建会**明确报错**
> 告诉你这件事，不会烂成一堆 `CS0246`。

安装后在游戏主菜单 → 模组 里启用 **愈深愈肉（Deeper and Tankier）**，开新局生效。

### 验证方法

- 看日志文件 `%APPDATA%\SlayTheSpire2\logs\DeeperAndTankier.log`，每刷一只怪一行，例如：
  `ScaleMonsterHpForMultiplayer: Cultist 第 16 层 A10 血量 55 → 63（倍率 1.16）`
- 直接看怪物血条数值：与原版差值 = 原版血量 × 0.001 × 难度 × 层数（向下取整）。

### 自动化自测（无需启动游戏）

`selftest/` 是独立测试程序，直接加载游戏程序集和模组的同一份补丁源码，
在真实 .NET 运行时里验证补丁触发、倍率数值、单人/多人路径，并检测 JIT 内联绕过：

```powershell
dotnet build selftest\SelfTest.csproj -c Release
.\selftest\bin\Release\DeeperAndTankierSelfTest.exe
```

全部 PASS 说明补丁逻辑正确；改公式或游戏版本更新后建议先跑一遍。
（CYLib 的配置页注册与持久化依赖 Godot 进程，自测用桩取值器验证「系数 → 倍率」链路，
CYLib 界面与存盘在游戏内验证。）

---

## 五、配置（CYLib）

成长速度通过 **CYLib「模组配置」** 调整（游戏主界面 → 模组配置 → 愈深愈肉）：

| 设置项 | 类型 | 默认 | 说明 |
|---|---|---|---|
| 成长系数（千分比，默认 1） | **小数输入框**（CYLib `FloatInput`，0–99999，可小数） | **1** | **千分比**：1 = 0.001（每难度每层 +1‰）。填 2 即 0.002（A10 第 16 层 → +32%）；填 1.5 即 +24%；0 = 关闭成长。越界自动夹取到 0–99999，非法输入红框提示并回退 |
| 外骨骼虫·难以杀灭 模式 | 下拉（CYLib `Choice`） | **固定模式** | 固定模式 / 参数模式 二选一，见「六、敌人Buff适配」 |
| 固定层数（默认 9） | 整数输入框（1–1000，仅固定模式显示） | **9** | 固定模式下的难以杀灭层数 |
| 难以杀灭系数（默认 1） | 小数输入框（0–10，仅参数模式显示） | **1** | 参数模式公式的额外系数 |

> 本页所有小数输入**最多两位**（超出两位自动取整写回、输入框随即刷新）。

输入框下方有**实时预览行**（CYLib `InfoTitle` 动态金色大字，每次配置变动后重新求值）：

```
【当前系数下A10第16层血量增幅为16%，48层血量增幅为48%】
```

（默认系数 1 时如上；系数 2 显示 32% / 96%；系数 1.5 显示 24% / 72%。
输入后按回车或点击别处提交即刷新。）

- **改动即时生效**：血量计算时实时读取，改完不用重启、当前局下一场战斗即生效
  （多人局除外：以房主开局系数为准、整局冻结，见「二、多人模式」的参数决议）；
- 值持久化在游戏 `user://mod_configs/DeeperAndTankier.json`（CYLib 统一管理，
  支持「重置为默认」）；
- 模组入口向 CYLib 注册配置页（`DeeperAndTankierMod.RegisterConfig`），
  系数读写在 `HpScaling.GrowthPerMille`（键 `growth_per_mille`，十进制，如 1.5）；
- 预览行文本在 `EffectPreview.BuildLine()`，由 CYLib 动态信息行渲染
  （v0.1.3 起 InfoTitle/Info(Func) 随配置变动刷新并支持字号/颜色）。

---

## 六、敌人Buff适配：外骨骼虫·难以杀灭

> 后续版本会陆续适配更多敌人 buff；0.1.3 先适配外骨骼虫（Exoskeleton）的
> **难以杀灭**（`HardToKillPower`）。原版机制是血量下限：怪物血量无法被压到
> 层数（Amount）以下，原版固定 9 层（`Exoskeleton.AfterAddedToRoom` 施加 9m）。

**两种模式（配置界面可切换）**：

| 模式 | 公式 | 可调项 |
|---|---|---|
| **固定模式**（默认） | 最终层数 = 固定值 | 固定值 **9**（原版）→ 1~1000 整数 |
| **参数模式** | 最终层数 = `9 × (1 + DAT血量增幅) × 难以杀灭系数` | 系数 **1** → 0~10（可小数） |

- **DAT血量增幅** = 成长系数 × 难度 × 层数——**只含本模组**的血量增幅，
  不含多人人数/幕数加成，也不含其他模组的加成；
- 例（默认配置、A10 第 20 层）：`9 × (1 + 0.001×10×20) × 1 = 9 × 1.2 = 10.8`；
- 结果**取两位小数**；配置界面所有小数输入**最多两位**（超出自动取整写回）；
- **实时预览**（随配置改动刷新）：`【当前系数下A10第20层难以杀灭层数为xxx】`
  （固定模式显示固定值，参数模式显示公式结果）；
- 多人局与血量增幅同一套决议：以房主开局配置快照为准、整局冻结，各端逐位一致
  （buff 层数进战斗校验和，必须一致）。

实现：`src/Buffs/HardToKillScaling.cs`（模式/公式/预览）+
`src/Patches/HardToKillPatch.cs`（改写 `PowerCmd.Apply<HardToKillPower>` 的施加层数；
挂在命令入口而非怪物调用点，后续任何来源施加该 buff 都自动适配）。
补丁独立成组（`HarmonyId.Buffs`），失败只丢 buff 适配、不影响核心玩法。

---

## 七、发版（局域网）

```powershell
# 一条命令：升版本号 → 自测 → 编译并装进 mods → 打包 → 放到局域网共享架
& .\release.ps1 -Version 0.1.3   # 示例：下一版。版本号只推第三位；各模组版本号相互独立，别与 CYLib 的混淆
```

- 包名规范（2026-10-03 起）：`愈深愈肉(DeeperAndTankier)-v<版本>-<yyyyMMdd-HHmm>.zip`；
- 落地两份：工程 `dist\` 一份 + 局域网共享架 `%USERPROFILE%\.dsh\lan-transfer\outgoing\` 一份
  （脚本会先删掉架上旧的愈深愈肉包，拷过去后再核一次哈希）；
- **「发布」= 局域网，不等于创意工坊**：工坊上架/更新是另一条路，见本机工程目录下
  `steam创意工坊（workshop）\上传交接文档.md` 第 0 条（该文档不随本仓库发布）；
- 想跳过自测加 `-SkipSelfTest`；共享架地址/令牌变了用 `-TransferUrl 'http://…/ui?t=…'`。
- 已发布记录：**`愈深愈肉(DeeperAndTankier)-v0.1.2-20261004-0332.zip`**（22,555 字节，在共享架上）。
  （0309 那版是补 DLL 版本元数据之前的，已被脚本自动删掉。）

---

## 八、创意工坊

工坊物品 ID **3812015038**（标题「愈深愈肉 —— Deeper and Tankier」，public，标签
`Balance / Monsters / Simplified Chinese`，详情中英同页、6059 字节）。

| 版本 | 时间 | 内容 |
| --- | --- | --- |
| v0.1.0 | 2026-10-03 | 首发，无依赖 |
| **v0.1.2** | **2026-10-04** | 成长系数改由 CYLib 配置页调整；**新增依赖 CYLib（3812834784）** |

- 线上依赖：`[3812834784]`（CYLib）。更新时 `workshop.json` 的 `dependencies` **必须写全**，
  漏写会把它删掉（上传交接文档 坑 1）。
- 上传流程与各种坑见本机工程目录下 `steam创意工坊（workshop）\上传交接文档.md`（不随本仓库发布）。
- DLL 版本元数据：2026-10-04 起 csproj 有了 `<Version>` 并开回 `GenerateAssemblyInfo`，
  所以 DLL 的 FileVersion 会是 0.1.2.0；`release.ps1` 编译后会核对它。

---

## 九、许可

本模组源码采用 **MIT 许可证**，详见 [LICENSE](LICENSE)。
《杀戮尖塔 2》游戏本体与游戏资产不属于本许可证，版权归 Mega Crit 所有。

前置模组 **CYLib 不随发布包分发**，请单独获取；它同样是 MIT 许可。

开发源码、构建脚本与完整技术文档：[https://github.com/caiying-dato/DeeperAndTankier](https://github.com/caiying-dato/DeeperAndTankier)
