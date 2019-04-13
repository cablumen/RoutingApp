using System.Text;

namespace Samraksh.Components.Utility
{
	public static class ExtensionMethods
	{

#region String Extension Methods ==================================
		// Thanks to http://extensionmethod.net/csharp/string/string-extensions

		/// <summary>
       /// Checks string object's value to array of string values
       /// </summary>        
       /// <param name="stringValues">Array of string values to compare</param>
       /// <returns>Return true if any string value matches</returns>
       public static bool In(this string value, params string[] stringValues) {
           foreach (string otherValue in stringValues)
               if (string.Compare(value, otherValue) == 0)
                   return true;
 
           return false;
       }
 
	   ///// <summary>
	   ///// Converts string to enum object
	   ///// </summary>
	   ///// <typeparam name="T">Type of enum</typeparam>
	   ///// <param name="value">String value to convert</param>
	   ///// <returns>Returns enum object</returns>
	   //public static T ToEnum<T>(this string value)
	   //	where T : struct
	   //{
	   //	return (T) System.Enum.Parse(typeof (T), value, true);
	   //}
 
       /// <summary>
       /// Returns characters from right of specified length
       /// </summary>
       /// <param name="value">String value</param>
       /// <param name="length">Max number of charaters to return</param>
       /// <returns>Returns string from right</returns>
       public static string Right(this string value, int length)
       {
           return value != null && value.Length > length ? value.Substring(value.Length - length) : value;
       }
 
       /// <summary>
       /// Returns characters from left of specified length
       /// </summary>
       /// <param name="value">String value</param>
       /// <param name="length">Max number of charaters to return</param>
       /// <returns>Returns string from left</returns>
       public static string Left(this string value, int length)
       {
           return value != null && value.Length > length ? value.Substring(0, length) : value;
       }
 
	   ///// <summary>
	   /////  Replaces the format item in a specified System.String with the text equivalent
	   /////  of the value of a specified System.Object instance.
	   ///// </summary>
	   ///// <param name="value">A composite format string</param>
	   ///// <param name="arg0">An System.Object to format</param>
	   ///// <returns>A copy of format in which the first format item has been replaced by the
	   ///// System.String equivalent of arg0</returns>
	   //public static string Format(this string value, object arg0)
	   //{
	   //	return string.Format(value, arg0);
	   //}
 
	   ///// <summary>
	   /////  Replaces the format item in a specified System.String with the text equivalent
	   /////  of the value of a specified System.Object instance.
	   ///// </summary>
	   ///// <param name="value">A composite format string</param>
	   ///// <param name="args">An System.Object array containing zero or more objects to format.</param>
	   ///// <returns>A copy of format in which the format items have been replaced by the System.String
	   ///// equivalent of the corresponding instances of System.Object in args.</returns>
	   //public static string Format(this string value, params object[] args)
	   //{
	   //	return string.Format(value, args);
	   //}
   
#endregion

		/// <summary>
		/// Convert byte array to char array
		/// </summary>
		/// <param name="byteArr"></param>
		/// <returns></returns>
		public static char[] ToCharArray(this byte[] byteArr)
		{
			var retVal = new char[byteArr.Length];
			for (var i = 0; i < byteArr.Length; i++)
			{
				retVal[i] = (char)byteArr[i];
			}
			return retVal;
		}

		/// <summary>
		/// Convert byte array to char array
		/// </summary>
		/// <param name="byteArr"></param>
		/// <param name="start"></param>
		/// <param name="length"></param>
		/// <returns></returns>
		public static char[] ToCharArray(this byte[] byteArr, int start, int length)
		{
			var retVal = new char[length - start - 1];
			for (var i = start; i < length; i++)
			{
				retVal[i] = (char)byteArr[i];
			}
			return retVal;
		}

		/// <summary>
		/// Convert char array to byte array
		/// </summary>
		/// <param name="charArr"></param>
		/// <returns></returns>
		public static byte[] ToByteArray(this char[] charArr)
		{
			var retVal = new byte[charArr.Length];
			for (var i = 0; i < charArr.Length; i++)
			{
				retVal[i] = (byte)charArr[i];
			}
			return retVal;
		}

		/// <summary>
		/// Pad left a string
		/// </summary>
		/// <param name="instring"></param>
		/// <param name="outLength"></param>
		/// <param name="padding"></param>
		/// <returns></returns>
		public static string PadLeft(this string instring, int outLength, char padding)
		{
			if (instring.Length >= outLength)
			{
				return instring;
			}
			var sb = new StringBuilder();
			for (var i = 0; i <= outLength - instring.Length; i++)
			{
				sb.Append(padding);
			}
			return sb.ToString();
			;
		}
	}
}
