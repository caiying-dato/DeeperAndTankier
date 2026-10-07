using HarmonyLib;
using DeeperAndTankier.Net;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Runs;

namespace DeeperAndTankier.Patches
{
	/// <summary>
	/// RunManager.InitializeShared：新局 / 读档 / 重连 / 回放所有启动路径都汇聚于此，
	/// 且此时 INetGameService 刚刚就绪、消息尚未开始流动——是注册消息处理器的最早可靠时机。
	/// 私有方法按名匹配（唯一签名）。
	/// </summary>
	[HarmonyPatch(typeof(RunManager), "InitializeShared")]
	internal static class InitializeSharedPatch
	{
		[HarmonyPostfix]
		private static void Postfix(INetGameService netService)
		{
			DatNetSync.Attach(netService);
		}
	}
}
