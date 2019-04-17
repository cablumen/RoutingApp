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
using System.Threading;
using Microsoft.SPOT;
using Samraksh.eMote.Net;
using Samraksh.eMote.Net.MAC;
//using Samraksh.Manager.LocalManager;
using BitConverter = Samraksh.Components.Utility.BitConverter;
using Math = System.Math;

namespace Samraksh.VirtualFence.Components
{
    /// <summary>
    /// Node colors
    /// </summary>
    public enum Color : byte
    {
        /// <summary>Green</summary>
        Green = 0,

        /// <summary>Red</summary>
        Red = 1,
    }

    // Candidate table
    public struct Candidate
    {
        private ushort macID;
        private int avgRSSI;
        private byte etx;
        private byte numReceivedInCurrentWindow;
        private byte numTriesInCurrentWindow;
        private double link_ewrnp;
        private byte path_ewrnp_Base_to_Candidate;
        private byte path_ewrnp;

        private Object thisLock;

        public Candidate(ushort macID, int avgRSSI, byte etx, byte path_ewrnp_B2C, byte path_ewrnp)
        {
            this.macID = macID;
            this.avgRSSI = avgRSSI;
            this.etx = etx;
            this.numReceivedInCurrentWindow = 0;
            this.numTriesInCurrentWindow = 0;
            this.link_ewrnp = 0;
            this.path_ewrnp_Base_to_Candidate = path_ewrnp_B2C;
            this.path_ewrnp = path_ewrnp;
            thisLock = new Object(); // Every candidate has its own lock
        }

        public void UpdateMetrics(int avgRSSI, byte etx, byte path_ewrnp_B2C)
        {
            this.avgRSSI = avgRSSI;
            this.etx = etx;
            this.path_ewrnp_Base_to_Candidate = path_ewrnp_B2C;
        }

        public void UpdateNumReceivedInCurrentWindow(byte num)
        {
            lock (thisLock)
            {
                this.numReceivedInCurrentWindow += num;
            }
        }

        public void UpdateNumTriesInCurrentWindow(byte num)
        {
            lock (thisLock)
            {
                this.numTriesInCurrentWindow += num;
            }
        }

        private double CalculateLinkEWRNP()
        {
            if (this.numReceivedInCurrentWindow > 0)
            {
                this.link_ewrnp = (this.link_ewrnp == 0) ? (this.numTriesInCurrentWindow / this.numReceivedInCurrentWindow) : RoutingGlobal.alpha * link_ewrnp + (1 - RoutingGlobal.alpha) * (this.numTriesInCurrentWindow / this.numReceivedInCurrentWindow);
            }
            else
            {
                this.link_ewrnp = (this.link_ewrnp == 0) ? RoutingGlobal.MaxTriesPerPacket : RoutingGlobal.alpha * link_ewrnp + (1 - RoutingGlobal.alpha) * RoutingGlobal.MaxTriesPerPacket; // TODO: Change to chosen Infinity
            }
            return this.link_ewrnp;
        }

        public void SetPathEWRNP()
        {
            lock (thisLock)
            {
                path_ewrnp = (byte)(path_ewrnp_Base_to_Candidate + Math.Round(CalculateLinkEWRNP()));

                // Reset counts for next Window
                this.numReceivedInCurrentWindow = 0;
                this.numTriesInCurrentWindow = 0;
            }
        }

        public byte GetPathEWRNP_Base_to_Candidate() { return path_ewrnp_Base_to_Candidate; }
        public ushort GetMacID() { return macID; }
        public double GetRSSI() { return avgRSSI; }
        public byte GetEtx() { return etx; }
        public byte GetPathEWRNP() { return path_ewrnp; }
        public byte GetNumTriesInCurrentWindow() { return numTriesInCurrentWindow; }
        public byte GetNumReceivedInCurrentWindow() { return numReceivedInCurrentWindow; }
    }

