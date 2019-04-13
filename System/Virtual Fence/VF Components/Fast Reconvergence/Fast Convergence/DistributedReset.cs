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

using System;
using System.Threading;
using Microsoft.SPOT;
using BitConverter = Samraksh.Components.Utility.BitConverter;
using Math = System.Math;
using Samraksh.eMote.Net;
using Samraksh.eMote.Net.MAC;

namespace Samraksh.VirtualFence.Components
{
    public class DistributedReset
    {
        internal class ChildrenList
        {
            internal ushort[] _childrenList;
            internal byte _length;
            internal byte _maxLength;

            internal ChildrenList()
            {
                _length = 0;
                _childrenList = MACBase.NeighborListArray(); // Initialize array;
                _maxLength = (byte)_childrenList.Length;
            }

            internal bool AddChild(ushort macID)
            {
                if (IsFull())
                {
#if !DBG_LOGIC
                    Debug.Print("ERROR: Children list is full");
#endif
                }
                else if (Array.IndexOf(_childrenList, macID) == -1) // Add new node
                {
                    _childrenList[_length++] = macID;
                    return true;
                }
                return false;
            }

            internal byte findIndex(ushort macID)
            {
                for (byte i = 0; i < _length; i++)
                    if (_childrenList[i] == macID)
                        return i;
                return byte.MaxValue;
            }

            internal bool DropChild(ushort macID)
            {
                if (!IsEmpty())
                {
                    int index = Array.IndexOf(_childrenList, macID);
                    if (index > -1)
                    {
                        // Shift all right candidates left by 1 position
                        for (int j = index + 1; j < _length; j++)
                            _childrenList[j - 1] = _childrenList[j];

                        // Reset last element
                        _childrenList[--_length] = 0;
                        return true;
                    }
                }
                return false;
            }

            internal void CleanseChildrenTable()
            {
                ushort[] _neighborList = MACBase.NeighborListArray();
                _distResetPipe.NeighborList(_neighborList); // Get current neighborlist
                foreach (ushort c in _childrenList)
                {
                    if (Array.IndexOf(_neighborList, c) == -1)
                    {
#if DBG_VERBOSE
                        Debug.Print("--- CHILDREN LIST CLEANUP: Removing stale child: " + c + " ---");
#elif DBG_SIMPLE
                        Debug.Print("Removing stale child: " + c);
#endif
                        DropChild(c);
                    }
                }
            }

            internal bool IsFull()
            {
                return _length == _maxLength;
            }

            internal bool IsEmpty()
            {
                return _length == 0;
            }
        }

        private static ChildrenList _children;
        private static MACPipe _distResetPipe;
        private static int _addMsgNum;
        private static int _dropMsgNum;

        private static bool[] _completionMsgs;
        private static int _resetMsgNum;
        private static int _statusMsgNum;

        private static Timer _distResetTimer;
        private static Timer _statusResponseTimer;

        public static bool IsInitialized = false;

        public static void Initialize(MACBase macBase)
        {
            if (!IsInitialized)
            {
#if !DBG_LOGIC
            Debug.Print("\n*** Initializing Distributed Reset Module ***\n");
#endif
                //_netManagerPipe = new SimpleCSMAStreamChannel(macBase, (byte)AppGlobal.MacPipeIds.NetworkManager);
                _distResetPipe = new MACPipe(macBase, SystemGlobal.MacPipeIds.DistReset);
                _distResetPipe.OnReceive += DistResetStreamReceive;
#if !DBG_LOGIC
            Debug.Print("***** subscribing to Distributed Reset on " + SystemGlobal.MacPipeIds.DistReset);
#endif
                _children = new ChildrenList();
                RoutingGlobal._color = Color.Green;
                _distResetTimer = new Timer(Reset_State, null, Timeout.Infinite, Timeout.Infinite);
                _statusResponseTimer = new Timer(Status_Response, null, Timeout.Infinite, Timeout.Infinite);
                IsInitialized = true;
            }
        }

