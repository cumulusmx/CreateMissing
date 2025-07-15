﻿using System;

namespace CumulusMX
{
	internal class MeteoLib
	{

		/// <summary>
		/// Calculates the Wind Chill in Celcius
		/// </summary>
		/// <remarks>
		/// JAG/TI - 2003
		/// </remarks>
		/// <param name="tempC">Temp in C</param>
		/// <param name="windSpeedKph">Average wind speed in km/h</param>
		/// <returns>Wind Chill in Celcius</returns>
		public static double WindChill(double tempC, double windSpeedKph)
		{
			// see American Meteorological Society Journal
			// see http://www.msc.ec.gc.ca/education/windchill/science_equations_e.cfm
			// see http://www.weather.gov/os/windchill/index.shtml

			if ((tempC >= 10.0) || (windSpeedKph <= 4.8))
				return tempC;

			double windPow = Math.Pow(windSpeedKph, 0.16);

			double wc = 13.12 + (0.6215*tempC) - (11.37*windPow) + (0.3965*tempC*windPow);

			if (wc > tempC) return tempC;

			return wc;
		}

		/// <summary>
		/// Calculates Apparent Temperature in Celcius
		/// </summary>
		/// <remarks>
		/// See http://www.bom.gov.au/info/thermal_stress/#atapproximation
		/// </remarks>
		/// <param name="tempC">Temp in C</param>
		/// <param name="windspeedMS">Wind speed in m/s</param>
		/// <param name="humidity">Relative humidity</param>
		/// <returns>Apparent temperature in Celcius</returns>
		public static double ApparentTemperature(double tempC, double windspeedMS, int humidity)
		{
			double avp = (humidity/100.0)*6.105*Math.Exp(17.27*tempC/(237.7 + tempC)); // hPa
			//double avp = ActualVapourPressure(tempC, humidity);
			return tempC + (0.33*avp) - (0.7*windspeedMS) - 4.0;
		}

		/// <summary>
		/// Calculates the Feels Like temperature in Celcius
		/// </summary>
		/// <remarks>
		/// Joint Action Group for Temperature Indices (JAG/TI) formula
		/// </remarks>
		/// <param name="tempC">Temp in C</param>
		/// <param name="windSpeedKph">Windspeed in kph</param>
		/// <param name="humidity">Relative humidity</param>
		/// <returns>Feels Like temperture in Celcius</returns>
		public static double FeelsLike(double tempC, double windSpeedKph, int humidity)
		{
			// Cannot use the WindChill function as we need the chill above 10 C
			double chill = windSpeedKph < 4.828 ? tempC : 13.12 + 0.6215 * tempC - 11.37 * Math.Pow(windSpeedKph, 0.16) + 0.3965 * tempC * Math.Pow(windSpeedKph, 0.16);
			double svp = SaturationVapourPressure1980(tempC);   // Saturation Vapour Pressure in hPa
			double avp = (float)humidity / 100.0 * svp / 10.0;             // Actual Vapour Pressure in kPa
			if (windSpeedKph > 72) windSpeedKph = 72;           // Windspeed limited to 20 m/s = 72 km/h
			double apptemp = (1.04 * tempC) + (2 * avp) - (windSpeedKph * 0.1805553) - 2.7;
			double feels;
			if (tempC < 10.0)
			{
				feels = chill;
			}
			else if (tempC > 20.0)
			{
				feels = apptemp;
			}
			else
			{
				// 10-20 C = linear interpolation between chill and apparent
				double A = (tempC - 10) / 10;
				double B = 1 - A;
				feels = (apptemp * A) + (chill * B);
			}
			return feels;
		}



