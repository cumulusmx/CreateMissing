using CumulusMX;
using System;
using System.Collections.Generic;
using System.Text;

namespace CreateMissing
{
	class Utils
	{

		public static double ConvertUserTempToC(double value)
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

		public static double ConvertUserWindToKPH(double wind) // input is in Units.Wind units, convert to km/h
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

		public static double ConvertUserWindToMS(double value)
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


		public static int CalcAvgBearing(double x, double y)
		{
			var avg = 90 - (int)(180 / Math.PI * Math.Atan2(y, x));
			if (avg < 0)
			{
				avg = 360 + avg;
			}

			return avg;
		}


	}
}
