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

using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using Microsoft.SPOT;
using Samraksh.Components.Utility;
using Samraksh.eMote.Net;
using Samraksh.eMote.Net.MAC;
//using Samraksh.Manager.Component;
//using Samraksh.Manager.LocalManager;
using Samraksh.Manager.NetManager;
using Samraksh.VirtualFence.Components;
using Samraksh.eMote;

namespace Samraksh.VirtualFence
{
    /// <summary>
    /// Main program
    /// </summary>
    public partial class Program
    {
        private static int SendPacketInterval = 15 * 1000;
        private static readonly EnhancedEmoteLCD _lcd = new EnhancedEmoteLCD();

        /// <summary>
        /// The main program
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        /// <exception cref="IOException"></exception>
        public static void Main()
        {
            Debug.EnableGCMessages(false);
            var radarInt = new RadarInterface();
            radarInt.TurnOff();
            Debug.Print(DebuggingSupport.SetupBorder);
            Debug.Print(VersionInfo.VersionBuild(Assembly.GetExecutingAssembly()));
            _lcd.Write("Clnt");

            Thread.Sleep(3000);
            try
            {
                var macBase = SystemGlobal.GetMAC();
                //#warning delete this when fixed
                //				macBase.OnReceive+=(mac, time) => { };
                Debug.Print(DebuggingSupport.MacInfo(macBase));
                Debug.Print(DebuggingSupport.SetupBorder);

                macBase.OnNeighborChange += Routing.Routing_OnNeighborChange;

                // Set up serial & pass it on to the components that need it
                var serialComm = new SerialComm("COM1");
                serialComm.Open();

                if (macBase is OMAC)
                {
                    const int waitForMac = 30;
#if !DBG_LOGIC
                    Debug.Print("Waiting " + waitForMac + " sec");
#endif
                    Thread.Sleep(waitForMac * 1000);
                }

                // Initialize System Global
                SystemGlobal.Initialize(SystemGlobal.NodeTypes.Client);

                // Set up the local manager
                //LocalServer.Initialize(macBase, Lcd, SensorNodeGlobal.PinDefs.EnactResetPort);
                //LocalServer.Initialize(macBase, null);

                // Initialize shared vars
                VersionInfo.Initialize(Assembly.GetExecutingAssembly());

                // Set the app version
                //LocalManagerGlobal.Shared.SharedVars.ProgramVersion = VersionInfo.AppVersion;

                //Initialize routing
                var routing = new Routing(macBase, null, 1);

                // Allow additional sleep to "time-shift" routing and heartbeats (NetManager)
	            const int additionalSleep = 60;
#if !DBG_LOGIC
				Debug.Print("Additional sleep to \"time-shift\" routing and heartbeats (NetManager)");
#endif
                Thread.Sleep(additionalSleep * 1000);

                // Initialize application message handler
                AppMsgHandler.Initialize(macBase, _lcd, serialComm, SendPacketInterval);

                // Initialize the Net Manager
                NetManager.Initialize(macBase);

                // Initialize the Neighborhood Manager
                NeighborInfoManager.Initialize(macBase);

                // Sleep forever
                Thread.Sleep(Timeout.Infinite);
            }

            catch (Exception ex)
            {
                Debug.Print("System exception " + ex);
            }
        }
    }
}
