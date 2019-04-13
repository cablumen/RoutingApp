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
using Samraksh.VirtualFence;
using Samraksh.VirtualFence.Components;
using Samraksh.eMote.Net;
using Samraksh.eMote.Net.MAC;

#if !PC
using Samraksh.VirtualFence;
using BitConverter = Samraksh.Components.Utility.BitConverter;
#endif

namespace Samraksh.Manager.NetManager
{
 	/// <summary>
	/// Global items for Net Manager
	/// </summary>
	public static class NetManagerGlobal
	{
        /// <summary>
        /// Size of heartbeat messages
        /// </summary>
        public const int HeartbeatMessageSize = sizeof(byte) + sizeof(ushort) + sizeof(ushort) + sizeof(byte) + sizeof(ushort) + sizeof(byte); // Adding field for TTL

        /*CAUTION: Any change in the advanced heartbeat structure should be accounted for in variables
         * NetManagerGlobal.AdvHeartbeatFixedSize and NetManagerGlobal.EachNeighborInfoSize
         */
        // Size of each neighbor information in advanced heartbeat messages
        public static readonly byte EachNeighborInfoSize = sizeof(ushort) + sizeof(byte) + sizeof(ushort) + sizeof(ushort) + sizeof(byte) + sizeof(byte) + sizeof(byte);

        // Size of fixed information in advanced heartbeat messages
        public static readonly byte AdvHeartbeatFixedSize = sizeof(byte) + sizeof(ushort) + sizeof(ushort) + sizeof(byte) + sizeof(ushort) + sizeof(byte) + sizeof(byte) + sizeof(byte); // Adding field for TTL

        // Mac MSS - assumed 100 bytes for our purpose
        public static readonly byte MaxSegmentSize = 100;

        //Max. number of neighbors reported in each heartbeat
        public static readonly byte MaxNeighborsPerHeartbeat = (byte)Math.Floor((double)(MaxSegmentSize - AdvHeartbeatFixedSize) / EachNeighborInfoSize);

        // Temporary parent
        public static ushort TempParent;
        /// <summary>
        /// Net Manager IDs
        /// </summary>
        public enum MessageIds : byte
        {
            /// <summary>
            /// Heartbeat message ID
            /// </summary>
            Heartbeat = 0,
            NeighborInfo = 1,
        }

#if !PC
        /// <summary>
        /// Message byte array
        /// </summary>
        public static readonly byte[] MsgBytes = new byte[128];

        public static ushort SendToTempParent(MACPipe mac, byte[] msgBytes, int length)
        {
            // If in a reset, do not forward
            if (RoutingGlobal._color == Color.Red)
            {
#if DBG_VERBOSE
                Debug.Print("\tIn a Reset wave... not forwarded");
#endif
                return 999;
            }

            ushort index = mac.EnqueueToSend(TempParent, msgBytes, 0, (ushort)length);
            return mac.IsMsgIDValid(index) ? index : (ushort)999;
        }

        //============================================================================================================================
        /// <summary>
        /// Mote Messages
        /// </summary>
        public static class MoteMessages
        {
            /// <summary>
            /// Create messages to send
            /// </summary>
            public static class Compose
            {
                /// <summary>
                /// Compose Heartbeat message
                /// </summary>
                /// <param name="msgBytes"></param>
                /// <param name="originatorId"></param>
                /// <param name="heartbeatNumber"></param>
                /// <param name="nodeType"></param>
                /// <param name="parent"></param>
                /// <returns>Message size</returns>
                /// </summary>
                public static int Heartbeat(byte[] msgBytes, ushort originatorId, ushort heartbeatNumber, SystemGlobal.NodeTypes nodeType, ushort parent, byte TTL)
                {
                    var idx = 0;
                    msgBytes[idx] = (byte)MessageIds.Heartbeat;
                    idx++;

                    BitConverter.InsertValueIntoArray(msgBytes, idx, originatorId);
                    idx += sizeof(ushort);

                    BitConverter.InsertValueIntoArray(msgBytes, idx, heartbeatNumber);
                    idx += sizeof(ushort);

                    msgBytes[idx] = (byte)nodeType;
                    idx += sizeof(byte);

                    BitConverter.InsertValueIntoArray(msgBytes, idx, parent);
                    idx += sizeof(ushort);

                    // Adding TTL
                    msgBytes[idx] = TTL; // RoutingGlobal.Infinity - 1, hardcoded for now
                    idx += sizeof(byte);

                    // Check if message is the right size
                    if (idx != HeartbeatMessageSize)
                    {
                        throw new Exception("Compose Heartbeat: message composed is " + idx + " bytes; should be " + HeartbeatMessageSize);
                    }

                    return idx;
                }

