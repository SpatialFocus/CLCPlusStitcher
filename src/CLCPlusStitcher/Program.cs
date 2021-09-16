// <copyright file="Program.cs" company="Spatial Focus GmbH">
// Copyright (c) Spatial Focus GmbH. All rights reserved.
// </copyright>

namespace CLCPlusStitcher
{
	using System;
	using System.Collections.Generic;
	using System.Diagnostics;
	using System.Linq;
	using System.Threading.Tasks;
	using ClcPlusRetransformer.Core;
	using ClcPlusRetransformer.Core.Processors;
	using ClcPlusRetransformer.Core.Processors.Extension;
	using CLCPlusStitcher.Processors;
	using Microsoft.Extensions.Configuration;
	using Microsoft.Extensions.DependencyInjection;
	using Microsoft.Extensions.Logging;
	using NetTopologySuite.Geometries;
	using Serilog;

	public class Program
	{
		public static async Task Main()
		{
			IConfigurationBuilder builder = new ConfigurationBuilder().AddJsonFile("appsettings.json");

			IConfigurationRoot config = builder.Build();

			ServiceCollection serviceCollection = new();
			serviceCollection.AddLogging(loggingBuilder =>
				loggingBuilder.AddSerilog(new LoggerConfiguration().ReadFrom.Configuration(config.GetSection("Logging")).CreateLogger()));

			serviceCollection.AddTransient(typeof(Processor<>));
			serviceCollection.AddTransient(typeof(ChainedProcessor<,>));
			serviceCollection.AddTransient<ProcessorFactory>();

			ServiceProvider provider = serviceCollection.BuildServiceProvider();

			ILogger<Program> logger = provider.GetRequiredService<ILogger<Program>>();

			logger.LogInformation("Workflow started");
			Stopwatch stopwatch = Stopwatch.StartNew();

			PrecisionModel precisionModel = new(int.Parse(config["Precision"]));

			// Load features and intersect with individual AOIs
			IConfigurationSection pu1Section = config.GetSection("PU1");
			Input pu1Input = pu1Section.GetSection("Input").Get<Input>();

			MultiPolygon pu1Aoi = new(provider.LoadFromFile<Polygon>(pu1Section.GetSection("AOI").Get<Input>(), precisionModel)
				.Buffer(0)
				.Execute()
				.ToArray());
			IProcessor<Polygon> pu1Processor = provider.LoadFromFile<Polygon>(pu1Input, precisionModel,
					provider.GetRequiredService<ILogger<Processor>>())
				.Buffer(0)
				.Intersect(pu1Aoi);

			IConfigurationSection pu2Section = config.GetSection("PU2");
			Input pu2Input = pu2Section.GetSection("Input").Get<Input>();

			MultiPolygon pu2Aoi = new(provider.LoadFromFile<Polygon>(pu2Section.GetSection("AOI").Get<Input>(), precisionModel)
				.Buffer(0)
				.Execute()
				.ToArray());
			IProcessor<Polygon> pu2Processor = provider.LoadFromFile<Polygon>(pu2Input, precisionModel,
					provider.GetRequiredService<ILogger<Processor>>())
				.Buffer(0)
				.Intersect(pu2Aoi);

			// Find duplicates and remove from PU2
			// TODO: Maybe use a buffered common border?
			////Geometry commonBorder = pu1Aoi.Intersection(pu2Aoi);
			Task<ICollection<Polygon>>? task5 = Task.Run(() => pu1Processor.Execute());
			Task<ICollection<Polygon>>? task6 = Task.Run(() => pu2Processor.Execute());

			await Task.WhenAll(task5, task6);

			ICollection<Polygon> pu1Intersects = task5.Result;
			ICollection<Polygon> pu2Intersects = task6.Result;

			Task<ICollection<Polygon>>? task1 = Task.Run(() => pu1Processor.Overlap(pu1Aoi).Execute());
			Task<ICollection<Polygon>>? task2 = Task.Run(() => pu2Processor.Overlap(pu2Aoi).Execute());

			await Task.WhenAll(task1, task2);

			ICollection<Polygon> pu1BorderPolygons = task1.Result;
			ICollection<Polygon> pu2BorderPolygons = task2.Result;

			int i = 0;

			foreach (var pu1BorderPolygon in pu1BorderPolygons)
			{
				Polygon? polygon = pu2BorderPolygons.FirstOrDefault(x => x.EqualsTopologically(pu1BorderPolygon));

				if (polygon != null)
				{
					pu2Intersects.Remove(polygon);
					i++;
				}
			}

			logger.LogInformation($"Remove duplicates: {i} polygons removed from PU2");

			// Export output PU1 and PU2
			Task? task3 = Task.Run(() => pu1Intersects.Save(pu1Section.GetSection("Output").Get<Input>().FileName, precisionModel));
			Task? task4 = Task.Run(() => pu2Intersects.Save(pu2Section.GetSection("Output").Get<Input>().FileName, precisionModel));

			await Task.WhenAll(task3, task4);

			logger.LogInformation("Saved PU outputs");

			stopwatch.Stop();
			logger.LogInformation("Workflow finished in {Time}ms", stopwatch.ElapsedMilliseconds);

			if (bool.Parse(config["WaitForUserInputAfterCompletion"]))
			{
				Console.WriteLine("=== PRESS A KEY TO PROCEED ===");
				Console.ReadKey();
			}
		}
	}
}