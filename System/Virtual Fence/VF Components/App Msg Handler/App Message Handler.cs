//#define DBG_VERBOSE
#if BASE_STATION
#define DBG_SIMPLE
#else
#define DBG_SIMPLE
//#define DBG_LOGIC
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
// Base node
#elif RELAY_NODE
// Relay node
#elif CLIENT_NODE
// Client node
#elif FAKE_FENCE
// Fake fence node
#elif PC
// PC node
#else
#error Invalid node type. Valid options: BASE_STATION, RELAY_NODE, CLIENT_NODE, FAKE_FENCE, PC
#endif

#if BASE_STATION || RELAY_NODE || CLIENT_NODE || FAKE_FENCE	// This code only applies to eMote nodes
using System;
using System.Collections;
using System.Threading;
using Microsoft.SPOT;
using Samraksh.Components.Utility;
using Samraksh.eMote.Net;
using Samraksh.eMote.Net.MAC;

namespace Samraksh.VirtualFence.Components
{
    /// <summary>
    /// Handle application messages
    /// </summary>
    public static class AppMsgHandler
    {
        // Added by Dhrubo for retransmission actions
        //private static Object thisLock = new Object();
        private static ArrayList _retriedPackets = new ArrayList(); // assumed number of retries=1
#if CLIENT_NODE
        private static int sendMsgNum = 0;
#endif
        private static EnhancedEmoteLCD _lcd;

        //private static int _numData;
        //private static Routing _routing;

        /// <summary>
        /// Initialize routing
        /// </summary>
        /// <param name="routing"></param>
        /// <param name="macBase"></param>
        /// <param name="lcd"></param>
        public static void Initialize(MACBase macBase, EnhancedEmoteLCD lcd)
        {
            AppGlobal.AppPipe = new MACPipe(macBase, SystemGlobal.MacPipeIds.App);
            AppGlobal.AppPipe.OnReceive += AppPipeReceive;
#if RELAY_NODE || CLIENT_NODE
            AppGlobal.AppPipe.OnSendStatus += OnSendStatus;
#endif

#if !DBG_LOGIC
            Debug.Print("***** Subscribing to App on " + SystemGlobal.MacPipeIds.App);
#endif
            _lcd = lcd;
        }

