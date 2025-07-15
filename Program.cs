using CumulusMX;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;

namespace CreateMissing
{
	static class Program
	{
		public static Cumulus cumulus;
		public static string location;

		private static ConsoleColor defConsoleColour;

		private static DayFile dayfile;
		private static readonly List<string> CurrentLogLines = [];
		private static string CurrentLogName;
		private static int CurrentLogLineNum = 0;

		private static readonly List<string> CurrentSolarLogLines = [];
		private static string CurrentSolarLogName;
		private static int CurrentSolarLogLineNum = 0;

		private static int RecsAdded = 0;
		private static int RecsUpdated = 0;
		private static int RecsNoData = 0;
		private static int RecsOK = 0;

		private static double TotalChillHours;

		private static WeatherDataDict CurrentWeatherData = new WeatherDataDict();

		static void Main()
		{
#if DEBUG
			//System.Threading.Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("sl-SL")
#endif
			TextWriterTraceListener myTextListener = new TextWriterTraceListener($"MXdiags{Path.DirectorySeparatorChar}CreateMissing-{DateTime.Now:yyyyMMdd-HHmmss}.txt", "CMlog");
			Trace.Listeners.Add(myTextListener);
			Trace.AutoFlush = true;

			defConsoleColour = Console.ForegroundColor;

			var fullVer = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
			var version = $"{fullVer.Major}.{fullVer.Minor}.{fullVer.Build}";
			LogMessage("CreateMissing v." + version);
			Console.WriteLine("CreateMissing v." + version);

			LogMessage("Processing started");
			Console.WriteLine();
			Console.WriteLine($"Processing started: {DateTime.Now:U}");
			Console.WriteLine();

			// get the location of the exe - we will assume this is in the Cumulus root folder
			location = AppDomain.CurrentDomain.BaseDirectory;

			cumulus = new Cumulus();

			// load existing day file
			dayfile = new DayFile();

			// for each day since records began date
			var currDate = cumulus.RecordsBeganDateTime;
			var dayfileStart = dayfile.DayfileRecs.Count > 0 ? dayfile.DayfileRecs[0].Date : DateTime.MaxValue;
			var endDate = SetStartTime(DateTime.Now.AddDays(-1).Date);

			LogMessage($"First dayfile record: {dayfileStart:d}");
			LogMessage($"Records Began Date  : {currDate:d}");
			Console.WriteLine($"First dayfile record: {dayfileStart:d}");
			Console.WriteLine($"Records Began Date  : {currDate:d}");
			Console.WriteLine();

			// Sanity check #1. Is the first date in the day file order than the records began date?
			if (dayfileStart < currDate)
			{
				LogMessage($"The first dayfile record ({dayfileStart:d}) is older than the records began date ({currDate:d}), using that date");
				Console.WriteLine($"The first dayfile record ({dayfileStart:d}) is older than the records began date ({currDate:d}), using that date");
				currDate = dayfileStart;
			}

			if (!GetUserConfirmation($"This will attempt to create/update your day file records from {currDate:D}. Continue? [Y/N]: "))
			{
				Console.WriteLine("Exiting...");
				Environment.Exit(1);
			}

			Console.WriteLine();

			// Sanity check #2
			if (currDate >= DateTime.Today)
			{
				LogMessage("Start date is today!???");
				LogConsole("Start date is today!???", ConsoleColor.Cyan);
				LogConsole("Press any key to exit", ConsoleColor.DarkYellow);
				Console.ReadKey(true);
				Console.WriteLine("Exiting...");

				Environment.Exit(1);
			}

			// convert to meteo date if required.
			currDate = SetStartTime(currDate);

			for (var i = 0; i < dayfile.DayfileRecs.Count; i++)
			{
				// check if the day record exists in the day file?
				if (dayfile.DayfileRecs[i].Date > currDate)
				{
					// Extract the Total Chill Hours from the last record we have to continue incrementing it
					// First check if total chill hours needs to be reset
					if (currDate.Month == cumulus.ChillHourSeasonStart && currDate.Day == 1)
					{
						TotalChillHours = 0;
					}
					else
					{
						// use whatever we have in the dayfile
						TotalChillHours = dayfile.DayfileRecs[i].ChillHours;

						// unless we don't have anything, then start at zero
						if (TotalChillHours == -9999)
							TotalChillHours = 0;
					}

					while (dayfile.DayfileRecs[i].Date > currDate)
					{
						// if not step through the monthly log file(s) to recreate it
						// 9am rollover means we may have to process two files

						LogMessage($"Date: {currDate:d} : Creating missing day entry ... ");
						Console.Write($"Date: {currDate:d} : Creating missing day entry ... ");

						var newRec = GetDayRecFromMonthly(currDate);
						if (newRec == null)
						{
							LogMessage($"Date: {currDate:d} : No monthly data was found, not creating a record");
							LogConsole("No monthly data was found, not creating a record", ConsoleColor.Yellow);
							RecsNoData++;
						}
						else
						{
							newRec = GetSolarDayRecFromMonthly(currDate, newRec);
							dayfile.DayfileRecs.Insert(i, newRec);
							LogConsole("done.", ConsoleColor.Green);
							RecsAdded++;
							i++;
						}


						// step forward a day
						currDate = IncrementMeteoDate(currDate);

						// check if total chill hours needs to be reset
						if (currDate.Month == cumulus.ChillHourSeasonStart && currDate.Day == 1)
						{
							TotalChillHours = 0;
						}

						// increment our index to allow for the newly inserted record
						if (i >= dayfile.DayfileRecs.Count)
						{
							break;
						}

					}
					// undo the last date increment in the while loop, it gets incremented in the main loop now
					currDate = currDate.AddDays(-1);
					i--;
				}
				else
				{
					// dayfile entry already exists, does it have the correct number of populated fields?
					if (dayfile.DayfileRecs[i].HasMissingData())
					{
						AddMissingData(i, currDate);
					}
					else
					{
						LogMessage($"Date: {currDate:d} : Entry is OK");
						Console.Write($"Date: {currDate:d} : ");
						LogConsole("Entry is OK", ConsoleColor.Green);
						RecsOK++;
					}
				}

				currDate = IncrementMeteoDate(currDate);

				// check if total chill hours needs to be reset
				if (currDate.Month == cumulus.ChillHourSeasonStart && currDate.Day == 1)
				{
					TotalChillHours = 0;
				}

				if (currDate >= DateTime.Today)
				{
					// We don't do the future!
					break;
				}
			}

			currDate = IncrementMeteoDate(dayfile.DayfileRecs[^1].Date);
			// We need the last total chill hours to increment if there are missing records at the end of the day file
			// check if total chill hours needs to be reset
			if (currDate.Month == cumulus.ChillHourSeasonStart && currDate.Day == 1)
			{
				TotalChillHours = 0;
			}
			else
			{
				TotalChillHours = dayfile.DayfileRecs[^1].ChillHours;
			}

			// that is the dayfile processed, but what if it had missing records at the end?
			while (currDate <= endDate)
			{
				LogMessage($"Date: {currDate:d} : Creating missing day entry ... ");
				Console.Write($"Date: {currDate:d} : Creating missing day entry ... ");

				var newRec = GetDayRecFromMonthly(currDate);
				if (newRec == null)
				{
					LogMessage($"Date: {currDate:d} : No monthly data was found, not creating a record");
					LogConsole("No monthly data was found, not creating a record", ConsoleColor.Yellow);
					RecsNoData++;
				}
				else
				{
					newRec = GetSolarDayRecFromMonthly(currDate, newRec);
					dayfile.DayfileRecs.Add(newRec);
					LogConsole("done.", ConsoleColor.Green);
					RecsAdded++;
				}
				currDate = IncrementMeteoDate(currDate);

				// check if total chill hours needs to be reset
				if (currDate.Month == cumulus.ChillHourSeasonStart && currDate.Day == 1)
				{
					TotalChillHours = 0;
				}
			}

			// create the new dayfile.txt with a different name
			LogMessage("Saving new dayfile.txt");
			Console.WriteLine();
			Console.WriteLine("Saving new dayfile.txt");

			dayfile.WriteDayFile();

			LogMessage("Created new dayfile.txt, the old is saved as dayfile.txt.sav");
			Console.WriteLine("Created new dayfile.txt, the original file has been saved as dayfile.txt.sav");

			LogMessage($"Number of records added  : {RecsAdded}");
			LogMessage($"Number of records updated: {RecsUpdated}");
			LogMessage($"Number of records No Data: {RecsNoData}");
			LogMessage($"Number of records were OK: {RecsOK}");

			Console.WriteLine();
			Console.WriteLine($"Number of records processed: {RecsAdded + RecsUpdated + RecsNoData + RecsOK}");
			Console.WriteLine($"  Were OK: {RecsOK}");
			Console.WriteLine($"  Added  : {RecsAdded}");
			Console.WriteLine($"  Updated: {RecsUpdated}");
			LogConsole(       $"  No Data: {RecsNoData}", ConsoleColor.Red, false);
			if (RecsNoData > 0)
			{
				LogConsole(" - please check the log file for the errors", ConsoleColor.Cyan);
			}
			else
			{
				Console.WriteLine();
			}

			LogMessage("Processing complete.");
			Console.WriteLine();
			Console.WriteLine();
			Console.WriteLine("Processing complete.");
			LogConsole("Press any key to exit", ConsoleColor.DarkYellow);
			Console.ReadKey(true);
		}

