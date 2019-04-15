//#define DBG_LOGIC
#if BASE_STATION
#define DBG_SIMPLE
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

#if !PC
// Specify protocol & radio

#define OMAC
//#define CSMA

#define RF231
//#define RF231LR
//#define SI4468

#if RF231LR
#error Not defined yet
#endif


#endif

using System.Diagnostics;
using System.Text;
using Samraksh.eMote.Net;
using Samraksh.eMote.Net.MAC;

#if !PC
using System;
//using Samraksh.Components.Utility;
using Microsoft.SPOT;
using Samraksh.eMote.Net;
using Samraksh.eMote.Net.MAC;
using Samraksh.eMote.Net.Radio;
#endif

namespace Samraksh.VirtualFence
{
    /// <summary>
    /// Items that are global to the entire system
    /// </summary>
    public static class SystemGlobal
    {
        /// <summary>
        /// Initialize System Global
        /// </summary>
        /// <param name="nodeType"></param>
        public static void Initialize(NodeTypes nodeType)
        {
            NodeType = nodeType;
        }

        /// <summary>
        /// Node types
        /// </summary>
        public enum NodeTypes : byte
        {
            /// <summary>PC</summary>
            PC = (byte)'P',
            /// <summary>Base</summary>
            Base = (byte)'B',
            /// <summary>Relay</summary>
            Relay = (byte)'R',
            /// <summary>Relay</summary>
            Client = (byte)'C',
            /// <summary>Fence</summary>
            Fence = (byte)'F',
            /// <summary>Unknown</summary>
            Unk = (byte)'U',
        }

        /// <summary>
        /// Type of this node.
        /// </summary>
        /// <remarks>Initialized in Initialize method</remarks>
        public static NodeTypes NodeType;

#if !PC
        /// <summary>
        /// Constant value for case of no parent
        /// </summary>
        public const ushort NoParent = ushort.MaxValue;

        /// <summary>
        /// Get the MAC
        /// </summary>
        /// <returns></returns>
        public static MACBase GetMAC()
        {
#if SI4468
#if !DBG_LOGIC
            Debug.Print("Configuring SI4468 radio with power " + RadioProperties.Power + ", channel " +
                        RadioProperties.RadioChannel);
#endif
            var radioConfig = new SI4468RadioConfiguration(RadioProperties.Power, RadioProperties.RadioChannel);
#elif RF231
			var radioConfig = new RF231RadioConfiguration(RadioProperties.Power, RadioProperties.RadioChannel);
#endif

#if CSMA
			var mac = new CSMA(radioConfig);
#endif
#if OMAC
#if !DBG_LOGIC
            Debug.Print("Configuring OMAC");
#endif
            var mac = new OMAC(radioConfig);
            //mac.NeighborLivenessDelay = 320;
            mac.NeighborLivenessDelay = 10 * 60 + 20;
#if !DBG_LOGIC
            Debug.Print("NeighborLivenessDelay = " + mac.NeighborLivenessDelay);
#endif
#endif
#if !DBG_LOGIC
            Debug.Print("Radio Power: " + mac.MACRadioObj.TxPower);
#endif
            return mac;
        }

        /// <summary>
        /// MAC Pipe IDs
        /// </summary>
        public static class MacPipeIds
        {
            /// <summary>Application payload type</summary>
            public const PayloadType App = PayloadType.Type01;

            /// <summary>Local manager payload type</summary>
            public const PayloadType LocalManager = PayloadType.Type02;

            /// <summary>Network monitor payload type</summary>
            public const PayloadType NetworkManager = PayloadType.Type03;

            /// <summary>Neighborhood monitor payload type</summary>
            public const PayloadType NeighborInfoManager = PayloadType.Type06;

            /// <summary>Relay payload type</summary>
            public const PayloadType Routing = PayloadType.Type04;

            /// <summary>Distributed Reset payload type</summary>
            public const PayloadType DistReset = PayloadType.Type05;
        }
#endif


        /// <summary>
        /// Methods for PC program
        /// </summary>
        public static class PCMessages
        {
            /// <summary>
            /// PC Message delimeters
            /// </summary>
            /// <remarks>
            /// Format: {MsgBegin}{MsgDelim1}message{MsgDelim2}{MsgEnd}
            /// </remarks>
            public static class Delimiters
            {
                /// <summary>PC Message begin char</summary>
                public const char MsgBegin = '~';

                /// <summary>Initial delimeter</summary>
                public const char MsgDelim1 = '[';

                /// <summary>Closing char</summary>
                public const char MsgDelim2 = ']';

                /// <summary>Message end char</summary>
                public const char MsgEnd = '\n';
            }

            /// <summary>
            /// Generate the message header for messages from mote to PC
            /// </summary>
            /// <param name="payloadType"></param>
            /// <returns></returns>
            public static StringBuilder MsgHeader(PCMacPipeIds payloadType)
            {
                var msgSb = new StringBuilder();
                msgSb.Append(Delimiters.MsgBegin);
                msgSb.Append(Delimiters.MsgDelim1);
                msgSb.Append((int)payloadType);
                msgSb.Append(' ');
                return msgSb;
            }

            /// <summary>
            /// Add the trailer for messages from mote to PC
            /// </summary>
            /// <param name="msgSb"></param>
            public static void MsgTrailer(StringBuilder msgSb)
            {
                msgSb.Append(Delimiters.MsgDelim2);
                msgSb.Append(Delimiters.MsgEnd);
            }

#if PC
			/// <summary>
			/// Check message delimeters and strip
			/// </summary>
			/// <param name="msgSb"></param>
			/// <param name="msg"></param>
			/// <returns>false iff delimeters not present</returns>
			public static bool CheckAndStripDelimeters(StringBuilder msgSb, out string msg)
			{
				msg = string.Empty;
				if (msgSb == null || msgSb.Length < 2)
				{
					return false;
				}
				if (msgSb[0] != Delimiters.MsgDelim1 || msgSb[msgSb.Length - 1] != Delimiters.MsgDelim2)
				{
					return false;
				}
				msg = msgSb.ToString(1, msgSb.Length - 2);
				return true;
			}
#endif