    public static class CandidateTable
    {
        private static byte _length;
        private static byte _maxLength;
        public static Candidate[] _candidateList;
        private static bool _initialized;

        public static bool IsInitialized() { return _initialized; }

        public static void Initialize(byte length)
        {
            _length = 0;
            _maxLength = length;
            _candidateList = new Candidate[_maxLength];
            _initialized = true;
        }

        public static void AddCandidate(ushort macID, int avgRSSI, byte etx, byte path_ewrnp_B2C, byte path_ewrnp)
        {
            if (IsFull()) // Replace worst candidate
            {
                byte index = findIndex(GetWorstCandidate().GetMacID());
                _candidateList[index] = new Candidate(macID, avgRSSI, etx, path_ewrnp_B2C, path_ewrnp);
            }
            else // Add new node
            {
                _candidateList[_length++] = new Candidate(macID, avgRSSI, etx, path_ewrnp_B2C, path_ewrnp);
            }
        }

        public static Candidate GetWorstCandidate()
        {
            Candidate tmpWorst = new Candidate(0, int.MaxValue, byte.MinValue, byte.MinValue, byte.MinValue);

            if (!IsEmpty())
            {
                for (int i = 0; i < _length; i++)
                {
                    if (_candidateList[i].GetPathEWRNP() > tmpWorst.GetPathEWRNP() || (_candidateList[i].GetPathEWRNP() == tmpWorst.GetPathEWRNP() && _candidateList[i].GetEtx() > tmpWorst.GetEtx()) || (_candidateList[i].GetPathEWRNP() == tmpWorst.GetPathEWRNP() && _candidateList[i].GetEtx() == tmpWorst.GetEtx() && _candidateList[i].GetRSSI() < tmpWorst.GetRSSI()))
                        tmpWorst = _candidateList[i];
                }
            }

            return tmpWorst;
        }

        public static ushort[] GetCandidateNames()
        {
            if (!IsEmpty())
            {
                ushort[] candidates = new ushort[_length];
                for (int i = 0; i < _length; i++)
                {
                    candidates[i] = _candidateList[i].GetMacID();
                }
                return candidates;
            }
            else
                return null;
        }

        public static byte findIndex(ushort macID)
        {
            for (byte i = 0; i < _length; i++)
                if (_candidateList[i].GetMacID() == macID)
                    return i;
            return byte.MaxValue;
        }

        public static bool DropCandidate(ushort macID)
        {
            byte index = findIndex(macID);
            if (index < byte.MaxValue)
            {
                // Shift all right candidates left by 1 position
                for (int j = index + 1; j < _length; j++)
                    _candidateList[j - 1] = _candidateList[j];

                // Reset last element
                _candidateList[--_length] = new Candidate();
                return true;
            }
            return false;
        }

        public static Candidate GetBestCandidate(bool refresh)
        {
            Candidate tmpBest = new Candidate(0, int.MinValue, byte.MaxValue, byte.MaxValue, byte.MaxValue);

            if (!IsEmpty())
            {
                //bool candidateFound = false;

                for (int i = 0; i < _length; i++)
                {
                    if (refresh)
                    {
                        _candidateList[i].SetPathEWRNP();
                    }

                    if ((byte)_candidateList[i].GetPathEWRNP() < tmpBest.GetPathEWRNP() || (_candidateList[i].GetPathEWRNP() == tmpBest.GetPathEWRNP() && _candidateList[i].GetEtx() < tmpBest.GetEtx()) || (_candidateList[i].GetPathEWRNP() == tmpBest.GetPathEWRNP() && _candidateList[i].GetEtx() == tmpBest.GetEtx() && _candidateList[i].GetRSSI() > tmpBest.GetRSSI()))
                    {
                        tmpBest = _candidateList[i];
                    }
                    #region no longer using _minRSSI
                    //                    if (_candidateList[i].GetRSSI() >= RoutingGlobal._minRSSI)
                    //                    {
                    //                        if (_candidateList[i].GetPathEWRNP() < tmpBest.GetPathEWRNP() || (_candidateList[i].GetPathEWRNP() == tmpBest.GetPathEWRNP() && _candidateList[i].GetEtx() < tmpBest.GetEtx()) || (_candidateList[i].GetPathEWRNP() == tmpBest.GetPathEWRNP() && _candidateList[i].GetEtx() == tmpBest.GetEtx() && _candidateList[i].GetRSSI() > tmpBest.GetRSSI()))
                    //                        {
                    //                            tmpBest = _candidateList[i];
                    //                            candidateFound = true;
                    //#if DBG_SIMPLE
                    //                            Debug.Print("candidate found: " + tmpBest.GetMacID().ToString() + " RSSI: " + tmpBest.GetRSSI().ToString());
                    //#endif
                    //                        }
                    //                    }
                    #endregion
                }
            }

            return tmpBest;
        }

