using System;
using System.Collections.Generic;
using System.Linq;


namespace CreateMissing
{
	internal class WeatherDataDict : Dictionary<DateTime, WeatherData>
	{
		public WeatherData GetAverages(DateTime date)
		{
			// Get the average values for the specified hour of the day
			var dataForHour = this
				.Where(kvp => kvp.Key.Date == date.Date && kvp.Key.Hour == date.Hour)
				.Select(kvp => kvp.Value)
				.ToList();

			if (dataForHour.Any())
			{
				return new WeatherData
				{
					Temp = dataForHour.Average(d => d.Temp ?? 0),
					Humidity = (int?) dataForHour.Average(d => d.Humidity ?? 0),
					Pressure = dataForHour.Average(d => d.Pressure ?? 0),
					SolarRad = (int?) dataForHour.Average(d => d.SolarRad ?? 0),
					SolarMax = (int?) dataForHour.Average(d => d.SolarMax ?? 0),
					WindSpeed = dataForHour.Average(d => d.WindSpeed ?? 0)
				};
			}
			else
			{
				return null;
			}
		}
	}
}