		public static void LogMessage(string message)
		{
			Trace.WriteLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff ") + message);
		}

		public static void LogConsole(string msg, ConsoleColor colour, bool newLine = true)
		{
			Console.ForegroundColor = colour;

			if (newLine)
			{
				Console.WriteLine(msg);
			}
			else
			{
				Console.Write(msg);
			}

			Console.ForegroundColor = defConsoleColour;
		}

		private static Dayfilerec GetDayRecFromMonthly(DateTime date)
		{
			var rec = new Dayfilerec()
			{
				ET = 0,
				SunShineHours = 0,
				HighSolar = 0,
				HighUv = 0,
				HighHourlyRain = 0,
				HeatingDegreeDays = 0,
				CoolingDegreeDays = 0,
				TotalRain = 0,
				WindRun = 0,
				ChillHours = 0
			};

			var inv = CultureInfo.InvariantCulture;

			var started = false;
			var finished = false;
			var recCount = 0;
			var idx = 0;

			var lastentrydate = DateTime.MinValue;
			int lastentryHour = -1;
			var lasttempvalue = 0.0;

			var startTime = date;
			var endTime = IncrementMeteoDate(date);
			var startTimeMinus1 = DecrementMeteoDate(date);

			// get the monthly log file name
			var fileName = GetLogFileName(startTimeMinus1);
			var fileDate = startTimeMinus1;

			var rain1hLog = new Queue<LastHourRainLog>();
			var rain24hLog = new Queue<LastHourRainLog>();

			var totalwinddirX = 0.0;
			var totalwinddirY = 0.0;
			var totalMins = 0.0;
			var totalTemp = 0.0;

			// what do we deem to be too large a jump in the rainfall counter to be true? use 20 mm or 0.8 inches
			var counterJumpTooBig = cumulus.Units.Rain == 0 ? 20 : 0.8;
			var totalRainfall = 0.0;
			var lastentryrain = 0.0;
			var lastentrycounter = 0.0;
			double rainThisHour;
			double rainLast24Hr;

			// clear the current weather data for this date
			CurrentWeatherData.Clear();

			rec.Date = date;

			// n-minute logfile. Fields are comma-separated:
			// 0  Date in the form dd/mm/yy (the slash may be replaced by other characters)
			// 1  Current time - hh:mm
			// 2  Current temperature
			// 3  Current humidity
			// 4  Current dewpoint
			// 5  Current wind speed
			// 6  Recent (10-minute) high gust
			// 7  Average wind bearing
			// 8  Current rainfall rate
			// 9  Total rainfall today so far
			// 10  Current sea level pressure
			// 11  Total rainfall counter as held by the station
			// 12  Inside temperature
			// 13  Inside humidity
			// 14  Current gust (i.e. 'Latest')
			// 15  Wind chill
			// 16  Heat Index
			// 17  UV Index
			// 18  Solar Radiation
			// 19  Evapotranspiration
			// 20  Annual Evapotranspiration
			// 21  Apparent temperature
			// 22  Current theoretical max solar radiation
			// 23  Hours of sunshine so far today
			// 24  Current wind bearing
			// 25  RG-11 rain total
			// 26  Rain since midnight
			// 27  Feels like
			// 28  Humidex


			while (!finished)
			{
				if (File.Exists(fileName))
				{
					// Have we determined the line endings for dayfile.txt yet?
					if (dayfile.LineEnding == string.Empty)
					{
						Utils.TryDetectNewLine(fileName, out dayfile.LineEnding);
					}


					try
					{
						if (CurrentLogName != fileName)
						{
							LogMessage($"LogFile: Loading log file - {fileName}");

							CurrentLogLines.Clear();
							CurrentLogLines.AddRange(File.ReadAllLines(fileName));
							CurrentLogName = fileName;
						}
						double valDbl;
						int valInt;
						CurrentLogLineNum = 0;

						while (CurrentLogLineNum < CurrentLogLines.Count)
						{
							// we use idx 0 & 1 together (date/time), set the idx to 1
							idx = 1;
							// process each record in the file
							// first a sanity check for an empty line!
							if (string.IsNullOrWhiteSpace(CurrentLogLines[CurrentLogLineNum]))
							{
								CurrentLogLineNum++;
								LogMessage($"LogFile: Error at line {CurrentLogLineNum}, an empty line was detected!");
								continue;
							}

							//var st = new List<string>(Regex.Split(line, CultureInfo.CurrentCulture.TextInfo.ListSeparator))
							// Regex is very expensive, let's assume the separator is always a single character
							var st = new List<string>(CurrentLogLines[CurrentLogLineNum++].Split(','));
							var entrydate = Utils.DdmmyyhhmmStrToDate(st[0], st[1]);

							if (entrydate < startTimeMinus1)
								continue;

							// are we within 24 hours of the start time?
							// if so initialise the 24 hour rain process
							if (entrydate >= startTimeMinus1 && entrydate >= startTime)
							{

								// logging format changed on with C1 v1.9.3 b1055 in Dec 2012
								// before that date the 00:00 log entry contained the rain total for the day before and the next log entry was reset to zero
								// after that build the total was reset to zero in the entry
								// messy!
								// no final rainfall entry after this date (approx). The best we can do is add in the increase in rain counter during this preiod
								var rain = double.Parse(st[9], inv);    // 9
								var raincounter = double.Parse(st[11], inv);  // 11

								// we need to initalise the rain counter on the first record
								if (rain1hLog.Count == 0)
								{
									lastentrycounter = raincounter;
								}


								if (entrydate == startTime)
								{

									if (rain == 0 && (raincounter - lastentrycounter > 0) && (raincounter - lastentrycounter < counterJumpTooBig))
									{
										rain = lastentryrain + (raincounter - lastentrycounter) * cumulus.CalibRainMult;
									}
									else if (rain == 0)
									{
										rain = lastentryrain;
									}
								}
								else if (entrydate == startTimeMinus1)
								{
									rain = 0;
								}

								AddLastHoursRainEntry(entrydate, raincounter, ref rain1hLog, ref rain24hLog);

								lastentryrain = rain;

								if (entrydate < startTime)
								{
									lastentrycounter = raincounter;
									lastentrydate = entrydate;

									continue;
								}
							}

							// same meto day, or first record of the next day
							// we want data from 00:00/09:00 to 00:00/09:00
							// but next day 00:00/09:00 values are only used for summation functions and rainfall in x hours

							if (entrydate >= startTime && entrydate <= endTime)
							{
								recCount++;
								var outsidetemp = double.Parse(st[++idx], inv);	// 2
								var hum = int.Parse(st[++idx]);					// 3
								var dewpoint = double.Parse(st[++idx], inv);	// 4
								var speed = double.Parse(st[++idx], inv);		// 5
								var gust = double.Parse(st[++idx], inv);		// 6
								var avgbearing = int.Parse(st[++idx], inv);		// 7
								var rainrate = double.Parse(st[++idx], inv);	// 8
								var raintoday = double.Parse(st[++idx], inv);	// 9
								var pressure = double.Parse(st[++idx], inv);	// 10
								var raincounter = double.Parse(st[++idx], inv);	// 11

								if (!started)
								{
									lasttempvalue = outsidetemp;
									lastentrydate = entrydate;
									totalRainfall = lastentryrain;
									lastentryHour = entrydate.Hour;
									started = true;
								}

								// Special case, the last record of the day is only used for averaging and summation purposes
								if (entrydate != endTime)
								{
									// current gust
									idx = 14;
									if (st.Count > idx && double.TryParse(st[idx], inv, out valDbl) && valDbl > rec.HighGust)
									{
										rec.HighGust = valDbl;
										rec.HighGustTime = entrydate;
										idx = 24;
										if (st.Count > idx && int.TryParse(st[idx], inv, out valInt))
										{
											rec.HighGustBearing = valInt;
										}
										else
										{
											rec.HighGustBearing = avgbearing;
										}
									}
									// low chill
									idx = 15;
									if (st.Count > idx && double.TryParse(st[idx], inv, out valDbl) && valDbl < rec.LowWindChill)
									{
										rec.LowWindChill = valDbl;
										rec.LowWindChillTime = entrydate;
									}
									// not logged, calculate it
									else
									{
										var wchill = Utils.TempCToUser(MeteoLib.WindChill(Utils.UserTempToC(outsidetemp), Utils.UserWindToKPH(speed)));
										if (wchill < rec.LowWindChill)
										{
											rec.LowWindChill = wchill;
											rec.LowWindChillTime = entrydate;
										}
									}
									// hi heat
									idx = 16;
									if (st.Count > idx && double.TryParse(st[idx], inv, out valDbl) && valDbl > rec.HighHeatIndex)
									{
										rec.HighHeatIndex = valDbl;
										rec.HighHeatIndexTime = entrydate;
									}
									// not logged, calculate it
									else
									{
										var heatIndex = Utils.TempCToUser(MeteoLib.HeatIndex(Utils.UserTempToC(outsidetemp), hum));
										if (heatIndex > rec.HighHeatIndex)
										{
											rec.HighHeatIndex = heatIndex;
											rec.HighHeatIndexTime = entrydate;
										}
									}
									// hi/low appt
									idx = 21;
									if (st.Count > idx && double.TryParse(st[idx], inv, out valDbl))
									{
										if (valDbl > rec.HighAppTemp)
										{
											rec.HighAppTemp = valDbl;
											rec.HighAppTempTime = entrydate;
										}
										if (valDbl < rec.LowAppTemp)
										{
											rec.LowAppTemp = valDbl;
											rec.LowAppTempTime = entrydate;
										}
									}
									// no logged apparent, calculate it
									else
									{
										var apparent = Utils.TempCToUser(MeteoLib.ApparentTemperature(Utils.UserTempToC(outsidetemp), Utils.UserWindToMS(speed), hum));
										if (apparent > rec.HighAppTemp)
										{
											rec.HighAppTemp = apparent;
											rec.HighAppTempTime = entrydate;
										}
										if (apparent < rec.LowAppTemp)
										{
											rec.LowAppTemp = apparent;
											rec.LowAppTempTime = entrydate;
										}
									}


									// hi/low feels like
									idx = 27;
									if (st.Count > idx && double.TryParse(st[idx], inv, out valDbl))
									{
										if (valDbl > rec.HighFeelsLike)
										{
											rec.HighFeelsLike = valDbl;
											rec.HighFeelsLikeTime = entrydate;
										}
										if (valDbl < rec.LowFeelsLike)
										{
											rec.LowFeelsLike = valDbl;
											rec.LowFeelsLikeTime = entrydate;
										}
									}
									// no logged feels like data available, calculate it
									else
									{
										var feels = Utils.TempCToUser(MeteoLib.FeelsLike(Utils.UserTempToC(outsidetemp), Utils.UserWindToKPH(speed), hum));
										if (feels > rec.HighFeelsLike)
										{
											rec.HighFeelsLike = feels;
											rec.HighFeelsLikeTime = entrydate;
										}
										if (feels < rec.LowFeelsLike)
										{
											rec.LowFeelsLike = feels;
											rec.LowFeelsLikeTime = entrydate;
										}
									}

									// hi humidex
									idx = 28;
									if (st.Count > idx && double.TryParse(st[idx], inv, out valDbl))
									{
										if (valDbl > rec.HighHumidex)
										{
											rec.HighHumidex = valDbl;
											rec.HighHumidexTime = entrydate;
										}
									}
									// no logged humidex available, calculate it
									else
									{
										var humidex = Utils.TempCToUser(MeteoLib.Humidex(Utils.UserTempToC(outsidetemp), hum));
										if (humidex > rec.HighHumidex)
										{
											rec.HighHumidex = humidex;
											rec.HighHumidexTime = entrydate;
										}
									}

									// hi temp
									if (outsidetemp > rec.HighTemp)
									{
										rec.HighTemp = outsidetemp;
										rec.HighTempTime = entrydate;
									}
									// lo temp
									if (outsidetemp < rec.LowTemp)
									{
										rec.LowTemp = outsidetemp;
										rec.LowTempTime = entrydate;
									}
									// hi dewpoint
									if (dewpoint > rec.HighDewPoint)
									{
										rec.HighDewPoint = dewpoint;
										rec.HighDewPointTime = entrydate;
									}
									// low dewpoint
									if (dewpoint < rec.LowDewPoint)
									{
										rec.LowDewPoint = dewpoint;
										rec.LowDewPointTime = entrydate;
									}
									// hi hum
									if (hum > rec.HighHumidity)
									{
										rec.HighHumidity = hum;
										rec.HighHumidityTime = entrydate;
									}
									// lo hum
									if (hum < rec.LowHumidity)
									{
										rec.LowHumidity = hum;
										rec.LowHumidityTime = entrydate;
									}
									// hi baro
									if (pressure > rec.HighPress)
									{
										rec.HighPress = pressure;
										rec.HighPressTime = entrydate;
									}
									// lo hum
									if (pressure < rec.LowPress)
									{
										rec.LowPress = pressure;
										rec.LowPressTime = entrydate;
									}
									// hi gust
									if (gust > rec.HighGust)
									{
										rec.HighGust = gust;
										rec.HighGustTime = entrydate;
										idx = 28;
										if (st.Count > idx && int.TryParse(st[idx], out valInt))
										{
											rec.HighGustBearing = valInt;
										}
										else // have to use the average bearing
										{
											rec.HighGustBearing = avgbearing;
										}
									}
									// hi wind
									if (speed > rec.HighAvgWind)
									{
										rec.HighAvgWind = speed;
										rec.HighAvgWindTime = entrydate;
									}
									// hi rain rate
									if (rainrate > rec.HighRainRate)
									{
										rec.HighRainRate = rainrate;
										rec.HighRainRateTime = entrydate;
									}
									// total rain - just take the last value - the user may have edited the value during the day
									rec.TotalRain = raintoday * cumulus.CalibRainMult;

									// add last hours rain - the first record of the day has already been added as the last record of the previous day
									if (entrydate > startTime)
									{
										AddLastHoursRainEntry(entrydate, raincounter, ref rain1hLog, ref rain24hLog);
									}

									// rainfall in last hour
									rainThisHour = Math.Round((rain1hLog.Last().Raincounter - rain1hLog.Peek().Raincounter) * cumulus.CalibRainMult, cumulus.Units.RainDPlaces);
									if (rainThisHour > rec.HighHourlyRain)
									{
										rec.HighHourlyRain = rainThisHour;
										rec.HighHourlyRainTime = entrydate;
									}

									// rainfall in last 24 hours
									rainLast24Hr = Math.Round((rain24hLog.Last().Raincounter - rain24hLog.Peek().Raincounter) * cumulus.CalibRainMult, cumulus.Units.RainDPlaces);
									if (rainLast24Hr > rec.HighRain24h)
									{
										rec.HighRain24h = rainLast24Hr;
										rec.HighRain24hTime = entrydate;
									}
									// tot up wind run
									rec.WindRun += entrydate.Subtract(lastentrydate).TotalHours * speed;

									// average temp values
									var intervalMins = entrydate.Subtract(lastentrydate).TotalMinutes;
									totalMins += intervalMins;
									totalTemp += intervalMins * (outsidetemp + lasttempvalue) / 2;

									// dominate wind direction values
									totalwinddirX += (speed * Math.Sin((avgbearing * (Math.PI / 180))));
									totalwinddirY += (speed * Math.Cos((avgbearing * (Math.PI / 180))));

									// heating/cooling degree days
									if (outsidetemp < cumulus.NOAAheatingthreshold)
									{
										if (rec.HeatingDegreeDays == -9999)
										{
											rec.HeatingDegreeDays = 0;
										}
										rec.HeatingDegreeDays += (((cumulus.NOAAheatingthreshold - outsidetemp) * intervalMins) / 1440);
									}
									else if (outsidetemp > cumulus.NOAAcoolingthreshold)
									{
										if (rec.CoolingDegreeDays == -9999)
										{
											rec.CoolingDegreeDays = 0;
										}
										rec.CoolingDegreeDays += (((outsidetemp - cumulus.NOAAcoolingthreshold) * intervalMins) / 1440);
									}

									// chill hours
									if (outsidetemp < cumulus.ChillHourThreshold)
									{
										TotalChillHours += intervalMins / 60.0;
									}
									rec.ChillHours = TotalChillHours;

									// data for ET
									if (st.Count >= 21)
									{
										idx = 18;
										int? solarRad = int.TryParse(st[idx], out var i) ? i : null;
										idx = 22;
										int? solarMax = int.TryParse(st[idx], out i) ? i : null;

										if (!CurrentWeatherData.TryAdd(entrydate, new WeatherData()
										{
											Temp = outsidetemp,
											Humidity = hum,
											WindSpeed = speed,
											Pressure = pressure,
											SolarRad = solarRad,
											SolarMax = solarMax
										}))
										{
											// normally this is a DST coming off error when 1 hours data is entered twice
											LogMessage($"Error adding ET weather data for {entrydate}, duplicate entry");
										}
									}

									if (lastentryHour < entrydate.Hour)
									{
										lastentryHour = entrydate.Hour;

										// get the averages for ET
										var etData = CurrentWeatherData.GetAverages(lastentrydate);
										if (etData != null)
										{
											rec.ET += Utils.RainMMToUser(
												MeteoLib.Evapotranspiration(
													Utils.UserTempToC(etData.Temp.Value),
													etData.Humidity.Value,
													etData.SolarRad.Value,
													etData.SolarMax.Value,
													Utils.UserWindToMS(etData.WindSpeed.Value),
													Utils.UserPressToKpa(etData.Pressure.Value)
													)
												);
										}
									}


									lastentryrain = raintoday;
									lastentrycounter = raincounter;
									lasttempvalue = outsidetemp;
									lastentrydate = entrydate;
									continue;
								}
								else // we are outside the time range of the current day
								{
									// These values need to include the last record for completeness

									// tot up wind run
									rec.WindRun += entrydate.Subtract(lastentrydate).TotalHours * speed;

									// average temp values
									var intervalMins = entrydate.Subtract(lastentrydate).TotalMinutes;
									totalMins += intervalMins;
									totalTemp += intervalMins * (outsidetemp + lasttempvalue) / 2;

									// dominant wind direction values
									totalwinddirX += (speed * Math.Sin((avgbearing * (Math.PI / 180))));
									totalwinddirY += (speed * Math.Cos((avgbearing * (Math.PI / 180))));

									// heating/cooling degree days
									if (outsidetemp < cumulus.NOAAheatingthreshold)
									{
										if (rec.HeatingDegreeDays == -9999)
										{
											rec.HeatingDegreeDays = 0;
										}
										rec.HeatingDegreeDays += (((cumulus.NOAAheatingthreshold - outsidetemp) * intervalMins) / 1440);
									}
									else if (outsidetemp > cumulus.NOAAcoolingthreshold)
									{
										if (rec.CoolingDegreeDays == -9999)
										{
											rec.CoolingDegreeDays = 0;
										}
										rec.CoolingDegreeDays += (((outsidetemp - cumulus.NOAAcoolingthreshold) * intervalMins) / 1440);
									}

									// chill hours
									if (outsidetemp < cumulus.ChillHourThreshold)
									{
										TotalChillHours += intervalMins / 60.0;
									}
									rec.ChillHours = TotalChillHours;


									// logging format changed on with C1 v1.9.3 b1055 in Dec 2012
									// before that date the 00:00 log entry contained the rain total for the day before and the next log entry was reset to zero
									// after that build the total was reset to zero in the 00:00 entry
									// messy!
									// no final rainfall entry after this date (approx). The best we can do is add in the increase in rain counter during this preiod
									//var rolloverRain = double.Parse(st[9]);          // 9 - rain so far today
									var rolloverRaincounter = double.Parse(st[11], inv);  // 11 - rain counter

									if (rolloverRaincounter > lastentrycounter)
									{
										rec.TotalRain += (rolloverRaincounter - lastentrycounter) * cumulus.CalibRainMult;
									}


									//if (rolloverRain > 0)
									//{
									//	raintoday = lastentryrain + rolloverRain;
									//}
									//if (rolloverRain == 0 && (raincounter - lastentrycounter > 0) && (raincounter - lastentrycounter < counterJumpTooBig))
									//{
									//	raintoday += (raincounter - lastentrycounter) * cumulus.CalibRainMult;
									//}

									// add last hours rain for this last record.
									AddLastHoursRainEntry(entrydate, rolloverRaincounter, ref rain1hLog, ref rain24hLog);

									// rainfall in last hour
									rainThisHour = Math.Round((rain1hLog.Last().Raincounter - rain1hLog.Peek().Raincounter) * cumulus.CalibRainMult, cumulus.Units.RainDPlaces);
									if (rainThisHour > rec.HighHourlyRain)
									{
										rec.HighHourlyRain = rainThisHour;
										rec.HighHourlyRainTime = entrydate;
									}

									rainLast24Hr = Math.Round((rain24hLog.Last().Raincounter - rain24hLog.Peek().Raincounter) * cumulus.CalibRainMult, cumulus.Units.RainDPlaces);
									if (rainLast24Hr > rec.HighRain24h)
									{
										rec.HighRain24h = rainLast24Hr;
										rec.HighRain24hTime = entrydate.AddMinutes(-1); // we want the high rate for the day to be at the end of the day we are closing
									}

									// total rain
									totalRainfall += rec.TotalRain;
									lastentrycounter = raincounter;

									// get the averages for ET
									var etData = CurrentWeatherData.GetAverages(lastentrydate);
									if (etData != null)
									{
										rec.ET += Utils.RainMMToUser(
											MeteoLib.Evapotranspiration(
												Utils.UserTempToC(etData.Temp.Value),
												etData.Humidity.Value,
												etData.SolarRad.Value,
												etData.SolarMax.Value,
												Utils.UserWindToMS(etData.WindSpeed.Value),
												Utils.UserPressToKpa(etData.Pressure.Value)
												)
											);
									}

									// flag we are done with this record
									finished = true;
								}
							}


							if (started && recCount >= 5) // need at least five records to create a day
							{
								// we were in the right day, now we aren't
								// calc average temp for the day, edge case we only have one record, in which case the totals will be zero, use hi or lo temp, they will be the same!
								rec.AvgTemp = totalMins > 0 ? totalTemp / totalMins : rec.HighTemp;

								// calc dominant wind direction for the day
								rec.DominantWindBearing = Utils.CalcAvgBearing(totalwinddirX, totalwinddirY);

								return rec;
							}
							else if (started && recCount <= 5)
							{
								// Oh dear, we have done the day and have less than five records
								return null;
							}
							else if (!started && entrydate > endTime)
							{
								// We didn't find any data
								return null;
							}

							lastentrydate = entrydate;
						} // end while
					}
					catch (Exception e)
					{
						LogMessage($"LogFile: Error at line {CurrentLogLineNum}, field {idx + 1} of {fileName} : {e.Message}");
						LogMessage("LogFile: Please edit the file to correct the error");
						LogMessage("LogFile: Line = " + CurrentLogLines[CurrentLogLineNum - 1]);

						LogConsole($"Error at line {CurrentLogLineNum}, field {idx + 1} of {fileName} : {e.Message}", ConsoleColor.Red);
						LogConsole("Please edit the file to correct the error", ConsoleColor.Red);

						Environment.Exit(1);
					}
				}
				else
				{
					LogMessage($"LogFile: Log file  not found - {fileName}");
					// have we run out of log files without finishing the current day?
					if (started && !finished)
					{
						// yes we have, so do the final end of day stuff now
						// calc average temp for the day, edge case we only have one record, in which case the totals will be zero, use hi or lo temp, they will be the same!
						rec.AvgTemp = totalMins > 0 ? totalTemp / totalMins : rec.HighTemp;

						// calc dominant wind direction for the day
						rec.DominantWindBearing = Utils.CalcAvgBearing(totalwinddirX, totalwinddirY);

						return rec;
					}
				}
				if (fileDate > date)
				{
					finished = true;
					LogMessage("LogFile: Finished processing all log files");
				}
				else
				{
					LogMessage($"LogFile: Finished processing log file - {fileName}");
					fileDate = fileDate.AddMonths(1);
					fileName = GetLogFileName(fileDate);
				}
			}

			if (started)
				return rec;
			else
				return null;
		}