        public static Candidate GetBestCandidateRelativeToParent(bool refresh)
        {
            // Initializing parent as current best candidate
            Candidate tmpBest = new Candidate(RoutingGlobal.Parent, RoutingGlobal._parentLinkRSSI, RoutingGlobal.BestEtx, byte.MaxValue, RoutingGlobal.GetPathEWRNP());

            if (!IsEmpty())
            {
                //bool candidateFound = false;

                for (int i = 0; i < _length; i++)
                {
                    if (refresh)
                    {
                        _candidateList[i].SetPathEWRNP();
                    }

                    if (_candidateList[i].GetPathEWRNP() < tmpBest.GetPathEWRNP() || (_candidateList[i].GetPathEWRNP() == tmpBest.GetPathEWRNP() && _candidateList[i].GetEtx() < tmpBest.GetEtx()) || (_candidateList[i].GetPathEWRNP() == tmpBest.GetPathEWRNP() && _candidateList[i].GetEtx() == tmpBest.GetEtx() && _candidateList[i].GetRSSI() > tmpBest.GetRSSI()))
                    {
                        tmpBest = _candidateList[i];
                    }
                    #region no longer using _minRSSI
                    //                    if (_candidateList[i].GetRSSI() >= RoutingGlobal._minRSSI)
                    //                    {
                    //                        if (_candidateList[i].GetPathEWRNP() < tmpBest.GetPathEWRNP() || (_candidateList[i].GetPathEWRNP() == tmpBest.GetPathEWRNP() && _candidateList[i].GetEtx() < tmpBest.GetEtx()) || (_candidateList[i].GetPathEWRNP() == tmpBest.GetPathEWRNP() && _candidateList[i].GetEtx() == tmpBest.GetEtx() && _candidateList[i].GetRSSI() > tmpBest.GetRSSI()))
                    //                        {
                    //                            tmpBest = _candidateList[i];
                    //                            candidateFound = true;
                    //#if DBG_SIMPLE
                    //                            Debug.Print("candidate found: " + tmpBest.GetMacID().ToString() + " RSSI: " + tmpBest.GetRSSI().ToString());
                    //#endif
                    //                        }
                    //                    }
                    #endregion
                }
            }

            return tmpBest;
        }

        public static bool IsFull()
        {
            return _length == _maxLength;
        }

        public static bool IsEmpty()
        {
            return _length == 0;
        }
    }

    /// <summary>
    /// Global items for Routing
    /// </summary>
    public static class RoutingGlobal
    {
        public static Color _color;
        // Added by Dhrubo to detect and break cycles
        public const byte Infinity = 8;
        public static int _minEtx;
        public const int _minRSSI = 174; // this translates to -82 dbm

        // RNP calculations
        public const byte windowLength = 12; // 12 = 1 hr
        public const double alpha = 0.9; // weight of history for EWMRNP
        private static byte numReceivedInCurrentWindow_Parent = 0;
        private static byte numTriesInCurrentWindow_Parent = 0;
        private static double link_ewrnp_Parent = 0;
        private static byte path_ewrnp_Parent;
        public static byte path_ewrnp;

