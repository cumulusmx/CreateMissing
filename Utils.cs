﻿using CumulusMX;
using System;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace CreateMissing
{
	class Utils
	{
		public static DateTime DdmmyyStrToDate(string d)
		{
			if (DateTime.TryParseExact(d, "dd/MM/yy", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var result))
			{
				return result;
			}
			return DateTime.MinValue;
		}

		public static DateTime DdmmyyhhmmStrToDate(string d, string t)
		{
			if (DateTime.TryParseExact(d + ' ' + t, "dd/MM/yy HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var result))
			{
				return result;
			}
			return DateTime.MinValue;
		}

		public static DateTime GetDateTime(DateTime date, string time)
		{
			var tim = time.Split(',');
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
			return Program.cumulus.Units.Wind switch
			{
				// m/s
				0 => wind * 3.6,
				// mph
				1 => wind * 1.609344,
				// kph
				2 => wind,
				// knots
				3 => wind * 1.852,
				_ => wind,
			};
		}

		public static double UserWindToMS(double value)
		{
			return Program.cumulus.Units.Wind switch
			{
				0 => value,
				1 => value / 2.23693629,
				2 => value / 3.6F,
				3 => value / 1.94384449,
				_ => 0,
			};
		}

		public static double WindMSToUser(double value)
		{
			return Program.cumulus.Units.Wind switch
			{
				0 => value,
				1 => value * 2.23693629,
				2 => value * 3.6,
				3 => value * 1.94384449,
				_ => 0,
			};
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
			using var fs = File.OpenRead(path);
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
}
