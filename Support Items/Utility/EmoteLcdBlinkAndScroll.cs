using System;
using System.Threading;
using Microsoft.SPOT;
using Microsoft.SPOT.Hardware;
using Samraksh.eMote.DotNow;

namespace Samraksh.Components.Utility
{
	//internal interface IEmoteLcdBlinkAndScroll
	//{
	//	void Display(string display, bool blink = false);
	//	string CurrDisplay { get; }
	//	bool Blink { get; set; }
	//	int BlinkScrollRate { get; set; }
	//}

	///// <summary>
	///// eMote .NOW blink to enable blink and scroll display
	///// </summary>
	//public class EmoteLcdBlinkAndScroll : IEmoteLcdBlinkAndScroll
	//{
	//	/// <summary>
	//	/// Constructor
	//	/// </summary>
	//	public EmoteLcdBlinkAndScroll()
	//	{
	//		_blinkScroll = new BlinkScroll(BlinkScrollRate);
	//	}

	//	// ReSharper disable once UnusedMember.Local
	//	private readonly BlinkScroll _blinkScroll;

	//	/// <summary>
	//	/// Display a string, with optional blink
	//	/// </summary>
	//	/// <param name="display"></param>
	//	/// <param name="blink"></param>
	//	public void Display(string display, bool blink = false)
	//	{
	//		CurrDisplay = display;
	//		Blink = blink;
	//		_blinkScroll.SetDisplay(display);
	//	}

	//	/// <summary>
	//	/// Get current display string
	//	/// </summary>
	//	public string CurrDisplay
	//	{
	//		get { return _currDisplay; }
	//		private set
	//		{
	//			if (value.Length > BlinkScroll.MaxDisplaySize)
	//			{
	//				throw new ArgumentException("Size of argument to CurrDisplay is " + value.Length + ". Max is " + BlinkScroll.MaxDisplaySize);
	//			}
	//			_currDisplay = value;
	//			//Scroll = _currDisplay.Length > 4;

	//		}
	//	}
	//	private string _currDisplay = string.Empty;

	//	///// <summary>
	//	///// Get whether scrolling or not
	//	///// </summary>
	//	//public bool Scroll
	//	//{
	//	//	get { return _scroll; }
	//	//	private set
	//	//	{
	//	//		_scroll = value;
	//	//		if (_scroll || Blink)
	//	//		{
	//	//			_blinkScroll.WaitBlinkScroll.Set();
	//	//		}
	//	//	}
	//	//}
	//	//private bool _scroll;

	//	///// <summary>
	//	///// Control and check blink state
	//	///// </summary>
	//	//public bool Blink
	//	//{
	//	//	get { return _blink; }
	//	//	set
	//	//	{
	//	//		_blink = value;
	//	//		if (_blink || Scroll)
	//	//		{
	//	//			_blinkScroll.WaitBlinkScroll.Set();
	//	//		}
	//	//	}
	//	//}
	//	//private bool _blink;

	//	public bool Blink
	//	{
	//		set { _blinkScroll.Blink = value; }
	//		get { return _blinkScroll.Blink; }
	//	}

	//	/// <summary>
	//	/// Set the blink scroll rate
	//	/// </summary>
	//	public int BlinkScrollRate
	//	{
	//		get { return _blinkScrollRate; }
	//		set { _blinkScrollRate = value; }
	//	}
	//	private int _blinkScrollRate = 500;

	//	//==============================================================

	//	private class BlinkScroll
	//	{
	//		public BlinkScroll(int blinkScrollRate)
	//		{
	//			BlinkScrollRate = blinkScrollRate;
	//			var blinkScrollThread = new Thread(BlinkScrollThread);
	//			blinkScrollThread.Start();
	//		}

	//		public bool Blink
	//		{
	//			get { return _blink; }
	//			set
	//			{
	//				_blink = value;
					
	//			}
	//		}
	//		private bool _blink;

	//		public bool Scroll
	//		{
	//			get { return _scroll;}
	//			private set
	//			{
	//				_scroll = value;
	//				if (_scroll)
	//				{
	//					_waitBlinkScroll.Set();
	//					_runBlinkScrollLoop = true;

	//				}
	//				else
	//				{
	//					_waitBlinkScroll.Reset();
	//					_runBlinkScrollLoop = false;
	//				}
	//			}
	//		}
	//		private bool _scroll;

	//		public void SetDisplay(string display)
	//		{
	//			lock (_lcdChars)
	//			{
	//				_lcdCharsLen = display.Length;
	//				_lcdCharsPtr = 0;
	//				int i;
	//				for (i = 0; i < _lcdCharsLen; i++)
	//				{
	//					_lcdChars[i] = display[i].ToLcd();
	//				}
	//				// Add blanks at the end
	//				_lcdChars[i++] = _lcdChars[i++] = _lcdChars[i++] = _lcdChars[i++]
	//					= LCD.CHAR_NULL;
	//				_lcdCharsLen = i;
	//				// Decide whether to scroll or not
	//				Scroll = display.Length > 4;
	//			}
	//		}
	//		private readonly EmoteLCD _lcd = new EmoteLCD();

	//		private void BlinkScrollThread()
	//		{
	//			while (true)
	//			{
	//				_waitBlinkScroll.WaitOne();
	//				lock (_lcdChars)
	//				{
	//					if (Blink)
	//					{
	//						_lcd.Clear();
	//						Thread.Sleep(BlinkScrollRate);
	//					}
	//					WriteLcd();
	//					if (Scroll)
	//					{
	//						_lcdCharsPtr = (_lcdCharsPtr + 1)%_lcdCharsLen;
	//					}
	//					Thread.Sleep(BlinkScrollRate);
	//					if (!_runBlinkScrollLoop)
	//					{
	//						_waitBlinkScroll.Reset();
	//						_finishBlinkScroll.Set();
	//					}
	//				}
	//			}
	//		}

	//		private void WriteLcd()
	//		{
	//			var lcd1 = _lcdChars[_lcdCharsPtr];
	//			var lcd2 = _lcdChars[(_lcdCharsPtr + 1)%_lcdCharsLen];
	//			var lcd3 = _lcdChars[(_lcdCharsPtr + 2)%_lcdCharsLen];
	//			var lcd4 = _lcdChars[(_lcdCharsPtr + 3)%_lcdCharsLen];
	//			_lcd.Write(lcd4, lcd3, lcd2, lcd1);
	//		}

	//		private bool _runBlinkScrollLoop;

			
	//		private readonly ManualResetEvent _waitBlinkScroll = new ManualResetEvent(false);
	//		private readonly  ManualResetEvent _finishBlinkScroll = new ManualResetEvent(false);
	//		public const int MaxDisplaySize = 10;

	//		public int BlinkScrollRate { private get; set; }

	//		private LCD[] _lcdChars = new LCD[MaxDisplaySize + 4];
	//		private int _lcdCharsLen;
	//		private int _lcdCharsPtr;

	//	}
	//}
}
