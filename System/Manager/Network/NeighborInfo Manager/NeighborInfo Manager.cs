//#define FastHeartbeat	// Comment this out for normal use
//#define DBG_VERBOSE
#if BASE_STATION
#define DBG_SIMPLE
#define DBG_DIAGNOSTIC
#else
#define DBG_LOGIC
#endif

// Ensure exactly one defined
#if !DBG_VERBOSE && !DBG_SIMPLE && !DBG_LOGIC
#error Exactly one of DBG_VERBOSE, DBG_SIMPLE, DBG_LOGIC must be defined.
#endif
#if  DBG_VERBOSE && (DBG_SIMPLE || DBG_LOGIC) || DBG_SIMPLE && (DBG_VERBOSE || DBG_LOGIC) || DBG_LOGIC && (DBG_VERBOSE || DBG_SIMPLE)
#error Exactly one of DBG_VERBOSE, DBG_SIMPLE, DBG_LOGIC must be defined.
#endif

#if BASE_STATION
// Base Node
#elif RELAY_NODE
// Relay Node
#elif FAKE_FENCE
#error Fake Fence not supported
#else
#error Invalid node type. Valid options: BASE_STATION, RELAY_NODE, FAKE_FENCE
#endif

using System;
using System.Collections;
using Microsoft.SPOT;
using Samraksh.Components.Utility;
using Samraksh.eMote.Net;
using Samraksh.eMote.Net.MAC;
using Samraksh.VirtualFence;
using Samraksh.VirtualFence.Components;

#if RELAY_NODE || BASE_STATION
using System.Threading;
#endif

namespace Samraksh.Manager.NetManager
{
    /// <summary>
    /// Network manager
    /// </summary>
    public static class NeighborInfoManager
    {
#if RELAY_NODE || BASE_STATION
        // ReSharper disable once NotAccessedField.Local
        private static Timer _nbrInfoTimer;
        private static int _numBeat;
#endif

        // Added by Dhrubo for retransmission actions
        //private static Object thisLock = new Object();
        private static ArrayList _retriedPackets = new ArrayList(); // assumed number of retries=1
        private static Hashtable _sentPacketSizes = new Hashtable(); // needed to recreate packets that can be of variable lengths

        private static MACPipe _neighborInfoManagerPipe;
        // ReSharper disable once NotAccessedField.Local
        // private static Routing _routing;

        // Circular array head pointer (used if number of neighbors is more than can fit in one message)
        private static byte circ_headptr = 0;

        /// <summary>
        /// Initialize neighborinfo manager
        /// </summary>
        /// <param name="routing"></param>
        /// <param name="macBase"></param>
        public static void Initialize(MACBase macBase)
        {
#if !DBG_LOGIC
            Debug.Print("\n*** Initializing Neighborhood Manager ***\n");
#endif
            //_routing = routing;
            //_neighborInfoManagerPipe = new SimpleCSMAStreamChannel(macBase, (byte)AppGlobal.MacPipeIds.NetworkManager);
            _neighborInfoManagerPipe = new MACPipe(macBase, SystemGlobal.MacPipeIds.NeighborInfoManager);
            _neighborInfoManagerPipe.OnReceive += NetManagerStreamReceive;
#if RELAY_NODE
            _neighborInfoManagerPipe.OnSendStatus += OnSendStatus;
#endif

#if !DBG_LOGIC
            Debug.Print("***** subscribing to Neighborhood Manager on " + SystemGlobal.MacPipeIds.NeighborInfoManager);
#endif

#if RELAY_NODE
#if FastHeartbeat
            _nbrInfoTimer = new Timer(Send_AdvancedHeartbeat, null, 180 * 10000, 60 * 1000);
#else
            _nbrInfoTimer = new Timer(Send_AdvancedHeartbeat, null, 180 * 10000, 360 * 10000); // Starts at half an hour
#endif
#endif
        }

