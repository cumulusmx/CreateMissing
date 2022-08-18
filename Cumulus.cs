using CumulusMX;
using System;
using System.Collections.Generic;
using System.Text;

namespace CreateMissing
{
	class Cumulus
	{
		public string RecordsBeganDate;
		public int RolloverHour;
		public bool Use10amInSummer;

		public string TempFormat;
		public string WindFormat;
		public string WindAvgFormat;
		public string RainFormat;
		public string PressFormat;
		public string UVFormat;
		public string SunFormat;
		public string ETFormat;
		public string WindRunFormat;
		public string TempTrendFormat;

		public double NOAAheatingthreshold;
		public double NOAAcoolingthreshold;

		public int ChillHourSeasonStart;
		public double ChillHourThreshold;

		public double CalibRainMult;

		private readonly StationOptions StationOptions = new StationOptions();
		internal StationUnits Units = new StationUnits();
		private readonly int[] WindDPlaceDefaults = { 1, 0, 0, 0 }; // m/s, mph, km/h, knots
		private readonly int[] TempDPlaceDefaults = { 1, 1 };
		private readonly int[] PressDPlaceDefaults = { 1, 1, 2 };
		private readonly int[] RainDPlaceDefaults = { 1, 2 };

		public Cumulus()
		{
			// Get all the stuff we need from Cumulus.ini
			ReadIniFile();

			TempFormat = "F" + Units.TempDPlaces;
			WindFormat = "F" + Units.WindDPlaces;
			WindAvgFormat = "F" + Units.WindAvgDPlaces;
			RainFormat = "F" + Units.RainDPlaces;
			PressFormat = "F" + Units.PressDPlaces;
			UVFormat = "F" + Units.UVDPlaces;
			SunFormat = "F" + Units.SunshineDPlaces;
			ETFormat = "F" + (Units.RainDPlaces + 1);
			WindRunFormat = "F" + Units.WindRunDPlaces;
			TempTrendFormat = "+0.0;-0.0;0";

		}

