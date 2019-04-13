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
#elif FAKE_FENCE
// Fake fence node
#elif PC
// PC node
#else
#error Invalid node type. Valid options: BASE_STATION, RELAY_NODE, FAKE_FENCE, PC
#endif

#if BASE_STATION || RELAY_NODE || FAKE_FENCE	// This code only applies to eMote nodes
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
#if RELAY_NODE
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

#if BASE_STATION
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
                        AppGlobal.ClassificationType classificationType;
                        ushort detectionNumber;
                        ushort originator;
                        byte TTL;

                        AppGlobal.MoteMessages.Parse.Detection(rcvPayloadBytes, out classificationType, out detectionNumber, out originator, out TTL);

                        Debug.Print("\tDetect. From neighbor " + packet.Src + " # " + detectionNumber + ". Classification " + (char)classificationType + " created by " + originator + " with TTL " + TTL);
#if RELAY_NODE
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