		private static Dayfilerec GetSolarDayRecFromMonthly(DateTime date, Dayfilerec rec)
		{
			var started = false;
			var finished = false;
			var fileDate = date;
			var startTime = date;
			var endTime = IncrementMeteoDate(date);
			var solarStartTime = startTime;
			var solarEndTime = endTime;

			// total sunshine is a pain for meteo days starting at 09:00 because we need to total to midnight only
			// starting at 00:01 on the meteo day
			if (startTime.Hour != 0)
			{
				solarStartTime = startTime.Date;
				solarEndTime = solarStartTime.AddDays(1);
			}

			// get the monthly log file name
			var fileName = GetLogFileName(solarStartTime);

			// n-minute logfile. Fields are comma-separated:
			// 0  Date in the form dd/mm/yy (the slash may be replaced by a dash in some cases)
			// 1  Current time - hh:mm
			// 2  Current temperature
			// 3  Current humidity
			// 4  Current dewpoint
			// 5  Current wind speed
			// 6  Recent (10-minute) high gust
			// 7  Average wind bearing
			// 8  Current rainfall rate
			// 9  Total rainfall today so far
			// 10  Current sea level pressure
			// 11  Total rainfall counter as held by the station
			// 12  Inside temperature
			// 13  Inside humidity
			// 14  Current gust (i.e. 'Latest')
			// 15  Wind chill
			// 16  Heat Index
			// 17  UV Index
			// 18  Solar Radiation
			// 19  Evapotranspiration
			// 20  Annual Evapotranspiration
			// 21  Apparent temperature
			// 22  Current theoretical max solar radiation
			// 23  Hours of sunshine so far today
			// 24  Current wind bearing
			// 25  RG-11 rain total
			// 26  Rain since midnight
			// 27  Feels like
			// 28  Humidex

			while (!finished)
			{
				if (File.Exists(fileName))
				{
					// Have we determined the line endings for dayfile.txt yet?
					if (dayfile.LineEnding == string.Empty)
					{
						Utils.TryDetectNewLine(fileName, out dayfile.LineEnding);
					}

					try
					{
						if (CurrentSolarLogName != fileName)
						{
							LogMessage($"Solar: Loading log file - {fileName}");

							CurrentSolarLogLines.Clear();
							CurrentSolarLogLines.TrimExcess();
							CurrentSolarLogLines.AddRange(File.ReadAllLines(fileName));
							CurrentSolarLogName = fileName;
							CurrentSolarLogLineNum = 0;
							LogMessage($"Solar: List capacity: {CurrentSolarLogLines.Capacity}, count: {CurrentSolarLogLines.Count}");
						}
					}
					catch (Exception ex)
					{
						LogMessage($"Solar: Loading log file - {fileName}, msg: {ex.Message}");
						LogMessage($"Solar: List capacity: {CurrentSolarLogLines.Capacity}, count: {CurrentSolarLogLines.Count}");
						Environment.Exit(1);
					}

					try
					{
						while (CurrentSolarLogLineNum < CurrentSolarLogLines.Count)
						{
							// process each record in the file
							// first a sanity check for an empty line!
							if (string.IsNullOrWhiteSpace(CurrentSolarLogLines[CurrentSolarLogLineNum]))
							{
								CurrentSolarLogLineNum++;
								LogMessage($"Solar: Error at line {CurrentSolarLogLineNum}, an empty line was detected!");
								continue;
							}

							// Regex is very expensive, let's assume the separator is always a single character
							var ut = new List<string>(CurrentSolarLogLines[CurrentSolarLogLineNum].Split(','));

							if (ut.Count < 10)
							{
								LogMessage($"Solar: Error at line {CurrentSolarLogLineNum + 1}, Number of fields less tha 10!");
								LogMessage("Solar: Line = " + CurrentSolarLogLines[CurrentSolarLogLineNum]);
								CurrentSolarLogLineNum++;
								continue;
							}

							try
							{
								var entrydate = Utils.DdmmyyhhmmStrToDate(ut[0], ut[1]);

								// Solar for 9am days is 00:00 the previous day to midnight the current day!
								if (entrydate > solarStartTime && entrydate <= solarEndTime)
								{
									// we are just getting the solar values to midnight
									ExtractSolarData(ut, ref rec, entrydate);
									started = true;
								}
								else if (started)
								{
									if (CurrentSolarLogLineNum > 0)
									{
										CurrentSolarLogLineNum--;
									}

									return rec;
								}
							}
							catch (IndexOutOfRangeException ex)
							{
								LogMessage($"Solar: Index Error at line {CurrentSolarLogLineNum + 1} of {fileName}");
								LogMessage("Solar: Line = " + CurrentSolarLogLines[CurrentSolarLogLineNum]);
								LogMessage($"Solar: Line List count = {ut.Count}, content = {string.Join(" ", ut.ToArray())}");
								LogMessage(ex.ToString());

								LogConsole($"Error at line {CurrentSolarLogLineNum + 1} of {fileName} : {ex.Message}", ConsoleColor.Red);
								LogConsole("Please edit the file to correct the error", ConsoleColor.Red);

								Environment.Exit(1);
							}
							catch (Exception ex)
							{
								LogMessage($"Solar: {ex.Message} Error at line {CurrentSolarLogLineNum + 1} of {fileName}");
								LogMessage("Solar: Line = " + CurrentSolarLogLines[CurrentSolarLogLineNum]);
								LogMessage(ex.ToString());

								LogConsole($"Error at line {CurrentSolarLogLineNum + 1} of {fileName} : {ex.Message}", ConsoleColor.Red);
								LogConsole("Please edit the file to correct the error", ConsoleColor.Red);

								Environment.Exit(1);
							}

							CurrentSolarLogLineNum++;
						} // end while
					}
					catch (Exception e)
					{
						LogMessage($"Solar: Error at line {CurrentSolarLogLineNum + 1} of {fileName}");
						LogMessage("Solar: Line = " + CurrentSolarLogLines[CurrentSolarLogLineNum]);
						LogMessage("Solar: Please edit the file to correct the error");
						LogMessage(e.ToString());

						LogConsole($"Error at line {CurrentSolarLogLineNum + 1} of {fileName} : {e.Message}", ConsoleColor.Red);
						LogConsole("Please edit the file to correct the error", ConsoleColor.Red);

						Environment.Exit(1);
					}
				}
				else
				{
					LogMessage($"Solar: Log file  not found - {fileName}");

					return rec;
				}
				if (fileDate > date)
				{
					finished = true;
					LogMessage("Solar: Finished processing all log files");
				}
				else
				{
					LogMessage($"Solar: Finished processing log file - {fileName}");
					fileDate = fileDate.AddMonths(1);
					fileName = GetLogFileName(fileDate);
				}
			}

			return rec;
		}


