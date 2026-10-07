using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using DeeperAndTankier.Buffs;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;

namespace DeeperAndTankier.Patches
{
	/// <summary>
	/// 「难以杀灭」层数适配补丁。
	///
	/// 挂在 PowerCmd.Apply&lt;HardToKillPower&gt; 的两个泛型重载（单体/群体）上，
	/// 把 amount（原版固定 9m）改写为配置解析出的层数。挂在命令入口而非
	/// Exoskeleton 的调用点，后续任何怪物/来源施加该 buff 都自动适配（多 buff 扩展时照此模式）。
	///
	/// Prefix 改写参数发生在异步状态机启动之前，原方法读到的就是新值；
	/// 层数来自联机同步快照（冻结），各端逐位一致，不影响校验和。
	/// </summary>
	[HarmonyPatch]
	internal static class HardToKillApplyPatch
	{
		private static IEnumerable<MethodBase> TargetMethods()
		{
			foreach (MethodInfo method in AccessTools.GetDeclaredMethods(typeof(PowerCmd)))
			{
				if (method.Name != "Apply" || !method.IsGenericMethodDefinition)
				{
					continue;
				}
				ParameterInfo[] ps = method.GetParameters();
				// 泛型两个重载的 amount 都在下标 2（context, target(s), amount, applier, cardSource, silent）
				if (ps.Length == 6 && ps[2].ParameterType == typeof(decimal))
				{
					yield return method.MakeGenericMethod(typeof(HardToKillPower));
				}
			}
		}

		/// <summary>把施加时的层数改写为配置值（实测层数随本局难度/层数变化）。</summary>
		internal static void Prefix(ref decimal amount)
		{
			amount = HardToKillScaling.ResolveStacks();
		}
	}
}