        private static void Status_Response(object state)
        {
#if DBG_VERBOSE
            Debug.Print("*** STATUS TIMEOUT FIRED: query# " + _statusMsgNum + "; setting new parent ***");
#elif DBG_SIMPLE
            Debug.Print("Setting new parent after status query#" + _statusMsgNum);
#endif
            // Try to switch to a new parent
            // Check if candidate table has valid entries
            RoutingGlobal.CleanseCandidateTable(_distResetPipe);
            // Set best node in candidate table as parent
            RoutingGlobal.SetParent(false);

            // Reset params
            RoutingGlobal._color = Color.Green;
            // Send "Add Parent" message to new parent
            DistributedReset.Send_AddParent();
            _statusMsgNum++;
        }

        public static int getResetMessageNum() { return _resetMsgNum; }

        public static byte findChild(ushort macID)
        {
            return _children.findIndex(macID);
        }

        public static bool AddChild(ushort macID)
        {
            return _children.AddChild(macID);
        }

        public static bool DropChild(ushort macID)
        {
            return _children.DropChild(macID);
        }

        private static void Reset_State(object state)
        {
#if DBG_VERBOSE
            Debug.Print("*** RESET WAVE TIMEOUT FIRED: Initiating completion wave# " + _resetMsgNum + " ***");
#elif DBG_SIMPLE
            Debug.Print("Initiating completion wave " + _resetMsgNum + "");
#endif
            Send_CompletionWave(_resetMsgNum);

            // Purge children table
            _children = new ChildrenList();
            _completionMsgs = null;

            // Create status query to existing candidates
            var msgBytes = new byte[3];
            var size = ComposeMessages.CreateStatusQueryMessage(msgBytes, (ushort)_statusMsgNum);

            // Multicast query to candidates
            if (MulticastToCandidates(msgBytes, size)) // If there are candidates
            {
                // Wait for response
                _statusResponseTimer.Change(10000, Timeout.Infinite); // 10 second timer

                // Reset candidate table
                ushort[] neighbors = MACBase.NeighborListArray();
                CandidateTable.Initialize((byte)neighbors.Length);
            }
            else //Empty candidate table; set parent immediately
            {
                // Set best node in candidate table as parent
                RoutingGlobal.SetParent(false);

                // Reset params
                RoutingGlobal._color = Color.Green;
                // Send "Add Parent" message to new parent
                DistributedReset.Send_AddParent();
                _statusMsgNum++;
            }

            _resetMsgNum++; // No more completion messages to be accepted beyond this point
        }

        public static void Send_AddParent()
        {
            _addMsgNum++;
            var msgBytes = new byte[3];
            var size = ComposeMessages.CreateAddParentMessage(msgBytes, (ushort)_addMsgNum);
            NetOpStatus status = _distResetPipe.Send(RoutingGlobal.Parent, msgBytes, 0, (ushort)size);

#if DBG_VERBOSE
            if (status != NetOpStatus.S_Success)
            {
                Debug.Print("~~~~~~~~~~~~~~~~~~~~~~~~~~~~~ Send status: " + status);
            }

            Debug.Print("Sent \"Added Parent\" message to " + RoutingGlobal.Parent);
#elif DBG_SIMPLE
            Debug.Print("Sent \"Added Parent\" message to " + RoutingGlobal.Parent + ", status: " + status);
#endif
        }

        public static void Send_DropParent()
        {
            _dropMsgNum++;
            var msgBytes = new byte[3];
            var size = ComposeMessages.CreateDropParentMessage(msgBytes, (ushort)_dropMsgNum);
            NetOpStatus status = _distResetPipe.Send(RoutingGlobal.ExParent, msgBytes, 0, (ushort)size);
#if DBG_VERBOSE
            if (status != NetOpStatus.S_Success)
            {
                Debug.Print("~~~~~~~~~~~~~~~~~~~~~~~~~~~~~ Send status: " + status);
            }

            Debug.Print("Sent \"Dropped Parent\" message to " + RoutingGlobal.Parent);
#elif DBG_SIMPLE
            Debug.Print("Sent \"Dropped Parent\" message to " + RoutingGlobal.Parent + ", status: " + status);
#endif
        }

        private static void Send_CompletionWave(int round)
        {
            var msgBytes = new byte[3];
            var size = ComposeMessages.CreateCompletionMessage(msgBytes, (ushort)round);
            NetOpStatus status = _distResetPipe.Send(RoutingGlobal.Parent, msgBytes, 0, (ushort)size);
#if DBG_VERBOSE
            if (status != NetOpStatus.S_Success)
            {
                Debug.Print("~~~~~~~~~~~~~~~~~~~~~~~~~~~~~ Send status: " + status);
            }

            Debug.Print("Sent Completion Wave " + round + " to " + RoutingGlobal.Parent);
#elif DBG_SIMPLE
            Debug.Print("Sent Completion Wave " + round + " to " + RoutingGlobal.Parent + ", status: " + status);
#endif
        }

