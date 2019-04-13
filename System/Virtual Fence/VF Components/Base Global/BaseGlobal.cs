using System;
using System.Diagnostics;

namespace Samraksh.VirtualFence
{
	/// <summary>
	/// Global items for Base Node
	/// </summary>
	public static class BaseGlobal
	{
		/// <summary>
		/// Base watchdog interval for Base
		/// </summary>
		public static int BaseWatchdogIntervalMs = 60 * 1000;

		/// <summary>
		/// Base live message Ids
		/// </summary>
		public enum MessageIds : byte
		{
			/// <summary>
			/// Base is live
			/// </summary>
			BaseWatchdog = 0,
		}

		/// <summary>
		/// Messages from Base to PC
		/// </summary>
		public static class PCMessages
		{
#if !PC
			/// <summary>
			/// Compose the message string
			/// </summary>
			public static class Compose
			{
				/// <summary>
				/// Base is live message
				/// </summary>
				/// <param name="msgNum"></param>
				/// <returns></returns>
				public static string BaseWatchdog(int msgNum)
				{
					var msgs = SystemGlobal.PCMessages.MsgHeader(SystemGlobal.PCMessages.PCMacPipeIds.BaseLiveness);
					msgs.Append((int)MessageIds.BaseWatchdog);

					msgs.Append(' ');
					msgs.Append(msgNum);

					SystemGlobal.PCMessages.MsgTrailer(msgs);
					return msgs.ToString();
				}
			}
#endif

#if PC
			/// <summary>
			/// Parse messages received by PC
			/// </summary>
			public static class Parse
			{
				/// <summary>
				/// Parse BaseWatchdog message
				/// </summary>
				/// <param name="args"></param>
				/// <param name="msgNum"></param>
				public static bool BaseHeartbeat(string[] args, out int msgNum)
				{
					const int firstArg = 2;	// We've already removed the pipe id and message id
					const int numArgs = 1;
					var argNo = 0;

					msgNum = -1;	// returned if error

					if (args.Length - firstArg != numArgs)
					{
						//throw new Exception(string.Format("Invalid number of arguments for Detection message. S/b {0}, found {1}\n{2}", numArgs, args.Length - firstArg, string.Join(" ", args)));
						Debug.Print("Invalid number of arguments for Detection message. S/b {0}, found {1}\n{2}", numArgs, args.Length - firstArg, string.Join(" ", args));
						return false;
					}

					// Get message number
					// Get originator
					if (!int.TryParse(args[firstArg + argNo], out msgNum))
					{
						//throw new Exception(string.Format("Detection message: Error converting originator {0}", args[firstArg + argNo]));
						Debug.Print("Detection message: Error converting originator {0}", args[firstArg + argNo]);
						return false;
					}
					argNo++;

					if (argNo != numArgs)
					{
						throw new Exception(string.Format("Detection message: Number of arguments parsed: {0}; s/b {1}", numArgs, argNo));
					}
					return true;
				}
			}
#endif
		}

	}
}
