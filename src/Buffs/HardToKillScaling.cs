using System;
using System.Globalization;
using CYLib.Config;

namespace DeeperAndTankier.Buffs
{
	/// <summary>
	/// 外骨骼虫「难以杀灭」（HardToKillPower）层数适配。
	///
	/// 原版机制：Exoskeleton.AfterAddedToRoom 里 PowerCmd.Apply&lt;HardToKillPower&gt;(…, 9m, …)，
	/// HardToKillPower.ModifyDamageCap 把怪物血量下限锁在 Amount（即"难以杀灭层数"，原版 9）。
	/// 本模组把 9 替换为可配置值（decimal 原生支持小数）。
	///
	/// 两种模式（可切换）：
	///   - 固定模式（默认）：层数 = 固定设定值（默认 9 = 原版），配置界面 1~1000 整数；
	///   - 参数模式：最终层数 = 9 × (1 + DAT血量增幅) × 难以杀灭系数。
	///
/// DAT血量增幅 = 本模组的成长系数 × 难度 × 层数
	/// （**只含本模组**的血量增幅，不含多人人数/幕数加成、也不含其他模组的加成）。
	/// 难以杀灭系数默认 1，0~10、最多两位小数。
	///
	/// 与多人模式的决议一致：生效值 = 房主开局同步快照（整局冻结）> 本地配置，
	/// 保证各端算出的层数逐位相同（战斗状态有校验和）。
	/// </summary>
	public static class HardToKillScaling
	{
		/// <summary>CYLib 配置键：适配模式（fixed / param）。</summary>
		public const string ConfigKeyMode = "hard_to_kill_mode";

		/// <summary>CYLib 配置键：固定模式的层数（1~1000 整数）。</summary>
		public const string ConfigKeyFixed = "hard_to_kill_fixed";

		/// <summary>CYLib 配置键：参数模式的难以杀灭系数（0~10，两位小数）。</summary>
		public const string ConfigKeyCoeff = "hard_to_kill_coeff";

		public const string ModeFixed = "fixed";
		public const string ModeParam = "param";

		/// <summary>原版层数（同时也是参数模式公式的基准值）。</summary>
		public const decimal VanillaStacks = 9m;

		public const int DefaultFixed = 9;
		public const int MinFixed = 1;
		public const int MaxFixed = 1000;
		public const decimal DefaultCoeff = 1m;
		public const decimal MaxCoeff = 10m;

		/// <summary>取值器（自测可替换注入桩值；生产环境不要动）。</summary>
		public static Func<string> ModeSource = ReadEffectiveMode;

		/// <summary>取值器（自测可替换注入桩值；生产环境不要动）。</summary>
		public static Func<int> FixedSource = ReadEffectiveFixed;

		/// <summary>取值器（自测可替换注入桩值；生产环境不要动）。</summary>
		public static Func<decimal> CoeffSource = ReadEffectiveCoeff;

		// ─────────── 生效值：联机同步快照（冻结）> 本地 CYLib 配置 ───────────

		public static string ReadEffectiveMode()
		{
			string synced = Net.DatNetSync.SyncedSettings?.HtkMode;
			if (synced != null)
			{
				return synced;
			}
			return ReadModeFromCyLib();
		}

		public static int ReadEffectiveFixed()
		{
			int? synced = Net.DatNetSync.SyncedSettings?.HtkFixed;
			if (synced.HasValue)
			{
				return synced.Value;
			}
			return ReadFixedFromCyLib();
		}

		public static decimal ReadEffectiveCoeff()
		{
			decimal? synced = Net.DatNetSync.SyncedSettings?.HtkCoeff;
			if (synced.HasValue)
			{
				return synced.Value;
			}
			return ReadCoeffFromCyLib();
		}

		// ─────────── 本地配置读取 ───────────

		public static string ReadModeFromCyLib()
		{
			string text = CYLibConfig.GetText(HpScaling.ModId, ConfigKeyMode, ModeFixed);
			return text == ModeParam ? ModeParam : ModeFixed;
		}

		public static int ReadFixedFromCyLib()
		{
			int value = CYLibConfig.Get<int>(HpScaling.ModId, ConfigKeyFixed, DefaultFixed);
			return Math.Clamp(value, MinFixed, MaxFixed);
		}

		public static decimal ReadCoeffFromCyLib()
		{
			double raw = CYLibConfig.Get<double>(HpScaling.ModId, ConfigKeyCoeff, (double)DefaultCoeff);
			if (double.IsNaN(raw) || double.IsInfinity(raw))
			{
				return DefaultCoeff;
			}
			decimal value = HpScaling.Round2((decimal)raw);
			if (value < 0m)
			{
				return 0m;
			}
			return value > MaxCoeff ? MaxCoeff : value;
		}

		// ─────────── 公式 ───────────

		/// <summary>
		/// 纯公式（指定难度/层数，便于预览与测试）：
		///   固定模式 → 固定值；参数模式 → 9 × (1 + DAT血量增幅) × 难以杀灭系数（结果取两位小数）。
		/// </summary>
		public static decimal StacksFor(int difficulty, int floor)
		{
			if (ModeSource() == ModeFixed)
			{
				return Math.Clamp(FixedSource(), MinFixed, MaxFixed);
			}
			decimal datHpBonus = HpScaling.BonusAt(difficulty, floor); // 仅本模组的血量增幅
			return HpScaling.Round2(VanillaStacks * (1m + datHpBonus) * CoeffSource());
		}

		/// <summary>当前局实际应用的层数（按本局难度/层数计算）。</summary>
		public static decimal ResolveStacks()
		{
			MegaCrit.Sts2.Core.Runs.IRunState runState = HpScaling.CurrentRunState();
			int difficulty = runState?.AscensionLevel ?? 0;
			int floor = runState?.TotalFloor ?? 0;
			return StacksFor(difficulty, floor);
		}
		/// <summary>实时预览行：【当前系数下A10第20层难以杀灭层数为xxx】</summary>
		public static string BuildLine()
		{
			return "【当前系数下A10第20层难以杀灭层数为"
				+ StacksFor(10, 20).ToString("0.##", CultureInfo.InvariantCulture) + "】";
		}
	}
}