        public static void Send_ResetWave()
        {
            // If in a reset wave already, return.
            if(RoutingGlobal._color == Color.Red)
                return;

            // Refresh children table
            _children.CleanseChildrenTable();

            if (!_children.IsEmpty())
            {
                RoutingGlobal._color = Color.Red;
                var msgBytes = new byte[3];
                var size = ComposeMessages.CreateResetMessage(msgBytes, (ushort)_resetMsgNum);
                MulticastToChildren(msgBytes, size);
                _distResetTimer.Change(120000, Timeout.Infinite); // 2 min timer. TODO: Make timeout interval contingent on distance from the reset wave origin
            }
            else
            {
                // Create status query to existing candidates
                var msgBytes = new byte[3];
                var size = ComposeMessages.CreateStatusQueryMessage(msgBytes, (ushort)_statusMsgNum);

                // Multicast query to candidates
                if (MulticastToCandidates(msgBytes, size)) // If there are candidates
                {
                    // Wait for response
                    _statusResponseTimer.Change(10000, Timeout.Infinite); // 10 second timer

                    // Reset candidate table
                    ushort[] neighbors = MACBase.NeighborListArray();
                    CandidateTable.Initialize((byte)neighbors.Length);
                }
                else //Empty candidate table; set parent immediately
                {
                    // Set best node in candidate table as parent
                    RoutingGlobal.SetParent(false);

                    // Reset params
                    RoutingGlobal._color = Color.Green;
                    // Send "Add Parent" message to new parent
                    DistributedReset.Send_AddParent();
                    _statusMsgNum++;
                    _resetMsgNum++;
                }
            }
        }

        public static void Send_ResetWave(byte[] payloadBytes)
        {
            RoutingGlobal._color = Color.Red;
            MulticastToChildren(payloadBytes, payloadBytes.Length);
            _distResetTimer.Change(120000, Timeout.Infinite); // 2 min timer. TODO: Make timeout interval contingent on distance from the reset wave origin
        }

        private static bool MulticastToCandidates(byte[] message, int messageLength)
        {
            ushort[] candidates = CandidateTable.GetCandidateNames();

            if (candidates != null)
            {
#if DBG_VERBOSE
                SystemGlobal.PrintNumericVals("Candidate List [for " + _distResetPipe.MACRadioObj.RadioAddress + "] ", candidates);
#endif
            }
            else
            {
                return false;
            }

#if DBG_VERBOSE
            if (_distResetPipe != null)
            {
                SystemGlobal.PrintNumericVals("Multicast (on MACPipe " + _distResetPipe.PayloadType + "): ", message, messageLength);
            }
            else
            {
                SystemGlobal.PrintNumericVals("Multicast: ", message, messageLength);
            }
#endif
            foreach (var theNeighbor in candidates)
            {
                if (theNeighbor == 0)
                {
                    continue;
                }
                var status = _distResetPipe.Send(theNeighbor, message, 0, (ushort)messageLength);
#if DBG_VERBOSE
                if (status != NetOpStatus.S_Success)
                {
                    Debug.Print("~~~~~~~~~~~~~~~~~~~~~~~~~~~~~ Send status: " + status);
                }

                Debug.Print("\tSent to " + theNeighbor);
#elif DBG_SIMPLE
                Debug.Print("\tSent to " + theNeighbor + ", status: " + status);
#endif
            }
            return true;
        }

