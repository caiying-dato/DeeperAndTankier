using System;
using System.IO;

namespace DeeperAndTankier
{
	/// <summary>
	/// 模组日志：写入 %APPDATA%\SlayTheSpire2\logs\DeeperAndTankier.log，
	/// 并尽力镜像到游戏控制台（GD.Print，脱离游戏进程时允许失败）。
	/// </summary>
	public static class DtLog
	{
		private static readonly object _lock = new object();
		private static StreamWriter _writer;

		/// <summary>自测进程里没有 Godot 引擎，置 true 跳过游戏日志镜像（只写文件）。</summary>
		public static bool DisableGameLog;

		public static void Info(string message)
		{
			Write("INFO", message);
		}

		public static void Warn(string message)
		{
			Write("WARN", message);
		}

		public static void Error(string message)
		{
			Write("ERROR", message);
		}

		private static void Write(string level, string message)
		{
			lock (_lock)
			{
				try
				{
					if (_writer == null)
					{
						string dir = Path.Combine(
							Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
							"SlayTheSpire2", "logs");
						Directory.CreateDirectory(dir);
						_writer = new StreamWriter(Path.Combine(dir, "DeeperAndTankier.log"), append: true)
						{
							AutoFlush = true
						};
					}
					_writer.WriteLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " [" + level + "] " + message);
				}
				catch
				{
					// 写文件失败不能影响游戏
				}
				try
				{
					if (DisableGameLog)
					{
						return;
					}
					if (level == "ERROR")
					{
						MegaCrit.Sts2.Core.Logging.Log.Error("[DeeperAndTankier] " + message);
					}
					else if (level == "WARN")
					{
						MegaCrit.Sts2.Core.Logging.Log.Warn("[DeeperAndTankier] " + message);
					}
					else
					{
						MegaCrit.Sts2.Core.Logging.Log.Info("[DeeperAndTankier] " + message);
					}
				}
				catch
				{
					// 脱离游戏进程（自测）时 GD.Print 不可用，忽略
				}
			}
		}
	}
}
