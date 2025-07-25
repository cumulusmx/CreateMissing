﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace CreateMissing
{


	class DayFile
	{
		public List<Dayfilerec> DayfileRecs = [];

		public string LineEnding = string.Empty;

		private readonly string dayFileName = Program.location + "data" + Path.DirectorySeparatorChar + "dayfile.txt";

		public DayFile()
		{
			// read in the existing day file

			if (File.Exists(dayFileName + ".sav"))
			{
				Program.LogConsole("The dayfile.txt backup file dayfile.txt.sav already exists, aborting to prevent overwriting the original data.", ConsoleColor.Cyan);
				Program.LogConsole("Press any key to exit", ConsoleColor.DarkYellow);
				Console.ReadKey(true);
				Console.WriteLine("Exiting...");
				Environment.Exit(1);
			}

			LoadDayFile();
		}


		public void LoadDayFile()
		{
			int addedEntries = 0;
			var inv = CultureInfo.InvariantCulture;

			Program.LogMessage($"LoadDayFile: Attempting to load the day file");
			Console.WriteLine("Attempting to load the day file");
			if (File.Exists(dayFileName))
			{
				int linenum = 0;
				int errorCount = 0;

				// determine dayfile line ending
				if (Utils.TryDetectNewLine(dayFileName, out string lineend))
				{
					LineEnding = lineend;
				}

				// Clear the existing list
				DayfileRecs.Clear();

				try
				{
					using var sr = new StreamReader(dayFileName);
					var lastDate = DateTime.MinValue;

					do
					{
						try
						{
							// process each record in the file


							linenum++;
							string Line = sr.ReadLine();
							var newRec = ParseDayFileRec(Line);

							// sanity check if this date is in sequence
							if (newRec.Date < lastDate)
							{
								Program.LogMessage($"LoadDayFile: Error - Date is out of order at line {linenum} of {dayFileName}, '{newRec.Date.ToString("dd/MM/yy", inv)}'");
								Console.WriteLine();
								Program.LogConsole($"Error, date is out of order at line {linenum} of {dayFileName}, '{newRec.Date.ToString("dd/MM/yy", inv)}'", ConsoleColor.Red);
								errorCount++;
							}

							// sanity check if this date has already been added
							//var matches = DayfileRecs.Where(p => p.Date == newRec.Date).ToList()
							// (matches.Count > 0)
							//Since we now know the order is correct, we can do a simple date compare
							if (newRec.Date == lastDate)
							{
								Program.LogMessage($"LoadDayFile: Error - Duplicate date at line {linenum} of {dayFileName}, '{newRec.Date.ToString("dd/MM/yy", inv)}'");
								Console.WriteLine();
								Program.LogConsole($"Error, duplicate date at line {linenum} of {dayFileName}, '{newRec.Date.ToString("dd/MM/yy", inv)}'", ConsoleColor.Red);
								Environment.Exit(4);
							}

							if (errorCount == 0)
							{
								DayfileRecs.Add(newRec);
							}

							lastDate = newRec.Date;

							addedEntries++;
						}
						catch (Exception e)
						{
							Program.LogMessage($"LoadDayFile: Error at line {linenum} of {dayFileName} : {e.Message}");
							Program.LogMessage("Please edit the file to correct the error");
							errorCount++;
							if (errorCount >= 20)
							{
								Program.LogMessage($"LoadDayFile: Too many errors reading {dayFileName} - aborting load of daily data");
								Console.WriteLine();
								Program.LogConsole($"Too many errors reading {dayFileName} - aborting load of daily data", ConsoleColor.Red);
								Program.LogConsole("Please see the log file for more details", ConsoleColor.Red);
								Environment.Exit(5);
							}
						}
					} while (!sr.EndOfStream);
				}
				catch (Exception e)
				{
					Program.LogMessage($"LoadDayFile: Error at line {linenum} of {dayFileName} : {e.Message}");
					Program.LogMessage("Please edit the file to correct the error");
				}

				if (errorCount > 0)
				{
					Environment.Exit(3);
				}

				Program.LogMessage($"LoadDayFile: Loaded {addedEntries} entries to the daily data list");
				Console.WriteLine($"Loaded {addedEntries} entries to the daily data list");
			}
			else
			{
				Program.LogMessage("LoadDayFile: No Dayfile found - No entries added to recent daily data list");
				Program.LogConsole("No Dayfile found - No entries added to recent daily data list", ConsoleColor.Cyan);
				// add a rcord for yesterday, just so we have something to process,
				// if it is left at default we will not write it out
				var newRec = new Dayfilerec
				{
					Date = DateTime.Today.AddDays(-1)
				};
				DayfileRecs.Add(newRec);
			}
		}

		public void WriteDayFile()
		{
			// backup old dayfile.txt
			if (File.Exists(dayFileName))
			{
				File.Move(dayFileName, dayFileName + ".sav");
			}


			try
			{
				using FileStream fs = new FileStream(dayFileName, FileMode.Append, FileAccess.Write, FileShare.Read);
				using StreamWriter file = new StreamWriter(fs);
				Program.LogMessage("Dayfile.txt opened for writing");

				file.NewLine = LineEnding;

				foreach (var rec in DayfileRecs)
				{
					var line = RecToCsv(rec);
					if (null != line)
						file.WriteLine(line);
				}

				file.Close();
			}
			catch (Exception ex)
			{
				Program.LogMessage("Error writing to dayfile.txt: " + ex.Message);
			}

		}

		private static string RecToCsv(Dayfilerec rec)
		{
			// Writes an entry to the daily extreme log file. Fields are comma-separated.
			// 0   Date in the form dd/mm/yy (the slash may be replaced by a dash in some cases)
			// 1  Highest wind gust
			// 2  Bearing of highest wind gust
			// 3  Time of highest wind gust
			// 4  Minimum temperature
			// 5  Time of minimum temperature
			// 6  Maximum temperature
			// 7  Time of maximum temperature
			// 8  Minimum sea level pressure
			// 9  Time of minimum pressure
			// 10  Maximum sea level pressure
			// 11  Time of maximum pressure
			// 12  Maximum rainfall rate
			// 13  Time of maximum rainfall rate
			// 14  Total rainfall for the day
			// 15  Average temperature for the day
			// 16  Total wind run
			// 17  Highest average wind speed
			// 18  Time of highest average wind speed
			// 19  Lowest humidity
			// 20  Time of lowest humidity
			// 21  Highest humidity
			// 22  Time of highest humidity
			// 23  Total evapotranspiration
			// 24  Total hours of sunshine
			// 25  High heat index
			// 26  Time of high heat index
			// 27  High apparent temperature
			// 28  Time of high apparent temperature
			// 29  Low apparent temperature
			// 30  Time of low apparent temperature
			// 31  High hourly rain
			// 32  Time of high hourly rain
			// 33  Low wind chill
			// 34  Time of low wind chill
			// 35  High dew point
			// 36  Time of high dew point
			// 37  Low dew point
			// 38  Time of low dew point
			// 39  Dominant wind bearing
			// 40  Heating degree days
			// 41  Cooling degree days
			// 42  High solar radiation
			// 43  Time of high solar radiation
			// 44  High UV Index
			// 45  Time of high UV Index
			// 46  High Feels like
			// 47  Time of high feels like
			// 48  Low feels like
			// 49  Time of low feels like
			// 50  High Humidex
			// 51  Time of high Humidex

			// 52  Chill hours
			// 53  High 24 hr rain
			// 54  Time of high 24 hr rain

			var inv = CultureInfo.InvariantCulture;
			var sep = ",";

			// Write the date back using the same separator as the source file
			string datestring = rec.Date.ToString($"dd/MM/yy", inv);
			// NB this string is just for logging, the dayfile update code is further down
			var strb = new StringBuilder(300);
			strb.Append(datestring + sep);

			if (rec.HighGust == -9999)
			{
				Program.LogMessage("Mandatory value High Gust missing, skipping this day");
				return null;
				//strb.Append("0.0" + listsep + "0" + listsep + "00:00" + listsep)
			}
			else
			{
				strb.Append(rec.HighGust.ToString(Program.cumulus.WindFormat, inv) + sep);
				strb.Append(rec.HighGustBearing + sep);
				strb.Append(rec.HighGustTime.ToString("HH:mm", inv) + sep);
			}

			if (rec.LowTemp == 9999)
			{
				Program.LogMessage("Mandatory value Low Temp missing, skipping this day");
				return null;
				//strb.Append("0.0" + listsep + "00:00" + listsep)
			}
			else
			{
				strb.Append(rec.LowTemp.ToString(Program.cumulus.TempFormat, inv) + sep);
				strb.Append(rec.LowTempTime.ToString("HH:mm", inv) + sep);
			}

			if (rec.HighTemp == -9999)
			{
				Program.LogMessage("Mandatory value High Temp missing, skipping this day");
				return null;
				//strb.Append("0.0" + listsep + "00:00" + listsep)
			}
			else
			{
				strb.Append(rec.HighTemp.ToString(Program.cumulus.TempFormat, inv) + sep);
				strb.Append(rec.HighTempTime.ToString("HH:mm", inv) + sep);
			}

			if (rec.LowPress == 9999)
			{
				Program.LogMessage("Mandatory value Low Press missing, skipping this day");
				return null;
				//strb.Append("0.0" + listsep + "00:00" + listsep)
			}
			else
			{
				strb.Append(rec.LowPress.ToString(Program.cumulus.PressFormat, inv) + sep);
				strb.Append(rec.LowPressTime.ToString("HH:mm", inv) + sep);
			}

			if (rec.HighPress == -9999)
			{
				Program.LogMessage("Mandatory value High Press missing, skipping this day");
				return null;
				//strb.Append("0.0" + listsep + "00:00" + listsep)
			}
			else
			{
				strb.Append(rec.HighPress.ToString(Program.cumulus.PressFormat, inv) + sep);
				strb.Append(rec.HighPressTime.ToString("HH:mm", inv) + sep);
			}

			if (rec.HighRainRate == -9999)
			{
				Program.LogMessage("Mandatory value High Rain Rate missing, skipping this day");
				return null;
				//strb.Append("0.0" + listsep + "00:00" + listsep)
			}
			else
			{
				strb.Append(rec.HighRainRate.ToString(Program.cumulus.RainFormat, inv) + sep);
				strb.Append(rec.HighRainRateTime.ToString("HH:mm", inv) + sep);
			}

			if (rec.TotalRain == -9999)
			{
				Program.LogMessage("Mandatory value Total Rain missing, skipping this day");
				return null;
				//strb.Append("0.0" + listsep)
			}
			else
				strb.Append(rec.TotalRain.ToString(Program.cumulus.RainFormat, inv) + sep);

			if (rec.AvgTemp == -9999)
			{
				Program.LogMessage("Mandatory value Avg Temp missing, skipping this day");
				strb.Append(sep);
			}
			else
				strb.Append(rec.AvgTemp.ToString(Program.cumulus.TempFormat, inv) + sep);


			strb.Append(rec.WindRun.ToString("F1", inv) + sep);

			if (rec.HighAvgWind == -9999)
				strb.Append(sep + sep);
			else
			{
				strb.Append(rec.HighAvgWind.ToString(Program.cumulus.WindAvgFormat, inv) + sep);
				strb.Append(rec.HighAvgWindTime.ToString("HH:mm", inv) + sep);
			}

			if (rec.LowHumidity == 9999)
				strb.Append(sep + sep);
			else
			{
				strb.Append(rec.LowHumidity + sep);
				strb.Append(rec.LowHumidityTime.ToString("HH:mm", inv) + sep);
			}

			if (rec.HighHumidity == -9999)
				strb.Append(sep + sep);
			else
			{
				strb.Append(rec.HighHumidity + sep);
				strb.Append(rec.HighHumidityTime.ToString("HH:mm", inv) + sep);
			}

			strb.Append(rec.ET.ToString(Program.cumulus.ETFormat, inv) + sep);
			strb.Append(rec.SunShineHours.ToString(Program.cumulus.SunFormat, inv) + sep);

			if (rec.HighHeatIndex == -9999)
				strb.Append(sep + sep);
			else
			{
				strb.Append(rec.HighHeatIndex.ToString(Program.cumulus.TempFormat, inv) + sep);
				strb.Append(rec.HighHeatIndexTime.ToString("HH:mm", inv) + sep);
			}

			if (rec.HighAppTemp == -9999)
				strb.Append(sep + sep);
			else
			{
				strb.Append(rec.HighAppTemp.ToString(Program.cumulus.TempFormat, inv) + sep);
				strb.Append(rec.HighAppTempTime.ToString("HH:mm", inv) + sep);
			}

			if (rec.LowAppTemp == 9999)
				strb.Append(sep + sep);
			else
			{
				strb.Append(rec.LowAppTemp.ToString(Program.cumulus.TempFormat, inv) + sep);
				strb.Append(rec.LowAppTempTime.ToString("HH:mm", inv) + sep);
			}

			if (rec.HighHourlyRain == -9999)
				strb.Append(sep + sep);
			else
			{
				strb.Append(rec.HighHourlyRain.ToString(Program.cumulus.RainFormat, inv) + sep);
				strb.Append(rec.HighHourlyRainTime.ToString("HH:mm", inv) + sep);
			}

			if (rec.LowWindChill == 9999)
				strb.Append(sep + sep);
			else
			{
				strb.Append(rec.LowWindChill.ToString(Program.cumulus.TempFormat, inv) + sep);
				strb.Append(rec.LowWindChillTime.ToString("HH:mm", inv) + sep);
			}

			if (rec.HighDewPoint == -9999)
				strb.Append(sep + sep);
			else
			{
				strb.Append(rec.HighDewPoint.ToString(Program.cumulus.TempFormat, inv) + sep);
				strb.Append(rec.HighDewPointTime.ToString("HH:mm", inv) + sep);
			}

			if (rec.LowDewPoint == -9999)
				strb.Append(sep + sep);
			else
			{
				strb.Append(rec.LowDewPoint.ToString(Program.cumulus.TempFormat, inv) + sep);
				strb.Append(rec.LowDewPointTime.ToString("HH:mm", inv) + sep);
			}

			if (rec.DominantWindBearing == 9999)
				strb.Append(sep);
			else
				strb.Append(rec.DominantWindBearing + sep);

			if (rec.HeatingDegreeDays == -9999)
				strb.Append(sep);
			else
				strb.Append(rec.HeatingDegreeDays.ToString("F1", inv) + sep);

			if (rec.CoolingDegreeDays == -9999)
				strb.Append(sep);
			else
				strb.Append(rec.CoolingDegreeDays.ToString("F1", inv) + sep);

			strb.Append(rec.HighSolar + sep);
			strb.Append(rec.HighSolarTime.ToString("HH:mm", inv) + sep);
			strb.Append(rec.HighUv.ToString(Program.cumulus.UVFormat, inv) + sep);
			strb.Append(rec.HighUvTime.ToString("HH:mm", inv) + sep);

			if (rec.HighFeelsLike == -9999)
				strb.Append(sep + sep);
			else
			{
				strb.Append(rec.HighFeelsLike.ToString(Program.cumulus.TempFormat, inv) + sep);
				strb.Append(rec.HighFeelsLikeTime.ToString("HH:mm", inv) + sep);
			}

			if (rec.LowFeelsLike == 9999)
				strb.Append(sep + sep);
			else
			{
				strb.Append(rec.LowFeelsLike.ToString(Program.cumulus.TempFormat, inv) + sep);
				strb.Append(rec.LowFeelsLikeTime.ToString("HH:mm", inv) + sep);
			}

			if (rec.HighHumidex == -9999)
				strb.Append(sep + sep);
			else
			{
				strb.Append(rec.HighHumidex.ToString(Program.cumulus.TempFormat, inv) + sep);
				strb.Append(rec.HighHumidexTime.ToString("HH:mm", inv) + sep);
			}

			if (rec.ChillHours != -9999)
				strb.Append(rec.ChillHours.ToString("F1", inv) + sep);

			if (rec.HighRain24h != -9999)
			{
				strb.Append(rec.HighRain24h.ToString(Program.cumulus.TempFormat, inv) + sep);
				strb.Append(rec.HighRain24hTime.ToString("HH:mm", inv));
			}

			Program.LogMessage("Dayfile.txt Added: " + datestring);

			return strb.ToString();
		}

		private static Dayfilerec ParseDayFileRec(string data)
		{
			var st = new List<string>(data.Split(','));
			double varDbl;
			int varInt;
			int idx = 0;
			var inv = CultureInfo.InvariantCulture;

			var rec = new Dayfilerec();
			try
			{
				rec.Date = Utils.DdmmyyStrToDate(st[idx++]);
				rec.HighGust = Convert.ToDouble(st[idx++], inv);
				rec.HighGustBearing = Convert.ToInt32(st[idx++]);
				rec.HighGustTime = Utils.GetDateTime(rec.Date, st[idx++]);
				rec.LowTemp = Convert.ToDouble(st[idx++], inv);
				rec.LowTempTime = Utils.GetDateTime(rec.Date, st[idx++]);
				rec.HighTemp = Convert.ToDouble(st[idx++], inv);
				rec.HighTempTime = Utils.GetDateTime(rec.Date, st[idx++]);
				rec.LowPress = Convert.ToDouble(st[idx++], inv);
				rec.LowPressTime = Utils.GetDateTime(rec.Date, st[idx++]);
				rec.HighPress = Convert.ToDouble(st[idx++], inv);
				rec.HighPressTime = Utils.GetDateTime(rec.Date, st[idx++]);
				rec.HighRainRate = Convert.ToDouble(st[idx++], inv);
				rec.HighRainRateTime = Utils.GetDateTime(rec.Date, st[idx++]);
				rec.TotalRain = Convert.ToDouble(st[idx++], inv);
				rec.AvgTemp = Convert.ToDouble(st[idx++], inv);

				if (st.Count > idx++ && double.TryParse(st[16], inv, out varDbl))
					rec.WindRun = varDbl;

				if (st.Count > idx++ && double.TryParse(st[17], inv, out varDbl))
					rec.HighAvgWind = varDbl;

				if (st.Count > idx++ && st[18].Length == 5)
					rec.HighAvgWindTime = Utils.GetDateTime(rec.Date, st[18]);

				if (st.Count > idx++ && int.TryParse(st[19], out varInt))
					rec.LowHumidity = varInt;

				if (st.Count > idx++ && st[20].Length == 5)
					rec.LowHumidityTime = Utils.GetDateTime(rec.Date, st[20]);

				if (st.Count > idx++ && int.TryParse(st[21], out varInt))
					rec.HighHumidity = varInt;

				if (st.Count > idx++ && st[22].Length == 5)
					rec.HighHumidityTime = Utils.GetDateTime(rec.Date, st[22]);

				if (st.Count > idx++ && double.TryParse(st[23], inv, out varDbl))
					rec.ET = varDbl;

				if (st.Count > idx++ && double.TryParse(st[24], inv, out varDbl))
					rec.SunShineHours = varDbl;

				if (st.Count > idx++ && double.TryParse(st[25], inv, out varDbl))
					rec.HighHeatIndex = varDbl;

				if (st.Count > idx++ && st[26].Length == 5)
					rec.HighHeatIndexTime = Utils.GetDateTime(rec.Date, st[26]);

				if (st.Count > idx++ && double.TryParse(st[27], inv, out varDbl))
					rec.HighAppTemp = varDbl;

				if (st.Count > idx++ && st[28].Length == 5)
					rec.HighAppTempTime = Utils.GetDateTime(rec.Date, st[28]);

				if (st.Count > idx++ && double.TryParse(st[29], inv, out varDbl))
					rec.LowAppTemp = varDbl;

				if (st.Count > idx++ && st[30].Length == 5)
					rec.LowAppTempTime = Utils.GetDateTime(rec.Date, st[30]);

				if (st.Count > idx++ && double.TryParse(st[31], inv, out varDbl))
					rec.HighHourlyRain = varDbl;

				if (st.Count > idx++ && st[32].Length == 5)
					rec.HighHourlyRainTime = Utils.GetDateTime(rec.Date, st[32]);

				if (st.Count > idx++ && double.TryParse(st[33], inv, out varDbl))
					rec.LowWindChill = varDbl;

				if (st.Count > idx++ && st[34].Length == 5)
					rec.LowWindChillTime = Utils.GetDateTime(rec.Date, st[34]);

				if (st.Count > idx++ && double.TryParse(st[35], inv, out varDbl))
					rec.HighDewPoint = varDbl;

				if (st.Count > idx++ && st[36].Length == 5)
					rec.HighDewPointTime = Utils.GetDateTime(rec.Date, st[36]);

				if (st.Count > idx++ && double.TryParse(st[37], inv, out varDbl))
					rec.LowDewPoint = varDbl;

				if (st.Count > idx++ && st[38].Length == 5)
					rec.LowDewPointTime = Utils.GetDateTime(rec.Date, st[38]);

				if (st.Count > idx++ && int.TryParse(st[39], out varInt))
					rec.DominantWindBearing = varInt;

				if (st.Count > idx++ && double.TryParse(st[40], inv, out varDbl))
					rec.HeatingDegreeDays = varDbl;

				if (st.Count > idx++ && double.TryParse(st[41], inv, out varDbl))
					rec.CoolingDegreeDays = varDbl;

				if (st.Count > idx++ && int.TryParse(st[42], out varInt))
					rec.HighSolar = varInt;

				if (st.Count > idx++ && st[43].Length == 5)
					rec.HighSolarTime = Utils.GetDateTime(rec.Date, st[43]);

				if (st.Count > idx++ && double.TryParse(st[44], inv, out varDbl))
					rec.HighUv = varDbl;

				if (st.Count > idx++ && st[45].Length == 5)
					rec.HighUvTime = Utils.GetDateTime(rec.Date, st[45]);

				if (st.Count > idx++ && double.TryParse(st[46], inv, out varDbl))
					rec.HighFeelsLike = varDbl;

				if (st.Count > idx++ && st[47].Length == 5)
					rec.HighFeelsLikeTime = Utils.GetDateTime(rec.Date, st[47]);

				if (st.Count > idx++ && double.TryParse(st[48], inv, out varDbl))
					rec.LowFeelsLike = varDbl;

				if (st.Count > idx++ && st[49].Length == 5)
					rec.LowFeelsLikeTime = Utils.GetDateTime(rec.Date, st[49]);

				if (st.Count > idx++ && double.TryParse(st[50], inv, out varDbl))
					rec.HighHumidex = varDbl;

				if (st.Count > idx++ && st[51].Length == 5)
					rec.HighHumidexTime = Utils.GetDateTime(rec.Date, st[51]);

				if (st.Count > idx++ && double.TryParse(st[52], inv, out varDbl))
					rec.ChillHours = varDbl;

				if (st.Count > idx++ && double.TryParse(st[53], inv, out varDbl))
					rec.HighRain24h = varDbl;

				if (st.Count > idx++ && st[54].Length == 5)
					rec.HighRain24hTime = Utils.GetDateTime(rec.Date, st[54]);

			}
			catch (Exception ex)
			{
				Program.LogMessage($"ParseDayFileRec: Error at record {idx} - {ex.Message}");
				var e = new Exception($"Error at record {idx} = \"{st[idx - 1]}\" - {ex.Message}");
				throw e;
			}
			return rec;
		}
	}

	public class Dayfilerec
	{
		public DateTime Date { get; set; }
		public double HighGust { get; set; }
		public int HighGustBearing { get; set; }
		public DateTime HighGustTime { get; set; }
		public double LowTemp { get; set; }
		public DateTime LowTempTime { get; set; }
		public double HighTemp { get; set; }
		public DateTime HighTempTime { get; set; }
		public double LowPress { get; set; }
		public DateTime LowPressTime { get; set; }
		public double HighPress { get; set; }
		public DateTime HighPressTime { get; set; }
		public double HighRainRate { get; set; }
		public DateTime HighRainRateTime { get; set; }
		public double TotalRain { get; set; }
		public double AvgTemp { get; set; }
		public double WindRun { get; set; }
		public double HighAvgWind { get; set; }
		public DateTime HighAvgWindTime { get; set; }
		public int LowHumidity { get; set; }
		public DateTime LowHumidityTime { get; set; }
		public int HighHumidity { get; set; }
		public DateTime HighHumidityTime { get; set; }
		public double ET { get; set; }
		public double SunShineHours { get; set; }
		public double HighHeatIndex { get; set; }
		public DateTime HighHeatIndexTime { get; set; }
		public double HighAppTemp { get; set; }
		public DateTime HighAppTempTime { get; set; }
		public double LowAppTemp { get; set; }
		public DateTime LowAppTempTime { get; set; }
		public double HighHourlyRain { get; set; }
		public DateTime HighHourlyRainTime { get; set; }
		public double LowWindChill { get; set; }
		public DateTime LowWindChillTime { get; set; }
		public double HighDewPoint { get; set; }
		public DateTime HighDewPointTime { get; set; }
		public double LowDewPoint { get; set; }
		public DateTime LowDewPointTime { get; set; }
		public int DominantWindBearing { get; set; }
		public double HeatingDegreeDays { get; set; }
		public double CoolingDegreeDays { get; set; }
		public int HighSolar { get; set; }
		public DateTime HighSolarTime { get; set; }
		public double HighUv { get; set; }
		public DateTime HighUvTime { get; set; }
		public double HighFeelsLike { get; set; }
		public DateTime HighFeelsLikeTime { get; set; }
		public double LowFeelsLike { get; set; }
		public DateTime LowFeelsLikeTime { get; set; }
		public double HighHumidex { get; set; }
		public DateTime HighHumidexTime { get; set; }
		public double ChillHours { get; set; }
		public double HighRain24h { get; set; }
		public DateTime HighRain24hTime { get; set; }

		public Dayfilerec()
		{
			HighGust = -9999;
			HighGustBearing = 0;
			LowTemp = 9999;
			HighTemp = -9999;
			LowPress = 9999;
			HighPress = -9999;
			HighRainRate = -9999;
			TotalRain = -9999;
			AvgTemp = -9999;
			WindRun = -9999;
			HighAvgWind = -9999;
			LowHumidity = 9999;
			HighHumidity = -9999;
			ET = -9999;
			SunShineHours = -9999;
			HighHeatIndex = -9999;
			HighAppTemp = -9999;
			LowAppTemp = 9999;
			HighHourlyRain = -9999;
			LowWindChill = 9999;
			HighDewPoint = -9999;
			LowDewPoint = 9999;
			DominantWindBearing = 9999;
			HeatingDegreeDays = -9999;
			CoolingDegreeDays = -9999;
			HighSolar = -9999;
			HighUv = -9999;
			HighFeelsLike = -9999;
			LowFeelsLike = 9999;
			HighHumidex = -9999;
			ChillHours = -9999;
			HighRain24h = -9999;
		}

		public bool HasMissingData()
		{
			if (HighHumidex == -9999 || LowFeelsLike == 9999 || HighFeelsLike == -9999 || CoolingDegreeDays == -9999 || HeatingDegreeDays == -9999 ||
				DominantWindBearing == 9999 || LowDewPoint == 9999 || HighDewPoint == -9999 || LowWindChill == 9999 || HighHourlyRain == -9999 ||
				LowAppTemp == 9999 || HighAppTemp == -9999 || HighHeatIndex == -9999 || HighHumidity == -9999 || LowHumidity == 9999 ||
				HighAvgWind == -9999 || AvgTemp == -9999 || HighRainRate == -9999 || LowPress == 9999 || HighPress == -9999 ||
				HighTemp == -9999 || LowTemp == 9999 || HighGust == -9999 || ChillHours == -9999 || HighRain24h == -9999 || ET == -9999)
			{
				return true;
			}

			return false;
		}
	}
}