		/// <summary>
		/// Calculates the North American Heat Index
		/// </summary>
		/// <remarks>
		/// Uses the NOAA formula and corrections
		/// see: https://www.wpc.ncep.noaa.gov/html/heatindex_equation.shtml
		/// </remarks>
		/// <param name="tempC">Temp in C</param>
		/// <param name="humidity">Relative humidity</param>
		/// <returns>Heat Index in Celcius</returns>
		public static double HeatIndex(double tempC, int humidity)
		{
			double tempF = CToF(tempC);

			if (tempF < 80)
			{
				return tempC;
			}
			else
			{
				double tempSqrd = tempF*tempF;

				double humSqrd = humidity*humidity;

				var result = FtoC(0 - 42.379 + (2.04901523*tempF) + (10.14333127*humidity) - (0.22475541*tempF*humidity) - (0.00683783*tempSqrd) - (0.05481717*humSqrd) +
					(0.00122874*tempSqrd*humidity) + (0.00085282*tempF*humSqrd) - (0.00000199*tempSqrd*humSqrd));

			  // Rothfusz adjustments
			  if ((humidity < 13) && (tempF >= 80) && (tempF <= 112))
			  {
				result -= ((13 - humidity) / 4.0) * Math.Sqrt((17 - Math.Abs(tempF - 95)) / 17.0);
			  }
			  else if ((humidity > 85) && (tempF >= 80) && (tempF <= 87))
			  {
				result += ((humidity - 85) / 10.0) * ((87 - tempF) / 5.0);
			  }

			  return result;
			}
		}

		/// <summary>
		/// Estimates the Wet Bulb temperature using a polynomial
		/// </summary>
		/// <remarks>
		/// To calculate this accurately we need an iterative process
		/// </remarks>
		/// <param name="tempC">Temp in C</param>
		/// <param name="dewPointC">Dew point in C</param>
		/// <param name="pressureMb">Station pressure in mb/hPa</param>
		/// <returns>Wet bulb temperature in Celcius</returns>
		public static double CalculateWetBulbC(double tempC, double dewPointC, double pressureMb)
		{
			double svpDP = SaturationVapourPressure1980(dewPointC);

			return (((0.00066 * pressureMb) * tempC) + ((4098 * svpDP) / (Sqr(dewPointC + 237.7)) * dewPointC)) / ((0.00066 * pressureMb) + (4098 * svpDP) / (Sqr(dewPointC + 237.7)));
		}

		/// <summary>
		/// Estimates the Wet Bulb temperature using a polynomial
		/// </summary>
		/// <remarks>
		/// To calculate this accurately we need an iterative process
		/// This method assumes a pressure of 1013.25 hPa, and RH in the range 5% - 99%
		/// It is an emprical approximation generated using a best fit function
		/// See: https://journals.ametsoc.org/jamc/article/50/11/2267/13533/Wet-Bulb-Temperature-from-Relative-Humidity-and
		/// and Strull: https://www.eoas.ubc.ca/books/Practical_Meteorology/prmet102/Ch04-watervapor-v102b.pdf
		/// Errors have multiple relative maxima and minima of order from −1.0° to +0.6°C
		/// </remarks>
		/// <param name="tempC">Temp in C</param>
		/// <param name="humidity">Relative Humidty in %</param>
		/// <param name="PressureMB">Station pressure in mb/hPa</param>
		/// <returns>Wet bulb temperature in Celcius</returns>
		public static double CalculateWetBulbC2(double tempC, int humidity)
		{
			if (humidity == 100)
				return tempC;

			return tempC * Math.Atan(0.151977 * Math.Sqrt(humidity + 8.313659)) + Math.Atan(tempC + humidity) - Math.Atan(humidity - 1.676331) + 0.00391838 * Math.Pow(humidity, 3/2) * Math.Atan(0.023101 * humidity) - 4.686035;
		}


		/// <summary>
		/// Calcuates the Wet Bulb temperature iteratively
		/// </summary>
		/// <remarks>
		/// To calculate this accurately we need an iterative process
		/// See: https://www.researchgate.net/publication/303156836_Simple_Iterative_Approach_to_Calculate_Wet-Bulb_Temperature_for_Estimating_Evaporative_Cooling_Efficiency
		/// </remarks>
		/// <param name="tempC">Temp in C</param>
		/// <param name="humidity">Relative Humidty in %</param>
		/// <param name="pressureHpa">Station pressure in mb/hPa</param>
		/// <returns>Wet bulb temperature in Celcius</returns>
		public static double CalculateWetBulbCIterative(double tempC, int humidity, double pressureHpa)
		{
			if (humidity == 100)
				return tempC;

			var e = ActualVapourPressure2008(tempC, humidity);
			double Tw;
			double Tw1 = tempC;

			do {
				Tw = Tw1;
				var Ewg = SaturationVapourPressure1980(Tw);
				var eg = Ewg - pressureHpa * (tempC - Tw) * 0.00066 * (1 + (0.00115 * Tw));
				var Ed = e - eg;
				Tw1 = Tw + Ed / 5 * 2;
			} while (Math.Abs(Tw - Tw1) > 0.1);

			return Tw1;
		}


