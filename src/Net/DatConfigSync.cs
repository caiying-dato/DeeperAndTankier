using System;
using System.Globalization;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;
using MegaCrit.Sts2.Core.Runs;

namespace DeeperAndTankier.Net
{
	/// <summary>
	/// 房主开局同步的配置快照（整局冻结）。字段随适配项扩展——
	/// 后续新增敌人 buff 适配时在这里加字段即可，协议两端同版本（握手强制）。
	/// </summary>
	public sealed class SyncedSettings
	{
		/// <summary>成长系数（千分比，可小数）。</summary>
		public decimal GrowthPerMille;

		/// <summary>难以杀灭适配模式（fixed / param）。</summary>
		public string HtkMode;

		/// <summary>难以杀灭固定层数（固定模式）。</summary>
		public int HtkFixed;

		/// <summary>难以杀灭系数（参数模式）。</summary>
		public decimal HtkCoeff;
	}

	/// <summary>
	/// 配置同步消息（成长系数 + 难以杀灭适配，后续 buff 适配可继续扩字段）。
	///
	/// 消息安全性依据（2026-10 逆向确认）：
	///   - 握手按「id-version」强制比对 gameplay 模组清单（ModManager.GetGameplayRelevantModNameList，
	///     不一致直接 ModMismatch 拒连），且消息 ID 空间 = 全部 INetMessage 类型排序后的下标；
	///   - 本模组 affects_gameplay: true → 所有联机端必然装着同版本 DAT → 消息 ID 空间必然一致。
	///
	/// 小数以不变文化十进制文本传输（如 "1.5"）：decimal 经文本往返是精确的，
	/// 保证各端拿到逐位相同的值再进十进制运算，杜绝浮点跨端差异。
	///
	/// 每个消息类型必须是 public 且可无参构造（游戏用 Activator.CreateInstance 复原）。
	/// </summary>
	public struct DatConfigSyncMessage : INetMessage, IPacketSerializable
	{
		/// <summary>房主的成长系数（十进制文本）。IsRequest 时无意义。</summary>
		public string GrowthText;

		/// <summary>难以杀灭模式（fixed / param）。</summary>
		public string HtkMode;

		/// <summary>难以杀灭固定层数。</summary>
		public int HtkFixed;

		/// <summary>难以杀灭系数（十进制文本）。</summary>
		public string HtkCoeffText;

		/// <summary>true = 客户端向房主请求当前快照；false = 房主的应答 / 广播。</summary>
		public bool IsRequest;

		// 客户端→房主（ShouldBroadcast=false 即只到房主）；房主发送时走 host 广播语义，与本标志无关。
		public bool ShouldBroadcast => false;

		public NetTransferMode Mode => NetTransferMode.Reliable;

		public LogLevel LogLevel => LogLevel.VeryDebug;

		public bool ShouldBuffer => true;

		public void Serialize(PacketWriter writer)
		{
			writer.WriteBool(IsRequest);
			writer.WriteString(GrowthText ?? string.Empty);
			writer.WriteString(HtkMode ?? string.Empty);
			writer.WriteInt(HtkFixed);
			writer.WriteString(HtkCoeffText ?? string.Empty);
		}

		public void Deserialize(PacketReader reader)
		{
			IsRequest = reader.ReadBool();
			GrowthText = reader.ReadString();
			HtkMode = reader.ReadString();
			HtkFixed = reader.ReadInt();
			HtkCoeffText = reader.ReadString();
		}
	}

	/// <summary>
	/// 多人参数决议：**房主权威 + 开局同步 + 整局冻结**。
	///
	/// 背景：联机战斗在各端独立确定性模拟，ChecksumTracker 会对完整战斗状态（含怪物血量、
	/// buff 层数）做校验和互比；各端配置不同会算出不同结果 → 状态发散。
	///
	/// 协议：
	///   1. 每次开局/读档，RunManager.InitializeShared 会给全新的 NetService → 在此注册消息处理器（最早可靠时机）；
	///   2. RunStarted 时：房主广播自己的配置快照，客户端向房主发请求；
	///   3. 房主收到请求 → 广播应答（顺带刷新所有端，幂等；覆盖中途加入/漏收场景）；
	///   4. 快照整局只采纳一次（开局值冻结）：局中改本地配置不影响本局，避免各端生效时机不同而发散；
	///   5. 单人不走消息（NetSingleplayerGameService 全是 no-op），永远读本地配置、即时生效。
	/// </summary>
	public static class DatNetSync
	{
		private static INetGameService _net;
		private static SyncedSettings _synced;
		private static bool _warnedUnsynced;

		/// <summary>联机同步到的配置快照；单人或尚未同步为 null。</summary>
		public static SyncedSettings SyncedSettings => _synced;

		/// <summary>当前是否处于真实联机会话（房主或客户端）。</summary>
		public static bool IsNetworked =>
			_net != null && (_net.Type == NetGameType.Host || _net.Type == NetGameType.Client);

		/// <summary>订阅开局事件（由模组入口调用一次）。</summary>
		public static void Install()
		{
			RunManager.Instance.RunStarted += OnRunStarted;
		}

		/// <summary>
		/// 绑定一个新的网络服务并注册消息处理器。
		/// 由 RunManager.InitializeShared 补丁在 NetService 就绪时调用；单人服务为 no-op，无副作用。
		/// </summary>
		public static void Attach(INetGameService net)
		{
			if (net == null || ReferenceEquals(net, _net))
			{
				return;
			}
			_net = net;
			_synced = null; // 新一局 / 新会话：解除旧冻结
			_warnedUnsynced = false;
			net.RegisterMessageHandler<DatConfigSyncMessage>(OnMessage);
		}

