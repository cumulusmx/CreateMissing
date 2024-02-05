using CumulusMX;
using System;
using System.IO;
using System.Text.RegularExpressions;

namespace CreateMissing
{
	class Utils
	{
		public static DateTime DdmmyyStrToDate(string d)
		{
			// Horrible hack, but we have localised separators, but UK sequence, so localised parsing may fail
			// Determine separators from the strings, allow for multi-byte!
			var datSep = Regex.Match(d, @"[^0-9]+").Value;

			// Converts a date string in UK order to a DateTime
			string[] date = d.Split(new string[] { datSep }, StringSplitOptions.None);

			int D = Convert.ToInt32(date[0]);
			int M = Convert.ToInt32(date[1]);
			int Y = Convert.ToInt32(date[2]);
			if (Y < 1900)
			{
				Y += Y > 70 ? 1900 : 2000;
			}
			return new DateTime(Y, M, D);
		}

		public static DateTime DdmmyyhhmmStrToDate(string d, string t)
		{
			// Horrible hack, but we have localised separators, but UK sequence, so localised parsing may fail
			// Determine separators from the strings, allow for multi-byte!
			var datSep = Regex.Match(d, @"[^0-9]+").Value;
			var timSep = Regex.Match(t, @"[^0-9]+").Value;

			// Converts a date string in UK order to a DateTime
			string[] date = d.Split(new string[] { datSep }, StringSplitOptions.None);
			string[] time = t.Split(new string[] { timSep }, StringSplitOptions.None);

			int D = Convert.ToInt32(date[0]);
			int M = Convert.ToInt32(date[1]);
			int Y = Convert.ToInt32(date[2]);

			// Double check - just in case we get a four digit year!
			if (Y < 1900)
			{
				Y += Y > 70 ? 1900 : 2000;
			}
			int h = Convert.ToInt32(time[0]);
			int m = Convert.ToInt32(time[1]);

			return new DateTime(Y, M, D, h, m, 0);
		}

		public static DateTime GetDateTime(DateTime date, string time)
		{
			var timSep = Regex.Match(time, @"[^0-9]+").Value;

			var tim = time.Split(new string[] { timSep }, StringSplitOptions.None);
			return new DateTime(date.Year, date.Month, date.Day, int.Parse(tim[0]), int.Parse(tim[1]), 0);
		}

		public static double UserTempToC(double value)
		{
			if (Program.cumulus.Units.Temp == 1)
			{
				return MeteoLib.FtoC(value);
			}
			else
			{
				// C
				return value;
			}
		}

		public static double TempCToUser(double value)
		{
			if (Program.cumulus.Units.Temp == 1)
			{
				return MeteoLib.CToF(value);
			}
			else
			{
				// C
				return value;
			}
		}

		public static double UserWindToKPH(double wind) // input is in Units.Wind units, convert to km/h
		{
			switch (Program.cumulus.Units.Wind)
			{
				// m/s
				case 0:
					return wind * 3.6;
				// mph
				case 1:
					return wind * 1.609344;
				// kph
				case 2:
					return wind;
				// knots
				case 3:
					return wind * 1.852;
				default:
					return wind;
			};
		}

		public static double UserWindToMS(double value)
		{
			switch (Program.cumulus.Units.Wind)
			{
				case 0:
					return value;
				case 1:
					return value / 2.23693629;
				case 2:
					return value / 3.6F;
				case 3:
					return value / 1.94384449;
				default:
					return 0;
			};
		}

		public static double WindMSToUser(double value)
		{
			switch (Program.cumulus.Units.Wind)
			{
				case 0:
					return value;
				case 1:
					return value * 2.23693629;
				case 2:
					return value * 3.6;
				case 3:
					return value * 1.94384449;
				default:
					return 0;
			}
		}

		public static int CalcAvgBearing(double x, double y)
		{
			var avg = 90 - (int)(180 / Math.PI * Math.Atan2(y, x));
			if (avg < 0)
			{
				avg = 360 + avg;
			}

			return avg;
		}

		public static bool TryDetectNewLine(string path, out string newLine)
		{
			using (var fs = File.OpenRead(path))
			{
				char prevChar = '\0';

				// read the first 1000 characters to try and find a newLine
				for (var i = 0; i < 1000; i++)
				{
					int b;
					if ((b = fs.ReadByte()) == -1)
						break;

					char curChar = (char)b;

					if (curChar == '\n')
					{
						newLine = prevChar == '\r' ? "\r\n" : "\n";
						return true;
					}

					prevChar = curChar;
				}

				// Returning false means could not determine linefeed convention
				newLine = Environment.NewLine;
				return false;
			}
		}

		public static void GetLogFileSeparators(string path, string defSep, out string fieldSep, out string dateSep)
		{
			// we know the dayfile and monthly log files start with
			// dd/MM/yy,NN,...
			// dd/MM/yy,hh:mm,N.N,....
			// so we just need to find the first separator after the date before a number
			using (var sr = new StreamReader(path))
			{
				string line = sr.ReadLine();
				var reg = Regex.Match(line, @"\d{2}[^\d]+\d{2}[^\d]+\d{2}([^\d])");
				if (reg.Success)
					fieldSep = reg.Groups[1].Value;
				else
					fieldSep = defSep;

				var fields = line.Split(new string[] { fieldSep }, StringSplitOptions.None);
				dateSep = Regex.Match(fields[0], @"[^0-9]+").Value;
			}
			Program.LogMessage($"File [{path}] found separators: field=[{fieldSep}] date=[{dateSep}]");
		}
	}
}