		private static void AddMissingData(int idx, DateTime metDate)
		{
			LogMessage($"Date: { metDate:d} : Adding missing data");
			Console.Write($"Date: {metDate:d} : Adding missing data ... ");
			// Extract all the data from the log file
			var newRec = GetDayRecFromMonthly(metDate);

			if (newRec == null)
			{
				RecsNoData++;
				LogMessage($"{metDate:d} : No monthly data was found, not updating this record");
				LogConsole("No monthly data was found, not updating this record", ConsoleColor.Yellow);
				return;
			}

			newRec = GetSolarDayRecFromMonthly(metDate, newRec);

			// update the existing record - only update missing values rather than replace
			// everything in-case it has been previously edited to remove a spike etc.
			if (dayfile.DayfileRecs[idx].HighGust == -9999)
			{
				dayfile.DayfileRecs[idx].HighGust = newRec.HighGust;
				dayfile.DayfileRecs[idx].HighGustBearing = newRec.HighGustBearing;
				dayfile.DayfileRecs[idx].HighGustTime = newRec.HighGustTime;
			}
			if (dayfile.DayfileRecs[idx].LowTemp == 9999)
			{
				dayfile.DayfileRecs[idx].LowTemp = newRec.LowTemp;
				dayfile.DayfileRecs[idx].LowTempTime = newRec.LowTempTime;
			}
			if (dayfile.DayfileRecs[idx].HighTemp == -9999)
			{
				dayfile.DayfileRecs[idx].HighTemp = newRec.HighTemp;
				dayfile.DayfileRecs[idx].HighTempTime = newRec.HighTempTime;
			}
			if (dayfile.DayfileRecs[idx].LowPress == 9999)
			{
				dayfile.DayfileRecs[idx].LowPress = newRec.LowPress;
				dayfile.DayfileRecs[idx].LowPressTime = newRec.LowPressTime;
			}
			if (dayfile.DayfileRecs[idx].HighPress == -9999)
			{
				dayfile.DayfileRecs[idx].HighPress = newRec.HighPress;
				dayfile.DayfileRecs[idx].HighPressTime = newRec.HighPressTime;
			}
			if (dayfile.DayfileRecs[idx].HighRainRate == -9999)
			{
				dayfile.DayfileRecs[idx].HighRainRate = newRec.HighRainRate;
				dayfile.DayfileRecs[idx].HighRainRateTime = newRec.HighRainRateTime;
			}
			if (dayfile.DayfileRecs[idx].TotalRain == -9999)
			{
				dayfile.DayfileRecs[idx].TotalRain = newRec.TotalRain;
			}
			if (dayfile.DayfileRecs[idx].AvgTemp == -9999)
			{
				dayfile.DayfileRecs[idx].AvgTemp = newRec.AvgTemp;
			}
			if (dayfile.DayfileRecs[idx].WindRun == -9999)
			{
				dayfile.DayfileRecs[idx].WindRun = newRec.WindRun;
			}
			if (dayfile.DayfileRecs[idx].HighAvgWind == -9999)
			{
				dayfile.DayfileRecs[idx].HighAvgWind = newRec.HighAvgWind;
				dayfile.DayfileRecs[idx].HighAvgWindTime = newRec.HighAvgWindTime;
			}
			if (dayfile.DayfileRecs[idx].LowHumidity == 9999)
			{
				dayfile.DayfileRecs[idx].LowHumidity = newRec.LowHumidity;
				dayfile.DayfileRecs[idx].LowHumidityTime = newRec.LowHumidityTime;
			}
			if (dayfile.DayfileRecs[idx].HighHumidity == -9999)
			{
				dayfile.DayfileRecs[idx].HighHumidity = newRec.HighHumidity;
				dayfile.DayfileRecs[idx].HighHumidityTime = newRec.HighHumidityTime;
			}
			if (dayfile.DayfileRecs[idx].ET == -9999)
			{
				dayfile.DayfileRecs[idx].ET = newRec.ET;
			}
			if (dayfile.DayfileRecs[idx].SunShineHours == -9999)
			{
				dayfile.DayfileRecs[idx].SunShineHours = newRec.SunShineHours;
			}
			if (dayfile.DayfileRecs[idx].HighHeatIndex == -9999)
			{
				dayfile.DayfileRecs[idx].HighHeatIndex = newRec.HighHeatIndex;
				dayfile.DayfileRecs[idx].HighHeatIndexTime = newRec.HighHeatIndexTime;
			}
			if (dayfile.DayfileRecs[idx].HighAppTemp == -9999)
			{
				dayfile.DayfileRecs[idx].HighAppTemp = newRec.HighAppTemp;
				dayfile.DayfileRecs[idx].HighAppTempTime = newRec.HighAppTempTime;
			}
			if (dayfile.DayfileRecs[idx].LowAppTemp == 9999)
			{
				dayfile.DayfileRecs[idx].LowAppTemp = newRec.LowAppTemp;
				dayfile.DayfileRecs[idx].LowAppTempTime = newRec.LowAppTempTime;
			}
			if (dayfile.DayfileRecs[idx].HighHourlyRain == -9999)
			{
				dayfile.DayfileRecs[idx].HighHourlyRain = newRec.HighHourlyRain;
				dayfile.DayfileRecs[idx].HighHourlyRainTime = newRec.HighHourlyRainTime;
			}
			if (dayfile.DayfileRecs[idx].LowWindChill == 9999)
			{
				dayfile.DayfileRecs[idx].LowWindChill = newRec.LowWindChill;
				dayfile.DayfileRecs[idx].LowWindChillTime = newRec.LowWindChillTime;
			}
			if (dayfile.DayfileRecs[idx].HighDewPoint == -9999)
			{
				dayfile.DayfileRecs[idx].HighDewPoint = newRec.HighDewPoint;
				dayfile.DayfileRecs[idx].HighDewPointTime = newRec.HighDewPointTime;
			}
			if (dayfile.DayfileRecs[idx].LowDewPoint == 9999)
			{
				dayfile.DayfileRecs[idx].LowDewPoint = newRec.LowDewPoint;
				dayfile.DayfileRecs[idx].LowDewPointTime = newRec.LowDewPointTime;
			}
			if (dayfile.DayfileRecs[idx].DominantWindBearing == 9999)
			{
				dayfile.DayfileRecs[idx].DominantWindBearing = newRec.DominantWindBearing;
			}
			if (dayfile.DayfileRecs[idx].HeatingDegreeDays == -9999)
			{
				dayfile.DayfileRecs[idx].HeatingDegreeDays = newRec.HeatingDegreeDays;
			}
			if (dayfile.DayfileRecs[idx].CoolingDegreeDays == -9999)
			{
				dayfile.DayfileRecs[idx].CoolingDegreeDays = newRec.CoolingDegreeDays;
			}
			if (dayfile.DayfileRecs[idx].HighSolar == -9999)
			{
				dayfile.DayfileRecs[idx].HighSolar = newRec.HighSolar;
			}
			if (dayfile.DayfileRecs[idx].HighUv == -9999)
			{
				dayfile.DayfileRecs[idx].HighUv = newRec.HighUv;
			}
			if (dayfile.DayfileRecs[idx].HighHumidex == -9999)
			{
				dayfile.DayfileRecs[idx].HighHumidex = newRec.HighHumidex;
				dayfile.DayfileRecs[idx].HighHumidexTime = newRec.HighHumidexTime;
			}
			if (dayfile.DayfileRecs[idx].HighFeelsLike == -9999)
			{
				dayfile.DayfileRecs[idx].HighFeelsLike = newRec.HighFeelsLike;
				dayfile.DayfileRecs[idx].HighFeelsLikeTime = newRec.HighFeelsLikeTime;
			}
			if (dayfile.DayfileRecs[idx].LowFeelsLike == 9999)
			{
				dayfile.DayfileRecs[idx].LowFeelsLike = newRec.LowFeelsLike;
				dayfile.DayfileRecs[idx].LowFeelsLikeTime = newRec.LowFeelsLikeTime;
			}
			if (dayfile.DayfileRecs[idx].ChillHours == -9999)
			{
				dayfile.DayfileRecs[idx].ChillHours = newRec.ChillHours;
			}
			if (dayfile.DayfileRecs[idx].HighRain24h == -9999)
			{
				dayfile.DayfileRecs[idx].HighRain24h = newRec.HighRain24h;
				dayfile.DayfileRecs[idx].HighRain24hTime = newRec.HighRain24hTime;
			}
			LogConsole("done.", ConsoleColor.Green);
			RecsUpdated++;
		}

