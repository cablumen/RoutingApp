//#define FastBeacons	// comment out for normal use

//#define DBG_VERBOSE
#if BASE_STATION
#define DBG_SIMPLE
// Base Station
#else
//#define DBG_LOGIC
#define DBG_SIMPLE
// Not base station
#endif

// Ensure exactly one defined
#if !DBG_VERBOSE && !DBG_SIMPLE && !DBG_LOGIC
#error Exactly one of DBG_VERBOSE, DBG_SIMPLE, DBG_LOGIC must be defined.
#endif
#if  DBG_VERBOSE && (DBG_SIMPLE || DBG_LOGIC) || DBG_SIMPLE && (DBG_VERBOSE || DBG_LOGIC) || DBG_LOGIC && (DBG_VERBOSE || DBG_SIMPLE)
#error Exactly one of DBG_VERBOSE, DBG_SIMPLE, DBG_LOGIC must be defined.
#endif

#if BASE_STATION
// Base node
#elif RELAY_NODE
// Relay node
#elif CLIENT_NODE
// Client node
#elif FENCE_NODE
// Fence node
#elif FAKE_FENCE
// Fake fence node
#else
#error Invalid node type. Valid options: BASE_STATION, RELAY_NODE, CLIENT_NODE, FENCE_NODE, FAKE_FENCE
#endif

using System;
using System.Threading;
using Microsoft.SPOT;
using Samraksh.Components.Utility;
using Samraksh.eMote.Net;
using Samraksh.eMote.Net.MAC;

namespace Samraksh.VirtualFence.Components
{
    /// <summary>
    /// Provides routing services between motes. 
    /// Uses a modified Bellman-Ford routing protocol to form a least-cost spanning tree.
    /// </summary>
    public class Routing
    {
        private readonly MACBase _macBase;
        private readonly MACPipe _routingPipe;
        private readonly ushort[] _neighborList;

        // ReSharper disable once NotAccessedField.Local
        private EnhancedEmoteLCD _lcd;
        private int _beaconNum;
        // ReSharper disable once NotAccessedField.Local
        private Timer _beaconTimer;

        // Added by Dhrubo to design special actions for first RNP window
        public static bool _isFirstWindowOver = false;

        /// <summary>
        /// Address of node
        /// </summary>
        public static ushort SelfAddress { get; private set; }

        #region unused
        //#pragma warning disable 414
        //        private int _dataNum;
        //#pragma warning restore 414 
        #endregion

        public Routing(MACBase macBase, EnhancedEmoteLCD lcd, int minEtx)
        {
            //_routingPipe = routingPipe;
#if !DBG_LOGIC
#if RELAY_NODE || CLIENT_NODE
            Debug.Print("Initializing routing on Relay Node");
#elif BASE_STATION
			Debug.Print("Initializing routing on Base Station");
#endif
#endif
            _macBase = macBase;
            _lcd = lcd;
            RoutingGlobal._minEtx = minEtx;
            _neighborList = MACBase.NeighborListArray();

            try
            {
                _routingPipe = new MACPipe(macBase, SystemGlobal.MacPipeIds.Routing);

#if BASE_STATION
                _routingPipe.OnReceive += BaseRoutingReceive;
                //_routingPipe.OnSendStatus += OnSendStatus;
#if !DBG_LOGIC
                Debug.Print("***** Subscribing to Routing on " + SystemGlobal.MacPipeIds.Routing + " with base receive print enabled.");
#endif
                SelfAddress = _macBase.MACRadioObj.RadioAddress;
				RoutingGlobal.Parent = SelfAddress;
                RoutingGlobal.BestEtx = 0;
                RoutingGlobal._parentLinkRSSI = 0;
                RoutingGlobal.path_ewrnp = 0;

				//lcd.Write("Base");

#endif
#if RELAY_NODE || CLIENT_NODE
                _routingPipe.OnReceive += RelayNodeReceive;
                _routingPipe.OnSendStatus += OnSendStatus;
#if !DBG_LOGIC
                Debug.Print("***** Subscribing to Routing on " + SystemGlobal.MacPipeIds.Routing);
#endif
                SelfAddress = _macBase.MACRadioObj.RadioAddress;
                RoutingGlobal.Parent = SystemGlobal.NoParent;
                RoutingGlobal.BestEtx = RoutingGlobal.MaxEtx;
                RoutingGlobal._parentLinkRSSI = 0;
                RoutingGlobal.path_ewrnp = byte.MaxValue;
                RoutingGlobal.ResetParentLinkRNP();

                //Set up Distributed Reset
                DistributedReset.Initialize(_macBase);


                // Initialize candidate table
#if !DBG_LOGIC
                Debug.Print("\nInitializing Candidate Table...");
#endif
                CandidateTable.Initialize((byte)_neighborList.Length);
#endif

                Send_Beacon(0);
#if FastBeacons
				_beaconTimer = new Timer(Send_Beacon, null, 60 * 1000, 10 * 1000);
#else
                _beaconTimer = new Timer(Send_Beacon, null, 300 * 1000, 300 * 1000);
#endif
            }
            catch
            {
                //Lcd.Display("Err");
                //Thread.Sleep(Timeout.Infinite);
            }
        }