        private static void OnSendStatus(IMAC macInstance, DateTime time, SendPacketStatus ACKStatus, uint transmitDestination, ushort index)
        {
            var pipe = macInstance as MACPipe;
            switch (ACKStatus)
            {
                case SendPacketStatus.SendACKed:
#if DBG_DIAGNOSTIC
                    Debug.Print("\t\tApp Message Handler: Retry queue length = " + _retriedPackets.Count);
#endif
#if !DBG_LOGIC
                    Debug.Print("Detect to " + transmitDestination.ToString() + " ACKed");
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
                    Debug.Print("Detect to " + transmitDestination.ToString() + " NACKed");
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
                    Debug.Print("\t\tApp Message Handler: Retry queue length = " + _retriedPackets.Count);
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
                        AppGlobal.TempParent = tmpBst.GetMacID();
                        byte[] msg = new byte[AppGlobal.DetectionMessageSize];
                        if (pipe.GetMsgWithMsgID(ref msg, index) == DeviceStatus.Success)
                        {
                            AppGlobal.SendToTempParent(pipe, msg, msg.Length);
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
#if RELAY_NODE
        private static ushort _MACID;
        /// <summary>
        /// Initialize for base station (include serial)
        /// </summary>
        /// <param name="routing"></param>
        /// <param name="macBase"></param>
        /// <param name="lcd"></param>
        /// <param name="serialComm"></param>
        public static void Initialize(MACBase macBase, EnhancedEmoteLCD lcd, ushort macID)
        {
            _MACID = macID;
            Initialize(macBase, lcd);
        }
#elif CLIENT_NODE
        private static SerialComm _serialComm;
        /// <summary>
        /// Initialize for base station (include serial)
        /// </summary>
        /// <param name="routing"></param>
        /// <param name="macBase"></param>
        /// <param name="lcd"></param>
        /// <param name="serialComm"></param>
        public static void Initialize(MACBase macBase, EnhancedEmoteLCD lcd, SerialComm serialComm, int SendPacketInterval)
        {
            _serialComm = serialComm;
            Initialize(macBase, lcd);
            sendMsgNum = 0;
            Random rand = new Random();
            // send a packet every 5 seconds
            var sendPacketTimer = new SimplePeriodicTimer(callBackValue =>
            {
                _lcd.Write("Send");
                int MsgSize = sizeof(byte) + sizeof(byte) + sizeof(ushort) + sizeof(ushort) + sizeof(byte) + sizeof(ushort) + sizeof(ushort) + sizeof(byte);
                byte[] routedMsg = new byte[MsgSize];
                ushort originator = AppGlobal.AppPipe.MACRadioObj.RadioAddress;
                AppGlobal.ClassificationType classificationType = AppGlobal.ClassificationType.Send;
                byte TTL = Byte.MaxValue;
                ushort pathLength = 1;
                ushort[] path = { originator };
                var size = AppGlobal.MoteMessages.Compose.SendPacket(routedMsg, originator, classificationType, sendMsgNum, TTL, pathLength, path);
                // add payload
                routedMsg[routedMsg.Length - 1] = (byte)rand.Next(11); 
                var status = RoutingGlobal.SendToParent(AppGlobal.AppPipe, routedMsg, size);
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
                    status = AppGlobal.SendToTempParent(AppGlobal.AppPipe, routedMsg, size);
                }
                _lcd.Write(sendMsgNum);
                sendMsgNum++;

            }, null, 0, SendPacketInterval);
            sendPacketTimer.Start();
        }
#elif BASE_STATION
        private static SerialComm _serialComm;
        /// <summary>
        /// Initialize for base station (include serial)
        /// </summary>
        /// <param name="routing"></param>
        /// <param name="macBase"></param>
        /// <param name="lcd"></param>
        /// <param name="serialComm"></param>
        public static void Initialize(MACBase macBase, EnhancedEmoteLCD lcd, SerialComm serialComm)
        {
            _serialComm = serialComm;
            Initialize(macBase, lcd);
        }
#endif

        public static void SendDetectionMessage(AppGlobal.ClassificationType classification, int detectNum)
        {
            var msgBytes = new byte[AppGlobal.DetectionMessageSize];
            var actualSize = AppGlobal.MoteMessages.Compose.Detection(msgBytes,
                AppGlobal.AppPipe.MACRadioObj.RadioAddress, classification, detectNum, RoutingGlobal.Infinity);

            #region Uncomment when not using scheduler
#if DBG_VERBOSE
                        Debug.Print("\nAttempting send of detection message " + _detectNum + " on pipe " + AppGlobal.AppPipe.PayloadType + " with classification " +
                                    (char)classification + ", size " + actualSize + " to parent " + RoutingGlobal.Parent);
#elif DBG_SIMPLE
            Debug.Print("\nSending to " + RoutingGlobal.Parent);
#endif

#if DBG_VERBOSE
                        var msgS = new System.Text.StringBuilder();
                        for (var i = 0; i < actualSize; i++)
                        {
                            msgS.Append(msgBytes[i]);
                            msgS.Append(' ');
                        }
                        Debug.Print("\t" + msgS);
#endif
            // If in a reset, do not forward TODO: Change this to "spray"
            if (RoutingGlobal._color == Color.Red)
            {
#if DBG_VERBOSE || DBG_SIMPLE
                Debug.Print("\tIn a Reset wave... not forwarded");
#endif
                return;
            }
            // If parent is available, pass it on
            if (RoutingGlobal.IsParent)
            {
                var status = RoutingGlobal.SendToParent(AppGlobal.AppPipe, msgBytes, actualSize);
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
                    RoutingGlobal.CleanseCandidateTable(AppGlobal.AppPipe);
                    Candidate tmpBest = CandidateTable.GetBestCandidate(false);
                    AppGlobal.TempParent = tmpBest.GetMacID();
                    status = AppGlobal.SendToTempParent(AppGlobal.AppPipe, msgBytes, actualSize);
                    if (status != 999)
                    {
                        tmpBest.UpdateNumTriesInCurrentWindow(1);
#if !DBG_LOGIC
                        Debug.Print("Updated numTriesInCurrentWindow for TempParent " + AppGlobal.TempParent + "; new value = " + tmpBest.GetNumTriesInCurrentWindow());
#endif
                    }
                }
            }
            #endregion
        }
#if BASE_STATION

        public static byte GetBaseReply(byte SendPayload)
        {
            byte ReplyPayload = 0;
            switch (SendPayload)
            {
                case (byte)1:
                    ReplyPayload = (byte)11;
                    break;
                case (byte)2:
                    ReplyPayload = (byte)12;
                    break;
                case (byte)3:
                    ReplyPayload = (byte)13;
                    break;
                case (byte)4:
                    ReplyPayload = (byte)14;
                    break;
                case (byte)5:
                    ReplyPayload = (byte)15;
                    break;
                case (byte)6:
                    ReplyPayload = (byte)16;
                    break;
                case (byte)7:
                    ReplyPayload = (byte)17;
                    break;
                case (byte)8:
                    ReplyPayload = (byte)18;
                    break;
                case (byte)9:
                    ReplyPayload = (byte)19;
                    break;
                case (byte)10:
                    ReplyPayload = (byte)20;
                    break;
                default:
                    Debug.Print(SendPayload + " not in base station table");
                    break;

            }
            return ReplyPayload;
        }
#endif
        /// <summary>
        /// Handle an App message
        /// </summary>
        /// <param name="macBase"></param>
        /// <param name="dateTime"></param>
        /// <param name="packet"></param>
        public static void AppPipeReceive(IMAC macBase, DateTime dateTime, Packet packet)
        {
#if DBG_VERBOSE
			DebuggingSupport.PrintMessageReceived(macBase, "App");
#elif DBG_SIMPLE
            Debug.Print("");
#endif
            Debug.Print("AppPipeReceive ");
            try
            {
#if !DBG_LOGIC
                Debug.Print("\tFrom " + packet.Src);
#endif

                DebuggingSupport.PrintMessageReceived(macBase, "App");

                //Debug.Print("\ton " + packet.PayloadType);
                //var rcvPayloadBytes = packet.Payload;
                //var rcvPayloadBytes = SystemGlobal.GetTrimmedPayload(packet);
                var rcvPayloadBytes = packet.Payload;
#if DBG_VERBOSE
				SystemGlobal.PrintNumericVals("\tApp Rcv: ", rcvPayloadBytes);
#elif DBG_SIMPLE
                Debug.Print("");
#endif

                switch ((AppGlobal.MessageIds)rcvPayloadBytes[0])
                {
                    case AppGlobal.MessageIds.Detect:
                        {

                        AppGlobal.ClassificationType classificationType;
                        ushort originator;
                        byte TTL;
                        ushort detectionNumber;

                        AppGlobal.MoteMessages.Parse.Detection(rcvPayloadBytes, out classificationType, out detectionNumber, out originator, out TTL);

                        Debug.Print("\tDetect. From neighbor " + packet.Src + " # " + detectionNumber + ". Classification " + (char)classificationType + " created by " + originator + " with TTL " + TTL);
#if RELAY_NODE || CLIENT_NODE
                        //	Check if originated by self or if TTL-1 = 0
                        if (originator == AppGlobal.AppPipe.MACRadioObj.RadioAddress || --TTL == 0)
                        {
                            return;
                        }

                        #region Uncomment when scheduler disabled
                        // If in a reset, do not forward TODO: Change this to "spray"
                        if (RoutingGlobal._color == Color.Red)
                        {
#if DBG_VERBOSE
                            Debug.Print("\tIn a Reset wave... not forwarded");
#endif
                            return;
                        }
                        #endregion
                        #region Uncomment when scheduler disabled
                        /* TODO: Uncomment lines 368-394 when not using scheduler*/
                        // Not originated by self. 
                        // If parent is available, pass it on
                        if (RoutingGlobal.IsParent)
                        {
                            byte[] routedMsg = new byte[rcvPayloadBytes.Length];
                            var size = AppGlobal.MoteMessages.Compose.Detection(routedMsg, originator, classificationType, detectionNumber, TTL);
                            var status = RoutingGlobal.SendToParent(AppGlobal.AppPipe, routedMsg, size);
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
                                RoutingGlobal.CleanseCandidateTable(AppGlobal.AppPipe);
                                Candidate tmpBest = CandidateTable.GetBestCandidate(false);
                                AppGlobal.TempParent = tmpBest.GetMacID();
                                status = AppGlobal.SendToTempParent(AppGlobal.AppPipe, routedMsg, size);
                                if (status != 999)
                                {
                                    tmpBest.UpdateNumTriesInCurrentWindow(1);
#if !DBG_LOGIC
                                    Debug.Print("Updated numTriesInCurrentWindow for TempParent " + AppGlobal.TempParent + "; new value = " + tmpBest.GetNumTriesInCurrentWindow());
#endif
                                }
                            }
                        }
                        #endregion
#elif BASE_STATION
                        var msg = AppGlobal.PCMessages.Compose.Detection(originator, classificationType, detectionNumber);
                        try
                        {
                            _serialComm.Write(msg);
#if DBG_VERBOSE
							Debug.Print("\n************ Detection sent to PC " + msg.Substring(1,msg.Length-2));
#endif
                        }
                        catch (Exception ex)
                        {
                            Debug.Print("SerialComm exception for Detection message [" + msg + "]\n" + ex);
                        }
#endif
                        break;
                        }
                    case AppGlobal.MessageIds.Send:
                        {
                        AppGlobal.ClassificationType classificationType;
                        ushort originator;
                        byte TTL;
                        ushort pathLength;
                        ushort sndNumber;


                        _lcd.Write("PSnd");
                        ushort[] path = AppGlobal.MoteMessages.Parse.SendPacket(rcvPayloadBytes, out classificationType, out sndNumber, out originator, out TTL, out pathLength);
                        
                        Debug.Print("\tSend. From neighbor " + packet.Src + " # " + sndNumber + ". Classification " + (char)classificationType + " created by " + originator + " with TTL " + TTL);
#if CLIENT_NODE
                        Debug.Print("\tClient Recieved a send message...");
                        //_serialComm.Write(rcvPayloadBytes);
#elif RELAY_NODE
                        //	Check if originated by self or if TTL-1 = 0
                        if (originator == AppGlobal.AppPipe.MACRadioObj.RadioAddress || --TTL == 0)
                        {
                            return;
                        }

                        #region Uncomment when scheduler disabled
                        // If in a reset, do not forward TODO: Change this to "spray"
                        if (RoutingGlobal._color == Color.Red)
                        {
#if DBG_VERBOSE
                            Debug.Print("\tIn a Reset wave... not forwarded");
#endif
                            return;
                        }
                        #endregion
                        #region Uncomment when scheduler disabled
                        /* TODO: Uncomment lines 368-394 when not using scheduler*/
                        // Not originated by self. 
                        // If parent is available, pass it on
                        ushort new_path_length = (ushort)(pathLength + 1);
                        // remove ushort in length because popping first in path
                        byte[] routedMsg = new byte[rcvPayloadBytes.Length + sizeof(ushort)];
                        ushort[] newPath = new ushort[new_path_length];
                        Array.Copy(path, 0, newPath, 0, pathLength);
                        newPath[pathLength] = AppGlobal.AppPipe.MACRadioObj.RadioAddress;
                        if (RoutingGlobal.IsParent)
                        {
                            var size = AppGlobal.MoteMessages.Compose.SendPacket(routedMsg, originator, classificationType, sndNumber, TTL, new_path_length, newPath);
                            var status = RoutingGlobal.SendToParent(AppGlobal.AppPipe, routedMsg, size);
                            //Neighbor(AppGlobal.AppPipe, next_neighbor, routedMsg, size);
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
                                status = AppGlobal.SendToTempParent(AppGlobal.AppPipe, routedMsg, size);
                                if (status != 999)
                                {
                                    //tmpBest.UpdateNumTriesInCurrentWindow(1);
#if !DBG_LOGIC
                                    //Debug.Print("Updated numTriesInCurrentWindow for TempParent " + AppGlobal.TempParent + "; new value = " + tmpBest.GetNumTriesInCurrentWindow());
#endif
                                }
                            }
                        }
                        #endregion
#elif BASE_STATION
                        byte sendPayload = rcvPayloadBytes[rcvPayloadBytes.Length - 1];
                        byte replyPayload = GetBaseReply(sendPayload);
                        ushort next_neighbor = path[pathLength - 1];
                        var size = AppGlobal.MoteMessages.Compose.RecievePacket(rcvPayloadBytes, originator, classificationType, sndNumber, TTL, pathLength, path);
                        rcvPayloadBytes[rcvPayloadBytes.Length - 1] = replyPayload;
                        try
                        {
                            var status = RoutingGlobal.SendToNeighbor(AppGlobal.AppPipe, next_neighbor, rcvPayloadBytes, size);

#if DBG_VERBOSE
							Debug.Print("\n************ Detection sent to PC " + msg.Substring(1,msg.Length-2));
#endif
                        }
                        catch (Exception ex)
                        {
                            Debug.Print("SerialComm exception for Detection message [" + size + "]\n" + ex);
                        }
#endif
                        break;
                        }
                    case AppGlobal.MessageIds.Recieve:
                        {
                        AppGlobal.ClassificationType classificationType;
                        ushort originator;
                        byte TTL;
                        ushort pathLength;
                        ushort cur_node;
                        ushort rcvNumber;
                        _lcd.Write("PRcv");
                        ushort[] rest_of_path = AppGlobal.MoteMessages.Parse.RecievePacket(rcvPayloadBytes, out classificationType, out rcvNumber, out originator, out TTL, out pathLength, out cur_node);

                        Debug.Print("\tRecieve. From neighbor " + packet.Src + " # " + rcvNumber + ". Classification " + (char)classificationType + " created by " + originator + " with TTL " + TTL);
#if CLIENT_NODE
                        _serialComm.Write(rcvPayloadBytes);
#elif RELAY_NODE
                        //	Check if originated by self or if TTL-1 = 0
                        if (originator == AppGlobal.AppPipe.MACRadioObj.RadioAddress || --TTL == 0)
                        {
                            return;
                        }

                        #region Uncomment when scheduler disabled
                        // If in a reset, do not forward TODO: Change this to "spray"
                        if (RoutingGlobal._color == Color.Red)
                        {
#if DBG_VERBOSE
                            Debug.Print("\tIn a Reset wave... not forwarded");
#endif
                            return;
                        }
                        #endregion
                        #region Uncomment when scheduler disabled
                        /* TODO: Uncomment lines 368-394 when not using scheduler*/
                        // Not originated by self. 
                        // If parent is available, pass it on
                        ushort new_path_length = (ushort)(pathLength - 1);
                        byte[] routedMsg = new byte[rcvPayloadBytes.Length - sizeof(ushort)];
                        ushort next_neighbor = rest_of_path[new_path_length - 1];
                        var size = AppGlobal.MoteMessages.Compose.RecievePacket(routedMsg, originator, classificationType, rcvNumber, TTL, new_path_length, rest_of_path);
                        var status = RoutingGlobal.SendToNeighbor(AppGlobal.AppPipe, next_neighbor, routedMsg, size);
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
                            status = RoutingGlobal.SendToNeighbor(AppGlobal.AppPipe, next_neighbor, routedMsg, size);

                            if (status != 999)
                            {
                                //tmpBest.UpdateNumTriesInCurrentWindow(1);
#if !DBG_LOGIC
                                //Debug.Print("Updated numTriesInCurrentWindow for TempParent " + AppGlobal.TempParent + "; new value = " + tmpBest.GetNumTriesInCurrentWindow());
#endif
                            }
                        }
                        #endregion
#elif BASE_STATION


#endif
                        break;
                        }
                    default:
                        Debug.Print("AppPipeReceive unknown rcvPayloadBytes[0] ");
                        throw new ArgumentOutOfRangeException();
                }
                #region unused
                ////var rcvPayloadChar = Encoding.UTF8.GetChars(rcvPayloadBytes);
                ////var payload = new string(rcvPayloadChar);