        private static void MulticastToChildren(byte[] message, int messageLength)
        {
#if DBG_VERBOSE
            SystemGlobal.PrintNumericVals("Children List [for " + _distResetPipe.MACRadioObj.RadioAddress + "] ", _children._childrenList);
#endif

#if DBG_VERBOSE
            if (_distResetPipe != null)
            {
                SystemGlobal.PrintNumericVals("Multicast (on MACPipe " + _distResetPipe.PayloadType + "): ", message, messageLength);
            }
            else
            {
                SystemGlobal.PrintNumericVals("Multicast: ", message, messageLength);
            }
#endif

            // Refresh children table
            _children.CleanseChildrenTable();
            _completionMsgs = new bool[_children._length];

            foreach (var theNeighbor in _children._childrenList)
            {
                if (theNeighbor == 0)
                {
                    continue;
                }
                var status = _distResetPipe.Send(theNeighbor, message, 0, (ushort)messageLength);
#if DBG_VERBOSE
                if (status != NetOpStatus.S_Success)
                {
                    Debug.Print("~~~~~~~~~~~~~~~~~~~~~~~~~~~~~ Send status: " + status);
                }

                Debug.Print("\tSent to " + theNeighbor);
#elif DBG_SIMPLE
                Debug.Print("\tSent to " + theNeighbor + ", status: " + status);
#endif
            }
        }