        private static Object thisLock = new Object(); // Every candidate has its own lock

        public static void UpdatePathEWRNP_Parent(byte pathEWRNP_B2P)
        {
            path_ewrnp_Parent = pathEWRNP_B2P;
        }

        public static void UpdateNumReceivedInCurrentWindow_Parent(byte num)
        {
            lock (thisLock)
            {
                numReceivedInCurrentWindow_Parent += num;
            }
        }

        public static void UpdateNumTriesInCurrentWindow_Parent(byte num)
        {
            lock (thisLock)
            {
                numTriesInCurrentWindow_Parent += num;
            }
        }

        public static void ResetParentLinkRNP()
        {
            link_ewrnp_Parent = 0;
        }

        private static double CalculateLinkEWRNP_Parent()
        {
            if (numReceivedInCurrentWindow_Parent > 0)
            {
                link_ewrnp_Parent = (link_ewrnp_Parent==0)? (numTriesInCurrentWindow_Parent / numReceivedInCurrentWindow_Parent) : alpha * link_ewrnp_Parent + (1 - alpha) * (numTriesInCurrentWindow_Parent / numReceivedInCurrentWindow_Parent);
            }
            else
            {
                link_ewrnp_Parent = (link_ewrnp_Parent == 0) ? MaxTriesPerPacket : alpha * link_ewrnp_Parent + (1 - alpha) * MaxTriesPerPacket; // TODO: Change to chosen Infinity
            }
            return link_ewrnp_Parent;
        }

        public static void SetPathEWRNP()
        {
            lock (thisLock)
            {
                path_ewrnp = (byte)(path_ewrnp_Parent + Math.Round(CalculateLinkEWRNP_Parent()));

                // Reset counts for next Window
                numReceivedInCurrentWindow_Parent = 0;
                numTriesInCurrentWindow_Parent = 0;
            }
        }

        public static byte GetPathEWRNP() { return path_ewrnp; }
        public static byte GetNumTriesInCurrentWindow_Parent() { return numTriesInCurrentWindow_Parent; }
        public static byte GetNumReceivedInCurrentWindow_Parent() { return numReceivedInCurrentWindow_Parent; }

        public const byte MaxEtx = byte.MaxValue - 10; // set it to a number below MaxValue so that adding to it won't cause overflow
        public const byte MaxTriesPerPacket = 4; // infinity value for a packet loss

        public static byte BestEtx
        {
            get { return _bestEtx; }
            set
            {
#if DBG_VERBOSE
                Debug.Print("=== BestEtx changed from " + _bestEtx + " to " + value);
#endif
                _bestEtx = value;
            }
        }
        private static byte _bestEtx;

        public static int _parentLinkRSSI;
        /// <summary>
        /// Current parent
        /// </summary>
        public static ushort Parent
        {
            get { return _parent; }
            set
            {
#if DBG_VERBOSE
                Debug.Print("===== Parent changed from " + _parent + " to " + value);
#endif

                _parent = value;
                //LocalManagerGlobal.Shared.SharedVars.Parent = _parent;
            }
        }
        private static ushort _parent;

        /// <summary>
        /// Current parent
        /// </summary>
        public static ushort ExParent
        {
            get { return _ex_parent; }
            set
            {
#if DBG_VERBOSE
                Debug.Print("===== Ex parent changed from " + _ex_parent + " to " + value);
#endif

                _ex_parent = value;
            }
        }
        private static ushort _ex_parent;

        /// <summary>
        /// True iff node has a parent
        /// </summary>
        public static bool IsParent { get { return _parent != SystemGlobal.NoParent; } }

        /// <summary>
        /// True iff node has an ex parent
        /// </summary>
        public static bool HadParent { get { return _ex_parent != SystemGlobal.NoParent; } }

