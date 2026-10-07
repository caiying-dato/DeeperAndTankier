using System.Globalization;

namespace DeeperAndTankier
{
	/// <summary>
	/// 配置页里的实时预览行文本：
	///
	///     【当前系数下A10第16层血量增幅为x%，48层血量增幅为x%】
	///
	/// 由 CYLib 的动态信息行（InfoTitle / Info(Func)）渲染：
	/// 每次配置变动后 CYLib 会重新求值这里的 BuildLine()，
	/// 并按金色大字（24px）显示。本类只算文本，不碰 UI。
	/// </summary>
	internal static class EffectPreview
	{
		/// <summary>A10 第 16 层与第 48 层的实时增幅文本（按当前系数计算）。</summary>
		public static string BuildLine()
		{
			return "【当前系数下A10第16层血量增幅为" + PercentAtFloor(16)
				+ "，48层血量增幅为" + PercentAtFloor(48) + "】";
		}

		/// <summary>当前系数下 A10 某层的增幅百分数文本。</summary>
		public static string PercentAtFloor(int floor)
		{
			decimal percent = HpScaling.GrowthPerDifficultyPerFloor * 10m * floor * 100m;
			return percent.ToString("0.##", CultureInfo.InvariantCulture) + "%";
		}
	}
}
