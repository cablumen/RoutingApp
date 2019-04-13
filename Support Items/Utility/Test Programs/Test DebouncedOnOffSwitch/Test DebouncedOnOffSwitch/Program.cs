using System;
using System.Threading;
using Microsoft.SPOT;
using Microsoft.SPOT.Hardware;
using Samraksh.Components.Utility;
using Samraksh.eMote.DotNow;

namespace Test_DebouncedOnOffSwitch
{
	public class Program
	{
		private static readonly EnhancedEmoteLCD Lcd = new EnhancedEmoteLCD();
		private const int BounceTimeMs = 50;
		private const int LongPressWaitMs = 350;

		public static void Main()
		{
			var debouncedSwitch = new DebouncedOnOffSwitch(Pins.GPIO_J12_PIN1, Port.ResistorMode.PullUp, false, BounceTimeMs,LongPressWaitMs);
		
			debouncedSwitch.OnLongPress += DebouncedSwitch_OnLongPress;
			debouncedSwitch.OnShortPress += DebouncedSwitch_OnShortPress;
			Lcd.Clear();

			Thread.Sleep(Timeout.Infinite);
		}

		private static int _cntr = 0;
		private static void DebouncedSwitch_OnShortPress(uint data1, uint data2, DateTime time)
		{
			Lcd.Write("SSSS");
			Debug.Print(_cntr++ + " Short press");
			//Thread.Sleep(1000);
			//Lcd.Clear();
		}

		private static void DebouncedSwitch_OnLongPress(uint data1, uint data2, DateTime time)
		{
			Lcd.Write("LLLL");
			Debug.Print(_cntr++ + " Long press");
			//Thread.Sleep(1000);
			//Lcd.Clear();
		}
	}
}