                /// <summary>
                /// Compose Advanced Heartbeat message
                /// </summary>
                /// <param name="msgBytes"></param>
                /// <param name="originatorId"></param>
                /// <param name="heartbeatNumber"></param>
                /// <param name="nodeType"></param>
                /// <param name="parent"></param>
                /// <param name="bestetx"></param>
                /// <param name="neighbors"></param>
                /// <param name="nbrStatus"></param>
                /// <param name="numSamplesRec"></param>
                /// <param name="numSyncSent"></param>
                /// <param name="avgRSSI"></param>
                /// <param name="ewrnp"></param>
                /// <returns>Message size</returns>
                /// </summary>

                /*CAUTION: Any change in the advanced heartbeat structure should be accounted for in variables
                * NetManagerGlobal.AdvHeartbeatFixedSize and NetManagerGlobal.EachNeighborInfoSize
                */
                public static int Heartbeat(byte[] msgBytes, ushort originatorId, ushort heartbeatNumber, SystemGlobal.NodeTypes nodeType, ushort parent, byte bestetx, ushort[] neighbors, byte[] nbrStatus, ushort[] numSamplesRec, ushort[] numSyncSent, byte[] avgRSSI, byte[] ewrnp, byte[] isAvailableForUpperLayers, byte TTL)
                {
                    var idx = 0;
                    msgBytes[idx] = (byte)MessageIds.NeighborInfo;
                    idx++;

                    BitConverter.InsertValueIntoArray(msgBytes, idx, originatorId);
                    idx += sizeof(ushort);

                    BitConverter.InsertValueIntoArray(msgBytes, idx, heartbeatNumber);
                    idx += sizeof(ushort);

                    msgBytes[idx] = (byte)nodeType;
                    idx += sizeof(byte);

                    BitConverter.InsertValueIntoArray(msgBytes, idx, parent);
                    idx += sizeof(ushort);

                    msgBytes[idx] = bestetx;
                    idx += sizeof(byte);

                    msgBytes[idx] = (byte)neighbors.Length;
                    idx += sizeof(byte);

                    for (int i = 0; i < neighbors.Length; i++)
                    {
                        BitConverter.InsertValueIntoArray(msgBytes, idx, neighbors[i]);
                        idx += sizeof(ushort);
                    }

                    for (int i = 0; i < neighbors.Length; i++)
                    {
                        msgBytes[idx] = nbrStatus[i];
                        idx += sizeof(byte);
                    }

                    for (int i = 0; i < neighbors.Length; i++)
                    {
                        BitConverter.InsertValueIntoArray(msgBytes, idx, numSamplesRec[i]);
                        idx += sizeof(ushort);
                    }

                    for (int i = 0; i < neighbors.Length; i++)
                    {
                        BitConverter.InsertValueIntoArray(msgBytes, idx, numSyncSent[i]);
                        idx += sizeof(ushort);
                    }

                    for (int i = 0; i < neighbors.Length; i++)
                    {
                        msgBytes[idx] = avgRSSI[i];
                        idx += sizeof(byte);
                    }

                    for (int i = 0; i < neighbors.Length; i++)
                    {
                        msgBytes[idx] = ewrnp[i];
                        idx += sizeof(byte);
                    }

                    for (int i = 0; i < neighbors.Length; i++)
                    {
                        msgBytes[idx] = isAvailableForUpperLayers[i];
                        idx += sizeof(byte);
                    }

                    // Adding TTL
                    msgBytes[idx] = TTL;
                    idx += sizeof(byte);

                    return idx;
                }
            }