        private void OnSendStatus(IMAC macInstance, DateTime time, SendPacketStatus ACKStatus, uint transmitDestination, ushort index)
        {
            var pipe = macInstance as MACPipe;
            switch (ACKStatus)
            {
                case SendPacketStatus.SendACKed:
#if !DBG_LOGIC
                    Debug.Print("Beacon to " + transmitDestination.ToString() + " ACKed");
#endif
                    // Update link metrics
                    if ((ushort)transmitDestination == RoutingGlobal.Parent)
                    {
                        RoutingGlobal.UpdateNumReceivedInCurrentWindow_Parent(1);
#if !DBG_LOGIC
                        Debug.Print("Updated numReceivedInCurrentWindow for parent " + transmitDestination + "; new value = " + RoutingGlobal.GetNumReceivedInCurrentWindow_Parent());
#endif
                    }
                    else
                    {
                        byte cindex = CandidateTable.findIndex((ushort)transmitDestination);
                        if (cindex < byte.MaxValue)
                        {
                            CandidateTable._candidateList[cindex].UpdateNumReceivedInCurrentWindow(1);
#if !DBG_LOGIC
                            Debug.Print("Updated numReceivedInCurrentWindow for candidate " + transmitDestination + "; new value = " + CandidateTable._candidateList[cindex].GetNumReceivedInCurrentWindow());
#endif
                        }
                    }
                    break;

                case SendPacketStatus.SendNACKed:
#if !DBG_LOGIC
                    Debug.Print("Beacon to " + transmitDestination.ToString() + " NACKed");
#endif
                    // Update link metrics
                    if ((ushort)transmitDestination == RoutingGlobal.Parent)
                    {
                        RoutingGlobal.UpdateNumTriesInCurrentWindow_Parent(1);
#if !DBG_LOGIC
                        Debug.Print("Updated numTriesInCurrentWindow for parent " + transmitDestination + "; new value = " + RoutingGlobal.GetNumTriesInCurrentWindow_Parent());
#endif
                    }
                    else
                    {
                        byte cindex = CandidateTable.findIndex((ushort)transmitDestination);
                        if (cindex < byte.MaxValue)
                        {
                            CandidateTable._candidateList[cindex].UpdateNumTriesInCurrentWindow(1);
#if !DBG_LOGIC
                            Debug.Print("Updated numTriesInCurrentWindow for candidate " + transmitDestination + "; new value = " + CandidateTable._candidateList[cindex].GetNumTriesInCurrentWindow());
#endif
                        }
                    }
                    break;

                case SendPacketStatus.SendFailed:
#if !DBG_LOGIC
                    Debug.Print("Beacon to " + transmitDestination.ToString() + " failed");
#endif
                    // Update link metrics
                    if ((ushort)transmitDestination == RoutingGlobal.Parent)
                    {
                        RoutingGlobal.UpdateNumTriesInCurrentWindow_Parent(1);
#if !DBG_LOGIC
                        Debug.Print("Updated numTriesInCurrentWindow for parent " + transmitDestination + "; new value = " + RoutingGlobal.GetNumTriesInCurrentWindow_Parent());
#endif
                    }
                    else
                    {
                        byte cindex = CandidateTable.findIndex((ushort)transmitDestination);
                        if (cindex < byte.MaxValue)
                        {
                            CandidateTable._candidateList[cindex].UpdateNumTriesInCurrentWindow(1);
#if !DBG_LOGIC
                            Debug.Print("Updated numTriesInCurrentWindow for candidate " + transmitDestination + "; new value = " + CandidateTable._candidateList[cindex].GetNumTriesInCurrentWindow());
#endif
                        }
                    }
                    break;

                default:
                    break;
            }
        }

        public Routing(MACBase macBase, EnhancedEmoteLCD lcd)
        {
            //_routingPipe = routingPipe;
#if !DBG_LOGIC
#if RELAY_NODE || CLIENT_NODE
            Debug.Print("Initializing routing on Relay Node");
#elif BASE_STATION
			Debug.Print("Initializing routing on Base Station");
#endif
#endif
            _macBase = macBase;
            RoutingGlobal._minEtx = 1;
            _lcd = lcd;
            _neighborList = MACBase.NeighborListArray();

            try
            {
                _routingPipe = new MACPipe(macBase, SystemGlobal.MacPipeIds.Routing);

#if BASE_STATION
                _routingPipe.OnReceive += BaseRoutingReceive;
                //_routingPipe.OnSendStatus += OnSendStatus;
#if !DBG_LOGIC
                Debug.Print("***** Subscribing to Routing on " + SystemGlobal.MacPipeIds.Routing + " with base receive print enabled.");
#endif
                
                SelfAddress = _macBase.MACRadioObj.RadioAddress;
                RoutingGlobal._color = Color.Green;
				RoutingGlobal.Parent = SelfAddress;
                RoutingGlobal.BestEtx = 0;
                RoutingGlobal._parentLinkRSSI = 0;
                RoutingGlobal.path_ewrnp = 0;

				//lcd.Write("Base");

#endif
#if RELAY_NODE || CLIENT_NODE
                _routingPipe.OnReceive += RelayNodeReceive;
                _routingPipe.OnSendStatus += OnSendStatus;
#if !DBG_LOGIC
                Debug.Print("***** Subscribing to Routing on " + SystemGlobal.MacPipeIds.Routing);
#endif
                SelfAddress = _macBase.MACRadioObj.RadioAddress;
                RoutingGlobal.Parent = SystemGlobal.NoParent;
                RoutingGlobal.BestEtx = RoutingGlobal.MaxEtx;
                RoutingGlobal._parentLinkRSSI = 0;
                RoutingGlobal.path_ewrnp = byte.MaxValue;
                RoutingGlobal.ResetParentLinkRNP();

                //Set up Distributed Reset
                DistributedReset.Initialize(_macBase);


                // Initialize candidate table
                Debug.Print("\nInitializing Candidate Table...");
                CandidateTable.Initialize((byte)_neighborList.Length);
#endif

                Send_Beacon(0);
#if FastBeacons
				_beaconTimer = new Timer(Send_Beacon, null, 60 * 1000, 10 * 1000);
#else
                _beaconTimer = new Timer(Send_Beacon, null, 300 * 1000, 300 * 1000);
#endif
            }
            catch
            {
                //Lcd.Display("Err");
                //Thread.Sleep(Timeout.Infinite);
            }
        }
        #region unused
#if RELAY_NODE || CLIENT_NODE
        ///// <summary>
        ///// Send to parent on designated channel
        ///// </summary>
        ///// <param name="macPipe"></param>
        ///// <param name="message"></param>
        //public void SendToParent(MACPipe macPipe, byte[] message)
        //{
        //	if (RoutingGlobal.Parent == SystemGlobal.NoParent)
        //	{
        //		return;
        //	}
        //	////var status = macPipe.Send(Parent, message, 0, (ushort)message.Length);
        //	////if (status != NetOpStatus.S_Success)
        //	////{
        //	////	Debug.Print("~~~~~~~~~~~~~~~~~~~~~~~~~~~~~ Send status: " + status);
        //	////}
        //	////SystemGlobal.PrintNumericVals("Routing Snd: ", message);
        //}
#endif
        #endregion