		private static double Sqr(double num)
		{
			return num*num;
		}

		/// <summary>
		/// Calculates the Dew Point in Celcius
		/// </summary>
		/// <remarks>
		/// Uses the Davis formula, described as "an approximation of the Goff & Gratch equation"
		/// It is functionally equivalent to the Magnus formula using the Sonntag 1990 values for constants
		/// </remarks>
		/// <param name="tempC">Temp in C</param>
		/// <param name="humidity">Relative humidity</param>
		/// <returns>Dew Point temperature in Celcius</returns>
		public static double DewPoint(double tempC, double humidity)
		{
			if (humidity == 0 || humidity == 100)
				return tempC;

			// Davis algorithm
			double lnVapor = Math.Log(ActualVapourPressure2008(tempC, (int) humidity));
			return ((243.12 * lnVapor) - 440.1) / (19.43 - lnVapor);
		}

		/// <summary>
		/// Calculates the Saturated Vapour Pressure in hPa
		/// </summary>
		/// <remarks>
		/// Bolton(1980) or
		/// August–Roche–Magnus?
		/// </remarks>
		/// <param name="tempC">Temp in C</param>
		/// <returns>SVP in hPa</returns>
		public static double SaturationVapourPressure1980(double tempC)
		{
			return 6.112 * Math.Exp(17.67 * tempC / (tempC + 243.5));
		}

		/// <summary>
		/// Calculates the Saturated Vapour Pressure in hPa
		/// </summary>
		/// <remarks>
		/// WMO - CIMO Guide - 2008
		/// Sonntag 1990
		/// </remarks>
		/// <param name="tempC">Temp in C</param>
		/// <returns>SVP in hPa</returns>
		public static double SaturationVapourPressure2008(double tempC)
		{
			return 6.112*Math.Exp((17.62*tempC)/(243.12 + tempC));
		}

		private static double ActualVapourPressure2008(double tempC, int humidity)
		{
			return humidity / 100.0 * SaturationVapourPressure2008(tempC);
		}

		/// <summary>
		/// Calculates the net long wave radiation
		/// http://www.fao.org/3/x0490e/x0490e07.htm#radiation - equation (39)
		/// </summary>
		/// <param name="tempMinC">Minimum temperature over the period</param>
		/// <param name="tempMaxC">Maximum temperature over the period</param>
		/// <param name="vapPresskPa">Vapour pressure in kPa</param>
		/// <param name="radMeasured">Measured solar radiation (same units as radClearSky)</param>
		/// <param name="radClearSky">Calculated clear sky radiation (same units as radMeasured)</param>
		/// <returns>Returns the long wave (back) radiation in MJ/m^2/hour</returns>
		private static double LongwaveRadiation(double tempAvgC, double vapPresskPa, double radMeasured, double radClearSky)
		{
			var avgK = tempAvgC + 273.16;

			// Stefan-Boltzman constant in MJ/K^4/m^2/day
			var sigma = 4.903e-09;
			// because we are using 1 hour, it needs scaling...
			sigma /= 24.0;

			// Use the ratio of measured to expected radiation as a measure of cloudiness, but only if it's daylight
			double cloudFactor;
			if (radClearSky > 0)
			{
				cloudFactor = radMeasured / radClearSky;
				if (cloudFactor > 1)
					cloudFactor = 1;
			}
			else
			{
				// It's night!
				// As the night time ET is low compared to day, let's just assume 50% cloud cover
				cloudFactor = 0.5;
			}

			// Calculate the long wave (back) radiation in MJ/m^2/hour.
			var part1 = sigma * Math.Pow(avgK, 4);
			var part2 = (0.34 - 0.14 * Math.Sqrt(vapPresskPa));
			var part3 = (1.35 * cloudFactor - 0.35);

			return part1 * part2 * part3;
		}