		private void ReadIniFile()
		{
			if (!System.IO.File.Exists(Program.location + "Cumulus.ini"))
			{
				Program.LogMessage("Failed to find Cumulus.ini file!");
				Console.WriteLine("Failed to find Cumulus.ini file!");
				Environment.Exit(1);
			}

			Program.LogMessage("Reading Cumulus.ini file");

			IniFile ini = new IniFile("Cumulus.ini");

			var StationType = ini.GetValue("Station", "Type", -1);

			var IncrementPressureDP = ini.GetValue("Station", "DavisIncrementPressureDP", false);

			RecordsBeganDate = ini.GetValue("Station", "StartDate", DateTime.Now.ToLongDateString());


			if ((StationType == 0) || (StationType == 1))
			{
				Units.UVDPlaces = 1;
			}
			else
			{
				Units.UVDPlaces = 0;
			}

			RolloverHour = ini.GetValue("Station", "RolloverHour", 0);
			Use10amInSummer = ini.GetValue("Station", "Use10amInSummer", true);

			Units.Wind = ini.GetValue("Station", "WindUnit", 0);
			Units.Press = ini.GetValue("Station", "PressureUnit", 0);

			Units.Rain = ini.GetValue("Station", "RainUnit", 0);
			Units.Temp = ini.GetValue("Station", "TempUnit", 0);

			var RoundWindSpeed = ini.GetValue("Station", "RoundWindSpeed", false);

			// Unit decimals
			Units.RainDPlaces = RainDPlaceDefaults[Units.Rain];
			Units.TempDPlaces = TempDPlaceDefaults[Units.Temp];
			Units.PressDPlaces = PressDPlaceDefaults[Units.Press];
			Units.WindDPlaces = RoundWindSpeed ? 0 : WindDPlaceDefaults[Units.Wind];
			Units.WindAvgDPlaces = Units.WindDPlaces;

			// Unit decimal overrides
			Units.WindDPlaces = ini.GetValue("Station", "WindSpeedDecimals", Units.WindDPlaces);
			Units.WindAvgDPlaces = ini.GetValue("Station", "WindSpeedAvgDecimals", Units.WindAvgDPlaces);
			Units.WindRunDPlaces = ini.GetValue("Station", "WindRunDecimals", Units.WindRunDPlaces);
			Units.SunshineDPlaces = ini.GetValue("Station", "SunshineHrsDecimals", 1);

			if ((StationType == 0 || StationType == 1) && IncrementPressureDP)
			{
				// Use one more DP for Davis stations
				++Units.PressDPlaces;
			}
			Units.PressDPlaces = ini.GetValue("Station", "PressDecimals", Units.PressDPlaces);
			Units.RainDPlaces = ini.GetValue("Station", "RainDecimals", Units.RainDPlaces);
			Units.TempDPlaces = ini.GetValue("Station", "TempDecimals", Units.TempDPlaces);
			Units.UVDPlaces = ini.GetValue("Station", "UVDecimals", Units.UVDPlaces);

			StationOptions.UseZeroBearing = ini.GetValue("Station", "UseZeroBearing", false);

			NOAAheatingthreshold = ini.GetValue("NOAA", "HeatingThreshold", -1000.0);
			if (NOAAheatingthreshold < -99 || NOAAheatingthreshold > 150)
			{
				NOAAheatingthreshold = Units.Temp == 0 ? 18.3 : 65;
			}
			NOAAcoolingthreshold = ini.GetValue("NOAA", "CoolingThreshold", -1000.0);
			if (NOAAcoolingthreshold < -99 || NOAAcoolingthreshold > 150)
			{
				NOAAcoolingthreshold = Units.Temp == 0 ? 18.3 : 65;
			}

			CalibRainMult = ini.GetValue("Offsets", "RainMult", 1.0);

			ChillHourSeasonStart = ini.GetValue("Station", "ChillHourSeasonStart", 10);
			if (ChillHourSeasonStart < 1 || ChillHourSeasonStart > 12)
				ChillHourSeasonStart = 1;
			ChillHourThreshold = ini.GetValue("Station", "ChillHourThreshold", -999.0);
			if (ChillHourThreshold < -998)
			{
				ChillHourThreshold = Units.Temp == 0 ? 7 : 45;
			}
		}

	}

	internal class StationUnits
	{
		public int Wind { get; set; }
		public int Press { get; set; }
		public int Rain { get; set; }
		public int Temp { get; set; }
		public int WindDPlaces { get; set; }
		public int PressDPlaces { get; set; }
		public int RainDPlaces { get; set; }
		public int TempDPlaces { get; set; }
		public int WindAvgDPlaces { get; set; }
		public int WindRunDPlaces { get; set; }
		public int SunshineDPlaces { get; set; }
		public int UVDPlaces { get; set; }
	}

	public class StationOptions
	{
		public bool UseZeroBearing { get; set; }
		public bool UseWind10MinAve { get; set; }
		public bool UseSpeedForAvgCalc { get; set; }
		public bool UseSpeedForLatest { get; set; }
		public bool Humidity98Fix { get; set; }
		public bool CalculatedDP { get; set; }
		public bool CalculatedWC { get; set; }
		public bool SyncTime { get; set; }
		public int ClockSettingHour { get; set; }
		public bool UseCumulusPresstrendstr { get; set; }
		public bool LogExtraSensors { get; set; }
		public bool WS2300IgnoreStationClock { get; set; }
		public bool RoundWindSpeed { get; set; }
		public int PrimaryAqSensor { get; set; }
		public bool NoSensorCheck { get; set; }
		public int AvgBearingMinutes { get; set; }
		public int AvgSpeedMinutes { get; set; }
		public int PeakGustMinutes { get; set; }
	}
}
