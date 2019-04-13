//#define DBG_VERBOSE
#define DBG_SIMPLE
//#define DBG_LOGIC
// Ensure exactly one defined
#if !DBG_VERBOSE && !DBG_SIMPLE && !DBG_LOGIC
#error Exactly one of DBG_VERBOSE, DBG_SIMPLE, DBG_LOGIC must be defined.
#endif
#if  DBG_VERBOSE && (DBG_SIMPLE || DBG_LOGIC) || DBG_SIMPLE && (DBG_VERBOSE || DBG_LOGIC) || DBG_LOGIC && (DBG_VERBOSE || DBG_SIMPLE)
#error Exactly one of DBG_VERBOSE, DBG_SIMPLE, DBG_LOGIC must be defined.
#endif

// Ensure exactly one defined
#if !DBG_VERBOSE && !DBG_SIMPLE && !DBG_LOGIC
#error Exactly one of DBG_VERBOSE, DBG_SIMPLE, DBG_LOGIC must be defined.
#endif
#if  DBG_VERBOSE && (DBG_SIMPLE || DBG_LOGIC) || DBG_SIMPLE && (DBG_VERBOSE || DBG_LOGIC) || DBG_LOGIC && (DBG_VERBOSE || DBG_SIMPLE)
#error Exactly one of DBG_VERBOSE, DBG_SIMPLE, DBG_LOGIC must be defined.
#endif

using System.Reflection;
using System.Threading;
using Microsoft.SPOT;
using Samraksh.Components.Utility;
using Samraksh.eMote.Net.MAC;
using Samraksh.Manager.NetManager;
using Samraksh.VirtualFence.Components;
using Samraksh.eMote;

namespace Samraksh.VirtualFence
{
	/// <summary>
	/// This program listens for radio packets and prints information about identity, sig
	/// nal strength, etc.
	/// It also periodically sends radio packets that another mote can listen to.
	/// It can help you debug another program by "sniffing" what's coming over the radio.
	/// </summary>
	public class Base
	{
		//private static readonly EnhancedEmoteLCD Lcd = new EnhancedEmoteLCD();
		#region unused
		//private static SimpleCSMAStreamChannel _appStream;
		//private static SimpleCSMAStreamChannel _routingStream;
		//private static int _numBeacons = 0;
		//private static int _numData = 0;
		//private static int _numBeat = 0;

		//private static Timer _beaconTimer;
		//private static readonly TimerCallback BeaconTimerCallback = Send_Beacon;

		//private static Timer _dataTimer;
		//static readonly TimerCallback DataTimerCallback = Send_Data;

		//private static Timer _heartbeatTimer;
		//static readonly TimerCallback HeartbeatTimerCallback = Send_Heartbeat;

		//private static void Send_Heartbeat(object state)
		//{
		//	if (_parent != AbstractRouting.NoParent)
		//	{
		//		String dataMsg = "Heartbeat" + SelfAddress;
		//		var toSendByte = System.Text.Encoding.UTF8.GetBytes(dataMsg);
		//		_appStream.Send(_parent, toSendByte);
		//		Debug.Print("\tSent heartbeat# " + (_numBeat++) + ": " + dataMsg + " to node " + _parent);
		//	}
		//}

		//static int count_rec = 0;
		//const int SendDelay = 2000; // ms

		//private static ushort _parent;
		//private static ushort SelfAddress;
		//private static int _bestEtx;
		//private static double _parentLinkRSSI; 
		#endregion

		private static int _baseLiveMsgNum;

		/// <summary>
		/// Set up things for the Base Node
		/// </summary>
		public static void Main()
		{
			Samraksh.eMote.RadarInterface radarInt = new Samraksh.eMote.RadarInterface();
			radarInt.TurnOff();
			Debug.Print(DebuggingSupport.SetupBorder);	//===================
			Debug.EnableGCMessages(false); // We don't want to see garbage collector messages in the Output window

			Debug.Print(VersionInfo.VersionBuild(Assembly.GetExecutingAssembly()));
			Thread.Sleep(3000);

			//Lcd.Write("Base");

			try
			{
				var macBase = SystemGlobal.GetMAC();

				Debug.Print(DebuggingSupport.MacInfo(macBase));
				Debug.Print(DebuggingSupport.SetupBorder);	//===================
				macBase.OnNeighborChange += Routing.Routing_OnNeighborChange;
				//macBase.OnReceiveAll += macBase_OnReceiveAll;

				// Set up serial & pass it on to the components that need it
				var serialComm = new SerialComm("COM1");
				serialComm.Open();

				// Periodically send Base Watchdog message to PC
				//		This is similar to Heartbeat in Net Manager but does not indicate network liveness.
				//		Instead, it is used 
				//		- by the PC Visualizer Data Collector to determine if the Base node is connected to the PC and is running
				//		- by Visualizer to determine if Data Collector is running and connected to the Base node
				var baseWatchdogTimer = new SimplePeriodicTimer(callBackValue =>
				{
					var msg = BaseGlobal.PCMessages.Compose.BaseWatchdog(_baseLiveMsgNum);
					_baseLiveMsgNum++;
					serialComm.Write(msg);
				}, null, 0, BaseGlobal.BaseWatchdogIntervalMs);
				baseWatchdogTimer.Start();

				if (macBase is OMAC)
				{
					const int waitForMac = 30;
#if !DBG_LOGIC
					Debug.Print("\tWaiting " + waitForMac + " sec");
#endif
					Thread.Sleep(waitForMac * 1000);
				}

				// Initialize System Global
				SystemGlobal.Initialize(SystemGlobal.NodeTypes.Base);

				// Initialize routing
				var routing = new Routing(macBase, null);

                // Allow additional sleep to "time-shift" routing and heartbeats (NetManager)
                Thread.Sleep(60 * 1000);

				// Initialize application message handler
				AppMsgHandler.Initialize(macBase, null, serialComm);

				// Initialize network manager
				NetManager.Initialize(macBase, serialComm);

                // Initialize neighborhood manager
                NeighborInfoManager.Initialize(macBase, serialComm);
			}
			catch
			{
				//Lcd.Write("Err");
				//Thread.Sleep(Timeout.Infinite);
			}

			// Sleep forever
			Thread.Sleep(Timeout.Infinite);
		}

	}
}