        private static void DistResetStreamReceive(IMAC macBase, DateTime time, Packet packet)
        {
#if DBG_VERBOSE
            DebuggingSupport.PrintMessageReceived(macBase, "Distributed Reset");
#endif

            if (packet == null)
            {
                return;
            }
#if !DBG_LOGIC
            Debug.Print("\tRSSI: " + packet.RSSI + ", from " + packet.Src);
#endif

            var rcvPayloadBytes = packet.Payload;

            //var payload = new string(Encoding.UTF8.GetChars(rcvPayloadBytes));
#if DBG_VERBOSE
            SystemGlobal.PrintNumericVals("\tDistributed reset Rcv: ", rcvPayloadBytes);
#endif
            try
            {
                ushort[] neighbors = MACBase.NeighborListArray();


                var neighborStatus = _distResetPipe.NeighborStatus(packet.Src);
                if (neighborStatus == null)
                {
#if DBG_VERBOSE
                    Debug.Print("\t\t!!!!!!!!!!!!!!!!! Node " + packet.Src + " is not a Neighbor (MACBase.NeighborStatus returned null)");
#elif DBG_SIMPLE
                    Debug.Print("Sender not a neighbor.");
#endif
                    // If node in children table, drop it
                    if (Array.IndexOf(_children._childrenList, packet.Src) < -1)
                        _children.DropChild(packet.Src);

                    return;
                }
                //Debug.Print("\t\tFwd avg RSSI: " + neighborStatus.ReceiveLink.AverageRSSI + ", Rev avg RSSI: " + neighborStatus.SendLink.AverageRSSI);

                switch ((MessageIds)rcvPayloadBytes[0])
                {
                    case MessageIds.AddParent:
#if !DBG_LOGIC
                        Debug.Print("\t>>> AddParent");
#endif
                        if (_children.AddChild(packet.Src))
                        {
#if DBG_VERBOSE
                            Debug.Print("+++ Added new child: " + packet.Src + " +++");
#elif DBG_SIMPLE
                            Debug.Print("Added new child: " + packet.Src);
#endif
                        }
                        else
                        {
#if DBG_VERBOSE
                            Debug.Print("@@@ Child already exists: " + packet.Src + " @@@");
#endif
                        }

                        return;

                    case MessageIds.DropParent:
#if !DBG_LOGIC
                        Debug.Print("\t>>> DropParent");
#endif

                        if (_children.DropChild(packet.Src))
                        {
#if DBG_VERBOSE
                            Debug.Print("--- Dropped child: " + packet.Src + " ---");
#elif DBG_SIMPLE
                            Debug.Print("Dropped child: " + packet.Src);
#endif
                        }
                        else
                        {
#if DBG_VERBOSE
                            Debug.Print("@@@ Child does not exist: " + packet.Src + " @@@");
#endif
                        }

                        return;

                    case MessageIds.Reset:
#if !DBG_LOGIC
                        Debug.Print("\t>>> ResetWave");
#endif
                        // Decode round number
                        ushort round_num;
                        ParseMessages.ParseResetMessage(rcvPayloadBytes, out round_num);

                        // Is this a legit reset wave?
                        if (RoutingGlobal._color == Color.Red || packet.Src != RoutingGlobal.Parent || round_num != _resetMsgNum)
                        {
#if DBG_VERBOSE
                            Debug.Print("!!! ILLEGAL RESET WAVE# " + round_num + ": Received from: " + packet.Src + " !!!");
#elif DBG_SIMPLE
                            Debug.Print("Illegal reset wave " + round_num + " from " + packet.Src);
#endif
                            return;
                        }

                        // Refresh children table
                        _children.CleanseChildrenTable();

                        if (_children.IsEmpty()) // Start completion wave if leaf node
                        {
#if DBG_VERBOSE
                            Debug.Print("*** LEAF NODE REACHED: Initiating completion wave# " + round_num + " ***");
#elif DBG_SIMPLE
                            Debug.Print("At leaf; initiating completion wave " + round_num);
#endif
                            Send_CompletionWave(round_num);

                            // Create status query to existing candidates
                            var msgBytes = new byte[3];
                            var size = ComposeMessages.CreateStatusQueryMessage(msgBytes, (ushort)_statusMsgNum);

                            // Multicast query to candidates
                            if (MulticastToCandidates(msgBytes, size)) // If there are candidates
                            {
                                // Wait for response
                                _statusResponseTimer.Change(10000, Timeout.Infinite); // 10 second timer

                                // Reset candidate table
                                CandidateTable.Initialize((byte)neighbors.Length);
                            }
                            else //Empty candidate table; set parent immediately
                            {
                                // Set best node in candidate table as parent
                                RoutingGlobal.SetParent(false);

                                // Reset params
                                RoutingGlobal._color = Color.Green;
                                // Send "Add Parent" message to new parent
                                DistributedReset.Send_AddParent();
                                _statusMsgNum++;
                            }

                            _resetMsgNum++;
                        }
                        else
                        {
                            // Forward reset wave to own children
                            Send_ResetWave(rcvPayloadBytes);
                        }
                        return;

                    case MessageIds.Completion:
#if !DBG_LOGIC
                        Debug.Print("\t>>> CompletionWave");
#endif
                        // Decode round number
                        ushort round;
                        ParseMessages.ParseCompletionMessage(rcvPayloadBytes, out round);

                        // Is this a legit completion wave?
                        int pos = Array.IndexOf(_children._childrenList, packet.Src);
                        if (RoutingGlobal._color != Color.Red || pos == -1 || round != _resetMsgNum)
                        {
#if DBG_VERBOSE
                            Debug.Print("!!! ILLEGAL COMPLETION WAVE# " + round + ": Received from: " + packet.Src + " !!!");
#elif DBG_SIMPLE
                            Debug.Print("Illegal completion wave " + round + " from " + packet.Src);
#endif
                            return;
                        }

                        _completionMsgs[pos] = true;

                        // Forward completion wave if received from all children
                        for (int i = 0; i < _completionMsgs.Length; i++)
                        {
                            if (!_completionMsgs[i]) // If not received from any child, return
                                return;
                        }

                        // Else, forward completion wave to parent
#if DBG_VERBOSE
                        Debug.Print("*** RECEIVED COMPLETION MESSAGES FROM ALL CHILDREN: initiating completion wave# " + round + " ***");
#elif DBG_SIMPLE
                            Debug.Print("All children responded; initiating completion wave " + round);
#endif
                        Send_CompletionWave(round);

                        // Purge children table
                        _children = new ChildrenList();
                        _completionMsgs = null;
                        _distResetTimer.Change(Timeout.Infinite, Timeout.Infinite);

                        // Create status query to existing candidates
                        var msgBytes1 = new byte[3];
                        var size1 = ComposeMessages.CreateStatusQueryMessage(msgBytes1, (ushort)_statusMsgNum);

                        // Multicast query to candidates
                        if (MulticastToCandidates(msgBytes1, size1)) // If there are candidates
                        {
                            // Wait for response
                            _statusResponseTimer.Change(10000, Timeout.Infinite); // 10 second timer

                            // Reset candidate table
                            CandidateTable.Initialize((byte)neighbors.Length);
                        }
                        else //Empty candidate table; set parent immediately
                        {
                            // Set best node in candidate table as parent
                            RoutingGlobal.SetParent(false);

                            // Reset params
                            RoutingGlobal._color = Color.Green;
                            // Send "Add Parent" message to new parent
                            DistributedReset.Send_AddParent();
                            _statusMsgNum++;
                        }

                        _resetMsgNum++;
                        return;

                    case MessageIds.StatusQuery:
#if !DBG_LOGIC
                        Debug.Print("\t>>> StatusQuery");
#endif
                        //If in a reset wave or no valid path, don't bother responding
                        if (RoutingGlobal._color == Color.Red || RoutingGlobal.BestEtx >= RoutingGlobal.Infinity)
                        {
#if DBG_VERBOSE
                            Debug.Print("!!! Not responding. Color: " + RoutingGlobal._color + ", BestEtx: " + RoutingGlobal.BestEtx + " !!!");
#elif DBG_SIMPLE
                            Debug.Print("Not responding. Color: " + RoutingGlobal._color + ", BestEtx: " + RoutingGlobal.BestEtx);
#endif
                            return;
                        }

                        // Decode round number
                        ushort status_msgnum;
                        ParseMessages.ParseStatusQueryMessage(rcvPayloadBytes, out status_msgnum);

                        // Send status response
                        var msgBytes2 = new byte[5];
                        var size2 = ComposeMessages.CreateStatusResponseMessage(msgBytes2, status_msgnum);
                        NetOpStatus status = _distResetPipe.Send(RoutingGlobal.Parent, msgBytes2, 0, (ushort)size2);

                        return;

                    case MessageIds.StatusResponse:
#if !DBG_LOGIC
                        Debug.Print("\t>>> StatusResponse");
#endif
                        ushort status_respnum;
                        Color col;
                        byte etx;
                        byte pathEWRNP_B2N;

                        // Decode message
                        ParseMessages.ParseStatusResponseMessage(rcvPayloadBytes, out col, out etx, out status_respnum, out pathEWRNP_B2N);

                        // Is this a valid response?
                        if (status_respnum != _statusMsgNum)
                        {
#if DBG_VERBOSE
                            Debug.Print("!!! ILLEGAL STATUS RESPONSE# " + status_respnum + ": Received from: " + packet.Src + " !!!");
#elif DBG_SIMPLE
                            Debug.Print("Illegal status response " + status_respnum + " from " + packet.Src);
#endif
                            return;
                        }

                        byte tempEtx = (byte)(etx + 1);

                        // Add candidate if its offered etx exceeds _minEtx
                        if (tempEtx >= RoutingGlobal._minEtx)
                        {
                            CandidateTable.AddCandidate(packet.Src, 0, tempEtx, pathEWRNP_B2N, RoutingGlobal.MaxEtx); // TODO: Add RSSI later
#if DBG_VERBOSE
                            Debug.Print("+++ Added new candidate: " + packet.Src + "; path length: " + tempEtx + " +++");
#elif DBG_SIMPLE
                            Debug.Print("New candidate " + packet.Src + ":[" + tempEtx + "]");
#endif
                        }
                        else
                        {
#if DBG_VERBOSE
                            Debug.Print("--- Not a candidate: " + packet.Src + "; path length: " + tempEtx + " ---");
#elif DBG_SIMPLE
                            Debug.Print("Not a candidate " + packet.Src + ":[" + tempEtx + "]");
#endif
                        }

                        return;

                    default:
#if !DBG_LOGIC
                        Debug.Print("\tUnknown message received <" + rcvPayloadBytes[0] + ">");
#endif
                        break;
                }
            }
            catch (Exception e) { Debug.Print(e.StackTrace); }
        }

