using System;
using System.Reflection;
using CYLib.Config;
using MegaCrit.Sts2.Core.Runs;

namespace DeeperAndTankier
{
	/// <summary>
	/// 血量增幅核心公式：
	///
	///     增幅 = 成长系数 × 难度(A) × 爬塔层数
	///     血量倍率 = 1 + 增幅
	///
	/// 成长系数来自 CYLib「模组配置」（键 growth_per_mille，千分比、可小数，如 1.5）：
	/// 默认 1 = 0.001，即每难度每层 +1‰。
	///
	/// 其中：
	///   - 难度 = RunState.AscensionLevel（A10 → 10）
	///   - 爬塔层数 = RunState.TotalFloor（本局已爬的总层数，跨幕累计；
	///     注意不是幕数 CurrentActIndex，也不是幕内层数 ActFloor）
	///
	/// 例（默认系数）：第 16 层、A10 → 0.001 × 10 × 16 = 16% → 最终血量 ×1.16
	/// 系数 1.5 时：0.0015 × 10 × 16 = 24% → ×1.24。A0 或第 0 层时倍率为 1（不加成）。
	///
	/// 与多人模式的乘算说明：
	/// 原版多人血量 = 原始血量 × 人数 × 按幕数系数（幕一 1.1 / 幕二 1.2 / 幕三 Boss 1.3），
	/// 本模组的倍率再乘在其结果上，互不覆盖：
	///     最终血量 = 原始血量 × 人数 × 按幕数系数 × (1 + 成长系数 × 难度 × 层数)
	///
	/// 多人参数决议见 Net.DatNetSync：生效系数 = 房主同步值（整局冻结）> 本地配置。
	/// </summary>
	public static class HpScaling
	{
		/// <summary>模组配置页 modId（= 模组清单 id）。</summary>
		public const string ModId = DeeperAndTankierMod.ModId;

		/// <summary>CYLib 配置键：成长系数（千分比，可小数）。</summary>
		public const string ConfigKeyGrowthPerMille = "growth_per_mille";

		/// <summary>默认成长系数：1‰ = 0.001。</summary>
		public const decimal DefaultGrowthPerMille = 1m;

		/// <summary>成长系数上限（CYLib 输入框与本模组钳制共用）。</summary>
		public const decimal MaxGrowthPerMille = 99999m;

		/// <summary>
		/// 成长系数取值器。生产环境默认走 <see cref="ReadEffectiveGrowthPerMille"/>
		/// （多人 = 开局同步的房主系数、整局冻结；单人 = 本地 CYLib 配置、即时生效）；
		/// 自测进程没有 Godot 引擎（CYLib 存储层会触发不可捕获的原生崩溃），
		/// 会替换此取值器注入桩值来验证公式链路。
		/// </summary>
		public static Func<decimal> GrowthPerMilleSource = ReadEffectiveGrowthPerMille;

		/// <summary>
		/// 本地 CYLib 配置读取器（自测可替换以隔离 CYLib；生产环境不要动）。
		/// </summary>
		public static Func<decimal> LocalConfigReader = ReadGrowthPerMilleFromCyLib;

		/// <summary>
		/// 成长系数（千分比，可小数）：值 N → 实际系数 N/1000。
		/// 每次血量计算时读取；取值失败或非法时回退默认 1。
		/// </summary>
		public static decimal GrowthPerMille
		{
			get
			{
				try
				{
					decimal value = GrowthPerMilleSource();
					if (value < 0m)
					{
						return 0m;
					}
					return value > MaxGrowthPerMille ? MaxGrowthPerMille : value;
				}
				catch
				{
					return DefaultGrowthPerMille;
				}
			}
		}

		/// <summary>
		/// 从 CYLib 配置读成长系数（键 growth_per_mille，小数，CYLib FloatInput 数字输入框）。
		/// 用泛型取值以兼容历史整数值（GetFloat 对 int 存量会误回退默认）；
		/// 按「配置小数最多两位」约定取整（写回侧另有夹取）。
		/// </summary>
		public static decimal ReadGrowthPerMilleFromCyLib()
		{
			double raw = CYLibConfig.Get<double>(ModId, ConfigKeyGrowthPerMille, (double)DefaultGrowthPerMille);
			if (double.IsNaN(raw) || double.IsInfinity(raw))
			{
				return DefaultGrowthPerMille;
			}
			return Round2((decimal)Math.Max(0.0, raw));
		}

