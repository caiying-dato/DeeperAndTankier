using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;

namespace DeeperAndTankier.Patches
{
	/// <summary>
	/// 挂钩点 1：Creature.ScaleHpForMultiplayer
	///
	/// 原版所有「怪物原始血量 → 最终血量」的换算都经过这个静态函数，包括：
	///   - 刷怪时的多人血量缩放（血量 × 人数 × 按幕数系数，见 Creature.ScaleMonsterHpForMultiplayer）
	///   - 战斗中动态设定血量的路径：煎蛋孵化、实验体复活、千足虫分节的血量上下限等
	///   - 事件文本里展示的怪物血量预览（例如「伤痕累累的假人」）
	///
	/// 在它的结果上乘层数倍率，天然与原版按幕数的人数加成乘算共存：
	///   最终血量 = 原始血量 × 人数 × 按幕数系数 × (1 + 0.001 × 难度 × 层数)
	/// </summary>
	[HarmonyPatch(typeof(Creature), nameof(Creature.ScaleHpForMultiplayer))]
	internal static class ScaleHpForMultiplayerPatch
	{
		[HarmonyPostfix]
		private static void Postfix(decimal hp, int playerCount, int actIndex, ref decimal __result)
		{
			decimal factor = HpScaling.FactorForCurrentRun();
			if (factor != 1m)
			{
				__result *= factor;
			}
			DtLog.Info("ScaleHpForMultiplayer: 原始=" + hp + " 人数=" + playerCount + " 幕=" + (actIndex + 1)
				+ " → " + __result + "（倍率 " + factor.ToString("0.####") + "）");
		}
	}

	/// <summary>
	/// 挂钩点 2：Creature.ScaleMonsterHpForMultiplayer
	///
	/// 每只敌方怪物出生时都会经过这里（CombatState.CreateCreature 里、随机完初始血量之后）。
	/// 原版该方法在单人模式（playerCount == 1）下是空操作，不走 ScaleHpForMultiplayer，
	/// 所以单人常规刷怪拿不到挂钩点 1 的加成，必须在这里补上；
	/// 多人模式（playerCount != 1）已在挂钩点 1 乘过倍率，这里跳过以防重复计算。
	///
	/// 关键陷阱：CreateCreature 里 AttachCreature（绑定 CombatState）发生在
	/// ScaleMonsterHpForMultiplayer 调用之后，此刻 __instance.CombatState 是 null，
	/// 所以 RunState 必须走 HpScaling.CurrentRunState() 全局查找，否则倍率算成 1 静默失效。
	/// </summary>
	[HarmonyPatch(typeof(Creature), nameof(Creature.ScaleMonsterHpForMultiplayer))]
	internal static class ScaleMonsterHpForMultiplayerPatch
	{
		[HarmonyPostfix]
		private static void Postfix(Creature __instance, int playerCount)
		{
			string who = (__instance.Monster != null) ? __instance.Monster.Id.Entry : "<非怪物>";
			var runState = __instance.CombatState?.RunState ?? HpScaling.CurrentRunState();
			int floor = (runState != null) ? runState.TotalFloor : -1;
			int asc = (runState != null) ? runState.AscensionLevel : -1;
			decimal factor = HpScaling.FactorFor(runState);

			if (playerCount != 1)
			{
				// 多人路径已在挂钩点 1 乘过，跳过防重复
				DtLog.Info("ScaleMonsterHpForMultiplayer: " + who + "（人数=" + playerCount + "，走挂钩点1）"
					+ " 第 " + floor + " 层 A" + asc + " 倍率 " + factor.ToString("0.####"));
				return;
			}
			if (__instance.Monster == null)
			{
				return;
			}
			if (runState != null)
			{
				HpScaling.RememberRunState(runState);
			}
			if (factor == 1m)
			{
				DtLog.Info("ScaleMonsterHpForMultiplayer: " + who + " 第 " + floor + " 层 A" + asc + " 倍率 1（不加成）");
				return;
			}

			int before = __instance.MaxHp;
			__instance.SetMaxHpInternal(__instance.MaxHp * factor);
			__instance.SetCurrentHpInternal(__instance.MaxHp);
			DtLog.Info("ScaleMonsterHpForMultiplayer: " + who + " 第 " + floor + " 层 A" + asc
				+ " 血量 " + before + " → " + __instance.MaxHp + "（倍率 " + factor.ToString("0.####") + "）");
		}
	}
}