                //if (payload.Substring(0, 5).Equals("Human")) //Data packets--human
                //{
                //	Debug.Print("\n\tReceived decision: " + payload.Substring(0, 5) + "; source: " + payload.Substring(5) + "; from Node: " + packet.Src);
                //	_lcd.Write("" + (++_numData));
                //}

                //if (RoutingGlobal.Parent == SystemGlobal.NoParent)
                //{
                //	Debug.Print("\tNo parent specified ");
                //	return;
                //}
                ////Debug.Print("\t\t*** Parent: "+ Parent+", SelfAddress: "+SelfAddress);
                //if (RoutingGlobal.Parent == _routing.SelfAddress)
                //{
                //	Debug.Print("\tAt base");
                //	return;
                //}
                //var toSendByte = Encoding.UTF8.GetBytes(payload);
                //var status = AppPipe.Send(RoutingGlobal.Parent, toSendByte, 0, (ushort)toSendByte.Length);
                //if (status != NetOpStatus.S_Success)
                //{
                //	Debug.Print("~~~~~~~~~~~~~~~~~~~~~~~~~~~~~ Send status: " + status);
                //}

                //SystemGlobal.PrintNumericVals("App Snd: ", toSendByte);

                //Debug.Print("Forwarded to parent node: " + RoutingGlobal.Parent);
                #endregion
            }
            catch (Exception e)
            {
                Debug.Print(e.ToString());
            }
        }
    }
}
#endif