		/// <summary>
		/// 生效成长系数（多人参数决议的唯一入口）：
		///   - 联机已同步 → 房主配置快照里的系数（整局冻结，见 DatNetSync）；
		///   - 其余（单人，或联机尚未同步）→ 本地 CYLib 配置。
		/// </summary>
		public static decimal ReadEffectiveGrowthPerMille()
		{
			decimal? synced = Net.DatNetSync.SyncedSettings?.GrowthPerMille;
			if (synced.HasValue)
			{
				return synced.Value;
			}
			if (Net.DatNetSync.IsNetworked)
			{
				Net.DatNetSync.WarnOnceUnsynced();
			}
			return LocalConfigReader();
		}

		/// <summary>增幅系数（= GrowthPerMille / 1000；默认 1‰ = 0.001）。</summary>
		public static decimal GrowthPerDifficultyPerFloor => GrowthPerMille / 1000m;

		/// <summary>
		/// DAT 血量增幅（仅本模组部分）：成长系数 × 难度 × 层数。
		/// 不含多人人数/幕数加成，也不含其他模组的加成——buff 适配的参数模式用它。
		/// </summary>
		public static decimal BonusAt(int difficulty, int floor)
		{
			if (difficulty <= 0 || floor <= 0)
			{
				return 0m;
			}
			return GrowthPerDifficultyPerFloor * difficulty * floor;
		}

		/// <summary>任意小数四舍五入到两位（AwayFromZero，避开银行家舍入的 1.225→1.22 怪相）。</summary>
		public static decimal Round2(decimal value)
		{
			return Math.Round(value, 2, MidpointRounding.AwayFromZero);
		}

		/// <summary>double 版（用于 CYLib 配置写回）。</summary>
		public static double Round2(double value)
		{
			return Math.Round(value, 2, MidpointRounding.AwayFromZero);
		}

		/// <summary>由 RunStarted 事件维护的当前局 RunState 备份。</summary>
		private static IRunState _trackedRunState;

		/// <summary>反射缓存：RunManager.DebugOnlyGetState()（避免硬引用，防止游戏更新删除该方法时直接崩）。</summary>
		private static MethodInfo _debugGetStateMethod;

		public static void Install()
		{
			RunManager.Instance.RunStarted += OnRunStarted;
			try
			{
				_debugGetStateMethod = typeof(RunManager).GetMethod(
					"DebugOnlyGetState",
					BindingFlags.Instance | BindingFlags.Public);
			}
			catch
			{
				_debugGetStateMethod = null;
			}
		}

		private static void OnRunStarted(RunState state)
		{
			_trackedRunState = state;
		}

		/// <summary>
		/// 由补丁在拿到权威 RunState 时顺手刷新备份（双保险）。
		/// </summary>
		public static void RememberRunState(IRunState runState)
		{
			if (runState != null)
			{
				_trackedRunState = runState;
			}
		}

		/// <summary>
		/// 获取当前局 RunState。优先走 RunManager 的公开查询方法（永远是当前局），
		/// 拿不到时退回 RunStarted 事件缓存。
		/// </summary>
		public static IRunState CurrentRunState()
		{
			if (_debugGetStateMethod != null)
			{
				try
				{
					IRunState live = _debugGetStateMethod.Invoke(RunManager.Instance, null) as IRunState;
					if (live != null)
					{
						return live;
					}
				}
				catch
				{
					// 忽略，走缓存
				}
			}
			return _trackedRunState;
		}

		/// <summary>
		/// 由 RunState 计算血量倍率：1 + 成长系数 × 难度 × 层数。
		/// NullRunState（测试/预览场景）或系数为 0 时返回 1，即不加成。
		/// </summary>
		public static decimal FactorFor(IRunState runState)
		{
			if (runState == null)
			{
				return 1m;
			}
			decimal bonus = BonusAt(runState.AscensionLevel, runState.TotalFloor);
			return bonus <= 0m ? 1m : 1m + bonus;
		}

		public static decimal FactorForCurrentRun()
		{
			return FactorFor(CurrentRunState());
		}
	}
}