            /// <summary>
            /// MACPipe Payload Types for PC code. For convenience, these match the actual values of MacPipeIds EXCEPT for BaseLiveness.
            /// <remarks>
            /// BaseLiveness does not correspond to a MACPipe. These messages are generated only by Base and sent across serial link to PC.
            /// </remarks>
            /// </summary>
            public enum PCMacPipeIds
            {
                /// <summary>Application stream ID</summary>
                App = 1,
                /// <summary>Local manager stream ID</summary>
                LocalManager = 2,
                /// <summary>Network manager stream ID</summary>
                NetworkManager = 3,
                /// <summary>Relay stream ID</summary>
                Routing = 4,
                /// <summary>Base Liveness ID</summary>
                BaseLiveness = 99,
            }
        }

#if !PC
#if RF231
		/// <summary>
		/// Radio properties
		/// </summary>
		public class RadioProperties
		{
			///// <summary>Radio name</summary>
			//public const RadioName Radio = RadioName.RF231;

			/// <summary>CCA sense time</summary>
			public const byte CCASenseTime = 140;

			/// <summary>Transmit power level</summary>
            public const RF231TxPower Power = RF231TxPower.Power_Minus17dBm;

			/// <summary>Radio channel</summary>
			public const RF231Channel RadioChannel = RF231Channel.Channel_13;
		}
#elif SI4468
        /// <summary>
        /// Radio properties
        /// </summary>
        public class RadioProperties
        {
            ///// <summary>Radio name</summary>
            //public const RadioName Radio = RadioName.RF231;

            /// <summary>CCA sense time</summary>
            public const byte CCASenseTime = 140;

            /// <summary>Transmit power level</summary>
            //public const SI4468TxPower Power = SI4468TxPower.Power_20dBm;
            public const SI4468TxPower Power = SI4468TxPower.Power_13Point7dBm;

            /// <summary>Radio channel</summary>
            public const SI4468Channel RadioChannel = SI4468Channel.Channel_00;
            //public const SI4468Channel RadioChannel = SI4468Channel.Channel_02;
        }
#endif
#endif

        /// <summary>
        /// Print byte values
        /// </summary>
        /// <param name="prefix"></param>
        /// <param name="messageEx"></param>
        public static void PrintNumericVals(string prefix, byte[] messageEx)
        {
            PrintNumericVals(prefix, messageEx, messageEx.Length);
        }
#if !PC
        /// <summary>
        /// Send a message to all neighbors
        /// </summary>
        /// <param name="mac"></param>
        /// <param name="message"></param>
        /// <param name="messageLength"></param>
        public static void Broadcast(IMAC mac, byte[] message, int messageLength)
        {
            var neighbors = MACBase.NeighborListArray();
            mac.NeighborList(neighbors);
#if !DBG_LOGIC
            PrintNeighborList(mac);
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
#endif

        /// <summary>
        /// Print byte values
        /// </summary>
        /// <param name="prefix"></param>
        /// <param name="messageEx"></param>
        /// <param name="messageLen"></param>
        public static void PrintNumericVals(string prefix, byte[] messageEx, int messageLen)
        {
            var msgBldr = new StringBuilder(prefix);
            for (var i = 0; i < messageLen; i++)
            {
                msgBldr.Append(messageEx[i] + " ");
            }
            Debug.Print(msgBldr.ToString());
            //Debug.Print("");
        }

        /// <summary>
        /// Print ushort values
        /// </summary>
        /// <param name="prefix"></param>
        /// <param name="messageEx"></param>
        public static void PrintNumericVals(string prefix, ushort[] messageEx)
        {
            var msgBldr = new StringBuilder(prefix);
            foreach (var val in messageEx)
            {
                msgBldr.Append(val + " ");
            }
            Debug.Print(msgBldr.ToString());
        }

#if !PC
        /// <summary>
        /// Print the neighbor list from MACBase instance
        /// </summary>
        /// <param name="macBase"></param>
        public static void PrintNeighborList(IMAC macBase)
        {
            macBase.NeighborList(Neighbors);
            PrintNumericVals("Neighbor List [for " + macBase.MACRadioObj.RadioAddress + "] ", Neighbors);
        }
        private static readonly ushort[] Neighbors = new ushort[12];

        /// <summary>
        /// Print the neighbor list for a given list of neighbors
        /// </summary>
        public static void PrintNeighborList(string prefix, ushort[] neighborList)
        {
            PrintNumericVals(prefix, neighborList);
        }

//        /// <summary>
//        /// When the neighbor list changes, print the old and the new
//        /// </summary>
//        /// <param name="macInstance"></param>
//        /// <param name="time"></param>
//        public static void macBase_OnNeighborChange(IMAC macInstance, DateTime time)
//        {
//            if (_changeNeighborList == null) { _changeNeighborList = MACBase.NeighborListArray(); }
//#if !DBG_LOGIC
//            PrintNeighborList("Old neighbor list: ", _changeNeighborList);
//#endif
//            macInstance.NeighborList(_changeNeighborList);
//#if !DBG_LOGIC
//            PrintNeighborList("New neighbor list: ", _changeNeighborList);
//#endif
//        }
//        private static ushort[] _changeNeighborList;

#endif
    }
}