		/// <summary>读取本地（各配置项的 CYLib 读取器）拼成一份快照。</summary>
		private static SyncedSettings SnapshotFromLocalConfig()
		{
			return new SyncedSettings
			{
				GrowthPerMille = HpScaling.ReadGrowthPerMilleFromCyLib(),
				HtkMode = Buffs.HardToKillScaling.ReadModeFromCyLib(),
				HtkFixed = Buffs.HardToKillScaling.ReadFixedFromCyLib(),
				HtkCoeff = Buffs.HardToKillScaling.ReadCoeffFromCyLib(),
			};
		}

		private static void OnRunStarted(RunState state)
		{
			if (!IsNetworked)
			{
				return;
			}
			SyncedSettings local = SnapshotFromLocalConfig();
			if (_net.Type == NetGameType.Host)
			{
				TryApplySyncedSettings(local);
				_net.SendMessage(ToMessage(local, isRequest: false));
				DtLog.Info("多人：房主配置快照已广播（成长系数 " + ToDecimalText(local.GrowthPerMille)
					+ "‰，难以杀灭 " + DescribeHtk(local) + "）");
			}
			else
			{
				_net.SendMessage(new DatConfigSyncMessage { IsRequest = true });
				DtLog.Info("多人：已请求房主配置快照（本地成长系数 " + ToDecimalText(local.GrowthPerMille) + "‰，等待同步）");
			}
		}

		private static void OnMessage(DatConfigSyncMessage message, ulong senderId)
		{
			if (message.IsRequest)
			{
				// 只有房主应答：广播当前快照（顺带刷新所有端，幂等）
				if (_net == null || _net.Type != NetGameType.Host)
				{
					return;
				}
				SyncedSettings snapshot = _synced ?? SnapshotFromLocalConfig();
				TryApplySyncedSettings(snapshot);
				_net.SendMessage(ToMessage(snapshot, isRequest: false));
				return;
			}

			// 值消息：房主是权威不接受覆盖；客户端只在未冻结时采纳（开局快照整局冻结）
			if (_net != null && _net.Type == NetGameType.Host)
			{
				return;
			}
			if (!TryParseDecimalText(message.GrowthText, out decimal growth)
				|| !TryParseDecimalText(message.HtkCoeffText, out decimal htkCoeff))
			{
				DtLog.Warn("多人：收到无法解析的房主配置快照，忽略");
				return;
			}
			var parsed = new SyncedSettings
			{
				GrowthPerMille = growth,
				HtkMode = message.HtkMode == Buffs.HardToKillScaling.ModeParam
					? Buffs.HardToKillScaling.ModeParam
					: Buffs.HardToKillScaling.ModeFixed,
				HtkFixed = message.HtkFixed,
				HtkCoeff = htkCoeff,
			};
			if (TryApplySyncedSettings(parsed))
			{
				DtLog.Info("多人：已同步房主配置快照（成长系数 " + ToDecimalText(parsed.GrowthPerMille)
					+ "‰，难以杀灭 " + DescribeHtk(parsed) + "）");
			}
		}

		/// <summary>
		/// 采纳配置快照：整局只采纳一次（开局值冻结）。已冻结返回 false。
		/// </summary>
		internal static bool TryApplySyncedSettings(SyncedSettings snapshot)
		{
			if (_synced != null)
			{
				return false;
			}
			if (snapshot.GrowthPerMille < 0m)
			{
				snapshot.GrowthPerMille = 0m;
			}
			_synced = snapshot;
			return true;
		}

		/// <summary>快照 → 消息。</summary>
		internal static DatConfigSyncMessage ToMessage(SyncedSettings snapshot, bool isRequest)
		{
			return new DatConfigSyncMessage
			{
				IsRequest = isRequest,
				GrowthText = ToDecimalText(snapshot.GrowthPerMille),
				HtkMode = snapshot.HtkMode,
				HtkFixed = snapshot.HtkFixed,
				HtkCoeffText = ToDecimalText(snapshot.HtkCoeff),
			};
		}

		/// <summary>小数 → 不变文化十进制文本（跨端逐位一致）。</summary>
		internal static string ToDecimalText(decimal value)
		{
			return value.ToString(CultureInfo.InvariantCulture);
		}

		/// <summary>十进制文本 → 小数；非法（含负数）返回 false。</summary>
		internal static bool TryParseDecimalText(string text, out decimal value)
		{
			if (decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out value)
				&& value >= 0m)
			{
				return true;
			}
			value = 0m;
			return false;
		}

		private static string DescribeHtk(SyncedSettings snapshot)
		{
			return snapshot.HtkMode == Buffs.HardToKillScaling.ModeParam
				? "参数模式（系数 " + ToDecimalText(snapshot.HtkCoeff) + "）"
				: "固定模式（" + snapshot.HtkFixed + " 层）";
		}

		/// <summary>联机但尚未收到房主快照时提示一次（此时暂用本地配置）。</summary>
		internal static void WarnOnceUnsynced()
		{
			if (_warnedUnsynced)
			{
				return;
			}
			_warnedUnsynced = true;
			DtLog.Warn("多人：尚未收到房主配置快照，暂用本地配置（收到后自动切换）");
		}

		/// <summary>自测用：清空会话与冻结状态。</summary>
		internal static void ResetForTest()
		{
			_net = null;
			_synced = null;
			_warnedUnsynced = false;
		}
	}
}