        //Reimplemented with change: unconditional beaconing even if you do not have a parent
        private void Send_Beacon(object state)
        {
#if !DBG_LOGIC
            Debug.Print("Send_Beacon: memory usage (before) " + Debug.GC(true));
#endif
            //ushort[] neighbors = _csmaRadio._csma.GetNeighborList();
            if (RoutingGlobal.Parent != SelfAddress) // Not for base
            {
                Debug.Print("Relay: SelfAddress=" + SelfAddress + ", RoutingGlobal.Parent=" + RoutingGlobal.Parent);
                // If in a reset wave, return immediately
                if (RoutingGlobal._color == Color.Red)
                {
#if DBG_VERBOSE
                    Debug.Print("!!!! IN A RESET WAVE: Exiting... !!!!");
#elif DBG_SIMPLE
                    Debug.Print("In a reset wave.");
#endif
#if !DBG_LOGIC
                    Debug.Print("Send_Beacon: memory usage (after) " + Debug.GC(true) + "\n");
#endif
                    return;
                }

                // Check if candidate table has valid entries
                RoutingGlobal.CleanseCandidateTable(_routingPipe);

                // If parent no longer exists, initiate Distributed Reset
                _macBase.NeighborList(_neighborList);
                if (RoutingGlobal.IsParent && Array.IndexOf(_neighborList, RoutingGlobal.Parent) == -1)
                {
#if DBG_VERBOSE
                    Debug.Print("### Removing stale parent: " + RoutingGlobal.Parent + " ###");
#elif DBG_SIMPLE
                    Debug.Print("Stale parent reset: " + RoutingGlobal.Parent);
#endif

                    RoutingGlobal.Parent = SystemGlobal.NoParent;
                    RoutingGlobal.BestEtx = RoutingGlobal.MaxEtx;
                    RoutingGlobal._parentLinkRSSI = 0;
                    RoutingGlobal.path_ewrnp = byte.MaxValue;
                    RoutingGlobal.ResetParentLinkRNP();
#if DBG_VERBOSE
                    Debug.Print("@@@ Initiating Reset " + DistributedReset.getResetMessageNum() +" @@@");
#elif DBG_SIMPLE
                    Debug.Print("Initiating Reset " + DistributedReset.getResetMessageNum());
#endif
                    DistributedReset.Send_ResetWave();

#if !DBG_LOGIC
                    Debug.Print("Send_Beacon: memory usage (after) " + Debug.GC(true) + "\n");
#endif
                    return;
                }

                // Check if first hour has passed
                if ((_beaconNum > 0) && (_beaconNum % RoutingGlobal.windowLength == 0))
                {
                    if (!_isFirstWindowOver)
                    {
#if !DBG_LOGIC
                        Debug.Print("The very first window has ended. Electing parent...");
#endif
                        _isFirstWindowOver = true;
                    }
                    else
                    {
#if !DBG_LOGIC
                        Debug.Print("End of a window. Electing parent...");
#endif
                    }

                    // Parent election
                    RoutingGlobal.SetParent(true);

                    if(RoutingGlobal.HadParent && RoutingGlobal.ExParent != RoutingGlobal.Parent) {
                        // Send "Dop Parent" message to new parent
                        DistributedReset.Send_DropParent();
                    }

                    if (RoutingGlobal.IsParent && RoutingGlobal.ExParent != RoutingGlobal.Parent)
                    {
                        // Send "Add Parent" message to new parent
                        DistributedReset.Send_AddParent();
                    }
                }

                // Parent election for very first window, done every beacon interval
                if (!_isFirstWindowOver)
                {
#if !DBG_LOGIC
                    Debug.Print("In first window. Setting parent...");
#endif
                    RoutingGlobal.SetParent(false);

                    if (RoutingGlobal.HadParent && RoutingGlobal.ExParent != RoutingGlobal.Parent)
                    {
                        // Send "Dop Parent" message to new parent
                        DistributedReset.Send_DropParent();
                    }

                    if (RoutingGlobal.IsParent && RoutingGlobal.ExParent != RoutingGlobal.Parent)
                    {
                        // Send "Add Parent" message to new parent
                        DistributedReset.Send_AddParent();
                    }
                }
            }

            _beaconNum++;
            var msgBytes = new byte[7];
            var size = RoutingGlobal.ComposeMessages.CreateBeacon(msgBytes, RoutingGlobal.BestEtx, (ushort)_beaconNum, RoutingGlobal.GetPathEWRNP(), RoutingGlobal.Parent);
            //Debug.Print("\t\tmsgBytes size reported: " + size);
#if RELAY_NODE || CLIENT_NODE
            RoutingGlobal.BroadcastBeacon(_routingPipe, msgBytes, size); // Tracks link metrics
#elif BASE_STATION
            SystemGlobal.Broadcast(_routingPipe, msgBytes, size);
#endif

#if DBG_VERBOSE
            DebuggingSupport.PrintSelfAndParentAddress(_routingPipe.MACRadioObj.RadioAddress, RoutingGlobal.Parent);
            DebuggingSupport.PrintMessageSent(_routingPipe, "(Broadcast) Routing Beacon. BestEtx: " + RoutingGlobal.BestEtx + ", Beacon #: " + _beaconNum);
#endif
#if !DBG_LOGIC
            Debug.Print("Send_Beacon: memory usage (after) " + Debug.GC(true) + "\n");
#endif
        }

#if BASE_STATION
        private void BaseRoutingReceive(IMAC macBase, DateTime dateTime, Packet packet)
        {
            try
            {
#if DBG_VERBOSE
				DebuggingSupport.PrintMessageReceived(macBase, "Base Routing");
                Debug.Print("\tRSSI: " + packet.RSSI + ", from neighbor " + packet.Src);
                var rcvPayloadBytes = packet.Payload;
                SystemGlobal.PrintNumericVals("Base Rcv: ", rcvPayloadBytes);
#elif DBG_SIMPLE
                Debug.Print("Received Beacon with RSSI: " + packet.RSSI + ", from neighbor " + packet.Src);

#endif
        #region unused
                //Debug.Print("\tRSSI: " + packet.RSSI + ", from neighbor " + packet.Src);

                //Debug.Print("\ton " + packet.PayloadType);
                //var rcvPayloadBytes = packet.Payload;
                //var rcvPayloadBytes = SystemGlobal.GetTrimmedPayload(packet);
                //var rcvPayloadBytes = packet.Payload;
                //var rcvPayloadChar = Encoding.UTF8.GetChars(rcvPayloadBytes);

                //SystemGlobal.PrintNumericVals("Base Rcv: ", rcvPayloadBytes);
                //Debug.Print("\t" + new string(rcvPayloadChar));

                //Debug.Print("Received " + (rcvMsg.Unicast ? "Unicast" : "Broadcast") + " message from src: " + rcvMsg.Src + ", size: " + rcvMsg.Size + ", rssi: " + rcvMsg.RSSI + ", lqi: " + rcvMsg.LQI);
                //Debug.Print("   Payload: [" + new string(rcvPayloadChar) + "]");
                //Debug.Print(new string(rcvPayloadChar));
                //Lcd.Display(count_rec++);

        #region No need to send beacon to PC
                //switch ((RoutingGlobal.MessageIds)rcvPayloadBytes[0])
                //{
                //	case RoutingGlobal.MessageIds.Beacon:
                //		byte etx;
                //		ushort beaconNum;
                //		RoutingGlobal.ParseMessages.ParseBeacon(rcvPayloadBytes, out etx, out beaconNum);
                //		Debug.Print("\t>>Beacon. " + " From neighbor " + packet.Src + ", Etx: " + etx + ", Beacon Num: " + beaconNum);
                //		Debug.Print("^^^^^^^^^^^^^^^^^^ (Need to send to PC) ^^^^^^^^^^^^^^^^^^");	//todo

                //		break;
                //	case RoutingGlobal.MessageIds.Hello:
                //		Debug.Print("\t>>Hello");
                //		Debug.Print("^^^^^^^^^^^^^^^^^^ (Need to send to PC) ^^^^^^^^^^^^^^^^^^");	//todo
                //		break;
                //	default:
                //		Debug.Print("\tUnknown message received <" + rcvPayloadBytes[0] + ">");
                //		Debug.Print("^^^^^^^^^^^^^^^^^^ (Need to send to PC) ^^^^^^^^^^^^^^^^^^");	//todo
                //		break;
                //}
        #endregion

                //var payload = new string(rcvPayloadChar);
                //if (payload.Substring(0, 5).Equals("Alive")) //Relay beacons
                //{
                //	var etx = int.Parse(payload.Substring(payload.IndexOf('|') + 1));
                //	Debug.Print("\tReceived beacon# " + payload.Substring(5, payload.IndexOf('|') - 5) + ", ETX: " + etx + " from Node " + packet.Src);
                //}
                //else if (payload.Substring(0, 5).Equals("Hello")) //Dummy packets sent by link/path test nodes
                //	Debug.Print("\tReceived dummy packet: " + payload.Substring(0, 5) + "; count: " + payload.Substring(5) + "; from Node: " + packet.Src);
                //else if (payload.Substring(0, 9).Equals("Non-human")) //Data packets--non-human
                //	Debug.Print("\tReceived decision: " + payload.Substring(0, 9) + "; source: " + payload.Substring(9) + "; from Node: " + packet.Src); 
        #endregion
            }

            catch (Exception e)
            {
                Debug.Print(e.ToString());
            }
        }
#endif

