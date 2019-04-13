//#define FastHeartbeat	// Comment this out for normal use
//#define DBG_VERBOSE
#if BASE_STATION
#define DBG_SIMPLE
#define DBG_DIAGNOSTIC
#else
#define DBG_LOGIC
#endif
//#define DBG_DIAGNOSTIC

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
    public static class NetManager
    {
#if RELAY_NODE || BASE_STATION
        // ReSharper disable once NotAccessedField.Local
        private static Timer _heartbeatTimer;
        private static int _numBeat;
#endif

        private static MACPipe _netManagerPipe;
        // ReSharper disable once NotAccessedField.Local
        // private static Routing _routing;

        // Added by Dhrubo for retransmission actions
        //private static Object thisLock = new Object();
        private static ArrayList _retriedPackets = new ArrayList(); // assumed number of retries=1

        /// <summary>
        /// Initialize net manager
        /// </summary>
        /// <param name="routing"></param>
        /// <param name="macBase"></param>
        public static void Initialize(MACBase macBase)
        {
#if !DBG_LOGIC
            Debug.Print("\n*** Initializing NetManager ***\n");
#endif
            //_routing = routing;
            //_netManagerPipe = new SimpleCSMAStreamChannel(macBase, (byte)AppGlobal.MacPipeIds.NetworkManager);
            _netManagerPipe = new MACPipe(macBase, SystemGlobal.MacPipeIds.NetworkManager);
            _netManagerPipe.OnReceive += NetManagerStreamReceive;
#if RELAY_NODE
            _netManagerPipe.OnSendStatus += OnSendStatus;
#endif

#if !DBG_LOGIC
            Debug.Print("***** subscribing to Net Manager on " + SystemGlobal.MacPipeIds.NetworkManager);
#endif

#if RELAY_NODE
#if FastHeartbeat
            _heartbeatTimer = new Timer(Send_Heartbeat, null, 0, 60 * 1000);
#else
            _heartbeatTimer = new Timer(Send_Heartbeat, null, 0, 360 * 10000);
#endif
#endif

#if BASE_STATION
#if FastHeartbeat
            _heartbeatTimer = new Timer(Send_Heartbeat, null, 0, 60 * 1000);
#else
            _heartbeatTimer = new Timer(Send_Heartbeat, null, 0, 360 * 10000);
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
                    Debug.Print("\t\tNet Manager: Retry queue length = " + _retriedPackets.Count);
#endif
#if !DBG_LOGIC
                    Debug.Print("Heartbeat to " + transmitDestination.ToString() + " ACKed");
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

                    if (_retriedPackets.Contains(index)) // If this was a re-try, remove packet from queue
                        _retriedPackets.Remove(index);
                    break;

                case SendPacketStatus.SendNACKed:
                    #if !DBG_LOGIC
                    Debug.Print("Heartbeat to " + transmitDestination.ToString() + " NACKed");
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
                    Debug.Print("\t\tNet Manager: Retry queue length = " + _retriedPackets.Count);
#endif

#if !DBG_LOGIC
                    Debug.Print("Heartbeat to " + transmitDestination.ToString() + " failed");
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
                        RoutingGlobal.CleanseCandidateTable(pipe);
                        Candidate tmpBst = CandidateTable.GetBestCandidate(false);
                        NetManagerGlobal.TempParent = tmpBst.GetMacID();
                        byte[] msg = new byte[NetManagerGlobal.HeartbeatMessageSize];
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
                        _retriedPackets.Remove(index);
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
            DebuggingSupport.PrintMessageReceived(macBase, "Net Manager");
#elif DBG_SIMPLE
            Debug.Print("");
#endif

            //Debug.Print("\ton " + packet.PayloadType);
            var rcvPayloadBytes = packet.Payload;

            //var payload = new string(Encoding.UTF8.GetChars(rcvPayloadBytes));

#if DBG_VERBOSE
            SystemGlobal.PrintNumericVals("Net Manager Rcv: ", rcvPayloadBytes);
#elif DBG_SIMPLE
            Debug.Print("");
#endif

            switch ((NetManagerGlobal.MessageIds)rcvPayloadBytes[0])
            {
                case NetManagerGlobal.MessageIds.Heartbeat:
                    ushort originator;
                    ushort numBeat;
                    SystemGlobal.NodeTypes nodeType;
                    ushort parent;
                    byte TTL;

                    NetManagerGlobal.MoteMessages.Parse.HeartBeat(rcvPayloadBytes, out originator, out numBeat, out nodeType, out parent, out TTL);
                    // NetManagerGlobal.MoteMessages.Parse.HeartBeat(rcvPayloadBytes, out originator, out numBeat, out nodeType, out parent, out bestetx, out neighbors, out nbrStatus, out numSamplesRec, out numSyncSent, out avgRSSI, out ewrnp);
                    // NetManagerGlobal.MoteMessages.Parse.HeartBeat(rcvPayloadBytes, out originator, out numBeat, out nodeType, out parent, out bestetx, out num_nbrs, out neighbors, out nbrStatus, out numSamplesRec, out numSyncSent, out avgRSSI, out ewrnp, out isAvailableForUpperLayers, out TTL);
                    Debug.Print("\t>>> Heartbeat #" + numBeat + " from neighbor " + packet.Src + " by " + originator + " with TTL " + TTL);
#if DBG_DIAGNOSTIC
                    Debug.Print("Parent: " + parent);
                    Debug.Print("");
#endif

#if RELAY_NODE
                    // If we're the originator of the message, or if (TTL-1) is 0, do not pass it on.
                    if (originator == _netManagerPipe.MACRadioObj.RadioAddress || --TTL == 0)
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
                        var size = NetManagerGlobal.MoteMessages.Compose.Heartbeat(routedMsg, originator, numBeat, nodeType, parent, TTL);
                        var status = RoutingGlobal.SendToParent(_netManagerPipe, routedMsg, size);
                        if (status != 999)
                        {
                            RoutingGlobal.UpdateNumTriesInCurrentWindow_Parent(1);
#if !DBG_LOGIC
                            Debug.Print("Updated numTriesInCurrentWindow for Parent " + RoutingGlobal.Parent + "; new value = " + RoutingGlobal.GetNumTriesInCurrentWindow_Parent());
#endif
                        }
                        else //Retry once
                        {
#if !DBG_LOGIC
                            Debug.Print("Retrying packet");
#endif
                            RoutingGlobal.CleanseCandidateTable(_netManagerPipe);
                            Candidate tmpBest = CandidateTable.GetBestCandidate(false);
                            NetManagerGlobal.TempParent = tmpBest.GetMacID();
                            status = NetManagerGlobal.SendToTempParent(_netManagerPipe, routedMsg, size);
                            if (status != 999)
                            {
                                tmpBest.UpdateNumTriesInCurrentWindow(1);
#if !DBG_LOGIC
                                Debug.Print("Updated numTriesInCurrentWindow for TempParent " + NetManagerGlobal.TempParent + "; new value = " + tmpBest.GetNumTriesInCurrentWindow());
#endif
                            }
                        }
                    }
                    #endregion
                    #region unused
                    // If parent is not available, broadcast it
                    //{
                    //    //var size = NetManagerGlobal.ComposeMessages.CreateHeartbeat(NetManagerGlobal.MsgBytes, _netManagerPipe.MACRadioObj.RadioAddress);
                    //    SystemGlobal.BroadcastBeacon(_netManagerPipe, rcvPayloadBytes, packet.Size);
                    //}

                    //if (payload.Substring(0, 9).Equals("Heartbeat")) //Relay heartbeats, generated hourly
                    //{
                    //	Debug.Print("\tReceived Heartbeat: " + payload.Substring(0, 9) + "; source: " + payload.Substring(9) + "; from neighbor: " + packet.Src);
                    //	if (RoutingGlobal.Parent == SystemGlobal.NoParent)
                    //	{
                    //		return;
                    //	}
                    //	var toSendByte = Encoding.UTF8.GetBytes(payload);
                    //	var status = _netManagerPipe.Send(RoutingGlobal.Parent, toSendByte, 0, (ushort)toSendByte.Length);
                    //	if (status != NetOpStatus.S_Success)
                    //	{
                    //		Debug.Print("~~~~~~~~~~~~~~~~~~~~~~~~~~~~~ Send status: " + status);
                    //	}

                    //	SystemGlobal.PrintNumericVals("Net Manager Snd: ", toSendByte);

                    //	Debug.Print("Forwarded Heartbeat: " + payload.Substring(0, 9) + "; source: " + payload.Substring(9) + "; from Node: " + packet.Src + " to Node: " + RoutingGlobal.Parent);
                    //} 
                    #endregion
#endif
#if BASE_STATION
                    string msg = NetManagerGlobal.PCMessages.Compose.Heartbeat(originator, numBeat, nodeType, parent);
                    try
                    {
                        _serialComm.Write(msg);
#if DBG_VERBOSE
						Debug.Print("\n************ Heartbeat forwarded to PC " + msg.Substring(1, msg.Length - 2));
#endif
                    }
                    catch (Exception ex)
                    {
                        Debug.Print("SerialComm exception for Heartbeat message [" + msg + "]\n" + ex);
                    }
#endif
                    break;

                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

#if RELAY_NODE || BASE_STATION
        private static void Send_Heartbeat(object state)
        {
            _numBeat++;

#if RELAY_NODE
            var size = NetManagerGlobal.MoteMessages.Compose.Heartbeat(NetManagerGlobal.MsgBytes, _netManagerPipe.MACRadioObj.RadioAddress, (ushort)_numBeat, SystemGlobal.NodeType, RoutingGlobal.Parent, RoutingGlobal.Infinity);

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
                var status = RoutingGlobal.SendToParent(_netManagerPipe, NetManagerGlobal.MsgBytes, size);
                if (status != 999)
                {
                    RoutingGlobal.UpdateNumTriesInCurrentWindow_Parent(1);
#if !DBG_LOGIC
                    Debug.Print("Updated numTriesInCurrentWindow for Parent " + RoutingGlobal.Parent + "; new value = " + RoutingGlobal.GetNumTriesInCurrentWindow_Parent());
#endif
                }
                else //Retry once
                {
#if !DBG_LOGIC
                    Debug.Print("Retrying packet");
#endif
                    RoutingGlobal.CleanseCandidateTable(_netManagerPipe);
                    Candidate tmpBest = CandidateTable.GetBestCandidate(false);
                    NetManagerGlobal.TempParent = tmpBest.GetMacID();
                    status = NetManagerGlobal.SendToTempParent(_netManagerPipe, NetManagerGlobal.MsgBytes, size);
                    if (status != 999)
                    {
                        tmpBest.UpdateNumTriesInCurrentWindow(1);
#if !DBG_LOGIC
                        Debug.Print("Updated numTriesInCurrentWindow for TempParent " + NetManagerGlobal.TempParent + "; new value = " + tmpBest.GetNumTriesInCurrentWindow());
#endif
                    }
                }
            }
            #region unused
            // Otherwise, broadcast it
            //            {
            //                SystemGlobal.BroadcastBeacon(_netManagerPipe, NetManagerGlobal.MsgBytes, size);

            //#if DBG_VERBOSE
            //                DebuggingSupport.PrintMessageSent(_netManagerPipe, "(broadcast) Heartbeat #" + _numBeat);
            //                SystemGlobal.PrintNumericVals("Net Manager ", NetManagerGlobal.MsgBytes, size);
            //#elif DBG_SIMPLE
            //                Debug.Print("\tBroadcastBeacon heartbeat # " + _numBeat);
            //#endif
            //            }
            #endregion
#endif
#if BASE_STATION
            var msg = NetManagerGlobal.PCMessages.Compose.Heartbeat(_netManagerPipe.MACRadioObj.RadioAddress, _numBeat,
                SystemGlobal.NodeTypes.Base, RoutingGlobal.Parent);
            try
            {
                var status = _serialComm.Write(msg);
                if (status)
                {
#if DBG_VERBOSE
					Debug.Print("\n************ Heartbeat generated to PC " + msg.Substring(1, msg.Length - 2));
#elif DBG_SIMPLE
                    Debug.Print("Heartbeat generated");
#endif
                }
                else
                {
                    Debug.Print("Error sending [" + msg + "] to PC");
                }
            }
            catch (Exception ex)
            {
                Debug.Print("SerialComm exception for Heartbeat message [" + msg + "]\n" + ex);
            }
#endif
        }
#endif
    }
}