        private static void OnSendStatus(IMAC macInstance, DateTime time, SendPacketStatus ACKStatus, uint transmitDestination, ushort index)
        {
            var pipe = macInstance as MACPipe;
            switch (ACKStatus)
            {
                case SendPacketStatus.SendACKed:
#if DBG_DIAGNOSTIC
                    Debug.Print("\t\tNeighborInfo Manager: Retry queue length = " + _retriedPackets.Count);
                    Debug.Print("\t\tNeighborInfo Manager: Send queue length = " + _sentPacketSizes.Count);
#endif
#if !DBG_LOGIC
                    Debug.Print("NeighborInfo to " + transmitDestination.ToString() + " ACKed");
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

                    _sentPacketSizes.Remove(index); // Remove packet from size hashtable
                    if (_retriedPackets.Contains(index)) // If this was a re-try, remove packet from queue
                        _retriedPackets.Remove(index);
                    break;

                case SendPacketStatus.SendNACKed:
#if !DBG_LOGIC
                    Debug.Print("NeighborInfo to " + transmitDestination.ToString() + " NACKed");
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
#if DBG_DIAGNOSTIC
                    Debug.Print("\t\tNeighborInfo Manager: Retry queue length = " + _retriedPackets.Count);
                    Debug.Print("\t\tNeighborInfo Manager: Send queue length = " + _sentPacketSizes.Count);
#endif

#if !DBG_LOGIC
                    Debug.Print("NeighborInfo to " + transmitDestination.ToString() + " failed");
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

                    // Retry
                    if (!_retriedPackets.Contains(index) && RoutingGlobal._color == Color.Green) // If packet not there, enqueue it and retry it once
                    {
#if !DBG_LOGIC
                        Debug.Print("Retrying packet...");
#endif
                        RoutingGlobal.CleanseCandidateTable(pipe);
                        Candidate tmpBst = CandidateTable.GetBestCandidate(false);
                        NetManagerGlobal.TempParent = tmpBst.GetMacID();
                        byte[] msg = new byte[(ushort)_sentPacketSizes[index]];
                        if (pipe.GetMsgWithMsgID(ref msg, index) == DeviceStatus.Success)
                        {
                            NetManagerGlobal.SendToTempParent(pipe, msg, msg.Length);
                            tmpBst.UpdateNumTriesInCurrentWindow(1);
#if !DBG_LOGIC
                            Debug.Print("Updated numTriesInCurrentWindow for TempParent " + transmitDestination + "; new value = " + tmpBst.GetNumTriesInCurrentWindow());
#endif
                            _retriedPackets.Add(index);
                        }
                    }
                    else // Retried once; drop packet
                    {
                        _retriedPackets.Remove(index);
                        _sentPacketSizes.Remove(index);
                    }
                    break;

                default:
                    break;
            }
        }

        public static SerialComm _serialComm;
        public static void Initialize(MACBase macBase, SerialComm serialComm)
        {
            _serialComm = serialComm;
            Initialize(macBase);
        }


