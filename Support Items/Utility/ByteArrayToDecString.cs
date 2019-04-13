using System.Text;

namespace Samraksh.Components.Utility
{
	public static class Convert
	{
		private static readonly StringBuilder Sb = new StringBuilder();
		public static string ByteArrayToDecString(byte[] byteArray)
		{
			Sb.Clear();

			for (var i = 0; i < byteArray.Length; i++)
			{
				Sb.Append(byteArray[i].ToString());
				Sb.Append(' ');
			}
			return Sb.ToString();
		}
	}
}