        /// <summary>
        /// Tree maintenance message IDs
        /// </summary>
        internal enum MessageIds : byte
        {
            /// <summary>Added parent notification</summary>
            AddParent = 0,

            /// <summary>Dropped parent notification</summary>
            DropParent = 1,

            /// <summary>Dropped child notification</summary>
            DropChild = 2, // Not used

            /// <summary>Reset wave</summary>
            Reset = 3,

            /// <summary>Completion wave</summary>
            Completion = 4,

            /// <summary>Status query</summary>
            StatusQuery = 5,

            /// <summary>Status response</summary>
            StatusResponse = 6,
        }


        /// <summary>
        /// Create messages to send
        /// </summary>
        internal static class ComposeMessages
        {
            internal static int CreateAddParentMessage(byte[] msgBytes, ushort beaconNum)
            {
                var idx = 0;
                msgBytes[idx] = (byte)MessageIds.AddParent;
                idx += sizeof(byte);

                var beaconAdj = (ushort)Math.Min(beaconNum, ushort.MaxValue);
                BitConverter.InsertValueIntoArray(msgBytes, idx, beaconAdj);
                idx += sizeof(ushort);

                return idx;
            }

            internal static int CreateDropParentMessage(byte[] msgBytes, ushort beaconNum)
            {
                var idx = 0;
                msgBytes[idx] = (byte)MessageIds.DropParent;
                idx += sizeof(byte);

                var beaconAdj = (ushort)Math.Min(beaconNum, ushort.MaxValue);
                BitConverter.InsertValueIntoArray(msgBytes, idx, beaconAdj);
                idx += sizeof(ushort);

                return idx;
            }