            /// <summary>
            /// Parse net health manager received
            /// </summary>
            public static class Parse
            {
                /// <summary>
                /// Parse heartbeat message
                /// </summary>
                /// <param name="msgBytes"></param>
                /// <param name="originatorId"></param>
                /// <param name="heartbeatNumber"></param>
                /// <param name="nodeType"></param>
                /// <param name="parent"></param>
                public static int HeartBeat(byte[] msgBytes, out ushort originatorId, out ushort heartbeatNumber, out SystemGlobal.NodeTypes nodeType, out ushort parent, out byte TTL)
                {
                    var idx = 1;	// Ignore message type
                    originatorId = BitConverter.ToUInt16(msgBytes, idx);
                    idx += sizeof(ushort);

                    heartbeatNumber = BitConverter.ToUInt16(msgBytes, idx);
                    idx += sizeof(ushort);

                    nodeType = (SystemGlobal.NodeTypes)msgBytes[idx];
                    idx += sizeof(byte);

                    parent = BitConverter.ToUInt16(msgBytes, idx);
                    idx += sizeof(ushort);

                    // Added TTL parsing
                    TTL = msgBytes[idx];
                    idx += sizeof(byte);

                    return idx;
                }

                /// <summary>
                /// Parse Advanced Heartbeat message
                /// </summary>
                /// <param name="msgBytes"></param>
                /// <param name="originatorId"></param>
                /// <param name="heartbeatNumber"></param>
                /// <param name="nodeType"></param>
                /// <param name="parent"></param>
                /// <param name="bestetx"></param>
                /// <param name="neighbors"></param>
                /// <param name="nbrStatus"></param>
                /// <param name="numSamplesRec"></param>
                /// <param name="numSyncSent"></param>
                /// <param name="avgRSSI"></param>
                /// <param name="ewrnp"></param>
                /// <returns>Message size</returns>
                /// </summary>

                /*CAUTION: Any change in the advanced heartbeat structure should be accounted for in variables
                 * NetManagerGlobal.AdvHeartbeatFixedSize and NetManagerGlobal.EachNeighborInfoSize
                 */
                public static int HeartBeat(byte[] msgBytes, out ushort originatorID, out ushort heartbeatNumber, out SystemGlobal.NodeTypes nodeType, out ushort parent, out byte bestetx, out  byte num_nbrs, out ushort[] neighbors, out byte[] nbrStatus, out ushort[] numSamplesRec, out ushort[] numSyncSent, out byte[] avgRSSI, out byte[] ewrnp, out byte[] isAvailableForUpperLayers, out byte TTL)
                {
                    var idx = 1;	// Ignore message type
                    originatorID = BitConverter.ToUInt16(msgBytes, idx);
                    idx += sizeof(ushort);

                    heartbeatNumber = BitConverter.ToUInt16(msgBytes, idx);
                    idx += sizeof(ushort);

                    nodeType = (SystemGlobal.NodeTypes)msgBytes[idx];
                    idx += sizeof(byte);

                    parent = BitConverter.ToUInt16(msgBytes, idx);
                    idx += sizeof(ushort);

                    bestetx = msgBytes[idx];
                    idx += sizeof(byte);

                    num_nbrs = msgBytes[idx];
                    idx += sizeof(byte);

                    byte num_nbrs_reported = (byte) ((msgBytes.Length - AdvHeartbeatFixedSize) / EachNeighborInfoSize);

                    neighbors = new ushort[num_nbrs_reported];
                    nbrStatus = new byte[num_nbrs_reported];
                    numSamplesRec = new ushort[num_nbrs_reported];
                    numSyncSent = new ushort[num_nbrs_reported];
                    avgRSSI = new byte[num_nbrs_reported];
                    ewrnp = new byte[num_nbrs_reported];
                    isAvailableForUpperLayers = new byte[num_nbrs_reported];

                    for (int i = 0; i < neighbors.Length; i++)
                    {
                        neighbors[i] = BitConverter.ToUInt16(msgBytes, idx);
                        idx += sizeof(ushort);
                    }

                    for (int i = 0; i < neighbors.Length; i++)
                    {
                        nbrStatus[i] = msgBytes[idx];
                        idx += sizeof(byte);
                    }

                    for (int i = 0; i < neighbors.Length; i++)
                    {
                        numSamplesRec[i] = BitConverter.ToUInt16(msgBytes, idx);
                        idx += sizeof(ushort);
                    }

                    for (int i = 0; i < neighbors.Length; i++)
                    {
                        numSyncSent[i] = BitConverter.ToUInt16(msgBytes, idx);
                        idx += sizeof(ushort);
                    }

                    for (int i = 0; i < neighbors.Length; i++)
                    {
                        avgRSSI[i] = msgBytes[idx];
                        idx += sizeof(byte);
                    }

                    for (int i = 0; i < neighbors.Length; i++)
                    {
                        ewrnp[i] = msgBytes[idx];
                        idx += sizeof(byte);
                    }

                    for (int i = 0; i < neighbors.Length; i++)
                    {
                        isAvailableForUpperLayers[i] = msgBytes[idx];
                        idx += sizeof(byte);
                    }

                    // Added TTL parsing
                    TTL = msgBytes[idx];
                    idx += sizeof(byte);

                    return idx;
                }
            }
        }
#endif