        /// <summary>
        /// 
        /// </summary>
        /// <param name="macBase"></param>
        /// <param name="dateTime"></param>
        private static void NetManagerStreamReceive(IMAC macBase, DateTime dateTime, Packet packet)
        {
#if DBG_VERBOSE
            DebuggingSupport.PrintMessageReceived(macBase, "NeighborInfo Manager");
#elif DBG_SIMPLE
            Debug.Print("");
#endif

            //Debug.Print("\ton " + packet.PayloadType);
            var rcvPayloadBytes = packet.Payload;

            //var payload = new string(Encoding.UTF8.GetChars(rcvPayloadBytes));

#if DBG_VERBOSE
            SystemGlobal.PrintNumericVals("NeighborInfo Manager Rcv: ", rcvPayloadBytes);
#elif DBG_SIMPLE
            Debug.Print("");
#endif

            switch ((NetManagerGlobal.MessageIds)rcvPayloadBytes[0])
            {
                case NetManagerGlobal.MessageIds.NeighborInfo:
                    ushort originator;
                    ushort numBeat;
                    SystemGlobal.NodeTypes nodeType;
                    ushort parent;
                    byte bestewrnp;
                    byte num_nbrs;
                    byte TTL;
                    ushort[] neighbors;
                    byte[] nbrStatus;
                    ushort[] numSamplesRec;
                    ushort[] numSyncSent;
                    byte[] avgRSSI;
                    byte[] ewrnp;
                    byte[] isAvailableForUpperLayers;

                    // NetManagerGlobal.MoteMessages.Parse.HeartBeat(rcvPayloadBytes, out originator, out numBeat, out nodeType, out parent);
                    // NetManagerGlobal.MoteMessages.Parse.HeartBeat(rcvPayloadBytes, out originator, out numBeat, out nodeType, out parent, out bestewrnp, out neighbors, out nbrStatus, out numSamplesRec, out numSyncSent, out avgRSSI, out ewrnp);
                    NetManagerGlobal.MoteMessages.Parse.HeartBeat(rcvPayloadBytes, out originator, out numBeat, out nodeType, out parent, out bestewrnp, out num_nbrs, out neighbors, out nbrStatus, out numSamplesRec, out numSyncSent, out avgRSSI, out ewrnp, out isAvailableForUpperLayers, out TTL);
                    Debug.Print("\t>>> NeighborInfo #" + numBeat + " from neighbor " + packet.Src + " by " + originator + " with TTL " + TTL);
#if DBG_DIAGNOSTIC
                    Debug.Print("Actual # neighbors: " + num_nbrs);
                    SystemGlobal.PrintNumericVals("Neighbor names: ", neighbors);
                    SystemGlobal.PrintNumericVals("Neighbor status: ", nbrStatus);
                    SystemGlobal.PrintNumericVals("Avg RSSI: ", avgRSSI);
                    SystemGlobal.PrintNumericVals("Routing EWRNP: ", ewrnp);
                    SystemGlobal.PrintNumericVals("# samples rcvd: ", numSamplesRec);
                    SystemGlobal.PrintNumericVals("# timesync sent: ", numSyncSent);
                    SystemGlobal.PrintNumericVals("Available for upper layers: ", isAvailableForUpperLayers);
                    Debug.Print("Parent: " + parent);
                    Debug.Print("");
#endif

#if RELAY_NODE
                    // If we're the originator of the message, or if TTL-1 is 0, do not pass it on.
                    if (originator == _neighborInfoManagerPipe.MACRadioObj.RadioAddress || --TTL == 0)
                    {
                        return;
                    }

                    #region Uncomment when not using scheduler
                    // TODO: Uncomment lines when not using scheduler
                    // If in a reset, do not forward TODO: Change this to "spray"
                    if (RoutingGlobal._color == Color.Red)
                    {
#if DBG_VERBOSE
                        Debug.Print("\tIn a Reset wave... not forwarded");
#endif
                        return;
                    }
                    // If parent is available, pass it on
                    if (RoutingGlobal.IsParent)
                    {
                        byte[] routedMsg = new byte[rcvPayloadBytes.Length];
                        var size = NetManagerGlobal.MoteMessages.Compose.Heartbeat(routedMsg, originator, numBeat, nodeType, parent, bestewrnp, neighbors, nbrStatus, numSamplesRec, numSyncSent, avgRSSI, ewrnp, isAvailableForUpperLayers, TTL);
                        var status = RoutingGlobal.SendToParent(_neighborInfoManagerPipe, routedMsg, size);
                        if (status != 999)
                        {
                            RoutingGlobal.UpdateNumTriesInCurrentWindow_Parent(1);
#if !DBG_LOGIC
                            Debug.Print("Updated numTriesInCurrentWindow for Parent " + RoutingGlobal.Parent + "; new value = " + RoutingGlobal.GetNumTriesInCurrentWindow_Parent());
#endif
                            if (!_sentPacketSizes.Contains(status))
                            {
                                _sentPacketSizes.Add(status, size);
                            }
                        }
                        else //Retry once
                        {
#if !DBG_LOGIC
                            Debug.Print("Retrying packet");
#endif
                            RoutingGlobal.CleanseCandidateTable(_neighborInfoManagerPipe);
                            Candidate tmpBest = CandidateTable.GetBestCandidate(false);
                            NetManagerGlobal.TempParent = tmpBest.GetMacID();
                            status = NetManagerGlobal.SendToTempParent(_neighborInfoManagerPipe, routedMsg, size);
                            if (status != 999)
                            {
                                tmpBest.UpdateNumTriesInCurrentWindow(1);
#if !DBG_LOGIC
                                Debug.Print("Updated numTriesInCurrentWindow for TempParent " + NetManagerGlobal.TempParent + "; new value = " + tmpBest.GetNumTriesInCurrentWindow());
#endif
                                if (!_sentPacketSizes.Contains(status))
                                {
                                    _sentPacketSizes.Add(status, size);
                                }
                            }
                        }
                    }
                    #endregion
                    #region unused
                    // If parent is not available, broadcast it
                    //{
                    //    //var size = NetManagerGlobal.ComposeMessages.CreateHeartbeat(NetManagerGlobal.MsgBytes, _neighborInfoManagerPipe.MACRadioObj.RadioAddress);
                    //    SystemGlobal.BroadcastBeacon(_neighborInfoManagerPipe, rcvPayloadBytes, packet.Size);
                    //}

                    //if (payload.Substring(0, 9).Equals("Heartbeat")) //Relay heartbeats, generated hourly
                    //{
                    //	Debug.Print("\tReceived Heartbeat: " + payload.Substring(0, 9) + "; source: " + payload.Substring(9) + "; from neighbor: " + packet.Src);
                    //	if (RoutingGlobal.Parent == SystemGlobal.NoParent)
                    //	{
                    //		return;
                    //	}
                    //	var toSendByte = Encoding.UTF8.GetBytes(payload);
                    //	var status = _neighborInfoManagerPipe.Send(RoutingGlobal.Parent, toSendByte, 0, (ushort)toSendByte.Length);
                    //	if (status != NetOpStatus.S_Success)
                    //	{
                    //		Debug.Print("~~~~~~~~~~~~~~~~~~~~~~~~~~~~~ Send status: " + status);
                    //	}

                    //	SystemGlobal.PrintNumericVals("Net Manager Snd: ", toSendByte);

                    //	Debug.Print("Forwarded Heartbeat: " + payload.Substring(0, 9) + "; source: " + payload.Substring(9) + "; from Node: " + packet.Src + " to Node: " + RoutingGlobal.Parent);
                    //} 
                    #endregion
#endif
                    break;

                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

#if RELAY_NODE
        /*CAUTION: Any change in the advanced heartbeat structure should be accounted for in variables
         * NetManagerGlobal.AdvHeartbeatFixedSize and NetManagerGlobal.EachNeighborInfoSize
         */
        private static void Send_AdvancedHeartbeat(object state)
        {
            ushort[] neighbors = MACBase.NeighborListArray();
            _neighborInfoManagerPipe.MACBase.MACNeighborList(neighbors);

            // Find the number of neighbors
            byte num_nbrs = 0;
            for (int i = 0; i < neighbors.Length; i++)
            {
                if (neighbors[i] == 0) // At the end
                    break;
                num_nbrs++;
            }

            if (num_nbrs == 0)
                return;

            _numBeat++;

            ushort[] valid_nbrs;
            byte[] nbrStatus;
            ushort[] numSamplesRec;
            ushort[] numSyncSent;
            byte[] avgRSSI;
            byte[] ewrnp;
            byte[] isAvailableForUpperLayers;

            // TODO: Make this if-else more compact
            if (num_nbrs <= NetManagerGlobal.MaxNeighborsPerHeartbeat) // Send all information
            {

                valid_nbrs = new ushort[num_nbrs];
                nbrStatus = new byte[num_nbrs];
                numSamplesRec = new ushort[num_nbrs];
                numSyncSent = new ushort[num_nbrs];
                avgRSSI = new byte[num_nbrs];
                ewrnp = new byte[num_nbrs];
                isAvailableForUpperLayers = new byte[num_nbrs];

                // Initialize ewrnp array with maxEtx
                for (int i = 0; i < ewrnp.Length; i++)
                {
                    ewrnp[i] = RoutingGlobal.MaxEtx;
                }

                for (int i = 0; i < num_nbrs; i++)
                {
                    var nbr_name = neighbors[i];
                    Neighbor nbr = _neighborInfoManagerPipe.NeighborStatus(nbr_name);

                    valid_nbrs[i] = nbr_name;
                    nbrStatus[i] = (byte)nbr.NeighborStatus;
                    numSamplesRec[i] = nbr.NumOfTimeSamplesRecorded;
                    numSyncSent[i] = nbr.NumTimeSyncMessagesSent;
                    avgRSSI[i] = (byte)((nbr.ReceiveLink.AverageRSSI + nbr.SendLink.AverageRSSI)); // * 0.5;

                    int index = CandidateTable.findIndex(nbr_name);
                    if (RoutingGlobal.Parent == nbr_name)
                    {
                        ewrnp[i] = RoutingGlobal.GetPathEWRNP();
                    }
                    else if (index < byte.MaxValue)
                    {
                        ewrnp[i] = (byte)CandidateTable._candidateList[index].GetPathEWRNP();
                    }

                    isAvailableForUpperLayers[i] = nbr.IsAvailableForUpperLayers ? (byte)1 : (byte)0;
                }
            }
            else // Starting with the current head pointer, use neighbor list as a circular array to send out info for NetManagerGlobal.MaxNeighborsPerHeartbeat consecutive neighbors
            {
                valid_nbrs = new ushort[NetManagerGlobal.MaxNeighborsPerHeartbeat];
                nbrStatus = new byte[NetManagerGlobal.MaxNeighborsPerHeartbeat];
                numSamplesRec = new ushort[NetManagerGlobal.MaxNeighborsPerHeartbeat];
                numSyncSent = new ushort[NetManagerGlobal.MaxNeighborsPerHeartbeat];
                avgRSSI = new byte[NetManagerGlobal.MaxNeighborsPerHeartbeat];
                ewrnp = new byte[NetManagerGlobal.MaxNeighborsPerHeartbeat];
                isAvailableForUpperLayers = new byte[NetManagerGlobal.MaxNeighborsPerHeartbeat];

                // Initialize ewrnp array with maxEtx
                for (int i = 0; i < ewrnp.Length; i++)
                {
                    ewrnp[i] = RoutingGlobal.MaxEtx;
                }

                for (int i = 0; i < NetManagerGlobal.MaxNeighborsPerHeartbeat; i++)
                {
                    // If current head pointer has a higher index than number of neighbors (owing to loss), restart at index 0; otherwise, start at current head pointer
                    circ_headptr = (circ_headptr < num_nbrs) ? (byte)(circ_headptr % num_nbrs) : (byte)0;
                    var nbr_name = neighbors[circ_headptr];
                    Neighbor nbr = _neighborInfoManagerPipe.NeighborStatus(nbr_name);

                    valid_nbrs[i] = nbr_name;
                    nbrStatus[i] = (byte)nbr.NeighborStatus;
                    numSamplesRec[i] = nbr.NumOfTimeSamplesRecorded;
                    numSyncSent[i] = nbr.NumTimeSyncMessagesSent;
                    avgRSSI[i] = (byte)((nbr.ReceiveLink.AverageRSSI + nbr.SendLink.AverageRSSI)); // * 0.5;

                    int index = CandidateTable.findIndex(nbr_name);
                    if (RoutingGlobal.Parent == nbr_name)
                    {
                        ewrnp[i] = RoutingGlobal.GetPathEWRNP();
                    }
                    else if (index < byte.MaxValue)
                    {
                        ewrnp[i] = (byte)CandidateTable._candidateList[index].GetPathEWRNP();
                    }

                    isAvailableForUpperLayers[i] = nbr.IsAvailableForUpperLayers ? (byte)1 : (byte)0;

                    circ_headptr = (byte)((circ_headptr + 1) % num_nbrs);
                }

                // Adjust circular buffer head pointer at the end
                circ_headptr = (byte)((circ_headptr + 1) % num_nbrs);
            }

#if DBG_DIAGNOSTIC
            SystemGlobal.PrintNumericVals("Neighbor names: ", valid_nbrs);
            SystemGlobal.PrintNumericVals("Neighbor status: ", nbrStatus);
            SystemGlobal.PrintNumericVals("Avg RSSI: ", avgRSSI);
            SystemGlobal.PrintNumericVals("Routing EWRNP: ", ewrnp);
            SystemGlobal.PrintNumericVals("# samples rcvd: ", numSamplesRec);
            SystemGlobal.PrintNumericVals("# timesync sent: ", numSyncSent);
            SystemGlobal.PrintNumericVals("Available for upper layers: ", isAvailableForUpperLayers);
            Debug.Print("Parent: " + RoutingGlobal.Parent);
            Debug.Print("");
#endif

            var size = NetManagerGlobal.MoteMessages.Compose.Heartbeat(NetManagerGlobal.MsgBytes, _neighborInfoManagerPipe.MACRadioObj.RadioAddress, (ushort)_numBeat, SystemGlobal.NodeType, RoutingGlobal.Parent, (byte)RoutingGlobal.GetPathEWRNP(), valid_nbrs, nbrStatus, numSamplesRec, numSyncSent, avgRSSI, ewrnp, isAvailableForUpperLayers, RoutingGlobal.Infinity);
            //var size = NetManagerGlobal.MoteMessages.Compose.Heartbeat(NetManagerGlobal.MsgBytes, _neighborInfoManagerPipe.MACRadioObj.RadioAddress, (ushort)_numBeat, SystemGlobal.NodeType, RoutingGlobal.Parent, (byte)RoutingGlobal.BestEtx, neighbors, nbrStatus, avgRSSI, ewrnp);
#if !DBG_LOGIC
            Debug.Print("NeighborInfo#" + _numBeat + " size: " + size);
#endif

            #region Uncomment when not using scheduler
            // If in a reset, do not forward TODO: Change this to "spray"
            if (RoutingGlobal._color == Color.Red)
            {
#if DBG_VERBOSE

                Debug.Print("\tIn a Reset wave... not forwarded");
#endif
                return;
            }

            // If parent is available, pass it on
            if (RoutingGlobal.IsParent)
            {
                var status = RoutingGlobal.SendToParent(_neighborInfoManagerPipe, NetManagerGlobal.MsgBytes, size);
                if (status != 999)
                {
                    RoutingGlobal.UpdateNumTriesInCurrentWindow_Parent(1);
#if !DBG_LOGIC
                    Debug.Print("Updated numTriesInCurrentWindow for Parent " + RoutingGlobal.Parent + "; new value = " + RoutingGlobal.GetNumTriesInCurrentWindow_Parent());
#endif
                    if (!_sentPacketSizes.Contains(status))
                    {
                        _sentPacketSizes.Add(status, size);
                    }
                }
                else //Retry once
                {
#if !DBG_LOGIC
                    Debug.Print("Retrying packet");
#endif
                    RoutingGlobal.CleanseCandidateTable(_neighborInfoManagerPipe);
                    Candidate tmpBest = CandidateTable.GetBestCandidate(false);
                    NetManagerGlobal.TempParent = tmpBest.GetMacID();
                    status = NetManagerGlobal.SendToTempParent(_neighborInfoManagerPipe, NetManagerGlobal.MsgBytes, size);
                    if (status != 999)
                    {
                        tmpBest.UpdateNumTriesInCurrentWindow(1);
#if !DBG_LOGIC
                        Debug.Print("Updated numTriesInCurrentWindow for TempParent " + NetManagerGlobal.TempParent + "; new value = " + tmpBest.GetNumTriesInCurrentWindow());
#endif
                        if (!_sentPacketSizes.Contains(status))
                        {
                            _sentPacketSizes.Add(status, size);
                        }
                    }
                }
            }
            #endregion
        }
#endif
    }
}