		private static string GetLogFileName(DateTime thedate)
		{
			// First determine the date for the log file.
			// If we're using 9am roll-over, the date should be 9 hours (10 in summer)
			// before 'Now'
			var logfiledate = thedate.AddHours(cumulus.GetHourInc(thedate));


			var datestring = logfiledate.ToString("yyyyMM");

			return location + "data" + Path.DirectorySeparatorChar + datestring + "log.txt";

		}

		private static void ExtractSolarData(List<string> st, ref Dayfilerec rec, DateTime entrydate)
		{
			var inv = CultureInfo.InvariantCulture;

			double valDbl;
			int valInt;
			int idx = 0;
			try
			{
				// hours of sunshine
				idx = 23;
				if (st.Count > idx && double.TryParse(st[idx], inv, out valDbl) && valDbl > rec.SunShineHours)
				{
					rec.SunShineHours = valDbl;
				}
				// hi UV-I
				idx = 17;
				if (st.Count > idx && double.TryParse(st[idx], inv, out valDbl) && valDbl > rec.HighUv)
				{
					rec.HighUv = valDbl;
					rec.HighUvTime = entrydate;
				}
				// hi solar
				idx = 18;
				if (st.Count > idx && int.TryParse(st[idx], inv, out valInt) && valInt > rec.HighSolar)
				{
					rec.HighSolar = valInt;
					rec.HighSolarTime = entrydate;
				}
				// ET
				idx = 19;
				if (st.Count > idx && double.TryParse(st[idx], inv, out valDbl) && valDbl > rec.ET)
				{
					rec.ET = valDbl;
				}
			}
			catch (Exception ex)
			{
				LogMessage($"Solar: Error at field {idx}: {ex.Message}");
				throw;
			}
		}