		/// <summary>
		///  Evapotranspiration
		///  The calculation of ETo by means of the FAO Penman-Monteith equation
		///  Using grass as the reference crop
		///  Uses the "hourly time step" equations - http://www.fao.org/3/x0490e/x0490e08.htm#calculation%20procedure
		///  With acknowledgement to the equivalent WeeWX formula - https://github.com/weewx/weewx/blob/master/bin/weewx/wxformulas.py
		/// </summary>
		/// <param name="tempMinC"></param>
		/// <param name="tempMaxC"></param>
		/// <param name="humMin"></param>
		/// <param name="humMax"></param>
		/// <param name="radMean">Mean solar irradiation over the period in W/m^2</param>
		/// <param name="windAvgMs">Mean wind speed over the period in m/s</param>
		/// <param name="latitude"></param>
		/// <param name="longitude"></param>
		/// <param name="altitudeM">Station altitude in metres</param>
		/// <param name="pressMinKpa"></param>
		/// <param name="pressMaxkpa"></param>
		/// <param name="date">Date/time of the end of the period</param>
		/// <returns>Evapotranspiration in mm</returns>
		public static double Evapotranspiration(
			double tempAvgC, int humAvg,
			double radMean, double maxRadMean, double windAvgMs,
			double pressKpa)
		{
			var windHeightM = 2.0; // height of wind sensor in metres, we assume 2m for a typical amateur station

			// Use grass as the reference crop
			var albedo = 0.23;

			// Adjust avg wind speed to a height of 2m (equation 47)
			var u2 = 4.87 * windAvgMs / Math.Log(67.8 * windHeightM - 5.42);

			// Calculate the psychrometric constant in kPa/C (equation 8)
			var gamma = 0.665e-03 * pressKpa;

			// Calculate mean saturation vapour pressure, converting from hPa to kPa (equation 12)
			var e0T = 0.6108 * Math.Exp(17.27 * tempAvgC / (tempAvgC + 237.3));

			// Calculate the slope of the saturation vapour pressure curve in kPa/C (equation 13)
			var delta = 4098.0 * (0.6108 * Math.Exp(17.27 * tempAvgC / (tempAvgC + 237.3))) / ((tempAvgC + 237.3) * (tempAvgC + 237.3));

			// Calculate actual vapour pressure from relative humidity (equation 17)
			var ea = e0T * humAvg / 100;

			// Convert solar radiation from W/m^2 to MJ/m^2/h
			var Rs = radMean * 0.0036;

			// Net short-wave (measured) radiation in MJ/m^2/h (equation 38)
			var Rns = (1 - albedo) * Rs;

			// Take the mean solar max and convert from W/m^2 to MJ/m^2/h
			var Rso = maxRadMean * 0.0036;

			// Long-wave (back) radiation. (equation 39 modified to per hour)
			var Rnl = LongwaveRadiation(tempAvgC, ea, Rs, Rso);

			// Calculate net radiation at the surface in MJ/m^2/h (equation 40)
			var Rn = Rns - Rnl;

			// Calculate the soil heat flux. (see section "For hourly or shorter periods" in http://www.fao.org/docrep/x0490e/x0490e07.htm#radiation
			var Ghr = (Rs > 0 ? 0.1 : 0.5) * Rn;

			// Result is in mm/h (equation 53)
			// But as we have fixed a 1 hour period, then the effective result is just mm
			var et0 = (0.408 * delta * (Rn - Ghr) + gamma * 37 / (tempAvgC + 273) * u2 * (e0T - ea)) / (delta + gamma * (1 + 0.34 * u2));

			if (et0 < 0) et0 = 0;

			return et0;
		}

		/// <summary>
		/// Calculates the Canadian Humidex
		/// </summary>
		/// <remarks>
		/// WMO - CIMO Guide - 2008
		/// Sonntag 1990
		/// </remarks>
		/// <param name="tempC">Temp in C</param>
		/// <param name="humidity">Relative humidity</param>
		/// <returns>Humidex - dimensionless</returns>
		public static double Humidex(double tempC, int humidity)
		{
			if (tempC < 10)
				return tempC;
			else
				return tempC + ((5.0 / 9.0) * (ActualVapourPressure2008(tempC, humidity) - 10.0));
		}

		public static double CToF(double tempC)
		{
			return ((tempC*9.0)/5.0) + 32;
		}

		public static double FtoC(double tempF)
		{
			return ((tempF - 32)*5.0)/9.0;
		}
	}
}