        #region unused
        //public void Send_Data(string data, bool increaseSeqNum)
        //{
        //	Debug.Print("Entering Send_Data: memory usage (before)" + Debug.GC(true));
        //	if (!RoutingGlobal.IsParent)
        //	{
        //		Debug.Print("No parent; not sending data."); // or maybe broadcast?
        //		return;
        //	}

        //	var msgBytes = new byte[4];
        //	var size = RoutingGlobal.ComposeMessages.CreateData(msgBytes, data, (ushort)_dataNum);
        //	var status = _routingPipe.Send(RoutingGlobal.Parent, msgBytes, 0, (ushort)size);
        //	if (status != NetOpStatus.S_Success)
        //	{
        //		Debug.Print("~~~~~~~~~~~~~~~~~~~~~~~~~~~~~ Send status: " + status);
        //	}

        //	DebuggingSupport.PrintSelfAndParentAddress(_routingPipe.MACRadioObj.RadioAddress, RoutingGlobal.Parent);
        //	DebuggingSupport.PrintMessageSent(_routingPipe, "(Unicast) Data. Message: " + data + ", Packet #: " + _dataNum);

        //	if (increaseSeqNum)
        //		_dataNum++;
        //	Debug.Print("Entering Send_Data: memory usage (after)" + Debug.GC(true));
        //} 
        #endregion

        private static ushort[] _changeNeighborList;
        public static void Routing_OnNeighborChange(IMAC macInstance, DateTime time)
        {
            if (_changeNeighborList == null) { _changeNeighborList = MACBase.NeighborListArray(); }
//#if !DBG_LOGIC
            SystemGlobal.PrintNeighborList("Old neighbor list: ", _changeNeighborList);
//#endif
            macInstance.NeighborList(_changeNeighborList);
//#if !DBG_LOGIC
            SystemGlobal.PrintNeighborList("New neighbor list: ", _changeNeighborList);
//#endif
            //If not the base and lost parent, initiate Distributed Reset (if not in one already)
            if (DistributedReset.IsInitialized)
            {
                if (RoutingGlobal.IsParent && RoutingGlobal.Parent != SelfAddress && Array.IndexOf(_changeNeighborList, RoutingGlobal.Parent) == -1 && RoutingGlobal._color == Color.Green)
                {
#if DBG_VERBOSE
                    Debug.Print("### Removing stale parent: " + RoutingGlobal.Parent + " ###");
#elif DBG_SIMPLE
                    Debug.Print("Stale parent reset: " + RoutingGlobal.Parent);
#endif
                    RoutingGlobal.Parent = SystemGlobal.NoParent;
                    RoutingGlobal.BestEtx = RoutingGlobal.MaxEtx;
                    RoutingGlobal._parentLinkRSSI = 0;
                    RoutingGlobal.path_ewrnp = byte.MaxValue;
                    RoutingGlobal.ResetParentLinkRNP();

#if DBG_VERBOSE
                    Debug.Print("@@@ Initiating Reset " + DistributedReset.getResetMessageNum() + " @@@");
#elif DBG_SIMPLE
                    Debug.Print("Initiating Reset " + DistributedReset.getResetMessageNum());
#endif
                    DistributedReset.Send_ResetWave();
                }
            }
        }

