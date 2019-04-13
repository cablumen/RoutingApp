//#define DBG_VERBOSE
//#define DBG_SIMPLE
#define DBG_LOGIC

// Ensure exactly one defined
#if !DBG_VERBOSE && !DBG_SIMPLE && !DBG_LOGIC
#error Exactly one of DBG_VERBOSE, DBG_SIMPLE, DBG_LOGIC must be defined.
#endif
#if  DBG_VERBOSE && (DBG_SIMPLE || DBG_LOGIC) || DBG_SIMPLE && (DBG_VERBOSE || DBG_LOGIC) || DBG_LOGIC && (DBG_VERBOSE || DBG_SIMPLE)
#error Exactly one of DBG_VERBOSE, DBG_SIMPLE, DBG_LOGIC must be defined.
#endif

using Microsoft.SPOT;
using Samraksh.eMote.Net;
using Samraksh.eMote.Net.MAC;

namespace Samraksh.VirtualFence
{
	/// <summary>
	/// Debugging support items
	/// </summary>
	public static class DebuggingSupport
	{
		/// <summary>Border for setup messages</summary>
		public const string SetupBorder = "=================================";

#if !PC
		public static string MacInfo(IMAC imacInstance)
		{
			var info = "MAC Type: " + imacInstance.GetType()
				+ ", Channel: " + imacInstance.MACRadioObj.Channel
				+ ", Power: " + imacInstance.MACRadioObj.TxPower
				+ ", Radio Address: " + imacInstance.MACRadioObj.RadioAddress
				+ ", Radio Type: " + imacInstance.MACRadioObj.RadioName
				+ ", Neighbor Liveness Delay: " + imacInstance.NeighborLivenessDelay;
			return info;
		}

		public static void PrintMessageReceived(IMAC imac, string toPrint)
		{
			const string stars = "****************** ";
			var pipe = imac as MACPipe;
			if (pipe != null)
			{
				Debug.Print("\n" + stars + toPrint + " Receive on pipe " + pipe.PayloadType);
			}
			else
			{
				Debug.Print("\n" + stars + toPrint + " Receive");
			}
		}

		public static void PrintMessageSent(IMAC imac, string toPrint)
		{
			const string hashes = "################## ";
			var pipe = imac as MACPipe;
			if (pipe != null)
			{
				Debug.Print("\n" + hashes + toPrint + " Sent on pipe " + pipe.PayloadType);
			}
			else
			{
				Debug.Print("\n" + hashes + toPrint + " Sent");
			}
		}

		public static void PrintSelfAndParentAddress(ushort selfAddress, ushort parentAddress)
		{
			Debug.Print("\tSelf: " + selfAddress + ", Parent: " + parentAddress);
		}
#endif
	}
}
