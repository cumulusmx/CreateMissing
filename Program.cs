using CumulusMX;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace CreateMissing
{
	class Program
	{
		public static Cumulus cumulus;
		private static DayFile dayfile;
		private static List<string> CurrentLogLines = new List<string>();
		private static string CurrentLogName;
		private static int CurrentLogLineNum = 0;

		private static int RecsAdded = 0;
		private static int RecsUpdated = 0;
		private static int RecsNoData = 0;
		private static int RecsOK = 0;

		static void Main()
		{

			TextWriterTraceListener myTextListener = new TextWriterTraceListener($"MXdiags{Path.DirectorySeparatorChar}CreateMissing-{DateTime.Now:yyyyMMdd-HHmmss}.txt", "CMlog");
			Trace.Listeners.Add(myTextListener);
			Trace.AutoFlush = true;

			var fullVer = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
			var version = $"{fullVer.Major}.{fullVer.Minor}.{fullVer.Build}";
			LogMessage("CreateMissing v." + version);
			Console.WriteLine("CreateMissing v." + version);

			LogMessage("Processing started");
			Console.WriteLine($"\nProcessing started: {DateTime.Now:U}\n");

			cumulus = new Cumulus();

			// load existing day file
			dayfile = new DayFile();

			// for each day since records began date
			var currDate = DateTime.Parse(cumulus.RecordsBeganDate);
			var dayfileStart = dayfile.DayfileRecs.Count > 0 ? dayfile.DayfileRecs[0].Date : DateTime.MaxValue;

			LogMessage($"First dayfile record: {dayfileStart:d}");
			LogMessage($"Records Began Date  : {currDate:d}");
			Console.WriteLine($"First dayfile record: {dayfileStart:d}");
			Console.WriteLine($"Records Began Date  : {currDate:d}\n");

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
				Console.WriteLine("Start date is today!???");
				Console.WriteLine("Press any key to exit");
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
							Console.WriteLine($"\nDate: {currDate:d} : No monthly data was found, not creating a record");
							RecsNoData++;
						}
						else
						{
							dayfile.DayfileRecs.Insert(i, newRec);
							Console.WriteLine(" done.");
							RecsAdded++;
							i++;
						}


						// step forward a day
						currDate = IncrementMeteoDate(currDate);
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
						Console.WriteLine($"Date: {currDate:d} : Entry is OK");
						RecsOK++;
					}
				}

				currDate = IncrementMeteoDate(currDate);

				if (currDate >= DateTime.Today)
				{
					// We don't do the future!
					break;
				}
			}

			// create the new dayfile.txt with a different name
			LogMessage("Saving new dayfile.txt");
			Console.WriteLine("\nSaving new dayfile.txt");

			dayfile.WriteDayFile();

			LogMessage("Created new dayfile.txt, the old is saved as dayfile.txt.sav");
			Console.WriteLine("Created new dayfile.txt, the original file has been saved as dayfile.txt.sav");

			LogMessage($"Number of records added  : {RecsAdded}");
			LogMessage($"Number of records updated: {RecsUpdated}");
			LogMessage($"Number of records No Data: {RecsNoData}");
			LogMessage($"Number of records that were OK: {RecsOK}");

			Console.WriteLine($"\nNumber of records processed: {RecsAdded + RecsUpdated + RecsNoData + RecsOK}");
			Console.WriteLine($"  Added  : {RecsAdded}");
			Console.WriteLine($"  Updated: {RecsUpdated}");
			Console.Write($"  No Data: {RecsNoData}");
			if (RecsNoData > 0)
			{
				Console.WriteLine(" - please check the log file for the errors");
			}
			else
			{
				Console.WriteLine();
			}
			Console.WriteLine($"  Were OK: {RecsOK}");

			LogMessage("\nProcessing complete.");
			Console.WriteLine("\n\nProcessing complete.");
			Console.WriteLine("Press any key to exit");
			Console.ReadKey(true);
		}

		public static void LogMessage(string message)
		{
			Trace.WriteLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff ") + message);
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
				WindRun = 0
			};
			var started = false;
			var finished = false;
			var fileDate = date;
			var recCount = 0;
			var idx = 0;

			var solarCurrRec = 0;

			var entrydate = DateTime.MinValue;
			var lastentrydate = DateTime.MinValue;
			var lasttempvalue = 0.0;

			var startTime = date;
			var endTime = IncrementMeteoDate(date);
			var solarStartTime = startTime;
			var solarEndTime = endTime;

			// total sunshine is a pain for meteo days starting at 09:00 becuase we need to total to midnight only
			// starting at 00:01 on the meteo day
			if (startTime.Hour != 0)
			{
				solarStartTime = startTime.Date;
				solarEndTime = solarStartTime.AddDays(1);
			}

			// get the monthly log file name
			var fileName = GetLogFileName(date);

			List<LastHourData> LastHourDataList = new List<LastHourData>();

			var totalwinddirX = 0.0;
			var totalwinddirY = 0.0;
			var totalMins = 0.0;
			var totalTemp = 0.0;

			rec.Date = date;

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
					try
					{
						if (CurrentLogName != fileName)
						{
							LogMessage($"Loading log file - {fileName}");

							CurrentLogLines.Clear();
							CurrentLogLines.AddRange(File.ReadAllLines(fileName));
							CurrentLogName = fileName;
							CurrentLogLineNum = 0;
						}
						double valDbl;
						int valInt;


						while (CurrentLogLineNum < CurrentLogLines.Count)
						{
							// we use idx 0 & 1 together (date/time), set the idx to 1
							idx = 1;
							// process each record in the file
							//var st = new List<string>(Regex.Split(line, CultureInfo.CurrentCulture.TextInfo.ListSeparator));
							// Regex is very expensive, let's assume the separator is always a single character
							var st = new List<string>(CurrentLogLines[CurrentLogLineNum].Split((CultureInfo.CurrentCulture.TextInfo.ListSeparator)[0]));
							entrydate = dayfile.DdmmyyhhmmStrToDate(st[0], st[1]);

							// same meto day, or first record of the next day
							// we want data from 00:00/09:00 to 00:00/09:00
							// but next day 00:00/09:00 values are only used for summation functions
							// Solar for 9am days is 00:00 the previous day to midnight the current day!
							if (entrydate >= solarStartTime && entrydate <= solarEndTime)
							{
								// we are just getting the solar values to midnight
								ExtractSolarData(st, ref rec, entrydate);
								solarCurrRec = CurrentLogLineNum;
							}

							if (entrydate >= startTime && entrydate <= endTime)
							{
								recCount++;
								var outsidetemp = double.Parse(st[++idx]);	// 2
								var hum = int.Parse(st[++idx]);				// 3
								var dewpoint = double.Parse(st[++idx]);		// 4
								var speed = double.Parse(st[++idx]);		// 5
								var gust = double.Parse(st[++idx]);			// 6
								var avgbearing = int.Parse(st[++idx]);		// 7
								var rainrate = double.Parse(st[++idx]);		// 8
								var raintoday = double.Parse(st[++idx]);	// 9
								var pressure = double.Parse(st[++idx]);		// 10
								var raincounter = double.Parse(st[++idx]);	// 11

								if (!started)
								{
									lasttempvalue = outsidetemp;
									lastentrydate = entrydate;
									started = true;
								}

								// Special case, the last record of the day is only used for averaging and summation purposes
								if (entrydate != endTime)
								{

									// current gust
									idx = 14;
									if (st.Count > idx && double.TryParse(st[idx], out valDbl) && valDbl > rec.HighGust)
									{
										rec.HighGust = valDbl;
										rec.HighGustTime = entrydate;
										idx = 24;
										if (st.Count > idx && int.TryParse(st[idx], out valInt))
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
									if (st.Count > idx && double.TryParse(st[idx], out valDbl) && valDbl < rec.LowWindChill)
									{
										rec.LowWindChill = valDbl;
										rec.LowWindChillTime = entrydate;
									}
									// not logged, calculate it
									else
									{
										var wchill = MeteoLib.WindChill(Utils.ConvertUserTempToC(outsidetemp), Utils.ConvertUserWindToKPH(speed));
										if (wchill < rec.LowWindChill)
										{
											rec.LowWindChill = wchill;
											rec.LowWindChillTime = entrydate;
										}
									}
									// hi heat
									idx = 16;
									if (st.Count > idx && double.TryParse(st[idx], out valDbl) && valDbl > rec.HighHeatIndex)
									{
										rec.HighHeatIndex = valDbl;
										rec.HighHeatIndexTime = entrydate;
									}
									// not logged, calculate it
									else
									{
										var heatIndex = MeteoLib.HeatIndex(Utils.ConvertUserTempToC(outsidetemp), hum);
										if (heatIndex > rec.HighHeatIndex)
										{
											rec.HighHeatIndex = heatIndex;
											rec.HighHeatIndexTime = entrydate;
										}
									}
									// hi/low appt
									idx = 21;
									if (st.Count > idx && double.TryParse(st[idx], out valDbl))
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
										var apparent = MeteoLib.ApparentTemperature(Utils.ConvertUserTempToC(outsidetemp), Utils.ConvertUserWindToMS(speed), hum);
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
									if (st.Count > idx && double.TryParse(st[idx], out valDbl))
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
										var feels = MeteoLib.FeelsLike(Utils.ConvertUserTempToC(outsidetemp), Utils.ConvertUserWindToKPH(speed), hum);
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
									if (st.Count > idx && double.TryParse(st[idx], out valDbl))
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
										var humidex = MeteoLib.Humidex(Utils.ConvertUserTempToC(outsidetemp), hum);
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
									rec.TotalRain = raintoday;

									// add last hour rain
									if (entrydate != startTime && raintoday > 0)
									{
										LastHourDataList.Add(new LastHourData(entrydate, raintoday));
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

									lasttempvalue = outsidetemp;
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

									// flag we are done with this record
									finished = true;
								}

								// rainfall in last hour
								if (LastHourDataList.Count > 0)
								{
									var onehourago = entrydate.AddHours(-1);

									// there are entries to consider
									while (LastHourDataList.Count > 0 && LastHourDataList[0].timestamp < onehourago)
									{
										// the oldest entry is older than 1 hour ago, delete it
										LastHourDataList.RemoveAt(0);
									}

									if (LastHourDataList.Count > 0)
									{
										var firstval = LastHourDataList[0].rainfall;
										var lastval = LastHourDataList[LastHourDataList.Count - 1].rainfall;
										var rainLastHr = lastval - firstval;
										if (rainLastHr > rec.HighHourlyRain)
										{
											rec.HighHourlyRain = rainLastHr;
											rec.HighHourlyRainTime = entrydate;
										}
									}
								}
							}
							else if (started && recCount >= 5) // need at least five records to create a day
							{
								// we were in the right day, now we aren't
								// calc average temp for the day, edge case we only have one record, in which case the totals will be zero, use hi or lo temp, they will be the same!
								rec.AvgTemp = totalMins > 0 ? totalTemp / totalMins : rec.HighTemp;

								// calc dominant wind direction for the day
								rec.DominantWindBearing = Utils.CalcAvgBearing(totalwinddirX, totalwinddirY);

								lastentrydate = entrydate;

								if (solarStartTime != startTime)
								{
									CurrentLogLineNum = solarCurrRec;
								}
								else if (CurrentLogLineNum > 0)
								{
									CurrentLogLineNum--;
								}

								return rec;
							}
							else if (started && recCount <= 5)
							{
								// Oh dear, we have done the day and have less than five records
								if (solarStartTime != startTime)
								{
									CurrentLogLineNum = solarCurrRec;
								}
								else if (CurrentLogLineNum > 0)
								{
									CurrentLogLineNum--;
								}

								return null;
							}
							else if (!started && entrydate > endTime)
							{
								// We didn't find any data
								CurrentLogLineNum = 0;
								return null;
							}

							CurrentLogLineNum++;
							lastentrydate = entrydate;
						} // end while
					}
					catch (Exception e)
					{
						LogMessage($"Error at line {CurrentLogLineNum + 1}, field {idx + 1} of {fileName} : {e.Message}");
						LogMessage("Please edit the file to correct the error");
						Console.WriteLine($"Error at line {CurrentLogLineNum + 1}, field {idx + 1} of {fileName} : {e.Message}");
						Console.WriteLine("Please edit the file to correct the error");

						Environment.Exit(1);
					}
				}
				else
				{
					LogMessage($"Log file  not found - {fileName}");
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

					return null;
				}
				if (fileDate > date)
				{
					finished = true;
					LogMessage("Finished processing the log files");
				}
				else
				{
					LogMessage($"Finished processing log file - {fileName}");
					fileDate = fileDate.AddMonths(1);
					fileName = GetLogFileName(fileDate);
				}
			}

			if (started)
				return rec;
			else
				return null;
		}

		private static void AddMissingData(int idx, DateTime metDate)
		{
			LogMessage($"{metDate:d} : Adding missing data");
			Console.Write($"{metDate:d} : Adding missing data ... ");
			// Extract all the data from the log file
			var newRec = GetDayRecFromMonthly(metDate);

			if (newRec == null)
			{
				RecsNoData++;
				LogMessage($"{metDate:d} : No monthly data was found, not updating this record");
				Console.WriteLine($"\n{metDate:d} : No monthly data was found, not updating this record");
				return;
			}

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
			Console.WriteLine("done.");
			RecsUpdated++;
		}

		private static string GetLogFileName(DateTime thedate)
		{
			var datestring = thedate.ToString("MMMyy").Replace(".", "");

			return "data" + Path.DirectorySeparatorChar + datestring + "log.txt";
		}

		private static void ExtractSolarData(List<string> st, ref Dayfilerec rec, DateTime entrydate)
		{
			double valDbl;
			int valInt;
			// hours of sunshine
			if (st.Count > 23 && double.TryParse(st[23], out valDbl) && valDbl > rec.SunShineHours)
			{
				rec.SunShineHours = valDbl;
			}
			// hi UV-I
			if (st.Count > 17 && double.TryParse(st[17], out valDbl) && valDbl > rec.HighUv)
			{
				rec.HighUv = valDbl;
				rec.HighUvTime = entrydate;
			}
			// hi solar
			if (st.Count > 18 && int.TryParse(st[18], out valInt) && valInt > rec.HighSolar)
			{
				rec.HighSolar = valInt;
				rec.HighSolarTime = entrydate;
			}
			// ET
			if (st.Count > 19 && double.TryParse(st[19], out valDbl) && valDbl > rec.ET)
			{
				rec.ET = valDbl;
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
				DateTime rawDate = new DateTime(thedate.Year, thedate.Month, thedate.Day);

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
	}

	class LastHourData
	{
		public DateTime timestamp;
		public double rainfall;

		public LastHourData(DateTime ts, double rain)
		{
			timestamp = ts;
			rainfall = rain;
		}
	}

}
