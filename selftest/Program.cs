using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Runs.History;
using MegaCrit.Sts2.Core.TestSupport;
using DeeperAndTankier;

namespace DeeperAndTankier.SelfTest
{
	/// <summary>测试用怪物：血量固定 100，避免随机与进阶干扰。</summary>
	public sealed class FakeMonster : MonsterModel
	{
		public FakeMonster()
		{
			NeverEverCallThisOutsideOfTests_SetIsMutable(true);
		}

		public override int MinInitialHp => 100;

		public override int MaxInitialHp => 100;

		public override bool ShouldReceiveCombatHooks => false;

		protected override MonsterMoveStateMachine GenerateMoveStateMachine()
		{
			return null;
		}
	}

	/// <summary>探针：记录被测方法入口是否真正被执行（用于检测 JIT 内联绕过补丁）。</summary>
	internal static class Probe
	{
		public static bool ScaleHpEntryHit;
		public static bool ScaleMonsterEntryHit;
	}

	[HarmonyPatch(typeof(Creature), nameof(Creature.ScaleHpForMultiplayer))]
	internal static class ProbeScaleHp
	{
		[HarmonyPostfix]
		private static void Postfix()
		{
			Probe.ScaleHpEntryHit = true;
		}
	}

	[HarmonyPatch(typeof(Creature), nameof(Creature.ScaleMonsterHpForMultiplayer))]
	internal static class ProbeScaleMonster
	{
		[HarmonyPostfix]
		private static void Postfix()
		{
			Probe.ScaleMonsterEntryHit = true;
		}
	}

	internal static class Program
	{
		private static int _failed;

		private static void Check(string name, bool ok, string detail)
		{
			Console.WriteLine((ok ? "[PASS] " : "[FAIL] ") + name + " —— " + detail);
			if (!ok)
			{
				_failed++;
			}
		}