            internal static int CreateResetMessage(byte[] msgBytes, ushort beaconNum)
            {
                var idx = 0;
                msgBytes[idx] = (byte)MessageIds.Reset;
                idx += sizeof(byte);

                var beaconAdj = (ushort)Math.Min(beaconNum, ushort.MaxValue);
                BitConverter.InsertValueIntoArray(msgBytes, idx, beaconAdj);
                idx += sizeof(ushort);

                return idx;
            }

            internal static int CreateCompletionMessage(byte[] msgBytes, ushort beaconNum)
            {
                var idx = 0;
                msgBytes[idx] = (byte)MessageIds.Completion;
                idx += sizeof(byte);

                var beaconAdj = (ushort)Math.Min(beaconNum, ushort.MaxValue);
                BitConverter.InsertValueIntoArray(msgBytes, idx, beaconAdj);
                idx += sizeof(ushort);

                return idx;
            }

            internal static int CreateStatusQueryMessage(byte[] msgBytes, ushort beaconNum)
            {
                var idx = 0;
                msgBytes[idx] = (byte)MessageIds.StatusQuery;
                idx += sizeof(byte);

                var beaconAdj = (ushort)Math.Min(beaconNum, ushort.MaxValue);
                BitConverter.InsertValueIntoArray(msgBytes, idx, beaconAdj);
                idx += sizeof(ushort);

                return idx;
            }

            internal static int CreateStatusResponseMessage(byte[] msgBytes, ushort beaconNum)
            {
                var idx = 0;
                msgBytes[idx] = (byte)MessageIds.StatusResponse;
                idx += sizeof(byte);
                msgBytes[idx] = (byte)RoutingGlobal._color;
                idx += sizeof(byte);
                msgBytes[idx] = RoutingGlobal.BestEtx;
                idx += sizeof(byte);

                var beaconAdj = (ushort)Math.Min(beaconNum, ushort.MaxValue);
                BitConverter.InsertValueIntoArray(msgBytes, idx, beaconAdj);
                idx += sizeof(ushort);

                msgBytes[idx] = RoutingGlobal.GetPathEWRNP();
                idx = +sizeof(byte);

                return idx;
            }
        }

        /// <summary>
        /// Parse routing messages received
        /// </summary>
        internal static class ParseMessages
        {
            internal static void ParseAddParentMessage(byte[] msgBytes, out ushort beaconNum)
            {
                var idx = 1;
                beaconNum = BitConverter.ToUInt16(msgBytes, idx);
            }

            internal static void ParseDropParentMessage(byte[] msgBytes, out ushort beaconNum)
            {
                var idx = 1;
                beaconNum = BitConverter.ToUInt16(msgBytes, idx);
            }

            internal static void ParseResetMessage(byte[] msgBytes, out ushort beaconNum)
            {
                var idx = 1;
                beaconNum = BitConverter.ToUInt16(msgBytes, idx);
            }

            internal static void ParseCompletionMessage(byte[] msgBytes, out ushort beaconNum)
            {
                var idx = 1;
                beaconNum = BitConverter.ToUInt16(msgBytes, idx);
            }

            internal static void ParseStatusQueryMessage(byte[] msgBytes, out ushort beaconNum)
            {
                var idx = 1;
                beaconNum = BitConverter.ToUInt16(msgBytes, idx);
            }

            internal static void ParseStatusResponseMessage(byte[] msgBytes, out Color color, out byte etx, out ushort beaconNum, out byte pathEWRNP_B2N)
            {
                var idx = 1;
                color = (Color)msgBytes[idx];
                idx += sizeof(byte);

                etx = msgBytes[idx];
                idx += sizeof(byte);

                beaconNum = BitConverter.ToUInt16(msgBytes, idx);
                idx += sizeof(ushort);

                pathEWRNP_B2N = msgBytes[idx];
                idx += sizeof(byte);
            }
        }
    }
}