		private static DateTime SetStartTime(DateTime thedate)
		{
			DateTime retDate;

			if (cumulus.RolloverHour == 0)
			{
				retDate = thedate;
			}
			else
			{
				TimeZoneInfo tz = TimeZoneInfo.Local;

				// Date without time
				DateTime rawDate = new DateTime(thedate.Year, thedate.Month, thedate.Day, 0, 0, 0, DateTimeKind.Local);

				if (cumulus.Use10amInSummer && tz.IsDaylightSavingTime(thedate))
				{
					// Locale is currently on Daylight (summer) time
					retDate = rawDate.AddHours(10);
				}
				else
				{
					// Locale is currently on Standard time or unknown
					retDate = rawDate.AddHours(9);
				}
			}

			return retDate;
		}

		private static DateTime IncrementMeteoDate(DateTime thedate)
		{
			return SetStartTime(thedate.AddDays(1));
		}

		private static DateTime DecrementMeteoDate(DateTime thedate)
		{
			return SetStartTime(thedate.AddDays(-1));
		}

		private static bool GetUserConfirmation(string msg)
		{
			do
			{
				while (Console.KeyAvailable)
					Console.ReadKey();

				Console.Write(msg);
				var resp = Console.ReadKey().Key;
				Console.WriteLine();

				if (resp == ConsoleKey.Y) return true;
				if (resp == ConsoleKey.N) return false;
			} while (true);

		}

		private static void AddLastHoursRainEntry(DateTime ts, double rain, ref Queue<LastHourRainLog> hourQueue, ref Queue<LastHourRainLog> h24Queue)
		{
			var lastrain = new LastHourRainLog(ts, rain);

			hourQueue.Enqueue(lastrain);

			var hoursago = ts.AddHours(-1);

			while ((hourQueue.Count > 0) && (hourQueue.Peek().Timestamp < hoursago))
			{
				// the oldest entry is older than 1 hour ago, delete it
				hourQueue.Dequeue();
			}

			h24Queue.Enqueue(lastrain);

			hoursago = ts.AddHours(-24);

			while ((h24Queue.Count > 0) && (h24Queue.Peek().Timestamp < hoursago))
			{
				// the oldest entry is older than 24 hours ago, delete it
				h24Queue.Dequeue();
			}
		}
	}


	class LastHourRainLog(DateTime ts, double rain)
	{
		public readonly DateTime Timestamp = ts;
		public readonly double Raincounter = rain;
	}
}