		/// <summary>反射设置属性或字段（含 init-only 自动属性的后备字段）。</summary>
		private static void SetMember(object target, string name, object value)
		{
			Type type = target.GetType();
			PropertyInfo prop = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (prop != null && prop.GetSetMethod(true) != null)
			{
				prop.SetValue(target, value);
				return;
			}
			FieldInfo field = type.GetField("<" + name + ">k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)
				?? type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (field == null)
			{
				throw new InvalidOperationException("找不到成员 " + name);
			}
			field.SetValue(target, value);
		}

		private static void Main()
		{
			Console.WriteLine("=== DeeperAndTankier 自测（A10、第 16 层，期望倍率 1.16）===");
			DtLog.DisableGameLog = true; // 测试进程无 Godot 引擎，跳过游戏日志镜像

			// 1) 与游戏一致：安装 RunState 追踪 + 打上模组补丁与探针补丁（同一程序集，只 PatchAll 一次）
			TestMode.IsOn = true; // 允许测试专用 API（如 SetIsMutable）
			HpScaling.Install();
			Harmony harmony = new Harmony("selftest");
			harmony.PatchAll(Assembly.GetExecutingAssembly());
			Console.WriteLine("补丁已应用");

			// 隔离配置来源：自测进程没有 Godot 引擎，CYLib 存储层会触发原生崩溃，
			// 且断言按默认系数写死 —— 故替换取值器注入默认值；测试 7 会临时改值验证公式链路
			HpScaling.GrowthPerMilleSource = () => HpScaling.DefaultGrowthPerMille;

			// 2) 构造 A10、第 16 层的 RunState。
			//    绕开 Player/存档等无关依赖（那条链会触碰 Godot 原生），
			//    只填本模组与刷怪路径真正读取的成员：AscensionLevel / TotalFloor(=地图点数) / Rng。
			RunState runState = (RunState)RuntimeHelpers.GetUninitializedObject(typeof(RunState));
			SetMember(runState, "AscensionLevel", 10);
			SetMember(runState, "Rng", new RunRngSet("SELFTEST"));
			List<List<MapPointHistoryEntry>> history = new List<List<MapPointHistoryEntry>>();
			List<MapPointHistoryEntry> act1 = new List<MapPointHistoryEntry>();
			for (int i = 0; i < 16; i++)
			{
				act1.Add(new MapPointHistoryEntry());
			}
			history.Add(act1);
			SetMember(runState, "_mapPointHistory", history);
			SetMember(runState, "_visitedMapCoords", new List<MapCoord>());

			// 模拟游戏运行：RunManager.Instance.State = runState（DebugOnlyGetState 读取的就是它）
			typeof(RunManager).GetProperty("State", BindingFlags.Instance | BindingFlags.NonPublic)
				.SetValue(RunManager.Instance, runState);

			Check("RunState 读取",
				runState.TotalFloor == 16 && runState.AscensionLevel == 10,
				"TotalFloor=" + runState.TotalFloor + " AscensionLevel=" + runState.AscensionLevel);

			decimal factor = HpScaling.FactorForCurrentRun();
			Check("倍率查找", factor == 1.16m, "FactorForCurrentRun()=" + factor);

			// 3) 直接调用静态方法：补丁必须生效（基线）
			Probe.ScaleHpEntryHit = false;
			decimal direct = Creature.ScaleHpForMultiplayer(100m, null, 1, 0);
			Check("直接调用 ScaleHpForMultiplayer",
				direct == 116m && Probe.ScaleHpEntryHit,
				"100 → " + direct + "，入口命中=" + Probe.ScaleHpEntryHit);

			// 4) 直接调用实例方法（单人刷怪路径，即用户实测的场景）
			Creature c1 = new Creature(new FakeMonster(), CombatSide.Enemy, null);
			c1.ScaleMonsterHpForMultiplayer(null, 1, 0);
			Check("直接调用 ScaleMonsterHpForMultiplayer（单人路径）",
				c1.MaxHp == 116 && c1.CurrentHp == 116,
				"100 → MaxHp=" + c1.MaxHp + " CurrentHp=" + c1.CurrentHp);

			// 5) 通过 CombatState.CreateCreature 完整刷怪（无玩家 → 原版走 ScaleHpForMultiplayer 内部调用）
			Probe.ScaleHpEntryHit = false;
			Probe.ScaleMonsterEntryHit = false;
			CombatState combatState = new CombatState(null, runState);
			Creature spawned = combatState.CreateCreature(new FakeMonster(), CombatSide.Enemy, null);
			Check("CreateCreature 刷怪（内部 ScaleHpForMultiplayer 路径）",
				spawned.MaxHp == 116,
				"100 → MaxHp=" + spawned.MaxHp + "，期望 116");
			Check("内联检测：ScaleHpForMultiplayer 入口（被 CreateCreature 间接调用）",
				Probe.ScaleHpEntryHit,
				"入口命中=" + Probe.ScaleHpEntryHit + "（false=调用被 JIT 内联，补丁被绕过！）");
			Check("内联检测：ScaleMonsterHpForMultiplayer 入口（被 CreateCreature 调用）",
				Probe.ScaleMonsterEntryHit,
				"入口命中=" + Probe.ScaleMonsterEntryHit + "（false=调用被 JIT 内联，补丁被绕过！）");

			// 6) 多人路径：原始 100 × 2 人 × 1.1(幕一) × 1.16 = 255.2 → 取整 255
			Probe.ScaleHpEntryHit = false;
			Creature c2 = new Creature(new FakeMonster(), CombatSide.Enemy, null);
			c2.ScaleMonsterHpForMultiplayer(null, 2, 0);
			Check("多人路径（2人幕一）",
				c2.MaxHp == 255,
				"100 → MaxHp=" + c2.MaxHp + "，期望 255（100×2×1.1×1.16 取整）");
			Check("内联检测：ScaleHpForMultiplayer 入口（多人内部调用）",
				Probe.ScaleHpEntryHit,
				"入口命中=" + Probe.ScaleHpEntryHit + "（false=被内联，挂钩点1在多人下失效！）");

			// 7) 成长系数链路：系数千分比（默认 1 = 0.001），改值即时生效。
			//    CYLib 接入本身（配置页注册、持久化）依赖 Godot 进程，在游戏内验证；
			//    这里用桩取值器验证「系数 → 倍率」的公式链路。
			HpScaling.GrowthPerMilleSource = () => 1;
			Check("默认系数 1‰ = 0.001 → 倍率 1.16",
				HpScaling.GrowthPerDifficultyPerFloor == 0.001m && HpScaling.FactorForCurrentRun() == 1.16m,
				"GrowthPerMille=" + HpScaling.GrowthPerMille + " FactorForCurrentRun()=" + HpScaling.FactorForCurrentRun());
			HpScaling.GrowthPerMilleSource = () => 2;
			Check("系数=2 → 0.002 → 倍率 1.32",
				HpScaling.FactorForCurrentRun() == 1.32m,
				"FactorForCurrentRun()=" + HpScaling.FactorForCurrentRun());
			HpScaling.GrowthPerMilleSource = () => 0;
			Check("系数=0 → 不加成",
				HpScaling.FactorForCurrentRun() == 1m,
				"FactorForCurrentRun()=" + HpScaling.FactorForCurrentRun());
			HpScaling.GrowthPerMilleSource = () => -5;
			Check("负系数钳制为 0 → 不加成",
				HpScaling.GrowthPerMille == 0 && HpScaling.FactorForCurrentRun() == 1m,
				"GrowthPerMille=" + HpScaling.GrowthPerMille + " FactorForCurrentRun()=" + HpScaling.FactorForCurrentRun());
			// 复原生产取值器
			HpScaling.GrowthPerMilleSource = HpScaling.ReadGrowthPerMilleFromCyLib;

			// 8) 实时预览行文本（CYLib 动态信息行每次变动后重新求值 BuildLine）
			HpScaling.GrowthPerMilleSource = () => 1;
			Check("预览行（默认系数 1）",
				EffectPreview.BuildLine() == "【当前系数下A10第16层血量增幅为16%，48层血量增幅为48%】",
				EffectPreview.BuildLine());
			HpScaling.GrowthPerMilleSource = () => 2;
			Check("预览行随系数实时变化（系数 2）",
				EffectPreview.BuildLine() == "【当前系数下A10第16层血量增幅为32%，48层血量增幅为96%】",
				EffectPreview.BuildLine());
			// 复原生产取值器
			HpScaling.GrowthPerMilleSource = HpScaling.ReadGrowthPerMilleFromCyLib;

			// 9) 多人参数决议：房主同步值优先且整局冻结，未同步时读本地配置
			Net.DatNetSync.ResetForTest();
			HpScaling.LocalConfigReader = () => 5;
			HpScaling.GrowthPerMilleSource = HpScaling.ReadEffectiveGrowthPerMille;
			Check("未同步：读本地配置 5‰ → 倍率 1.80",
				HpScaling.ReadEffectiveGrowthPerMille() == 5 && HpScaling.FactorForCurrentRun() == 1.8m,
				"effective=" + HpScaling.ReadEffectiveGrowthPerMille() + " factor=" + HpScaling.FactorForCurrentRun());
			Check("同步值采纳一次即冻结",
				Net.DatNetSync.TryApplySyncedSettings(new Net.SyncedSettings { GrowthPerMille = 2m, HtkMode = "fixed", HtkFixed = 9, HtkCoeff = 1m })
					&& !Net.DatNetSync.TryApplySyncedSettings(new Net.SyncedSettings { GrowthPerMille = 7m, HtkMode = "fixed", HtkFixed = 9, HtkCoeff = 1m }),
				"首次采纳应为 true、二次应为 false");
			Check("已同步：房主值 2‰ 优先于本地 5‰ → 倍率 1.32",
				HpScaling.ReadEffectiveGrowthPerMille() == 2 && HpScaling.FactorForCurrentRun() == 1.32m,
				"effective=" + HpScaling.ReadEffectiveGrowthPerMille() + " factor=" + HpScaling.FactorForCurrentRun());
			Net.DatConfigSyncMessage msg = new Net.DatConfigSyncMessage
			{
				GrowthText = "1.5", HtkMode = "param", HtkFixed = 9, HtkCoeffText = "1.35", IsRequest = true,
			};
			PacketWriter packetWriter = new PacketWriter();
			msg.Serialize(packetWriter);
			byte[] packetBytes = new byte[packetWriter.BytePosition];
			Array.Copy(packetWriter.Buffer, packetBytes, packetBytes.Length);
			PacketReader packetReader = new PacketReader();
			packetReader.Reset(packetBytes);
			Net.DatConfigSyncMessage restoredMsg = new Net.DatConfigSyncMessage();
			restoredMsg.Deserialize(packetReader);
			Check("DatConfigSyncMessage 序列化往返（十进制文本逐位一致）",
				restoredMsg.GrowthText == "1.5" && restoredMsg.HtkMode == "param" && restoredMsg.HtkFixed == 9
					&& restoredMsg.HtkCoeffText == "1.35" && restoredMsg.IsRequest,
				"GrowthText=" + restoredMsg.GrowthText + " HtkMode=" + restoredMsg.HtkMode
					+ " HtkFixed=" + restoredMsg.HtkFixed + " HtkCoeffText=" + restoredMsg.HtkCoeffText);
			Check("同步文本解析：小数合法、负数与非法拒绝",
				Net.DatNetSync.TryParseDecimalText("1.5", out decimal parsedGrowth) && parsedGrowth == 1.5m
					&& !Net.DatNetSync.TryParseDecimalText("-1", out _)
					&& !Net.DatNetSync.TryParseDecimalText("abc", out _),
				"\"1.5\"→" + parsedGrowth + "；\"-1\"/\"abc\" 应拒绝");
			// 复原生产取值器
			Net.DatNetSync.ResetForTest();
			HpScaling.LocalConfigReader = HpScaling.ReadGrowthPerMilleFromCyLib;
			HpScaling.GrowthPerMilleSource = HpScaling.ReadEffectiveGrowthPerMille;

			// 10) 小数系数（CYLib FloatInput）与上限钳制
			HpScaling.GrowthPerMilleSource = () => 1.5m;
			Check("小数系数 1.5‰ → 倍率 1.24",
				HpScaling.GrowthPerMille == 1.5m && HpScaling.FactorForCurrentRun() == 1.24m,
				"GrowthPerMille=" + HpScaling.GrowthPerMille + " factor=" + HpScaling.FactorForCurrentRun());
			Check("小数系数预览行（1.5‰ → 24% / 72%）",
				EffectPreview.BuildLine() == "【当前系数下A10第16层血量增幅为24%，48层血量增幅为72%】",
				EffectPreview.BuildLine());
			HpScaling.GrowthPerMilleSource = () => 1.05m;
			Check("小数显示保留（1.05‰ → 16.8%）",
				EffectPreview.PercentAtFloor(16) == "16.8%",
				EffectPreview.PercentAtFloor(16));
			HpScaling.GrowthPerMilleSource = () => 123456.78m;
			Check("超上限钳制为 99999",
				HpScaling.GrowthPerMille == HpScaling.MaxGrowthPerMille,
				"GrowthPerMille=" + HpScaling.GrowthPerMille);
			HpScaling.GrowthPerMilleSource = HpScaling.ReadEffectiveGrowthPerMille;

			// 11) 外骨骼虫「难以杀灭」层数适配（固定/参数两种模式 + 实时预览 + 补丁改写）
			Buffs.HardToKillScaling.ModeSource = () => Buffs.HardToKillScaling.ModeFixed;
			Buffs.HardToKillScaling.FixedSource = () => 42;
			Check("固定模式 → 固定值 42",
				Buffs.HardToKillScaling.StacksFor(10, 20) == 42m
					&& Buffs.HardToKillScaling.BuildLine() == "【当前系数下A10第20层难以杀灭层数为42】",
				Buffs.HardToKillScaling.BuildLine());
			Buffs.HardToKillScaling.ModeSource = () => Buffs.HardToKillScaling.ModeParam;
			Buffs.HardToKillScaling.CoeffSource = () => 1m;
			HpScaling.GrowthPerMilleSource = () => 1m;
			Check("参数模式默认：9×(1+0.2)×1 = 10.8",
				Buffs.HardToKillScaling.StacksFor(10, 20) == 10.8m
					&& Buffs.HardToKillScaling.BuildLine() == "【当前系数下A10第20层难以杀灭层数为10.8】",
				Buffs.HardToKillScaling.BuildLine());
			Buffs.HardToKillScaling.CoeffSource = () => 1.35m;
			HpScaling.GrowthPerMilleSource = () => 2m;
			Check("参数模式组合：9×(1+0.4)×1.35 = 17.01",
				Buffs.HardToKillScaling.StacksFor(10, 20) == 17.01m,
				"StacksFor=" + Buffs.HardToKillScaling.StacksFor(10, 20));
			HpScaling.GrowthPerMilleSource = () => 1.05m;
			Check("结果两位小数取整：9×1.21×1.35=14.7015 → 14.70",
				Buffs.HardToKillScaling.StacksFor(10, 20) == 14.70m,
				"StacksFor=" + Buffs.HardToKillScaling.StacksFor(10, 20));
			decimal rewritten = 9m;
			Patches.HardToKillApplyPatch.Prefix(ref rewritten);
			Check("补丁 Prefix 改写施加层数（按本局 A10 第 16 层）",
				rewritten == Buffs.HardToKillScaling.StacksFor(10, 16),
				"rewritten=" + rewritten + " StacksFor(10,16)=" + Buffs.HardToKillScaling.StacksFor(10, 16));
			// 复原生产取值器
			Buffs.HardToKillScaling.ModeSource = Buffs.HardToKillScaling.ReadEffectiveMode;
			Buffs.HardToKillScaling.FixedSource = Buffs.HardToKillScaling.ReadEffectiveFixed;
			Buffs.HardToKillScaling.CoeffSource = Buffs.HardToKillScaling.ReadEffectiveCoeff;
			HpScaling.GrowthPerMilleSource = HpScaling.ReadEffectiveGrowthPerMille;

			Console.WriteLine(_failed == 0 ? "=== 全部通过 ===" : "=== 失败 " + _failed + " 项 ===");
			Environment.Exit(_failed == 0 ? 0 : 1);
		}
	}
}