        /*private void OnSendStatus(IMAC macBase, DateTime dateTime, SendPacketStatus sendPacketStatus, uint dest)
        {
            Debug.Print("*** Received MAC Send Status " + dateTime + ", send packet status " + sendPacketStatus + " to " + dest);
        }*/

#if RELAY_NODE || CLIENT_NODE
        /// <summary>
        /// 
        /// </summary>
        /// <param name="macBase"></param>
        /// <param name="dateTime"></param>
        /// <param name="packet"></param>
        private void RelayNodeReceive(IMAC macBase, DateTime dateTime, Packet packet)
        {
#if !DBG_LOGIC
            Debug.Print("RelayNodeReceive: memory usage (before) " + Debug.GC(true));
#endif
#if DBG_VERBOSE
            DebuggingSupport.PrintMessageReceived(macBase, "Relay Node Routing");
#endif

            // var packet = macBase.NextPacket();
            if (packet == null)
            {
                return;
            }
//#if !DBG_LOGIC
            Debug.Print("\tRSSI: " + packet.RSSI + ", from " + packet.Src);
//#endif
            var rcvPayloadBytes = packet.Payload;
            //var rcvPayloadBytes = new byte[packet.Size];
            //Array.Copy(rcvPayloadBytesAll, rcvPayloadBytes, packet.Size);

            //var rcvPayloadChar = Encoding.UTF8.GetChars(rcvPayloadBytes);

#if DBG_VERBOSE
            SystemGlobal.PrintNumericVals("\tRelay Rcv: ", rcvPayloadBytes, packet.Size);
#endif

            try
            {
                byte index = CandidateTable.findIndex(packet.Src);
                ushort[] neighbors = MACBase.NeighborListArray();
                _macBase.NeighborList(neighbors); // Step 1: check if source is in the (filtered) neighbor list

                if (Array.IndexOf(neighbors, packet.Src) == -1)
                {
#if DBG_VERBOSE
                    Debug.Print("\t\t!!!!!!!!!!!!!!!!! Node " + packet.Src + " is not a Neighbor (MACBase.NeighborStatus returned null)");
#elif DBG_SIMPLE
                    Debug.Print("Sender not a neighbor.");
#endif
                    // Drop candidate if it exists in the table
                    if (index < byte.MaxValue)
                    {
                        if (CandidateTable.DropCandidate(packet.Src))
                        {
#if DBG_VERBOSE
                            Debug.Print("--- Dropped stale candidate: " + packet.Src + " ---");
#elif DBG_SIMPLE
                            Debug.Print("Dropped candidate " + packet.Src);
#endif
                        }
                    }
                    else if (RoutingGlobal.Parent == packet.Src)// Drop parent and initiate Distributed Reset
                    {
#if DBG_VERBOSE
                        Debug.Print("--- Dropping parent: " + RoutingGlobal.Parent + " ---");
#elif DBG_SIMPLE
                        Debug.Print("Dropping parent: " + RoutingGlobal.Parent);
#endif
                        RoutingGlobal.Parent = SystemGlobal.NoParent;
                        RoutingGlobal.BestEtx = RoutingGlobal.MaxEtx;
                        RoutingGlobal._parentLinkRSSI = 0;
                        RoutingGlobal.path_ewrnp = byte.MaxValue;
                        RoutingGlobal.ResetParentLinkRNP();
#if DBG_VERBOSE
                        Debug.Print("@@@ Initiating Reset " + DistributedReset.getResetMessageNum() + " @@@");
#elif DBG_SIMPLE
                        Debug.Print("Initiating Reset " + DistributedReset.getResetMessageNum());
#endif
                        DistributedReset.Send_ResetWave();
                    }
#if !DBG_LOGIC
                    Debug.Print("RelayNodeReceive: memory usage (after) " + Debug.GC(true));
#endif
                    return;
                }

                var neighborStatus = _macBase.NeighborStatus(packet.Src); // Step 2: check the status of the node and get RSSIs etc.

#if DBG_VERBOSE
                Debug.Print("\t\tFwd avg RSSI: " + neighborStatus.ReceiveLink.AverageRSSI + ", Rev avg RSSI: " + neighborStatus.SendLink.AverageRSSI);
#endif
                #region unused
                //Debug.Print("Received " + (rcvMsg.Unicast ? "Unicast" : "BroadcastBeacon") + " message from src: " + rcvMsg.Src + ", size: " + rcvMsg.Size + ", rssi: " + rcvMsg.RSSI + ", lqi: " + rcvMsg.LQI);
                //Debug.Print("   Payload: [" + new string(rcvPayloadChar) + "]");
                //Debug.Print(new string(rcvPayloadChar));
                //Lcd.Display(count_rec++);

                //var payload = new string(rcvPayloadChar);

                //Debug.Print(">>> " + payload); 
                #endregion

                switch ((RoutingGlobal.MessageIds)rcvPayloadBytes[0])
                {
                    case RoutingGlobal.MessageIds.Beacon:
#if !DBG_LOGIC
                        Debug.Print("\t>>> Beacon");
#endif
                        // Exit immediately if in a reset wave
                        if (RoutingGlobal._color == Color.Red)
                        {
#if DBG_VERBOSE
                            Debug.Print("!!!! IN A RESET WAVE: Exiting... !!!!");
#elif DBG_SIMPLE
                            Debug.Print("In a reset wave.");
#endif
#if !DBG_LOGIC
                            Debug.Print("RelayNodeReceive: memory usage (after) " + Debug.GC(true));
#endif
                            return;
                        }
                        // Path offered by sender
                        byte senderEtx;
                        // Beacon number
                        ushort beaconNum;
                        byte pathEWRNP_B2N;
                        ushort senderparent;
                        //Debug.Print("\t>>> "+ Samraksh.Components.Utility.Convert.ByteArrayToDecString(rcvPayloadBytes));

                        RoutingGlobal.ParseMessages.ParseBeacon(rcvPayloadBytes, out senderEtx, out beaconNum, out pathEWRNP_B2N, out senderparent);

                        #region unused
                        Debug.Print("\tBeacon Parse Results. SenderEtx: " + senderEtx + ", beaconNum: " + beaconNum);

                        ////Calculate path length (etx) offered by the sender
                        //int etx = short.Parse(payload.Substring(payload.IndexOf('|') + 1));
                        ////Debug.Print("\t>>> etx " + etx);

                        //var beaconNum = payload.Substring(5, payload.IndexOf('|') - 5);
                        ////Debug.Print("\t\tReceived beacon # " + beaconNum + ", ETX: " + etx + " from Node " + packet.Src); 
                        #endregion

                        // If the sender is adopted as a parent then the new path length will be one more
                        var tempEtx = (byte)(senderEtx + 1);
#if !DBG_LOGIC
                        Debug.Print("\t::: Best Etx: " + RoutingGlobal.BestEtx + ", Sender Etx + 1: " + tempEtx + ", Parent: " + senderparent);
#endif
                        // If offered Etx is less than _minEtx, exit
                        if (tempEtx < RoutingGlobal._minEtx)
                        {
#if DBG_VERBOSE
                            Debug.Print("!!!! Offered Etx " + tempEtx + " less than min Etx " + RoutingGlobal._minEtx + ". Exiting... !!!!");
#elif DBG_SIMPLE
                            Debug.Print("Offered Etx " + tempEtx + "<" + RoutingGlobal._minEtx);
#endif
#if !DBG_LOGIC
                            Debug.Print("RelayNodeReceive: memory usage (after) " + Debug.GC(true));
#endif
                            return;
                        }

                        // If I am the parent but sender is not in my children table, add the node
                        if (senderparent == SelfAddress && DistributedReset.findChild(packet.Src) == byte.MaxValue)
                        {
                            var retVal = DistributedReset.AddChild(packet.Src);
#if !DBG_LOGIC
                            Debug.Print("Adding node to children table; status: " + retVal);
#endif
                        }

                        // If I'm not the parent but sender is in my children table, drop the node
                        if (senderparent != SelfAddress && DistributedReset.findChild(packet.Src) < byte.MaxValue)
                        {
                            var retVal = DistributedReset.DropChild(packet.Src);
#if !DBG_LOGIC
                            Debug.Print("Dropping node from children table; status: " + retVal);
#endif
                        }

                        //double rssi = 0.5 * (neighborStatus.ReceiveLink.AverageRSSI + neighborStatus.SendLink.AverageRSSI);
                        double rssi = neighborStatus.ReceiveLink.AverageRSSI;

                        // If the sender is current parent, update path cost (etx) and link RSSI
                        if (RoutingGlobal.Parent == packet.Src)
                        {
                            RoutingGlobal.BestEtx = tempEtx;
                            RoutingGlobal._parentLinkRSSI = (int)rssi;
                            RoutingGlobal.UpdatePathEWRNP_Parent(pathEWRNP_B2N);
#if DBG_VERBOSE
                            Debug.Print("### Updated metrics for parent: " + RoutingGlobal.Parent + "; path length (unchanged): " + RoutingGlobal.BestEtx + "; link RSSI: " + RoutingGlobal._parentLinkRSSI + " ###");
#elif DBG_SIMPLE
                            Debug.Print("Updating metrics for parent " + RoutingGlobal.Parent + ":[" + RoutingGlobal.BestEtx + "," + RoutingGlobal._parentLinkRSSI + "," + pathEWRNP_B2N + "]");
#endif
                            // If cycle detected, initiate Distributed Reset
                            if (RoutingGlobal.BestEtx >= RoutingGlobal.Infinity)
                            {
#if DBG_VERBOSE
                                Debug.Print("!!! Cycle detected: " + RoutingGlobal.Parent + " !!!");
#elif DBG_SIMPLE
                                Debug.Print("Cycle detected: " + RoutingGlobal.Parent);
#endif
                                RoutingGlobal.Parent = SystemGlobal.NoParent;
                                RoutingGlobal.BestEtx = RoutingGlobal.MaxEtx;
                                RoutingGlobal._parentLinkRSSI = 0;
                                RoutingGlobal.path_ewrnp = byte.MaxValue;
                                RoutingGlobal.ResetParentLinkRNP();
#if DBG_VERBOSE
                                Debug.Print("@@@ Initiating Reset " + DistributedReset.getResetMessageNum() + " @@@");
#elif DBG_SIMPLE
                                Debug.Print("Initiating Reset " + DistributedReset.getResetMessageNum());
#endif
                                DistributedReset.Send_ResetWave();
#if !DBG_LOGIC
                                Debug.Print("RelayNodeReceive: memory usage (after) " + Debug.GC(true));
#endif
                            }
                            return;
                        }

                        // Else, add to/update the candidate table. SetParent() in Send_Beacon(...) should take care of parent setting
                        if (index == byte.MaxValue)
                        {
                            // Add to candidate table if it does not exist and has a valid path, and is not already a child
                            if (tempEtx < RoutingGlobal.Infinity && DistributedReset.findChild(packet.Src) == byte.MaxValue)
                            {
                                CandidateTable.AddCandidate(packet.Src, (int)rssi, tempEtx, pathEWRNP_B2N, RoutingGlobal.MaxEtx);
#if DBG_VERBOSE
                                Debug.Print("+++ Added new candidate: " + packet.Src + "; path length: " + tempEtx + "; link RSSI: " + rssi + " +++");
#elif DBG_SIMPLE
                                Debug.Print("New candidate " + packet.Src + ":[" + tempEtx + "," + rssi + "," + pathEWRNP_B2N + "]");
#endif
                            }
                            else
                            {
#if DBG_VERBOSE
                                Debug.Print("--- Not a candidate: " + packet.Src + "; path length: " + tempEtx + "; link RSSI: " + rssi + " ---");
#elif DBG_SIMPLE
                                Debug.Print("Not a candidate " + packet.Src + ":[" + tempEtx + "," + rssi + "]");
#endif
                            }
                        }
                        else
                        {
                            // Drop candidate (if exists) if it's in the children table
                            if (DistributedReset.findChild(packet.Src) < byte.MaxValue)
                            {
#if DBG_VERBOSE
                                Debug.Print("!!! INVALID CANDIDATE: Removing child " + packet.Src + " from the candidate table !!!");
#elif DBG_SIMPLE
                                Debug.Print("Removing child " + packet.Src + " from candidate table");
#endif
                                CandidateTable.DropCandidate(packet.Src);

#if !DBG_LOGIC
                                Debug.Print("RelayNodeReceive: memory usage (after) " + Debug.GC(true));
#endif
                                return;
                            }

                            // Drop candidate if its metric exceeds Infinity
                            if (tempEtx >= RoutingGlobal.Infinity)
                            {
#if DBG_VERBOSE
                                Debug.Print("!!! INVALID CANDIDATE: Removing " + packet.Src + " with invalid metrics from the candidate table !!!");
#elif DBG_SIMPLE
                                Debug.Print("Removing " + packet.Src + "with invalid metrics from candidate table");
#endif
                                CandidateTable.DropCandidate(packet.Src);
#if !DBG_LOGIC
                                Debug.Print("RelayNodeReceive: memory usage (after) " + Debug.GC(true));
#endif
                                return;
                            }

                            // Update metrics regardless of validity of path
                            CandidateTable._candidateList[index].UpdateMetrics((int)rssi, tempEtx, pathEWRNP_B2N);
#if DBG_VERBOSE
                            Debug.Print("### Updated metrics for candidate: " + packet.Src + "; path length: " + tempEtx + "; link RSSI: " + rssi + " ###");
#elif DBG_SIMPLE
                            Debug.Print("Updating metrics for " + packet.Src + ":[" + tempEtx + "," + rssi + "," + pathEWRNP_B2N + "]");
#endif
                        }
                        #region old protocol (unused)
                        //                        //Select sender as parent if it offers a lower path length than the current best
                        //                        //###################################################################################################
                        //                        //if (tempEtx < RoutingGlobal.BestEtx && tempEtx != 1)	// DEMO: Fence node only
                        //                        if (tempEtx < RoutingGlobal.BestEtx)	// Use for all other cases
                        //                        //if ((tempEtx < RoutingGlobal.BestEtx) && ((int)rssi >= RoutingGlobal._minRSSI))
                        //                        //###################################################################################################
                        //                        {
                        //                            //Relegate current parent to the candidate table
                        //                            if (RoutingGlobal.IsParent)
                        //                            {
                        //#if !DBG_LOGIC
                        //                                Debug.Print("Adding current parent" + RoutingGlobal.Parent + "to the candidate table");
                        //#endif
                        //                                CandidateTable.AddCandidate(RoutingGlobal.Parent, RoutingGlobal._parentLinkRSSI, RoutingGlobal.BestEtx);
                        //                            }

                        //                            // Drop new parent from candidate table if it exists
                        //                            if (index < byte.MaxValue)
                        //                                CandidateTable.DropCandidate(packet.Src);

                        //                            // Send "Drop Parent" message to old parent
                        //                            if (RoutingGlobal.IsParent)
                        //                                DistributedReset.Send_DropParent();

                        //                            RoutingGlobal.BestEtx = tempEtx;
                        //                            RoutingGlobal.Parent = packet.Src;
                        //                            RoutingGlobal._parentLinkRSSI = (int)rssi;

                        //#if DBG_VERBOSE
                        //                            Debug.Print("*** NEW PARENT: " + RoutingGlobal.Parent + "; path length (new): " + RoutingGlobal.BestEtx + "; link RSSI: " + RoutingGlobal._parentLinkRSSI + " ***");
                        //#elif DBG_SIMPLE
                        //                            Debug.Print("New Parent " + RoutingGlobal.Parent + ":[" + RoutingGlobal.BestEtx + "," + RoutingGlobal._parentLinkRSSI + "]");
                        //#endif
                        //                            // Send "Add Parent" message to new parent
                        //                            DistributedReset.Send_AddParent();
                        //                            return;
                        //                        }

                        //                        //Select sender as parent if it offers the same path length as the current best, but has a better link with receiver than current parent
                        //                        if (tempEtx == RoutingGlobal.BestEtx && RoutingGlobal.BestEtx < RoutingGlobal.MaxEtx && ((int)rssi >= RoutingGlobal._minRSSI))
                        //                        {
                        //                            if (!((int)rssi > RoutingGlobal._parentLinkRSSI))
                        //                            {
                        //                                return;
                        //                            }

                        //                            //Relegate current parent to the candidate table
                        //                            if (RoutingGlobal.IsParent)
                        //                            {
                        //#if !DBG_LOGIC
                        //                                Debug.Print("Adding current parent" + RoutingGlobal.Parent + "to the candidate table");
                        //#endif
                        //                                CandidateTable.AddCandidate(RoutingGlobal.Parent, RoutingGlobal._parentLinkRSSI, RoutingGlobal.BestEtx);
                        //                            }

                        //                            // Drop new parent from candidate table if it exists
                        //                            if (index < byte.MaxValue)
                        //                                CandidateTable.DropCandidate(packet.Src);

                        //                            // Send "Drop Parent" message to old parent
                        //                            if (RoutingGlobal.IsParent)
                        //                                DistributedReset.Send_DropParent();

                        //                            RoutingGlobal.Parent = packet.Src;
                        //                            RoutingGlobal._parentLinkRSSI = (int)rssi;
                        //#if DBG_VERBOSE
                        //                            Debug.Print("*** NEW PARENT: " + RoutingGlobal.Parent + "; path length (new): " + RoutingGlobal.BestEtx + "; link RSSI: " + RoutingGlobal._parentLinkRSSI + " ***");
                        //#elif DBG_SIMPLE
                        //                            Debug.Print("New Parent " + RoutingGlobal.Parent + ":[" + RoutingGlobal.BestEtx + "," + RoutingGlobal._parentLinkRSSI + "]");
                        //#endif
                        //                            // Send "Add Parent" message to new parent
                        //                            DistributedReset.Send_AddParent();
                        //                            return;
                        //                        }

                        //                        // Else, add candidate if valid
                        //                        if (index == byte.MaxValue)
                        //                        {
                        //                            // Add to candidate table if it does not exist and has a valid path, and is not already a child
                        //                            if (tempEtx < RoutingGlobal.Infinity && DistributedReset.findChild(packet.Src) == byte.MaxValue)
                        //                            {
                        //                                CandidateTable.AddCandidate(packet.Src, (int)rssi, tempEtx);
                        //#if DBG_VERBOSE
                        //                                Debug.Print("+++ Added new candidate: " + packet.Src + "; path length: " + tempEtx + "; link RSSI: " + rssi + " +++");
                        //#elif DBG_SIMPLE
                        //                                Debug.Print("New candidate " + packet.Src + ":[" + tempEtx + "," + rssi + "]");
                        //#endif
                        //                            }
                        //                            else
                        //                            {
                        //#if DBG_VERBOSE
                        //                                Debug.Print("--- Not a candidate: " + packet.Src + "; path length: " + tempEtx + "; link RSSI: " + rssi + " ---");
                        //#elif DBG_SIMPLE
                        //                                Debug.Print("Not a candidate " + packet.Src + ":[" + tempEtx + "," + rssi + "]");
                        //#endif
                        //                            }
                        //                        }
                        //                        else
                        //                        {
                        //                            // Drop candidate (if exists) if it's in the children table
                        //                            if (DistributedReset.findChild(packet.Src) < byte.MaxValue)
                        //                            {
                        //#if DBG_VERBOSE
                        //                                Debug.Print("!!! INVALID CANDIDATE: Removing child " + packet.Src + " from the candidate table !!!");
                        //#elif DBG_SIMPLE
                        //                                Debug.Print("Removing child " + packet.Src + " from candidate table");
                        //#endif
                        //                                CandidateTable.DropCandidate(packet.Src);
                        //                                return;
                        //                            }

                        //                            // Drop candidate if its metric exceeds Infinity
                        //                            if (tempEtx >= RoutingGlobal.Infinity)
                        //                            {
                        //#if DBG_VERBOSE
                        //                                Debug.Print("!!! INVALID CANDIDATE: Removing " + packet.Src + " with invalid metrics from the candidate table !!!");
                        //#elif DBG_SIMPLE
                        //                                Debug.Print("Removing " + packet.Src + "with invalid metrics from candidate table");
                        //#endif
                        //                                CandidateTable.DropCandidate(packet.Src);
                        //                                return;
                        //                            }

                        //                            // Update metrics regardless of validity of path
                        //                            CandidateTable._candidateList[index].UpdateMetrics((int)rssi, tempEtx);
                        //#if DBG_VERBOSE
                        //                            Debug.Print("### Updated metrics for candidate: " + packet.Src + "; path length: " + tempEtx + "; link RSSI: " + rssi + " ###");
                        //#elif DBG_SIMPLE
                        //                            Debug.Print("Updating metrics for " + packet.Src + ":[" + tempEtx + "," + rssi + "]");
                        //#endif
                        //                        }
                        #endregion
                        break;
                    #region unused
                    ////_macBase.NeighborList(_neighborList);

                    ////Debug.Print("\t>>> Number of neighbors " + _neighborList.Length + ", Parent " + Parent + ", Src " + packet.Src);
                    ////Debug.Print("\t>>> Src index in neighborlist " + Array.IndexOf(_neighborList, packet.Src));
                    ////Debug.Print("\t>>> etx " + etx + ", tempEtx " + tempEtx + ", bestEtx " + BestEtx + ", bestEtx+bestEtx " + (BestEtx + BestEtx));
                    ////SystemGlobal.PrintNeighborList(_macBase);

                    //// If the sender is current parent, update path cost (etx) and link RSSI
                    //if (RoutingGlobal.Parent == packet.Src)
                    //{
                    //    BestEtx = tempEtx;
                    //    _parentLinkRSSI = 0.5 * (neighborStatus.ReceiveLink.AverageRSSI + neighborStatus.SendLink.AverageRSSI);
                    //    Debug.Print("### Updated metrics for parent: " + RoutingGlobal.Parent + "; path length (unchanged): " + BestEtx + "; link RSSI: " + _parentLinkRSSI + " ###");

                    //    return;
                    //}

                    ////Select sender as parent if it offers a lower path length than the current best
                    ////###################################################################################################
                    ////if (tempEtx < BestEtx && tempEtx != 1)	// DEMO: Fence node only
                    //if (tempEtx < BestEtx)	// Use for all other cases
                    ////###################################################################################################
                    //{
                    //    BestEtx = tempEtx;
                    //    RoutingGlobal.Parent = packet.Src;
                    //    _parentLinkRSSI = 0.5 * (neighborStatus.ReceiveLink.AverageRSSI + neighborStatus.SendLink.AverageRSSI);

                    //    //Debug.Print(Debug.GC(true).ToString());
                    //    //var msg2 = "**$New parent acquired: " + rcvMsg.Src + "; path length: " + BestEtx + "; link RSSI: " +
                    //    //		   _parentLinkRSSI + "***";
                    //    //Debug.Print(msg2);

                    //    Debug.Print("*** New parent acquired (shorter path): " + packet.Src + "; path length (new): " + BestEtx + "; link RSSI: " + _parentLinkRSSI + " ***");
                    //    return;
                    //}

                    ////Select sender as parent if it offers the same path length as the current best, but has a better link with receiver than current parent
                    //if (tempEtx == BestEtx && BestEtx < byte.MaxValue)	// Second condition should not be necessary
                    //{
                    //    var tempRSSI = 0.5 * (neighborStatus.ReceiveLink.AverageRSSI + neighborStatus.SendLink.AverageRSSI);
                    //    Debug.Print("\tCurrent parent RSSI: " + _parentLinkRSSI + ", proposed new RSSI: " + tempRSSI);
                    //    if (!(tempRSSI > _parentLinkRSSI))
                    //    {
                    //        Debug.Print("\t\t... Not better RSSI, no change");
                    //        return;
                    //    }
                    //    //BestEtx = tempEtx;
                    //    RoutingGlobal.Parent = packet.Src;
                    //    _parentLinkRSSI = tempRSSI;
                    //    Debug.Print("*** New parent acquired (better link): " + packet.Src + "; path length (unchanged): " + BestEtx + "; link RSSI: " + _parentLinkRSSI + "***");
                    //}
                    //else
                    //{
                    //    Debug.Print("*** No change to parent (" + RoutingGlobal.Parent + ")");
                    //}

                    // Hello and Data no longer used
                    //case RoutingGlobal.MessageIds.Hello:
                    //    Debug.Print("\t>>> Hello");
                    //    if (!RoutingGlobal.IsParent)
                    //    {
                    //        return;
                    //    }
                    //    //var toSendByte = Encoding.UTF8.GetBytes(payload);
                    //    //var status = _routingPipe.Send(RoutingGlobal.Parent, toSendByte, 0, (ushort)toSendByte.Length);
                    //    var status = _routingPipe.Send(RoutingGlobal.Parent, rcvPayloadBytes, 0, packet.Size);
                    //    if (status != NetOpStatus.S_Success)
                    //    {
                    //        Debug.Print("~~~~~~~~~~~~~~~~~~~~~~~~~~~~~ Send status: " + status);
                    //    }

                    //    //SystemGlobal.PrintNumericVals("Routing Snd: ", toSendByte);
                    //    SystemGlobal.PrintNumericVals("Dummy packet Snd: ", rcvPayloadBytes, packet.Size);

                    //    //Debug.Print("Forwarded dummy packet: " + payload.Substring(0, 5) + "; source: " + payload.Substring(5) + "; from Node: " + packet.Src + " to Node: " + RoutingGlobal.Parent);
                    //    return;

                    //case RoutingGlobal.MessageIds.Data:
                    //    Debug.Print("\t>>> DATA");
                    //    if (!RoutingGlobal.IsParent)
                    //    {
                    //        Debug.Print("No parent; not forwarding data."); // or maybe broadcast?
                    //        Debug.Print("RelayNodeReceive: memory usage (after) " + Debug.GC(true));
                    //        return;
                    //    }
                    //    //var toSendByte = Encoding.UTF8.GetBytes(payload);
                    //    //var status = _routingPipe.Send(RoutingGlobal.Parent, toSendByte, 0, (ushort)toSendByte.Length);
                    //    status = _routingPipe.Send(RoutingGlobal.Parent, rcvPayloadBytes, 0, packet.Size);
                    //    if (status != NetOpStatus.S_Success)
                    //    {
                    //        Debug.Print("~~~~~~~~~~~~~~~~~~~~~~~~~~~~~ Send status: " + status);
                    //    }

                    //    //SystemGlobal.PrintNumericVals("Routing Snd: ", toSendByte);
                    //    SystemGlobal.PrintNumericVals("Data Snd: ", rcvPayloadBytes, packet.Size);

                    //    //Debug.Print("Forwarded dummy packet: " + payload.Substring(0, 5) + "; source: " + payload.Substring(5) + "; from Node: " + packet.Src + " to Node: " + RoutingGlobal.Parent);
                    //    Debug.Print("RelayNodeReceive: memory usage (after) " + Debug.GC(true));
                    //    return; 
                    #endregion

                    default:
#if !DBG_LOGIC
                        Debug.Print("\tUnknown message received <" + rcvPayloadBytes[0] + ">");
#endif
                        break;
                }
#if !DBG_LOGIC
                Debug.Print("RelayNodeReceive: memory usage (after) " + Debug.GC(true));
#endif
            }
            catch (Exception e)
            {
                Debug.Print(e.ToString());
            }
        }
#endif
    }
}