        /// <summary>
        /// Send a message to all neighbors - moved here from SystemGlobal
        /// </summary>
        /// <param name="mac"></param>
        /// <param name="message"></param>
        /// <param name="messageLength"></param>
        public static void BroadcastBeacon(IMAC mac, byte[] message, int messageLength)
        {
            var neighbors = MACBase.NeighborListArray();
            mac.NeighborList(neighbors);
#if !DBG_LOGIC
            SystemGlobal.PrintNeighborList(mac);
#endif

            var pipe = mac as MACPipe;

#if DBG_VERBOSE
			if (pipe != null)
			{
				PrintNumericVals("Broadcast (on MACPipe " + pipe.PayloadType + "): ", message, messageLength);
			}
			else
			{
				PrintNumericVals("Broadcast: ", message, messageLength);
			}
#endif
            foreach (var theNeighbor in neighbors)
            {
                if (theNeighbor == 0)
                {
                    continue;
                }
                var status = pipe.EnqueueToSend(theNeighbor, message, 0, (ushort)messageLength);

                if (pipe.IsMsgIDValid(status))
                {
                // Update link metrics
                if (theNeighbor == Parent)
                {
                    UpdateNumTriesInCurrentWindow_Parent(1);
#if !DBG_LOGIC
                    Debug.Print("Updated numTriesInCurrentWindow for parent " + theNeighbor + "; new value = " + GetNumTriesInCurrentWindow_Parent());
#endif
                }
                else
                {
                    byte cindex = CandidateTable.findIndex(theNeighbor);
                    if (cindex < byte.MaxValue)
                    {
                        CandidateTable._candidateList[cindex].UpdateNumTriesInCurrentWindow(1);
#if !DBG_LOGIC
                            Debug.Print("Updated numTriesInCurrentWindow for candidate " + theNeighbor + "; new value = " + CandidateTable._candidateList[cindex].GetNumTriesInCurrentWindow());
#endif
                    }
                }


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
        }

        /// <summary>
        /// Send to parent, if any
        /// </summary>
        /// <remarks>
        /// User should first check if IsParent is true and only then invoke this method.
        /// </remarks>
        /// <param name="mac"></param>
        /// <param name="msgBytes"></param>
        /// <param name="length"></param>
        /// <returns></returns>
        public static ushort SendToParent(MACPipe mac, byte[] msgBytes, int length)
        {
            // If in a reset, do not forward
            if (RoutingGlobal._color == Color.Red)
            {
#if DBG_VERBOSE
                Debug.Print("\tIn a Reset wave... not forwarded");
#endif
                return 999;
            }
            Debug.Print("length: " + length);
            Debug.Print("msgByte len: " + msgBytes.Length);
            ushort index = mac.EnqueueToSend(_parent, msgBytes, 0, (ushort)length);
            Debug.Print("index: " + index);
            return mac.IsMsgIDValid(index) ? index : (ushort)999;
        }

        public static ushort SendToNeighbor(MACPipe mac, ushort next_neighbor, byte[] msgBytes, int length)
        {
            ushort[] _neighborList = MACBase.NeighborListArray();
            mac.NeighborList(_neighborList); // Get current neighborlist
            if (Array.IndexOf(_neighborList, next_neighbor) == -1)
            {
                Debug.Print(next_neighbor + " not in neighbor list");
                return 0;
            }
            // If in a reset, do not forward
            if (RoutingGlobal._color == Color.Red)
            {
#if DBG_VERBOSE
                Debug.Print("\tIn a Reset wave... not forwarded");
#endif
                return 999;
            }

            ushort index = mac.EnqueueToSend(next_neighbor, msgBytes, 0, (ushort)length);
            return mac.IsMsgIDValid(index) ? index : (ushort)999;
        }

        public static void CleanseCandidateTable(MACPipe pipe)
        {
            ushort[] _neighborList = MACBase.NeighborListArray();
            pipe.NeighborList(_neighborList); // Get current neighborlist
            foreach (Candidate c in CandidateTable._candidateList)
            {
                ushort macID = c.GetMacID();
                if (Array.IndexOf(_neighborList, macID) == -1)
                {
#if DBG_VERBOSE
                    Debug.Print("--- CANDIDATE LIST CLEANUP: Removing stale candidate: " + macID + " ---");
#elif DBG_SIMPLE
                    Debug.Print("Removing stale candidate: " + macID);
#endif
                    CandidateTable.DropCandidate(macID);
                }
            }
        }

        public static void SetParent(bool refresh)
        {
            // Add current parent as ex parent
            _ex_parent = _parent;

            if (IsParent && refresh)
            {
                //Update path EWRNP
                SetPathEWRNP();
            }

            if (!CandidateTable.IsEmpty())
            {
                Candidate p = CandidateTable.GetBestCandidateRelativeToParent(refresh); // Includes path EWRNP updation and comparison with present parent

                // If a parent existed previously, add it to the candidate table
                if (Parent != p.GetMacID())
                {
                    if (IsParent)
                    {
#if !DBG_LOGIC
                        Debug.Print("Adding current parent" + RoutingGlobal.Parent + " to the candidate table");
#endif
                        CandidateTable.AddCandidate(RoutingGlobal.Parent, RoutingGlobal._parentLinkRSSI, RoutingGlobal.BestEtx, RoutingGlobal.path_ewrnp_Parent, RoutingGlobal.path_ewrnp);
                    }

                    Parent = p.GetMacID();
                    BestEtx = p.GetEtx();
                    _parentLinkRSSI = (int)p.GetRSSI();
                    path_ewrnp_Parent = p.GetPathEWRNP_Base_to_Candidate();
                    path_ewrnp = p.GetPathEWRNP();

                    // Drop parent from candidate table
                    CandidateTable.DropCandidate(Parent);
                }
#if DBG_VERBOSE
                Debug.Print("*** NEW PARENT: " + p.GetMacID() + "; path length (new): " + BestEtx + "; link RSSI: " + _parentLinkRSSI + " ***");
#elif DBG_SIMPLE
                Debug.Print("Set Parent " + p.GetMacID() + ":[" + path_ewrnp + "," + BestEtx + "," + _parentLinkRSSI + "]");
#endif
                Debug.Print("Set Parent " + p.GetMacID() + ":[" + path_ewrnp + "," + BestEtx + "," + _parentLinkRSSI + "]");
            }
            else
            {
#if !DBG_LOGIC
                Debug.Print("Empty candidate table. Parent " + RoutingGlobal.Parent + " unchanged: [" + path_ewrnp + "," + BestEtx + "," + _parentLinkRSSI + "]");
#endif
            }
        }

        /// <summary>
        /// Routing message IDs
        /// </summary>
        public enum MessageIds : byte
        {
            /// <summary>Beacon neighbors</summary>
            Beacon = 0,
            #region unused
            /// <summary>Hello</summary>
            //Hello = 1,

            /// <summary>Data</summary>
            //Data = 2, 
            #endregion
        }

        //internal static readonly byte[] MsgBytes = new byte[128];

        /// <summary>
        /// Create messages to send
        /// </summary>
        public static class ComposeMessages
        {
            /// <summary>
            /// Create beacon message
            /// </summary>
            /// <param name="msgBytes"></param>
            /// <param name="etx">Estimated distance from Base</param>
            /// <param name="beaconNum">Beacon sequence counter</param>
            /// <returns>Size of message</returns>
            public static int CreateBeacon(byte[] msgBytes, byte etx, ushort beaconNum, byte ewmarnp, ushort parent)
            {
                var idx = 0;
                msgBytes[idx] = (byte)MessageIds.Beacon;
                idx += sizeof(byte);
                //Debug.Print("\tIdx: " + idx);

                msgBytes[idx] = etx;
                idx += sizeof(byte);
                //Debug.Print("\tIdx: " + idx);

                var beaconAdj = (ushort)Math.Min(beaconNum, ushort.MaxValue);
                BitConverter.InsertValueIntoArray(msgBytes, idx, beaconAdj);
                idx += sizeof(ushort);

                msgBytes[idx] = ewmarnp;
                idx += sizeof(byte);

                BitConverter.InsertValueIntoArray(msgBytes, idx, parent);
                idx += sizeof(ushort);

                return idx;
            }

            #region unused
            ///// <summary>
            ///// Create hello message
            ///// </summary>
            ///// <param name="msgBytes"></param>
            ///// <returns></returns>
            //internal static int CreateHello(byte[] msgBytes)
            //{
            //	msgBytes[0] = (byte)MessageIds.Hello;

            //	return 1;
            //}

            //internal static int CreateData(byte[] msgBytes, string data, ushort dataNum)
            //{
            //	var idx = 0;
            //	msgBytes[idx] = (byte)MessageIds.Data;
            //	idx += sizeof(byte);

            //	byte dataByte = 0; // default value: detection packet
            //	switch (data)
            //	{
            //		case "D":
            //			dataByte = 0;
            //			break;

            //		case "H":
            //			dataByte = 1;
            //			break;

            //		case "N":
            //			dataByte = 2;
            //			break;

            //		case "A":
            //			dataByte = 3;
            //			break;

            //		case "CH":
            //			dataByte = 4;
            //			break;

            //		case "CN":
            //			dataByte = 5;
            //			break;

            //		case "CA":
            //			dataByte = 6;
            //			break;

            //		default:
            //			break;
            //	}

            //	msgBytes[idx] = dataByte;
            //	idx += sizeof(byte);

            //	var dataAdj = (ushort)Math.Min(dataNum, ushort.MaxValue);
            //	BitConverter.InsertValueIntoArray(msgBytes, idx, dataAdj);
            //	idx += sizeof(ushort);

            //	return idx;
            //} 
            #endregion
        }

        /// <summary>
        /// Parse routing messages received
        /// </summary>
        public static class ParseMessages
        {
            public static void ParseBeacon(byte[] msgBytes, out byte etx, out ushort beaconNum, out byte ewmarnp, out ushort parent)
            {
                //Debug.Print("msg size: " + msgBytes.Length.ToString());
                //for (int i = 0; i < msgBytes.Length; i++ )
                //    Debug.Print(msgBytes[i].ToString());
                var idx = 1;                
                //Debug.Print("\tEtx 1: " + msgBytes[idx] + ", Idx: " + idx);
                etx = msgBytes[idx];
                idx += sizeof(byte);
                //Debug.Print("\tEtx 2: " + etx + ", Idx: " + idx);

                //Debug.Print("\t$$$ ParseBeacon " + msgBytes[idx] + "," + msgBytes[idx + 1]);

                beaconNum = (UInt16)((msgBytes[idx+1] << 8) | (msgBytes[idx])); 
                idx += sizeof(ushort);
                ewmarnp = msgBytes[idx];                               
                idx += sizeof(byte);

                // for some reason msgBytes is one byte short of where it needs to be it looks like the payload passed up is the wrong length
                //parent = (UInt16)((msgBytes[idx + 1] << 8) | (msgBytes[idx])); 
                parent = 0;
                //idx += sizeof(ushort);
                /*beaconNum = BitConverter.ToUInt16(msgBytes, idx);
                idx += sizeof(ushort);

                ewmarnp = msgBytes[idx];
                idx += sizeof(byte);

                parent = BitConverter.ToUInt16(msgBytes, idx);
                idx += sizeof(ushort);*/
            }

            #region unused
            //public static void ParseData(byte[] msgBytes, out byte data, out ushort dataNum)
            //{
            //    var idx = 1;

            //    //Debug.Print("\tEtx 1: " + msgBytes[idx] + ", Idx: " + idx);
            //    data = msgBytes[idx];
            //    idx += sizeof(byte);
            //    //Debug.Print("\tEtx 2: " + etx + ", Idx: " + idx);


            //    dataNum = BitConverter.ToUInt16(msgBytes, idx);
            //}
            #endregion
        }
    }
}