        //============================================================================================================================
        /// <summary>
        /// PC Messages
        /// </summary>
        public static class PCMessages
        {
            /// <summary>
            /// Compose messages to send to PC
            /// </summary>
            public static class Compose
            {
                /// <summary>
                /// Compose Heartbeat message to send to PC
                /// </summary>
                /// <param name="originatorId"></param>
                /// <param name="heartbeatNumber"></param>
                /// <param name="nodeType"></param>
                /// <param name="parentId"></param>
                /// <returns></returns>
                /// 
                public static string Heartbeat(int originatorId, int heartbeatNumber, SystemGlobal.NodeTypes nodeType, int parentId)
                {
                    var msgS = SystemGlobal.PCMessages.MsgHeader(SystemGlobal.PCMessages.PCMacPipeIds.NetworkManager);

                    msgS.Append((int)MessageIds.Heartbeat);
                    msgS.Append(' ');
                    msgS.Append(originatorId);
                    msgS.Append(' ');
                    msgS.Append(heartbeatNumber);
                    msgS.Append(' ');
                    msgS.Append((int)nodeType);
                    msgS.Append(' ');
                    msgS.Append(parentId);
                    SystemGlobal.PCMessages.MsgTrailer(msgS);
                    //msgS.Append('\n');
                    return msgS.ToString();
                }
            }
#if PC
			/// <summary>
			/// Parse messages received by PC
			/// </summary>
			public static class Parse
			{
				/// <summary>
				/// Parse Heartbeat message received at PC
				/// </summary>
				/// <param name="args"></param>
				/// <param name="originatorId"></param>
				/// <param name="heartbeatNumber"></param>
				/// <param name="nodeType"></param>
				/// <param name="parentId"></param>
				public static void HeartBeat(string[] args, out int originatorId, out int heartbeatNumber, out SystemGlobal.NodeTypes nodeType, out int parentId)
				{
					const int firstArg = 2;
					const int numArgs = 4;
					var argNo = 0;
					if (args.Length - firstArg != numArgs)
					{
						throw new Exception(string.Format("Invalid number of arguments for Heartbeat message. S/b {0}, found {1}", numArgs, args.Length - firstArg));
					}

					// Get originatorId ID
					if (!int.TryParse(args[firstArg + argNo], out originatorId))
					{
						throw new Exception(string.Format("Heartbeat message: Error converting originator ID {0}", args[firstArg + argNo]));
					}
					argNo++;

					// Get heartbeat number
					if (!int.TryParse(args[firstArg + argNo], out heartbeatNumber))
					{
						throw new Exception(string.Format("Heartbeat message: Error converting heartbeat number {0}", args[firstArg + argNo]));
					}
					argNo++;

					// Get Node type
					byte nodeTypeB;
					if (!byte.TryParse(args[firstArg + argNo], out nodeTypeB))
					{
						throw new Exception(string.Format("Heartbeat message: Error converting node type {0}", args[firstArg + argNo]));
					}
					nodeType = (SystemGlobal.NodeTypes)nodeTypeB;
					argNo++;

					// Get parent
					if (!int.TryParse(args[firstArg + argNo], out parentId))
					{
						throw new Exception(string.Format("Heartbeat message: Error converting parent ID {0}", args[firstArg + argNo]));
					}
					argNo++;

					if (argNo != numArgs)
					{
						throw new Exception(string.Format("Detection message: Number of arguments parsed: {0}; s/b {1}", numArgs, argNo));
					}
				}
			}
#endif
        }
    }
}
