using System;
using System.Reflection;
using CYLib.Config;
using HarmonyLib;
using DeeperAndTankier.Buffs;
using DeeperAndTankier.Patches;
using MegaCrit.Sts2.Core.Modding;

namespace DeeperAndTankier
{
	/// <summary>
	/// 模组入口。游戏的 ModManager 发现 [ModInitializer] 后会调用 Initialize()。
	/// </summary>
	[ModInitializer(nameof(Initialize))]
	public static class DeeperAndTankierMod
	{
		public const string ModId = "DeeperAndTankier";
		public const string HarmonyId = "CaiYing.DeeperAndTankier";

		private static Harmony _harmony;
		private static Harmony _buffHarmony;

		public static void Initialize()
		{
			// CYLib 配置注册：失败只丢配置界面，不影响玩法补丁
			try
			{
				RegisterConfig();
			}
			catch (Exception e)
			{
				DtLog.Error("注册 CYLib 配置失败（不影响玩法补丁）：" + e);
			}

			try
			{
				// 先安装 RunState 追踪与联机配置同步，再打补丁
				HpScaling.Install();
				Net.DatNetSync.Install();

				// 核心补丁逐个应用：任何单个失败都不拖累其余（PatchAll 一遇异常会中断整批）
				_harmony = new Harmony(HarmonyId);
				foreach (Type type in CorePatchTypes())
				{
					new PatchClassProcessor(_harmony, type).Patch();
				}

				DtLog.Info("已加载：怪物血量增幅 = 成长系数 × 难度 × 爬塔层数（与多人按幕数的人数加成乘算），当前成长系数 "
					+ HpScaling.GrowthPerMille + "‰（" + HpScaling.GrowthPerDifficultyPerFloor.ToString("0.####") + "）");
			}
			catch (Exception e)
			{
				DtLog.Error("初始化失败：" + e);
			}

			// 敌人 buff 适配补丁：单独一组，失败只丢该适配、不影响核心玩法（后续多种 buff 适配都挂这一组）
			try
			{
				_buffHarmony = new Harmony(HarmonyId + ".Buffs");
				foreach (Type type in BuffPatchTypes())
				{
					new PatchClassProcessor(_buffHarmony, type).Patch();
				}
				DtLog.Info("难以杀灭层数适配已生效：" + HardToKillScaling.BuildLine());
			}
			catch (Exception e)
			{
				DtLog.Error("buff 适配补丁失败（核心玩法不受影响）：" + e);
			}
		}

		/// <summary>核心玩法补丁（血量增幅 + 联机配置同步挂接）。</summary>
		private static Type[] CorePatchTypes() => new Type[]
		{
			typeof(ScaleHpForMultiplayerPatch),
			typeof(ScaleMonsterHpForMultiplayerPatch),
			typeof(InitializeSharedPatch),
		};

		/// <summary>敌人 buff 适配补丁（按 buff 逐个追加）。</summary>
		private static Type[] BuffPatchTypes() => new Type[]
		{
			typeof(HardToKillApplyPatch),
		};

		/// <summary>
		/// 向 CYLib 注册配置页。配置值即时生效（每次血量/层数计算时读取，改完不用重启；
		/// 多人局以房主开局快照为准、整局冻结）。
		/// CYLib v0.1.3+：FloatInput/IntInput/Choice（解析、夹取、红框回退由 CYLib 负责，
		/// ControlWidth 收窄输入框），InfoTitle 动态金色大字（每次配置变动后重新求值）。
		/// </summary>
		private static void RegisterConfig()
		{
			CYLibConfig.Register(
				CYLibConfig.NewPage(ModId, "愈深愈肉（Deeper and Tankier）")
					.Describe("怪物血量随爬塔层数增加：增幅 = 成长系数 × 难度 × 层数。层数为全局累计爬塔层数（非幕数），与多人模式原版的人数/幕数血量加成乘算共存。")
					.Section("成长", section => section
						.FloatInput(
							HpScaling.ConfigKeyGrowthPerMille,
							"成长系数（千分比，默认 1）",
							min: 0,
							max: (double)HpScaling.MaxGrowthPerMille,
							defaultValue: (double)HpScaling.DefaultGrowthPerMille,
							onChanged: v => WriteBackRounded(HpScaling.ConfigKeyGrowthPerMille, v),
							description: "填数字（千分比，可小数、最多两位，如 1.5）：1 = 0.001（每难度每层 +1‰）。默认 1；填 0 = 关闭成长。例：填 2 则 A10 第 16 层 +32%；填 1.5 则 +24%。输入后按回车或点击别处生效，越界自动夹取到 0–99999，非法输入红框提示并回退。单人即时生效；多人以房主开局时的配置为准（整局冻结，防各端不同步），局中改动从下一局生效。",
							controlWidth: 160f)
						.InfoTitle(EffectPreview.BuildLine))
					.Section("敌人Buff适配", section => section
						.Add(new ChoiceSetting(HardToKillScaling.ConfigKeyMode, "外骨骼虫·难以杀灭 模式", HardToKillScaling.ModeFixed)
						{
							Description = "固定模式：层数为下方固定值（默认 9 = 原版）。参数模式：最终层数 = 9 × (1 + DAT血量增幅) × 难以杀灭系数；"
								+ "DAT血量增幅只含本模组的成长部分（成长系数×难度×层数），不含多人人数/幕数加成与其他模组加成。",
						}.WithChoice(HardToKillScaling.ModeFixed, "固定模式").WithChoice(HardToKillScaling.ModeParam, "参数模式"))
						.Add(new IntSetting(HardToKillScaling.ConfigKeyFixed, "固定层数（默认 9）", HardToKillScaling.DefaultFixed)
						{
							Min = HardToKillScaling.MinFixed,
							Max = HardToKillScaling.MaxFixed,
							Style = NumberStyle.Input,
							Description = "固定模式下的难以杀灭层数：1~1000 的整数，默认 9（原版数值）。",
							ControlWidth = 160f,
							VisibleIf = () => HardToKillScaling.ReadEffectiveMode() == HardToKillScaling.ModeFixed,
						})
						.Add(new FloatSetting(HardToKillScaling.ConfigKeyCoeff, "难以杀灭系数（默认 1）", (double)HardToKillScaling.DefaultCoeff)
						{
							Min = 0,
							Max = (double)HardToKillScaling.MaxCoeff,
							Style = NumberStyle.Input,
							Description = "参数模式下的系数：0~10 的数字（小数最多两位），默认 1。最终层数 = 9 × (1 + DAT血量增幅) × 系数。",
							ControlWidth = 160f,
							OnChanged = v => WriteBackRounded(HardToKillScaling.ConfigKeyCoeff, v),
							VisibleIf = () => HardToKillScaling.ReadEffectiveMode() == HardToKillScaling.ModeParam,
						})
						.InfoTitle(HardToKillScaling.BuildLine))
					.Build());

			DtLog.Info("已注册 CYLib 配置页，成长系数 " + HpScaling.GrowthPerMille + "‰；" + HardToKillScaling.BuildLine());
		}

		/// <summary>
		/// 「配置小数最多两位」约定：提交值取整到两位写回，CYLib 会随即刷新输入框显示。
		/// （同一值写回时 OnChanged 再触发一次但取整后相等即停，不会死循环。）
		/// </summary>
		private static void WriteBackRounded(string key, double value)
		{
			double rounded = HpScaling.Round2(value);
			if (rounded != value)
			{
				CYLibConfig.Set(ModId, key, rounded);
			}
		}
	}
}
